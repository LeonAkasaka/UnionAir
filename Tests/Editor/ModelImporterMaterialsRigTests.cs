using System;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    internal sealed class ModelImporterMaterialsRigTests
    {
        private const string TestDirectory = "Assets/UnionAirModelImporterMaterialsRigTests";
        private const string ModelPath = TestDirectory + "/model.obj";
        private const string MaterialOnePath = TestDirectory + "/one.mat";
        private const string MaterialTwoPath = TestDirectory + "/two.mat";
        private const string MaterialContainerPath = TestDirectory + "/multiple.mat";
        private const string AvatarOnePath = TestDirectory + "/one.avatar.asset";
        private const string AvatarTwoPath = TestDirectory + "/two.avatar.asset";

        [Test]
        public void Parser_ReadsMaterialsRemapsAndRig()
        {
            const string json =
                "{\"schemaVersion\":1,\"materials\":{\"importMode\":\"ImportStandard\"," +
                "\"location\":\"External\",\"naming\":\"BasedOnMaterialName\",\"search\":\"Local\"}," +
                "\"materialRemaps\":[{\"source\":{\"type\":\"UnityEngine.Material\"," +
                "\"name\":\"Body\"},\"target\":null}],\"rig\":{\"animationType\":\"Generic\"," +
                "\"avatarSetup\":\"CreateFromThisModel\",\"optimizeGameObjects\":true," +
                "\"extraExposedTransformPaths\":[\"Root/Hand\"]}}";

            ModelImporterUpdateRequest request;
            string error;
            Assert.IsTrue(ModelImporterUpdateParser.TryParse(json, out request, out error), error);
            Assert.AreEqual(ModelImporterMaterialImportMode.ImportStandard, request.Materials.ImportMode);
            Assert.AreEqual(ModelImporterMaterialLocation.External, request.Materials.Location);
            Assert.AreEqual(1, request.MaterialRemaps.Count);
            Assert.AreEqual("Body", request.MaterialRemaps[0].SourceName);
            Assert.IsNull(request.MaterialRemaps[0].Target);
            Assert.AreEqual(ModelImporterAnimationType.Generic, request.Rig.AnimationType);
            CollectionAssert.AreEqual(new[] { "Root/Hand" }, request.Rig.ExtraExposedTransformPaths);
        }

        [TestCase("{\"schemaVersion\":1,\"materialRemaps\":[]}", "at least one")]
        [TestCase("{\"schemaVersion\":1,\"materialRemaps\":[{\"source\":{\"type\":\"UnityEngine.Texture2D\",\"name\":\"Body\"},\"target\":null}]}", "UnityEngine.Material")]
        [TestCase("{\"schemaVersion\":1,\"materialRemaps\":[{\"source\":{\"type\":\"UnityEngine.Material\",\"name\":\"Body\"},\"target\":null},{\"source\":{\"type\":\"UnityEngine.Material\",\"name\":\"Body\"},\"target\":null}]}", "Duplicate material remap")]
        [TestCase("{\"schemaVersion\":1,\"rig\":{\"extraExposedTransformPaths\":[\"Root/Hand\",\"Root/Hand\"]}}", "Duplicate extra exposed")]
        public void Parser_RejectsInvalidMaterialAndRigShapes(string json, string expected)
        {
            ModelImporterUpdateRequest request;
            string error;
            Assert.IsFalse(ModelImporterUpdateParser.TryParse(json, out request, out error));
            StringAssert.Contains(expected, error);
        }

        [Test]
        public void Capture_DoesNotAliasExtraExposedTransformPaths()
        {
            CreateAssets();
            try
            {
                var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
                Assert.IsNotNull(importer);
                importer.extraExposedTransformPaths = new[] { "Root/Hand" };

                var state = ModelImporterState.Capture(importer);
                state.ExtraExposedTransformPaths[0] = "Changed";

                CollectionAssert.AreEqual(
                    new[] { "Root/Hand" }, importer.extraExposedTransformPaths);
            }
            finally
            {
                DeleteAssets();
            }
        }

        [Test]
        public void Preflight_RejectsWrongMissingAndAmbiguousMaterialTargetsWithoutMutation()
        {
            CreateAssets();
            try
            {
                var guid = AssetDatabase.AssetPathToGUID(ModelPath);
                var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
                Assert.IsNotNull(importer);
                var originalMode = importer.materialImportMode;
                var originalMapCount = importer.GetExternalObjectMap().Count;
                var mainModel = AssetDatabase.LoadMainAssetAtPath(ModelPath);

                AssertPreflightError(
                    guid,
                    RemapJson("Body", ReferenceJson(mainModel, true)),
                    "expected UnityEngine.Material");
                AssertPreflightError(
                    guid,
                    RemapJson("Body", "{\"guid\":\"" +
                        AssetDatabase.AssetPathToGUID(MaterialOnePath) +
                        "\",\"localIdentifier\":\"999999999\"}"),
                    "was not found");
                AssertPreflightError(
                    guid,
                    RemapJson("Body", "{\"guid\":\"" +
                        AssetDatabase.AssetPathToGUID(MaterialContainerPath) + "\"}"),
                    "ambiguous");

                var mixed = new FakeResponse();
                new ModelImporterHandler().HandleUpdate(
                    new FakeRequest("PATCH").WithJsonBody(
                        "{\"schemaVersion\":1,\"materials\":{\"importMode\":\"None\"}," +
                        "\"rig\":{\"animationType\":\"Generic\",\"avatarSetup\":\"CopyFromOther\"}}"),
                    mixed,
                    guid);
                Assert.AreEqual(400, mixed.StatusCode, mixed.Body);
                importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
                Assert.IsNotNull(importer);
                Assert.AreEqual(originalMode, importer.materialImportMode);
                Assert.AreEqual(originalMapCount, importer.GetExternalObjectMap().Count);
            }
            finally
            {
                DeleteAssets();
            }
        }

        [Test]
        public void Handler_AddsReplacesAndRemovesAMaterialRemap()
        {
            CreateAssets();
            try
            {
                var guid = AssetDatabase.AssetPathToGUID(ModelPath);
                var first = AssetDatabase.LoadAssetAtPath<Material>(MaterialOnePath);
                var second = AssetDatabase.LoadAssetAtPath<Material>(MaterialTwoPath);
                var handler = new ModelImporterHandler();

                var added = Update(handler, guid, RemapJson("Body", ReferenceJson(first, true)));
                Assert.AreEqual(200, added.StatusCode, added.Body);
                StringAssert.Contains("\"reimported\":true", added.Body);
                AssertRemap("Body", first);

                var failing = new ModelImporterHandler(
                    value => throw new InvalidOperationException("Forced reimport failure."));
                var failed = Update(failing, guid, RemapJson("Body", ReferenceJson(second, true)));
                Assert.AreEqual(500, failed.StatusCode, failed.Body);
                StringAssert.Contains("\"restored\":true", failed.Body);
                AssertRemap("Body", first);

                var replaced = Update(handler, guid, RemapJson("Body", ReferenceJson(second, true)));
                Assert.AreEqual(200, replaced.StatusCode, replaced.Body);
                AssertRemap("Body", second);

                var removed = Update(handler, guid, RemapJson("Body", "null"));
                Assert.AreEqual(200, removed.StatusCode, removed.Body);
                var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
                Assert.IsNotNull(importer);
                Assert.IsFalse(importer.GetExternalObjectMap().Keys.Any(
                    key => key.type == typeof(Material) && key.name == "Body"));
            }
            finally
            {
                DeleteAssets();
            }
        }

        [Test]
        public void Handler_AssignsAndRestoresASourceAvatar()
        {
            CreateAssets();
            try
            {
                var guid = AssetDatabase.AssetPathToGUID(ModelPath);
                var first = AssetDatabase.LoadAssetAtPath<Avatar>(AvatarOnePath);
                var second = AssetDatabase.LoadAssetAtPath<Avatar>(AvatarTwoPath);
                Assert.IsTrue(first.isValid);
                Assert.IsFalse(first.isHuman);
                var handler = new ModelImporterHandler();
                var assigned = Update(
                    handler,
                    guid,
                    "{\"schemaVersion\":1,\"rig\":{\"animationType\":\"Generic\"," +
                    "\"avatarSetup\":\"CopyFromOther\",\"sourceAvatar\":" +
                    ReferenceJson(first, true) + "}}" );
                Assert.AreEqual(200, assigned.StatusCode, assigned.Body);
                var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
                Assert.IsNotNull(importer);
                Assert.IsTrue(ModelImporterObjectIdentity.Same(first, importer.sourceAvatar));

                var incompatible = new FakeResponse();
                handler.HandlePreflight(
                    new FakeRequest("POST").WithJsonBody(
                        "{\"schemaVersion\":1,\"rig\":{\"avatarSetup\":\"CreateFromThisModel\"}}"),
                    incompatible,
                    guid);
                Assert.AreEqual(400, incompatible.StatusCode, incompatible.Body);
                StringAssert.Contains("sourceAvatar is allowed only", incompatible.Body);

                var failing = new ModelImporterHandler(
                    value => throw new InvalidOperationException("Forced reimport failure."));
                var failed = Update(
                    failing,
                    guid,
                    "{\"schemaVersion\":1,\"rig\":{\"sourceAvatar\":" +
                    ReferenceJson(second, true) + "}}" );
                Assert.AreEqual(500, failed.StatusCode, failed.Body);
                StringAssert.Contains("\"restored\":true", failed.Body);
                importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
                Assert.IsNotNull(importer);
                Assert.IsTrue(ModelImporterObjectIdentity.Same(first, importer.sourceAvatar));
            }
            finally
            {
                DeleteAssets();
            }
        }

        private static FakeResponse Update(ModelImporterHandler handler, string guid, string json)
        {
            var response = new FakeResponse();
            handler.HandleUpdate(new FakeRequest("PATCH").WithJsonBody(json), response, guid);
            return response;
        }

        private static void AssertPreflightError(string guid, string json, string expected)
        {
            var response = new FakeResponse();
            new ModelImporterHandler().HandlePreflight(
                new FakeRequest("POST").WithJsonBody(json), response, guid);
            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains(expected, response.Body);
        }

        private static void AssertRemap(string sourceName, Material expected)
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.IsNotNull(importer);
            var match = importer.GetExternalObjectMap().Single(
                entry => entry.Key.type == typeof(Material) && entry.Key.name == sourceName);
            Assert.IsTrue(ModelImporterObjectIdentity.Same(expected, match.Value));
        }

        private static string RemapJson(string sourceName, string target)
        {
            return "{\"schemaVersion\":1,\"materialRemaps\":[{\"source\":{" +
                   "\"type\":\"UnityEngine.Material\",\"name\":\"" + sourceName +
                   "\"},\"target\":" + target + "}]}";
        }

        private static string ReferenceJson(UnityEngine.Object asset, bool includeLocalIdentifier)
        {
            var path = AssetDatabase.GetAssetPath(asset);
            var json = "{\"guid\":\"" + AssetDatabase.AssetPathToGUID(path) + "\"";
            if (includeLocalIdentifier)
                json += ",\"localIdentifier\":\"" +
                        GlobalObjectId.GetGlobalObjectIdSlow(asset).targetObjectId.ToString(
                            CultureInfo.InvariantCulture) + "\"";
            return json + "}";
        }

        private static void CreateAssets()
        {
            if (!AssetDatabase.IsValidFolder(TestDirectory))
                AssetDatabase.CreateFolder("Assets", "UnionAirModelImporterMaterialsRigTests");
            File.WriteAllText(
                ModelPath,
                "o Model\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl Body\nf 1 2 3\n");

            var shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            Assert.IsNotNull(shader);
            AssetDatabase.CreateAsset(new Material(shader) { name = "One" }, MaterialOnePath);
            AssetDatabase.CreateAsset(new Material(shader) { name = "Two" }, MaterialTwoPath);
            var main = new Material(shader) { name = "Main" };
            AssetDatabase.CreateAsset(main, MaterialContainerPath);
            AssetDatabase.AddObjectToAsset(new Material(shader) { name = "Nested" }, MaterialContainerPath);
            CreateGenericAvatar(AvatarOnePath, "AvatarOne");
            CreateGenericAvatar(AvatarTwoPath, "AvatarTwo");
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(MaterialContainerPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void CreateGenericAvatar(string path, string name)
        {
            var root = new GameObject("Root");
            var bone = new GameObject("Bone");
            bone.transform.SetParent(root.transform);
            var avatar = AvatarBuilder.BuildGenericAvatar(root, string.Empty);
            avatar.name = name;
            AssetDatabase.CreateAsset(avatar, path);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static void DeleteAssets()
        {
            AssetDatabase.DeleteAsset(TestDirectory);
            AssetDatabase.Refresh();
        }
    }
}

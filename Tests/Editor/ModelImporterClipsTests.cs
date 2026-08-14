using System;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    internal sealed class ModelImporterClipsTests
    {
        private const string FixturePath =
            "Packages/com.leonakasaka.unionair/Tests/Editor/Fixtures/AnimatedTriangle.dae";
        private const string TestDirectory = "Assets/UnionAirModelImporterClipsTests";
        private const string ModelPath = TestDirectory + "/animated.dae";
        private const string MaskPath = TestDirectory + "/mask.mask";

        [Test]
        public void Parser_ReadsAFullClipDefinition()
        {
            const string json =
                "{\"schemaVersion\":1,\"clips\":[{\"takeName\":\"Take 001\",\"name\":\"Move\"," +
                "\"firstFrame\":0,\"lastFrame\":30,\"wrapMode\":\"Loop\",\"loop\":true," +
                "\"loopTime\":true,\"loopPose\":true,\"mirror\":false,\"lockRootRotation\":true," +
                "\"keepOriginalOrientation\":true,\"rotationOffset\":5,\"lockRootHeightY\":true," +
                "\"keepOriginalPositionY\":true,\"heightFromFeet\":false,\"heightOffset\":0.1," +
                "\"lockRootPositionXZ\":true,\"keepOriginalPositionXZ\":true,\"cycleOffset\":0.25," +
                "\"hasAdditiveReferencePose\":true,\"additiveReferencePoseFrame\":10," +
                "\"maskType\":\"None\",\"maskSource\":null,\"events\":[{\"time\":0.5," +
                "\"functionName\":\"Footstep\",\"stringParameter\":\"left\"," +
                "\"floatParameter\":1.5,\"intParameter\":2,\"objectReferenceParameter\":null," +
                "\"messageOptions\":\"RequireReceiver\"}]}]}";

            ModelImporterUpdateRequest request;
            string error;
            Assert.IsTrue(ModelImporterUpdateParser.TryParse(json, out request, out error), error);
            Assert.AreEqual(1, request.Clips.Count);
            Assert.AreEqual("Take 001", request.Clips[0].TakeName);
            Assert.AreEqual("Move", request.Clips[0].Name);
            Assert.AreEqual(WrapMode.Loop, request.Clips[0].WrapMode);
            Assert.AreEqual(ClipAnimationMaskType.None, request.Clips[0].MaskType);
            Assert.AreEqual(1, request.Clips[0].Events.Length);
            Assert.AreEqual("Footstep", request.Clips[0].Events[0].functionName);
        }

        [TestCase("{\"schemaVersion\":1,\"clips\":[{\"takeName\":\"Take 001\",\"name\":\"A\",\"firstFrame\":0,\"lastFrame\":30,\"unknown\":true}]}", "Unknown field 'unknown'")]
        [TestCase("{\"schemaVersion\":1,\"clips\":[{\"takeName\":\"Take 001\",\"name\":\"A\",\"firstFrame\":0,\"lastFrame\":30},{\"takeName\":\"Take 001\",\"name\":\"A\",\"firstFrame\":0,\"lastFrame\":30}]}", "Duplicate clip name")]
        [TestCase("{\"schemaVersion\":1,\"clips\":[{\"takeName\":\"Take 001\",\"name\":\"A\",\"firstFrame\":0,\"lastFrame\":30,\"events\":[{\"time\":0.5,\"functionName\":\"Run\",\"unknown\":1}]}]}", "Unknown field 'unknown'")]
        [TestCase("{\"schemaVersion\":1,\"clips\":[{\"takeName\":\"Take 001\",\"name\":\"A\",\"firstFrame\":0,\"lastFrame\":30,\"events\":[{\"time\":-1,\"functionName\":\"Run\"}]}]}", "must be non-negative")]
        public void Parser_RejectsInvalidClipShapes(string json, string expected)
        {
            ModelImporterUpdateRequest request;
            string error;
            Assert.IsFalse(ModelImporterUpdateParser.TryParse(json, out request, out error));
            StringAssert.Contains(expected, error);
        }

        [Test]
        public void State_UsesDefaultDefinitionsWhenStoredArrayIsEmpty()
        {
            var state = new ModelImporterState
            {
                StoredClipAnimations = new ModelImporterClipAnimation[0],
                DefaultClipAnimations = new[] { CreateDefaultClip() }
            };

            Assert.IsTrue(ModelImporterClipsState.DerivedFromDefaults(state));
            Assert.AreEqual(1, ModelImporterClipsState.Effective(state).Length);
            Assert.AreEqual("Take 001", ModelImporterClipsState.Effective(state)[0].takeName);
        }

        [TestCase("{\"schemaVersion\":1,\"clips\":[{\"takeName\":\"Missing\",\"name\":\"A\",\"firstFrame\":0,\"lastFrame\":30}]}", "does not exist")]
        [TestCase("{\"schemaVersion\":1,\"clips\":[{\"takeName\":\"Take 001\",\"name\":\"A\",\"firstFrame\":31,\"lastFrame\":1}]}", "firstFrame <= lastFrame")]
        [TestCase("{\"schemaVersion\":1,\"clips\":[{\"takeName\":\"Take 001\",\"name\":\"A\",\"firstFrame\":0,\"lastFrame\":31}]}", "must stay within")]
        public void Prepare_RejectsMissingTakesAndInvalidRanges(string json, string expected)
        {
            ModelImporterUpdateRequest request;
            string error;
            Assert.IsTrue(ModelImporterUpdateParser.TryParse(json, out request, out error), error);
            var state = new ModelImporterState
            {
                StoredClipAnimations = new ModelImporterClipAnimation[0],
                DefaultClipAnimations = new[] { CreateDefaultClip() }
            };
            Assert.IsFalse(ModelImporterClipsParser.TryPrepare(request, state, out error));
            StringAssert.Contains(expected, error);
        }

        [Test]
        public void Handler_ReplacesPersistsAndRestoresImportedClipDefinitions()
        {
            CreateAssets();
            try
            {
                var guid = AssetDatabase.AssetPathToGUID(ModelPath);
                var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
                Assert.IsNotNull(importer);
                Assert.AreEqual(0, importer.clipAnimations.Length);
                Assert.AreEqual(1, importer.defaultClipAnimations.Length);
                var handler = new ModelImporterHandler();

                var read = new FakeResponse();
                handler.HandleGet(read, guid);
                Assert.AreEqual(200, read.StatusCode, read.Body);
                StringAssert.Contains("\"derivedFromDefaults\":true", read.Body);
                StringAssert.Contains("\"clips.curves\":false", read.Body);
                StringAssert.DoesNotContain("__preview__", read.Body);

                var invalid = new FakeResponse();
                handler.HandleUpdate(
                    new FakeRequest("PATCH").WithJsonBody(
                        "{\"schemaVersion\":1,\"clips\":[{\"takeName\":\"Missing\"," +
                        "\"name\":\"Bad\",\"firstFrame\":0,\"lastFrame\":30}]}"),
                    invalid,
                    guid);
                Assert.AreEqual(400, invalid.StatusCode, invalid.Body);
                importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
                Assert.AreEqual(0, importer.clipAnimations.Length);

                var body = SuccessfulBody("Move");
                var updated = new FakeResponse();
                handler.HandleUpdate(new FakeRequest("PATCH").WithJsonBody(body), updated, guid);
                Assert.AreEqual(200, updated.StatusCode, updated.Body);
                StringAssert.Contains("\"changedFields\":[\"clips\"]", updated.Body);
                StringAssert.Contains("\"derivedFromDefaults\":false", updated.Body);
                StringAssert.Contains("\"localIdentifier\":\"", updated.Body);
                StringAssert.DoesNotContain("__preview__", updated.Body);
                importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
                Assert.AreEqual(1, importer.clipAnimations.Length);
                Assert.AreEqual("Move", importer.clipAnimations[0].name);
                Assert.IsTrue(importer.clipAnimations[0].loopTime);
                Assert.IsTrue(importer.clipAnimations[0].loopPose);
                Assert.AreEqual(0.25f, importer.clipAnimations[0].cycleOffset);
                Assert.AreEqual(1, importer.clipAnimations[0].events.Length);
                Assert.AreEqual("Footstep", importer.clipAnimations[0].events[0].functionName);
                Assert.IsTrue(ModelImporterObjectIdentity.Same(
                    AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath),
                    importer.clipAnimations[0].maskSource));

                var unchanged = new FakeResponse();
                handler.HandleUpdate(new FakeRequest("PATCH").WithJsonBody(body), unchanged, guid);
                Assert.AreEqual(200, unchanged.StatusCode, unchanged.Body);
                StringAssert.Contains("\"reimported\":false", unchanged.Body);

                var failing = new ModelImporterHandler(
                    value => throw new InvalidOperationException("Forced reimport failure."));
                var failed = new FakeResponse();
                failing.HandleUpdate(
                    new FakeRequest("PATCH").WithJsonBody(SuccessfulBody("Other")), failed, guid);
                Assert.AreEqual(500, failed.StatusCode, failed.Body);
                StringAssert.Contains("\"restored\":true", failed.Body);
                importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
                Assert.AreEqual("Move", importer.clipAnimations[0].name);
            }
            finally
            {
                DeleteAssets();
            }
        }

        [Test]
        public void Handler_RejectsCoupledClipSettingsBeforeMutation()
        {
            CreateAssets();
            try
            {
                var guid = AssetDatabase.AssetPathToGUID(ModelPath);
                var handler = new ModelImporterHandler();
                var response = new FakeResponse();
                handler.HandleUpdate(
                    new FakeRequest("PATCH").WithJsonBody(
                        "{\"schemaVersion\":1,\"clips\":[{\"takeName\":\"Take 001\"," +
                        "\"name\":\"Bad\",\"firstFrame\":0,\"lastFrame\":30," +
                        "\"loopTime\":false,\"loopPose\":true,\"maskType\":\"CopyFromOther\"}]}"),
                    response,
                    guid);

                Assert.AreEqual(400, response.StatusCode, response.Body);
                var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
                Assert.IsNotNull(importer);
                Assert.AreEqual(0, importer.clipAnimations.Length);
            }
            finally
            {
                DeleteAssets();
            }
        }

        private static ModelImporterClipAnimation CreateDefaultClip()
        {
            return new ModelImporterClipAnimation
            {
                takeName = "Take 001",
                name = "Take 001",
                firstFrame = 0,
                lastFrame = 30,
                maskType = ClipAnimationMaskType.None,
                events = new AnimationEvent[0],
                curves = new ClipAnimationInfoCurve[0]
            };
        }

        private static string SuccessfulBody(string name)
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            var maskReference = ReferenceJson(mask);
            return "{\"schemaVersion\":1,\"clips\":[{\"takeName\":\"Take 001\",\"name\":\"" +
                   name + "\",\"firstFrame\":0,\"lastFrame\":30,\"wrapMode\":\"Loop\"," +
                   "\"loop\":true,\"loopTime\":true,\"loopPose\":true,\"mirror\":false," +
                   "\"lockRootRotation\":true,\"keepOriginalOrientation\":true," +
                   "\"rotationOffset\":5,\"lockRootHeightY\":true,\"keepOriginalPositionY\":true," +
                   "\"heightFromFeet\":false,\"heightOffset\":0.1,\"lockRootPositionXZ\":true," +
                   "\"keepOriginalPositionXZ\":true,\"cycleOffset\":0.25," +
                   "\"hasAdditiveReferencePose\":true,\"additiveReferencePoseFrame\":10," +
                   "\"maskType\":\"CopyFromOther\",\"maskSource\":" + maskReference +
                   ",\"events\":[{\"time\":0.5,\"functionName\":\"Footstep\"," +
                   "\"objectReferenceParameter\":" + maskReference +
                   ",\"messageOptions\":\"RequireReceiver\"}]}]}";
        }

        private static string ReferenceJson(UnityEngine.Object asset)
        {
            var path = AssetDatabase.GetAssetPath(asset);
            return "{\"guid\":\"" + AssetDatabase.AssetPathToGUID(path) +
                   "\",\"localIdentifier\":\"" +
                   GlobalObjectId.GetGlobalObjectIdSlow(asset).targetObjectId.ToString(
                       CultureInfo.InvariantCulture) + "\"}";
        }

        private static void CreateAssets()
        {
            if (!AssetDatabase.IsValidFolder(TestDirectory))
                AssetDatabase.CreateFolder("Assets", "UnionAirModelImporterClipsTests");
            File.WriteAllText(ModelPath, File.ReadAllText(FixturePath));
            var mask = new AvatarMask { name = "TestMask" };
            var root = new GameObject("MaskRoot");
            mask.AddTransformPath(root.transform, true);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.CreateAsset(mask, MaskPath);
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void DeleteAssets()
        {
            AssetDatabase.DeleteAsset(TestDirectory);
            AssetDatabase.Refresh();
        }
    }
}

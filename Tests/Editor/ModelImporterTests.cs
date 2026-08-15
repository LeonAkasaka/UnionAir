using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    internal sealed class ModelImporterTests
    {
        private const string TestDirectory = "Assets/UnionAirModelImporterTests";
        private const string TestAssetPath = TestDirectory + "/triangle.obj";

        [Test]
        public void Parser_ReadsVersionedCoreSettings()
        {
            const string json =
                "{\"schemaVersion\":1,\"model\":{\"globalScale\":2.5,\"isReadable\":true}," +
                "\"mesh\":{\"compression\":\"Medium\",\"maxBonesPerVertex\":4}," +
                "\"normals\":{\"import\":\"Calculate\"},\"tangents\":{\"import\":\"CalculateMikk\"}}";

            ModelImporterUpdateRequest request;
            string error;
            Assert.IsTrue(ModelImporterUpdateParser.TryParse(json, out request, out error), error);
            Assert.AreEqual(2.5f, request.Model.GlobalScale);
            Assert.IsTrue(request.Model.IsReadable);
            Assert.AreEqual(ModelImporterMeshCompression.Medium, request.Mesh.Compression);
            Assert.AreEqual(4, request.Mesh.MaxBonesPerVertex);
            Assert.AreEqual(ModelImporterNormals.Calculate, request.Normals.Import);
            Assert.AreEqual(ModelImporterTangents.CalculateMikk, request.Tangents.Import);
        }

        [Test]
        public void Apply_TreatsAGetFormattedFloatAsUnchanged()
        {
            const float stored = 1.2345678f;
            const float echoed = 1.234568f;
            Assert.AreNotEqual(stored, echoed);
            Assert.AreEqual(RestResponse.FormatFloat(stored), RestResponse.FormatFloat(echoed));

            var state = new ModelImporterState { GlobalScale = stored };
            var request = new ModelImporterUpdateRequest
            {
                Model = new ModelImporterModelPatch { GlobalScale = echoed }
            };
            var changed = new List<string>();

            request.Apply(state, changed);

            Assert.IsEmpty(changed);
        }

        [TestCase("{}", "schemaVersion")]
        [TestCase("{\"schemaVersion\":2,\"model\":{\"isReadable\":true}}", "integer 1")]
        [TestCase("{\"schemaVersion\":1,\"unknown\":true}", "Unknown field 'unknown'")]
        [TestCase("{\"schemaVersion\":1,\"model\":{}}", "at least one setting")]
        [TestCase("{\"schemaVersion\":1,\"model\":{\"isReadable\":\"true\"}}", "JSON boolean")]
        public void Parser_RejectsInvalidRequests(string json, string expected)
        {
            ModelImporterUpdateRequest request;
            string error;
            Assert.IsFalse(ModelImporterUpdateParser.TryParse(json, out request, out error));
            StringAssert.Contains(expected, error);
        }

        [Test]
        public void Handler_ReadsUpdatesAndSkipsANoOpForARealModelImporter()
        {
            CreateModelAsset();
            try
            {
                var guid = AssetDatabase.AssetPathToGUID(TestAssetPath);
                Assert.IsNotEmpty(guid);
                var handler = new ModelImporterHandler();

                var read = new FakeResponse();
                handler.HandleGet(read, guid);
                Assert.AreEqual(200, read.StatusCode, read.Body);
                StringAssert.Contains("\"schemaVersion\":1", read.Body);
                StringAssert.Contains("\"settings\":{", read.Body);
                StringAssert.Contains("\"localIdentifier\":\"", read.Body);
                StringAssert.Contains("UnityEngine.Mesh", read.Body);

                var importer = AssetImporter.GetAtPath(TestAssetPath) as ModelImporter;
                Assert.IsNotNull(importer);
                var requestedReadable = !importer.isReadable;
                var json = "{\"schemaVersion\":1,\"model\":{\"isReadable\":" +
                           (requestedReadable ? "true" : "false") + "}}";

                var preflight = new FakeResponse();
                handler.HandlePreflight(new FakeRequest("POST").WithJsonBody(json), preflight, guid);
                Assert.AreEqual(200, preflight.StatusCode, preflight.Body);
                StringAssert.Contains("\"valid\":true", preflight.Body);
                StringAssert.Contains("\"reimportRequired\":true", preflight.Body);
                Assert.AreNotEqual(requestedReadable, importer.isReadable);

                var update = new FakeResponse();
                handler.HandleUpdate(new FakeRequest("PATCH").WithJsonBody(json), update, guid);
                Assert.AreEqual(200, update.StatusCode, update.Body);
                StringAssert.Contains("\"reimported\":true", update.Body);
                StringAssert.Contains("\"changedFields\":[\"model.isReadable\"]", update.Body);
                StringAssert.Contains("\"diagnostics\":[", update.Body);
                importer = AssetImporter.GetAtPath(TestAssetPath) as ModelImporter;
                Assert.IsNotNull(importer);
                Assert.AreEqual(requestedReadable, importer.isReadable);

                var unchanged = new FakeResponse();
                handler.HandleUpdate(new FakeRequest("PATCH").WithJsonBody(json), unchanged, guid);
                Assert.AreEqual(200, unchanged.StatusCode, unchanged.Body);
                StringAssert.Contains("\"reimported\":false", unchanged.Body);
                StringAssert.Contains("\"added\":[]", unchanged.Body);
                StringAssert.Contains("\"removed\":[]", unchanged.Body);
            }
            finally
            {
                DeleteTestAssets();
            }
        }

        [Test]
        public void Handler_RestoresCoreSettingsWhenReimportThrows()
        {
            CreateModelAsset();
            try
            {
                var guid = AssetDatabase.AssetPathToGUID(TestAssetPath);
                var importer = AssetImporter.GetAtPath(TestAssetPath) as ModelImporter;
                Assert.IsNotNull(importer);
                var original = importer.isReadable;
                var requested = original ? "false" : "true";
                var handler = new ModelImporterHandler(
                    value => throw new InvalidOperationException("Forced reimport failure."));
                var response = new FakeResponse();

                handler.HandleUpdate(
                    new FakeRequest("PATCH").WithJsonBody(
                        "{\"schemaVersion\":1,\"model\":{\"isReadable\":" + requested + "}}"),
                    response,
                    guid);

                Assert.AreEqual(500, response.StatusCode, response.Body);
                StringAssert.Contains("\"attempted\":true", response.Body);
                StringAssert.Contains("\"restored\":true", response.Body);
                importer = AssetImporter.GetAtPath(TestAssetPath) as ModelImporter;
                Assert.IsNotNull(importer);
                Assert.AreEqual(original, importer.isReadable);
            }
            finally
            {
                DeleteTestAssets();
            }
        }

        private static void CreateModelAsset()
        {
            if (!AssetDatabase.IsValidFolder(TestDirectory))
                AssetDatabase.CreateFolder("Assets", "UnionAirModelImporterTests");
            File.WriteAllText(
                TestAssetPath,
                "o Triangle\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
            AssetDatabase.ImportAsset(TestAssetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void DeleteTestAssets()
        {
            AssetDatabase.DeleteAsset(TestDirectory);
            AssetDatabase.Refresh();
        }
    }
}

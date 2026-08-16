using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers how <c>PATCH /api/assets/materials</c> resolves a key to a shader property.
    /// </summary>
    /// <remarks>
    /// The reference tells a client that property names "are the ones the shader declares, and are
    /// case-sensitive", and nothing held the endpoint to it: the lookup was a detail of how the
    /// handler happened to tabulate the shader. These pin the promise instead, so the mechanism
    /// underneath can change without the answer changing.
    /// </remarks>
    internal sealed class MaterialPropertyLookupTests
    {
        private const string Dir = "Assets/UnionAirMaterialLookupTests";
        private const string MaterialPath = Dir + "/Test.mat";

        private string _guid;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "UnionAirMaterialLookupTests");

            AssetDatabase.DeleteAsset(MaterialPath);
            AssetDatabase.CreateAsset(new Material(Shader.Find("Standard")), MaterialPath);
            AssetDatabase.SaveAssets();
            _guid = AssetDatabase.AssetPathToGUID(MaterialPath);
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(Dir);

        [Test]
        public void TheNameTheShaderDeclaresIsWritten()
        {
            var response = Patch("{\"properties\":{\"_Glossiness\":0.25}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"updated\":[\"_Glossiness\"]", response.Body);
        }

        [Test]
        public void ANameThatDiffersOnlyInCaseIsRefused()
        {
            var response = Patch("{\"properties\":{\"_glossiness\":0.25}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("_glossiness", response.Body);
            StringAssert.Contains("case-sensitive", response.Body);
        }

        [Test]
        public void ARefusalNamesTheShaderItLookedIn()
        {
            // A client sending a name from another shader gets told which shader it is talking to,
            // rather than only that the name is wrong.
            var response = Patch("{\"properties\":{\"_BaseColor\":{\"r\":1,\"g\":0,\"b\":0,\"a\":1}}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("Standard", response.Body);
        }

        [Test]
        public void AHiddenPropertyIsStillWritable()
        {
            // Hidden is an Inspector concern, not a write one. Standard hides _Mode.
            var response = Patch("{\"properties\":{\"_Mode\":1.0}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"updated\":[\"_Mode\"]", response.Body);
        }

        private FakeResponse Patch(string body)
        {
            var request = new FakeRequest(
                "PATCH", "/api/assets/materials?guid=" + _guid).WithJsonBody(body);
            var response = new FakeResponse();
            new MaterialWriteHandler().Handle(request, response);
            return response;
        }
    }
}

using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers naming one object inside a file, and the promise that gives: a reference read from a
    /// multi-object asset resolves back to the object it was read from.
    /// </summary>
    /// <remarks>
    /// The vocabulary identified a file and a type, which is not an identity when a file holds
    /// twenty-three meshes. The write bound whichever object <c>LoadAssetAtPath</c> returned and
    /// answered <c>200</c>, and the read afterwards was byte-identical for every one of them, so a
    /// read-modify-write could quietly swap the mesh on a renderer.
    ///
    /// The fixture builds a two-material asset with <see cref="AssetDatabase.AddObjectToAsset"/>
    /// rather than importing a model, so the ambiguity is reproduced without depending on any
    /// particular file in the project.
    /// </remarks>
    internal sealed class SubAssetReferenceTests
    {
        private const string Dir = "Assets/UnionAirSubAssetTests";
        private const string SinglePath = Dir + "/Single.mat";
        private const string MultiPath = Dir + "/Multi.mat";

        private GameObject _target;
        private string _multiGuid;
        private string _mainId;
        private string _addedId;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "UnionAirSubAssetTests");

            AssetDatabase.DeleteAsset(SinglePath);
            AssetDatabase.CreateAsset(new Material(Shader.Find("Unlit/Color")), SinglePath);

            AssetDatabase.DeleteAsset(MultiPath);
            var main = new Material(Shader.Find("Unlit/Color")) { name = "Main" };
            AssetDatabase.CreateAsset(main, MultiPath);
            var added = new Material(Shader.Find("Unlit/Color")) { name = "Added" };
            AssetDatabase.AddObjectToAsset(added, MultiPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(MultiPath);

            _multiGuid = AssetDatabase.AssetPathToGUID(MultiPath);
            _mainId = LocalId(AssetDatabase.LoadMainAssetAtPath(MultiPath));
            foreach (var representation in AssetDatabase.LoadAllAssetRepresentationsAtPath(MultiPath))
                if (representation is Material) _addedId = LocalId(representation);

            _target = new GameObject("UnionAirSubAsset_" + Guid.NewGuid().ToString("N"));
            _target.AddComponent<MeshRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_target != null) UnityEngine.Object.DestroyImmediate(_target);
            AssetDatabase.DeleteAsset(Dir);
        }

        // ── What the read reports ────────────────────────────────────────────

        [Test]
        public void AReferenceCarriesTheLocalIdentifierOfTheObjectItNames()
        {
            Write("{\"assetPath\":\"" + SinglePath + "\"}");

            StringAssert.Contains("\"localIdentifier\":\"", Read());
        }

        [Test]
        public void TwoObjectsInOneFileAreDistinguishable()
        {
            // The heart of it: before, both of these read back byte-identical.
            Assert.IsNotNull(_addedId);
            Assert.AreNotEqual(_mainId, _addedId);
        }

        // ── The round trip ───────────────────────────────────────────────────

        [Test]
        public void ANamedSubAssetResolvesBackToTheObjectItNames()
        {
            var response = Write(Reference(_addedId));
            Assert.AreEqual(200, response.StatusCode, response.Body);

            StringAssert.Contains("\"localIdentifier\":\"" + _addedId + "\"", Read());
        }

        [Test]
        public void ANamedMainAssetResolvesBackToTheMainAsset()
        {
            var response = Write(Reference(_mainId));
            Assert.AreEqual(200, response.StatusCode, response.Body);

            StringAssert.Contains("\"localIdentifier\":\"" + _mainId + "\"", Read());
        }

        // ── The refusals ─────────────────────────────────────────────────────

        [Test]
        public void AnUnnamedReferenceToAMultiObjectFileIsRefused()
        {
            // It answered 200 before, having bound whichever object Unity returned.
            var response = Write(Reference(null));

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("2 objects assignable to Material", response.Body);
            StringAssert.Contains("localIdentifier", response.Body);
        }

        [Test]
        public void ALocalIdentifierNamingNothingAtThatPathIs404()
        {
            var response = Write(Reference("987654321"));

            Assert.AreEqual(404, response.StatusCode, response.Body);
            StringAssert.Contains("987654321", response.Body);
        }

        [Test]
        public void ALocalIdentifierThatIsNotADecimalIntegerIs400()
        {
            var response = Write(Reference("4300014.5"));

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("decimal integer", response.Body);
        }

        [Test]
        public void ALocalIdentifierThatIsNotAStringIs400()
        {
            // A JSON number loses the low bits of a 64-bit id, so the string is the shape.
            var response = Write("{\"assetGuid\":\"" + _multiGuid + "\",\"localIdentifier\":4300014}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("must be a JSON string", response.Body);
        }

        // ── What did not change ──────────────────────────────────────────────

        [Test]
        public void ASingleObjectFileStillResolvesWithoutOne()
        {
            // Most references name one of these, and nothing about them changes.
            var response = Write("{\"assetPath\":\"" + SinglePath + "\"}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string LocalId(UnityEngine.Object obj)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out _, out long id);
            return id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private string Reference(string localIdentifier)
            => "{\"assetGuid\":\"" + _multiGuid + "\",\"assetType\":\"UnityEngine.Material\"" +
               (localIdentifier == null ? "" : ",\"localIdentifier\":\"" + localIdentifier + "\"") + "}";

        private FakeResponse Write(string reference)
        {
            var target = "{\"type\":\"componentPath\",\"value\":\"" +
                         _target.name + ":UnityEngine.MeshRenderer\"}";
            var request = new FakeRequest(
                    "PATCH", "/api/gameobjects/components?target=" + Uri.EscapeDataString(target))
                .WithJsonBody("{\"properties\":{\"m_Materials\":[" + reference + "]}}");
            var response = new FakeResponse();
            new ComponentWriteHandler().Handle(request, response);
            return response;
        }

        private string Read()
        {
            var target = "{\"type\":\"hierarchyPath\",\"value\":\"" + _target.name + "\"}";
            var request = new FakeRequest(
                "GET", "/api/gameobjects?target=" + Uri.EscapeDataString(target));
            var response = new FakeResponse();
            new GameObjectHandler().Handle(request, response);
            Assert.AreEqual(200, response.StatusCode, response.Body);
            return response.Body;
        }
    }
}

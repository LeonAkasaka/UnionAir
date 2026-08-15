using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the shape an object reference is reported in, and the promise that gives:
    /// a value read out of a component can be sent back to the write endpoint unchanged.
    /// </summary>
    /// <remarks>
    /// The two directions were designed for different jobs and never reconciled. The read
    /// described what the object is -- <c>{type, name, globalObjectId}</c> -- and the write
    /// describes how to find one, so echoing a read answered
    /// <c>400 Unknown field 'name'</c>. <c>type</c> was the sharp edge: it carried the
    /// object's class in one direction and the kind of reference in the other, so a client
    /// was told its type was unknown about a field it never meant to fill in.
    ///
    /// These tests drive the handlers rather than the shapes, because the thing being
    /// promised is that one endpoint's output is another's input, and only running both
    /// shows it.
    /// </remarks>
    internal sealed class ObjectReferenceRoundTripTests
    {
        private const string Dir = "Assets/UnionAirRoundTripTests";
        private const string MaterialPath = Dir + "/Test.mat";

        private GameObject _target;
        private GameObject _anchor;
        private MeshRenderer _renderer;
        private Material _asset;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "UnionAirRoundTripTests");

            // A material on disk rather than in memory: an asset reference is the case where
            // the two spellings differ, and only a persisted object has a GUID and a path.
            AssetDatabase.DeleteAsset(MaterialPath);
            AssetDatabase.CreateAsset(new Material(Shader.Find("Unlit/Color")), MaterialPath);
            AssetDatabase.SaveAssets();
            _asset = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

            var suffix = Guid.NewGuid().ToString("N");
            _target = new GameObject("UnionAirRoundTripTarget_" + suffix);
            _anchor = new GameObject("UnionAirRoundTripAnchor_" + suffix);
            _renderer = _target.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _asset;
        }

        [TearDown]
        public void TearDown()
        {
            if (_target != null) UnityEngine.Object.DestroyImmediate(_target);
            if (_anchor != null) UnityEngine.Object.DestroyImmediate(_anchor);
            AssetDatabase.DeleteAsset(Dir);
        }

        // ── What the read reports ────────────────────────────────────────────

        [Test]
        public void Read_ReportsAnAssetReferenceInTheVocabularyTheWriteAccepts()
        {
            var body = Read();

            StringAssert.Contains("\"assetPath\":\"" + MaterialPath + "\"", body);
            StringAssert.Contains("\"assetType\":\"UnityEngine.Material\"", body);
            StringAssert.Contains(
                "\"assetGuid\":\"" + AssetDatabase.AssetPathToGUID(MaterialPath) + "\"", body);
        }

        [Test]
        public void Read_NoLongerReportsTheDisplayNameOfAReference()
        {
            // The readable half, and no field of the write carries it. Keeping it would mean
            // the write either refusing the read again or accepting a key it ignores.
            _asset.name = "UnionAirDistinctiveMaterialName";

            StringAssert.DoesNotContain("UnionAirDistinctiveMaterialName", Read());
        }

        [Test]
        public void Read_ReportsASceneObjectReferenceAsAGlobalObjectId()
        {
            // Reported rather than dropped: PATCH .../components resolves globalObjectId, so
            // this endpoint has a spelling for it.
            _renderer.probeAnchor = _anchor.transform;

            var body = Read();

            StringAssert.Contains("\"type\":\"globalObjectId\"", body);
            StringAssert.Contains(
                "\"value\":\"" + ObjectIdUtils.GetGlobalObjectId(_anchor.transform) + "\"", body);
        }

        [Test]
        public void Read_UsesTypeForTheKindOfReferenceAndNotForTheClass()
        {
            // The collision this change exists to remove. "Transform" in `type` was what made
            // an echoed read answer "Unknown m_ProbeAnchor.type: Transform".
            _renderer.probeAnchor = _anchor.transform;

            StringAssert.DoesNotContain("\"type\":\"Transform\"", Read());
        }

        // ── That the read is accepted by the write ───────────────────────────

        [Test]
        public void Write_AcceptsTheAssetReferenceTheReadReported()
        {
            var reference = ReferenceValue("m_Materials");
            _renderer.sharedMaterial = null;

            var response = Patch("{\"properties\":{\"m_Materials.Array.data[0]\":" + reference + "}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.AreSame(_asset, _renderer.sharedMaterial);
        }

        [Test]
        public void Write_ReadsTheSceneReferenceVocabularyRatherThanRefusingIt()
        {
            // The vocabulary is what this change is about, and it is what this asserts. The
            // resolution behind it cannot be exercised here: a test runs in an untitled scene,
            // an object in one has no GlobalObjectId -- Unity answers the null id
            // `GlobalObjectId_V1-0-0000...-0-0` -- and so nothing addressed that way resolves.
            // That is a property of Unity's id rather than of the spelling, and it predates
            // this change: the read reported the same id under `globalObjectId` before. In a
            // saved scene the same request round-trips, measured on 6000.0.80f1.
            //
            // So the check is that the failure is about the identity and not about the shape.
            // "Unknown field" and "Unknown ...type" are the two the old read produced, and
            // either one coming back would mean the vocabularies still disagree.
            _renderer.probeAnchor = _anchor.transform;
            var reference = ReferenceValue("m_ProbeAnchor");
            _renderer.probeAnchor = null;

            var response = Patch("{\"properties\":{\"m_ProbeAnchor\":" + reference + "}}");

            StringAssert.DoesNotContain("Unknown field", response.Body);
            StringAssert.DoesNotContain("Unknown m_ProbeAnchor.type", response.Body);
            StringAssert.Contains("globalObjectId", response.Body);
        }

        // ── That the two reads agree ─────────────────────────────────────────

        [Test]
        public void TheScriptableObjectReadSpellsAnAssetReferenceTheSameWay()
        {
            // Same value, same package, one spelling. The two reads disagreeing is half of
            // what made the round trip impossible to write a client for.
            var fixture = ScriptableObject.CreateInstance<UnionAirPropertyKeyFixture>();
            try
            {
                fixture.reference = _asset;

                var sb = new System.Text.StringBuilder();
                var iterator = new SerializedObject(fixture).FindProperty("reference");
                SerializedPropertySerializer.SerializePropertyToJson(iterator, sb);

                StringAssert.Contains("\"assetPath\":\"" + MaterialPath + "\"", sb.ToString());
                StringAssert.Contains("\"assetType\":\"UnityEngine.Material\"", sb.ToString());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture);
            }
        }

        [Test]
        public void TheScriptableObjectReadStillDropsASceneObjectReference()
        {
            // Deliberately not symmetrical. PATCH .../scriptableobjects accepts only the asset
            // fields, so reporting a scene reference there would hand back a value that
            // endpoint refuses -- the defect this change removes, in a smaller size.
            var fixture = ScriptableObject.CreateInstance<UnionAirPropertyKeyFixture>();
            try
            {
                fixture.reference = _anchor.transform;

                var sb = new System.Text.StringBuilder();
                var iterator = new SerializedObject(fixture).FindProperty("reference");
                SerializedPropertySerializer.SerializePropertyToJson(iterator, sb);

                Assert.AreEqual("null", sb.ToString());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

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

        /// <summary>
        /// The JSON value the read reported for one property, lifted out of the response so the
        /// write receives exactly what a client would have echoed rather than a rebuilt copy.
        /// </summary>
        private string ReferenceValue(string propertyName)
        {
            var body = Read();
            var key = "\"" + propertyName + "\":";
            var start = body.IndexOf(key, StringComparison.Ordinal);
            Assert.Greater(start, -1, propertyName + " missing from the read: " + body);
            start += key.Length;

            if (body[start] == '[') start++;
            Assert.AreEqual('{', body[start], "expected an object reference value: " + body);

            var depth = 0;
            for (var i = start; i < body.Length; i++)
            {
                if (body[i] == '{') depth++;
                else if (body[i] == '}' && --depth == 0)
                    return body.Substring(start, i - start + 1);
            }

            Assert.Fail("unterminated value for " + propertyName + ": " + body);
            return null;
        }

        private FakeResponse Patch(string body)
        {
            var target = "{\"type\":\"componentPath\",\"value\":\"" +
                         _target.name + ":UnityEngine.MeshRenderer\"}";
            var request = new FakeRequest(
                    "PATCH", "/api/gameobjects/components?target=" + Uri.EscapeDataString(target))
                .WithJsonBody(body);
            var response = new FakeResponse();

            new ComponentWriteHandler().Handle(request, response);
            return response;
        }
    }
}

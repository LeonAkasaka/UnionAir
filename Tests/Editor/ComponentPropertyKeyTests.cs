using System;
using NUnit.Framework;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers what a key in <c>properties</c> selects, and what happens to one that selects
    /// nothing writable, on <c>PATCH /api/gameobjects/components</c>.
    /// </summary>
    /// <remarks>
    /// Two defects meet here. A key used to be matched by a substring search over the whole body,
    /// so a name appearing inside another property's value selected a field the client never asked
    /// for. And a key that selected nothing was passed over in silence, so a typo, a number sent as
    /// a string, and an array all answered 200 with an empty <c>updated</c> — a write that did not
    /// happen, reported as success. The requests below are measured ones.
    /// </remarks>
    internal sealed class ComponentPropertyKeyTests
    {
        private GameObject _target;
        private GameObject _anchor;
        private MeshRenderer _renderer;
        private Material _material;

        [SetUp]
        public void SetUp()
        {
            var suffix = Guid.NewGuid().ToString("N");
            _target = new GameObject("UnionAirPropertyKeyTarget_" + suffix);
            _anchor = new GameObject("UnionAirPropertyKeyAnchor_" + suffix);
            _renderer = _target.AddComponent<MeshRenderer>();

            // One material, so m_Materials has an element for the array-path tests to address.
            _material = new Material(Shader.Find("Unlit/Color"));
            _renderer.sharedMaterial = _material;
        }

        [TearDown]
        public void TearDown()
        {
            if (_target != null) UnityEngine.Object.DestroyImmediate(_target);
            if (_anchor != null) UnityEngine.Object.DestroyImmediate(_anchor);
            if (_material != null) UnityEngine.Object.DestroyImmediate(_material);
        }

        // ── A key selects only what it names at the top level ────────────────

        [Test]
        public void ANestedStringValueDoesNotSelectAnotherProperty()
        {
            // The historical scanner searched for the quoted property name anywhere, then took
            // the next colon as its value separator. Keep "type" after "value" so this request
            // fails if that scanner returns: the value names m_ReceiveShadows exactly, and the
            // later "type" colon makes the false match complete.
            _anchor.name = "m_ReceiveShadows";
            Assert.IsTrue(_renderer.receiveShadows, "fixture expects the Unity default");

            var response = Patch(
                "{\"properties\":{\"m_ProbeAnchor\":{\"value\":\"" + _anchor.name +
                "\",\"type\":\"hierarchyPath\"}}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.AreSame(_anchor.transform, _renderer.probeAnchor);
            Assert.IsTrue(_renderer.receiveShadows, "the nested name must not have been written");
        }

        [Test]
        public void AnObjectReferenceWithAnUnknownMemberIsRejected()
        {
            Assert.IsTrue(_renderer.receiveShadows, "fixture expects the Unity default");

            var response = Patch(
                "{\"properties\":{\"m_ProbeAnchor\":{\"type\":\"hierarchyPath\",\"value\":\"" +
                _anchor.name + "\",\"typo\":1}}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("typo", response.Body);
            Assert.IsNull(_renderer.probeAnchor);
            Assert.IsTrue(_renderer.receiveShadows);
        }

        [Test]
        public void AnObjectReferenceWithADuplicateMemberIsRejected()
        {
            var response = Patch(
                "{\"properties\":{\"m_ProbeAnchor\":{\"type\":\"hierarchyPath\",\"type\":\"componentPath\",\"value\":\"" +
                _anchor.name + "\"}}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("Duplicate field 'type'", response.Body);
            Assert.IsNull(_renderer.probeAnchor);
        }

        [Test]
        public void AKeyNamingNothingIsRejectedEvenWhenItNestsRealNames()
        {
            var response = Patch(
                "{\"properties\":{\"decoy\":{\"m_ProbeAnchor\":" +
                "{\"type\":\"hierarchyPath\",\"value\":\"" + _anchor.name + "\"}}}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("decoy", response.Body);
            Assert.IsNull(_renderer.probeAnchor);
        }

        // ── Every key sent is accounted for ──────────────────────────────────

        [Test]
        public void AKeyNamingNoPropertyIsRejected()
        {
            var response = Patch("{\"properties\":{\"decoy\":1}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("decoy", response.Body);
            StringAssert.Contains("UnityEngine.MeshRenderer", response.Body);
        }

        [Test]
        public void AChildPropertysBareNameIsRejected()
        {
            // "x" is the name of the child of every vector property, and its propertyPath is
            // "m_LocalPosition.x". The write loop addresses children by path, so a gate that
            // accepted the bare name let this through and then applied it to nothing.
            var response = Patch("{\"properties\":{\"x\":5}}", "UnityEngine.Transform");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("'x'", response.Body);
        }

        [Test]
        public void AChildPropertysFullPathStillWrites()
        {
            var response = Patch(
                "{\"properties\":{\"m_LocalPosition.x\":5}}", "UnityEngine.Transform");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.AreEqual(5f, _target.transform.localPosition.x);
        }

        [Test]
        public void AValueOfTheWrongTypeIsRejected()
        {
            // A client that JSON-encodes its numbers and booleans as strings used to get a 200
            // for a request that changed nothing.
            var response = Patch("{\"properties\":{\"m_ReceiveShadows\":\"false\"}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("JSON boolean", response.Body);
            Assert.IsTrue(_renderer.receiveShadows);
        }

        [Test]
        public void AnArrayPropertyIsRejected()
        {
            var response = Patch("{\"properties\":{\"m_Materials\":[]}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("array", response.Body);
        }

        [Test]
        public void AnArrayElementIsRejected()
        {
            // The walk descends into children, so this arrives as an ordinary object reference and
            // was written one element at a time -- past the guard that refuses the array itself.
            var response = Patch("{\"properties\":{\"m_Materials.Array.data[0]\":null}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("array", response.Body);
            Assert.AreSame(_material, _renderer.sharedMaterial);
        }

        [Test]
        public void AnArraySizeIsRejected()
        {
            // Refused for being part of an array rather than for its serialized type, which is
            // what the catch-all used to say and what said nothing about arrays.
            var response = Patch("{\"properties\":{\"m_Materials.Array.size\":2}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("array", response.Body);
            Assert.AreEqual(1, _renderer.sharedMaterials.Length);
        }

        [Test]
        public void APropertyWhoseSerializedTypeHasNoWriteSupportIsRejected()
        {
            // Rect is serialized by the read direction and has no write case, so this used to
            // report a viewport as set that was never touched. Quaternion and Bounds are the
            // same shape of gap. A built-in component carries one, which matters here: Unity
            // refuses to attach a MonoBehaviour compiled into an Editor-only assembly, so this
            // test assembly cannot supply a component of its own.
            _target.AddComponent<Camera>();

            var response = Patch(
                "{\"properties\":{\"m_NormalizedViewPortRect\":" +
                "{\"x\":0,\"y\":0,\"width\":0.5,\"height\":1}}}",
                "UnityEngine.Camera");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("Rect", response.Body);
        }

        [Test]
        public void AnUnreadableValueIsReportedWithItsKey()
        {
            // An unescaped Windows path. Naming the key beats reporting the body as malformed,
            // because the key is the part the client has to fix.
            var response = Patch(
                "{\"properties\":{\"m_ProbeAnchor\":{\"assetPath\":\"C:\\Assets\\Foo.mat\"}}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("m_ProbeAnchor", response.Body);
            Assert.IsNull(_renderer.probeAnchor);
        }

        [Test]
        public void ARejectedRequestWritesNothingItAlreadyAccepted()
        {
            // m_ReceiveShadows is visited before m_Materials, so it is applied to the
            // SerializedObject before the array is refused. Nothing may reach the component.
            var response = Patch("{\"properties\":{\"m_ReceiveShadows\":false,\"m_Materials\":[]}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            Assert.IsTrue(_renderer.receiveShadows, "a refused request must not half-apply");
        }

        [Test]
        public void AnEmptyPropertiesObjectIsAccepted()
        {
            // Nothing was sent, so nothing is unaccounted for.
            var response = Patch("{\"properties\":{}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"updated\":[]", response.Body);
        }

        // ── The writes that must keep working ────────────────────────────────

        [Test]
        public void ATopLevelKeyStillWritesAnObjectReference()
        {
            var response = Patch(
                "{\"properties\":{\"m_ProbeAnchor\":" +
                "{\"type\":\"hierarchyPath\",\"value\":\"" + _anchor.name + "\"}}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"m_ProbeAnchor\"", response.Body);
            Assert.AreSame(_anchor.transform, _renderer.probeAnchor);
        }

        [Test]
        public void ATopLevelKeyStillWritesAScalar()
        {
            Assert.IsTrue(_renderer.receiveShadows, "fixture expects the Unity default");

            var response = Patch("{\"properties\":{\"m_ReceiveShadows\":false}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"m_ReceiveShadows\"", response.Body);
            Assert.IsFalse(_renderer.receiveShadows);
        }

        [Test]
        public void TheFixtureDecodesItsQueryTheWayTheServerDoes()
        {
            // This file is the only place a JSON object travels through the query string, so the
            // fake's decoding is load-bearing here. Measured against a live server on 6000.0.80f1:
            // ?target=...A+B... reaches the handler as "A B", and %2B reaches it as "+".
            var request = new FakeRequest("GET", "/api/test?name=A+B&path=Assets%2FC%2B%2B");

            Assert.AreEqual("A B", request.QueryString["name"]);
            Assert.AreEqual("Assets/C++", request.QueryString["path"]);
        }

        private FakeResponse Patch(string body, string component = "UnityEngine.MeshRenderer")
        {
            var target = "{\"type\":\"componentPath\",\"value\":\"" +
                         _target.name + ":" + component + "\"}";
            var request = new FakeRequest(
                    "PATCH",
                    "/api/gameobjects/components?target=" + Uri.EscapeDataString(target))
                .WithJsonBody(body);
            var response = new FakeResponse();

            new ComponentWriteHandler().Handle(request, response);
            return response;
        }
    }
}

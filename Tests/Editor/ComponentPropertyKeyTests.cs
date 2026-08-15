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
    /// for. And a key that selected nothing was passed over in silence, so a typo and a number sent
    /// as a string answered 200 with an empty <c>updated</c> — a write that did not happen,
    /// reported as success. The requests below are measured ones.
    ///
    /// The array keys are here for a third reason: an array is addressed in three ways and none of
    /// them is reached by the walk the other keys use, so what selects them is the part to cover.
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

        // ── The three array addresses ────────────────────────────────────────

        [Test]
        public void AWholeArrayIsReplaced()
        {
            var response = Patch("{\"properties\":{\"m_Materials\":[null,null]}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"m_Materials\"", response.Body);
            Assert.AreEqual(2, _renderer.sharedMaterials.Length);
            Assert.IsNull(_renderer.sharedMaterial);
        }

        [Test]
        public void AnEmptyJsonArrayClearsTheArray()
        {
            var response = Patch("{\"properties\":{\"m_Materials\":[]}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.AreEqual(0, _renderer.sharedMaterials.Length);
        }

        [Test]
        public void AnArrayElementIsWrittenInPlace()
        {
            var response = Patch("{\"properties\":{\"m_Materials.Array.data[0]\":null}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("m_Materials.Array.data[0]", response.Body);
            Assert.AreEqual(1, _renderer.sharedMaterials.Length, "an element write must not resize");
            Assert.IsNull(_renderer.sharedMaterial);
        }

        [Test]
        public void AnArraySizeResizes()
        {
            var response = Patch("{\"properties\":{\"m_Materials.Array.size\":2}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.AreEqual(2, _renderer.sharedMaterials.Length);
        }

        [Test]
        public void ATwoElementWriteAddressesEachIndexIndependently()
        {
            // Two elements of one array are two independent writes; only a length beside them is
            // a conflict, so this must not be caught by the check that refuses that pairing.
            var response = Patch(
                "{\"properties\":{\"m_Materials.Array.size\":2}}");
            Assert.AreEqual(200, response.StatusCode, response.Body);

            response = Patch(
                "{\"properties\":{\"m_Materials.Array.data[0]\":null," +
                "\"m_Materials.Array.data[1]\":null}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.AreEqual(2, _renderer.sharedMaterials.Length);
        }

        [Test]
        public void ANegativeArraySizeIsRejected()
        {
            var response = Patch("{\"properties\":{\"m_Materials.Array.size\":-1}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            Assert.AreEqual(1, _renderer.sharedMaterials.Length);
        }

        [Test]
        public void AnArraySizePastTheLimitIsRejectedBeforeUnityAllocatesIt()
        {
            // The one write whose cost is not bounded by the request carrying it: a few bytes name
            // a length Unity would try to allocate, and the Editor goes down with it.
            var response = Patch("{\"properties\":{\"m_Materials.Array.size\":2000000000}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains(
                SerializedPropertySerializer.MaxArrayLength.ToString(), response.Body);
            Assert.AreEqual(1, _renderer.sharedMaterials.Length);
        }

        [Test]
        public void AnElementIndexPastTheEndIsRejectedWithTheLength()
        {
            var response = Patch("{\"properties\":{\"m_Materials.Array.data[3]\":null}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("1 element", response.Body);
            Assert.AreSame(_material, _renderer.sharedMaterial);
        }

        [Test]
        public void AnArrayValueOfTheWrongShapeIsRejected()
        {
            var response = Patch("{\"properties\":{\"m_Materials\":5}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("JSON array", response.Body);
            Assert.AreSame(_material, _renderer.sharedMaterial);
        }

        [Test]
        public void AnElementOfTheWrongShapeIsNamedByItsOwnAddress()
        {
            // The array key is what the client sent; the element address is what it has to fix.
            var response = Patch("{\"properties\":{\"m_Materials\":[null,5]}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("m_Materials.Array.data[1]", response.Body);
            Assert.AreSame(_material, _renderer.sharedMaterial);
            Assert.AreEqual(1, _renderer.sharedMaterials.Length, "the resize must not survive either");
        }

        [Test]
        public void ALengthAndAnElementForOneArrayAreRejected()
        {
            var response = Patch(
                "{\"properties\":{\"m_Materials.Array.size\":2," +
                "\"m_Materials.Array.data[0]\":null}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("m_Materials", response.Body);
            Assert.AreEqual(1, _renderer.sharedMaterials.Length);
            Assert.AreSame(_material, _renderer.sharedMaterial);
        }

        [Test]
        public void AKeyReachingPastAnElementIsRejected()
        {
            // The walk follows foldout state, so a key like this was reachable or not depending on
            // Editor UI. Refused by name instead, whatever the walk would have done.
            var response = Patch("{\"properties\":{\"m_Materials.Array.data[0].name\":\"x\"}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("inside an array", response.Body);
            Assert.AreSame(_material, _renderer.sharedMaterial);
        }

        [Test]
        public void AnElementAddressOnSomethingThatIsNotAnArrayIsRejected()
        {
            var response = Patch("{\"properties\":{\"m_ReceiveShadows.Array.data[0]\":true}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("m_ReceiveShadows", response.Body);
            Assert.IsTrue(_renderer.receiveShadows);
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
            // The walk applies m_ReceiveShadows to the SerializedObject, and the array pass that
            // runs after it refuses the element. Neither may reach the component.
            var response = Patch("{\"properties\":{\"m_ReceiveShadows\":false,\"m_Materials\":[5]}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            Assert.IsTrue(_renderer.receiveShadows, "a refused request must not half-apply");
            Assert.AreSame(_material, _renderer.sharedMaterial);
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

        // ── The Inspector header checkbox ────────────────────────────────────

        [Test]
        public void EnabledIsWritableAsItsOwnField()
        {
            Assert.IsTrue(_renderer.enabled, "fixture expects the Unity default");

            var response = Patch("{\"enabled\":false}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"enabled\":false", response.Body);
            Assert.IsFalse(_renderer.enabled);
        }

        [Test]
        public void EnabledAndPropertiesTravelInOneRequest()
        {
            var response = Patch("{\"properties\":{\"m_ReceiveShadows\":false},\"enabled\":false}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.IsFalse(_renderer.enabled);
            Assert.IsFalse(_renderer.receiveShadows);
        }

        [Test]
        public void EnabledIsReportedEvenWhenTheRequestDidNotSetIt()
        {
            var response = Patch("{\"properties\":{\"m_ReceiveShadows\":false}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"enabled\":true", response.Body);
        }

        [Test]
        public void EnabledIsRejectedOnAComponentThatHasNoCheckbox()
        {
            // Transform shows no checkbox, and has no m_Enabled behind one.
            var response = Patch("{\"enabled\":false}", "UnityEngine.Transform");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("no enabled state", response.Body);
        }

        [Test]
        public void EnabledRejectsANonBooleanValue()
        {
            var response = Patch("{\"enabled\":\"false\"}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("JSON boolean", response.Body);
            Assert.IsTrue(_renderer.enabled);
        }

        [Test]
        public void ARequestCarryingNeitherFieldIsRejected()
        {
            var response = Patch("{}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("'enabled'", response.Body);
        }

        [Test]
        public void MEnabledSentAsAPropertyKeySaysWhereTheCheckboxLives()
        {
            // The name is real and serialized, but it is not in the walk this endpoint addresses,
            // so the message has to point somewhere rather than only deny the name.
            var response = Patch("{\"properties\":{\"m_Enabled\":false}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("'enabled' field", response.Body);
            Assert.IsTrue(_renderer.enabled);
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

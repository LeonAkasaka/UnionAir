using System;
using NUnit.Framework;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers which serialized property a <c>properties</c> key selects on
    /// <c>PATCH /api/gameobjects/components</c>.
    /// </summary>
    /// <remarks>
    /// The selection used to be a substring search over the whole body, so a key appearing only
    /// inside another property's value -- and every object reference, vector and colour payload is
    /// full of them -- named a field the client never asked to write. The two negative cases below
    /// are the two ways that escaped: one wrote a field, the other failed the whole request over a
    /// field it should not have looked at. Both are measured requests, not invented ones.
    /// </remarks>
    internal sealed class ComponentPropertyKeyTests
    {
        private GameObject _target;
        private GameObject _anchor;
        private MeshRenderer _renderer;

        [SetUp]
        public void SetUp()
        {
            var suffix = Guid.NewGuid().ToString("N");
            _target = new GameObject("UnionAirPropertyKeyTarget_" + suffix);
            _anchor = new GameObject("UnionAirPropertyKeyAnchor_" + suffix);
            _renderer = _target.AddComponent<MeshRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_target != null) UnityEngine.Object.DestroyImmediate(_target);
            if (_anchor != null) UnityEngine.Object.DestroyImmediate(_anchor);
        }

        [Test]
        public void AKeyOnlyInsideANestedObjectDoesNotWriteTheField()
        {
            var response = Patch(
                "{\"properties\":{\"decoy\":{\"m_ProbeAnchor\":" +
                "{\"type\":\"hierarchyPath\",\"value\":\"" + _anchor.name + "\"}}}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"updated\":[]", response.Body);
            Assert.IsNull(_renderer.probeAnchor);
        }

        [Test]
        public void AKeyOnlyInsideANestedObjectDoesNotFailTheRequest()
        {
            // The nested value is not a legal object reference. Reading it at all answered 400 and
            // discarded every other property in the same request.
            var response = Patch("{\"properties\":{\"decoy\":{\"m_ProbeAnchor\":5}}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"updated\":[]", response.Body);
            Assert.IsNull(_renderer.probeAnchor);
        }

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
        public void ATopLevelKeyIsStillFoundBesideANestedObjectCarryingTheSameName()
        {
            // The top-level key comes second here: a scanner that stops at the first match finds
            // the nested one, which is the shape the substring search failed on.
            var response = Patch(
                "{\"properties\":{\"decoy\":{\"m_ProbeAnchor\":null},\"m_ProbeAnchor\":" +
                "{\"type\":\"hierarchyPath\",\"value\":\"" + _anchor.name + "\"}}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.AreSame(_anchor.transform, _renderer.probeAnchor);
        }

        private FakeResponse Patch(string body)
        {
            var target = "{\"type\":\"componentPath\",\"value\":\"" +
                         _target.name + ":UnityEngine.MeshRenderer\"}";
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

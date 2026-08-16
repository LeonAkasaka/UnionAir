using System;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the serialized types <c>GET /api/gameobjects</c> reports in
    /// <c>components[].properties</c>.
    /// </summary>
    /// <remarks>
    /// The endpoint carried its own copy of the property serializer, and that copy had no case for
    /// <c>Quaternion</c> or <c>Bounds</c>, so both fell through to <c>null</c> -- the rotation of
    /// every Transform in every project read as nothing, while the same value read from a
    /// ScriptableObject through the shared serializer did not. One value, two answers, decided by
    /// which endpoint produced it.
    ///
    /// The copy is gone, so the types are exercised against the shared serializer and the
    /// component read is shown to reach it by a Transform's own <c>Quaternion</c> -- the case the
    /// issue measured, and the one every project has.
    /// </remarks>
    internal sealed class ComponentSerializedTypeTests
    {
        private GameObject _target;
        private UnionAirSerializedTypeFixture _fixture;

        [SetUp]
        public void SetUp()
        {
            _target = new GameObject("UnionAirSerializedType_" + Guid.NewGuid().ToString("N"));
            _fixture = ScriptableObject.CreateInstance<UnionAirSerializedTypeFixture>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_target != null) UnityEngine.Object.DestroyImmediate(_target);
            if (_fixture != null) UnityEngine.Object.DestroyImmediate(_fixture);
        }

        // ── Through the endpoint ─────────────────────────────────────────────

        [Test]
        public void ATransformReportsItsRotation()
        {
            // The case the issue measured: every Transform in every project read as null here,
            // and it is also the proof that the component read now reaches the shared serializer.
            _target.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            StringAssert.Contains(
                "\"m_LocalRotation\":{\"x\":0,\"y\":0.7071068,\"z\":0,\"w\":0.7071068}", Read());
        }

        [Test]
        public void AReferenceToASceneObjectIsStillResolved()
        {
            // The one behaviour the two readers genuinely differ on: a component read resolves a
            // scene reference, an asset read reports null. Folding them together must not have
            // taken the component side down to the asset side.
            var renderer = _target.AddComponent<MeshRenderer>();
            var anchor = new GameObject("UnionAirAnchor_" + Guid.NewGuid().ToString("N"));
            try
            {
                renderer.probeAnchor = anchor.transform;

                StringAssert.Contains("\"m_ProbeAnchor\":{\"type\":\"globalObjectId\"", Read());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(anchor);
            }
        }

        [Test]
        public void ReportingATypeDoesNotMakeItWritable()
        {
            _target.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            StringAssert.Contains("\"m_LocalRotation\":", Read());

            var target = "{\"type\":\"componentPath\",\"value\":\"" +
                         _target.name + ":UnityEngine.Transform\"}";
            var request = new FakeRequest(
                    "PATCH", "/api/gameobjects/components?target=" + Uri.EscapeDataString(target))
                .WithJsonBody("{\"properties\":{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1}}}");
            var response = new FakeResponse();
            new ComponentWriteHandler().Handle(request, response);

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("Quaternion", response.Body);
        }

        [Test]
        public void ASkinnedMeshRendererReportsItsLocalBounds()
        {
            // The Bounds half, through the endpoint rather than through the serializer alone.
            // m_AABB is the renderer's local bounds and is a real Bounds property -- unlike a
            // MeshRenderer, which has no such property at all rather than reporting it as null.
            var renderer = _target.AddComponent<SkinnedMeshRenderer>();
            renderer.localBounds = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(2f, 4f, 6f));

            StringAssert.Contains(
                "\"m_AABB\":{\"center\":{\"x\":1,\"y\":2,\"z\":3},\"extents\":{\"x\":1,\"y\":2,\"z\":3}}",
                Read());
        }

        [Test]
        public void AMeshRendererHasNoBoundsPropertyAtAll()
        {
            // Recorded because it looks like the same gap and is not one: the key is absent from
            // the response, not reported as null. A probe that reads it with a defaulting lookup
            // cannot tell those apart -- mine could not, and said so wrongly.
            _target.AddComponent<MeshRenderer>();

            StringAssert.DoesNotContain("m_AABB", Read());
        }

        // ── Through the shared serializer ────────────────────────────────────

        [Test]
        public void AQuaternionIsReportedWithAllFourComponents()
        {
            Assert.AreEqual("{\"x\":0.1,\"y\":0.2,\"z\":0.3,\"w\":0.9}", Serialize("rotation"));
        }

        [Test]
        public void ABoundsIsReportedAsCentreAndExtents()
        {
            // Unity's own two fields, and the shape the ScriptableObject read has always used.
            Assert.AreEqual(
                "{\"center\":{\"x\":1,\"y\":2,\"z\":3},\"extents\":{\"x\":1,\"y\":2,\"z\":3}}",
                Serialize("volume"));
        }

        [Test]
        public void ARectIsUnchanged()
        {
            // Already reported before the change, and asserted so that folding the two readers
            // together is shown not to have moved anything that already worked.
            Assert.AreEqual("{\"x\":1,\"y\":2,\"width\":3,\"height\":4}", Serialize("area"));
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private string Serialize(string propertyName)
        {
            var property = new SerializedObject(_fixture).FindProperty(propertyName);
            Assert.IsNotNull(property, propertyName + " missing from the fixture");

            var sb = new StringBuilder();
            SerializedPropertySerializer.SerializePropertyToJson(property, sb, true);
            return sb.ToString();
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

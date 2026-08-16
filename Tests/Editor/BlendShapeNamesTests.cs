using System;
using NUnit.Framework;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the blend shape names <c>GET /api/gameobjects</c> reports beside a
    /// <see cref="SkinnedMeshRenderer"/>'s properties, and the promise that gives: index
    /// <c>i</c> names the shape <c>m_BlendShapeWeights[i]</c> drives.
    /// </summary>
    /// <remarks>
    /// The handler is driven rather than the mesh inspected, because the thing being promised
    /// is about the response: that the names arrive in mesh order, that they arrive outside
    /// <c>properties</c> where the write endpoint will not be asked to accept them back, and
    /// that a renderer with nothing to report says so with an empty array rather than by
    /// omitting the field.
    ///
    /// A mesh is built here rather than imported. Blend shapes usually arrive from an FBX, but
    /// <see cref="Mesh.AddBlendShapeFrame"/> makes one in memory, which keeps the case in the
    /// EditMode suite instead of in a manual check against a live Editor.
    /// </remarks>
    internal sealed class BlendShapeNamesTests
    {
        private GameObject _target;

        [TearDown]
        public void TearDown()
        {
            if (_target != null) UnityEngine.Object.DestroyImmediate(_target);
        }

        [Test]
        public void Read_NamesTheShapesInMeshOrder()
        {
            var renderer = NewSkinnedRenderer();
            renderer.sharedMesh = MeshWithShapes("mouth_a", "mouth_i", "mouth_u");

            var body = Read();

            StringAssert.Contains(
                "\"blendShapeNames\":[\"mouth_a\",\"mouth_i\",\"mouth_u\"]", body);
        }

        [Test]
        public void Read_NamesTheMeshsShapesEvenWhenTheSerializedWeightsHaveNotCaughtUp()
        {
            var renderer = NewSkinnedRenderer();
            renderer.sharedMesh = MeshWithShapes("mouth_a", "mouth_i", "mouth_u");

            var body = Read();

            // The two arrays have two sources -- the names are the mesh's, the weights are what
            // was serialized on the component -- and Unity does not resize the serialized array
            // the moment a mesh is assigned. Measured here rather than assumed: three names
            // arrive beside an empty weights array. Neither is corrected to match the other,
            // which is why the documentation tells a client to index defensively.
            Assert.AreEqual(3, Elements(body, "\"blendShapeNames\":["), body);
            Assert.AreEqual(0, Elements(body, "\"m_BlendShapeWeights\":["), body);
        }

        [Test]
        public void Read_ReportsTheNamesOutsideProperties()
        {
            var renderer = NewSkinnedRenderer();
            renderer.sharedMesh = MeshWithShapes("mouth_a");

            // Inside 'properties' the field would name a key PATCH /api/gameobjects/components
            // has to refuse, which is the read/write divergence removed elsewhere in this release.
            // Read from the renderer's own component object: every component before it in the
            // response carries a 'properties' of its own.
            var component = SkinnedRendererJson(Read());

            var names = component.IndexOf("\"blendShapeNames\"", StringComparison.Ordinal);
            var properties = component.IndexOf("\"properties\"", StringComparison.Ordinal);
            Assert.Greater(names, -1, component);
            Assert.Greater(properties, -1, component);
            Assert.Less(names, properties, component);
        }

        [Test]
        public void Read_ReportsAnEmptyArrayForAMeshWithoutShapes()
        {
            var renderer = NewSkinnedRenderer();
            renderer.sharedMesh = MeshWithShapes();

            StringAssert.Contains("\"blendShapeNames\":[]", Read());
        }

        [Test]
        public void Read_ReportsAnEmptyArrayWhenThereIsNoMesh()
        {
            NewSkinnedRenderer();

            // Empty rather than absent, and not distinguished from a mesh carrying no shapes:
            // m_Mesh already tells those two apart, and a client indexing the weights with the
            // names never has to special-case either.
            StringAssert.Contains("\"blendShapeNames\":[]", Read());
        }

        [Test]
        public void Read_OmitsTheFieldForAComponentThatHasNoBlendShapes()
        {
            _target = new GameObject("UnionAirBlendShapes_" + Guid.NewGuid().ToString("N"));
            _target.AddComponent<MeshRenderer>();

            StringAssert.DoesNotContain("blendShapeNames", Read());
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private SkinnedMeshRenderer NewSkinnedRenderer()
        {
            _target = new GameObject("UnionAirBlendShapes_" + Guid.NewGuid().ToString("N"));
            return _target.AddComponent<SkinnedMeshRenderer>();
        }

        private static Mesh MeshWithShapes(params string[] names)
        {
            var mesh = new Mesh { name = "UnionAirBlendShapeFixture" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };

            var delta = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            foreach (var name in names)
                mesh.AddBlendShapeFrame(name, 100f, delta, null, null);

            return mesh;
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

        private static string SkinnedRendererJson(string body)
        {
            const string marker = "{\"type\":\"UnityEngine.SkinnedMeshRenderer\"";
            var start = body.IndexOf(marker, StringComparison.Ordinal);
            Assert.Greater(start, -1, "no SkinnedMeshRenderer in the read: " + body);
            return body.Substring(start);
        }

        private static int Elements(string body, string key)
        {
            var start = body.IndexOf(key, StringComparison.Ordinal);
            Assert.Greater(start, -1, key + " missing from the read: " + body);
            start += key.Length;

            var end = body.IndexOf(']', start);
            Assert.Greater(end, -1, "unterminated array for " + key + ": " + body);

            var contents = body.Substring(start, end - start);
            return contents.Length == 0 ? 0 : contents.Split(',').Length;
        }
    }
}

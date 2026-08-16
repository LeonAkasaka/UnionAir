using System;
using NUnit.Framework;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the world transform and renderer bounds <c>GET /api/gameobjects</c> reports beside
    /// the local transform and the serialized properties.
    /// </summary>
    /// <remarks>
    /// Composing a world transform out of the local ones a client can read means reimplementing
    /// Unity's transform hierarchy, and it was not even open: <c>m_LocalRotation</c> reads
    /// <c>null</c>, so the rotations the composition needs are the ones missing. The workaround was
    /// to reparent marker objects to the scene root and subtract — four creates, four reparents,
    /// four reads and four deletes to learn where one bone is and which way it faces.
    ///
    /// The handler is driven rather than <see cref="Transform"/> inspected, because what is being
    /// promised is about the response: that both frames arrive, that the derived scale is named so
    /// nobody writes it back, and that the basis vectors are there so a direction does not have to
    /// be recovered from Euler angles.
    /// </remarks>
    internal sealed class WorldTransformAndBoundsTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
        }

        [Test]
        public void ARootObjectReportsTheSamePositionInBothFrames()
        {
            var go = NewObject();
            go.transform.position = new Vector3(1f, 2f, 3f);

            var local = Vector(Read(), "\"transform\":{\"position\":");
            var world = Vector(Read(), "\"worldTransform\":{\"position\":");

            Assert.AreEqual(new Vector3(1f, 2f, 3f), world);
            Assert.AreEqual(local, world);
        }

        [Test]
        public void AChildReportsThePositionItsParentPutItAt()
        {
            var parent = NewObject();
            parent.transform.position = new Vector3(10f, 0f, 0f);

            var child = new GameObject("Child");
            child.transform.SetParent(parent.transform);
            child.transform.localPosition = new Vector3(0f, 5f, 0f);

            var body = Read(child.name);
            Assert.AreEqual(new Vector3(0f, 5f, 0f), Vector(body, "\"transform\":{\"position\":"));
            Assert.AreEqual(new Vector3(10f, 5f, 0f), Vector(body, "\"worldTransform\":{\"position\":"));
        }

        [Test]
        public void LossyScaleIsTheProductOfTheChainAndNotTheLocalValue()
        {
            var parent = NewObject();
            parent.transform.localScale = new Vector3(2f, 2f, 2f);

            var child = new GameObject("Child");
            child.transform.SetParent(parent.transform);
            child.transform.localScale = new Vector3(3f, 3f, 3f);

            var body = Read(child.name);

            // Reported under Unity's name because it is derived and cannot be written back;
            // calling it "scale" would invite a client to echo it into a PATCH that means the
            // local value.
            Assert.AreEqual(new Vector3(3f, 3f, 3f), Vector(body, "\"transform\":{\"position\":{\"x\":0,\"y\":0,\"z\":0},\"rotation\":{\"x\":0,\"y\":0,\"z\":0},\"scale\":"));
            Assert.AreEqual(new Vector3(6f, 6f, 6f), Vector(body, "\"lossyScale\":"));
        }

        [Test]
        public void TheBasisVectorsPointWhereTheObjectPoints()
        {
            var go = NewObject();
            go.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            var body = Read();

            // A quarter turn about Y: forward becomes world +X, right becomes world -Z.
            AssertVector(new Vector3(0f, 0f, -1f), Vector(body, "\"right\":"));
            AssertVector(new Vector3(0f, 1f, 0f), Vector(body, "\"up\":"));
            AssertVector(new Vector3(1f, 0f, 0f), Vector(body, "\"forward\":"));
        }

        [Test]
        public void TheBasisVectorsAreUnitLengthAndPerpendicular()
        {
            var go = NewObject();
            go.transform.rotation = Quaternion.Euler(23f, -47f, 91f);

            var body = Read();
            var right = Vector(body, "\"right\":");
            var up = Vector(body, "\"up\":");
            var forward = Vector(body, "\"forward\":");

            Assert.AreEqual(1f, right.magnitude, 1e-3f);
            Assert.AreEqual(1f, up.magnitude, 1e-3f);
            Assert.AreEqual(1f, forward.magnitude, 1e-3f);
            Assert.AreEqual(0f, Vector3.Dot(right, up), 1e-3f);
            Assert.AreEqual(0f, Vector3.Dot(up, forward), 1e-3f);
            Assert.AreEqual(0f, Vector3.Dot(forward, right), 1e-3f);
        }

        [Test]
        public void ARendererReportsItsWorldBounds()
        {
            var go = NewObject();
            var renderer = go.AddComponent<MeshRenderer>();
            go.AddComponent<MeshFilter>().sharedMesh = UnitQuad();
            go.transform.position = new Vector3(4f, 0f, 0f);

            var body = Read();

            // Renderer.bounds is world space, so the centre moves with the object. The serialized
            // m_AABB is the local bounds and is a different value.
            AssertVector(renderer.bounds.center, Vector(body, "\"bounds\":{\"center\":"));
            AssertVector(new Vector3(4f, 0f, 0f), Vector(body, "\"bounds\":{\"center\":"));
        }

        [Test]
        public void AComponentThatIsNotARendererHasNoBounds()
        {
            NewObject().AddComponent<BoxCollider>();

            var body = Read();
            var transformEnd = body.IndexOf("\"components\":", StringComparison.Ordinal);
            StringAssert.DoesNotContain("\"bounds\"", body.Substring(transformEnd));
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private GameObject NewObject()
        {
            _root = new GameObject("UnionAirWorld_" + Guid.NewGuid().ToString("N"));
            return _root;
        }

        private static Mesh UnitQuad()
        {
            var mesh = new Mesh { name = "UnionAirWorldFixture" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0f, 0.5f, 0f)
            };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private string Read(string name = null)
        {
            var value = name == null ? _root.name : _root.name + "/" + name;
            var target = "{\"type\":\"hierarchyPath\",\"value\":\"" + value + "\"}";
            var request = new FakeRequest(
                "GET", "/api/gameobjects?target=" + Uri.EscapeDataString(target));
            var response = new FakeResponse();

            new GameObjectHandler().Handle(request, response);
            Assert.AreEqual(200, response.StatusCode, response.Body);
            return response.Body;
        }

        /// <summary>Reads the vector that follows a key, so the response is parsed as a client would.</summary>
        private static Vector3 Vector(string body, string key)
        {
            var start = body.IndexOf(key, StringComparison.Ordinal);
            Assert.Greater(start, -1, key + " missing from the read: " + body);
            start += key.Length;

            var end = body.IndexOf('}', start);
            Assert.Greater(end, -1, "unterminated vector for " + key + ": " + body);

            var value = new Vector3();
            foreach (var part in body.Substring(start + 1, end - start - 1).Split(','))
            {
                var pair = part.Split(':');
                var component = float.Parse(pair[1], System.Globalization.CultureInfo.InvariantCulture);
                switch (pair[0].Trim('"'))
                {
                    case "x": value.x = component; break;
                    case "y": value.y = component; break;
                    case "z": value.z = component; break;
                }
            }
            return value;
        }

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.AreEqual(expected.x, actual.x, 1e-3f, actual.ToString());
            Assert.AreEqual(expected.y, actual.y, 1e-3f, actual.ToString());
            Assert.AreEqual(expected.z, actual.z, 1e-3f, actual.ToString());
        }
    }
}

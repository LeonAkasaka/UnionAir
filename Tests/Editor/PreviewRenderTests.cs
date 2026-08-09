using System;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    internal sealed class PreviewRenderTests
    {
        [Test]
        public void Parser_AcceptsTheCompleteRequest()
        {
            var body = "{" +
                       "\"target\":{\"assetPath\":\"Assets/Character.prefab\"}," +
                       "\"focusPath\":\"Rig/Head\",\"width\":320,\"height\":180," +
                       "\"format\":\"jpeg\",\"quality\":91,\"times\":[0,0.5]," +
                       "\"view\":{\"yaw\":35,\"pitch\":15,\"distance\":4,\"fieldOfView\":40,\"padding\":0.2}," +
                       "\"background\":{\"r\":0.1,\"g\":0.2,\"b\":0.3}," +
                       "\"lighting\":{\"keyIntensity\":2,\"fillIntensity\":0.25," +
                       "\"keyColor\":{\"r\":1,\"g\":0.9,\"b\":0.8}}," +
                       "\"animation\":{\"mode\":\"parameters\",\"animatorPath\":\"Rig\"," +
                       "\"parameters\":[{\"name\":\"Pose\",\"value\":2}]}}";

            Assert.IsTrue(
                PreviewRenderRequestParser.TryParse(body, false, out var model, out var error),
                error);
            Assert.AreEqual(320, model.Width);
            Assert.AreEqual(180, model.Height);
            Assert.AreEqual("jpeg", model.Format);
            Assert.AreEqual(2, model.Times.Length);
            Assert.AreEqual("custom", model.View.Preset);
            Assert.AreEqual(35f, model.View.Yaw);
            Assert.AreEqual(4f, model.View.Distance.Value);
            Assert.AreEqual(PreviewAnimationMode.Parameters, model.Animation.Mode);
            Assert.AreEqual("Pose", model.Animation.Parameters[0].Name);
        }

        [Test]
        public void Parser_AcceptsAClipNameForImportedAnimationAssets()
        {
            var body = "{\"target\":{\"assetPath\":\"Assets/Character.prefab\"}," +
                       "\"animation\":{\"mode\":\"clip\",\"clip\":{" +
                       "\"assetPath\":\"Assets/Animations/Character.fbx\"}," +
                       "\"clipName\":\"Idle\"}}";

            Assert.IsTrue(
                PreviewRenderRequestParser.TryParse(body, false, out var model, out var error),
                error);
            Assert.AreEqual(PreviewAnimationMode.Clip, model.Animation.Mode);
            Assert.AreEqual("Idle", model.Animation.ClipName);
        }

        [TestCase("{\"target\":{},\"unknown\":true}", "Unknown field")]
        [TestCase("{\"target\":{},\"view\":{\"preset\":\"front\",\"yaw\":1}}", "either preset or yaw/pitch")]
        [TestCase("{\"target\":{},\"view\":{\"distance\":1000001}}", "no more than 1000000")]
        [TestCase("{\"target\":{},\"format\":\"gif\"}", "png")]
        [TestCase("{\"target\":{},\"times\":[]}", "between 1 and 16")]
        [TestCase("{\"target\":{},\"times\":[1000001]}", "between 0 and 1000000")]
        [TestCase("{\"target\":{},\"animation\":{\"mode\":\"state\"}}", "requires animation.state")]
        [TestCase("{\"target\":{},\"animation\":{\"mode\":\"state\",\"state\":\"Idle\",\"clipName\":\"Idle\"}}", "must match animation.mode")]
        public void Parser_RejectsInvalidContracts(string body, string expected)
        {
            Assert.IsFalse(PreviewRenderRequestParser.TryParse(body, false, out _, out var error));
            StringAssert.Contains(expected, error);
        }

        [Test]
        public void Parser_RejectsSeveralFramesForBinaryImage()
        {
            Assert.IsFalse(PreviewRenderRequestParser.TryParse(
                "{\"target\":{},\"times\":[0,1]}", true, out _, out var error));
            StringAssert.Contains("exactly one", error);
        }

        [Test]
        public void Parser_RejectsDuplicateAnimatorParameters()
        {
            var body = "{\"target\":{},\"animation\":{\"mode\":\"parameters\",\"parameters\":[" +
                       "{\"name\":\"Pose\",\"value\":1},{\"name\":\"Pose\",\"value\":2}]}}";
            Assert.IsFalse(PreviewRenderRequestParser.TryParse(body, false, out _, out var error));
            StringAssert.Contains("Duplicate Animator parameter", error);
        }

        [TestCase(16f / 9f, 0f, 0f)]
        [TestCase(9f / 16f, 0f, 0f)]
        [TestCase(1f, 47f, 23f)]
        public void ProjectedBoundsFit_ContainsAllCorners(float aspect, float yaw, float pitch)
        {
            var bounds = new Bounds(new Vector3(3f, -2f, 7f), new Vector3(4f, 9f, 2f));
            var rotation = PreviewFraming.CameraRotation(yaw, pitch);
            var distance = PreviewFraming.CalculateDistance(bounds, rotation, 37f, aspect, 0.1f);
            var cameraPosition = bounds.center - (rotation * Vector3.forward) * distance;
            var verticalTangent = Mathf.Tan(37f * 0.5f * Mathf.Deg2Rad);
            var horizontalTangent = verticalTangent * aspect;

            var extents = bounds.extents;
            for (var x = -1; x <= 1; x += 2)
            for (var y = -1; y <= 1; y += 2)
            for (var z = -1; z <= 1; z += 2)
            {
                var corner = bounds.center + new Vector3(extents.x * x, extents.y * y, extents.z * z);
                var cameraSpace = Quaternion.Inverse(rotation) * (corner - cameraPosition);
                Assert.Greater(cameraSpace.z, 0f);
                Assert.LessOrEqual(
                    Math.Abs(cameraSpace.x / (cameraSpace.z * horizontalTangent)), 0.8001f);
                Assert.LessOrEqual(
                    Math.Abs(cameraSpace.y / (cameraSpace.z * verticalTangent)), 0.8001f);
            }
        }

        [Test]
        public void Handler_RenderSceneObjectReturnsImageWithoutChangingSourceOrSelection()
        {
            var source = GameObject.CreatePrimitive(PrimitiveType.Cube);
            source.name = "UnionAirPreviewRenderTest_" + Guid.NewGuid().ToString("N");
            var previousSelection = Selection.activeObject;
            try
            {
                Selection.activeObject = source;
                var scene = source.scene;
                var dirtyBefore = scene.isDirty;
                var request = new FakeRequest("POST", "/api/previews/render")
                    .WithJsonBody("{\"target\":{\"type\":\"hierarchyPath\",\"value\":\"" +
                                  source.name + "\"},\"width\":64,\"height\":64}");
                var response = new FakeResponse();

                new PreviewRenderHandler().Handle(request, response, false);

                Assert.AreEqual(200, response.StatusCode, response.Body);
                var frames = RequestBodyReader.GetArray(response.Body, "frames");
                Assert.AreEqual(1, frames.Count);
                var bytes = Convert.FromBase64String(RequestBodyReader.GetString(frames[0], "image"));
                CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71 }, new[] { bytes[0], bytes[1], bytes[2], bytes[3] });
                Assert.AreSame(source, Selection.activeObject);
                Assert.AreEqual(dirtyBefore, scene.isDirty);
                Assert.AreEqual(0, PreviewRenderHandler.ActivePreviewCount);
            }
            finally
            {
                Selection.activeObject = previousSelection;
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void Handler_FailureAfterOpeningPreviewReleasesAdmissionAndLeavesSource()
        {
            var source = new GameObject("UnionAirPreviewNoRenderer_" + Guid.NewGuid().ToString("N"));
            try
            {
                var request = new FakeRequest("POST", "/api/previews/render")
                    .WithJsonBody("{\"target\":{\"type\":\"hierarchyPath\",\"value\":\"" +
                                  source.name + "\"}}");
                var response = new FakeResponse();

                new PreviewRenderHandler().Handle(request, response, false);

                Assert.AreEqual(422, response.StatusCode);
                StringAssert.Contains("renderer bounds", response.Body);
                Assert.IsNotNull(source);
                Assert.AreEqual(0, PreviewRenderHandler.ActivePreviewCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void Routes_ReportEveryActivityThatCanInvalidateAPreview()
        {
            UnionAirRouteRegistry.Refresh();
            UnionAirEndpointDescriptor descriptor = null;
            foreach (var candidate in UnionAirRouteRegistry.Descriptors)
                if (candidate.Path == "/api/previews/render") { descriptor = candidate; break; }

            Assert.IsNotNull(descriptor);
            Assert.AreEqual(UnionAirEndpointCategories.Read, descriptor.Category);
            var expected = UnionAirActivity.PlayMode | UnionAirActivity.TestRun |
                           UnionAirActivity.Compile | UnionAirActivity.AssetUpdate |
                           UnionAirActivity.Build | UnionAirActivity.BuildTargetSwitch;
            Assert.AreEqual(expected, descriptor.BlockedDuring);
        }
    }
}

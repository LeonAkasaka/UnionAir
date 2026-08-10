using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Drives POST .../curves against a real `.anim` asset, because the thing under test is
    /// what <see cref="AnimationClip.SetCurve"/> does to a property name -- and only SetCurve
    /// knows that. The endpoint used to answer with the names the request sent, so a clip
    /// carrying three bindings reported one, and the one it reported was a name
    /// DELETE .../curves rejects.
    /// </summary>
    internal sealed class AnimationClipCurveWriteTests
    {
        private const string Dir = "Assets/UnionAirCurveWriteTests";
        private const string ClipPath = Dir + "/Test.anim";
        private const string MaterialPath = Dir + "/Test.mat";

        private string _guid;

        [SetUp]
        public void CreateClip()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "UnionAirCurveWriteTests");

            AssetDatabase.DeleteAsset(ClipPath);
            AssetDatabase.CreateAsset(new AnimationClip(), ClipPath);
            AssetDatabase.SaveAssets();

            _guid = AssetDatabase.AssetPathToGUID(ClipPath);
        }

        [TearDown]
        public void DeleteClip()
        {
            AssetDatabase.DeleteAsset(Dir);
        }

        private FakeResponse Post(string json)
        {
            var response = new FakeResponse();
            new AnimationClipHandler().HandleAddCurves(
                new FakeRequest("POST").WithJsonBody(json), response, _guid);
            return response;
        }

        private FakeResponse Delete(string json)
        {
            var response = new FakeResponse();
            new AnimationClipHandler().HandleDeleteCurves(
                new FakeRequest("DELETE").WithJsonBody(json), response, _guid);
            return response;
        }

        private static string FloatCurve(string type, string property, string relativePath = "Hips")
            => "{\"curves\":[{\"relativePath\":\"" + relativePath + "\",\"type\":\"" + type + "\"," +
               "\"property\":\"" + property + "\"," +
               "\"keys\":[{\"time\":0.0,\"value\":0.0},{\"time\":1.0,\"value\":5.0}]}]}";

        // ── The names the response carries ───────────────────────────────────

        [Test]
        public void Post_ReportsTheStoredBindingsRatherThanTheNameItWasSent()
        {
            var body = Post(FloatCurve("Transform", "localPosition.y")).Body;

            StringAssert.Contains("\"added\":[\"m_LocalPosition.x\",\"m_LocalPosition.y\",\"m_LocalPosition.z\"]", body);
            StringAssert.DoesNotContain("\"added\":[\"localPosition.y\"]", body);
        }

        [Test]
        public void Post_ShowsWhatOneEntryWasAskedForNextToWhatItProduced()
        {
            // The expansion is Unity's and stays. What changes is that a caller who wrote one
            // axis can see the other two were written without reading the clip back.
            var body = Post(FloatCurve("Transform", "localPosition.y")).Body;

            StringAssert.Contains("\"requested\":\"localPosition.y\"", body);
            StringAssert.Contains(
                "\"bindings\":[\"m_LocalPosition.x\",\"m_LocalPosition.y\",\"m_LocalPosition.z\"]", body);
            StringAssert.Contains("\"relativePath\":\"Hips\"", body);
            StringAssert.Contains("\"type\":\"Transform\"", body);
        }

        [Test]
        public void Post_ThenDelete_RoundTripsTheNamesItReported()
        {
            // The point of reporting serialized names: a name from a write can be handed
            // straight to a removal. "localPosition.y" answered 400 here.
            Post(FloatCurve("Transform", "localPosition.y"));

            var response = Delete(
                "{\"bindings\":[{\"relativePath\":\"Hips\",\"type\":\"Transform\"," +
                "\"property\":\"m_LocalPosition.y\"}]}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"removed\":[\"m_LocalPosition.y\"]", response.Body);
        }

        [Test]
        public void Post_ReportsEulerAnglesUnderTheNameUnityStores()
        {
            // localEulerAngles becomes localEulerAnglesRaw, which is not a name
            // AnimationUtility.GetAnimatableBindings reports for Transform. Locked in here
            // because it is why the write's result cannot be predicted from the animatable
            // set, only measured.
            var body = Post(FloatCurve("Transform", "localEulerAngles.y")).Body;

            StringAssert.Contains(
                "\"bindings\":[\"localEulerAnglesRaw.x\",\"localEulerAnglesRaw.y\",\"localEulerAnglesRaw.z\"]",
                body);
        }

        [Test]
        public void Post_ExpandsALocalRotationIntoItsFourQuaternionComponents()
        {
            // Three of the four recognised groups hold three components and this one holds
            // four. The documentation said "position, scale, and euler angles" and left this
            // group out entirely.
            var body = Post(FloatCurve("Transform", "localRotation.x")).Body;

            StringAssert.Contains(
                "\"bindings\":[\"m_LocalRotation.x\",\"m_LocalRotation.y\",\"m_LocalRotation.z\"," +
                "\"m_LocalRotation.w\"]", body);
        }

        [Test]
        public void Post_DoesNotTranslateScriptingNamesToSerializedOnes()
        {
            // The rewriting is four names on Transform, not a general mapping from the C#
            // property to the serialized field: Light.intensity stays "intensity" rather than
            // becoming the "m_Intensity" that would have worked, and Transform.position --
            // which is a real scripting property -- is not the local position group.
            StringAssert.Contains("\"bindings\":[\"intensity\"]",
                Post(FloatCurve("Light", "intensity", "Lamp")).Body);
            StringAssert.Contains("\"bindings\":[\"position.y\"]",
                Post(FloatCurve("Transform", "position.y")).Body);
        }

        [Test]
        public void Post_RewritesOnlyWhenTheTypeIsTransform()
        {
            // The group is keyed on the type as well as the name, so the same name on another
            // component is one binding stored verbatim.
            StringAssert.Contains("\"bindings\":[\"m_LocalPosition.y\"]",
                Post(FloatCurve("Light", "m_LocalPosition.y", "Lamp")).Body);
        }

        [Test]
        public void Post_LeavesAScalarPropertyAsTheSingleBindingItIs()
        {
            // Only Transform's vector properties expand, so the response must not imply a
            // group where there is none.
            var body = Post(FloatCurve("Light", "m_Intensity", "Lamp")).Body;

            StringAssert.Contains("\"added\":[\"m_Intensity\"]", body);
            StringAssert.Contains("\"bindings\":[\"m_Intensity\"]", body);
        }

        [Test]
        public void Post_ReportsAColourChannelAsOneBindingRatherThanThree()
        {
            // A name shaped like an expanded one that is not: a client cannot predict the
            // behavior from the shape, which is why the response has to state it.
            var body = Post(FloatCurve("Light", "m_Color.r", "Lamp")).Body;

            StringAssert.Contains("\"added\":[\"m_Color.r\"]", body);
        }

        [Test]
        public void Post_ReplacingAnExistingCurveStillReportsItsBindings()
        {
            // A replacement creates no binding. Reporting the difference between before and
            // after would answer "nothing was written" for a write that happened.
            Post(FloatCurve("Transform", "localPosition.y"));
            var response = Post(FloatCurve("Transform", "localPosition.y"));

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains(
                "\"added\":[\"m_LocalPosition.x\",\"m_LocalPosition.y\",\"m_LocalPosition.z\"]", response.Body);
        }

        [Test]
        public void Post_KeepsTheSameNameOnTwoPathsAsTwoBindings()
        {
            // Deduplication is by binding, not by name: one name on two paths is two curves.
            var body = Post(
                "{\"curves\":[" +
                "{\"relativePath\":\"Hips\",\"type\":\"Light\",\"property\":\"m_Intensity\"," +
                "\"keys\":[{\"time\":0.0,\"value\":0.0}]}," +
                "{\"relativePath\":\"Head\",\"type\":\"Light\",\"property\":\"m_Intensity\"," +
                "\"keys\":[{\"time\":0.0,\"value\":0.0}]}]}").Body;

            StringAssert.Contains("\"added\":[\"m_Intensity\",\"m_Intensity\"]", body);
        }

        [Test]
        public void Post_DoesNotCheckThePropertyNameAgainstTheType()
        {
            // Stated as a test rather than left as an accident. SetCurve accepts any name and
            // stores it, and the endpoint deliberately does not second-guess it: the
            // animatable set of a type does not contain localEulerAnglesRaw, blendShape, or
            // material names, so using it as a gate would reject working curves. What the
            // response can promise is that the name it reports is the one on the clip.
            var body = Post(FloatCurve("Transform", "bogusProperty.y")).Body;

            StringAssert.Contains("\"added\":[\"bogusProperty.y\"]", body);
            StringAssert.Contains("\"errors\":[]", body);
        }

        // ── Object reference curves ──────────────────────────────────────────

        [Test]
        public void Post_ReportsAnObjectReferenceCurveUnderItsOwnEntry()
        {
            AssetDatabase.CreateAsset(new Material(Shader.Find("Unlit/Color")), MaterialPath);
            AssetDatabase.SaveAssets();
            var materialGuid = AssetDatabase.AssetPathToGUID(MaterialPath);

            var body = Post(
                "{\"objectReferenceCurves\":[{\"relativePath\":\"Body\",\"type\":\"MeshRenderer\"," +
                "\"property\":\"m_Materials.Array.data[0]\"," +
                "\"keys\":[{\"time\":0.0,\"guid\":\"" + materialGuid + "\"}]}]}").Body;

            StringAssert.Contains("\"addedObjectReference\":[\"m_Materials.Array.data[0]\"]", body);
            StringAssert.Contains("\"addedFloat\":[]", body);
            StringAssert.Contains("\"requested\":\"m_Materials.Array.data[0]\"", body);
        }

        // ── Failures ─────────────────────────────────────────────────────────

        [Test]
        public void Post_StillRejectsAnUnresolvableType()
        {
            var response = Post(FloatCurve("NoSuchType", "m_Intensity", "Lamp"));

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("Unknown type: NoSuchType", response.Body);
            StringAssert.Contains("\"added\":[]", response.Body);
        }

        [Test]
        public void Post_ReportsOneBadEntryWithoutLosingTheGoodOne()
        {
            var response = Post(
                "{\"curves\":[" +
                "{\"relativePath\":\"Lamp\",\"type\":\"Light\",\"property\":\"m_Intensity\"," +
                "\"keys\":[{\"time\":0.0,\"value\":0.0}]}," +
                "{\"relativePath\":\"Lamp\",\"type\":\"NoSuchType\",\"property\":\"m_Intensity\"," +
                "\"keys\":[{\"time\":0.0,\"value\":0.0}]}]}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"added\":[\"m_Intensity\"]", response.Body);
            StringAssert.Contains("Unknown type: NoSuchType", response.Body);
        }
    }
}

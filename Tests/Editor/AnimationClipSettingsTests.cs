using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Drives the clip settings and event endpoints against a real `.anim` asset.
    ///
    /// The read used to describe a clip with five fields, none of which answered the
    /// question the Animation Inspector puts at the top: does this loop. That is
    /// <c>settings.loopTime</c>, and <c>wrapMode</c> -- the one field the read did carry --
    /// is a different thing entirely.
    /// </summary>
    internal sealed class AnimationClipSettingsTests
    {
        private const string Dir = "Assets/UnionAirClipSettingsTests";
        private const string ClipPath = Dir + "/Test.anim";

        private string _guid;

        [SetUp]
        public void CreateClip()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "UnionAirClipSettingsTests");

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

        private FakeResponse Read()
            => Send(new FakeRequest("GET"), (h, rq, rs) => h.HandleRead(rq, rs, _guid));

        private FakeResponse Patch(string json)
            => Send(new FakeRequest("PATCH").WithJsonBody(json), (h, rq, rs) => h.HandleUpdate(rq, rs, _guid));

        private FakeResponse PostEvents(string json)
            => Send(new FakeRequest("POST").WithJsonBody(json), (h, rq, rs) => h.HandleSetEvents(rq, rs, _guid));

        private FakeResponse DeleteEvents()
            => Send(new FakeRequest("DELETE"), (h, rq, rs) => h.HandleDeleteEvents(rq, rs, _guid));

        private static FakeResponse Send(
            FakeRequest request,
            System.Action<AnimationClipHandler, FakeRequest, FakeResponse> call)
        {
            var response = new FakeResponse();
            call(new AnimationClipHandler(), request, response);
            return response;
        }

        private static AnimationClip Clip() => AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);

        // ── Read ─────────────────────────────────────────────────────────────

        [Test]
        public void Read_CarriesTheSettingsAndEventsItUsedToOmit()
        {
            var body = Read().Body;

            StringAssert.Contains("\"settings\":{", body);
            StringAssert.Contains("\"loopTime\":", body);
            StringAssert.Contains("\"cycleOffset\":", body);
            StringAssert.Contains("\"stopTime\":", body);
            StringAssert.Contains("\"events\":[", body);
        }

        [Test]
        public void Read_SaysWhoOwnsTheClip()
        {
            var body = Read().Body;

            StringAssert.Contains("\"imported\":false", body);
            StringAssert.Contains("\"writable\":true", body);
            StringAssert.Contains("\"importer\":null", body);
            StringAssert.Contains("\"name\":\"Test\"", body);
            StringAssert.Contains("\"clipsAtPath\":1", body);
        }

        // ── Settings ─────────────────────────────────────────────────────────

        [Test]
        public void SettingsRoundTrip()
        {
            var response = Patch("{\"settings\":{\"loopTime\":true,\"cycleOffset\":0.25,\"mirror\":true,\"stopTime\":2.5}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            var settings = AnimationUtility.GetAnimationClipSettings(Clip());
            Assert.IsTrue(settings.loopTime);
            Assert.AreEqual(0.25f, settings.cycleOffset);
            Assert.IsTrue(settings.mirror);
            Assert.AreEqual(2.5f, settings.stopTime);
            StringAssert.Contains("\"settings.loopTime\"", response.Body);
        }

        [Test]
        public void AnOmittedSettingIsLeftAlone()
        {
            Patch("{\"settings\":{\"loopTime\":true,\"cycleOffset\":0.25}}");

            Patch("{\"settings\":{\"mirror\":true}}");

            var settings = AnimationUtility.GetAnimationClipSettings(Clip());
            Assert.IsTrue(settings.loopTime, "an omitted setting must survive the next patch");
            Assert.AreEqual(0.25f, settings.cycleOffset);
            Assert.IsTrue(settings.mirror);
        }

        [Test]
        public void FrameRateAndWrapModeRoundTrip()
        {
            Assert.AreEqual(200, Patch("{\"frameRate\":30.0,\"wrapMode\":\"Loop\"}").StatusCode);

            Assert.AreEqual(30f, Clip().frameRate);
            Assert.AreEqual(WrapMode.Loop, Clip().wrapMode);
        }

        [Test]
        public void WrapModeIsNotLoopTime()
        {
            // The reason the read reports both: they are different fields with different
            // meanings, and wrapMode is the one that does not answer "does this loop".
            Patch("{\"settings\":{\"loopTime\":true}}");

            Assert.AreEqual(WrapMode.Default, Clip().wrapMode);
            Assert.IsTrue(AnimationUtility.GetAnimationClipSettings(Clip()).loopTime);
            StringAssert.Contains("\"wrapMode\":\"Default\"", Read().Body);
            StringAssert.Contains("\"loopTime\":true", Read().Body);
        }

        [TestCase("{\"frameRate\":\"fast\"}", "frameRate")]
        [TestCase("{\"frameRate\":0}", "greater than zero")]
        [TestCase("{\"wrapMode\":\"Sometimes\"}", "Unknown wrapMode")]
        [TestCase("{\"settings\":{\"loopTime\":\"yes\"}}", "settings.loopTime")]
        [TestCase("{\"settings\":{\"loopTyme\":true}}", "loopTyme")]
        public void AMalformedFieldIsRejectedAndNothingIsApplied(string json, string expected)
        {
            Patch("{\"frameRate\":30.0,\"settings\":{\"cycleOffset\":0.5}}");

            var response = Patch(json);

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains(expected, response.Body);
            Assert.AreEqual(30f, Clip().frameRate);
            Assert.AreEqual(0.5f, AnimationUtility.GetAnimationClipSettings(Clip()).cycleOffset);
        }

        [Test]
        public void AnUnknownTopLevelFieldIsRejected()
        {
            var response = Patch("{\"frameRate\":30.0,\"loopTime\":true}");

            Assert.AreEqual(400, response.StatusCode);
            // loopTime is a settings field, not a clip field, and answering "unknown" is
            // what tells a client it nested it wrongly.
            StringAssert.Contains("loopTime", response.Body);
        }

        // ── Events ───────────────────────────────────────────────────────────

        [Test]
        public void EventsRoundTrip()
        {
            var response = PostEvents(
                "{\"events\":[{\"time\":0.25,\"functionName\":\"Footstep\",\"stringParameter\":\"left\"}," +
                "{\"time\":0.75,\"functionName\":\"Footstep\",\"stringParameter\":\"right\",\"messageOptions\":\"DontRequireReceiver\"}]}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            var events = AnimationUtility.GetAnimationEvents(Clip());
            Assert.AreEqual(2, events.Length);
            Assert.AreEqual(0.25f, events[0].time);
            Assert.AreEqual("Footstep", events[0].functionName);
            Assert.AreEqual("left", events[0].stringParameter);
            Assert.AreEqual(SendMessageOptions.DontRequireReceiver, events[1].messageOptions);
            StringAssert.Contains("\"events\":[{\"time\":0.25", Read().Body);
        }

        [Test]
        public void MessageOptionsDefaultsToRequireReceiver()
        {
            // Unity's default rather than this endpoint's choice: SendMessageOptions
            // .RequireReceiver is 0, so an omitted value is what a new AnimationEvent holds.
            PostEvents("{\"events\":[{\"time\":0.1,\"functionName\":\"Hit\"}]}");

            Assert.AreEqual(SendMessageOptions.RequireReceiver,
                AnimationUtility.GetAnimationEvents(Clip())[0].messageOptions);
        }

        [Test]
        public void TheEventArrayReplacesRatherThanAppends()
        {
            PostEvents("{\"events\":[{\"time\":0.1,\"functionName\":\"A\"}]}");
            PostEvents("{\"events\":[{\"time\":0.2,\"functionName\":\"B\"}]}");

            var events = AnimationUtility.GetAnimationEvents(Clip());
            Assert.AreEqual(1, events.Length);
            Assert.AreEqual("B", events[0].functionName);
        }

        [Test]
        public void AnEmptyArrayClearsTheEvents()
        {
            PostEvents("{\"events\":[{\"time\":0.1,\"functionName\":\"A\"}]}");

            Assert.AreEqual(200, PostEvents("{\"events\":[]}").StatusCode);
            Assert.AreEqual(0, AnimationUtility.GetAnimationEvents(Clip()).Length);
        }

        [Test]
        public void DeleteClearsTheEventsAndSaysHowMany()
        {
            PostEvents("{\"events\":[{\"time\":0.1,\"functionName\":\"A\"},{\"time\":0.2,\"functionName\":\"B\"}]}");

            var response = DeleteEvents();

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"removed\":2", response.Body);
            Assert.AreEqual(0, AnimationUtility.GetAnimationEvents(Clip()).Length);
        }

        [TestCase("{\"events\":[{\"functionName\":\"A\"}]}", "time")]
        [TestCase("{\"events\":[{\"time\":0.1}]}", "functionName")]
        [TestCase("{\"events\":[{\"time\":0.1,\"functionName\":\"A\",\"messageOptions\":\"Maybe\"}]}", "messageOptions")]
        [TestCase("{\"events\":[{\"time\":0.1,\"functionName\":\"A\",\"tyme\":1}]}", "tyme")]
        public void AMalformedEventReplacesNothing(string json, string expected)
        {
            PostEvents("{\"events\":[{\"time\":0.5,\"functionName\":\"Kept\"}]}");

            var response = PostEvents(json);

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains(expected, response.Body);
            var events = AnimationUtility.GetAnimationEvents(Clip());
            Assert.AreEqual(1, events.Length);
            Assert.AreEqual("Kept", events[0].functionName, "a refused request must replace nothing");
        }

        [Test]
        public void AnEventNamingAMissingAssetReplacesNothing()
        {
            // Every element is resolved before any is written, so a list whose second entry
            // is unresolvable does not leave the first one applied.
            PostEvents("{\"events\":[{\"time\":0.5,\"functionName\":\"Kept\"}]}");

            var response = PostEvents(
                "{\"events\":[{\"time\":0.1,\"functionName\":\"A\"}," +
                "{\"time\":0.2,\"functionName\":\"B\",\"objectReferenceParameter\":{\"guid\":\"0000\"}}]}");

            Assert.AreEqual(404, response.StatusCode, response.Body);
            Assert.AreEqual("Kept", AnimationUtility.GetAnimationEvents(Clip())[0].functionName);
        }

        [Test]
        public void OmittingTheEventsFieldIsRejectedRatherThanTreatedAsEmpty()
        {
            PostEvents("{\"events\":[{\"time\":0.5,\"functionName\":\"Kept\"}]}");

            var response = PostEvents("{}");

            Assert.AreEqual(400, response.StatusCode);
            Assert.AreEqual(1, AnimationUtility.GetAnimationEvents(Clip()).Length,
                "an absent array must not read as a request to clear");
        }
    }
}

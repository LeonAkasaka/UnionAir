using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

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
            LogAssert.ignoreFailingMessages = false;
            AssetDatabase.DeleteAsset(Dir);
        }

        /// <summary>
        /// Lets a test provoke the Editor-console error Unity logs for a group name that
        /// carries no usable component: "Can't assign curve because m_LocalPosition is not a
        /// valid Transform property". It is the same condition the endpoint now reports, and
        /// it is logged once per SetCurve -- so twice per entry, since the probe writes the
        /// entry as well. Ignoring rather than counting keeps the tests off that detail.
        ///
        /// That Unity logs it at all is worth knowing: the failure was never silent to a
        /// human watching the console, only to a client reading the response.
        /// </summary>
        private static void AllowUnitysInvalidPropertyLog()
        {
            LogAssert.ignoreFailingMessages = true;
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
            => "{\"curves\":[" + Entry(type, property, relativePath, 0.0f, 5.0f) + "]}";

        /// <summary>One entry of a float curve, keyed at t=0 and t=1.</summary>
        private static string Entry(
            string type, string property, string relativePath, float at0, float at1)
            => "{\"relativePath\":\"" + relativePath + "\",\"type\":\"" + type + "\"," +
               "\"property\":\"" + property + "\",\"keys\":[" +
               "{\"time\":0.0,\"value\":" + at0.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "}," +
               "{\"time\":1.0,\"value\":" + at1.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "}]}";

        /// <summary>The property names and key values the clip holds at a path, in binding order.</summary>
        private static string CurveDump(string relativePath)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            var text = "";
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path != relativePath) continue;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                text += binding.propertyName + "=[";
                foreach (var key in curve.keys) text += "(" + key.time + "," + key.value + ")";
                text += "] ";
            }
            return text;
        }

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

        // ── A group write that stored none of its keys ───────────────────────

        [Test]
        public void Post_RejectsAGroupNameCarryingNoComponentSuffix()
        {
            // The prefix selects the group and the suffix selects which component carries the
            // keys. With no suffix the keys have nowhere to land, and the group is created
            // holding none of them. This answered 200 with three bindings and no error.
            AllowUnitysInvalidPropertyLog();

            var response = Post(FloatCurve("Transform", "m_LocalPosition"));

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("stored none of its keys", response.Body);
            StringAssert.Contains("m_LocalPosition.x, m_LocalPosition.y, m_LocalPosition.z", response.Body);
            StringAssert.Contains("m_LocalPosition.x=[] m_LocalPosition.y=[] m_LocalPosition.z=[]",
                CurveDump("Hips"));
        }

        [Test]
        public void Post_RejectsASuffixThatNamesNoComponentOfTheGroup()
        {
            // The plausible spelling of the two: the quaternion group does have a .w, so a
            // caller moving between the two groups writes it on position as well.
            AllowUnitysInvalidPropertyLog();

            var response = Post(FloatCurve("Transform", "m_LocalPosition.w"));

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("stored none of its keys", response.Body);
        }

        [Test]
        public void Post_RejectsAGroupWriteThatStoredNothingEvenWhenTheGroupAlreadyHasKeys()
        {
            // The check has to read the probe rather than the saved clip. When the group is
            // already populated this write is a complete no-op -- the keys that were there
            // survive untouched -- so a check made against the clip afterwards finds curves
            // carrying keys and passes, while the keys this request sent were still dropped.
            // This is the worse of the two shapes: the caller believes it replaced the curve.
            Post("{\"curves\":[" + Entry("Transform", "m_LocalPosition.y", "Hips", 7f, 9f) + "]}");

            AllowUnitysInvalidPropertyLog();
            var response = Post("{\"curves\":[" + Entry("Transform", "m_LocalPosition", "Hips", 1f, 2f) + "]}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("stored none of its keys", response.Body);
            StringAssert.Contains("m_LocalPosition.y=[(0,7)(1,9)]", CurveDump("Hips"));
        }

        [Test]
        public void Post_LeavesAnOrdinaryComponentWriteUnreported()
        {
            // The siblings of a real component write receive a constant curve carrying keys,
            // not an empty one, so a valid group write never looks like the failure above.
            var response = Post(FloatCurve("Transform", "localPosition.y"));

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"errors\":[]", response.Body);
        }

        [Test]
        public void Post_KeepsAGoodEntryWhenAnotherStoredNothing()
        {
            // One failing entry does not sink the request, the same way an unresolvable type
            // does not: the status turns on whether any entry stored what it was asked to.
            AllowUnitysInvalidPropertyLog();

            var response = Post(
                "{\"curves\":[" +
                Entry("Light", "m_Intensity", "Lamp", 0f, 1f) + "," +
                Entry("Transform", "m_LocalPosition", "Hips", 1f, 2f) + "]}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"added\":[\"m_Intensity\"", response.Body);
            StringAssert.Contains("stored none of its keys", response.Body);
        }

        // ── A rotation that is not a unit quaternion ─────────────────────────

        [Test]
        public void Post_WarnsWhenOneEntryLeavesTheQuaternionNonUnit()
        {
            // SetCurve fills w with 0, and (0, y, 0, 0) normalizes to a half turn whatever y
            // holds -- so a request asking for 90 degrees plays back 180. The write stored a
            // curve and the curve is applied, so this is a warning, not an error.
            var response = Post(
                "{\"curves\":[" + Entry("Transform", "localRotation.y", "Hips", 0f, 0.7071068f) + "]}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"errors\":[]", response.Body);
            StringAssert.Contains("is not a unit quaternion", response.Body);
            StringAssert.Contains("localEulerAngles.*", response.Body);
        }

        [Test]
        public void Post_DoesNotWarnWhenAllFourComponentsArriveInOneRequest()
        {
            // The check reads the clip once every entry has been written, so the four entries
            // are judged on the quaternion they add up to rather than one at a time.
            var response = Post(
                "{\"curves\":[" +
                Entry("Transform", "m_LocalRotation.x", "Hips", 0f, 0f) + "," +
                Entry("Transform", "m_LocalRotation.y", "Hips", 0f, 0.7071068f) + "," +
                Entry("Transform", "m_LocalRotation.z", "Hips", 0f, 0f) + "," +
                Entry("Transform", "m_LocalRotation.w", "Hips", 1f, 0.7071068f) + "]}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"warnings\":[]", response.Body);
        }

        [Test]
        public void Post_StopsWarningOnceAQuaternionIsCompletedAcrossRequests()
        {
            // A later write to another component of the group replaces that component and
            // leaves the ones already carrying curves alone, so a caller may fill the
            // quaternion in over several requests. The intermediate state really is a clip
            // that plays back wrong and is warned about; the completed one is not.
            var first = Post(
                "{\"curves\":[" + Entry("Transform", "m_LocalRotation.y", "Hips", 0f, 0.7071068f) + "]}");
            StringAssert.Contains("is not a unit quaternion", first.Body);

            var second = Post(
                "{\"curves\":[" + Entry("Transform", "m_LocalRotation.w", "Hips", 1f, 0.7071068f) + "]}");

            Assert.AreEqual(200, second.StatusCode, second.Body);
            StringAssert.Contains("\"warnings\":[]", second.Body);
        }

        [Test]
        public void Post_WarnsWhenAComponentIsKeyedWhereTheOthersAreNot()
        {
            // Not only the w=0 case. A group write resamples every component onto the union
            // of the group's key times, so a component keyed at 0 and 2 receives an
            // interpolated value at 1 -- here 0.5 against y's 0.7071, which is a length of
            // 0.866 and a rotation of 109.5 degrees rather than the 90 that was asked for.
            var response = Post(
                "{\"curves\":[" +
                "{\"relativePath\":\"Hips\",\"type\":\"Transform\",\"property\":\"m_LocalRotation.y\"," +
                "\"keys\":[{\"time\":0.0,\"value\":0.0},{\"time\":1.0,\"value\":0.7071068},{\"time\":2.0,\"value\":1.0}]}," +
                "{\"relativePath\":\"Hips\",\"type\":\"Transform\",\"property\":\"m_LocalRotation.w\"," +
                "\"keys\":[{\"time\":0.0,\"value\":1.0},{\"time\":2.0,\"value\":0.0}]}]}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("is not a unit quaternion", response.Body);
            StringAssert.Contains("at t=1", response.Body);
        }

        [Test]
        public void Post_DoesNotWarnAboutEulerAngles()
        {
            // Euler is the group that survives a single entry: the components it does not
            // name default to 0, which is the identity there.
            var response = Post(FloatCurve("Transform", "localEulerAngles.y"));

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"warnings\":[]", response.Body);
        }

        [Test]
        public void Post_DoesNotTreatARotationNameOnAnotherTypeAsAQuaternion()
        {
            // The check is keyed on the four bindings the entry produced, not on the name it
            // sent, so the same name on a type that does not expand is one ordinary curve.
            var response = Post(FloatCurve("Light", "m_LocalRotation.y", "Lamp"));

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"warnings\":[]", response.Body);
        }

        [Test]
        public void Post_CarriesAnEmptyWarningsArrayOnAnOrdinaryWrite()
        {
            // Absent-when-empty would make the field impossible to read without a null check
            // on every response. Every other array on this endpoint is always present.
            StringAssert.Contains("\"warnings\":[]", Post(FloatCurve("Light", "m_Intensity", "Lamp")).Body);
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

using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Drives the state endpoints against a real controller asset. What matters about the
    /// state settings is that each one lands on the field it names and that the others are
    /// left alone -- a state write that quietly resets Write Defaults or a parameter
    /// override changes how the controller plays without saying so.
    /// </summary>
    internal sealed class AnimatorStateSettingsTests
    {
        private const string Dir = "Assets/UnionAirStateTests";
        private const string ControllerPath = Dir + "/Test.controller";

        private AnimatorController _controller;
        private string _guid;

        [SetUp]
        public void CreateController()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "UnionAirStateTests");

            AssetDatabase.DeleteAsset(ControllerPath);

            _controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            _controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            _controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            AssetDatabase.SaveAssets();

            _guid = AssetDatabase.AssetPathToGUID(ControllerPath);
        }

        [TearDown]
        public void DeleteController()
        {
            AssetDatabase.DeleteAsset(Dir);
        }

        private FakeResponse Post(string json)
            => Send(new FakeRequest("POST").WithJsonBody(json), (h, rq, rs) => h.HandleAddState(rq, rs, _guid));

        private FakeResponse Patch(string json)
            => Send(new FakeRequest("PATCH").WithJsonBody(json), (h, rq, rs) => h.HandleUpdateState(rq, rs, _guid));

        private FakeResponse Read()
            => Send(new FakeRequest("GET"), (h, rq, rs) => h.HandleRead(rq, rs, _guid));

        private static FakeResponse Send(
            FakeRequest request,
            System.Action<AnimatorControllerHandler, FakeRequest, FakeResponse> call)
        {
            var response = new FakeResponse();
            call(new AnimatorControllerHandler(), request, response);
            return response;
        }

        private static AnimatorController Reloaded()
            => AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

        private static AnimatorState State(string name)
        {
            foreach (var child in Reloaded().layers[0].stateMachine.states)
                if (child.state.name == name) return child.state;
            return null;
        }

        private static Vector3 PositionOf(string name)
        {
            foreach (var child in Reloaded().layers[0].stateMachine.states)
                if (child.state.name == name) return child.position;
            return Vector3.zero;
        }

        // ── Read ─────────────────────────────────────────────────────────────

        [Test]
        public void Read_CarriesEverySettingItUsedToOmit()
        {
            Post("{\"name\":\"Idle\"}");

            var body = Read().Body;

            StringAssert.Contains("\"tag\":", body);
            StringAssert.Contains("\"writeDefaultValues\":", body);
            StringAssert.Contains("\"iKOnFeet\":", body);
            StringAssert.Contains("\"mirror\":", body);
            StringAssert.Contains("\"cycleOffset\":", body);
            StringAssert.Contains("\"position\":{\"x\":", body);
            StringAssert.Contains("\"behaviours\":[]", body);
            foreach (var field in AnimatorStateRules.ParameterFields)
            {
                StringAssert.Contains("\"" + field + "\":", body);
                StringAssert.Contains("\"" + AnimatorStateRules.ActiveFieldFor(field) + "\":", body);
            }
        }

        [Test]
        public void Read_ReportsAParameterNameEvenWhileItsOverrideIsInactive()
        {
            // Folding an inactive name to an empty string would discard something the asset
            // holds, and a client could not reproduce the state from the response.
            Post("{\"name\":\"Idle\",\"speedParameter\":\"Speed\",\"speedParameterActive\":false}");

            StringAssert.Contains("\"speedParameter\":\"Speed\",\"speedParameterActive\":false", Read().Body);
        }

        [Test]
        public void Read_NamesTheBehavioursAttachedToAState()
        {
            // A state that runs script on entry was previously indistinguishable from one
            // that does not.
            Post("{\"name\":\"Idle\"}");

            // Assigned rather than added through AnimatorState.AddStateMachineBehaviour,
            // which was measured to attach nothing from this assembly: the call makes the
            // behaviour a sub-asset of the controller, and this assembly is Editor-only, so
            // the same call with a type from a runtime assembly attaches as expected. What
            // is under test is the serializer, and it reads the same array either way; the
            // genuinely attached case was verified live against a controller in the test
            // project, which reported ["Issue68Behaviour"].
            var behaviour = ScriptableObject.CreateInstance<UnionAirProbeStateBehaviour>();
            var state = State("Idle");
            state.behaviours = new StateMachineBehaviour[] { behaviour };

            Assert.AreEqual(1, state.behaviours.Length, "the probe behaviour did not attach");
            StringAssert.Contains("\"behaviours\":[\"UnionAirProbeStateBehaviour\"]", Read().Body);
            Object.DestroyImmediate(behaviour);
        }

        // ── Write ────────────────────────────────────────────────────────────

        [Test]
        public void Post_CreatesAStateFullyFormed()
        {
            var response = Post("{\"name\":\"Idle\",\"tag\":\"Locomotion\",\"writeDefaultValues\":false," +
                                "\"iKOnFeet\":true,\"mirror\":true,\"cycleOffset\":0.25,\"speed\":1.5," +
                                "\"speedParameter\":\"Speed\",\"speedParameterActive\":true," +
                                "\"position\":{\"x\":300,\"y\":120}}");

            Assert.AreEqual(201, response.StatusCode, response.Body);
            var state = State("Idle");
            Assert.AreEqual("Locomotion", state.tag);
            Assert.IsFalse(state.writeDefaultValues);
            Assert.IsTrue(state.iKOnFeet);
            Assert.IsTrue(state.mirror);
            Assert.AreEqual(0.25f, state.cycleOffset);
            Assert.AreEqual(1.5f, state.speed);
            Assert.AreEqual("Speed", state.speedParameter);
            Assert.IsTrue(state.speedParameterActive);
            Assert.AreEqual(new Vector2(300f, 120f), (Vector2)PositionOf("Idle"));
        }

        [Test]
        public void Patch_SetsOneFieldAndLeavesTheRestAlone()
        {
            Post("{\"name\":\"Idle\",\"tag\":\"Locomotion\",\"writeDefaultValues\":false,\"speed\":1.5}");

            Assert.AreEqual(200, Patch("{\"name\":\"Idle\",\"cycleOffset\":0.75}").StatusCode);

            var state = State("Idle");
            Assert.AreEqual(0.75f, state.cycleOffset);
            Assert.AreEqual("Locomotion", state.tag);
            Assert.IsFalse(state.writeDefaultValues);
            Assert.AreEqual(1.5f, state.speed);
        }

        [Test]
        public void WriteDefaultValuesRoundTrips()
        {
            Post("{\"name\":\"Idle\",\"writeDefaultValues\":false}");
            Assert.IsFalse(State("Idle").writeDefaultValues);
            StringAssert.Contains("\"writeDefaultValues\":false", Read().Body);

            Patch("{\"name\":\"Idle\",\"writeDefaultValues\":true}");
            Assert.IsTrue(State("Idle").writeDefaultValues);
            StringAssert.Contains("\"writeDefaultValues\":true", Read().Body);
        }

        [Test]
        public void PositionRoundTripsAndKeepsTheUnusedZ()
        {
            // The position lives on the ChildAnimatorState struct, so writing it means
            // assigning the whole array back. z is not part of the graph and not part of the
            // response, and nothing here has grounds to zero it.
            Post("{\"name\":\"Idle\"}");
            var states = Reloaded().layers[0].stateMachine.states;
            states[0].position = new Vector3(0f, 0f, 7f);
            Reloaded().layers[0].stateMachine.states = states;

            Assert.AreEqual(200, Patch("{\"name\":\"Idle\",\"position\":{\"x\":300,\"y\":120}}").StatusCode);

            var position = PositionOf("Idle");
            Assert.AreEqual(300f, position.x);
            Assert.AreEqual(120f, position.y);
            Assert.AreEqual(7f, position.z, "z is not the graph's and must not be zeroed");
            StringAssert.Contains("\"position\":{\"x\":300,\"y\":120}", Read().Body);
        }

        // ── Rejections ───────────────────────────────────────────────────────

        [Test]
        public void AParameterThatDoesNotExistIsRejectedAndNeitherHalfIsWritten()
        {
            Post("{\"name\":\"Idle\"}");

            var response = Patch("{\"name\":\"Idle\",\"timeParameter\":\"Nope\",\"timeParameterActive\":true,\"tag\":\"Kept\"}");

            Assert.AreEqual(400, response.StatusCode);
            StringAssert.Contains("Nope", response.Body);
            var state = State("Idle");
            Assert.AreEqual("", state.timeParameter);
            Assert.IsFalse(state.timeParameterActive, "activating an override on a missing parameter must not land");
            Assert.AreEqual("", state.tag, "the whole request must be refused, not half of it");
        }

        [Test]
        public void ActivatingAnOverrideWithNoNameAnywhereIsRejected()
        {
            // Checking only the halves the request carries let this through: neither
            // "activate" nor an absent name looks wrong alone, and the pair they leave
            // behind is an override that drives nothing.
            Post("{\"name\":\"Idle\"}");

            var response = Patch("{\"name\":\"Idle\",\"speedParameterActive\":true}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("drives nothing", response.Body);
            Assert.IsFalse(State("Idle").speedParameterActive);
        }

        [Test]
        public void ActivatingAnOverrideWithAnExplicitlyEmptyNameIsRejected()
        {
            Post("{\"name\":\"Idle\"}");

            var response = Patch("{\"name\":\"Idle\",\"mirrorParameter\":\"\",\"mirrorParameterActive\":true}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            Assert.IsFalse(State("Idle").mirrorParameterActive);
            Assert.AreEqual("", State("Idle").mirrorParameter);
        }

        [Test]
        public void ClearingTheNameOfAnActiveOverrideIsRejected()
        {
            // The same broken pair reached from the other side.
            Post("{\"name\":\"Idle\",\"timeParameter\":\"Speed\",\"timeParameterActive\":true}");

            var response = Patch("{\"name\":\"Idle\",\"timeParameter\":\"\"}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            Assert.AreEqual("Speed", State("Idle").timeParameter);
            Assert.IsTrue(State("Idle").timeParameterActive);
        }

        [Test]
        public void ActivatingAnOverrideTheStateAlreadyNamesIsAccepted()
        {
            // The name does not have to be resent to turn the override on.
            Post("{\"name\":\"Idle\",\"speedParameter\":\"Speed\",\"speedParameterActive\":false}");

            Assert.AreEqual(200, Patch("{\"name\":\"Idle\",\"speedParameterActive\":true}").StatusCode);
            Assert.IsTrue(State("Idle").speedParameterActive);
            Assert.AreEqual("Speed", State("Idle").speedParameter);
        }

        [Test]
        public void ADormantOverrideNamingADeletedParameterDoesNotBlockAnUnrelatedPatch()
        {
            // Re-checking a name the request did not send would make the state unwritable
            // rather than repairable.
            Post("{\"name\":\"Idle\",\"speedParameter\":\"Speed\",\"speedParameterActive\":false}");
            var controller = Reloaded();
            foreach (var p in controller.parameters)
                if (p.name == "Speed") { controller.RemoveParameter(p); break; }
            AssetDatabase.SaveAssets();

            Assert.AreEqual(200, Patch("{\"name\":\"Idle\",\"tag\":\"Still writable\"}").StatusCode);
            Assert.AreEqual("Still writable", State("Idle").tag);
        }

        [Test]
        public void AnEmptyParameterNameClearsTheOverrideWithoutALookup()
        {
            Post("{\"name\":\"Idle\",\"speedParameter\":\"Speed\",\"speedParameterActive\":true}");

            Assert.AreEqual(200, Patch("{\"name\":\"Idle\",\"speedParameter\":\"\",\"speedParameterActive\":false}").StatusCode);
            Assert.AreEqual("", State("Idle").speedParameter);
            Assert.IsFalse(State("Idle").speedParameterActive);
        }

        [Test]
        public void Post_AppliesNothingWhenASettingIsRejected()
        {
            var response = Post("{\"name\":\"Ghost\",\"speedParameter\":\"Nope\"}");

            Assert.AreEqual(400, response.StatusCode);
            Assert.AreEqual(0, Reloaded().layers[0].stateMachine.states.Length,
                "a refused create must leave no state behind");
        }

        [TestCase("\"speed\":\"fast\"", "speed")]
        [TestCase("\"writeDefaultValues\":\"no\"", "writeDefaultValues")]
        [TestCase("\"cycleOffset\":null", "cycleOffset")]
        [TestCase("\"position\":{\"x\":1}", "position")]
        public void AMalformedSettingIsRejectedByName(string fragment, string expectedInMessage)
        {
            Post("{\"name\":\"Idle\",\"tag\":\"Kept\"}");

            var response = Patch("{\"name\":\"Idle\"," + fragment + "}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains(expectedInMessage, response.Body);
            Assert.AreEqual("Kept", State("Idle").tag);
        }

        [Test]
        public void BehavioursInTheRequestIsReportedRatherThanIgnored()
        {
            var response = Post("{\"name\":\"Idle\",\"behaviours\":[\"UnionAirProbeStateBehaviour\"]}");

            Assert.AreEqual(201, response.StatusCode, response.Body);
            StringAssert.Contains("behaviours is read-only", response.Body);
            Assert.AreEqual(0, State("Idle").behaviours.Length);
        }

        [Test]
        public void AnUnknownFieldIsRejectedAndTheAcceptedOnesAreListed()
        {
            // A typo used to be indistinguishable from a setting that did nothing.
            var response = Patch("{\"name\":\"Idle\",\"writeDefaults\":false}");

            Assert.AreEqual(400, response.StatusCode);
            StringAssert.Contains("writeDefaults", response.Body);
            StringAssert.Contains("writeDefaultValues", response.Body);
        }
    }
}

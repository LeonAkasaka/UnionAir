using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Drives the transition endpoints against a real controller asset, because what
    /// matters about them is what a state pair carrying more than one transition does to
    /// each verb: the name pair addresses one transition only while there is one, and the
    /// endpoints used to answer that case by silently picking the first or by removing
    /// them all.
    /// </summary>
    internal sealed class AnimatorTransitionHandlerTests
    {
        private const string Dir = "Assets/UnionAirTransitionTests";
        private const string ControllerPath = Dir + "/Test.controller";

        private AnimatorController _controller;
        private string _guid;

        [SetUp]
        public void CreateController()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "UnionAirTransitionTests");

            AssetDatabase.DeleteAsset(ControllerPath);

            _controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            _controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            _controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            _controller.layers[0].stateMachine.AddState("Idle");
            _controller.layers[0].stateMachine.AddState("Walk");
            AssetDatabase.SaveAssets();

            _guid = AssetDatabase.AssetPathToGUID(ControllerPath);
        }

        [TearDown]
        public void DeleteController()
        {
            AssetDatabase.DeleteAsset(Dir);
        }

        private FakeResponse Post(string json)
            => Send(new FakeRequest("POST").WithJsonBody(json), (h, rq, rs) => h.HandleAddTransition(rq, rs, _guid));

        private FakeResponse Patch(string json)
            => Send(new FakeRequest("PATCH").WithJsonBody(json), (h, rq, rs) => h.HandleUpdateTransition(rq, rs, _guid));

        private FakeResponse Delete(string json)
            => Send(new FakeRequest("DELETE").WithJsonBody(json), (h, rq, rs) => h.HandleDeleteTransition(rq, rs, _guid));

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

        private static int TransitionsInAsset()
        {
            var count = 0;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                if (o is AnimatorStateTransition) count++;
            return count;
        }

        /// <summary>Adds the two Idle -> Walk transitions that only their conditions tell apart.</summary>
        private void AddTwoBetweenTheSamePair()
        {
            Post("{\"from\":\"Idle\",\"to\":\"Walk\",\"conditions\":[{\"parameter\":\"Speed\",\"mode\":\"Greater\",\"threshold\":0.1}]}");
            Post("{\"from\":\"Idle\",\"to\":\"Walk\",\"conditions\":[{\"parameter\":\"Jump\",\"mode\":\"If\"}]}");
        }

        private static string IdOf(AnimatorStateTransition transition)
            => ObjectIdUtils.GetGlobalObjectId(transition);

        // ── Read ─────────────────────────────────────────────────────────────

        [Test]
        public void Read_CarriesTheTransitionIdAndTheSettingsItUsedToOmit()
        {
            Post("{\"from\":\"Idle\",\"to\":\"Walk\",\"offset\":0.25,\"fixedDuration\":false," +
                 "\"interruptionSource\":\"Destination\",\"orderedInterruption\":false,\"mute\":true,\"solo\":true}");

            var body = Read().Body;

            StringAssert.Contains("\"transitionId\":\"GlobalObjectId", body);
            StringAssert.Contains("\"offset\":0.25", body);
            StringAssert.Contains("\"fixedDuration\":false", body);
            StringAssert.Contains("\"interruptionSource\":\"Destination\"", body);
            StringAssert.Contains("\"orderedInterruption\":false", body);
            StringAssert.Contains("\"canTransitionToSelf\":", body);
            StringAssert.Contains("\"mute\":true", body);
            StringAssert.Contains("\"solo\":true", body);
        }

        [Test]
        public void Read_ReportsDurationBesideTheFlagThatGivesItItsUnit()
        {
            // duration alone does not say whether it is seconds or a fraction of the source
            // state, so the read must never carry one without the other.
            Post("{\"from\":\"Idle\",\"to\":\"Walk\",\"duration\":0.5,\"fixedDuration\":true}");

            var body = Read().Body;

            Assert.Less(body.IndexOf("\"duration\":", System.StringComparison.Ordinal),
                body.IndexOf("\"fixedDuration\":", System.StringComparison.Ordinal));
            StringAssert.Contains("\"duration\":0.5,\"fixedDuration\":true", body);
        }

        [Test]
        public void Post_ReturnsAnIdThatAddressesTheTransitionItJustCreated()
        {
            var created = Post("{\"from\":\"Idle\",\"to\":\"Walk\"}");

            Assert.AreEqual(201, created.StatusCode, created.Body);
            var id = IdOf(State("Idle").transitions[0]);
            StringAssert.Contains(id, created.Body);
        }

        // ── Ambiguity ────────────────────────────────────────────────────────

        [Test]
        public void Patch_AnswersA409ListingEveryMatchWhenTheNamePairIsAmbiguous()
        {
            AddTwoBetweenTheSamePair();

            var response = Patch("{\"from\":\"Idle\",\"to\":\"Walk\",\"duration\":9.0}");

            Assert.AreEqual(409, response.StatusCode, response.Body);
            foreach (var transition in State("Idle").transitions)
                StringAssert.Contains(IdOf(transition), response.Body);
            // The conditions are what tell the candidates apart, so they travel with them.
            StringAssert.Contains("\"conditions\":", response.Body);
            StringAssert.Contains("\"Speed\"", response.Body);
            StringAssert.Contains("\"Jump\"", response.Body);
        }

        [Test]
        public void Patch_ChangesNothingWhenItAnswers409()
        {
            AddTwoBetweenTheSamePair();

            Patch("{\"from\":\"Idle\",\"to\":\"Walk\",\"duration\":9.0}");

            foreach (var transition in State("Idle").transitions)
                Assert.AreNotEqual(9.0f, transition.duration);
        }

        [Test]
        public void Delete_AnswersA409InsteadOfRemovingEveryMatch()
        {
            // The behaviour this replaces: RemoveAll took every transition between the pair
            // and reported the same {"removed":true} it reports for one.
            AddTwoBetweenTheSamePair();

            var response = Delete("{\"from\":\"Idle\",\"to\":\"Walk\"}");

            Assert.AreEqual(409, response.StatusCode, response.Body);
            Assert.AreEqual(2, State("Idle").transitions.Length, "a refused delete must remove nothing");
        }

        [Test]
        public void FromAndToStillWorkWhileTheyResolveToOne()
        {
            Post("{\"from\":\"Idle\",\"to\":\"Walk\"}");

            Assert.AreEqual(200, Patch("{\"from\":\"Idle\",\"to\":\"Walk\",\"duration\":0.75}").StatusCode);
            Assert.AreEqual(0.75f, State("Idle").transitions[0].duration);
            Assert.AreEqual(200, Delete("{\"from\":\"Idle\",\"to\":\"Walk\"}").StatusCode);
            Assert.AreEqual(0, State("Idle").transitions.Length);
        }

        // ── Addressing by id ─────────────────────────────────────────────────

        [Test]
        public void Patch_AddressedByIdTouchesOnlyTheTransitionItNames()
        {
            AddTwoBetweenTheSamePair();
            var first = IdOf(State("Idle").transitions[0]);
            var secondBefore = State("Idle").transitions[1].duration;

            var response = Patch("{\"transitionId\":\"" + first + "\",\"duration\":0.75}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.AreEqual(0.75f, State("Idle").transitions[0].duration);
            Assert.AreEqual(secondBefore, State("Idle").transitions[1].duration);
        }

        [Test]
        public void Delete_AddressedByIdRemovesOneAndDestroysItsSubAsset()
        {
            // Assigning a transitions array that omits one detaches it without destroying
            // it, which left an AnimatorStateTransition in the .controller file that nothing
            // referred to. RemoveTransition is what owns the sub-asset's lifetime.
            AddTwoBetweenTheSamePair();
            Assert.AreEqual(2, TransitionsInAsset());
            var first = IdOf(State("Idle").transitions[0]);

            var response = Delete("{\"transitionId\":\"" + first + "\"}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.AreEqual(1, State("Idle").transitions.Length);
            Assert.AreEqual(1, TransitionsInAsset(), "the removed transition must not stay in the asset");
        }

        [Test]
        public void AnIdThatNoLongerResolvesAnswers404()
        {
            Post("{\"from\":\"Idle\",\"to\":\"Walk\"}");
            var id = IdOf(State("Idle").transitions[0]);
            Delete("{\"transitionId\":\"" + id + "\"}");

            Assert.AreEqual(404, Patch("{\"transitionId\":\"" + id + "\",\"duration\":1.0}").StatusCode);
        }

        [Test]
        public void AMalformedIdAnswers400()
        {
            Assert.AreEqual(400, Patch("{\"transitionId\":\"not-an-id\"}").StatusCode);
        }

        [Test]
        public void AnIdFromAnotherLayerNamesTheLayerItIsIn()
        {
            // layerIndex defaults to 0, so this is what a caller holding an id from a higher
            // layer hits first, and the layer number is the one thing the message can add.
            _controller.AddLayer("Arms");
            _controller.layers[1].stateMachine.AddState("Wave");
            _controller.layers[1].stateMachine.AddState("Rest");
            AssetDatabase.SaveAssets();
            Post("{\"from\":\"Wave\",\"to\":\"Rest\",\"layerIndex\":1}");
            var id = IdOf(Reloaded().layers[1].stateMachine.states[0].state.transitions[0]);

            var response = Patch("{\"transitionId\":\"" + id + "\",\"duration\":1.0}");

            Assert.AreEqual(404, response.StatusCode, response.Body);
            StringAssert.Contains("layer 1", response.Body);
        }

        // ── Settings ─────────────────────────────────────────────────────────

        [Test]
        public void EverySettingRoundTripsThroughPatch()
        {
            Post("{\"from\":\"Idle\",\"to\":\"Walk\"}");
            var id = IdOf(State("Idle").transitions[0]);

            var response = Patch("{\"transitionId\":\"" + id + "\",\"hasExitTime\":true,\"exitTime\":0.4," +
                                 "\"duration\":0.2,\"fixedDuration\":false,\"offset\":0.3," +
                                 "\"interruptionSource\":\"SourceThenDestination\",\"orderedInterruption\":false," +
                                 "\"mute\":true,\"solo\":true}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            var transition = State("Idle").transitions[0];
            Assert.IsTrue(transition.hasExitTime);
            Assert.AreEqual(0.4f, transition.exitTime);
            Assert.AreEqual(0.2f, transition.duration);
            Assert.IsFalse(transition.hasFixedDuration);
            Assert.AreEqual(0.3f, transition.offset);
            Assert.AreEqual(TransitionInterruptionSource.SourceThenDestination, transition.interruptionSource);
            Assert.IsFalse(transition.orderedInterruption);
            Assert.IsTrue(transition.mute);
            Assert.IsTrue(transition.solo);
        }

        [Test]
        public void CanTransitionToSelfIsReportedAsUnsupportedOnAStateTransition()
        {
            var response = Post("{\"from\":\"Idle\",\"to\":\"Walk\",\"canTransitionToSelf\":false}");

            Assert.AreEqual(201, response.StatusCode, response.Body);
            StringAssert.Contains("canTransitionToSelf", response.Body);
            StringAssert.Contains("\"unsupported\":[\"", response.Body);
        }

        [Test]
        public void CanTransitionToSelfIsAcceptedWithoutCommentOnAnAnyStateTransition()
        {
            var response = Post("{\"from\":\"AnyState\",\"to\":\"Walk\",\"canTransitionToSelf\":false}");

            Assert.AreEqual(201, response.StatusCode, response.Body);
            StringAssert.Contains("\"unsupported\":[]", response.Body);
            Assert.IsFalse(Reloaded().layers[0].stateMachine.anyStateTransitions[0].canTransitionToSelf);
        }

        [Test]
        public void AnUnknownInterruptionSourceAnswers400NamingTheAcceptedValues()
        {
            var response = Post("{\"from\":\"Idle\",\"to\":\"Walk\",\"interruptionSource\":\"Nope\"}");

            Assert.AreEqual(400, response.StatusCode);
            StringAssert.Contains("SourceThenDestination", response.Body);
            Assert.AreEqual(0, TransitionsInAsset(), "a refused create must leave no sub-asset");
        }

        [Test]
        public void AnUnknownConditionModeAnswers400AndAppliesNothing()
        {
            // Skipping the element is what this used to do, and it produced a transition
            // holding fewer conditions than the request listed, reported as a success.
            Post("{\"from\":\"Idle\",\"to\":\"Walk\",\"conditions\":[{\"parameter\":\"Speed\",\"mode\":\"Greater\",\"threshold\":0.1}]}");
            var id = IdOf(State("Idle").transitions[0]);

            var response = Patch("{\"transitionId\":\"" + id + "\",\"duration\":9.0," +
                                 "\"conditions\":[{\"parameter\":\"Speed\",\"mode\":\"Bigger\"}]}");

            Assert.AreEqual(400, response.StatusCode);
            StringAssert.Contains("NotEqual", response.Body);
            Assert.AreEqual(1, State("Idle").transitions[0].conditions.Length);
            Assert.AreNotEqual(9.0f, State("Idle").transitions[0].duration,
                "a refused request must not leave its other fields applied");
        }

        [Test]
        public void AnEmptyConditionsArrayClearsTheConditions()
        {
            Post("{\"from\":\"Idle\",\"to\":\"Walk\",\"conditions\":[{\"parameter\":\"Speed\",\"mode\":\"Greater\",\"threshold\":0.1}]}");
            var id = IdOf(State("Idle").transitions[0]);

            Assert.AreEqual(200, Patch("{\"transitionId\":\"" + id + "\",\"conditions\":[]}").StatusCode);
            Assert.AreEqual(0, State("Idle").transitions[0].conditions.Length);
        }

        [Test]
        public void OmittingConditionsLeavesThemAlone()
        {
            Post("{\"from\":\"Idle\",\"to\":\"Walk\",\"conditions\":[{\"parameter\":\"Speed\",\"mode\":\"Greater\",\"threshold\":0.1}]}");
            var id = IdOf(State("Idle").transitions[0]);

            Patch("{\"transitionId\":\"" + id + "\",\"duration\":0.5}");

            Assert.AreEqual(1, State("Idle").transitions[0].conditions.Length);
        }

        [Test]
        public void AMalformedSettingLeavesNoTransitionBehind()
        {
            var response = Post("{\"from\":\"Idle\",\"to\":\"Walk\",\"duration\":\"soon\"}");

            Assert.AreEqual(400, response.StatusCode);
            Assert.AreEqual(0, State("Idle").transitions.Length);
            Assert.AreEqual(0, TransitionsInAsset());
        }

        [Test]
        public void AWriteWithNoAddressAtAllAnswers400()
        {
            var response = Patch("{\"duration\":0.5}");

            Assert.AreEqual(400, response.StatusCode);
            StringAssert.Contains("transitionId", response.Body);
        }
    }
}

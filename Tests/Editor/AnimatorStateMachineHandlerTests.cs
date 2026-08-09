using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Drives the sub-state machine endpoints against a real controller asset. What matters
    /// is what removal leaves in the file: a state machine owns its states, transitions, and
    /// the blend trees those states hold, all sub-assets of the controller, and Unity does
    /// not collect all of them.
    /// </summary>
    internal sealed class AnimatorStateMachineHandlerTests
    {
        private const string Dir = "Assets/UnionAirStateMachineTests";
        private const string ControllerPath = Dir + "/Test.controller";

        private AnimatorController _controller;
        private string _guid;

        [SetUp]
        public void CreateController()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "UnionAirStateMachineTests");

            AssetDatabase.DeleteAsset(ControllerPath);

            _controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            _controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            _controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            AssetDatabase.SaveAssets();

            _guid = AssetDatabase.AssetPathToGUID(ControllerPath);
        }

        [TearDown]
        public void DeleteController()
        {
            AssetDatabase.DeleteAsset(Dir);
        }

        private FakeResponse PostMachine(string json)
            => SendMachine(new FakeRequest("POST").WithJsonBody(json), (h, rq, rs) => h.HandleCreate(rq, rs, _guid));

        private FakeResponse DeleteMachine(string json)
            => SendMachine(new FakeRequest("DELETE").WithJsonBody(json), (h, rq, rs) => h.HandleDelete(rq, rs, _guid));

        private FakeResponse PostMachineTransition(string json)
            => SendMachine(new FakeRequest("POST").WithJsonBody(json), (h, rq, rs) => h.HandleAddTransition(rq, rs, _guid));

        private FakeResponse DeleteMachineTransition(string json)
            => SendMachine(new FakeRequest("DELETE").WithJsonBody(json), (h, rq, rs) => h.HandleDeleteTransition(rq, rs, _guid));

        private static FakeResponse SendMachine(
            FakeRequest request,
            System.Action<AnimatorStateMachineHandler, FakeRequest, FakeResponse> call)
        {
            var response = new FakeResponse();
            call(new AnimatorStateMachineHandler(), request, response);
            return response;
        }

        private FakeResponse PostState(string json)
            => SendController(new FakeRequest("POST").WithJsonBody(json), (h, rq, rs) => h.HandleAddState(rq, rs, _guid));

        private FakeResponse PatchState(string json)
            => SendController(new FakeRequest("PATCH").WithJsonBody(json), (h, rq, rs) => h.HandleUpdateState(rq, rs, _guid));

        private FakeResponse PostTransition(string json)
            => SendController(new FakeRequest("POST").WithJsonBody(json), (h, rq, rs) => h.HandleAddTransition(rq, rs, _guid));

        private FakeResponse Read()
            => SendController(new FakeRequest("GET"), (h, rq, rs) => h.HandleRead(rq, rs, _guid));

        private static FakeResponse SendController(
            FakeRequest request,
            System.Action<AnimatorControllerHandler, FakeRequest, FakeResponse> call)
        {
            var response = new FakeResponse();
            call(new AnimatorControllerHandler(), request, response);
            return response;
        }

        private static AnimatorController Reloaded()
            => AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

        private static AnimatorStateMachine Machine(params string[] path)
        {
            AnimatorStateMachineRules.TryResolve(
                Reloaded().layers[0].stateMachine, path, out var machine, out _, out _);
            return machine;
        }

        private static int CountInAsset<T>() where T : Object
        {
            var count = 0;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                if (o is T) count++;
            return count;
        }

        /// <summary>Combat &gt; Melee, with two states in Melee.</summary>
        private void BuildTwoLevels()
        {
            PostMachine("{\"name\":\"Combat\"}");
            PostMachine("{\"stateMachinePath\":[\"Combat\"],\"name\":\"Melee\"}");
            PostState("{\"stateMachinePath\":[\"Combat\",\"Melee\"],\"name\":\"Swing\"}");
            PostState("{\"stateMachinePath\":[\"Combat\",\"Melee\"],\"name\":\"Recover\"}");
        }

        // ── Read ─────────────────────────────────────────────────────────────

        [Test]
        public void Read_DescribesNestedMachinesWithTheirPath()
        {
            // A layer whose states live inside a sub-state machine used to report
            // "states": [], indistinguishable from an empty layer.
            BuildTwoLevels();

            var body = Read().Body;

            StringAssert.Contains("\"stateMachines\":[", body);
            StringAssert.Contains("\"path\":[\"Combat\"]", body);
            StringAssert.Contains("\"path\":[\"Combat\",\"Melee\"]", body);
            StringAssert.Contains("\"name\":\"Swing\"", body);
        }

        [Test]
        public void Read_ReportsEveryStateMachineLevelsOwnAnyStateTransitions()
        {
            // anyStateTransitions is a property of each state machine, not of the layer;
            // the response used to show only the root's.
            BuildTwoLevels();
            PostTransition("{\"stateMachinePath\":[\"Combat\",\"Melee\"],\"from\":\"AnyState\",\"to\":\"Recover\"}");

            var melee = Machine("Combat", "Melee");
            Assert.AreEqual(1, melee.anyStateTransitions.Length);
            StringAssert.Contains("\"anyStateTransitions\":[{\"transitionId\"", Read().Body);
        }

        [Test]
        public void Read_BoundsRecursionAndSaysSoAtTheBoundary()
        {
            // An empty machine is legal, so an empty array cannot be the signal for a
            // boundary. Nest one deeper than the bound and the read must say truncated.
            var path = new System.Collections.Generic.List<string>();
            for (int i = 0; i <= AnimatorStateMachineRules.MaxStateMachineDepth; i++)
            {
                var json = new System.Text.StringBuilder("{\"stateMachinePath\":[");
                for (int j = 0; j < path.Count; j++)
                {
                    if (j > 0) json.Append(",");
                    json.Append("\"").Append(path[j]).Append("\"");
                }
                json.Append("],\"name\":\"M").Append(i).Append("\"}");
                Assert.AreEqual(201, PostMachine(json.ToString()).StatusCode, json.ToString());
                path.Add("M" + i);
            }

            StringAssert.Contains("\"truncated\":true", Read().Body);
        }

        // ── Addressing ───────────────────────────────────────────────────────

        [Test]
        public void AStateInsideASubMachineIsReachableByEveryStateEndpoint()
        {
            // The defect: every write that resolves a state by name answered 404 for a
            // state the Animator window plainly shows.
            BuildTwoLevels();

            var response = PatchState("{\"stateMachinePath\":[\"Combat\",\"Melee\"],\"name\":\"Swing\",\"speed\":2.0}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.AreEqual(2.0f, Machine("Combat", "Melee").states[0].state.speed);
        }

        [Test]
        public void AnOmittedPathStillMeansTheLayerRoot()
        {
            PostState("{\"name\":\"Idle\"}");

            Assert.AreEqual(1, Reloaded().layers[0].stateMachine.states.Length);
            Assert.AreEqual("Idle", Reloaded().layers[0].stateMachine.states[0].state.name);
        }

        [Test]
        public void APathThatDoesNotResolveAnswers404WithTheDepth()
        {
            PostMachine("{\"name\":\"Combat\"}");

            var response = PostState("{\"stateMachinePath\":[\"Combat\",\"Nope\"],\"name\":\"X\"}");

            Assert.AreEqual(404, response.StatusCode);
            StringAssert.Contains("depth 1", response.Body);
        }

        [Test]
        public void ACreateThatWouldDuplicateASiblingNameIsRefused()
        {
            // The path addresses by name, so a second sibling of the same name could not be
            // addressed at all. Refusing to create the ambiguity beats reporting it later.
            PostMachine("{\"name\":\"Combat\"}");

            var response = PostMachine("{\"name\":\"Combat\"}");

            Assert.AreEqual(409, response.StatusCode, response.Body);
            Assert.AreEqual(1, Reloaded().layers[0].stateMachine.stateMachines.Length);
        }

        // ── Transitions between machines ─────────────────────────────────────

        [Test]
        public void AStateCanTransitionIntoAStateMachine()
        {
            BuildTwoLevels();
            PostState("{\"name\":\"Idle\"}");

            var response = PostTransition(
                "{\"from\":\"Idle\",\"toStateMachine\":[\"Combat\"],\"conditions\":[{\"parameter\":\"Attack\",\"mode\":\"If\"}]}");

            Assert.AreEqual(201, response.StatusCode, response.Body);
            var transition = Reloaded().layers[0].stateMachine.states[0].state.transitions[0];
            Assert.IsNotNull(transition.destinationStateMachine);
            Assert.AreEqual("Combat", transition.destinationStateMachine.name);
            StringAssert.Contains("\"destination\":{\"type\":\"StateMachine\",\"name\":\"Combat\"}", Read().Body);
        }

        [Test]
        public void SendingBothDestinationsIsRefused()
        {
            BuildTwoLevels();
            PostState("{\"name\":\"Idle\"}");

            var response = PostTransition("{\"from\":\"Idle\",\"to\":\"Idle\",\"toStateMachine\":[\"Combat\"]}");

            Assert.AreEqual(400, response.StatusCode);
            Assert.AreEqual(0, Reloaded().layers[0].stateMachine.states[0].state.transitions.Length);
        }

        [Test]
        public void AnEntryTransitionChoosesWhereAMachineStarts()
        {
            BuildTwoLevels();

            var response = PostMachineTransition(
                "{\"stateMachinePath\":[\"Combat\",\"Melee\"],\"from\":\"Entry\",\"to\":\"Recover\"}");

            Assert.AreEqual(201, response.StatusCode, response.Body);
            Assert.AreEqual(1, Machine("Combat", "Melee").entryTransitions.Length);
            StringAssert.Contains("\"from\":{\"type\":\"Entry\"}", Read().Body);
        }

        [Test]
        public void AnEntryTransitionCannotTargetExit()
        {
            BuildTwoLevels();

            var response = PostMachineTransition(
                "{\"stateMachinePath\":[\"Combat\",\"Melee\"],\"from\":\"Entry\",\"toExit\":true}");

            Assert.AreEqual(400, response.StatusCode);
            Assert.AreEqual(0, Machine("Combat", "Melee").entryTransitions.Length);
        }

        [Test]
        public void AStateMachineTransitionLeavesTheNestedMachine()
        {
            BuildTwoLevels();

            var response = PostMachineTransition(
                "{\"stateMachinePath\":[\"Combat\"],\"from\":\"Melee\",\"toExit\":true}");

            Assert.AreEqual(201, response.StatusCode, response.Body);
            var combat = Machine("Combat");
            Assert.AreEqual(1, combat.GetStateMachineTransitions(combat.stateMachines[0].stateMachine).Length);
            StringAssert.Contains("\"from\":{\"type\":\"StateMachine\",\"name\":\"Melee\"}", Read().Body);
        }

        [Test]
        public void AnAnimatorTransitionCarriesNoFieldItsTypeDoesNotHave()
        {
            // AnimatorTransition has no exit time, duration, offset, or interruption. Emitting
            // them as zeros would read as settings rather than as absent fields.
            BuildTwoLevels();
            PostMachineTransition("{\"stateMachinePath\":[\"Combat\",\"Melee\"],\"from\":\"Entry\",\"to\":\"Swing\"}");

            var body = Read().Body;
            var start = body.IndexOf("\"entryTransitions\":[{", System.StringComparison.Ordinal);
            Assert.Greater(start, -1, body);
            var entry = body.Substring(start, body.IndexOf("}]", start, System.StringComparison.Ordinal) - start);

            StringAssert.DoesNotContain("hasExitTime", entry);
            StringAssert.DoesNotContain("duration", entry);
            StringAssert.DoesNotContain("offset", entry);
            StringAssert.Contains("transitionId", entry);
        }

        [Test]
        public void AnAnimatorTransitionIsRemovedByIdAndDestroyed()
        {
            BuildTwoLevels();
            PostMachineTransition("{\"stateMachinePath\":[\"Combat\",\"Melee\"],\"from\":\"Entry\",\"to\":\"Swing\"}");
            var id = ObjectIdUtils.GetGlobalObjectId(Machine("Combat", "Melee").entryTransitions[0]);
            var before = CountInAsset<AnimatorTransition>();

            var response = DeleteMachineTransition("{\"transitionId\":\"" + id + "\"}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.AreEqual(0, Machine("Combat", "Melee").entryTransitions.Length);
            Assert.AreEqual(before - 1, CountInAsset<AnimatorTransition>(),
                "the removed transition must not stay in the asset");
        }

        [Test]
        public void AStateTransitionIdSentToTheStateMachineEndpointIsRefused()
        {
            BuildTwoLevels();
            PostState("{\"name\":\"Idle\"}");
            PostTransition("{\"from\":\"Idle\",\"toStateMachine\":[\"Combat\"]}");
            var id = ObjectIdUtils.GetGlobalObjectId(
                Reloaded().layers[0].stateMachine.states[0].state.transitions[0]);

            var response = DeleteMachineTransition("{\"transitionId\":\"" + id + "\"}");

            Assert.AreEqual(422, response.StatusCode, response.Body);
            StringAssert.Contains("AnimatorTransition", response.Body);
        }

        // ── Removal ──────────────────────────────────────────────────────────

        [Test]
        public void DeletingANonEmptyMachineAnswers409WithWhatItHolds()
        {
            BuildTwoLevels();

            var response = DeleteMachine("{\"stateMachinePath\":[\"Combat\"]}");

            Assert.AreEqual(409, response.StatusCode, response.Body);
            // Combat holds no state directly and two through Melee; the count must be the
            // cost of the removal, not the depth-one tally.
            StringAssert.Contains("\"totalStates\":2", response.Body);
            StringAssert.Contains("\"Melee\"", response.Body);
            Assert.IsNotNull(Machine("Combat"), "a refused delete must remove nothing");
        }

        [Test]
        public void DeletingRecursivelyLeavesNothingOrphanedInTheAsset()
        {
            BuildTwoLevels();
            PostMachineTransition("{\"stateMachinePath\":[\"Combat\",\"Melee\"],\"from\":\"Entry\",\"to\":\"Swing\"}");
            PostTransition("{\"stateMachinePath\":[\"Combat\",\"Melee\"],\"from\":\"Swing\",\"to\":\"Recover\"}");
            new BlendTreeHandler().HandleCreate(
                new FakeRequest("POST").WithJsonBody(
                    "{\"stateMachinePath\":[\"Combat\",\"Melee\"],\"state\":\"Swing\",\"name\":\"Tree\",\"blendParameter\":\"Speed\"}"),
                new FakeResponse(), _guid);
            Assert.AreEqual(1, CountInAsset<BlendTree>(), "the blend tree did not attach");

            var response = DeleteMachine("{\"stateMachinePath\":[\"Combat\"],\"recursive\":true}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.AreEqual(0, CountInAsset<BlendTree>());
            Assert.AreEqual(0, CountInAsset<AnimatorTransition>());
            Assert.AreEqual(0, CountInAsset<AnimatorStateTransition>());
            Assert.AreEqual(0, CountInAsset<AnimatorState>());
            // Only the layer's own root state machine remains.
            Assert.AreEqual(1, CountInAsset<AnimatorStateMachine>());
        }

        [Test]
        public void DeletingAnEmptyPathIsRefused()
        {
            var response = DeleteMachine("{\"stateMachinePath\":[]}");

            Assert.AreEqual(400, response.StatusCode);
            StringAssert.Contains("root", response.Body);
        }

        [Test]
        public void AnUnknownFieldIsRejected()
        {
            var response = PostMachine("{\"name\":\"Combat\",\"positon\":{\"x\":1,\"y\":2}}");

            Assert.AreEqual(400, response.StatusCode);
            StringAssert.Contains("positon", response.Body);
        }
    }
}

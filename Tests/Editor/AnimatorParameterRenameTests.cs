using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Drives the parameter endpoints against a real controller asset.
    ///
    /// What matters about a rename is not the response but the sites it reaches. A
    /// parameter is named from four kinds of place and none of them is a reference Unity
    /// maintains, so a rename that walks the top level of a layer is a rename that quietly
    /// breaks a controller -- which is worse than having no rename at all.
    /// </summary>
    internal sealed class AnimatorParameterRenameTests
    {
        private const string Dir = "Assets/UnionAirParameterTests";
        private const string ControllerPath = Dir + "/Test.controller";

        private AnimatorController _controller;
        private string _guid;

        [SetUp]
        public void CreateController()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "UnionAirParameterTests");

            AssetDatabase.DeleteAsset(ControllerPath);

            _controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            AssetDatabase.SaveAssets();

            _guid = AssetDatabase.AssetPathToGUID(ControllerPath);
        }

        [TearDown]
        public void DeleteController()
        {
            AssetDatabase.DeleteAsset(Dir);
        }

        private FakeResponse Post(string json)
            => Send(new FakeRequest("POST").WithJsonBody(json), (h, rq, rs) => h.HandleAddParameter(rq, rs, _guid));

        private FakeResponse Patch(string json)
            => Send(new FakeRequest("PATCH").WithJsonBody(json), (h, rq, rs) => h.HandleUpdateParameter(rq, rs, _guid));

        private FakeResponse Delete(string json)
            => Send(new FakeRequest("DELETE").WithJsonBody(json), (h, rq, rs) => h.HandleDeleteParameter(rq, rs, _guid));

        private FakeResponse PostState(string json)
            => Send(new FakeRequest("POST").WithJsonBody(json), (h, rq, rs) => h.HandleAddState(rq, rs, _guid));

        private FakeResponse PostTransition(string json)
            => Send(new FakeRequest("POST").WithJsonBody(json), (h, rq, rs) => h.HandleAddTransition(rq, rs, _guid));

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

        private static string[] ParameterOrder()
        {
            var all = Reloaded().parameters;
            var names = new string[all.Length];
            for (int i = 0; i < all.Length; i++) names[i] = all[i].name;
            return names;
        }

        private static AnimatorState State(AnimatorStateMachine sm, string name)
        {
            foreach (var child in sm.states)
                if (child.state.name == name) return child.state;
            return null;
        }

        /// <summary>
        /// A controller naming "Speed" from every kind of site: a condition at the root, a
        /// condition inside a sub-state machine, a state override, and a blend tree nested
        /// inside another blend tree.
        /// </summary>
        private void BuildEveryReferenceKind()
        {
            Post("{\"name\":\"Speed\",\"type\":\"Float\"}");
            PostState("{\"name\":\"Idle\",\"setAsDefault\":true}");
            PostState("{\"name\":\"Run\",\"speedParameter\":\"Speed\",\"speedParameterActive\":true}");
            PostTransition("{\"from\":\"Idle\",\"to\":\"Run\",\"conditions\":[{\"parameter\":\"Speed\",\"mode\":\"Greater\",\"threshold\":0.1}]}");

            new BlendTreeHandler().HandleCreate(
                new FakeRequest("POST").WithJsonBody("{\"state\":\"Idle\",\"name\":\"Tree\",\"blendParameter\":\"Speed\"}"),
                new FakeResponse(), _guid);
            new BlendTreeHandler().HandleCreate(
                new FakeRequest("POST").WithJsonBody(
                    "{\"state\":\"Idle\",\"childPath\":[],\"addChild\":true,\"name\":\"Nested\",\"blendParameter\":\"Speed\"}"),
                new FakeResponse(), _guid);

            new AnimatorStateMachineHandler().HandleCreate(
                new FakeRequest("POST").WithJsonBody("{\"name\":\"Combat\"}"), new FakeResponse(), _guid);
            PostState("{\"stateMachinePath\":[\"Combat\"],\"name\":\"Swing\"}");
            PostState("{\"stateMachinePath\":[\"Combat\"],\"name\":\"Recover\"}");
            PostTransition("{\"stateMachinePath\":[\"Combat\"],\"from\":\"Swing\",\"to\":\"Recover\"," +
                           "\"conditions\":[{\"parameter\":\"Speed\",\"mode\":\"Less\",\"threshold\":0.5}]}");
            new AnimatorStateMachineHandler().HandleAddTransition(
                new FakeRequest("POST").WithJsonBody(
                    "{\"stateMachinePath\":[\"Combat\"],\"from\":\"Entry\",\"to\":\"Swing\"," +
                    "\"conditions\":[{\"parameter\":\"Speed\",\"mode\":\"Greater\",\"threshold\":2.0}]}"),
                new FakeResponse(), _guid);
        }

        // ── Reference discovery ──────────────────────────────────────────────

        [Test]
        public void EveryKindOfReferenceIsFound()
        {
            BuildEveryReferenceKind();

            var references = AnimatorParameterReferences.Find(Reloaded(), "Speed");

            var kinds = new System.Collections.Generic.List<string>();
            foreach (var r in references) kinds.Add(r.Kind);
            Assert.AreEqual(6, references.Count, string.Join(", ", kinds));
            CollectionAssert.Contains(kinds, "speedParameter");
            Assert.AreEqual(2, kinds.FindAll(k => k == "blendParameter").Count, "root and nested tree");
            Assert.AreEqual(3, kinds.FindAll(k => k == "condition").Count,
                "root, sub-state machine, and entry transition");
        }

        [Test]
        public void AReferenceInsideASubStateMachineCarriesItsPath()
        {
            BuildEveryReferenceKind();

            var references = AnimatorParameterReferences.Find(Reloaded(), "Speed");

            var nested = references.FindAll(r => r.StateMachinePath.Count > 0);
            Assert.AreEqual(2, nested.Count);
            Assert.AreEqual("Combat", nested[0].StateMachinePath[0]);
        }

        [Test]
        public void ANestedBlendTreeReferenceCarriesItsChildPath()
        {
            BuildEveryReferenceKind();

            var references = AnimatorParameterReferences.Find(Reloaded(), "Speed");

            var trees = references.FindAll(r => r.Kind == "blendParameter");
            Assert.AreEqual(0, trees[0].ChildPath.Length, "the root tree");
            Assert.AreEqual(1, trees[1].ChildPath.Length, "the nested tree");
            Assert.AreEqual(0, trees[1].ChildPath[0]);
        }

        [Test]
        public void AParameterNothingNamesHasNoReferences()
        {
            Post("{\"name\":\"Unused\",\"type\":\"Bool\"}");

            Assert.IsEmpty(AnimatorParameterReferences.Find(Reloaded(), "Unused"));
        }

        // ── Rename ───────────────────────────────────────────────────────────

        [Test]
        public void RenameRewritesEverySiteAndReportsThem()
        {
            BuildEveryReferenceKind();

            var response = Patch("{\"name\":\"Speed\",\"newName\":\"MoveSpeed\"}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"referencesUpdated\":6", response.Body);
            Assert.IsEmpty(AnimatorParameterReferences.Find(Reloaded(), "Speed"),
                "no site may still name the old parameter");
            Assert.AreEqual(6, AnimatorParameterReferences.Find(Reloaded(), "MoveSpeed").Count);
        }

        [Test]
        public void RenameReachesAConditionInsideASubStateMachine()
        {
            // The reason this issue waited for sub-state machine traversal: a rename that
            // walked only the top level would have missed this one silently.
            BuildEveryReferenceKind();

            Patch("{\"name\":\"Speed\",\"newName\":\"MoveSpeed\"}");

            var combat = Reloaded().layers[0].stateMachine.stateMachines[0].stateMachine;
            Assert.AreEqual("MoveSpeed", State(combat, "Swing").transitions[0].conditions[0].parameter);
            Assert.AreEqual("MoveSpeed", combat.entryTransitions[0].conditions[0].parameter);
        }

        [Test]
        public void RenameKeepsTheParameterWhereItWas()
        {
            Post("{\"name\":\"A\",\"type\":\"Float\"}");
            Post("{\"name\":\"B\",\"type\":\"Float\"}");
            Post("{\"name\":\"C\",\"type\":\"Float\"}");

            Patch("{\"name\":\"B\",\"newName\":\"Bee\"}");

            CollectionAssert.AreEqual(new[] { "A", "Bee", "C" }, ParameterOrder());
        }

        [Test]
        public void RenameToAnExistingNameAnswers409AndChangesNothing()
        {
            BuildEveryReferenceKind();
            Post("{\"name\":\"Grounded\",\"type\":\"Bool\"}");

            var response = Patch("{\"name\":\"Speed\",\"newName\":\"Grounded\"}");

            Assert.AreEqual(409, response.StatusCode, response.Body);
            CollectionAssert.AreEqual(new[] { "Speed", "Grounded" }, ParameterOrder());
            Assert.AreEqual(6, AnimatorParameterReferences.Find(Reloaded(), "Speed").Count,
                "a refused rename must leave every reference as it was");
        }

        [Test]
        public void ARefusedRenameLeavesTheControllerExactlyAsItWas()
        {
            // Atomicity: the collision is detected before the first write, so the parameter
            // and the references move together or not at all.
            BuildEveryReferenceKind();
            Post("{\"name\":\"Taken\",\"type\":\"Float\"}");

            Patch("{\"name\":\"Speed\",\"newName\":\"Taken\"}");

            Assert.IsNotNull(FindParameterByName("Speed"));
            Assert.IsNull(FindParameterByName("MoveSpeed"));
            var sm = Reloaded().layers[0].stateMachine;
            Assert.AreEqual("Speed", State(sm, "Run").speedParameter);
            Assert.AreEqual("Speed", State(sm, "Idle").transitions[0].conditions[0].parameter);
        }

        private static AnimatorControllerParameter FindParameterByName(string name)
        {
            foreach (var p in Reloaded().parameters)
                if (p.name == name) return p;
            return null;
        }

        [Test]
        public void AMalformedDefaultValueDoesNotLetTheRenameLand()
        {
            // The rename and the default value arrive in one request, so the request is
            // atomic across both or it is not atomic at all. Checking the value where it was
            // written meant a 400 whose rename had already gone through.
            BuildEveryReferenceKind();

            var response = Patch("{\"name\":\"Speed\",\"newName\":\"MoveSpeed\",\"defaultValue\":\"soon\"}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            Assert.IsNotNull(FindParameterByName("Speed"));
            Assert.IsNull(FindParameterByName("MoveSpeed"));
            Assert.AreEqual(6, AnimatorParameterReferences.Find(Reloaded(), "Speed").Count,
                "no reference may have been rewritten");
        }

        [Test]
        public void Post_AMalformedDefaultValueAddsNoParameter()
        {
            var response = Post("{\"name\":\"Ghost\",\"type\":\"Float\",\"defaultValue\":\"soon\"}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            Assert.IsNull(FindParameterByName("Ghost"), "a refused create must add nothing");
        }

        [Test]
        public void Post_AMalformedDefaultValueDoesNotReplaceTheType()
        {
            // The worst of the three: the parameter was destroyed and recreated with the new
            // type, orphaning every reference, and then the request answered 400.
            BuildEveryReferenceKind();

            var response = Post("{\"name\":\"Speed\",\"type\":\"Int\",\"defaultValue\":\"soon\"}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            Assert.AreEqual(AnimatorControllerParameterType.Float, FindParameterByName("Speed").type);
            Assert.AreEqual(6, AnimatorParameterReferences.Find(Reloaded(), "Speed").Count);
        }

        [Test]
        public void RenamingToTheSameNameChangesNothingAndReportsNoReferences()
        {
            BuildEveryReferenceKind();

            var response = Patch("{\"name\":\"Speed\",\"newName\":\"Speed\"}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"referencesUpdated\":0", response.Body);
            Assert.AreEqual(6, AnimatorParameterReferences.Find(Reloaded(), "Speed").Count);
        }

        // ── Default values and order ─────────────────────────────────────────

        [Test]
        public void UpdatingOnlyTheDefaultValueKeepsThePosition()
        {
            Post("{\"name\":\"A\",\"type\":\"Float\"}");
            Post("{\"name\":\"B\",\"type\":\"Float\"}");

            Assert.AreEqual(200, Patch("{\"name\":\"A\",\"defaultValue\":0.5}").StatusCode);

            CollectionAssert.AreEqual(new[] { "A", "B" }, ParameterOrder());
            Assert.AreEqual(0.5f, FindParameterByName("A").defaultFloat);
        }

        [Test]
        public void PostOnAnExistingParameterNoLongerMovesItToTheEnd()
        {
            // The old implementation removed and re-added on every POST, so changing a
            // default value reordered the Animator window's list.
            Post("{\"name\":\"A\",\"type\":\"Float\"}");
            Post("{\"name\":\"B\",\"type\":\"Float\"}");

            Post("{\"name\":\"A\",\"type\":\"Float\",\"defaultValue\":0.9}");

            CollectionAssert.AreEqual(new[] { "A", "B" }, ParameterOrder());
            Assert.AreEqual(0.9f, FindParameterByName("A").defaultFloat);
        }

        [Test]
        public void PostWithADifferentTypeReplacesAndReportsWhatItOrphans()
        {
            BuildEveryReferenceKind();

            var response = Post("{\"name\":\"Speed\",\"type\":\"Int\"}");

            Assert.AreEqual(201, response.StatusCode, response.Body);
            StringAssert.Contains("\"replacedType\":true", response.Body);
            StringAssert.Contains("\"orphanedReferences\":[", response.Body);
            Assert.AreEqual(AnimatorControllerParameterType.Int, FindParameterByName("Speed").type);
        }

        [TestCase("\"defaultValue\":true")]
        public void ATriggerDefaultValueIsReportedAsUnsupported(string fragment)
        {
            Post("{\"name\":\"Fire\",\"type\":\"Trigger\"}");

            var posted = Post("{\"name\":\"Fire\",\"type\":\"Trigger\"," + fragment + "}");
            var patched = Patch("{\"name\":\"Fire\"," + fragment + "}");

            StringAssert.Contains("Trigger", posted.Body);
            StringAssert.Contains("unsupported", posted.Body);
            StringAssert.Contains("Trigger", patched.Body);
        }

        [Test]
        public void ATypeFieldInAPatchIsRejectedWithTheReason()
        {
            Post("{\"name\":\"Speed\",\"type\":\"Float\"}");

            var response = Patch("{\"name\":\"Speed\",\"type\":\"Int\"}");

            Assert.AreEqual(400, response.StatusCode);
            StringAssert.Contains("type cannot be changed", response.Body);
            Assert.AreEqual(AnimatorControllerParameterType.Float, FindParameterByName("Speed").type);
        }

        [Test]
        public void AnUnknownFieldInAPatchIsRejected()
        {
            Post("{\"name\":\"Speed\",\"type\":\"Float\"}");

            var response = Patch("{\"name\":\"Speed\",\"newname\":\"x\"}");

            Assert.AreEqual(400, response.StatusCode);
            StringAssert.Contains("newname", response.Body);
        }

        [Test]
        public void PatchingAParameterThatDoesNotExistAnswers404()
        {
            Assert.AreEqual(404, Patch("{\"name\":\"Nope\",\"newName\":\"X\"}").StatusCode);
        }

        // ── Delete ───────────────────────────────────────────────────────────

        [Test]
        public void DeleteReportsTheReferencesItOrphans()
        {
            BuildEveryReferenceKind();

            var response = Delete("{\"name\":\"Speed\"}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"orphanedReferences\":6", response.Body);
            // Still deleted, and the conditions still name it -- which is the point of
            // reporting rather than repairing.
            Assert.IsNull(FindParameterByName("Speed"));
            Assert.AreEqual("Speed",
                State(Reloaded().layers[0].stateMachine, "Idle").transitions[0].conditions[0].parameter);
        }

        [Test]
        public void DeletingAnUnreferencedParameterReportsZero()
        {
            Post("{\"name\":\"Unused\",\"type\":\"Bool\"}");

            StringAssert.Contains("\"orphanedReferences\":0", Delete("{\"name\":\"Unused\"}").Body);
        }
    }
}

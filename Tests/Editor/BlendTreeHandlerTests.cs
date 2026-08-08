using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Drives the blend tree endpoints against a real controller asset, because what
    /// matters about them is not the JSON they emit but what they leave in the
    /// <c>.controller</c> file: a request that fails must add nothing, and a request that
    /// removes a subtree must not leave it behind as a sub-asset.
    /// </summary>
    internal sealed class BlendTreeHandlerTests
    {
        private const string Dir = "Assets/UnionAirBlendTreeTests";
        private const string ControllerPath = Dir + "/Test.controller";
        private const string ClipPath = Dir + "/Clip.anim";

        private AnimatorController _controller;
        private string _guid;
        private string _clipGuid;

        [SetUp]
        public void CreateController()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "UnionAirBlendTreeTests");

            AssetDatabase.DeleteAsset(ControllerPath);
            AssetDatabase.DeleteAsset(ClipPath);

            _controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            _controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            _controller.layers[0].stateMachine.AddState("S");
            AssetDatabase.CreateAsset(new AnimationClip(), ClipPath);
            AssetDatabase.SaveAssets();

            _guid = AssetDatabase.AssetPathToGUID(ControllerPath);
            _clipGuid = AssetDatabase.AssetPathToGUID(ClipPath);
        }

        [TearDown]
        public void DeleteController()
        {
            AssetDatabase.DeleteAsset(Dir);
        }

        private FakeResponse Post(string json) => Send("POST", json, (h, rq, rs) => h.HandleCreate(rq, rs, _guid));
        private FakeResponse Patch(string json) => Send("PATCH", json, (h, rq, rs) => h.HandleUpdate(rq, rs, _guid));
        private FakeResponse Delete(string json) => Send("DELETE", json, (h, rq, rs) => h.HandleDelete(rq, rs, _guid));

        private FakeResponse Send(string method, string json,
            System.Action<BlendTreeHandler, FakeRequest, FakeResponse> call)
        {
            var request = new FakeRequest(method).WithJsonBody(json);
            var response = new FakeResponse();
            call(new BlendTreeHandler(), request, response);
            return response;
        }

        private BlendTree Root()
            => AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                .GetStateEffectiveMotion(
                    AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                        .layers[0].stateMachine.states[0].state, 0) as BlendTree;

        private static int BlendTreesInAsset()
        {
            var count = 0;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                if (o is BlendTree) count++;
            return count;
        }

        [Test]
        public void Post_CreatesTheRootTreeAndHidesTheSubAsset()
        {
            var response = Post("{\"state\":\"S\",\"name\":\"Root\",\"blendParameter\":\"Speed\"}");

            Assert.AreEqual(201, response.StatusCode, response.Body);
            var root = Root();
            Assert.IsNotNull(root);
            Assert.AreEqual("Root", root.name);
            Assert.AreEqual("Speed", root.blendParameter);
            // Matches what the Animator window produces; AddObjectToAsset does not set it.
            Assert.AreEqual(HideFlags.HideInHierarchy, root.hideFlags);
        }

        [Test]
        public void Post_RefusesASecondRootTree()
        {
            Post("{\"state\":\"S\",\"name\":\"Root\"}");
            var response = Post("{\"state\":\"S\",\"name\":\"Again\"}");

            Assert.AreEqual(409, response.StatusCode, response.Body);
            Assert.AreEqual(1, BlendTreesInAsset());
        }

        [Test]
        public void Post_AddsANestedTreeAndAClipChild()
        {
            Post("{\"state\":\"S\",\"name\":\"Root\"}");
            Assert.AreEqual(201, Post("{\"state\":\"S\",\"childPath\":[],\"addChild\":true,\"name\":\"Kid\"}").StatusCode);
            Assert.AreEqual(201, Post(
                "{\"state\":\"S\",\"childPath\":[0],\"addChild\":true,\"motion\":{\"guid\":\"" + _clipGuid + "\"}}").StatusCode);

            var root = Root();
            Assert.AreEqual(1, root.children.Length);
            var kid = root.children[0].motion as BlendTree;
            Assert.IsNotNull(kid);
            Assert.AreEqual(1, kid.children.Length);
            Assert.IsInstanceOf<AnimationClip>(kid.children[0].motion);
        }

        [Test]
        public void Post_AppliesNothingWhenAChildFieldIsMalformed()
        {
            // The defect this guards: the child used to be created before the child fields
            // were checked, so a 400 left one appended anyway.
            Post("{\"state\":\"S\",\"name\":\"Root\"}");
            var before = BlendTreesInAsset();

            var response = Post("{\"state\":\"S\",\"childPath\":[],\"addChild\":true,\"name\":\"Bad\",\"position\":{\"x\":1}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            Assert.AreEqual(0, Root().children.Length, "a refused request must add no child");
            Assert.AreEqual(before, BlendTreesInAsset(), "a refused request must add no sub-asset");
        }

        [Test]
        public void Patch_AppliesNothingWhenTheChildHalfIsMalformed()
        {
            // Tree fields used to be written before the child half was parsed, so a valid
            // rename plus a malformed position left the rename applied behind a 400.
            Post("{\"state\":\"S\",\"name\":\"Root\"}");
            Post("{\"state\":\"S\",\"childPath\":[],\"addChild\":true,\"name\":\"Kid\"}");

            var response = Patch("{\"state\":\"S\",\"childPath\":[0],\"name\":\"Renamed\",\"position\":{\"x\":1}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            Assert.AreEqual("Kid", (Root().children[0].motion as BlendTree).name);
        }

        [Test]
        public void Patch_UpdatesAChildThatHoldsAClip()
        {
            // Requiring the addressed motion to be a blend tree made every clip child
            // unreachable, and most children of a real tree hold a clip.
            Post("{\"state\":\"S\",\"name\":\"Root\"}");
            Post("{\"state\":\"S\",\"childPath\":[],\"addChild\":true,\"motion\":{\"guid\":\"" + _clipGuid + "\"}}");

            var response = Patch("{\"state\":\"S\",\"childPath\":[0],\"timeScale\":2.0,\"mirror\":true}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            var child = Root().children[0];
            Assert.AreEqual(2.0f, child.timeScale);
            Assert.IsTrue(child.mirror);
        }

        [Test]
        public void Patch_RefusesTreeFieldsOnAChildThatHoldsAClip()
        {
            Post("{\"state\":\"S\",\"name\":\"Root\"}");
            Post("{\"state\":\"S\",\"childPath\":[],\"addChild\":true,\"motion\":{\"guid\":\"" + _clipGuid + "\"}}");

            var response = Patch("{\"state\":\"S\",\"childPath\":[0],\"blendType\":\"Direct\"}");

            Assert.AreEqual(400, response.StatusCode);
            StringAssert.Contains("holds a clip", response.Body);
        }

        [Test]
        public void Patch_SwappingAMotionDestroysTheTreeItDisplaces()
        {
            Post("{\"state\":\"S\",\"name\":\"Root\"}");
            Post("{\"state\":\"S\",\"childPath\":[],\"addChild\":true,\"name\":\"Kid\"}");
            Assert.AreEqual(2, BlendTreesInAsset());

            var response = Patch("{\"state\":\"S\",\"childPath\":[0],\"motion\":{\"guid\":\"" + _clipGuid + "\"}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.AreEqual(1, BlendTreesInAsset(), "the displaced tree must not stay in the asset");
        }

        [Test]
        public void Patch_RejectsAParameterThatDoesNotExist()
        {
            Post("{\"state\":\"S\",\"name\":\"Root\"}");

            var response = Patch("{\"state\":\"S\",\"childPath\":[],\"name\":\"Kept\",\"blendParameter\":\"Nope\"}");

            Assert.AreEqual(400, response.StatusCode);
            Assert.AreEqual("Root", Root().name, "the whole request must be refused, not half of it");
        }

        [Test]
        public void Patch_ReportsAThresholdTheParentWillRecompute()
        {
            Post("{\"state\":\"S\",\"name\":\"Root\"}");
            Post("{\"state\":\"S\",\"childPath\":[],\"addChild\":true,\"motion\":{\"guid\":\"" + _clipGuid + "\"}}");

            var response = Patch("{\"state\":\"S\",\"childPath\":[0],\"threshold\":0.5}");

            Assert.AreEqual(200, response.StatusCode);
            StringAssert.Contains("useAutomaticThresholds", response.Body);
        }

        [Test]
        public void Delete_RemovesAChildAndItsWholeSubtree()
        {
            Post("{\"state\":\"S\",\"name\":\"Root\"}");
            Post("{\"state\":\"S\",\"childPath\":[],\"addChild\":true,\"name\":\"Kid\"}");
            Post("{\"state\":\"S\",\"childPath\":[0],\"addChild\":true,\"name\":\"Grand\"}");
            Assert.AreEqual(3, BlendTreesInAsset());

            var response = Delete("{\"state\":\"S\",\"childPath\":[0]}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"destroyedSubTrees\":2", response.Body);
            Assert.AreEqual(1, BlendTreesInAsset(), "RemoveChild leaves the detached subtree behind");
        }

        [Test]
        public void Delete_ClearingTheMotionLeavesNoTreeBehind()
        {
            Post("{\"state\":\"S\",\"name\":\"Root\"}");
            Post("{\"state\":\"S\",\"childPath\":[],\"addChild\":true,\"name\":\"Kid\"}");

            var response = Delete("{\"state\":\"S\",\"childPath\":[]}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.AreEqual(0, BlendTreesInAsset());
        }

        [Test]
        public void ChildPathThatDoesNotResolveAnswers404WithTheDepth()
        {
            Post("{\"state\":\"S\",\"name\":\"Root\"}");

            var response = Patch("{\"state\":\"S\",\"childPath\":[9],\"name\":\"X\"}");

            Assert.AreEqual(404, response.StatusCode);
            StringAssert.Contains("depth 0", response.Body);
        }

        [Test]
        public void UnknownBlendTypeAnswers400NamingTheAcceptedValues()
        {
            var response = Post("{\"state\":\"S\",\"name\":\"Root\",\"blendType\":\"Nope\"}");

            Assert.AreEqual(400, response.StatusCode);
            StringAssert.Contains("Simple1D", response.Body);
            Assert.AreEqual(0, BlendTreesInAsset(), "a refused create must leave no sub-asset");
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Holds the examples in <c>[UnionAirEndpoint]</c> to the contract they claim to show.
    ///
    /// They are hand-written string literals served to clients through
    /// <c>GET /api/help?detail=full</c>, and nothing used to check them, so they could go
    /// stale without failing to compile. The AnimatorController read's example survived two
    /// breaking changes to its own response that way: it showed a transition's destination
    /// as <c>"to": "Idle"</c> after that became a <c>destination</c> object, and a layer
    /// carrying none of the five fields sub-state machine traversal added.
    /// </summary>
    internal sealed class EndpointExampleTests
    {
        /// <summary>Every top-level key of every field name in a JSON document.</summary>
        private static HashSet<string> KeyNames(string json) => new HashSet<string>(
            Regex.Matches(json, @"""(?<k>[A-Za-z_][A-Za-z0-9_]*)""\s*:")
                 .Cast<Match>().Select(m => m.Groups["k"].Value));

        /// <summary>
        /// A declared field is normally one name, but five endpoints spell an alternation as
        /// a single entry -- "guid or assetPath", "path or name". That is prose in a
        /// machine-readable list and is its own question; here it is accepted rather than
        /// flagged, so this test fails only for genuine drift.
        /// </summary>
        private static IEnumerable<string> DeclaredNames(IEnumerable<string> declared)
            => (declared ?? new string[0]).SelectMany(f => f.Split(new[] { " or " }, System.StringSplitOptions.None));

        [Test]
        public void EveryRequestExampleSendsOnlyFieldsItsEndpointDeclares()
        {
            // Through the same reader the handlers use, so "the example is a valid request"
            // means valid by the parser that will actually see it. It checks well-formedness
            // and duplicate keys at the same time.
            var failures = new List<string>();

            foreach (var e in UnionAirRouteRegistry.Descriptors)
            {
                if (string.IsNullOrEmpty(e.RequestExample)) continue;

                var declared = DeclaredNames(e.RequiredBody).Concat(DeclaredNames(e.OptionalBody)).ToList();
                if (!RequestBodyReader.TryValidateObjectFields(e.RequestExample, declared, out var error))
                    failures.Add($"{e.Method} {e.Path}: {error}");
            }

            Assert.IsEmpty(failures,
                "a RequestExample must be a request its own endpoint accepts:\n  " +
                string.Join("\n  ", failures));
        }

        [Test]
        public void EveryResponseExampleIsAWellFormedJsonDocument()
        {
            // Weaker than the request check on purpose: a response has no declared field
            // list to check against, so this only catches an example that is not parseable
            // at all -- a truncated literal, or an unescaped quote.
            var failures = new List<string>();

            foreach (var e in UnionAirRouteRegistry.Descriptors)
            {
                if (string.IsNullOrEmpty(e.ResponseExample)) continue;

                var trimmed = e.ResponseExample.TrimStart();
                if (!trimmed.StartsWith("{")) continue;   // a few endpoints show an array or a scalar

                var wrapped = "{\"wrapped\":[" + e.ResponseExample + "]}";
                if (!RequestBodyReader.TryGetArrayElements(wrapped, "wrapped", out var elements, out var present, out var error))
                    failures.Add($"{e.Method} {e.Path}: {error}");
                else if (!present || elements.Count != 1)
                    failures.Add($"{e.Method} {e.Path}: did not parse as a single JSON object");
            }

            Assert.IsEmpty(failures,
                "a ResponseExample must at least parse:\n  " + string.Join("\n  ", failures));
        }

        [Test]
        public void TheAnimatorControllerReadExampleMatchesWhatTheReadEmits()
        {
            // The one ResponseExample checked against a real response, because it is the one
            // that drifted -- twice -- and because it is the largest and most nested response
            // in the package, the kind a client walks rather than glances at. Doing this for
            // all 71 examples would mean a fixture each; doing it for none is what allowed
            // "to" to outlive the field by two releases.
            //
            // Key names rather than values: the example carries "..." for GUIDs on purpose.
            const string dir = "Assets/UnionAirExampleShapeTests";
            const string controllerPath = dir + "/Example.controller";
            const string clipPath = dir + "/Clip.anim";

            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets", "UnionAirExampleShapeTests");

            try
            {
                var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                AssetDatabase.CreateAsset(new AnimationClip(), clipPath);
                AssetDatabase.SaveAssets();

                controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
                var root = controller.layers[0].stateMachine;

                // Everything the example shows, so that a key missing from one side is drift
                // rather than a fixture that simply never built that shape: a state whose
                // motion is a blend tree holding a clip child, a transition to another state,
                // and a sub-state machine entered by an entry transition.
                var locomotion = root.AddState("Locomotion");
                var jump = root.AddState("Jump");
                root.defaultState = locomotion;

                var tree = new BlendTree { name = "Locomotion", blendParameter = "Speed" };
                AssetDatabase.AddObjectToAsset(tree, controller);
                tree.AddChild(AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath));
                controller.SetStateEffectiveMotion(locomotion, tree, 0);

                locomotion.AddTransition(jump);

                var combat = root.AddStateMachine("Combat");
                combat.AddStateMachine("Melee");
                combat.AddEntryTransition(combat.stateMachines[0].stateMachine);

                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();

                var guid = AssetDatabase.AssetPathToGUID(controllerPath);
                var response = new FakeResponse();
                new AnimatorControllerHandler().HandleRead(new FakeRequest("GET"), response, guid);
                Assert.AreEqual(200, response.StatusCode, response.Body);

                var descriptor = UnionAirRouteRegistry.Descriptors.Single(
                    d => d.Method == "GET" && d.Path == "/api/assets/animator-controllers/{guid}");
                Assert.IsNotEmpty(descriptor.ResponseExample ?? "", "the endpoint must declare a ResponseExample");

                var live = KeyNames(response.Body);
                var example = KeyNames(descriptor.ResponseExample);

                var missingFromExample = live.Except(example).OrderBy(k => k).ToList();
                var notInResponse = example.Except(live).OrderBy(k => k).ToList();

                Assert.IsEmpty(missingFromExample,
                    "the read emits fields the ResponseExample does not show: "
                    + string.Join(", ", missingFromExample));
                Assert.IsEmpty(notInResponse,
                    "the ResponseExample shows fields the read does not emit -- this is the shape "
                    + "'to' was left in by: " + string.Join(", ", notInResponse));
            }
            finally
            {
                AssetDatabase.DeleteAsset(dir);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor.Animations;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Finds every place in a controller that names a parameter, and knows how to rewrite
    /// each one.
    ///
    /// A parameter is referenced by name from four kinds of site, and none of them is a
    /// reference Unity maintains: <c>AnimatorCondition.parameter</c>, a blend tree's
    /// <c>blendParameter</c> and <c>blendParameterY</c>, and a state's four
    /// <c>*Parameter</c> overrides are all plain strings. Measured on 6000.0.80f1:
    /// assigning a renamed <c>parameters</c> array renames the parameter and leaves every
    /// one of those strings naming what no longer exists. The controller still loads, the
    /// conditions still serialize, and they never evaluate again.
    ///
    /// So a rename is only a rename if it reaches all of them, which is why this walks the
    /// whole controller -- every layer, every nesting level of every state machine, and
    /// every blend tree beneath every state -- rather than the top level the old endpoints
    /// looked at.
    /// </summary>
    internal static class AnimatorParameterReferences
    {
        /// <summary>
        /// One site naming a parameter, with the means to rewrite it.
        ///
        /// The rewrite travels with the site so that finding and changing cannot disagree:
        /// a caller collects everything first, checks whatever it needs to check, and then
        /// applies, which is what makes a rename atomic without a second walk that might
        /// see a different controller.
        /// </summary>
        internal sealed class Reference
        {
            public string Kind;
            public int LayerIndex;
            public List<string> StateMachinePath;
            public string State;
            public string TransitionId;
            public int ConditionIndex = -1;
            public int[] ChildPath;
            public Action<string> Rewrite;
        }

        /// <summary>
        /// Every site in the controller that names <paramref name="parameterName"/>, in
        /// layer order.
        /// </summary>
        internal static List<Reference> Find(AnimatorController controller, string parameterName)
        {
            var found = new List<Reference>();
            var layers = controller.layers;
            for (int li = 0; li < layers.Length; li++)
            {
                // Blend trees are gathered per layer rather than per state machine, because
                // the same tree can be reached twice -- a synced layer's effective motion is
                // the source layer's -- and rewriting one twice would double the count.
                var seenTrees = new HashSet<BlendTree>();
                WalkStateMachine(controller, layers[li].stateMachine, li, new List<string>(),
                    parameterName, found, seenTrees);
            }
            return found;
        }

        private static void WalkStateMachine(
            AnimatorController controller, AnimatorStateMachine sm, int layerIndex,
            List<string> path, string parameterName, List<Reference> found, HashSet<BlendTree> seenTrees)
        {
            foreach (var child in sm.states)
            {
                var state = child.state;
                if (state == null) continue;

                AddStateOverride(state, "speedParameter", layerIndex, path, parameterName, found,
                    () => state.speedParameter, v => state.speedParameter = v);
                AddStateOverride(state, "cycleOffsetParameter", layerIndex, path, parameterName, found,
                    () => state.cycleOffsetParameter, v => state.cycleOffsetParameter = v);
                AddStateOverride(state, "mirrorParameter", layerIndex, path, parameterName, found,
                    () => state.mirrorParameter, v => state.mirrorParameter = v);
                AddStateOverride(state, "timeParameter", layerIndex, path, parameterName, found,
                    () => state.timeParameter, v => state.timeParameter = v);

                WalkBlendTree(state.motion as BlendTree, state.name, layerIndex, path,
                    new int[0], parameterName, found, seenTrees);
                WalkBlendTree(controller.GetStateEffectiveMotion(state, layerIndex) as BlendTree,
                    state.name, layerIndex, path, new int[0], parameterName, found, seenTrees);

                foreach (var t in state.transitions)
                    AddConditions(t, t.conditions, c => t.conditions = c, layerIndex, path, parameterName, found);
            }

            foreach (var t in sm.anyStateTransitions)
                AddConditions(t, t.conditions, c => t.conditions = c, layerIndex, path, parameterName, found);

            foreach (var t in sm.entryTransitions)
                AddConditions(t, t.conditions, c => t.conditions = c, layerIndex, path, parameterName, found);

            foreach (var child in sm.stateMachines)
            {
                if (child.stateMachine == null) continue;
                foreach (var t in sm.GetStateMachineTransitions(child.stateMachine))
                    AddConditions(t, t.conditions, c => t.conditions = c, layerIndex, path, parameterName, found);
            }

            foreach (var child in sm.stateMachines)
            {
                if (child.stateMachine == null) continue;
                var childPath = new List<string>(path);
                childPath.Add(child.stateMachine.name);
                WalkStateMachine(controller, child.stateMachine, layerIndex, childPath,
                    parameterName, found, seenTrees);
            }
        }

        private static void AddStateOverride(
            AnimatorState state, string kind, int layerIndex, List<string> path,
            string parameterName, List<Reference> found, Func<string> read, Action<string> write)
        {
            if (read() != parameterName) return;

            found.Add(new Reference
            {
                Kind = kind,
                LayerIndex = layerIndex,
                StateMachinePath = new List<string>(path),
                State = state.name,
                Rewrite = write,
            });
        }

        private static void WalkBlendTree(
            BlendTree tree, string stateName, int layerIndex, List<string> path, int[] childPath,
            string parameterName, List<Reference> found, HashSet<BlendTree> seenTrees)
        {
            if (tree == null || !seenTrees.Add(tree)) return;

            if (tree.blendParameter == parameterName)
            {
                var captured = tree;
                found.Add(new Reference
                {
                    Kind = "blendParameter",
                    LayerIndex = layerIndex,
                    StateMachinePath = new List<string>(path),
                    State = stateName,
                    ChildPath = childPath,
                    Rewrite = v => captured.blendParameter = v,
                });
            }

            if (tree.blendParameterY == parameterName)
            {
                var captured = tree;
                found.Add(new Reference
                {
                    Kind = "blendParameterY",
                    LayerIndex = layerIndex,
                    StateMachinePath = new List<string>(path),
                    State = stateName,
                    ChildPath = childPath,
                    Rewrite = v => captured.blendParameterY = v,
                });
            }

            var children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                var nested = new int[childPath.Length + 1];
                Array.Copy(childPath, nested, childPath.Length);
                nested[childPath.Length] = i;
                WalkBlendTree(children[i].motion as BlendTree, stateName, layerIndex, path,
                    nested, parameterName, found, seenTrees);
            }
        }

        /// <summary>
        /// Adds a reference for each condition naming the parameter.
        ///
        /// <c>AnimatorCondition</c> is a struct and <c>conditions</c> hands back a copy, so
        /// the rewrite reads the array again, replaces the one element, and assigns the
        /// array back. Mutating the copy this walk holds would change nothing.
        /// </summary>
        private static void AddConditions(
            UnityEngine.Object owner, AnimatorCondition[] conditions, Action<AnimatorCondition[]> write,
            int layerIndex, List<string> path, string parameterName, List<Reference> found)
        {
            var transitionId = ObjectIdUtils.GetGlobalObjectId(owner);
            for (int i = 0; i < conditions.Length; i++)
            {
                if (conditions[i].parameter != parameterName) continue;

                var index = i;
                found.Add(new Reference
                {
                    Kind = "condition",
                    LayerIndex = layerIndex,
                    StateMachinePath = new List<string>(path),
                    TransitionId = transitionId,
                    ConditionIndex = index,
                    Rewrite = v =>
                    {
                        var current = ReadConditions(owner);
                        if (index >= current.Length) return;
                        current[index].parameter = v;
                        write(current);
                    },
                });
            }
        }

        private static AnimatorCondition[] ReadConditions(UnityEngine.Object owner)
        {
            var stateTransition = owner as AnimatorStateTransition;
            if (stateTransition != null) return stateTransition.conditions;

            var transition = owner as AnimatorTransition;
            return transition != null ? transition.conditions : new AnimatorCondition[0];
        }

        /// <summary>Rewrites every collected site. Cannot fail; every check belongs before this.</summary>
        internal static void Apply(List<Reference> references, string newName)
        {
            foreach (var reference in references)
                reference.Rewrite(newName);
        }

        internal static void AppendJson(StringBuilder sb, List<Reference> references)
        {
            sb.Append("[");
            for (int i = 0; i < references.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var r = references[i];
                sb.Append("{\"kind\":").Append(RestResponse.FormatNullableString(r.Kind));
                sb.Append(",\"layerIndex\":").Append(r.LayerIndex);
                sb.Append(",\"stateMachinePath\":[");
                for (int p = 0; p < r.StateMachinePath.Count; p++)
                {
                    if (p > 0) sb.Append(",");
                    sb.Append(RestResponse.FormatNullableString(r.StateMachinePath[p]));
                }
                sb.Append("]");

                if (r.State != null)
                    sb.Append(",\"state\":").Append(RestResponse.FormatNullableString(r.State));
                if (r.TransitionId != null)
                {
                    sb.Append(",\"transitionId\":").Append(RestResponse.FormatNullableString(r.TransitionId));
                    sb.Append(",\"conditionIndex\":").Append(r.ConditionIndex);
                }
                if (r.ChildPath != null)
                {
                    sb.Append(",\"childPath\":[");
                    for (int c = 0; c < r.ChildPath.Length; c++)
                    {
                        if (c > 0) sb.Append(",");
                        sb.Append(r.ChildPath[c]);
                    }
                    sb.Append("]");
                }
                sb.Append("}");
            }
            sb.Append("]");
        }
    }
}

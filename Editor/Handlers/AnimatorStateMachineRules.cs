using System.Collections.Generic;
using UnityEditor.Animations;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Addressing for sub-state machines: how a <c>stateMachinePath</c> resolves, and how
    /// far the read descends.
    ///
    /// The path is an array of state machine names from the layer root rather than a
    /// <c>/</c>-joined string. Unity does not forbid <c>/</c> in a state machine name, so a
    /// joined path would need an escaping rule, and an escaping rule is a thing clients get
    /// wrong quietly. An array has no separator to collide with.
    /// </summary>
    internal static class AnimatorStateMachineRules
    {
        /// <summary>
        /// How deep the read descends into nested state machines. A machine at the boundary
        /// is emitted with "truncated": true and no "states" or "stateMachines", so a
        /// boundary is distinguishable from a machine that is genuinely empty -- an empty
        /// machine is legal, so an empty array cannot be the signal. Matches the bound
        /// <see cref="MotionJson.MaxBlendTreeDepth"/> puts on blend trees, for the same
        /// reason: nesting in Unity is not formally bounded, and a response has to end.
        /// </summary>
        internal const int MaxStateMachineDepth = 10;

        /// <summary>
        /// Where a path stopped resolving, and why. Kept apart from the HTTP response so
        /// the rule can be tested without a controller asset or a request.
        /// </summary>
        internal enum PathResult
        {
            Resolved,
            /// <summary>No child machine at that depth carries the name.</summary>
            NotFound,
            /// <summary>Several sibling machines carry it, so the name addresses none of them.</summary>
            Ambiguous,
        }

        /// <summary>
        /// Walks a path of state machine names from <paramref name="root"/>.
        ///
        /// Unity does not enforce unique sibling names, so a name that several siblings
        /// carry is reported rather than resolved to the first. Guessing would silently
        /// write to whichever machine happened to be first in the array, and reordering
        /// would change which one that is.
        /// </summary>
        /// <param name="failedDepth">
        /// Index into <paramref name="path"/> of the segment that did not resolve, so the
        /// error can say where rather than only that.
        /// </param>
        /// <param name="matchCount">How many siblings carried an ambiguous name.</param>
        internal static PathResult TryResolve(
            AnimatorStateMachine root,
            IReadOnlyList<string> path,
            out AnimatorStateMachine machine,
            out int failedDepth,
            out int matchCount)
        {
            machine = root;
            failedDepth = -1;
            matchCount = 0;

            if (path == null) return PathResult.Resolved;

            for (int depth = 0; depth < path.Count; depth++)
            {
                var name = path[depth];
                AnimatorStateMachine found = null;
                var matches = 0;

                foreach (var child in machine.stateMachines)
                {
                    if (child.stateMachine == null || child.stateMachine.name != name) continue;
                    matches++;
                    if (found == null) found = child.stateMachine;
                }

                if (matches == 0)
                {
                    failedDepth = depth;
                    machine = null;
                    return PathResult.NotFound;
                }
                if (matches > 1)
                {
                    failedDepth = depth;
                    matchCount = matches;
                    machine = null;
                    return PathResult.Ambiguous;
                }

                machine = found;
            }

            return PathResult.Resolved;
        }

        internal static string NotFoundMessage(IReadOnlyList<string> path, int failedDepth)
            => $"stateMachinePath does not resolve: no state machine named '{path[failedDepth]}' " +
               $"at depth {failedDepth} of {Describe(path)}.";

        internal static string AmbiguousMessage(IReadOnlyList<string> path, int failedDepth, int matchCount)
            => $"{matchCount} sibling state machines are named '{path[failedDepth]}' at depth {failedDepth} " +
               $"of {Describe(path)}, so the path addresses none of them. " +
               "Rename one in the Animator window; nothing here can tell them apart.";

        /// <summary>Renders a path for an error message, without inventing a separator the address does not use.</summary>
        internal static string Describe(IReadOnlyList<string> path)
        {
            if (path == null || path.Count == 0) return "[] (the layer's root state machine)";

            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < path.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append('"').Append(path[i]).Append('"');
            }
            return sb.Append(']').ToString();
        }

        /// <summary>
        /// Everything a state machine holds, at every depth, for the answer a non-recursive
        /// delete gives.
        ///
        /// Counted through the whole subtree rather than one level down. Removing a machine
        /// takes its states, its transitions, and any blend trees those states hold, all
        /// sub-assets of the controller — and it takes them from the nested machines too. A
        /// machine that directly holds no states but holds one that holds five would
        /// otherwise be reported as costing nothing, which is the opposite of what a
        /// confirmation prompt is for.
        /// </summary>
        internal static void CountContents(
            AnimatorStateMachine machine, out int states, out int stateMachines)
        {
            states = machine.states.Length;
            stateMachines = 0;

            foreach (var child in machine.stateMachines)
            {
                if (child.stateMachine == null) continue;
                stateMachines++;
                CountContents(child.stateMachine, out var nestedStates, out var nestedMachines);
                states += nestedStates;
                stateMachines += nestedMachines;
            }
        }
    }
}

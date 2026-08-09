using System.Collections.Generic;
using UnityEditor.Animations;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// The parts of a transition write that can be decided without a live controller:
    /// which enum a name denotes, and which fields the addressed transition does not
    /// consult.
    /// </summary>
    internal static class AnimatorTransitionRules
    {
        internal static bool TryParseInterruptionSource(string value, out TransitionInterruptionSource source)
        {
            switch ((value ?? "").ToLowerInvariant())
            {
                case "none": source = TransitionInterruptionSource.None; return true;
                case "source": source = TransitionInterruptionSource.Source; return true;
                case "destination": source = TransitionInterruptionSource.Destination; return true;
                case "sourcethendestination": source = TransitionInterruptionSource.SourceThenDestination; return true;
                case "destinationthensource": source = TransitionInterruptionSource.DestinationThenSource; return true;
            }
            source = TransitionInterruptionSource.None;
            return false;
        }

        internal static string InterruptionSourceNames =>
            "None, Source, Destination, SourceThenDestination, DestinationThenSource";

        internal static bool TryParseConditionMode(string value, out AnimatorConditionMode mode)
        {
            switch ((value ?? "").ToLowerInvariant())
            {
                case "if": mode = AnimatorConditionMode.If; return true;
                case "ifnot": mode = AnimatorConditionMode.IfNot; return true;
                case "greater": mode = AnimatorConditionMode.Greater; return true;
                case "less": mode = AnimatorConditionMode.Less; return true;
                case "equals": mode = AnimatorConditionMode.Equals; return true;
                case "notequal": mode = AnimatorConditionMode.NotEqual; return true;
            }
            mode = AnimatorConditionMode.If;
            return false;
        }

        internal static string ConditionModeNames =>
            "If, IfNot, Greater, Less, Equals, NotEqual";

        /// <summary>
        /// Names the fields a request set that this transition will not consult.
        ///
        /// Stored rather than refused, matching how the blend tree endpoints treat a field
        /// the addressed blend type does not read: Unity stores it and the read reports it,
        /// so refusing would make the API narrower than the asset. What it must not do is
        /// pass silently.
        /// </summary>
        /// <param name="isAnyStateTransition">
        /// Whether the transition leaves AnyState. Only those transitions can be told
        /// whether the destination may be the state the Animator is already in; on any
        /// other transition the source and the destination are fixed and different, so
        /// canTransitionToSelf has nothing to decide.
        /// </param>
        internal static List<string> CollectUnsupported(bool canTransitionToSelfSet, bool isAnyStateTransition)
        {
            var unsupported = new List<string>();

            if (canTransitionToSelfSet && !isAnyStateTransition)
                unsupported.Add("canTransitionToSelf is stored but not consulted: it applies to AnyState transitions, " +
                                "and this transition leaves a state.");

            return unsupported;
        }

        /// <summary>
        /// The message a <c>from</c> plus <c>to</c> address gets when the state pair carries
        /// more than one transition.
        ///
        /// Answered as a conflict rather than a bad request: the address is well formed, and
        /// what makes it unusable is the controller's own shape. Acting on the first match
        /// or on all of them are both guesses the request did not authorise.
        /// </summary>
        internal static string AmbiguousAddressMessage(string fromName, string toName, int matchCount)
            => $"{matchCount} transitions match {fromName} -> {toName}. " +
               "Address one by transitionId; 'matches' lists every candidate with its conditions.";
    }
}

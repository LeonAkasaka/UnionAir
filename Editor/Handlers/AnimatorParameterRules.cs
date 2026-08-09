using System.Collections.Generic;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// The parts of a parameter write that can be decided without a live controller.
    /// </summary>
    internal static class AnimatorParameterRules
    {
        /// <summary>
        /// Names the fields a request set that the parameter's type does not consult.
        ///
        /// A Trigger has no default to hold -- it is set and consumed within a frame -- so
        /// Unity stores nothing for one. The request is not refused, matching how the blend
        /// tree and state endpoints treat a field their subject does not read; what it must
        /// not do is answer 201 as though the value had been applied, which is what this
        /// endpoint did.
        /// </summary>
        internal static List<string> CollectUnsupported(
            AnimatorControllerParameterType type, bool defaultValueSet)
        {
            var unsupported = new List<string>();

            if (defaultValueSet && type == AnimatorControllerParameterType.Trigger)
                unsupported.Add("defaultValue is not stored for a Trigger parameter: a Trigger is set and " +
                                "consumed within a frame, so Unity keeps no default for one.");

            return unsupported;
        }

        /// <summary>
        /// Why <c>PATCH</c> refuses a <c>type</c> field.
        ///
        /// Changing a parameter's type invalidates every condition that names it, in a way
        /// nothing can resolve on the client's behalf: <c>Greater</c> with a threshold of
        /// 0.1 is a sentence about a Float, and it has no reading at all once the parameter
        /// is a Trigger. Deleting and re-adding is the honest route, and it is the one that
        /// reports what it orphans.
        /// </summary>
        internal static string TypeChangeRefusal =>
            "type cannot be changed here. A type change invalidates every condition that names the " +
            "parameter -- Greater on a Float has no meaning once the parameter is a Trigger -- and no " +
            "rule can resolve that for you. Use DELETE and then POST, which report the references the " +
            "change orphans.";
    }
}

using System.Collections.Generic;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// The parts of a state write that can be decided without a live controller: which
    /// fields the endpoints accept, and which of those the request may name but the
    /// endpoint cannot act on.
    /// </summary>
    internal static class AnimatorStateRules
    {
        /// <summary>
        /// The settings both state endpoints accept. <c>behaviours</c> is in the list on
        /// purpose: it is a field of the read, so a client round-tripping a state sends it
        /// back, and answering "unknown field" to a name this API itself emitted would be
        /// wrong. It is reported as unsupported instead.
        /// </summary>
        internal static readonly string[] SettingFields =
        {
            "tag",
            "writeDefaultValues",
            "iKOnFeet",
            "mirror",
            "cycleOffset",
            "speed",
            "speedParameter",
            "speedParameterActive",
            "cycleOffsetParameter",
            "cycleOffsetParameterActive",
            "mirrorParameter",
            "mirrorParameterActive",
            "timeParameter",
            "timeParameterActive",
            "position",
            "motion",
            "behaviours",
        };

        /// <summary>Fields <c>POST .../states</c> accepts, addressing included.</summary>
        internal static IEnumerable<string> AddFields => Concat(
            new[] { "name", "layerIndex", "stateMachinePath", "setAsDefault" }, SettingFields);

        /// <summary>Fields <c>PATCH .../states</c> accepts, addressing included.</summary>
        internal static IEnumerable<string> UpdateFields => Concat(
            new[] { "name", "layerIndex", "stateMachinePath", "newName", "setAsDefault" }, SettingFields);

        private static IEnumerable<string> Concat(string[] first, string[] second)
        {
            var all = new List<string>(first.Length + second.Length);
            all.AddRange(first);
            all.AddRange(second);
            return all;
        }

        /// <summary>
        /// Names the fields a request set that this endpoint will not act on.
        ///
        /// Only <c>behaviours</c> today. Attaching a <c>StateMachineBehaviour</c> means
        /// resolving a script type and instantiating it as a sub-asset of the controller,
        /// which is an ownership problem of its own; the read reports what is attached so a
        /// client can see it, and writing is out of scope rather than half-implemented.
        /// </summary>
        internal static List<string> CollectUnsupported(bool behavioursSet)
        {
            var unsupported = new List<string>();

            if (behavioursSet)
                unsupported.Add("behaviours is read-only. Attaching a StateMachineBehaviour creates a sub-asset " +
                                "of the controller, which no endpoint does yet; the field is reported by GET only.");

            return unsupported;
        }

        /// <summary>
        /// The four values a parameter can drive, paired with the state property each one
        /// overrides. Naming them in one place keeps the read, the write, and the error
        /// text from drifting apart.
        /// </summary>
        internal static readonly string[] ParameterFields =
        {
            "speedParameter",
            "cycleOffsetParameter",
            "mirrorParameter",
            "timeParameter",
        };

        internal static string ActiveFieldFor(string parameterField) => parameterField + "Active";
    }
}

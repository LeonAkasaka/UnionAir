using System;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Long-running Unity Editor work that other work cannot safely overlap with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the things the Editor can be "busy with". They are mutually exclusive in practice:
    /// a player build occupies the main thread, a test run drives Play mode, compilation ends in a
    /// domain reload, and an asset import invalidates what a request was about to act on.
    /// </para>
    /// <para>
    /// Used as a bit mask so an endpoint can declare every activity that must not be running when
    /// it is called. <see cref="UnionAirEndpointAttribute.BlockedDuring"/> declares it and
    /// <c>GET /api/help</c> reports the resolved value per endpoint.
    /// </para>
    /// </remarks>
    [Flags]
    public enum UnionAirActivity
    {
        /// <summary>No activity; the endpoint is not gated on Editor activity.</summary>
        None = 0,

        /// <summary>A script compilation cycle is queued or running.</summary>
        Compile = 1 << 0,

        /// <summary>A Unity Test Framework run is active, whether started by UnionAir or not.</summary>
        TestRun = 1 << 1,

        /// <summary>The Editor is in Play mode or entering or leaving it.</summary>
        PlayMode = 1 << 2,

        /// <summary>The Editor is importing or refreshing assets.</summary>
        AssetUpdate = 1 << 3,

        /// <summary>A player build is queued or running.</summary>
        Build = 1 << 4,

        /// <summary>The active build target is being switched, with the reimport and reload it causes.</summary>
        BuildTargetSwitch = 1 << 5
    }

    /// <summary>
    /// Stable wire names and human-readable descriptions for <see cref="UnionAirActivity"/>.
    /// </summary>
    /// <remarks>
    /// Kept apart from the enum so the JSON vocabulary is defined in exactly one place: these
    /// strings appear in <c>GET /api/help</c>, in <c>GET /api/editor/status</c>, and inside every
    /// <c>409</c> rejection body, and a client matches on them.
    /// </remarks>
    internal static class UnionAirActivityNames
    {
        /// <summary>
        /// Order in which concurrent activities are reported and blamed.
        /// </summary>
        /// <remarks>
        /// Most exclusive first. When more than one is active, a client is best served by being
        /// told about the one that will take longest to clear and that owns the others: a build
        /// runs its own compilation, and a test run drives Play mode.
        /// </remarks>
        internal static readonly UnionAirActivity[] Priority =
        {
            UnionAirActivity.BuildTargetSwitch,
            UnionAirActivity.Build,
            UnionAirActivity.TestRun,
            UnionAirActivity.PlayMode,
            UnionAirActivity.Compile,
            UnionAirActivity.AssetUpdate
        };

        /// <summary>Stable identifier used in JSON.</summary>
        internal static string Name(UnionAirActivity activity)
        {
            switch (activity)
            {
                case UnionAirActivity.Compile:           return "compile";
                case UnionAirActivity.TestRun:           return "testRun";
                case UnionAirActivity.PlayMode:          return "playMode";
                case UnionAirActivity.AssetUpdate:       return "assetUpdate";
                case UnionAirActivity.Build:             return "build";
                case UnionAirActivity.BuildTargetSwitch: return "buildTargetSwitch";
                default:                                 return "none";
            }
        }

        /// <summary>Sentence fragment naming the activity in a rejection message.</summary>
        internal static string Describe(UnionAirActivity activity)
        {
            switch (activity)
            {
                case UnionAirActivity.Compile:           return "a script compilation is active";
                case UnionAirActivity.TestRun:           return "a Unity Test Framework run is active";
                case UnionAirActivity.PlayMode:          return "the Unity Editor is in Play mode";
                case UnionAirActivity.AssetUpdate:       return "the Unity Editor is updating assets";
                case UnionAirActivity.Build:             return "a player build is active";
                case UnionAirActivity.BuildTargetSwitch: return "the active build target is being switched";
                default:                                 return "the Unity Editor is busy";
            }
        }
    }
}

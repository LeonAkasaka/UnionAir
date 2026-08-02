using System.Collections.Generic;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// The single place that answers "what is the Unity Editor busy with, and who owns it".
    /// </summary>
    /// <remarks>
    /// <para>
    /// Activities come from two sources. <b>Declared</b> activities — compilation, test runs,
    /// builds, and build target switches — are started by a UnionAir service or adopted from an
    /// external trigger, and their identity is held in <see cref="SessionState"/> so it survives a
    /// domain reload. <b>Observed</b> activities — Play mode and asset updating — are read from
    /// <c>EditorApplication</c> at query time, because Unity already tracks them and a second copy
    /// could only be wrong.
    /// </para>
    /// <para>
    /// Crash recovery follows the pattern in <c>CompileService.Initialize</c>: the durable payload
    /// lives in a record on disk while liveness lives here. <see cref="SessionState"/> is cleared
    /// when the Editor process restarts but survives a domain reload, so a record that still claims
    /// to be running with no activity open belongs to a process that died, and its owner finalizes
    /// it on the next initialization.
    /// </para>
    /// <para>
    /// This type reports and identifies; it does not enforce. Enforcement is per endpoint, through
    /// <see cref="UnionAirEndpointAttribute.BlockedDuring"/> and the request pipeline in
    /// <c>RestRouter</c>, so that what blocks a request is declared next to the request rather than
    /// hidden in a mutual-exclusion rule here.
    /// </para>
    /// </remarks>
    internal static class UnionAirActivityCoordinator
    {
        internal const string UnionAirSource = "unionAir";
        internal const string ExternalSource = "external";
        internal const string BuildSource = "build";

        private const string KeyPrefix = "UnionAir.Activity.";

        /// <summary>Activities whose identity is stored rather than observed.</summary>
        private static readonly UnionAirActivity[] Declared =
        {
            UnionAirActivity.Compile,
            UnionAirActivity.TestRun,
            UnionAirActivity.Build,
            UnionAirActivity.BuildTargetSwitch
        };

        /// <summary>
        /// Records that an activity has started and who owns it.
        /// </summary>
        /// <param name="activity">Declared activity being started.</param>
        /// <param name="source">Owner, usually <see cref="UnionAirSource"/> or <see cref="ExternalSource"/>.</param>
        /// <param name="id">Record id that owns the activity; may be empty for an adopted external activity.</param>
        internal static void Begin(UnionAirActivity activity, string source, string id)
        {
            SessionState.SetBool(ActiveKey(activity), true);
            SessionState.SetString(SourceKey(activity), source ?? "");
            SessionState.SetString(IdKey(activity), id ?? "");
        }

        /// <summary>
        /// Clears an activity, ignoring a caller that does not own it.
        /// </summary>
        /// <param name="activity">Declared activity being ended.</param>
        /// <param name="source">Source that started it; a mismatch is ignored.</param>
        /// <param name="id">Optional record id that must match the open activity.</param>
        /// <remarks>
        /// Ownership is checked so a late callback from a superseded record cannot clear the
        /// activity a newer record now owns.
        /// </remarks>
        internal static void End(UnionAirActivity activity, string source, string id = null)
        {
            if (!IsDeclaredActive(activity) || SourceOf(activity) != source)
                return;
            if (!string.IsNullOrEmpty(id) && IdOf(activity) != id)
                return;

            SessionState.EraseBool(ActiveKey(activity));
            SessionState.EraseString(SourceKey(activity));
            SessionState.EraseString(IdKey(activity));
        }

        /// <summary>Whether an activity is currently running, declared or observed.</summary>
        internal static bool IsActive(UnionAirActivity activity)
        {
            switch (activity)
            {
                case UnionAirActivity.PlayMode:
                    // isPlayingOrWillChangePlaymode, not isPlaying: work started during the
                    // transition is lost when the mode change completes.
                    return EditorApplication.isPlayingOrWillChangePlaymode;
                case UnionAirActivity.AssetUpdate:
                    return EditorApplication.isUpdating;
                case UnionAirActivity.Compile:
                    return IsDeclaredActive(UnionAirActivity.Compile) || EditorApplication.isCompiling;
                default:
                    return IsDeclaredActive(activity);
            }
        }

        /// <summary>Source that owns a declared activity, or an empty string.</summary>
        internal static string SourceOf(UnionAirActivity activity)
            => SessionState.GetString(SourceKey(activity), "");

        /// <summary>Record id that owns a declared activity, or an empty string.</summary>
        internal static string IdOf(UnionAirActivity activity)
            => SessionState.GetString(IdKey(activity), "");

        /// <summary>
        /// Record id that owns an activity, exposed only when UnionAir can name it.
        /// </summary>
        /// <remarks>
        /// An activity adopted from an external trigger has no id a client could poll, and
        /// reporting the empty string as if it were one would invite exactly that mistake.
        /// </remarks>
        internal static string PublicIdOf(UnionAirActivity activity)
        {
            if (!IsDeclaredActive(activity)) return null;
            var id = IdOf(activity);
            return string.IsNullOrEmpty(id) ? null : id;
        }

        /// <summary>Source that owns an activity, or <c>null</c> when the activity is not running.</summary>
        internal static string PublicSourceOf(UnionAirActivity activity)
        {
            if (!IsActive(activity)) return null;
            var source = SourceOf(activity);
            return string.IsNullOrEmpty(source) ? null : source;
        }

        /// <summary>
        /// Returns every currently active activity with its owner.
        /// </summary>
        internal static List<UnionAirActivityRecord> Snapshot()
        {
            var records = new List<UnionAirActivityRecord>();
            foreach (var activity in UnionAirActivityNames.Priority)
            {
                if (!IsActive(activity))
                    continue;

                records.Add(new UnionAirActivityRecord(
                    activity,
                    PublicSourceOf(activity),
                    PublicIdOf(activity)));
            }
            return records;
        }

        /// <summary>
        /// Returns what the Editor is busy with, or an inactive record when it is idle.
        /// </summary>
        internal static UnionAirActivityRecord Current()
            => UnionAirActivityDecision.SelectCurrent(Snapshot());

        /// <summary>
        /// Returns the highest-priority active activity a caller declared it cannot run during.
        /// </summary>
        internal static UnionAirActivityRecord Blocking(UnionAirActivity blockedDuring)
        {
            if (blockedDuring == UnionAirActivity.None)
                return UnionAirActivityRecord.None;
            return UnionAirActivityDecision.SelectBlocking(blockedDuring, Snapshot());
        }

        /// <summary>
        /// Whether an activity was explicitly started or adopted, ignoring observed Editor state.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="IsActive"/> for compilation: a cycle Unity is running that
        /// UnionAir has not adopted is active but not declared. Crash recovery keys on the declared
        /// flag, because that is the one whose absence proves nobody owns the record on disk.
        /// </remarks>
        internal static bool IsDeclared(UnionAirActivity activity)
            => IsDeclaredActive(activity);

        private static bool IsDeclaredActive(UnionAirActivity activity)
            => SessionState.GetBool(ActiveKey(activity), false);

        private static string ActiveKey(UnionAirActivity activity)
            => KeyPrefix + UnionAirActivityNames.Name(activity) + ".Active";

        private static string SourceKey(UnionAirActivity activity)
            => KeyPrefix + UnionAirActivityNames.Name(activity) + ".Source";

        private static string IdKey(UnionAirActivity activity)
            => KeyPrefix + UnionAirActivityNames.Name(activity) + ".Id";

        /// <summary>
        /// Declared activities, exposed for diagnostics and tests.
        /// </summary>
        internal static IReadOnlyList<UnionAirActivity> DeclaredActivities => Declared;
    }
}

using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// The Unity Test Framework run id for the run UnionAir started, kept so it can be canceled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// UnionAir issues its own run id: the framework returns its id only after
    /// <c>Execute</c> has dispatched the run, which would make it impossible to persist the record
    /// before starting anything. The framework's id is not an identity here, only the handle
    /// <c>TestRunnerApi.CancelTestRun</c> needs - no test callback carries a run id, so everything
    /// else correlates through the activity and the current record.
    /// </para>
    /// <para>
    /// It lives in <see cref="SessionState"/> because that matches the lifetime of the thing it
    /// refers to: a PlayMode run outlives the domain reload entering Play mode causes, and no run
    /// outlives the Editor process.
    /// </para>
    /// </remarks>
    internal static class TestRunCancellationHandle
    {
        private const string RunIdKey = "UnionAir.TestRun.FrameworkRunId";
        private const string OwnerKey = "UnionAir.TestRun.FrameworkRunOwner";

        /// <summary>
        /// Records the framework handle for a UnionAir run.
        /// </summary>
        /// <param name="unionAirRunId">Run id UnionAir issued and reported to the caller.</param>
        /// <param name="frameworkRunId">Id the Test Framework returned from <c>Execute</c>.</param>
        /// <remarks>
        /// An empty framework id is not stored. Cancellation is then unavailable for that run, which
        /// is reported as such rather than being sent to the framework as an id it never issued.
        /// </remarks>
        internal static void Set(string unionAirRunId, string frameworkRunId)
        {
            if (string.IsNullOrEmpty(unionAirRunId) || string.IsNullOrEmpty(frameworkRunId))
                return;

            SessionState.SetString(OwnerKey, unionAirRunId);
            SessionState.SetString(RunIdKey, frameworkRunId);
        }

        /// <summary>
        /// Looks up the framework handle for a UnionAir run.
        /// </summary>
        /// <returns><c>false</c> when no handle was stored, or when it belongs to another run.</returns>
        /// <remarks>
        /// The owner is checked rather than assumed, so a handle left behind by a run that ended
        /// without clearing it is inert instead of being applied to the next run.
        /// </remarks>
        internal static bool TryGet(string unionAirRunId, out string frameworkRunId)
        {
            frameworkRunId = "";
            if (string.IsNullOrEmpty(unionAirRunId))
                return false;
            if (SessionState.GetString(OwnerKey, "") != unionAirRunId)
                return false;

            frameworkRunId = SessionState.GetString(RunIdKey, "");
            return !string.IsNullOrEmpty(frameworkRunId);
        }

        internal static void Clear()
        {
            SessionState.EraseString(OwnerKey);
            SessionState.EraseString(RunIdKey);
        }

        /// <summary>Run the stored handle belongs to, or an empty string.</summary>
        /// <remarks>
        /// With <see cref="StoredRunId"/>, lets a test put back the handle of a run that was live
        /// while it exercised the start transaction. Reading it to decide anything would defeat the
        /// ownership check in <see cref="TryGet"/>.
        /// </remarks>
        internal static string Owner => SessionState.GetString(OwnerKey, "");

        /// <summary>Stored framework handle regardless of who owns it, or an empty string.</summary>
        internal static string StoredRunId => SessionState.GetString(RunIdKey, "");
    }
}

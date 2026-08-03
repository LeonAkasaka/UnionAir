using System;
using System.Collections.Generic;
using System.Text;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// One active Editor activity, with the identity of whatever owns it.
    /// </summary>
    internal readonly struct UnionAirActivityRecord
    {
        internal UnionAirActivityRecord(UnionAirActivity activity, string source, string id)
        {
            Activity = activity;
            Source = string.IsNullOrEmpty(source) ? null : source;
            Id = string.IsNullOrEmpty(id) ? null : id;
        }

        internal UnionAirActivity Activity { get; }

        /// <summary>
        /// Who started the activity: <c>unionAir</c>, <c>external</c>, or <c>null</c> when the
        /// activity is observed from Editor state and has no owner to name.
        /// </summary>
        internal string Source { get; }

        /// <summary>Record id owning the activity, or <c>null</c> when there is none.</summary>
        internal string Id { get; }

        internal bool IsActive => Activity != UnionAirActivity.None;

        internal static readonly UnionAirActivityRecord None =
            new UnionAirActivityRecord(UnionAirActivity.None, null, null);
    }

    /// <summary>
    /// Pure decisions over a set of active activities: which one to report, which one blocks a
    /// request, and what the rejection says.
    /// </summary>
    /// <remarks>
    /// Separated from <see cref="UnionAirActivityCoordinator"/> so the parts that decide can be
    /// tested without a live Editor. Everything the coordinator adds is reading
    /// <c>SessionState</c> and <c>EditorApplication</c>.
    /// </remarks>
    internal static class UnionAirActivityDecision
    {
        /// <summary>
        /// Activities the router rejects generically.
        /// </summary>
        /// <remarks>
        /// Play mode and test runs are deliberately excluded. Both already have shipped, documented
        /// gates with their own response bodies and their own place in the request pipeline — the
        /// test-run gate runs before the category check, Play mode after it, and Play mode supports
        /// a per-request opt-in that no generic rule can express. They are still reported in
        /// <c>blockedDuring</c>, so the metadata stays unified even though the enforcement is not.
        /// </remarks>
        internal const UnionAirActivity RouterMask =
            UnionAirActivity.Compile |
            UnionAirActivity.AssetUpdate |
            UnionAirActivity.Build |
            UnionAirActivity.BuildTargetSwitch;

        /// <summary>
        /// Whether a declared activity has no live record behind it and must be released.
        /// </summary>
        /// <param name="declared">Whether the activity flag is set.</param>
        /// <param name="declaredId">Record id the flag names; may be empty.</param>
        /// <param name="recordId">Id of the record the owning service restored, or <c>null</c> when it restored none.</param>
        /// <param name="recordIsActive">Whether that record is still in a non-terminal state.</param>
        /// <remarks>
        /// <para>
        /// Shared by every service that owns an activity, because each had written this predicate
        /// by hand and the three had already drifted apart — one omitted the terminal-state test
        /// and one inverted it. Recovery code is the wrong place for a condition that has to be
        /// re-derived per caller.
        /// </para>
        /// <para>
        /// A <b>terminal</b> record does not own a live activity. The flag is released last, after
        /// the record reaches its terminal state, so a terminal record paired with a flag that is
        /// still set means the release did not happen and nothing else will do it. Accepting such
        /// a pair as legitimate is what leaves the Editor reporting itself busy for the rest of the
        /// session.
        /// </para>
        /// <para>
        /// Written to depend on nothing but its arguments. A check that is only correct because of
        /// what some earlier method guarantees stops being correct the moment that method changes,
        /// and this one exists precisely to catch states nobody anticipated.
        /// </para>
        /// </remarks>
        internal static bool IsDebris(bool declared, string declaredId, string recordId, bool recordIsActive)
        {
            if (!declared)
                return false;

            // No record was restored for it at all.
            if (string.IsNullOrEmpty(recordId))
                return true;

            if (!recordIsActive)
                return true;

            return !string.Equals(recordId, declaredId ?? "", StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether a declared activity is debris, for an owner that is not the only possible one.
        /// </summary>
        /// <param name="declared">Whether the activity flag is set.</param>
        /// <param name="declaredSource">Source the flag names.</param>
        /// <param name="ownerSource">Source whose records the caller restored.</param>
        /// <param name="declaredId">Record id the flag names; may be empty.</param>
        /// <param name="recordId">Id of the record the owning service restored, or <c>null</c> when it restored none.</param>
        /// <param name="recordIsActive">Whether that record is still in a non-terminal state.</param>
        /// <remarks>
        /// <para>
        /// A flag owned by someone else says nothing about the caller's records, so it cannot be
        /// judged here. The test-run activity is the case that needs this: a run started from the
        /// Test Runner window is adopted with no record and an empty id, which is exactly the shape
        /// <see cref="IsDebris"/> reports as debris. Releasing it would end the activity for a run
        /// that is still going, and a PlayMode run reloads the domain, so the owning service
        /// re-initializes in the middle of one.
        /// </para>
        /// <para>
        /// Adopted activities are reconciled by whatever can observe the underlying work instead -
        /// for test runs, the Test Framework poll in <c>TestRunnerService.Update</c>, which can see
        /// whether the run is actually still active rather than inferring it from a missing record.
        /// </para>
        /// </remarks>
        internal static bool IsDebrisForOwner(
            bool declared,
            string declaredSource,
            string ownerSource,
            string declaredId,
            string recordId,
            bool recordIsActive)
        {
            if (!declared)
                return false;

            if (!string.Equals(declaredSource ?? "", ownerSource ?? "", StringComparison.Ordinal))
                return false;

            return IsDebris(declared, declaredId, recordId, recordIsActive);
        }

        /// <summary>
        /// Returns the activity that should be reported as what the Editor is busy with.
        /// </summary>
        internal static UnionAirActivityRecord SelectCurrent(IReadOnlyList<UnionAirActivityRecord> active)
            => SelectBlocking(~UnionAirActivity.None, active);

        /// <summary>
        /// Returns the highest-priority active activity that the caller declared it cannot run during.
        /// </summary>
        /// <param name="blockedDuring">Activities that must not be running.</param>
        /// <param name="active">Currently active activities, in any order.</param>
        internal static UnionAirActivityRecord SelectBlocking(
            UnionAirActivity blockedDuring,
            IReadOnlyList<UnionAirActivityRecord> active)
        {
            if (active == null || blockedDuring == UnionAirActivity.None)
                return UnionAirActivityRecord.None;

            foreach (var candidate in UnionAirActivityNames.Priority)
            {
                if ((blockedDuring & candidate) == 0)
                    continue;

                for (var i = 0; i < active.Count; i++)
                {
                    if (active[i].Activity == candidate)
                        return active[i];
                }
            }

            return UnionAirActivityRecord.None;
        }

        /// <summary>
        /// Builds the <c>409</c> body for a blocked request.
        /// </summary>
        /// <param name="blocking">Activity that blocked the request.</param>
        /// <param name="message">
        /// Message to report, or <c>null</c> to derive one from the activity. Callers that shipped
        /// a specific wording pass it here so the text a client already matches on does not change.
        /// </param>
        /// <remarks>
        /// A test run additionally emits the legacy <c>activeTestRun</c> object. That field is
        /// documented and clients read it, so it is kept alongside the unified
        /// <c>activeActivity</c> rather than replaced by it.
        /// </remarks>
        internal static string RejectionJson(UnionAirActivityRecord blocking, string message = null)
        {
            var sb = new StringBuilder();
            sb.Append("{\"error\":\"");
            sb.Append(RestResponse.EscapeJson(
                message ?? "This endpoint cannot be used while " +
                           UnionAirActivityNames.Describe(blocking.Activity) + "."));
            sb.Append("\",\"activeActivity\":");
            AppendActivity(sb, blocking);

            if (blocking.Activity == UnionAirActivity.TestRun)
            {
                sb.Append(",\"activeTestRun\":{\"source\":");
                sb.Append(RestResponse.FormatNullableString(blocking.Source));
                sb.Append(",\"id\":");
                sb.Append(RestResponse.FormatNullableString(blocking.Id));
                sb.Append("}");
            }

            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// Writes an activity as the <c>activeActivity</c> object, or <c>null</c> when none is active.
        /// </summary>
        internal static void AppendActivity(StringBuilder sb, UnionAirActivityRecord record)
        {
            if (!record.IsActive)
            {
                sb.Append("null");
                return;
            }

            sb.Append("{\"activity\":\"");
            sb.Append(UnionAirActivityNames.Name(record.Activity));
            sb.Append("\",\"source\":");
            sb.Append(RestResponse.FormatNullableString(record.Source));
            sb.Append(",\"id\":");
            sb.Append(RestResponse.FormatNullableString(record.Id));
            sb.Append("}");
        }

        /// <summary>
        /// Writes an activity mask as a JSON array of stable names, in priority order.
        /// </summary>
        internal static void AppendActivityArray(StringBuilder sb, UnionAirActivity activities)
        {
            sb.Append("[");
            var first = true;
            foreach (var candidate in UnionAirActivityNames.Priority)
            {
                if ((activities & candidate) == 0)
                    continue;

                if (!first) sb.Append(",");
                first = false;
                sb.Append("\"").Append(UnionAirActivityNames.Name(candidate)).Append("\"");
            }
            sb.Append("]");
        }
    }
}

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Liveness flag for the Unity Test Framework run UnionAir is tracking.
    /// </summary>
    /// <remarks>
    /// A named view over <see cref="UnionAirActivityCoordinator"/>'s
    /// <see cref="UnionAirActivity.TestRun"/> slot, keeping the vocabulary the Test Runner service
    /// and the documented <c>activeTestRun</c> response are written in.
    /// </remarks>
    internal static class UnionAirTestRunGate
    {
        internal const string UnionAirSource = UnionAirActivityCoordinator.UnionAirSource;
        internal const string ExternalSource = UnionAirActivityCoordinator.ExternalSource;

        internal static bool IsActive =>
            UnionAirActivityCoordinator.IsDeclared(UnionAirActivity.TestRun);

        internal static string Source => UnionAirActivityCoordinator.SourceOf(UnionAirActivity.TestRun);
        internal static string RunId => UnionAirActivityCoordinator.IdOf(UnionAirActivity.TestRun);

        internal static string PublicSource =>
            UnionAirActivityCoordinator.PublicSourceOf(UnionAirActivity.TestRun);

        /// <summary>
        /// Run id a client can poll, or <c>null</c> for a run UnionAir only adopted.
        /// </summary>
        /// <remarks>
        /// A run started from the Test Runner window has no UnionAir record behind it, so reporting
        /// its empty id would invite a client to poll a run that was never created here.
        /// </remarks>
        internal static string PublicRunId
            => IsActive && Source == UnionAirSource
                ? UnionAirActivityCoordinator.PublicIdOf(UnionAirActivity.TestRun)
                : null;

        internal static void Begin(string source, string runId)
            => UnionAirActivityCoordinator.Begin(UnionAirActivity.TestRun, source, runId);

        internal static void End(string source, string runId = null)
            => UnionAirActivityCoordinator.End(UnionAirActivity.TestRun, source, runId);
    }
}

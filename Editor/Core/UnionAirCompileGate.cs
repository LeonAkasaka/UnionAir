namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Liveness flag for the script compilation cycle UnionAir is tracking.
    /// </summary>
    /// <remarks>
    /// A named view over <see cref="UnionAirActivityCoordinator"/>'s
    /// <see cref="UnionAirActivity.Compile"/> slot. The coordinator owns the storage so one place
    /// can answer what the Editor is busy with; this type keeps the compile-specific vocabulary the
    /// compile service and its documented responses are written in.
    /// </remarks>
    internal static class UnionAirCompileGate
    {
        internal const string UnionAirSource = UnionAirActivityCoordinator.UnionAirSource;
        internal const string ExternalSource = UnionAirActivityCoordinator.ExternalSource;

        /// <summary>
        /// A compilation cycle owned by a player build rather than by the Editor.
        /// </summary>
        /// <remarks>
        /// Recorded distinctly so a build's player compilation is attributable to the build instead
        /// of being adopted as an unrelated external cycle. See <c>CompileService.OnCompilationStarted</c>.
        /// </remarks>
        internal const string BuildSource = UnionAirActivityCoordinator.BuildSource;

        /// <summary>
        /// Whether UnionAir is tracking a compilation cycle.
        /// </summary>
        /// <remarks>
        /// Deliberately the declared flag rather than the coordinator's broader
        /// <see cref="UnionAirActivity.Compile"/> view, which also counts a cycle Unity is running
        /// that UnionAir has not adopted. Crash recovery in <c>CompileService.Initialize</c> reads
        /// this to decide whether a record on disk still has an owner, and a cycle in flight during
        /// the reload would otherwise mask a record that nobody will ever finalize.
        /// </remarks>
        internal static bool IsActive =>
            UnionAirActivityCoordinator.IsDeclared(UnionAirActivity.Compile);

        internal static string Source => UnionAirActivityCoordinator.SourceOf(UnionAirActivity.Compile);
        internal static string Id => UnionAirActivityCoordinator.IdOf(UnionAirActivity.Compile);

        internal static string PublicSource =>
            UnionAirActivityCoordinator.PublicSourceOf(UnionAirActivity.Compile);

        internal static string PublicId =>
            UnionAirActivityCoordinator.PublicIdOf(UnionAirActivity.Compile);

        internal static void Begin(string source, string id)
            => UnionAirActivityCoordinator.Begin(UnionAirActivity.Compile, source, id);

        /// <summary>
        /// Clears the gate, ignoring requests that do not own it.
        /// </summary>
        /// <param name="source">Source that opened the gate.</param>
        /// <param name="id">Optional compile id that must match the open gate.</param>
        internal static void End(string source, string id = null)
            => UnionAirActivityCoordinator.End(UnionAirActivity.Compile, source, id);
    }
}

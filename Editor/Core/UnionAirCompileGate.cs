using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Liveness flag for the script compilation cycle UnionAir is tracking.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="UnionAirTestRunGate"/>. The durable record on disk holds the payload
    /// while this <see cref="SessionState"/> flag holds the liveness bit: a record that still
    /// claims to be running when no gate is open belongs to a cycle that died with its process.
    /// </remarks>
    internal static class UnionAirCompileGate
    {
        internal const string UnionAirSource = "unionAir";
        internal const string ExternalSource = "external";
        private const string ActiveKey = "UnionAir.Compile.Active";
        private const string SourceKey = "UnionAir.Compile.Source";
        private const string IdKey = "UnionAir.Compile.Id";

        internal static bool IsActive => SessionState.GetBool(ActiveKey, false);
        internal static string Source => SessionState.GetString(SourceKey, "");
        internal static string Id => SessionState.GetString(IdKey, "");
        internal static string PublicSource => IsActive && !string.IsNullOrEmpty(Source) ? Source : null;
        internal static string PublicId => IsActive && !string.IsNullOrEmpty(Id) ? Id : null;

        internal static void Begin(string source, string id)
        {
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(SourceKey, source ?? "");
            SessionState.SetString(IdKey, id ?? "");
        }

        /// <summary>
        /// Clears the gate, ignoring requests that do not own it.
        /// </summary>
        /// <param name="source">Source that opened the gate.</param>
        /// <param name="id">Optional compile id that must match the open gate.</param>
        internal static void End(string source, string id = null)
        {
            if (!IsActive || Source != source)
                return;
            if (!string.IsNullOrEmpty(id) && Id != id)
                return;

            SessionState.EraseBool(ActiveKey);
            SessionState.EraseString(SourceKey);
            SessionState.EraseString(IdKey);
        }
    }
}

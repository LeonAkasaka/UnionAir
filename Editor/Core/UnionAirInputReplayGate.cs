using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Liveness flag for the input replay UnionAir is driving.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="UnionAirCompileGate"/>. The durable record on disk holds the payload —
    /// a replay list has no size bound and would not fit in <see cref="SessionState"/> — while
    /// this flag holds the liveness bit: a record that still claims to be running when no gate is
    /// open belongs to a replay that died with its process.
    /// <para>
    /// The gate deliberately stays open across the domain reload that entering Play mode causes,
    /// which is what lets an armed replay survive it.
    /// </para>
    /// </remarks>
    internal static class UnionAirInputReplayGate
    {
        private const string ActiveKey = "UnionAir.InputReplay.Active";
        private const string IdKey = "UnionAir.InputReplay.Id";

        internal static bool IsActive => SessionState.GetBool(ActiveKey, false);
        internal static string Id => SessionState.GetString(IdKey, "");
        internal static string PublicId => IsActive && !string.IsNullOrEmpty(Id) ? Id : null;

        internal static void Begin(string id)
        {
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(IdKey, id ?? "");
        }

        /// <summary>
        /// Clears the gate, ignoring requests that do not own it.
        /// </summary>
        /// <param name="id">Replay id that must match the open gate.</param>
        internal static void End(string id = null)
        {
            if (!IsActive) return;
            if (!string.IsNullOrEmpty(id) && Id != id) return;

            SessionState.EraseBool(ActiveKey);
            SessionState.EraseString(IdKey);
        }
    }
}

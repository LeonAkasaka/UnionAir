using System;
using System.Globalization;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Identity of the running Unity Editor process and of the current assembly domain.
    /// </summary>
    /// <remarks>
    /// <see cref="SessionState"/> survives domain reloads but is cleared when the Editor restarts,
    /// which is what lets this type distinguish a reload from a fresh process. Initialization runs
    /// from a plain static constructor rather than an explicit entry point because
    /// <c>[InitializeOnLoad]</c> ordering between UnionAir subsystems is unspecified; whichever
    /// subsystem touches this type first initializes it.
    /// </remarks>
    internal static class UnionAirSession
    {
        private const string GenerationKey = "UnionAir.LifecycleGeneration";
        private const string SessionIdKey = "UnionAir.SessionId";
        private const string NextLogSequenceKey = "UnionAir.Log.NextSequence";
        private const string PreviousLogSameSessionKey = "UnionAir.Log.PreviousSameSession";
        private const string AutomaticPortKey = "UnionAir.AutomaticPort";

        /// <summary>
        /// Monotonic assembly-domain counter for the current Editor process, starting at 1.
        /// </summary>
        /// <remarks>The number of domain reloads so far is <c>Generation - 1</c>.</remarks>
        internal static int Generation { get; }

        /// <summary>Identifier regenerated once per Editor process.</summary>
        internal static string SessionId { get; }

        /// <summary>
        /// Whether this domain load is the first of a new Editor process rather than a reload.
        /// </summary>
        internal static bool IsNewEditorSession { get; }

        static UnionAirSession()
        {
            Generation = SessionState.GetInt(GenerationKey, 0) + 1;
            SessionState.SetInt(GenerationKey, Generation);

            var storedSessionId = SessionState.GetString(SessionIdKey, "");
            IsNewEditorSession = string.IsNullOrEmpty(storedSessionId);
            if (IsNewEditorSession)
            {
                storedSessionId = Guid.NewGuid().ToString("N");
                SessionState.SetString(SessionIdKey, storedSessionId);
            }
            SessionId = storedSessionId;
        }

        /// <summary>
        /// Forces SessionState-backed initialization on the Unity main thread before any background
        /// thread can observe this type.
        /// </summary>
        internal static void Initialize()
        {
            // The static constructor guarantees initialization before this method runs.
        }

        /// <summary>
        /// Reads the log sequence number to resume from after a domain reload.
        /// </summary>
        /// <returns>The next unused sequence number, or 0 when none was persisted.</returns>
        internal static long LoadNextLogSequence()
        {
            var raw = SessionState.GetString(NextLogSequenceKey, "");
            if (string.IsNullOrEmpty(raw))
                return 0;

            long value;
            return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0
                ? value
                : 0;
        }

        /// <summary>
        /// Persists the log sequence number so the next assembly domain can resume from it.
        /// </summary>
        /// <param name="value">Next unused sequence number.</param>
        internal static void SaveNextLogSequence(long value)
        {
            SessionState.SetString(NextLogSequenceKey, value.ToString(CultureInfo.InvariantCulture));
        }

        internal static bool LoadPreviousLogSameSession()
            => SessionState.GetBool(PreviousLogSameSessionKey, false);

        internal static void SavePreviousLogSameSession(bool value)
        {
            if (value) SessionState.SetBool(PreviousLogSameSessionKey, true);
            else SessionState.EraseBool(PreviousLogSameSessionKey);
        }

        internal static int LoadAutomaticPort()
            => SessionState.GetInt(AutomaticPortKey, 0);

        internal static void SaveAutomaticPort(int port)
        {
            if (UnionAirPortAllocator.IsValidConcretePort(port))
                SessionState.SetInt(AutomaticPortKey, port);
        }
    }
}

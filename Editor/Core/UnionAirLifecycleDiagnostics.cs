using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Retains a bounded lifecycle trace across domain reloads without flooding the Console.
    /// Detailed events are printed live only when diagnostic logging is enabled, while failures
    /// always dump the retained trace once per domain.
    /// </summary>
    internal static class UnionAirLifecycleDiagnostics
    {
        [Serializable]
        private sealed class TraceBuffer
        {
            public List<string> events = new List<string>();
        }

        private sealed class BackgroundEvent
        {
            internal string Message;
        }

        private const string SessionKey = "UnionAir.LifecycleTrace";
        private const int MaxEvents = 80;
        private const int MaxEventLength = 2000;
        private const int MaxSerializedLength = 48000;

        private static readonly object Sync = new object();
        private static readonly List<string> Events = LoadEvents();
        private static readonly ConcurrentQueue<BackgroundEvent> BackgroundEvents =
            new ConcurrentQueue<BackgroundEvent>();
        private static bool _failureDumped;

        static UnionAirLifecycleDiagnostics()
        {
        }

        /// <summary>
        /// Forces SessionState-backed initialization on the Unity main thread before any listener
        /// thread can be created.
        /// </summary>
        internal static void Initialize()
        {
            // The explicit static constructor guarantees initialization before this method runs.
        }

        internal static void Record(string message)
        {
            var entry = $"[{DateTime.Now:HH:mm:ss.fff}] {Limit(message, MaxEventLength)}";
            lock (Sync)
            {
                Events.Add(entry);
                PersistLocked(SerializeTrimmedLocked());
            }

            if (UnionAirSettings.DiagnosticLifecycleLogging)
                Debug.Log(message);
        }

        internal static void RecordFromBackground(string message)
        {
            BackgroundEvents.Enqueue(new BackgroundEvent
            {
                Message = message
            });
        }

        internal static void FlushBackground()
        {
            while (BackgroundEvents.TryDequeue(out var backgroundEvent))
                Record(backgroundEvent.Message);
        }

        internal static void DumpFailure(string reason)
        {
            if (_failureDumped)
                return;

            _failureDumped = true;
            string[] snapshot;
            lock (Sync)
                snapshot = Events.ToArray();

            var sb = new StringBuilder();
            sb.Append("[UnionAir] Lifecycle diagnostic trace: ");
            sb.Append(reason);
            for (var i = 0; i < snapshot.Length; i++)
            {
                sb.Append('\n');
                sb.Append(snapshot[i]);
            }
            Debug.LogError(sb.ToString());
        }

        private static List<string> LoadEvents()
        {
            try
            {
                var json = SessionState.GetString(SessionKey, "");
                if (string.IsNullOrEmpty(json))
                    return new List<string>();

                var buffer = JsonUtility.FromJson<TraceBuffer>(json);
                return buffer?.events ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static string SerializeTrimmedLocked()
        {
            while (Events.Count > MaxEvents)
                Events.RemoveAt(0);

            var json = JsonUtility.ToJson(new TraceBuffer { events = Events });
            while (Events.Count > 1 && json.Length > MaxSerializedLength)
            {
                Events.RemoveAt(0);
                json = JsonUtility.ToJson(new TraceBuffer { events = Events });
            }

            return json;
        }

        private static void PersistLocked(string json)
        {
            try
            {
                SessionState.SetString(SessionKey, json);
            }
            catch
            {
                // Diagnostics must never interfere with server lifecycle operations.
            }
        }

        private static string Limit(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value ?? "";
            return value.Substring(0, maxLength) + "...";
        }
    }
}

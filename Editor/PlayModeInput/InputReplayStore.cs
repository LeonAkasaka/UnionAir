using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Durable storage for input replay records.
    /// </summary>
    /// <remarks>
    /// The record — the schedule included — lives on disk rather than in <c>SessionState</c>,
    /// because a replay list is unbounded by design and would not fit the size a SessionState
    /// string can carry. SessionState holds only the liveness bit.
    /// <para>
    /// Like the Test Runner, only the current replay and the latest completed one are retained.
    /// Replays are driven one at a time and interactively, so a history of them has no reader.
    /// </para>
    /// </remarks>
    internal static class InputReplayStore
    {
        internal static readonly string StorageDirectory = Path.Combine("Library", "UnionAir", "InputReplay");
        internal static readonly string CurrentPath = Path.Combine(StorageDirectory, "current.json");
        internal static readonly string LatestPath = Path.Combine(StorageDirectory, "latest.json");

        /// <summary>
        /// Mints a sortable, filename-safe replay id.
        /// </summary>
        internal static string NewId()
            => "ir-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
               "-" + Guid.NewGuid().ToString("N").Substring(0, 6);

        /// <summary>
        /// Writes a record atomically, so a reader after an Editor crash sees either the previous
        /// record or the new one, never a partial file.
        /// </summary>
        internal static void Write(string path, InputReplayRecord record)
        {
            if (record == null) return;
            ProfilingArtifactStore.WriteAtomicJson(path, JsonUtility.ToJson(record));
        }

        /// <summary>
        /// Reads a record, returning null when the file is absent or unreadable.
        /// </summary>
        internal static InputReplayRecord Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var record = JsonUtility.FromJson<InputReplayRecord>(File.ReadAllText(path));
                if (record == null) return null;

                // JsonUtility leaves list fields null when the stored JSON omitted them.
                if (record.inputs == null) record.inputs = new System.Collections.Generic.List<InputReplayEventSpec>();
                if (record.events == null) record.events = new System.Collections.Generic.List<InputReplayEventResult>();
                return record;
            }
            catch (IOException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                // Malformed JSON from a partially written or hand-edited file.
                return null;
            }
        }

        /// <summary>Removes a record file, ignoring a file that is absent or locked.</summary>
        internal static void Delete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { /* Another process holds it; the next write replaces it. */ }
            catch (UnauthorizedAccessException) { /* Read-only or locked by a scanner. */ }
        }
    }
}

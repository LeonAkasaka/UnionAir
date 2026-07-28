using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Captures Unity Console log messages via <see cref="Application.logMessageReceived"/>,
    /// keeps the most recent entries in memory, and mirrors every entry to an append-only
    /// NDJSON file so history survives assembly domain reloads.
    /// </summary>
    /// <remarks>
    /// <see cref="Application.logMessageReceived"/> can be raised from background threads, so
    /// <see cref="OnLog"/> touches no Unity API: paths and the session identifier are resolved
    /// once on the main thread by <see cref="Initialize"/>, and write failures are recorded in a
    /// field instead of being logged (logging from inside the callback would re-enter it).
    /// </remarks>
    internal static class LogStore
    {
        private const int MaxEntries = 1000;

        /// <summary>Size at which the active NDJSON file is rotated.</summary>
        internal const long RotateThresholdBytes = 8L * 1024L * 1024L;

        /// <summary>Trailing byte count read back when rehydrating after a domain reload.</summary>
        private const int RehydrateTailBytes = 4 * 1024 * 1024;

        private static readonly List<LogEntry> _buffer = new List<LogEntry>(MaxEntries);
        private static readonly object _lock = new object();
        private static bool _capturing;
        private static bool _initialized;

        private static string _logPath;
        private static string _previousLogPath;
        private static string _sessionId = "";
        private static long _nextSequence;
        private static long _writtenBytes;
        private static StreamWriter _writer;
        private static string _writeError;
        private static bool _writeErrorReported;

        internal struct LogEntry
        {
            /// <summary>Monotonic sequence number within the current Editor session.</summary>
            public long Sequence;

            /// <summary>Console log message text.</summary>
            public string Message;

            /// <summary>Stack trace captured with the log message.</summary>
            public string StackTrace;

            /// <summary>Normalized log type: log, warning, error, exception, or assert.</summary>
            public string Type;      // "log" | "warning" | "error" | "exception" | "assert"

            /// <summary>UTC timestamp when the log entry was captured.</summary>
            public DateTime Timestamp;
        }

        /// <summary>Outcome of a filtered log query, including cursor metadata.</summary>
        internal struct LogQueryResult
        {
            /// <summary>Matching entries, newest first.</summary>
            public List<LogEntry> Entries;

            /// <summary>Oldest sequence still held in memory, or -1 when the buffer is empty.</summary>
            public long OldestSequence;

            /// <summary>Newest sequence still held in memory, or -1 when the buffer is empty.</summary>
            public long LatestSequence;

            /// <summary>Whether more matching entries existed beyond the requested limit.</summary>
            public bool HasMore;

            /// <summary>Whether entries after the cursor had already been evicted from memory.</summary>
            public bool Truncated;

            /// <summary>Identifier of the Editor session the entries belong to.</summary>
            public string SessionId;
        }

        [Serializable]
        private sealed class PersistedEntry
        {
            public long sequence;
            public string type = "";
            public string message = "";
            public string stackTrace = "";
            public string timestamp = "";
        }

        /// <summary>Path of the active NDJSON file, relative to the project root.</summary>
        internal static string LogFilePath
        {
            get { lock (_lock) { return _logPath; } }
        }

        /// <summary>
        /// Resolves session-scoped state on the Unity main thread.
        /// </summary>
        /// <remarks>
        /// A new Editor process rotates the previous file away and restarts sequence numbering,
        /// so a client that sees an unfamiliar session identifier knows its cursor is void. A
        /// domain reload instead reopens the same file and rehydrates the in-memory buffer.
        /// </remarks>
        internal static void Initialize()
        {
            if (_initialized) return;

            var directory = Path.Combine("Library", "UnionAir", "Logs");
            _logPath = Path.Combine(directory, "console.ndjson");
            _previousLogPath = Path.Combine(directory, "console.1.ndjson");
            _sessionId = UnionAirSession.SessionId;

            try
            {
                Directory.CreateDirectory(directory);

                if (UnionAirSession.IsNewEditorSession)
                {
                    Rotate();
                    _nextSequence = 0;
                }
                else
                {
                    _nextSequence = UnionAirSession.LoadNextLogSequence();
                    Rehydrate();
                }

                _writtenBytes = File.Exists(_logPath) ? new FileInfo(_logPath).Length : 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnionAir] Could not prepare the Console log file: " + ex.Message);
            }

            _initialized = true;
        }

        /// <summary>
        /// Begins capturing Unity console log messages.
        /// </summary>
        public static void StartCapturing()
        {
            if (_capturing) return;
            Initialize();
            OpenWriter();
            Application.logMessageReceived += OnLog;
            _capturing = true;
        }

        /// <summary>
        /// Stops capturing Unity console log messages and releases the log file handle.
        /// </summary>
        /// <remarks>
        /// Called before a domain reload and before the Editor quits, which is also where the
        /// sequence cursor is persisted so the next assembly domain resumes from it.
        /// </remarks>
        public static void StopCapturing()
        {
            if (!_capturing) return;
            Application.logMessageReceived -= OnLog;
            _capturing = false;

            lock (_lock)
            {
                CloseWriterLocked();
                try { UnionAirSession.SaveNextLogSequence(_nextSequence); }
                catch { /* SessionState is unavailable while the Editor is tearing down. */ }
            }

            ReportWriteErrorOnce();
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            lock (_lock)
            {
                var entry = new LogEntry
                {
                    Sequence   = _nextSequence++,
                    Message    = condition,
                    StackTrace = stackTrace,
                    Type       = LogTypeToString(type),
                    Timestamp  = DateTime.UtcNow,
                };

                if (_buffer.Count >= MaxEntries)
                    _buffer.RemoveAt(0);
                _buffer.Add(entry);

                AppendLocked(entry);
            }
        }

        private static void AppendLocked(LogEntry entry)
        {
            if (_writer == null) return;

            try
            {
                var line = FormatLine(entry);
                _writer.WriteLine(line);
                // Approximate: enough to drive rotation without measuring the encoded length.
                _writtenBytes += line.Length + 1;

                if (_writtenBytes >= RotateThresholdBytes)
                {
                    CloseWriterLocked();
                    Rotate();
                    _writtenBytes = 0;
                    OpenWriterLocked();
                }
            }
            catch (Exception ex)
            {
                // Never log from here: Debug would re-enter this callback.
                _writeError = ex.Message;
                CloseWriterLocked();
            }
        }

        /// <summary>
        /// Serializes one entry as a single NDJSON line.
        /// </summary>
        /// <param name="entry">Entry to serialize.</param>
        /// <returns>A JSON object with no embedded newlines.</returns>
        /// <remarks>
        /// Hand-written rather than using <c>JsonUtility</c> because this runs on whichever thread
        /// raised the log message, and <c>JsonUtility</c> is main-thread only.
        /// </remarks>
        internal static string FormatLine(LogEntry entry)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"sequence\":").Append(entry.Sequence.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"type\":\"").Append(RestResponse.EscapeJson(entry.Type)).Append('"');
            sb.Append(",\"message\":\"").Append(RestResponse.EscapeJson(entry.Message)).Append('"');
            sb.Append(",\"stackTrace\":\"").Append(RestResponse.EscapeJson(entry.StackTrace)).Append('"');
            sb.Append(",\"timestamp\":\"").Append(entry.Timestamp.ToString("o", CultureInfo.InvariantCulture)).Append('"');
            sb.Append('}');
            return sb.ToString();
        }

        private static void OpenWriter()
        {
            lock (_lock)
                OpenWriterLocked();
        }

        private static void OpenWriterLocked()
        {
            if (_writer != null || string.IsNullOrEmpty(_logPath)) return;

            try
            {
                // FileShare.Read lets GET /api/editor/logs.ndjson stream the file while it grows.
                var stream = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            }
            catch (Exception ex)
            {
                _writer = null;
                _writeError = ex.Message;
            }
        }

        private static void CloseWriterLocked()
        {
            if (_writer == null) return;
            try { _writer.Flush(); } catch { /* The handle may already be invalid. */ }
            try { _writer.Dispose(); } catch { /* The handle may already be invalid. */ }
            _writer = null;
        }

        private static void Rotate()
        {
            try
            {
                if (!File.Exists(_logPath)) return;
                if (File.Exists(_previousLogPath)) File.Delete(_previousLogPath);
                File.Move(_logPath, _previousLogPath);
            }
            catch (Exception ex)
            {
                _writeError = ex.Message;
            }
        }

        private static void Rehydrate()
        {
            if (!File.Exists(_logPath)) return;

            var restored = new List<LogEntry>(MaxEntries);
            using (var stream = new FileStream(
                       _logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var start = Math.Max(0L, stream.Length - RehydrateTailBytes);
                stream.Seek(start, SeekOrigin.Begin);

                using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                {
                    // A non-zero offset lands mid-line; drop that fragment.
                    if (start > 0) reader.ReadLine();

                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;

                        LogEntry entry;
                        if (!TryParseLine(line, out entry)) continue;

                        if (restored.Count >= MaxEntries)
                            restored.RemoveAt(0);
                        restored.Add(entry);
                    }
                }
            }

            _buffer.AddRange(restored);
            if (restored.Count > 0)
            {
                var highest = restored[restored.Count - 1].Sequence + 1;
                if (highest > _nextSequence) _nextSequence = highest;
            }
        }

        private static bool TryParseLine(string line, out LogEntry entry)
        {
            entry = default(LogEntry);
            try
            {
                var parsed = JsonUtility.FromJson<PersistedEntry>(line);
                if (parsed == null) return false;

                DateTime timestamp;
                DateTime.TryParse(
                    parsed.timestamp,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out timestamp);

                entry = new LogEntry
                {
                    Sequence   = parsed.sequence,
                    Message    = parsed.message ?? "",
                    StackTrace = parsed.stackTrace ?? "",
                    Type       = string.IsNullOrEmpty(parsed.type) ? "log" : parsed.type,
                    Timestamp  = timestamp,
                };
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ReportWriteErrorOnce()
        {
            if (_writeErrorReported || string.IsNullOrEmpty(_writeError)) return;
            _writeErrorReported = true;
            Debug.LogWarning("[UnionAir] Console log persistence is degraded: " + _writeError);
        }

        private static string LogTypeToString(LogType type)
        {
            switch (type)
            {
                case LogType.Warning:   return "warning";
                case LogType.Error:     return "error";
                case LogType.Exception: return "exception";
                case LogType.Assert:    return "assert";
                default:                return "log";
            }
        }

        /// <summary>
        /// Returns entries from newest to oldest, applying the cursor and optional filters.
        /// </summary>
        /// <param name="type">Log type filter, or <c>all</c> to include every type.</param>
        /// <param name="search">Optional case-insensitive message substring filter.</param>
        /// <param name="limit">Maximum number of entries to return.</param>
        /// <param name="since">Exclusive sequence cursor; negative disables the cursor.</param>
        /// <returns>Matching entries plus the cursor metadata the caller needs to advance.</returns>
        internal static LogQueryResult Query(string type, string search, int limit, long since)
        {
            LogQueryResult result;
            lock (_lock)
            {
                result = Filter(_buffer, type, search, limit, since);
                result.SessionId = _sessionId;
            }

            // Outside the lock: this logs, which re-enters OnLog on this thread.
            ReportWriteErrorOnce();
            return result;
        }

        /// <summary>
        /// Applies the cursor and filters to an oldest-first entry list.
        /// </summary>
        /// <param name="ordered">Entries ordered oldest first.</param>
        /// <param name="type">Log type filter, or <c>all</c> to include every type.</param>
        /// <param name="search">Optional case-insensitive message substring filter.</param>
        /// <param name="limit">Maximum number of entries to return.</param>
        /// <param name="since">Exclusive sequence cursor; negative disables the cursor.</param>
        /// <returns>Matching entries newest first, plus cursor metadata.</returns>
        /// <remarks>
        /// The cursor is applied before <paramref name="type"/> and <paramref name="search"/>, so
        /// <c>truncated</c> reports lost entries rather than filtered-out ones. Pure and free of
        /// Unity dependencies so it can be exercised directly.
        /// </remarks>
        internal static LogQueryResult Filter(
            IReadOnlyList<LogEntry> ordered, string type, string search, int limit, long since)
        {
            var result = new LogQueryResult
            {
                Entries = new List<LogEntry>(Math.Max(0, Math.Min(limit, MaxEntries))),
                OldestSequence = -1,
                LatestSequence = -1,
                SessionId = "",
            };

            // An empty buffer has evicted nothing; `latestSequence: -1` already tells the caller
            // the store is empty, and a reset is detected through the session identifier.
            if (ordered == null || ordered.Count == 0)
                return result;

            result.OldestSequence = ordered[0].Sequence;
            result.LatestSequence = ordered[ordered.Count - 1].Sequence;
            // Everything the caller asked for after `since` is gone if the buffer already moved past it.
            result.Truncated = since >= 0 && since + 1 < result.OldestSequence;

            for (var i = ordered.Count - 1; i >= 0; i--)
            {
                var entry = ordered[i];

                if (since >= 0 && entry.Sequence <= since)
                    break;

                if (!string.IsNullOrEmpty(type) && type != "all" && entry.Type != type)
                    continue;

                if (!string.IsNullOrEmpty(search) &&
                    (entry.Message == null ||
                     entry.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;

                if (result.Entries.Count >= limit)
                {
                    result.HasMore = true;
                    break;
                }

                result.Entries.Add(entry);
            }

            return result;
        }
    }
}

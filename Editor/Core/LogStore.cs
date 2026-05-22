using System;
using System.Collections.Generic;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Captures Unity Console log messages via <see cref="Application.logMessageReceived"/>
    /// and stores them in an in-memory ring buffer (max 1000 entries).
    /// </summary>
    internal static class LogStore
    {
        private const int MaxEntries = 1000;

        private static readonly List<LogEntry> _buffer = new List<LogEntry>(MaxEntries);
        private static readonly object _lock = new object();
        private static bool _capturing;

        internal struct LogEntry
        {
            /// <summary>Console log message text.</summary>
            public string Message;

            /// <summary>Stack trace captured with the log message.</summary>
            public string StackTrace;

            /// <summary>Normalized log type: log, warning, error, exception, or assert.</summary>
            public string Type;      // "log" | "warning" | "error" | "exception" | "assert"

            /// <summary>Local timestamp when the log entry was captured.</summary>
            public DateTime Timestamp;
        }

        /// <summary>
        /// Begins capturing Unity console log messages into the in-memory buffer.
        /// </summary>
        public static void StartCapturing()
        {
            if (_capturing) return;
            Application.logMessageReceived += OnLog;
            _capturing = true;
        }

        /// <summary>
        /// Stops capturing Unity console log messages.
        /// </summary>
        public static void StopCapturing()
        {
            if (!_capturing) return;
            Application.logMessageReceived -= OnLog;
            _capturing = false;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            var entry = new LogEntry
            {
                Message    = condition,
                StackTrace = stackTrace,
                Type       = LogTypeToString(type),
                Timestamp  = DateTime.Now,
            };

            lock (_lock)
            {
                if (_buffer.Count >= MaxEntries)
                    _buffer.RemoveAt(0);
                _buffer.Add(entry);
            }
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
        /// Returns entries from newest to oldest, applying optional filters.
        /// </summary>
        /// <param name="type">Log type filter, or <c>all</c> to include every type.</param>
        /// <param name="search">Optional case-insensitive message substring filter.</param>
        /// <param name="limit">Maximum number of entries to return.</param>
        /// <returns>Matching log entries from newest to oldest.</returns>
        public static List<LogEntry> GetLogs(string type, string search, int limit)
        {
            var result = new List<LogEntry>(Math.Min(limit, MaxEntries));

            lock (_lock)
            {
                for (int i = _buffer.Count - 1; i >= 0 && result.Count < limit; i--)
                {
                    var entry = _buffer[i];

                    if (!string.IsNullOrEmpty(type) && type != "all" && entry.Type != type)
                        continue;

                    if (!string.IsNullOrEmpty(search) &&
                        entry.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    result.Add(entry);
                }
            }

            return result;
        }

        /// <summary>
        /// Gets the current number of entries stored in the log buffer.
        /// </summary>
        public static int TotalCount
        {
            get { lock (_lock) { return _buffer.Count; } }
        }
    }
}

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
            public string Message;
            public string StackTrace;
            public string Type;      // "log" | "warning" | "error" | "exception" | "assert"
            public DateTime Timestamp;
        }

        public static void StartCapturing()
        {
            if (_capturing) return;
            Application.logMessageReceived += OnLog;
            _capturing = true;
        }

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

        public static int TotalCount
        {
            get { lock (_lock) { return _buffer.Count; } }
        }
    }
}

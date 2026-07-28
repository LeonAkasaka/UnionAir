using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Records every script compilation cycle as a durable, structured result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Compilation succeeds and the assembly domain reloads, taking the HTTP server with it, so
    /// an in-memory result would never survive long enough to be read. The record therefore
    /// reaches its terminal state on disk during <c>compilationFinished</c>, which Unity raises
    /// before the reload begins.
    /// </para>
    /// <para>
    /// Cycles started outside UnionAir are adopted rather than ignored: an IDE save followed by
    /// Unity's focus auto-refresh is the most common way a project recompiles.
    /// </para>
    /// </remarks>
    internal static class CompileService
    {
        private const double StaleGraceSeconds = 2.0;
        private const double CurrentFlushIntervalSeconds = 0.5;
        private const int MaxMessages = 200;
        private const int RetainedRecords = 20;

        private static readonly string StorageDirectory = Path.Combine("Library", "UnionAir", "Compile");
        private static readonly string CurrentPath = Path.Combine(StorageDirectory, "current.json");
        private static readonly string LatestPath = Path.Combine(StorageDirectory, "latest.json");
        private static readonly string RecordsDirectory = Path.Combine(StorageDirectory, "records");

        private static CompileRecord _current;
        private static CompileRecord _latest;
        private static object _activeContext;
        private static string _projectRoot = "";
        private static double _inactiveSince = -1;
        private static double _nextCurrentFlushAt;
        private static bool _currentDirty;
        private static bool _flushErrorLogged;

        internal static void Initialize()
        {
            _projectRoot = Directory.GetCurrentDirectory();
            _current = Load(CurrentPath);
            _latest = Load(LatestPath);

            // A record that still claims to be active with no gate open belongs to a cycle whose
            // process died. Finalization at reload handles the ordinary paths; this is the crash net.
            if (_current != null && _current.IsActive && !UnionAirCompileGate.IsActive)
            {
                Abort(_current, "The Unity Editor domain was reloaded or restarted during compilation.");
                SaveCurrentNow();
            }
        }

        internal static CompileRecord Current => _current;
        internal static CompileRecord Latest => _latest;

        /// <summary>
        /// Looks up a retained record by id.
        /// </summary>
        /// <param name="id">Compile id supplied by the caller.</param>
        /// <returns>The record, or <c>null</c> when it was never created or has been evicted.</returns>
        internal static CompileRecord Find(string id)
        {
            if (!CompileMessageParser.IsValidId(id)) return null;
            if (_current != null && _current.id == id) return _current;
            if (_latest != null && _latest.id == id) return _latest;
            return Load(RecordPath(id));
        }

        internal static void Update()
        {
            FlushCurrentIfDue();

            if (_current == null || !_current.IsActive)
            {
                _inactiveSince = -1;
                return;
            }

            // isCompiling covers the queued window too: it goes true as soon as compilation is
            // requested, so a request that never produced a cycle is detectable here.
            if (EditorApplication.isCompiling)
            {
                _inactiveSince = -1;
                return;
            }

            if (_inactiveSince < 0)
            {
                _inactiveSince = EditorApplication.timeSinceStartup;
                return;
            }

            if (EditorApplication.timeSinceStartup - _inactiveSince < StaleGraceSeconds)
                return;

            // Cancelling a compilation skips Unity's result processing entirely, so
            // compilationStarted can arrive with no matching compilationFinished.
            Abort(_current, "Compilation stopped without reporting a result.");
            Commit(_current);
            _inactiveSince = -1;
        }

        /// <summary>
        /// Finalizes an in-flight record before the assembly domain is torn down.
        /// </summary>
        /// <remarks>
        /// Flushing alone is not enough: the gate lives in <c>SessionState</c> and survives the
        /// reload, so a record left in <c>running</c> would still look live to
        /// <see cref="Initialize"/> and would only be reaped later by the watchdog.
        /// </remarks>
        internal static void FinalizeBeforeReload(string reason)
        {
            if (_current == null || !_current.IsActive)
            {
                FlushCurrentIfDirty();
                return;
            }

            Abort(_current, reason);
            Commit(_current);
        }

        internal static void OnCompilationStarted(object context)
        {
            // Adoption is decided before anything is aborted: a record this service queued is the
            // cycle now starting, not a stale one to discard.
            var adopted = UnionAirCompileGate.IsActive &&
                          UnionAirCompileGate.Source == UnionAirCompileGate.UnionAirSource &&
                          _current != null &&
                          _current.id == UnionAirCompileGate.Id &&
                          _current.state == "queued";

            if (!adopted)
            {
                // A previous cycle that never reported a result cannot be revived.
                if (_current != null && _current.IsActive)
                {
                    Abort(_current, "A new compilation started before this one reported a result.");
                    Commit(_current);
                }

                _current = NewRecord(UnionAirCompileGate.ExternalSource, NewId());
                UnionAirCompileGate.Begin(UnionAirCompileGate.ExternalSource, _current.id);
            }

            _activeContext = context;
            _current.state = "running";
            _current.startedAt = UtcNow();
            _inactiveSince = -1;
            SaveCurrentNow();
        }

        internal static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (_current == null || !_current.IsActive) return;

            var name = SafeAssemblyName(assemblyPath);
            var outputDirectory = SafeDirectory(assemblyPath);
            var entry = new CompileAssemblyRecord
            {
                name = name,
                path = CompileMessageParser.NormalizePath(assemblyPath, _projectRoot) ?? "",
                outputDirectory = outputDirectory,
                compiled = true,
            };

            if (messages != null)
            {
                foreach (var message in messages)
                {
                    var severity = SeverityOf(message.type);
                    if (severity == "error") { entry.errorCount++; _current.errorCount++; }
                    else if (severity == "warning") { entry.warningCount++; _current.warningCount++; }

                    if (_current.messages.Count >= MaxMessages)
                    {
                        _current.messagesTruncated = true;
                        continue;
                    }

                    _current.messages.Add(new CompileMessageRecord
                    {
                        severity = severity,
                        code = CompileMessageParser.ExtractCode(message.message) ?? "",
                        file = CompileMessageParser.NormalizePath(message.file, _projectRoot) ?? "",
                        line = message.line,
                        column = message.column,
                        assembly = name,
                        message = CompileMessageParser.Cap(CompileMessageParser.StripPrefix(message.message)),
                        raw = CompileMessageParser.Cap(message.message),
                    });
                }
            }

            _current.assemblies.Add(entry);
            MarkCurrentDirty();
        }

        internal static void OnAssemblyCompilationNotRequired(string assemblyPath)
        {
            if (_current == null || !_current.IsActive) return;
            _current.unchangedAssemblyCount++;
            MarkCurrentDirty();
        }

        internal static void OnCompilationFinished(object context)
        {
            if (_current == null || !_current.IsActive) return;

            // The context token identifies one cycle, which keeps a concurrent AssemblyBuilder
            // build from resolving this record.
            if (_activeContext != null && !ReferenceEquals(context, _activeContext)) return;

            _current.state = "completed";
            _current.result = _current.errorCount > 0
                ? "failed"
                : _current.assemblies.Count > 0 ? "succeeded" : "upToDate";

            _current.target = ResolveTarget(_current);
            SortMessages(_current);
            Commit(_current);
            _activeContext = null;
        }

        private static void Commit(CompileRecord record)
        {
            record.finishedAt = UtcNow();
            record.lifecycleGenerationAtFinish = UnionAirSession.Generation;
            record.durationSeconds = DurationSeconds(record);

            SaveCurrentNow();
            SaveRecord(record);

            // Only Editor cycles become `latest`: a player build or an AssemblyBuilder cycle
            // classified wrongly then simply fails to replace the agent's view, and stays
            // reachable by id.
            if (record.target == "editor")
            {
                _latest = record;
                TryWrite(LatestPath, record, "latest compile record");
            }

            UnionAirCompileGate.End(record.source, record.id);
            TrimRecords(record.id);
        }

        private static void Abort(CompileRecord record, string reason)
        {
            record.state = "aborted";
            record.result = string.IsNullOrEmpty(record.startedAt) ? "notStarted" : "aborted";
            if (string.IsNullOrEmpty(record.error)) record.error = reason;
            if (string.IsNullOrEmpty(record.finishedAt)) record.finishedAt = UtcNow();
        }

        internal static CompileRecord NewRecord(string source, string id)
        {
            return new CompileRecord
            {
                id = id,
                source = source,
                state = "queued",
                sessionId = UnionAirSession.SessionId,
                requestedAt = UtcNow(),
                lifecycleGenerationAtRequest = UnionAirSession.Generation,
            };
        }

        internal static void SetCurrent(CompileRecord record)
        {
            _current = record;
            SaveCurrentNow();
        }

        internal static string NewId()
            => "c-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
               "-" + Guid.NewGuid().ToString("N").Substring(0, 6);

        private static string ResolveTarget(CompileRecord record)
        {
            var sawPlayer = false;
            foreach (var assembly in record.assemblies)
            {
                var target = CompileMessageParser.ClassifyTarget(assembly.outputDirectory);
                if (target == "editor") return "editor";
                if (target == "player") sawPlayer = true;
            }

            if (sawPlayer) return "player";
            // A cycle with nothing to compile reports no assemblies; in the Editor that is an
            // up-to-date Editor cycle.
            return record.assemblies.Count == 0 ? "editor" : "other";
        }

        private static void SortMessages(CompileRecord record)
        {
            record.messages.Sort((a, b) =>
            {
                var rank = SeverityRank(a.severity).CompareTo(SeverityRank(b.severity));
                if (rank != 0) return rank;
                var file = string.CompareOrdinal(a.file ?? "", b.file ?? "");
                if (file != 0) return file;
                return a.line.CompareTo(b.line);
            });
        }

        private static int SeverityRank(string severity)
        {
            switch (severity)
            {
                case "error":   return 0;
                case "warning": return 1;
                default:        return 2;
            }
        }

        private static string SeverityOf(CompilerMessageType type)
        {
            switch (type)
            {
                case CompilerMessageType.Error:   return "error";
                case CompilerMessageType.Warning: return "warning";
                default:                          return "info";
            }
        }

        private static string SafeAssemblyName(string assemblyPath)
        {
            if (string.IsNullOrEmpty(assemblyPath)) return "";
            try { return Path.GetFileNameWithoutExtension(assemblyPath); }
            catch { return assemblyPath; }
        }

        private static string SafeDirectory(string assemblyPath)
        {
            if (string.IsNullOrEmpty(assemblyPath)) return "";
            try
            {
                var directory = Path.GetDirectoryName(assemblyPath) ?? "";
                return CompileMessageParser.NormalizePath(directory, _projectRoot) ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static double DurationSeconds(CompileRecord record)
        {
            DateTime started, finished;
            if (!TryParseUtc(record.startedAt, out started) || !TryParseUtc(record.finishedAt, out finished))
                return 0;

            var seconds = (finished - started).TotalSeconds;
            return seconds > 0 ? seconds : 0;
        }

        private static bool TryParseUtc(string value, out DateTime result)
        {
            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out result);
        }

        private static void MarkCurrentDirty()
        {
            // A cycle reports 50+ assemblies; an atomic write per callback would be a disk storm.
            if (!_currentDirty)
                _nextCurrentFlushAt = EditorApplication.timeSinceStartup + CurrentFlushIntervalSeconds;
            _currentDirty = true;
        }

        private static void FlushCurrentIfDue()
        {
            if (!_currentDirty || _current == null) return;
            if (EditorApplication.timeSinceStartup < _nextCurrentFlushAt) return;
            FlushCurrentIfDirty();
        }

        private static void FlushCurrentIfDirty()
        {
            if (!_currentDirty || _current == null) return;
            SaveCurrentNow();
        }

        private static void SaveCurrentNow()
        {
            if (_current == null) return;
            TryWrite(CurrentPath, _current, "current compile record");
            _currentDirty = false;
            _nextCurrentFlushAt = 0;
        }

        private static void SaveRecord(CompileRecord record)
        {
            if (!CompileMessageParser.IsValidId(record.id)) return;
            TryWrite(RecordPath(record.id), record, "compile record");
        }

        private static void TryWrite(string path, CompileRecord record, string what)
        {
            try
            {
                ProfilingArtifactStore.WriteAtomicJson(path, JsonUtility.ToJson(record));
                _flushErrorLogged = false;
            }
            catch (Exception ex)
            {
                if (_flushErrorLogged) return;
                _flushErrorLogged = true;
                Debug.LogWarning($"[UnionAir] Could not write the {what}; UnionAir will retry: {ex.Message}");
            }
        }

        private static string RecordPath(string id) => Path.Combine(RecordsDirectory, id + ".json");

        private static void TrimRecords(string protectedId)
        {
            try
            {
                if (!Directory.Exists(RecordsDirectory)) return;

                var files = new List<FileInfo>(new DirectoryInfo(RecordsDirectory).GetFiles("*.json"));
                if (files.Count <= RetainedRecords) return;

                files.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                for (var i = RetainedRecords; i < files.Count; i++)
                {
                    if (Path.GetFileNameWithoutExtension(files[i].Name) == protectedId) continue;
                    try { files[i].Delete(); }
                    catch { /* Another process may already have removed it. */ }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnionAir] Could not trim retained compile records: " + ex.Message);
            }
        }

        private static CompileRecord Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var record = JsonUtility.FromJson<CompileRecord>(File.ReadAllText(path));
                if (record == null) return null;

                record.assemblies = record.assemblies ?? new List<CompileAssemblyRecord>();
                record.messages = record.messages ?? new List<CompileMessageRecord>();
                return record;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnionAir] Could not read a stored compile record: " + ex.Message);
                return null;
            }
        }

        internal static int RetainedRecordCount => RetainedRecords;

        private static string UtcNow() => DateTime.UtcNow.ToString("o");
    }
}

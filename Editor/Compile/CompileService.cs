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
        private const double RunningGraceSeconds = 2.0;

        /// <summary>
        /// Grace applied to a queued record before it is treated as never started.
        /// </summary>
        /// <remarks>
        /// Longer than <see cref="RunningGraceSeconds"/> because Unity does not always begin a
        /// requested cycle on the next tick, and a queued record that is reaped early would report
        /// a failure for a compilation that then runs normally.
        /// </remarks>
        private const double QueuedGraceSeconds = 30.0;
        private const double CurrentFlushIntervalSeconds = 0.5;
        private const int MaxMessages = 200;
        private const int RetainedRecords = 20;

        private static readonly string StorageDirectory = Path.Combine("Library", "UnionAir", "Compile");
        private static readonly string CurrentPath = Path.Combine(StorageDirectory, "current.json");
        private static readonly string LatestPath = Path.Combine(StorageDirectory, "latest.json");
        private static readonly string RecordsDirectory = Path.Combine(StorageDirectory, "records");

        /// <summary>Marks that the retained records were scanned and held no eligible cycle.</summary>
        private const string LatestRebuildExhaustedKey = "UnionAir.Compile.LatestRebuildExhausted";

        private static CompileRecord _current;
        private static CompileRecord _latest;
        private static object _activeContext;
        private static string _projectRoot = "";
        private static double _inactiveSince = -1;
        private static bool _startDispatched;
        private static double _startRequestedAt;
        private static double _nextCurrentFlushAt;
        private static bool _currentDirty;
        private static bool _flushErrorLogged;

        internal static void Initialize()
        {
            _projectRoot = Directory.GetCurrentDirectory();
            _current = Load(CurrentPath);
            _latest = Load(LatestPath);
            ReconcileLatest();

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
            string path;
            return TryGetRecordPath(id, out path) ? Load(path) : null;
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

            // A queued record is only stale once the compilation request has actually been issued.
            // The deadline keeps that exemption bounded: a deferred start that never runs must
            // still resolve rather than leave the caller polling forever.
            if (_current.state == "queued" && !_startDispatched)
            {
                if (EditorApplication.timeSinceStartup - _startRequestedAt < QueuedGraceSeconds)
                {
                    _inactiveSince = -1;
                    return;
                }

                Abort(_current, "The compilation request was never dispatched by the Unity Editor.");
                Commit(_current);
                _inactiveSince = -1;
                return;
            }

            if (_inactiveSince < 0)
            {
                _inactiveSince = EditorApplication.timeSinceStartup;
                return;
            }

            var grace = _current.state == "queued" ? QueuedGraceSeconds : RunningGraceSeconds;
            if (EditorApplication.timeSinceStartup - _inactiveSince < grace)
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
                // A build runs its own player compilation. Attributing that cycle to the build,
                // rather than adopting it as an unrelated external one, is what lets the build
                // record point at it by id and keeps it out of a caller's view of Editor compiles.
                var buildId = UnionAirActivityCoordinator.IsDeclared(UnionAirActivity.Build)
                    ? UnionAirActivityCoordinator.IdOf(UnionAirActivity.Build)
                    : "";
                var ownedByBuild = !string.IsNullOrEmpty(buildId);

                // A previous cycle that never reported a result cannot be revived. The reason names
                // the build when there is one, so a record aborted by a build's player compilation
                // is not mistaken for one lost to a hand-started cycle.
                if (_current != null && _current.IsActive)
                {
                    Abort(_current, ownedByBuild
                        ? "The player compilation for build " + buildId + " started before this one reported a result."
                        : "A new compilation started before this one reported a result.");
                    Commit(_current);
                }

                var source = ownedByBuild
                    ? UnionAirCompileGate.BuildSource
                    : UnionAirCompileGate.ExternalSource;
                _current = NewRecord(source, NewId());
                _current.buildId = buildId;
                UnionAirCompileGate.Begin(source, _current.id);
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
            _current.result = CompileDecision.ResolveCompletedResult(
                _current.errorCount,
                _current.assemblies.Count);

            _current.target = CompileDecision.ResolveTarget(_current.assemblies, _current.source);
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

            // Only completed Editor cycles become `latest`. Restricting by state keeps an aborted
            // cycle from discarding the last real result, and restricting by target means a
            // player or AssemblyBuilder cycle that was classified wrongly merely fails to replace
            // the caller's view instead of corrupting it. Both stay reachable by id.
            if (record.target == "editor" && record.state == "completed")
            {
                _latest = record;
                TryWrite(LatestPath, record, "latest compile record");
                // An eligible record now exists, so a future rebuild is worth attempting again.
                SessionState.EraseBool(LatestRebuildExhaustedKey);
            }

            UnionAirCompileGate.End(record.source, record.id);
            TrimRecords(record.id);
        }

        private static void Abort(CompileRecord record, string reason)
        {
            record.state = "aborted";
            record.result = CompileDecision.ResolveAbortedResult(record.startedAt);
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

        /// <summary>
        /// Registers a queued record and defers the work that can tear down the domain.
        /// </summary>
        /// <param name="record">Record created for this request.</param>
        /// <param name="refresh">Whether to import pending asset changes first.</param>
        /// <param name="clean">Whether to clear the build cache and rebuild everything.</param>
        /// <remarks>
        /// The record is persisted and the response is sent before any compilation work begins.
        /// Refreshing and compiling block the Unity main thread and can end in a domain reload,
        /// which would drop the connection before the caller learned the id it needs to poll.
        /// </remarks>
        internal static void ScheduleStart(CompileRecord record, bool refresh, bool clean)
        {
            _startDispatched = false;
            _startRequestedAt = EditorApplication.timeSinceStartup;
            SetCurrent(record);
            UnionAirCompileGate.Begin(UnionAirCompileGate.UnionAirSource, record.id);

            var id = record.id;
            EditorApplication.CallbackFunction pending = null;
            pending = () =>
            {
                EditorApplication.update -= pending;
                RunStart(id, refresh, clean);
            };

            // EditorApplication.update, not delayCall: delayCall does not run while the Editor is
            // in the background, which left the request queued indefinitely. update is the same
            // pump that already serves HTTP requests, so it ticks regardless of focus.
            EditorApplication.update += pending;
        }

        private static void RunStart(string id, bool refresh, bool clean)
        {
            if (_current == null || _current.id != id || _current.state != "queued")
            {
                _startDispatched = true;
                return;
            }

            try
            {
                // A newly written .cs file belongs to no assembly until it is imported.
                if (refresh)
                {
                    var sceneConflicts = LoadedSceneDiskChangeGuard.FindConflicts();
                    if (sceneConflicts.Count > 0)
                    {
                        _startDispatched = true;
                        Abort(_current, LoadedSceneDiskChangeGuard.BuildAbortReason(sceneConflicts));
                        Commit(_current);
                        return;
                    }

                    AssetDatabase.Refresh();
                }

                // Refresh starts a cycle by itself when scripts changed; requesting another would
                // queue a redundant one. RequestScriptCompilation still forces a cycle when Unity
                // has no compiled assembly to report, which makes an upToDate result observable.
                if (!EditorApplication.isCompiling)
                {
                    CompilationPipeline.RequestScriptCompilation(
                        clean
                            ? RequestScriptCompilationOptions.CleanBuildCache
                            : RequestScriptCompilationOptions.None);
                }

                // Only now can a queued record be judged against the watchdog.
                _startDispatched = true;
            }
            catch (Exception ex)
            {
                _startDispatched = true;
                if (_current != null && _current.id == id && _current.IsActive)
                {
                    Abort(_current, "Compilation could not be requested: " + ex.Message);
                    Commit(_current);
                }
            }
        }

        /// <summary>
        /// Whether a compilation cycle is already in progress.
        /// </summary>
        internal static bool IsBusy => UnionAirCompileGate.IsActive || EditorApplication.isCompiling;

        internal static string NewId()
            => "c-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
               "-" + Guid.NewGuid().ToString("N").Substring(0, 6);

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
            string path;
            if (!TryGetRecordPath(record.id, out path)) return;
            TryWrite(path, record, "compile record");
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

        internal static bool TryGetRecordPath(string id, out string path)
        {
            path = null;
            if (!CompileMessageParser.IsValidId(id)) return false;

            try
            {
                var root = Path.GetFullPath(RecordsDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                var candidate = Path.GetFullPath(Path.Combine(RecordsDirectory, id + ".json"));
                if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
                path = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TrimRecords(string protectedId)
        {
            try
            {
                TrimRecordFiles(RecordsDirectory, RetainedRecords, protectedId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnionAir] Could not trim retained compile records: " + ex.Message);
            }
        }

        internal static void TrimRecordFiles(string directory, int keep, string protectedId)
            => TrimRecordFiles(directory, keep, protectedId, null);

        /// <summary>
        /// Deletes retained records past the limit, keeping the protected id.
        /// </summary>
        /// <param name="directory">Directory holding the per-id record files.</param>
        /// <param name="keep">Number of records to retain.</param>
        /// <param name="protectedId">Record that must survive regardless of age.</param>
        /// <param name="delete">Deletion to apply; defaults to deleting the file.</param>
        /// <remarks>
        /// The deletion is injectable so the resilience of this loop can be exercised without
        /// depending on the host platform: an open file cannot be deleted on Windows, but on
        /// Unix <c>unlink</c> succeeds and the inode simply outlives the directory entry.
        /// </remarks>
        internal static void TrimRecordFiles(
            string directory, int keep, string protectedId, Action<FileInfo> delete)
        {
            if (!Directory.Exists(directory)) return;

            var files = new List<FileInfo>(new DirectoryInfo(directory).GetFiles("*.json"));
            if (files.Count <= keep) return;

            files.Sort((a, b) =>
            {
                var aProtected = Path.GetFileNameWithoutExtension(a.Name) == protectedId;
                var bProtected = Path.GetFileNameWithoutExtension(b.Name) == protectedId;
                if (aProtected != bProtected) return aProtected ? -1 : 1;
                return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc);
            });

            var remove = delete ?? (file => file.Delete());
            for (var i = keep; i < files.Count; i++)
            {
                // Per file: one undeletable record must not stop the rest from being trimmed,
                // or retention degrades silently until the directory is cleaned by hand.
                try { remove(files[i]); }
                catch (IOException) { /* Another process holds or already removed it. */ }
                catch (UnauthorizedAccessException) { /* Read-only or locked by a scanner. */ }
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
                CompileDecision.NormalizeCompletedTarget(record);
                return record;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnionAir] Could not read a stored compile record: " + ex.Message);
                return null;
            }
        }

        internal static int RetainedRecordCount => RetainedRecords;

        internal static List<CompileRecord> ListRetained(out bool completed)
            => LoadRetainedNewestFirst(RecordsDirectory, out completed);

        private static void ReconcileLatest()
        {
            if (_latest != null && _latest.state == "completed" && _latest.target == "editor")
                return;

            // Whatever was loaded is not an eligible latest, so drop it before deciding whether
            // to rescan. Keeping it would let a stale non-Editor record be served as `latest`
            // whenever the rescan is skipped or a previous cleanup failed.
            _latest = null;

            if (!SessionState.GetBool(LatestRebuildExhaustedKey, false))
            {
                bool scanCompleted;
                var records = LoadRetainedNewestFirst(RecordsDirectory, out scanCompleted);
                _latest = CompileDecision.SelectLatestEditor(records);
                if (_latest != null)
                {
                    TryWrite(LatestPath, _latest, "latest compile record");
                    return;
                }

                // Only a scan that ran to completion proves there is nothing to find. Marking a
                // scan that threw as exhausted would disable rebuilding for the whole Editor
                // session over one transient I/O error.
                if (scanCompleted)
                    SessionState.SetBool(LatestRebuildExhaustedKey, true);
            }

            DeleteStaleLatestFile();
        }

        private static void DeleteStaleLatestFile()
        {
            try
            {
                if (File.Exists(LatestPath)) File.Delete(LatestPath);
            }
            catch (Exception ex)
            {
                // Retried on the next reload: the early return above is guarded by the in-memory
                // record, not by the presence of this file.
                Debug.LogWarning("[UnionAir] Could not remove a stale latest compile record: " + ex.Message);
            }
        }

        /// <summary>
        /// Loads the retained records, newest first.
        /// </summary>
        /// <param name="directory">Directory holding the per-id record files.</param>
        /// <param name="completed">Whether the directory could be enumerated without error.</param>
        /// <remarks>
        /// A record that fails to parse is skipped and does not make the scan incomplete; only a
        /// failure to enumerate the directory does, because that is the case where the caller
        /// cannot conclude anything about what the directory holds.
        /// </remarks>
        internal static List<CompileRecord> LoadRetainedNewestFirst(
            string directory,
            out bool completed)
            => LoadRetainedNewestFirst(directory, out completed, null);

        internal static List<CompileRecord> LoadRetainedNewestFirst(
            string directory,
            out bool completed,
            Func<DirectoryInfo, FileInfo[]> enumerate)
        {
            var records = new List<CompileRecord>();
            completed = false;
            try
            {
                if (!Directory.Exists(directory))
                {
                    completed = true;
                    return records;
                }

                var directoryInfo = new DirectoryInfo(directory);
                var files = new List<FileInfo>(
                    enumerate == null ? directoryInfo.GetFiles("*.json") : enumerate(directoryInfo));
                foreach (var file in files)
                {
                    var record = Load(file.FullName);
                    if (record != null) records.Add(record);
                }
                records.Sort(CompileDecision.CompareRecordsNewestFirst);

                completed = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnionAir] Could not enumerate retained compile records: " + ex.Message);
            }

            return records;
        }

        private static string UtcNow() => DateTime.UtcNow.ToString("o");
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Runs player builds in the Editor process and records what they produced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A build occupies the Unity main thread for its whole duration — measured at roughly 72
    /// seconds for a Windows player — and UnionAir dispatches HTTP requests from
    /// <c>EditorApplication.update</c> on that same thread. Nothing is served while a build runs.
    /// The request that starts one therefore persists a queued record and answers <c>202</c>
    /// <b>before</b> scheduling the work, exactly as <c>CompileService.ScheduleStart</c> does; a
    /// response written after <c>BuildPlayer</c> returns would arrive on a connection the caller
    /// gave up on.
    /// </para>
    /// <para>
    /// Live progress and cancellation are not offered, because they are not achievable in process:
    /// Unity exposes no player-build cancellation API, and no callback can run while the main
    /// thread is inside <c>BuildPipeline.BuildPlayer</c>.
    /// </para>
    /// </remarks>
    internal static class BuildService
    {
        private const int MaxReportMessages = 200;
        private const int RetainedRecords = 20;

        private static readonly string StorageDirectory = Path.Combine("Library", "UnionAir", "Builds");
        private static readonly string CurrentPath = Path.Combine(StorageDirectory, "current.json");
        private static readonly string RecordsDirectory = Path.Combine(StorageDirectory, "records");

        private static BuildRecord _current;

        internal static BuildRecord Current => _current;
        internal static int RetainedRecordCount => RetainedRecords;

        internal static void Initialize()
        {
            _current = Load(CurrentPath);

            // A record that still claims to be active with no build activity open belongs to a
            // build whose process died. Nothing else can finalize it: unlike compilation, a build
            // has no Unity callback that fires when it is interrupted.
            if (_current != null && _current.IsActive &&
                !UnionAirActivityCoordinator.IsDeclared(UnionAirActivity.Build))
            {
                Abort(_current, "The Unity Editor was closed or reloaded the assembly domain during the build.");
                Commit(_current);
            }
        }

        /// <summary>
        /// Finalizes an in-flight record before the assembly domain is torn down.
        /// </summary>
        internal static void FinalizeBeforeReload(string reason)
        {
            if (_current == null || !_current.IsActive) return;
            Abort(_current, reason);
            Commit(_current);
        }

        /// <summary>Whether a build is queued or running.</summary>
        internal static bool IsBusy => UnionAirActivityCoordinator.IsDeclared(UnionAirActivity.Build);

        /// <summary>State of the in-flight build, or <c>null</c> when none is active.</summary>
        internal static string ActiveState => _current != null && _current.IsActive ? _current.state : null;

        internal static string NewId()
            => "b-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
               "-" + Guid.NewGuid().ToString("N").Substring(0, 6);

        /// <summary>
        /// Looks up a retained record by id.
        /// </summary>
        internal static BuildRecord Find(string id)
        {
            if (!CompileMessageParser.IsValidId(id)) return null;
            if (_current != null && _current.id == id) return _current;
            string path;
            return TryGetRecordPath(id, out path) ? Load(path) : null;
        }

        /// <summary>Loads every retained record, newest first.</summary>
        internal static List<BuildRecord> ListRetained(out bool completed)
        {
            var records = new List<BuildRecord>();
            completed = false;
            try
            {
                if (!Directory.Exists(RecordsDirectory))
                {
                    completed = true;
                    return records;
                }

                foreach (var file in new DirectoryInfo(RecordsDirectory).GetFiles("*.json"))
                {
                    var record = Load(file.FullName);
                    if (record != null) records.Add(record);
                }
                records.Sort(CompareNewestFirst);
                completed = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnionAir] Could not enumerate retained build records: " + ex.Message);
            }

            return records;
        }

        internal static int CompareNewestFirst(BuildRecord left, BuildRecord right)
        {
            var finished = string.CompareOrdinal(right?.finishedAt ?? "", left?.finishedAt ?? "");
            if (finished != 0) return finished;
            var requested = string.CompareOrdinal(right?.requestedAt ?? "", left?.requestedAt ?? "");
            if (requested != 0) return requested;
            return string.CompareOrdinal(right?.id ?? "", left?.id ?? "");
        }

        /// <summary>
        /// Deletes a retained record and its artifacts.
        /// </summary>
        /// <returns><c>false</c> when the record no longer exists.</returns>
        internal static bool Delete(string id)
        {
            var record = Find(id);
            if (record == null) return false;

            BuildArtifactStore.Delete(id);

            string path;
            if (TryGetRecordPath(id, out path))
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch (Exception ex)
                {
                    Debug.LogWarning("[UnionAir] Could not delete a build record: " + ex.Message);
                }
            }

            if (_current != null && _current.id == id)
            {
                _current = null;
                try { if (File.Exists(CurrentPath)) File.Delete(CurrentPath); }
                catch (Exception) { }
            }

            return true;
        }

        /// <summary>
        /// Creates a queued record for a request.
        /// </summary>
        internal static BuildRecord NewRecord(string id, BuildRequestOptions options, IReadOnlyList<string> scenes)
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            var record = new BuildRecord
            {
                id = id,
                source = UnionAirActivityCoordinator.UnionAirSource,
                state = "queued",
                buildTarget = target.ToString(),
                buildTargetGroup = BuildTargetCatalog.GroupOf(target).ToString(),
                namedBuildTarget = BuildTargetCatalog.Active().TargetName,
                sessionId = UnionAirSession.SessionId,
                requestedAt = UtcNow(),
                lifecycleGenerationAtRequest = UnionAirSession.Generation,
                development = options.development,
                allowDebugging = options.allowDebugging,
                connectProfiler = options.connectProfiler,
                deepProfiling = options.deepProfiling,
                waitForPlayerConnection = options.waitForPlayerConnection,
                clean = options.clean,
                strictMode = options.strictMode,
            };

            record.scenes.AddRange(scenes);
            return record;
        }

        /// <summary>
        /// Registers a queued record and defers the build itself.
        /// </summary>
        /// <remarks>
        /// The record is persisted and the response sent before the build starts. The deferred work
        /// runs from <c>EditorApplication.update</c> rather than <c>delayCall</c>, because
        /// <c>delayCall</c> does not run while the Editor is in the background — the normal state
        /// for an agent-driven workflow.
        /// </remarks>
        internal static void ScheduleStart(BuildRecord record)
        {
            _current = record;
            SaveCurrent();
            UnionAirActivityCoordinator.Begin(
                UnionAirActivity.Build, UnionAirActivityCoordinator.UnionAirSource, record.id);

            var id = record.id;
            EditorApplication.CallbackFunction pending = null;
            pending = () =>
            {
                EditorApplication.update -= pending;
                Run(id);
            };
            EditorApplication.update += pending;
        }

        private static void Run(string id)
        {
            if (_current == null || _current.id != id || _current.state != "queued")
                return;

            var record = _current;
            record.state = "running";
            record.startedAt = UtcNow();

            string directory;
            try
            {
                directory = BuildArtifactStore.CreateDirectory(id);
            }
            catch (Exception ex)
            {
                Abort(record, "The build output directory could not be created: " + ex.Message);
                Commit(record);
                return;
            }

            // BuildPlayer opens the build scenes itself and raises sceneClosed for the loaded one
            // without a matching sceneOpened, which drops its disk baseline. Captured here and
            // restored below only for scenes the build left byte-identical, so the next refresh
            // does not report a scene as externally changed just because a build ran.
            var sceneBaselines = LoadedSceneDiskChangeGuard.CaptureLoadedSceneBaselines();

            record.outputDirectory = directory;
            record.outputPath = BuildArtifactStore.NormalizePath(Path.Combine(
                directory,
                BuildArtifactStore.OutputFileName(PlayerSettings.productName, EditorUserBuildSettings.activeBuildTarget)));
            SaveCurrent();

            try
            {
                var options = new BuildPlayerOptions
                {
                    scenes = record.scenes.ToArray(),
                    locationPathName = record.outputPath,
                    target = EditorUserBuildSettings.activeBuildTarget,
                    targetGroup = BuildTargetCatalog.GroupOf(EditorUserBuildSettings.activeBuildTarget),
                    options = ResolveBuildOptions(record),
                };

                if (options.targetGroup == BuildTargetGroup.Standalone)
                    options.subtarget = (int)EditorUserBuildSettings.standaloneBuildSubtarget;

                // Everything past this point is synchronous. No request is served, no callback
                // fires, and there is no way to cancel from outside.
                var report = BuildPipeline.BuildPlayer(options);

                // Snapshotted immediately: BuildReport is a Unity object backed by native state
                // that a later domain reload discards, and the record has to outlive it.
                record.report = Snapshot(report);
                record.result = record.report == null ? "unknown" : record.report.result;
                record.state = record.result == "succeeded" ? "completed" : "failed";
                if (record.state == "failed" && string.IsNullOrEmpty(record.error))
                    record.error = "The player build did not succeed. See report.messages for the reported errors.";
            }
            catch (Exception ex)
            {
                record.state = "failed";
                record.result = "failed";
                record.error = "The player build threw: " + ex.Message;
            }

            LoadedSceneDiskChangeGuard.RestoreUnchangedBaselines(sceneBaselines);

            // The compile record the build's own player compilation produced, attributed to this
            // build rather than adopted as an unrelated cycle.
            var compile = CompileService.Current;
            if (compile != null && compile.buildId == record.id)
                record.compileId = compile.id;

            record.outputBytes = BuildArtifactStore.DirectoryBytes(record.id);
            Commit(record);
        }

        private static BuildOptions ResolveBuildOptions(BuildRecord record)
        {
            var options = BuildOptions.None;
            if (record.development) options |= BuildOptions.Development;
            if (record.allowDebugging) options |= BuildOptions.AllowDebugging;
            if (record.connectProfiler) options |= BuildOptions.ConnectWithProfiler;
            if (record.deepProfiling) options |= BuildOptions.EnableDeepProfilingSupport;
            if (record.waitForPlayerConnection) options |= BuildOptions.WaitForPlayerConnection;
            if (record.clean) options |= BuildOptions.CleanBuildCache;
            if (record.strictMode) options |= BuildOptions.StrictMode;
            return options;
        }

        private static BuildReportRecord Snapshot(BuildReport report)
        {
            if (report == null) return null;

            var summary = report.summary;
            var snapshot = new BuildReportRecord
            {
                result = ResultName(summary.result),
                platform = summary.platform.ToString(),
                platformGroup = summary.platformGroup.ToString(),
                outputPath = BuildArtifactStore.NormalizePath(summary.outputPath ?? ""),
                startedAt = Utc(summary.buildStartedAt),
                endedAt = Utc(summary.buildEndedAt),
                totalTimeSeconds = summary.totalTime.TotalSeconds,
                totalSizeBytes = summary.totalSize > long.MaxValue ? long.MaxValue : (long)summary.totalSize,
                totalErrors = (int)summary.totalErrors,
                totalWarnings = (int)summary.totalWarnings,
            };

            try
            {
                foreach (var step in report.steps)
                {
                    if (step.messages == null) continue;
                    foreach (var message in step.messages)
                    {
                        var severity = SeverityOf(message.type);
                        // Info is dropped: a successful build reports thousands of them, and the
                        // record is written to disk and returned in full on every poll.
                        if (severity == "info") continue;

                        if (snapshot.messages.Count >= MaxReportMessages)
                        {
                            snapshot.messagesTruncated = true;
                            continue;
                        }

                        snapshot.messages.Add(new BuildMessageRecord
                        {
                            severity = severity,
                            step = step.name ?? "",
                            message = CompileMessageParser.Cap(message.content ?? ""),
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnionAir] Could not read build report messages: " + ex.Message);
            }

            return snapshot;
        }

        /// <summary>
        /// Formats a <c>BuildReport</c> timestamp as UTC ISO 8601.
        /// </summary>
        /// <remarks>
        /// Unity reports these already in UTC but with <see cref="DateTimeKind.Unspecified"/>.
        /// Calling <c>ToUniversalTime()</c> on such a value subtracts the machine's offset a second
        /// time, which on a UTC+9 machine reported a build that started nine hours before it was
        /// requested. Only a value explicitly marked local is converted.
        /// </remarks>
        private static string Utc(DateTime value)
        {
            var utc = value.Kind == DateTimeKind.Local
                ? value.ToUniversalTime()
                : DateTime.SpecifyKind(value, DateTimeKind.Utc);
            return utc.ToString("o");
        }

        private static string ResultName(BuildResult result)
        {
            switch (result)
            {
                case BuildResult.Succeeded: return "succeeded";
                case BuildResult.Failed:    return "failed";
                case BuildResult.Cancelled: return "cancelled";
                default:                    return "unknown";
            }
        }

        private static string SeverityOf(LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    return "error";
                case LogType.Warning:
                    return "warning";
                default:
                    return "info";
            }
        }

        private static void Abort(BuildRecord record, string reason)
        {
            record.state = "aborted";
            record.result = string.IsNullOrEmpty(record.startedAt) ? "notStarted" : "aborted";
            if (string.IsNullOrEmpty(record.error)) record.error = reason;
            if (string.IsNullOrEmpty(record.finishedAt)) record.finishedAt = UtcNow();
        }

        private static void Commit(BuildRecord record)
        {
            record.finishedAt = UtcNow();
            record.lifecycleGenerationAtFinish = UnionAirSession.Generation;
            record.durationSeconds = DurationSeconds(record);

            SaveCurrent();
            SaveRecord(record);
            WriteArtifactReport(record);

            UnionAirActivityCoordinator.End(
                UnionAirActivity.Build, UnionAirActivityCoordinator.UnionAirSource, record.id);

            BuildArtifactStore.Trim(record.id);
            TrimRecords(record.id);
        }

        /// <summary>
        /// Writes the record next to the output, so the artifact directory explains itself.
        /// </summary>
        /// <remarks>
        /// The record under <c>Library/</c> is the authoritative one and outlives the artifact:
        /// retention removes hundred-megabyte directories long before the twenty small records
        /// need trimming, and a client asking about an old build should learn what it produced
        /// rather than get a 404.
        /// </remarks>
        private static void WriteArtifactReport(BuildRecord record)
        {
            if (string.IsNullOrEmpty(record.outputDirectory)) return;

            try
            {
                var path = Path.Combine(record.outputDirectory, "report.json");
                ProfilingArtifactStore.WriteAtomicJson(path, record.ToApiJson(true));
                record.reportPath = BuildArtifactStore.NormalizePath(path);
                SaveRecord(record);
                SaveCurrent();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnionAir] Could not write the build report artifact: " + ex.Message);
            }
        }

        private static double DurationSeconds(BuildRecord record)
        {
            DateTime started, finished;
            if (!TryParseUtc(record.startedAt, out started) || !TryParseUtc(record.finishedAt, out finished))
                return 0;
            var seconds = (finished - started).TotalSeconds;
            return seconds > 0 ? seconds : 0;
        }

        private static bool TryParseUtc(string value, out DateTime result)
            => DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out result);

        private static void SaveCurrent()
        {
            if (_current == null) return;
            TryWrite(CurrentPath, _current, "current build record");
        }

        private static void SaveRecord(BuildRecord record)
        {
            string path;
            if (!TryGetRecordPath(record.id, out path)) return;
            TryWrite(path, record, "build record");
        }

        private static void TryWrite(string path, BuildRecord record, string what)
        {
            try
            {
                ProfilingArtifactStore.WriteAtomicJson(path, JsonUtility.ToJson(record));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnionAir] Could not write the {what}: {ex.Message}");
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
                CompileService.TrimRecordFiles(RecordsDirectory, RetainedRecords, protectedId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnionAir] Could not trim retained build records: " + ex.Message);
            }
        }

        private static BuildRecord Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var record = JsonUtility.FromJson<BuildRecord>(File.ReadAllText(path));
                if (record == null) return null;
                record.scenes = record.scenes ?? new List<string>();
                if (record.report != null)
                    record.report.messages = record.report.messages ?? new List<BuildMessageRecord>();
                return record;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnionAir] Could not read a stored build record: " + ex.Message);
                return null;
            }
        }

        private static string UtcNow() => DateTime.UtcNow.ToString("o");
    }
}

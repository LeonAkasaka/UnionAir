using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Switches the active build target and tracks the switch as a long-running activity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is not a settings write. Switching targets reimports every asset for the new platform,
    /// recompiles, and ends in a domain reload, so it needs the same asynchronous treatment as a
    /// build — and unlike a build, the reload is the <em>expected</em> path rather than an
    /// interruption. The record is therefore not finalized before a reload; it is finalized
    /// afterwards, by comparing the active target against the one that was requested.
    /// </para>
    /// <para>
    /// While the switch runs the Editor is unavailable in the same way it is during a build, and
    /// for longer. UnionAir answers nothing until it settles.
    /// </para>
    /// </remarks>
    internal static class BuildTargetSwitchService
    {
        private const int RetainedRecords = 20;

        private static readonly string StorageDirectory =
            Path.Combine("Library", "UnionAir", "BuildTargetSwitches");
        private static readonly string CurrentPath = Path.Combine(StorageDirectory, "current.json");
        private static readonly string RecordsDirectory = Path.Combine(StorageDirectory, "records");

        private static BuildTargetSwitchRecord _current;

        internal static BuildTargetSwitchRecord Current => _current;
        internal static int RetainedRecordCount => RetainedRecords;

        internal static bool IsBusy =>
            UnionAirActivityCoordinator.IsDeclared(UnionAirActivity.BuildTargetSwitch);

        internal static string ActiveState => _current != null && _current.IsActive ? _current.state : null;

        /// <summary>
        /// Reconciles an in-flight record after a domain load.
        /// </summary>
        /// <remarks>
        /// Runs on every domain load, which is what makes the record survive the reload the switch
        /// itself causes. The activity flag lives in <c>SessionState</c>: still set means the same
        /// Editor process is coming back from the reload, absent means the process died.
        /// </remarks>
        internal static void Initialize()
        {
            _current = Load(CurrentPath);
            Reconcile();

            // The mirror case: an activity with no live record behind it. Reached when the record
            // was never written, or was written and then lost. Nothing else would ever close it,
            // and the project would report itself busy for the rest of the Editor session.
            if (UnionAirActivityDecision.IsDebris(
                    UnionAirActivityCoordinator.IsDeclared(UnionAirActivity.BuildTargetSwitch),
                    UnionAirActivityCoordinator.IdOf(UnionAirActivity.BuildTargetSwitch),
                    _current == null ? null : _current.id,
                    _current != null && _current.IsActive))
            {
                UnionAirActivityCoordinator.ClearDebris(
                    UnionAirActivity.BuildTargetSwitch, "no live build target switch record was restored for it.");
            }
        }

        private static void Reconcile()
        {
            if (_current == null || !_current.IsActive)
                return;

            if (!UnionAirActivityCoordinator.IsDeclared(UnionAirActivity.BuildTargetSwitch))
            {
                Finish(_current, "aborted",
                    "The Unity Editor was closed or restarted during the build target switch.");
                return;
            }

            // Back from the reload the switch caused. Unity does not report the outcome across it,
            // so the active target is the only evidence of what happened.
            var active = EditorUserBuildSettings.activeBuildTarget.ToString();
            if (string.Equals(active, _current.requestedTarget, StringComparison.Ordinal))
            {
                Finish(_current, "completed", null);
                return;
            }

            if (_current.state == "switching")
            {
                Finish(_current, "failed",
                    "The Unity Editor reloaded without switching to " + _current.requestedTarget +
                    "; the active target is " + active + ".");
                return;
            }

            // Still queued: the deferred start lived in the previous domain and did not survive
            // the reload, so nothing will ever run it. Left alone it would sit queued forever
            // behind an activity that never closes.
            Finish(_current, "failed",
                "The Unity Editor reloaded the assembly domain before the switch to " +
                _current.requestedTarget + " started; the active target is still " + active + ".");
        }

        internal static string NewId()
            => "t-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
               "-" + Guid.NewGuid().ToString("N").Substring(0, 6);

        internal static BuildTargetSwitchRecord Find(string id)
        {
            if (!CompileMessageParser.IsValidId(id)) return null;
            if (_current != null && _current.id == id) return _current;
            string path;
            return TryGetRecordPath(id, out path) ? Load(path) : null;
        }

        /// <summary>
        /// Registers a queued record and defers the switch.
        /// </summary>
        /// <returns><c>false</c> when the record could not be persisted and nothing was started.</returns>
        /// <remarks>
        /// <para>
        /// The response is sent before the switch begins, for the same reason a build's is: the
        /// reimport blocks the Unity main thread and the domain reload drops the connection, so a
        /// response written afterwards would never reach the caller that needs the id.
        /// </para>
        /// <para>
        /// The activity is opened only once the record is on disk, and here that is load-bearing
        /// rather than merely careful. The switch's terminal path is <see cref="Initialize"/> on
        /// the far side of the reload, and it can only reconcile a record it can read. A switch
        /// started without one would leave an activity that nothing ever closes.
        /// </para>
        /// </remarks>
        internal static bool ScheduleSwitch(
            BuildTargetSwitchRecord record,
            BuildTargetGroup group,
            BuildTarget target)
        {
            var previous = _current;
            _current = record;
            if (!SaveCurrent())
            {
                _current = previous;
                return false;
            }

            UnionAirActivityCoordinator.Begin(
                UnionAirActivity.BuildTargetSwitch, UnionAirActivityCoordinator.UnionAirSource, record.id);

            var id = record.id;
            EditorApplication.CallbackFunction pending = null;
            pending = () =>
            {
                EditorApplication.update -= pending;
                Run(id, group, target);
            };
            EditorApplication.update += pending;
            return true;
        }

        private static void Run(string id, BuildTargetGroup group, BuildTarget target)
        {
            if (_current == null || _current.id != id || _current.state != "queued")
                return;

            var record = _current;
            record.state = "switching";
            record.startedAt = UtcNow();

            // Persisted before the call, not after: the switch can end in a domain reload that
            // never returns here, and a record still saying "queued" would look like a request
            // that was never dispatched.
            SaveCurrent();

            bool switched;
            try
            {
                switched = EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
            }
            catch (Exception ex)
            {
                Finish(record, "failed", "The build target switch threw: " + ex.Message);
                return;
            }

            if (!switched)
            {
                Finish(record, "failed",
                    "The Unity Editor refused to switch to " + target +
                    ". This usually means the platform module is not installed or another operation held the Editor.");
                return;
            }

            // Reaching here without a reload means Unity completed the switch inline. When a reload
            // does follow, Initialize finalizes the record on the other side instead.
            if (EditorUserBuildSettings.activeBuildTarget == target)
            {
                Finish(record, "completed", null);
                return;
            }

            // Unity accepted the switch but the active target has not changed yet, which is what a
            // pending domain reload looks like from here. The record deliberately stays `switching`
            // so Initialize can reconcile it on the far side. Failing it here instead would report
            // a switch that is about to succeed as failed, on a path this package cannot currently
            // verify: only the Standalone modules are installed on the machine it is developed
            // against, and a switch within one group completes inline without a reload.
            Debug.Log(
                "[UnionAir] The switch to " + target + " was accepted but the active target is still " +
                EditorUserBuildSettings.activeBuildTarget +
                "; leaving the record switching for the reload to resolve.");
        }

        private static void Finish(BuildTargetSwitchRecord record, string state, string error)
        {
            record.state = state;
            record.activeTarget = EditorUserBuildSettings.activeBuildTarget.ToString();
            record.finishedAt = UtcNow();
            record.lifecycleGenerationAtFinish = UnionAirSession.Generation;
            record.durationSeconds = DurationSeconds(record);
            if (!string.IsNullOrEmpty(error) && string.IsNullOrEmpty(record.error))
                record.error = error;

            SaveCurrent();
            SaveRecord(record);
            UnionAirActivityCoordinator.End(
                UnionAirActivity.BuildTargetSwitch, UnionAirActivityCoordinator.UnionAirSource, record.id);
            TrimRecords(record.id);
        }

        internal static BuildTargetSwitchRecord NewRecord(
            string id, BuildTarget target, BuildTargetGroup group, string namedBuildTarget)
            => new BuildTargetSwitchRecord
            {
                id = id,
                source = UnionAirActivityCoordinator.UnionAirSource,
                state = "queued",
                requestedTarget = target.ToString(),
                requestedTargetGroup = group.ToString(),
                requestedNamedBuildTarget = namedBuildTarget ?? "",
                previousTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                sessionId = UnionAirSession.SessionId,
                requestedAt = UtcNow(),
                lifecycleGenerationAtRequest = UnionAirSession.Generation,
            };

        internal static List<BuildTargetSwitchRecord> ListRetained(out bool completed)
        {
            var records = new List<BuildTargetSwitchRecord>();
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
                records.Sort((a, b) =>
                {
                    var finished = string.CompareOrdinal(b?.finishedAt ?? "", a?.finishedAt ?? "");
                    if (finished != 0) return finished;
                    return string.CompareOrdinal(b?.requestedAt ?? "", a?.requestedAt ?? "");
                });
                completed = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnionAir] Could not enumerate retained build target switches: " + ex.Message);
            }
            return records;
        }

        private static double DurationSeconds(BuildTargetSwitchRecord record)
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

        private static bool SaveCurrent()
        {
            if (_current == null) return false;
            return TryWrite(CurrentPath, _current, "current build target switch record");
        }

        private static void SaveRecord(BuildTargetSwitchRecord record)
        {
            string path;
            if (!TryGetRecordPath(record.id, out path)) return;
            TryWrite(path, record, "build target switch record");
        }

        private static bool TryWrite(string path, BuildTargetSwitchRecord record, string what)
        {
            string error;
            if (ProfilingArtifactStore.TryWriteAtomicJson(path, JsonUtility.ToJson(record), out error))
                return true;

            Debug.LogWarning($"[UnionAir] Could not write the {what}: {error}");
            return false;
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
            try { CompileService.TrimRecordFiles(RecordsDirectory, RetainedRecords, protectedId); }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnionAir] Could not trim retained build target switches: " + ex.Message);
            }
        }

        private static BuildTargetSwitchRecord Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return JsonUtility.FromJson<BuildTargetSwitchRecord>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnionAir] Could not read a stored build target switch record: " + ex.Message);
                return null;
            }
        }

        private static string UtcNow() => DateTime.UtcNow.ToString("o");
    }
}

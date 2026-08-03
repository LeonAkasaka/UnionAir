using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal static class TestRunnerService
    {
        private const double StaleGateGraceSeconds = 2.0;
        private const double FrameworkPollIntervalSeconds = 0.5;
        private const double CurrentFlushIntervalSeconds = 0.5;
        private static readonly string StorageDirectory = Path.Combine("Library", "UnionAir", "TestRuns");
        private static readonly string CurrentPath = Path.Combine(StorageDirectory, "current.json");
        private static readonly string LatestPath = Path.Combine(StorageDirectory, "latest.json");
        private static readonly string LatestXmlPath = Path.Combine(StorageDirectory, "latest.xml");
        private static readonly string LatestXmlBackupPath = Path.Combine(StorageDirectory, "latest.backup.xml");
        private static readonly string LatestJsonBackupPath = Path.Combine(StorageDirectory, "latest.backup.json");
        private static readonly string LatestTransactionPath = Path.Combine(StorageDirectory, "latest.pending.json");
        private static TestRunRecord _current;
        private static TestRunRecord _latest;
        private static double _frameworkInactiveSince = -1;
        private static double _nextFrameworkPollAt;
        private static double _nextCurrentFlushAt;
        private static bool _currentDirty;
        private static bool _currentFlushErrorLogged;
        private static bool _frameworkInspectionErrorLogged;
        private static Func<bool> _isFrameworkRunActive;

        internal static void Initialize()
        {
            ResolveFrameworkRunInspector();
            RecoverLatestResultTransaction();
            _current = Load(CurrentPath);
            _latest = Load(LatestPath);
            if (_latest != null && _latest.resultFileAvailable && !HasMatchingLatestResult(_latest))
                _latest.resultFileAvailable = false;
            if (_current != null && _current.resultFileAvailable &&
                (_latest == null || _latest.id != _current.id || !_latest.resultFileAvailable))
                _current.resultFileAvailable = false;

            // A record that still claims to be active belongs to a live run only while the activity
            // this service opened for it is still there. Ownership is checked rather than mere
            // liveness: an external run adopted after this record was lost would otherwise keep
            // protecting it, leaving a record nothing can finish and a cancel that looks for a
            // handle no one stored.
            if (_current != null && _current.IsActive && !OwnsActiveRun())
            {
                MarkAborted(_current, "Unity Test Framework run state was lost during reload.");
                FinishProfiling(_current, _current.error);
                SaveCurrentNow();
            }

            // The mirror case: an activity with no live record behind it, which nothing would ever
            // close. Scoped to activities UnionAir owns, because a run started from the Test Runner
            // window is adopted with no record at all and would read as debris while it is still
            // going; Update's Test Framework poll reconciles those instead.
            if (UnionAirActivityDecision.IsDebrisForOwner(
                    UnionAirTestRunGate.IsActive,
                    UnionAirTestRunGate.Source,
                    UnionAirTestRunGate.UnionAirSource,
                    UnionAirTestRunGate.RunId,
                    _current == null ? null : _current.id,
                    _current != null && _current.IsActive))
            {
                UnionAirActivityCoordinator.ClearDebris(
                    UnionAirActivity.TestRun, "no live UnionAir test run record was restored for it.");
                TestRunCancellationHandle.Clear();
            }
        }

        /// <summary>Current record, exposed for tests.</summary>
        internal static TestRunRecord Current => _current;

        /// <summary>
        /// Replaces the in-memory current record without writing anything.
        /// </summary>
        /// <remarks>
        /// For tests, which exercise the start transaction with a stubbed store and must leave the
        /// service as they found it.
        /// </remarks>
        internal static void SetCurrentForTests(TestRunRecord record) => RestoreCurrent(record);

        internal static bool TryParseMode(string value, out TestMode mode, out string modeName)
        {
            if (string.Equals(value, "editMode", StringComparison.OrdinalIgnoreCase))
            {
                mode = TestMode.EditMode;
                modeName = "editMode";
                return true;
            }
            if (string.Equals(value, "playMode", StringComparison.OrdinalIgnoreCase))
            {
                mode = TestMode.PlayMode;
                modeName = "playMode";
                return true;
            }
            mode = default(TestMode);
            modeName = "";
            return false;
        }

        internal static void Start(UnionAirRequestContext ctx)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                RestResponse.SendError(ctx.Response, "Tests cannot be started while the Editor is playing, compiling, or updating.", 409);
                return;
            }
            bool frameworkActive;
            if (!TryGetFrameworkRunActive(out frameworkActive))
            {
                RestResponse.SendError(ctx.Response, "Unity Test Framework run state is unavailable; starting a run is disabled to prevent concurrent execution.", 503);
                return;
            }
            if (UnionAirTestRunGate.IsActive || frameworkActive || TestDiscoveryHandler.IsPending)
            {
                RestResponse.SendError(ctx.Response, "Another Unity Test Framework run or test discovery is already active.", 409);
                return;
            }

            var body = RequestBodyReader.ReadString(ctx.Request);
            TestMode mode;
            string modeName;
            if (string.IsNullOrWhiteSpace(body) ||
                !TryParseMode(RequestBodyReader.GetString(body, "mode"), out mode, out modeName))
            {
                RestResponse.SendError(ctx.Response, "Body field 'mode' must be 'editMode' or 'playMode'.", 400);
                return;
            }

            var filters = new TestRunFilters();
            string error;
            if (!TryReadStringArray(body, "testNames", out filters.testNames, out error) ||
                !TryReadStringArray(body, "groupNames", out filters.groupNames, out error) ||
                !TryReadStringArray(body, "categoryNames", out filters.categoryNames, out error) ||
                !TryReadStringArray(body, "assemblyNames", out filters.assemblyNames, out error))
            {
                RestResponse.SendError(ctx.Response, error, 400);
                return;
            }

            foreach (var pattern in filters.groupNames)
            {
                try { _ = new Regex(pattern); }
                catch (ArgumentException ex)
                {
                    RestResponse.SendError(ctx.Response, "Invalid groupNames regular expression: " + ex.Message, 400);
                    return;
                }
            }

            var profilingJson = RequestBodyReader.GetObject(body, "profiling");
            string profilingSessionId = "";
            if (profilingJson == null && RequestBodyReader.HasTopLevelField(body, "profiling"))
            {
                RestResponse.SendError(ctx.Response, "Body field 'profiling' must be a JSON object.", 400);
                return;
            }
            if (profilingJson != null)
            {
                if (!ProfilingService.IsCategoryEnabled())
                {
                    RestResponse.SendError(ctx.Response, "Profiling category is disabled.", 403);
                    return;
                }
                if (!ProfilingService.TryParseSettings(profilingJson, out var profilingSettings, out error, out var profilingStatus) ||
                    !ProfilingService.TryCreateArmed(profilingSettings, true, out profilingSessionId, out error, out profilingStatus))
                {
                    RestResponse.SendError(ctx.Response, error, profilingStatus);
                    return;
                }
            }

            string id;
            if (!TryStartRun(mode, modeName, filters, profilingSessionId, out id, out error))
            {
                RestResponse.SendError(ctx.Response, error, 500);
                return;
            }

            RestResponse.Send(ctx.Response,
                $"{{\"id\":\"{RestResponse.EscapeJson(id)}\",\"state\":\"queued\",\"statusUrl\":\"/api/test-runs/{RestResponse.EscapeJson(id)}\",\"resultUrl\":\"/api/test-runs/{RestResponse.EscapeJson(id)}/results.xml\",\"profilingSessionId\":{RestResponse.FormatNullableString(string.IsNullOrEmpty(profilingSessionId) ? null : profilingSessionId)},\"profilingUrl\":{RestResponse.FormatNullableString(string.IsNullOrEmpty(profilingSessionId) ? null : $"/api/profiling/sessions/{profilingSessionId}")}}}",
                202);
        }

        /// <summary>
        /// Records a run, opens the test-run activity for it, and dispatches it, in that order.
        /// </summary>
        /// <param name="mode">Test mode to run.</param>
        /// <param name="modeName">Mode as it is reported back to the caller.</param>
        /// <param name="filters">Validated filters for the run.</param>
        /// <param name="profilingSessionId">Armed profiling session to attach, or an empty string.</param>
        /// <param name="runId">Run id issued for the started run.</param>
        /// <param name="error">Why nothing was started, when this returns <c>false</c>.</param>
        /// <param name="save">Persists the record; defaults to writing it as the current record.</param>
        /// <param name="execute">Dispatches the run and returns the framework's id.</param>
        /// <param name="bindProfiling">Attaches the profiling session to the run.</param>
        /// <returns><c>false</c> when the run was refused or could not be dispatched.</returns>
        /// <remarks>
        /// <para>
        /// The run id is UnionAir's own. The Test Framework returns its id only once <c>Execute</c>
        /// has dispatched the run, so adopting it as the identity would make it impossible to
        /// record the run before starting it. Its id is kept as
        /// <see cref="TestRunCancellationHandle"/> instead, which is all it is needed for - no test
        /// callback carries a run id.
        /// </para>
        /// <para>
        /// Nothing reaches the Test Framework until the record is on disk. Dispatching a run whose
        /// record was never written would leave a liveness bit with nothing behind it: after the
        /// domain reload a PlayMode run causes there would be no record to restore and nothing to
        /// close the activity, so every endpoint blocked during a test run would be refused for the
        /// rest of the Editor session.
        /// </para>
        /// <para>
        /// The delegates are injectable so the paths that cannot be reached from the file system -
        /// a dispatch that throws after the activity is open, an attachment that throws before it -
        /// can be exercised, following <c>CompileService.TrimRecordFiles</c>.
        /// </para>
        /// </remarks>
        internal static bool TryStartRun(
            TestMode mode,
            string modeName,
            TestRunFilters filters,
            string profilingSessionId,
            out string runId,
            out string error,
            Func<TestRunRecord, bool> save = null,
            Func<TestMode, TestRunFilters, string> execute = null,
            Action<string, string> bindProfiling = null)
        {
            runId = "";
            error = "";
            save = save ?? SaveAsCurrent;
            execute = execute ?? ExecuteRun;
            bindProfiling = bindProfiling ?? ProfilingService.BindToTest;

            var previous = _current;
            var record = new TestRunRecord
            {
                id = NewRunId(),
                mode = modeName,
                state = "queued",
                filters = filters,
                startedAt = UtcNow(),
                profilingSessionId = profilingSessionId
            };

            // Attaching writes the profiling session and fails the way a record write does. It goes
            // first so both failures leave the same state: nothing written for this run, nothing
            // dispatched, and the record that was stored before still the stored one. Attaching
            // after the record was committed would refuse the request while leaving a queued record
            // on disk that the next domain reload would surface as a run that never existed.
            if (!string.IsNullOrEmpty(profilingSessionId))
            {
                try
                {
                    bindProfiling(profilingSessionId, record.id);
                }
                catch (Exception ex)
                {
                    ProfilingService.DeleteArmed(profilingSessionId);
                    error = "The profiling session could not be attached, so no run was started: " + ex.Message;
                    return false;
                }
            }

            if (!save(record))
            {
                RestoreCurrent(previous);
                if (!string.IsNullOrEmpty(profilingSessionId)) ProfilingService.DeleteArmed(profilingSessionId);
                error = "The test run record could not be written, so no run was started.";
                return false;
            }

            UnionAirTestRunGate.Begin(UnionAirTestRunGate.UnionAirSource, record.id);

            string frameworkRunId;
            try
            {
                frameworkRunId = execute(mode, filters);
            }
            catch (Exception ex)
            {
                error = "The test run could not be started: " + ex.Message;
                try
                {
                    // The record owns an open activity from here on, so it is finished rather than
                    // rolled back. Every record that got this far reaches a terminal state through
                    // one path, and that path is what releases the activity.
                    MarkAborted(record, error);
                    FinishProfiling(record, error);
                    save(record);
                }
                finally
                {
                    // Unconditional. An activity left open here is closed by nothing, which is the
                    // failure the whole ordering exists to prevent.
                    TestRunCancellationHandle.Clear();
                    UnionAirTestRunGate.End(UnionAirTestRunGate.UnionAirSource, record.id);
                }
                return false;
            }

            TestRunCancellationHandle.Set(record.id, frameworkRunId);
            runId = record.id;
            return true;
        }

        private static bool SaveAsCurrent(TestRunRecord record)
        {
            _current = record;
            return SaveCurrentNow();
        }

        private static string ExecuteRun(TestMode mode, TestRunFilters filters)
        {
            var filter = new Filter
            {
                testMode = mode,
                testNames = EmptyToNull(filters.testNames),
                groupNames = EmptyToNull(filters.groupNames),
                categoryNames = EmptyToNull(filters.categoryNames),
                assemblyNames = EmptyToNull(filters.assemblyNames)
            };
            return TestRunnerApiProvider.Instance.Execute(new ExecutionSettings(filter));
        }

        /// <summary>
        /// Puts back the record that was current before a run that was never started.
        /// </summary>
        /// <remarks>
        /// Only memory is restored. The durable write is atomic, so one that failed left the stored
        /// record exactly as it was and the two already agree. A pending flush is left scheduled:
        /// it belongs to whatever was current before, not to the record being discarded.
        /// </remarks>
        private static void RestoreCurrent(TestRunRecord previous) => _current = previous;

        private static string NewRunId() => Guid.NewGuid().ToString("D");

        /// <summary>
        /// Finalizes the profiling session attached to a run, best-effort.
        /// </summary>
        /// <param name="record">Run whose session should be finalized; ignored when it has none.</param>
        /// <param name="abortReason">Reason to record, or <c>null</c> to complete the session normally.</param>
        /// <remarks>
        /// Profiling writes artifacts and metadata of its own, on the same disk, through a store
        /// that throws. Every caller is on a path that still has to release the test-run activity,
        /// so an exception escaping here would leave that activity open with nothing able to close
        /// it - the failure this service is ordered to prevent.
        /// <para>
        /// Catching is not enough on its own, which is why the profiling session is released by
        /// <c>ProfilingService.TryFinishAttached</c> rather than here: a session left active would
        /// refuse every later profiling request and be restored after the next domain reload for a
        /// test that has already finished. Only the artifacts it was still writing are lost, and
        /// that is reported rather than swallowed.
        /// </para>
        /// </remarks>
        private static void FinishProfiling(TestRunRecord record, string abortReason = null)
        {
            if (record == null || string.IsNullOrEmpty(record.profilingSessionId))
                return;

            string error;
            if (!ProfilingService.TryFinishAttached(record.id, abortReason, out error))
            {
                Debug.LogWarning(
                    "[UnionAir] The profiling session attached to test run " + record.id +
                    " could not be finalized and was released: " + error);
            }
        }

        /// <summary>Puts a record into its aborted terminal state.</summary>
        private static void MarkAborted(TestRunRecord record, string reason)
        {
            record.state = "aborted";
            record.result = "aborted";
            record.finishedAt = UtcNow();
            record.currentTest = "";
            record.resultFileAvailable = false;
            record.resultFileSha256 = "";
            if (!string.IsNullOrEmpty(reason)) record.error = reason;
        }

        internal static void Status(UnionAirRequestContext ctx)
        {
            var id = ctx.RouteValues["id"];
            var record = Find(id);
            if (record == null)
            {
                RestResponse.SendNotFound(ctx.Response, "Test run not found. UnionAir retains only the current run and latest completed result.");
                return;
            }
            RestResponse.Send(ctx.Response, record.ToApiJson());
        }

        internal static void Cancel(UnionAirRequestContext ctx)
        {
            var id = ctx.RouteValues["id"];
            if (_current == null || _current.id != id)
            {
                RestResponse.SendNotFound(ctx.Response, "Active UnionAir test run not found.");
                return;
            }
            if (!_current.IsActive || _current.state == "canceling")
            {
                RestResponse.SendError(ctx.Response, "This test run cannot be canceled.", 409);
                return;
            }

            // The framework is addressed by its own id, which UnionAir kept only for this.
            string frameworkRunId;
            if (!TestRunCancellationHandle.TryGet(id, out frameworkRunId))
            {
                RestResponse.SendError(ctx.Response,
                    "The Unity Test Framework handle for this run is unavailable, so it cannot be canceled.", 409);
                return;
            }

            if (!TestRunnerApi.CancelTestRun(frameworkRunId))
            {
                RestResponse.SendError(ctx.Response, "Unity Test Framework did not accept the cancellation request.", 409);
                return;
            }
            _current.state = "canceling";
            SaveCurrentNow();
            RestResponse.Send(ctx.Response,
                $"{{\"id\":\"{RestResponse.EscapeJson(id)}\",\"state\":\"canceling\",\"statusUrl\":\"/api/test-runs/{RestResponse.EscapeJson(id)}\"}}",
                202);
        }

        internal static void Results(UnionAirRequestContext ctx)
        {
            var id = ctx.RouteValues["id"];
            if (_current != null && _current.id == id && _current.IsActive)
            {
                RestResponse.SendError(ctx.Response, "The requested test run has not completed.", 409);
                return;
            }
            if (_latest == null || _latest.id != id || !_latest.resultFileAvailable || !HasMatchingLatestResult(_latest))
            {
                if (_latest != null && _latest.id == id)
                    _latest.resultFileAvailable = false;
                RestResponse.SendNotFound(ctx.Response, "NUnit XML is not available for this test run.");
                return;
            }

            ctx.Response.AddHeader("Content-Disposition", $"attachment; filename=\"TestResults-{id}.xml\"");
            RestResponse.SendBinary(ctx.Response, File.ReadAllBytes(LatestXmlPath), "application/xml; charset=utf-8");
        }

        internal static void OnRunStarted(ITestAdaptor testsToRun)
        {
            if (!UnionAirTestRunGate.IsActive)
            {
                UnionAirTestRunGate.Begin(UnionAirTestRunGate.ExternalSource, "");
                return;
            }
            if (UnionAirTestRunGate.Source != UnionAirTestRunGate.UnionAirSource || _current == null)
                return;

            _current.state = "running";
            _current.total = testsToRun?.TestCaseCount ?? 0;
            SaveCurrentNow();
            if (!string.IsNullOrEmpty(_current.profilingSessionId))
            {
                try { ProfilingService.StartAttached(_current.id); }
                catch (Exception ex)
                {
                    _current.error = "Profiling could not be started: " + ex.Message;
                    FinishProfiling(_current, _current.error);
                    SaveCurrentNow();
                }
            }
        }

        internal static void OnTestStarted(ITestAdaptor test)
        {
            if (!OwnsActiveRun() || test == null || test.IsSuite)
                return;
            _current.currentTest = test.FullName;
            MarkCurrentDirty();
        }

        internal static void OnTestFinished(ITestResultAdaptor result)
        {
            if (!OwnsActiveRun() || result?.Test == null || result.Test.IsSuite)
                return;
            _current.completed++;
            CountResult(result, _current);
            MarkCurrentDirty();
        }

        internal static void OnRunFinished(ITestResultAdaptor result)
        {
            if (UnionAirTestRunGate.Source == UnionAirTestRunGate.ExternalSource)
            {
                UnionAirTestRunGate.End(UnionAirTestRunGate.ExternalSource);
                return;
            }
            if (!OwnsActiveRun())
                return;

            try
            {
                var cancellationRequested = _current.state == "canceling";
                _current.state = "completed";
                _current.result = cancellationRequested ? "canceled" : MapResult(result);
                _current.finishedAt = result.EndTime.ToUniversalTime().ToString("o");
                _current.startedAt = result.StartTime.ToUniversalTime().ToString("o");
                _current.currentTest = "";
                _current.total = Math.Max(_current.total, result.Test?.TestCaseCount ?? 0);
                _current.completed = result.PassCount + result.FailCount + result.SkipCount + result.InconclusiveCount;
                _current.passed = result.PassCount;
                _current.failed = result.FailCount;
                _current.skipped = result.SkipCount;
                _current.inconclusive = result.InconclusiveCount;
                _current.duration = result.Duration;
                _current.assertCount = result.AssertCount;

                FinishProfiling(_current);

                try
                {
                    CommitLatestResult(result);
                }
                catch (Exception ex)
                {
                    RecoverLatestResultTransaction();
                    _latest = Load(LatestPath);
                    _current.resultFileAvailable = false;
                    _current.resultFileSha256 = "";
                    _current.error = "NUnit XML could not be saved: " + ex.Message;
                    Debug.LogError("[UnionAir] " + _current.error);
                }

                SaveCurrentNow();
            }
            finally
            {
                // Unconditional, and whether or not the write landed. A terminal record that could
                // not be stored is reported wrongly after a reload and recovered by the crash net;
                // an activity that was never released blocks the rest of the Editor session and is
                // recovered by nothing.
                TestRunCancellationHandle.Clear();
                UnionAirTestRunGate.End(UnionAirTestRunGate.UnionAirSource, _current.id);
            }
        }

        internal static void OnError(string message)
        {
            if (UnionAirTestRunGate.Source == UnionAirTestRunGate.ExternalSource)
            {
                UnionAirTestRunGate.End(UnionAirTestRunGate.ExternalSource);
                return;
            }
            if (!OwnsActiveRun())
                return;
            try
            {
                MarkAborted(_current, message ?? "The Unity Test Framework aborted the run.");
                FinishProfiling(_current, _current.error);
                SaveCurrentNow();
            }
            finally
            {
                TestRunCancellationHandle.Clear();
                UnionAirTestRunGate.End(UnionAirTestRunGate.UnionAirSource, _current.id);
            }
        }

        internal static void Update()
        {
            FlushCurrentIfDue();
            var now = EditorApplication.timeSinceStartup;
            if (now < _nextFrameworkPollAt)
                return;
            _nextFrameworkPollAt = now + FrameworkPollIntervalSeconds;

            bool frameworkActive;
            if (!TryGetFrameworkRunActive(out frameworkActive))
            {
                _frameworkInactiveSince = -1;
                return;
            }

            if (frameworkActive)
            {
                _frameworkInactiveSince = -1;
                if (!UnionAirTestRunGate.IsActive)
                    UnionAirTestRunGate.Begin(UnionAirTestRunGate.ExternalSource, "");
                return;
            }

            if (!UnionAirTestRunGate.IsActive)
            {
                _frameworkInactiveSince = -1;
                return;
            }

            if (_frameworkInactiveSince < 0)
            {
                _frameworkInactiveSince = EditorApplication.timeSinceStartup;
                return;
            }
            if (EditorApplication.timeSinceStartup - _frameworkInactiveSince < StaleGateGraceSeconds)
                return;

            ReconcileStaleGate();
            _frameworkInactiveSince = -1;
        }

        private static bool OwnsActiveRun()
            => UnionAirTestRunGate.IsActive &&
               UnionAirTestRunGate.Source == UnionAirTestRunGate.UnionAirSource &&
               _current != null && _current.id == UnionAirTestRunGate.RunId;

        private static bool TryGetFrameworkRunActive(out bool active)
        {
            active = false;
            if (_isFrameworkRunActive == null)
                return false;
            try
            {
                active = _isFrameworkRunActive();
                return true;
            }
            catch (Exception ex)
            {
                LogFrameworkInspectionError("Unity Test Framework run-state inspection failed: " + ex.Message);
                return false;
            }
        }

        private static void ResolveFrameworkRunInspector()
        {
            try
            {
                var method = typeof(TestRunnerApi).GetMethod(
                    "IsRunActive",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (method == null || method.ReturnType != typeof(bool) || method.GetParameters().Length != 0)
                {
                    LogFrameworkInspectionError("Unity Test Framework does not expose the expected internal run-state method.");
                    return;
                }
                _isFrameworkRunActive = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), method);
            }
            catch (Exception ex)
            {
                _isFrameworkRunActive = null;
                LogFrameworkInspectionError("Unity Test Framework run-state inspection could not be initialized: " + ex.Message);
            }
        }

        private static void LogFrameworkInspectionError(string message)
        {
            if (_frameworkInspectionErrorLogged)
                return;
            _frameworkInspectionErrorLogged = true;
            Debug.LogError("[UnionAir] " + message + " UnionAir will not start test runs until compatibility is restored.");
        }

        private static void ReconcileStaleGate()
        {
            var source = UnionAirTestRunGate.Source;
            var runId = UnionAirTestRunGate.RunId;
            if (source == UnionAirTestRunGate.UnionAirSource &&
                _current != null && _current.IsActive && _current.id == runId)
            {
                MarkAborted(_current, "Unity Test Framework became idle without delivering a completion callback.");
                FinishProfiling(_current, _current.error);
                if (SaveCurrentNow())
                    Debug.LogWarning("[UnionAir] Recovered a stale UnionAir test-run gate and marked the run aborted.");
                else
                    Debug.LogError("[UnionAir] Recovered a stale UnionAir test-run gate, but could not persist the aborted state.");
            }
            else
            {
                Debug.LogWarning("[UnionAir] Recovered a stale test-run gate after Unity Test Framework became idle.");
            }

            TestRunCancellationHandle.Clear();
            UnionAirTestRunGate.End(source, string.IsNullOrEmpty(runId) ? null : runId);
        }

        private static TestRunRecord Find(string id)
        {
            if (_current != null && _current.id == id) return _current;
            if (_latest != null && _latest.id == id) return _latest;
            return null;
        }

        private static void CountResult(ITestResultAdaptor result, TestRunRecord record)
        {
            var state = result.ResultState ?? "";
            if (state.StartsWith("Passed", StringComparison.OrdinalIgnoreCase)) record.passed++;
            else if (state.StartsWith("Inconclusive", StringComparison.OrdinalIgnoreCase)) record.inconclusive++;
            else if (state.StartsWith("Skipped", StringComparison.OrdinalIgnoreCase)) record.skipped++;
            else record.failed++;
            record.assertCount += result.AssertCount;
            record.duration += result.Duration;
        }

        private static string MapResult(ITestResultAdaptor result)
        {
            var state = result?.ResultState ?? "";
            if (state.IndexOf("Cancel", StringComparison.OrdinalIgnoreCase) >= 0) return "canceled";
            if (state.StartsWith("Passed", StringComparison.OrdinalIgnoreCase)) return "passed";
            if (state.StartsWith("Skipped", StringComparison.OrdinalIgnoreCase)) return "skipped";
            if (state.StartsWith("Inconclusive", StringComparison.OrdinalIgnoreCase)) return "inconclusive";
            if (state.StartsWith("Failed", StringComparison.OrdinalIgnoreCase)) return "failed";
            return "aborted";
        }

        private static bool TryReadStringArray(string body, string name, out string[] values, out string error)
        {
            error = "";
            if (!RequestBodyReader.TryGetStringArray(body, name, out values))
            {
                error = $"Body field '{name}' must be an array of non-empty strings.";
                return false;
            }
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    error = $"Body field '{name}' must be an array of non-empty strings.";
                    return false;
                }
            }
            return true;
        }

        private static string[] EmptyToNull(string[] values) => values == null || values.Length == 0 ? null : values;
        private static string UtcNow() => DateTime.UtcNow.ToString("o");

        private static TestRunRecord Load(string path)
        {
            try
            {
                return File.Exists(path) ? JsonUtility.FromJson<TestRunRecord>(File.ReadAllText(path)) : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnionAir] Could not load test run metadata from {path}: {ex.Message}");
                return null;
            }
        }

        private static void CommitLatestResult(ITestResultAdaptor result)
        {
            Directory.CreateDirectory(StorageDirectory);
            RecoverLatestResultTransaction();
            if (File.Exists(LatestTransactionPath))
                throw new IOException("A previous latest-result transaction could not be recovered safely.");

            var tempXml = Path.Combine(StorageDirectory, "latest.tmp.xml");
            var tempJson = Path.Combine(StorageDirectory, "latest.tmp.json");
            DeleteIfExists(tempXml);
            DeleteIfExists(tempJson);
            DeleteIfExists(LatestXmlBackupPath);
            DeleteIfExists(LatestJsonBackupPath);

            TestRunnerApi.SaveResultToFile(result, tempXml);
            _current.resultFileSha256 = ComputeSha256(tempXml);
            _current.resultFileAvailable = true;
            File.WriteAllText(tempJson, JsonUtility.ToJson(_current));

            var transaction = new LatestResultTransaction
            {
                id = _current.id,
                sha256 = _current.resultFileSha256,
                hadXml = File.Exists(LatestXmlPath),
                hadJson = File.Exists(LatestPath)
            };
            // The marker is committed before either public latest file. Recovery uses
            // the backups and XML hash to distinguish a complete commit from a partial one.
            WriteAtomicJson(LatestTransactionPath, transaction);

            ReplaceWithBackup(tempXml, LatestXmlPath, LatestXmlBackupPath, transaction.hadXml);
            ReplaceWithBackup(tempJson, LatestPath, LatestJsonBackupPath, transaction.hadJson);
            if (!HasMatchingLatestResult(_current))
                throw new IOException("The committed NUnit XML failed its integrity check.");

            _latest = Clone(_current);
            CleanupLatestTransactionFiles();
        }

        private static void RecoverLatestResultTransaction()
        {
            if (!File.Exists(LatestTransactionPath))
                return;

            try
            {
                var transaction = JsonUtility.FromJson<LatestResultTransaction>(File.ReadAllText(LatestTransactionPath));
                if (transaction == null || string.IsNullOrEmpty(transaction.id) || string.IsNullOrEmpty(transaction.sha256))
                    throw new IOException("The pending transaction metadata is incomplete.");

                var latest = Load(LatestPath);
                var committed = latest != null &&
                                latest.id == transaction.id &&
                                latest.resultFileSha256 == transaction.sha256 &&
                                HasMatchingLatestResult(latest);
                if (!committed)
                {
                    RestoreBackup(LatestXmlBackupPath, LatestXmlPath, transaction.hadXml);
                    RestoreBackup(LatestJsonBackupPath, LatestPath, transaction.hadJson);
                    Debug.LogWarning("[UnionAir] Rolled back an incomplete latest test-result transaction.");
                }

                CleanupLatestTransactionFiles();
            }
            catch (Exception ex)
            {
                Debug.LogError("[UnionAir] Could not recover the pending latest-result transaction: " + ex.Message);
            }
        }

        private static bool HasMatchingLatestResult(TestRunRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.resultFileSha256) || !File.Exists(LatestXmlPath))
                return false;
            try
            {
                return string.Equals(
                    record.resultFileSha256,
                    ComputeSha256(LatestXmlPath),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void ReplaceWithBackup(string temp, string destination, string backup, bool hadDestination)
        {
            if (hadDestination)
                File.Replace(temp, destination, backup);
            else
                File.Move(temp, destination);
        }

        private static void RestoreBackup(string backup, string destination, bool hadDestination)
        {
            if (hadDestination)
            {
                if (!File.Exists(backup))
                    return;
                if (File.Exists(destination))
                    File.Replace(backup, destination, null);
                else
                    File.Move(backup, destination);
                return;
            }

            DeleteIfExists(destination);
        }

        private static void CleanupLatestTransactionFiles()
        {
            TryDelete(LatestXmlBackupPath);
            TryDelete(LatestJsonBackupPath);
            TryDelete(LatestTransactionPath);
            TryDelete(Path.Combine(StorageDirectory, "latest.tmp.xml"));
            TryDelete(Path.Combine(StorageDirectory, "latest.tmp.json"));
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        private static void TryDelete(string path)
        {
            try
            {
                DeleteIfExists(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnionAir] Could not remove transaction file {path}: {ex.Message}");
            }
        }

        private static void MarkCurrentDirty()
        {
            if (!_currentDirty)
                _nextCurrentFlushAt = EditorApplication.timeSinceStartup + CurrentFlushIntervalSeconds;
            _currentDirty = true;
        }

        private static void FlushCurrentIfDue()
        {
            if (!_currentDirty || _current == null || EditorApplication.timeSinceStartup < _nextCurrentFlushAt)
                return;
            SaveCurrentNow();
        }

        internal static void FlushCurrentBeforeReload()
        {
            if (!_currentDirty || _current == null)
                return;
            SaveCurrentNow();
        }

        /// <summary>
        /// Writes the current record, reporting whether it reached disk.
        /// </summary>
        /// <remarks>
        /// A failed write schedules its own retry. Most callers are terminal paths that run with
        /// nothing pending, so leaving the dirty flag down would mean the write is never attempted
        /// again - including by <see cref="FlushCurrentBeforeReload"/>, which is the last chance
        /// before the domain goes away.
        /// </remarks>
        private static bool SaveCurrentNow()
        {
            if (_current == null) return false;

            if (!TryWrite(CurrentPath, _current, "current test run record"))
            {
                _currentDirty = true;
                _nextCurrentFlushAt = EditorApplication.timeSinceStartup + CurrentFlushIntervalSeconds;
                return false;
            }

            _currentDirty = false;
            _currentFlushErrorLogged = false;
            _nextCurrentFlushAt = 0;
            return true;
        }

        private static bool TryWrite(string path, TestRunRecord record, string what)
        {
            string error;
            if (ProfilingArtifactStore.TryWriteAtomicJson(path, JsonUtility.ToJson(record), out error))
                return true;

            if (!_currentFlushErrorLogged)
            {
                _currentFlushErrorLogged = true;
                Debug.LogWarning($"[UnionAir] Could not write the {what}; UnionAir will retry: {error}");
            }
            return false;
        }

        private static void WriteAtomicJson<T>(string path, T value)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(value));
            AtomicReplace(temp, path);
        }

        private static void AtomicReplace(string temp, string destination)
        {
            if (File.Exists(destination))
                File.Replace(temp, destination, null);
            else
                File.Move(temp, destination);
        }

        private static TestRunRecord Clone(TestRunRecord record)
            => JsonUtility.FromJson<TestRunRecord>(JsonUtility.ToJson(record));
    }
}

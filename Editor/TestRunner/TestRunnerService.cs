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
            if (_current != null && _current.IsActive && !UnionAirTestRunGate.IsActive)
            {
                _current.state = "aborted";
                _current.result = "aborted";
                _current.finishedAt = UtcNow();
                _current.currentTest = "";
                _current.resultFileAvailable = false;
                _current.resultFileSha256 = "";
                SaveCurrentNow();
            }
        }

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

            var filter = new Filter
            {
                testMode = mode,
                testNames = EmptyToNull(filters.testNames),
                groupNames = EmptyToNull(filters.groupNames),
                categoryNames = EmptyToNull(filters.categoryNames),
                assemblyNames = EmptyToNull(filters.assemblyNames)
            };
            string id;
            try
            {
                id = TestRunnerApiProvider.Instance.Execute(new ExecutionSettings(filter));
            }
            catch (Exception ex)
            {
                RestResponse.SendError(ctx.Response, "The test run could not be started: " + ex.Message, 500);
                return;
            }

            _current = new TestRunRecord
            {
                id = id,
                mode = modeName,
                state = "queued",
                filters = filters,
                startedAt = UtcNow()
            };
            SaveCurrentNow();
            UnionAirTestRunGate.Begin(UnionAirTestRunGate.UnionAirSource, id);
            RestResponse.Send(ctx.Response,
                $"{{\"id\":\"{RestResponse.EscapeJson(id)}\",\"state\":\"queued\",\"statusUrl\":\"/api/test-runs/{RestResponse.EscapeJson(id)}\",\"resultUrl\":\"/api/test-runs/{RestResponse.EscapeJson(id)}/results.xml\"}}",
                202);
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

            if (!TestRunnerApi.CancelTestRun(id))
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
            UnionAirTestRunGate.End(UnionAirTestRunGate.UnionAirSource, _current.id);
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
            _current.state = "aborted";
            _current.result = "aborted";
            _current.finishedAt = UtcNow();
            _current.currentTest = "";
            _current.resultFileAvailable = false;
            _current.resultFileSha256 = "";
            _current.error = message ?? "The Unity Test Framework aborted the run.";
            SaveCurrentNow();
            UnionAirTestRunGate.End(UnionAirTestRunGate.UnionAirSource, _current.id);
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
            => UnionAirTestRunGate.Source == UnionAirTestRunGate.UnionAirSource &&
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
                _current.state = "aborted";
                _current.result = "aborted";
                _current.finishedAt = UtcNow();
                _current.currentTest = "";
                _current.resultFileAvailable = false;
                _current.resultFileSha256 = "";
                _current.error = "Unity Test Framework became idle without delivering a completion callback.";
                try
                {
                    SaveCurrentNow();
                    Debug.LogWarning("[UnionAir] Recovered a stale UnionAir test-run gate and marked the run aborted.");
                }
                catch (Exception ex)
                {
                    Debug.LogError("[UnionAir] Recovered a stale UnionAir test-run gate, but could not persist the aborted state: " + ex.Message);
                }
            }
            else
            {
                Debug.LogWarning("[UnionAir] Recovered a stale test-run gate after Unity Test Framework became idle.");
            }

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

        private static void Save(string path, TestRunRecord record)
        {
            Directory.CreateDirectory(StorageDirectory);
            WriteAtomicJson(path, record);
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
            try
            {
                SaveCurrentNow();
            }
            catch (Exception ex)
            {
                _nextCurrentFlushAt = EditorApplication.timeSinceStartup + CurrentFlushIntervalSeconds;
                if (!_currentFlushErrorLogged)
                {
                    _currentFlushErrorLogged = true;
                    Debug.LogWarning("[UnionAir] Could not flush current test-run metadata; UnionAir will retry: " + ex.Message);
                }
            }
        }

        internal static void FlushCurrentBeforeReload()
        {
            if (!_currentDirty || _current == null)
                return;
            try
            {
                SaveCurrentNow();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnionAir] Could not flush current test-run metadata before assembly reload: " + ex.Message);
            }
        }

        private static void SaveCurrentNow()
        {
            Save(CurrentPath, _current);
            _currentDirty = false;
            _currentFlushErrorLogged = false;
            _nextCurrentFlushAt = 0;
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

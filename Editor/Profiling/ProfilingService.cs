using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace LeonAkasaka.UnionAir.Editor
{
    [Serializable]
    internal sealed class ProfilingSettings
    {
        public string label = "";
        public string[] metrics = new string[0];
        public int warmupFrames = 60;
        public int maxFrames = 600;
        public float maxDurationSeconds = 30;
        public bool captureRaw;
    }

    internal sealed class AvailableProfilingMetric
    {
        internal string Id;
        internal string Category;
        internal string Marker;
        internal string Unit;
        internal string DataType;
        internal ProfilerCategory ProfilerCategory;
        internal double Scale = 1;
    }

    internal static class ProfilingService
    {
        private const int MaxFrames = 100000;
        private const int MaxWarmupFrames = 10000;
        private const int MaxMetrics = 64;
        private const float MaxDurationSeconds = 3600;
        private static readonly string ActivePath = Path.Combine(ProfilingArtifactStore.ProfilingRoot, "active.json");
        private static readonly string[] DefaultMetricIds =
        {
            "mainThreadTime", "renderThreadTime", "gcAllocInFrame",
            "gcUsedMemory", "totalUsedMemory", "totalReservedMemory"
        };
        private static readonly Dictionary<string, string> AliasMarkers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "mainThreadTime", "Main Thread" },
            { "renderThreadTime", "Render Thread" },
            { "gcAllocInFrame", "GC Allocated In Frame" },
            { "gcUsedMemory", "GC Used Memory" },
            { "totalUsedMemory", "Total Used Memory" },
            { "totalReservedMemory", "Total Reserved Memory" }
        };

        private static ProfilingSessionRecord _active;
        private static readonly List<ProfilerRecorder> Recorders = new List<ProfilerRecorder>();
        private static StreamWriter _samplesWriter;
        private static double _segmentStartedAt;
        private static int _segmentFrame;
        private static List<AvailableProfilingMetric> _available;
        private static Dictionary<string, ProfilingStatistics> _liveStatistics;

        internal static void Initialize()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
            EditorApplication.quitting -= OnQuitting;
            EditorApplication.quitting += OnQuitting;

            if (!File.Exists(ActivePath)) return;
            try
            {
                _active = JsonUtility.FromJson<ProfilingSessionRecord>(File.ReadAllText(ActivePath));
                if (_active == null || !_active.IsActive) { _active = null; return; }
                _active.continuous = false;
                _active.domainReloadCount++;
                _active.interruptionReason = "Unity assembly domain reload interrupted the sample stream.";
                if (_active.state == "armed") SaveActive();
                else StartRecorders(true);
            }
            catch (Exception ex)
            {
                var message = "Profiling session could not be restored after an assembly reload: " + ex.Message;
                Debug.LogError("[UnionAir] " + message);
                RecoverRestoreFailure(message);
            }
        }

        internal static bool IsCategoryEnabled()
        {
            foreach (var category in UnionAirRouteRegistry.Categories)
                if (category.Source == UnionAirRouteSource.Builtin && category.Id == UnionAirEndpointCategories.Profiling)
                    return category.Enabled;
            return false;
        }

        internal static bool TryParseSettings(string json, out ProfilingSettings settings, out string error, out int status)
        {
            settings = new ProfilingSettings(); error = ""; status = 400;
            if (json == null) json = "";
            settings.label = RequestBodyReader.GetString(json, "label") ?? "";
            var warmup = RequestBodyReader.GetInt(json, "warmupFrames");
            var frames = RequestBodyReader.GetInt(json, "maxFrames");
            var duration = RequestBodyReader.GetFloat(json, "maxDurationSeconds");
            var raw = RequestBodyReader.GetBool(json, "captureRaw");
            if (warmup.HasValue) settings.warmupFrames = warmup.Value;
            if (frames.HasValue) settings.maxFrames = frames.Value;
            if (duration.HasValue) settings.maxDurationSeconds = duration.Value;
            if (raw.HasValue) settings.captureRaw = raw.Value;
            if (settings.warmupFrames < 0 || settings.warmupFrames > MaxWarmupFrames)
            { error = $"warmupFrames must be between 0 and {MaxWarmupFrames}."; return false; }
            if (settings.maxFrames < 1 || settings.maxFrames > MaxFrames)
            { error = $"maxFrames must be between 1 and {MaxFrames}."; return false; }
            if (settings.maxDurationSeconds <= 0 || settings.maxDurationSeconds > MaxDurationSeconds || float.IsNaN(settings.maxDurationSeconds))
            { error = $"maxDurationSeconds must be greater than 0 and at most {MaxDurationSeconds}."; return false; }
            if (!RequestBodyReader.TryGetStringArray(json, "metrics", out settings.metrics))
            { error = "metrics must be an array of non-empty metric IDs."; return false; }
            foreach (var metric in settings.metrics)
                if (string.IsNullOrWhiteSpace(metric)) { error = "metrics must be an array of non-empty metric IDs."; return false; }
            var uniqueMetrics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var metric in settings.metrics)
                if (!uniqueMetrics.Add(metric)) { error = $"metrics contains duplicate ID '{metric}'."; return false; }
            if (settings.metrics.Length > MaxMetrics)
            { error = $"metrics cannot contain more than {MaxMetrics} metric IDs."; status = 422; return false; }
            var available = GetAvailableMetrics();
            if (settings.metrics.Length == 0)
            {
                var defaults = new List<string>();
                foreach (var metricId in DefaultMetricIds)
                    if (FindMetric(available, metricId) != null) defaults.Add(metricId);
                settings.metrics = defaults.ToArray();
                if (settings.metrics.Length == 0)
                { error = "No default Unity Profiler metrics are available in this Editor session."; status = 422; return false; }
            }
            foreach (var metric in settings.metrics)
            {
                if (FindMetric(available, metric) == null)
                { error = $"Profiler metric '{metric}' is not available. Query GET /api/profiling/metrics for valid IDs."; status = 422; return false; }
            }
            if (settings.captureRaw && (Profiler.enableBinaryLog || !string.IsNullOrEmpty(Profiler.logFile)))
            { error = "Unity Profiler binary logging is already configured by another tool."; status = 409; return false; }
            status = 200;
            return true;
        }

        internal static bool TryCreateArmed(ProfilingSettings settings, bool attachedToTest, out string id, out string error, out int status)
        {
            id = ""; error = ""; status = 409;
            if (_active != null && _active.IsActive) { error = "Another profiling session is already active."; return false; }
            if (ProfilingArtifactStore.IsOverQuota()) { error = "Profiling artifact storage exceeds the 5 GiB limit. Delete completed artifacts before capturing again."; status = 507; return false; }
            id = Guid.NewGuid().ToString("D");
            var available = GetAvailableMetrics();
            var metricRecords = new List<ProfilingMetricRecord>();
            foreach (var metricId in settings.metrics)
            {
                var metric = FindMetric(available, metricId);
                metricRecords.Add(new ProfilingMetricRecord { metricId = metric.Id, category = metric.Category, marker = metric.Marker, unit = metric.Unit });
            }
            _active = new ProfilingSessionRecord
            {
                id = id, label = settings.label, state = "armed", createdAt = UtcNow(), attachedToTest = attachedToTest,
                warmupFrames = settings.warmupFrames, warmupRemaining = settings.warmupFrames,
                maxFrames = settings.maxFrames, maxDurationSeconds = settings.maxDurationSeconds,
                captureRaw = settings.captureRaw, metrics = metricRecords.ToArray(), unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(), graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                scenePath = SceneManager.GetActiveScene().path ?? "", isPlaying = EditorApplication.isPlaying
            };
            ProfilingArtifactStore.CreateDirectory(ProfilingArtifactStore.ProfilingRoot, id);
            SaveActive();
            status = 202;
            return true;
        }

        internal static void BindToTest(string sessionId, string testRunId)
        {
            if (_active == null || _active.id != sessionId) return;
            _active.testRunId = testRunId ?? "";
            SaveActive();
        }

        internal static void StartManual(string body, UnionAirRequestContext ctx)
        {
            if (!TryParseSettings(body, out var settings, out var error, out var status) ||
                !TryCreateArmed(settings, false, out var id, out error, out status))
            { RestResponse.SendError(ctx.Response, error, status); return; }
            try { StartRecorders(false); }
            catch (Exception ex) { var message = "Profiling could not be started: " + ex.Message; FailActive(message); RestResponse.SendError(ctx.Response, message, 500); return; }
            RestResponse.Send(ctx.Response, $"{{\"schemaVersion\":1,\"id\":\"{id}\",\"state\":\"{_active.state}\",\"statusUrl\":\"/api/profiling/sessions/{id}\"}}", 202);
        }

        internal static void StartAttached(string testRunId)
        {
            if (_active == null || !_active.attachedToTest || _active.testRunId != testRunId || _active.state != "armed") return;
            StartRecorders(false);
        }

        internal static void FinishAttached(string testRunId, string abortReason = null)
        {
            if (_active == null || !_active.attachedToTest || _active.testRunId != testRunId) return;
            if (!string.IsNullOrEmpty(abortReason)) AbortActive(abortReason);
            else CompleteActive();
        }

        /// <summary>
        /// Finalizes the session attached to a test run, reporting failure instead of throwing.
        /// </summary>
        /// <param name="testRunId">Run the session is attached to.</param>
        /// <param name="abortReason">Reason to record, or <c>null</c> to complete the session normally.</param>
        /// <param name="error">What could not be finalized, or <c>null</c> when it was.</param>
        /// <returns><c>false</c> when the session had to be let go rather than finalized.</returns>
        /// <remarks>
        /// For callers that must go on to release something of their own. A test run's activity is
        /// released by the same statements that finish its record, and profiling failing on the
        /// disk those statements are about to use must not take them down with it. The session is
        /// released either way; only the artifacts it was still writing are at risk.
        /// </remarks>
        internal static bool TryFinishAttached(string testRunId, string abortReason, out string error)
        {
            error = null;
            try
            {
                FinishAttached(testRunId, abortReason);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static void DeleteArmed(string id)
        {
            if (_active == null || _active.id != id || _active.state != "armed") return;
            _active = null;
            TryDelete(ActivePath);
            ProfilingArtifactStore.TryDeleteDirectory(Path.Combine(ProfilingArtifactStore.ProfilingRoot, id));
        }

        private static void StartRecorders(bool resumed)
        {
            if (_active == null) return;
            DisposeRecorders();
            var available = GetAvailableMetrics();
            _liveStatistics = resumed ? ReadStatistics(_active) : CreateStatistics(_active);
            foreach (var requested in _active.metrics)
            {
                var metric = FindMetric(available, requested.metricId);
                if (metric == null) throw new InvalidOperationException("Profiler metric is no longer available: " + requested.metricId);
                Recorders.Add(ProfilerRecorder.StartNew(metric.ProfilerCategory, metric.Marker, 1));
            }
            var samplesPath = SamplesPath(_active.id);
            _samplesWriter = new StreamWriter(new FileStream(samplesPath, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
            _samplesWriter.AutoFlush = true;
            _active.segment++;
            _segmentFrame = 0;
            _segmentStartedAt = EditorApplication.timeSinceStartup;
            _active.warmupRemaining = _active.warmupFrames;
            _active.state = _active.warmupRemaining > 0 ? "warming" : "running";
            if (string.IsNullOrEmpty(_active.startedAt)) _active.startedAt = UtcNow();
            _active.isPlaying = EditorApplication.isPlaying;
            _active.scenePath = SceneManager.GetActiveScene().path ?? "";
            if (_active.captureRaw && !resumed) StartRawCapture();
            SaveActive();
        }

        private static void Update()
        {
            if (_active == null || (_active.state != "warming" && _active.state != "running")) return;
            if (_active.elapsedSeconds + EditorApplication.timeSinceStartup - _segmentStartedAt >= _active.maxDurationSeconds)
            { CompleteActive(); return; }
            _segmentFrame++;
            if (_active.warmupRemaining > 0)
            {
                _active.warmupRemaining--;
                if (_active.warmupRemaining == 0) _active.state = "running";
                return;
            }
            var sb = new StringBuilder();
            sb.Append("{\"segment\":").Append(_active.segment).Append(",\"frame\":").Append(_active.capturedFrames + 1);
            sb.Append(",\"segmentFrame\":").Append(_segmentFrame).Append(",\"elapsedMs\":");
            sb.Append(((EditorApplication.timeSinceStartup - _segmentStartedAt) * 1000).ToString("G17", CultureInfo.InvariantCulture));
            sb.Append(",\"values\":[");
            var available = GetAvailableMetrics();
            for (var i = 0; i < Recorders.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var definition = FindMetric(available, _active.metrics[i].metricId);
                var value = Recorders[i].LastValueAsDouble * (definition?.Scale ?? 1);
                if (!double.IsNaN(value) && !double.IsInfinity(value))
                    _liveStatistics[_active.metrics[i].metricId].Add(value);
                sb.Append(double.IsNaN(value) || double.IsInfinity(value) ? "null" : value.ToString("G17", CultureInfo.InvariantCulture));
            }
            sb.Append("]}");
            _samplesWriter.WriteLine(sb.ToString());
            _active.capturedFrames++;
            if (_active.capturedFrames % 30 == 0) SaveActive();
            if (_active.capturedFrames >= _active.maxFrames) CompleteActive();
        }

        internal static void Stop(UnionAirRequestContext ctx)
        {
            var record = Load(ctx.RouteValues["id"]);
            if (record == null) { RestResponse.SendNotFound(ctx.Response, "Profiling session not found."); return; }
            if (_active != null && _active.id == record.id && _active.IsActive) CompleteActive();
            else if (record.IsActive && _active == null)
            {
                _active = record;
                _liveStatistics = ReadStatistics(record);
                RecoverRestoreFailure("Profiling session state was recovered after its in-memory recorder state was lost.");
            }
            record = Load(record.id) ?? _active ?? record;
            EnsureFinalizedStatistics(record);
            RestResponse.Send(ctx.Response, record.ToApiJson(true));
        }

        internal static void Status(UnionAirRequestContext ctx)
        {
            var record = Load(ctx.RouteValues["id"]);
            if (record == null) { RestResponse.SendNotFound(ctx.Response, "Profiling session not found."); return; }
            if (_active != null && _active.id == record.id) { _samplesWriter?.Flush(); record = _active; }
            EnsureFinalizedStatistics(record);
            RestResponse.Send(ctx.Response, record.ToApiJson(true));
        }

        internal static void List(UnionAirRequestContext ctx)
        {
            var records = LoadAll();
            var sb = new StringBuilder("{\"schemaVersion\":1,\"sessions\":[");
            for (var i = 0; i < records.Count; i++) { if (i > 0) sb.Append(","); sb.Append(records[i].ToApiJson(false)); }
            sb.Append("]}"); RestResponse.Send(ctx.Response, sb.ToString());
        }

        internal static void Delete(UnionAirRequestContext ctx)
        {
            var id = ctx.RouteValues["id"];
            var record = Load(id);
            if (record == null) { RestResponse.SendNotFound(ctx.Response, "Profiling session not found."); return; }
            if ((_active != null && _active.id == id && _active.IsActive) || record.IsActive)
            { RestResponse.SendError(ctx.Response, "Stop the profiling session before deleting it.", 409); return; }
            ProfilingArtifactStore.TryDeleteDirectory(Path.Combine(ProfilingArtifactStore.ProfilingRoot, id));
            RestResponse.Send(ctx.Response, $"{{\"deleted\":true,\"id\":\"{RestResponse.EscapeJson(id)}\"}}");
        }

        internal static void Samples(UnionAirRequestContext ctx)
        {
            var record = Load(ctx.RouteValues["id"]);
            if (record == null || !File.Exists(SamplesPath(record.id))) { RestResponse.SendNotFound(ctx.Response, "Profiling samples are not available."); return; }
            _samplesWriter?.Flush();
            RestResponse.SendArtifactFile(ctx, SamplesPath(record.id), "application/x-ndjson; charset=utf-8", $"ProfilingSamples-{record.id}.ndjson");
        }

        internal static void Raw(UnionAirRequestContext ctx)
        {
            var record = Load(ctx.RouteValues["id"]);
            if (record == null || !record.rawAvailable) { RestResponse.SendNotFound(ctx.Response, "Profiler raw capture is not available."); return; }
            RestResponse.SendArtifactFile(ctx, RawPath(record.id), "application/octet-stream", $"Profiler-{record.id}.raw");
        }

        internal static void Metrics(UnionAirRequestContext ctx)
        {
            var search = ctx.Request.QueryString["search"] ?? "";
            var category = ctx.Request.QueryString["category"] ?? "";
            var offset = ParseBounded(ctx.Request.QueryString["offset"], 0, 0, int.MaxValue);
            var limit = ParseBounded(ctx.Request.QueryString["limit"], 100, 1, 1000);
            var filtered = new List<AvailableProfilingMetric>();
            foreach (var metric in GetAvailableMetrics())
            {
                if (!string.IsNullOrEmpty(search) && metric.Id.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 && metric.Marker.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!string.IsNullOrEmpty(category) && !string.Equals(metric.Category, category, StringComparison.OrdinalIgnoreCase)) continue;
                filtered.Add(metric);
            }
            var sb = new StringBuilder("{\"schemaVersion\":1,\"total\":").Append(filtered.Count).Append(",\"offset\":").Append(offset).Append(",\"limit\":").Append(limit).Append(",\"metrics\":[");
            var end = Math.Min(filtered.Count, offset + limit); var first = true;
            for (var i = Math.Min(offset, filtered.Count); i < end; i++)
            {
                if (!first) sb.Append(","); first = false; var m = filtered[i];
                sb.Append("{\"metricId\":\"").Append(RestResponse.EscapeJson(m.Id)).Append("\",\"category\":\"").Append(RestResponse.EscapeJson(m.Category));
                sb.Append("\",\"marker\":\"").Append(RestResponse.EscapeJson(m.Marker)).Append("\",\"unit\":\"").Append(RestResponse.EscapeJson(m.Unit));
                sb.Append("\",\"dataType\":\"").Append(RestResponse.EscapeJson(m.DataType)).Append("\",\"available\":true}");
            }
            sb.Append("]}"); RestResponse.Send(ctx.Response, sb.ToString());
        }

        private static List<AvailableProfilingMetric> GetAvailableMetrics()
        {
            if (_available != null) return _available;
            _available = new List<AvailableProfilingMetric>();
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var handle in handles)
            {
                var description = ProfilerRecorderHandle.GetDescription(handle);
                var category = description.Category.Name;
                var marker = description.Name;
                var id = category + ":" + marker;
                foreach (var alias in AliasMarkers) if (string.Equals(alias.Value, marker, StringComparison.OrdinalIgnoreCase)) { id = alias.Key; break; }
                if (!ids.Add(id))
                {
                    id = category + ":" + marker;
                    if (!ids.Add(id)) continue;
                }
                var unit = description.UnitType.ToString(); var scale = 1d;
                if (unit.IndexOf("Nanosecond", StringComparison.OrdinalIgnoreCase) >= 0) { unit = "ms"; scale = 1e-6; }
                else if (unit.IndexOf("Byte", StringComparison.OrdinalIgnoreCase) >= 0) unit = "bytes";
                _available.Add(new AvailableProfilingMetric { Id = id, Category = category, Marker = marker, Unit = unit, DataType = description.DataType.ToString(), ProfilerCategory = description.Category, Scale = scale });
            }
            _available.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
            return _available;
        }

        private static AvailableProfilingMetric FindMetric(List<AvailableProfilingMetric> list, string id)
            => list.Find(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

        private static void CompleteActive()
        {
            if (_active == null || !_active.IsActive) return;
            try
            {
                AccumulateSegmentTime();
                CloseWriters(); StopRawCapture();
                FinalizeStatistics(_active);
                _active.state = "completed"; _active.finishedAt = UtcNow(); FinalizeArtifacts(_active); SaveCompleted(_active);
                var id = _active.id; _active = null; TryDelete(ActivePath);
                ProfilingArtifactStore.Trim(ProfilingArtifactStore.ProfilingRoot, 10, id);
            }
            catch (Exception ex) { AbandonActive("Profiling could not be completed: " + ex.Message); throw; }
        }

        private static void AbortActive(string reason)
        {
            if (_active == null) return;
            try
            {
                AccumulateSegmentTime();
                CloseWriters(); StopRawCapture(); FinalizeStatistics(_active); _active.state = "aborted"; _active.error = reason; _active.finishedAt = UtcNow();
                FinalizeArtifacts(_active); SaveCompleted(_active); _active = null; TryDelete(ActivePath);
            }
            catch (Exception ex) { AbandonActive("Profiling could not be aborted: " + ex.Message); throw; }
        }

        private static void FailActive(string reason)
        {
            if (_active == null) return;
            try
            {
                AccumulateSegmentTime();
                CloseWriters(); StopRawCapture(); FinalizeStatistics(_active); _active.state = "failed"; _active.error = reason; _active.finishedAt = UtcNow();
                FinalizeArtifacts(_active); SaveCompleted(_active); _active = null; TryDelete(ActivePath);
            }
            catch (Exception ex) { AbandonActive("Profiling could not be failed cleanly: " + ex.Message); throw; }
        }

        /// <summary>
        /// Releases the active session when finalizing it threw partway through.
        /// </summary>
        /// <param name="reason">What went wrong, recorded on the session that is being let go.</param>
        /// <remarks>
        /// <para>
        /// Every step is independent and best-effort, because the premise is that the ordinary path
        /// already failed. What must not survive is the pair <c>_active</c> and <c>active.json</c>.
        /// A session left active in memory makes <see cref="TryCreateArmed"/> refuse every later
        /// session for the rest of the Editor session; one left on disk is restored by
        /// <see cref="Initialize"/> after the next domain reload, as a session for work that has
        /// already finished.
        /// </para>
        /// <para>
        /// The recorders and the raw capture are closed first because both read <c>_active</c>, and
        /// the Profiler settings <c>StopRawCapture</c> restores are global to the Editor.
        /// </para>
        /// </remarks>
        private static void AbandonActive(string reason)
        {
            var record = _active;
            try { CloseWriters(); } catch { }
            try { StopRawCapture(); } catch { }
            if (record != null)
            {
                record.state = "failed";
                record.error = reason;
                record.finishedAt = UtcNow();
                try { FinalizeStatistics(record); } catch { }
                try { SaveCompleted(record); } catch { }
            }
            _active = null;
            _liveStatistics = null;
            TryDelete(ActivePath);
        }

        private static void StartRawCapture()
        {
            _active.previousProfilerEnabled = Profiler.enabled; _active.previousBinaryLog = Profiler.enableBinaryLog; _active.previousLogFile = Profiler.logFile ?? "";
            Profiler.logFile = Path.GetFullPath(Path.Combine(SessionDirectory(_active.id), "profile.tmp"));
            Profiler.enableBinaryLog = true; Profiler.enabled = true;
        }

        private static void StopRawCapture()
        {
            if (_active == null || !_active.captureRaw) return;
            try
            {
                Profiler.enabled = false; Profiler.logFile = ""; Profiler.enableBinaryLog = false;
                var generated = Path.Combine(SessionDirectory(_active.id), "profile.tmp.raw");
                var alternative = Path.Combine(SessionDirectory(_active.id), "profile.tmp");
                var source = File.Exists(generated) ? generated : alternative;
                if (File.Exists(source)) { if (File.Exists(RawPath(_active.id))) File.Delete(RawPath(_active.id)); File.Move(source, RawPath(_active.id)); }
            }
            finally
            {
                Profiler.logFile = _active.previousLogFile ?? "";
                Profiler.enableBinaryLog = _active.previousBinaryLog;
                Profiler.enabled = _active.previousProfilerEnabled;
            }
        }

        private static void BeforeAssemblyReload()
        {
            if (_active == null || !_active.IsActive) return;
            _active.continuous = false; _active.interruptionReason = "Unity assembly domain reload interrupted the sample stream.";
            AccumulateSegmentTime();
            CloseWriters(); SaveActive();
        }

        private static void AccumulateSegmentTime()
        {
            if (_active == null || _segmentStartedAt <= 0 || (_active.state != "warming" && _active.state != "running")) return;
            _active.elapsedSeconds += Math.Max(0, EditorApplication.timeSinceStartup - _segmentStartedAt);
            _segmentStartedAt = 0;
        }

        private static void OnQuitting() { if (_active != null && _active.IsActive) AbortActive("Unity Editor quit during profiling."); }
        private static void CloseWriters() { try { _samplesWriter?.Dispose(); } catch { } _samplesWriter = null; DisposeRecorders(); }
        private static void DisposeRecorders() { foreach (var recorder in Recorders) recorder.Dispose(); Recorders.Clear(); }

        private static void FinalizeArtifacts(ProfilingSessionRecord record)
        {
            if (File.Exists(SamplesPath(record.id))) { var info = new FileInfo(SamplesPath(record.id)); record.samplesAvailable = info.Length > 0; record.samplesSizeBytes = info.Length; if (record.samplesAvailable) record.samplesSha256 = ProfilingArtifactStore.Sha256(info.FullName); }
            if (File.Exists(RawPath(record.id))) { var info = new FileInfo(RawPath(record.id)); record.rawAvailable = info.Length > 0; record.rawSizeBytes = info.Length; if (record.rawAvailable) record.rawSha256 = ProfilingArtifactStore.Sha256(info.FullName); }
        }

        private static Dictionary<string, ProfilingStatistics> ReadStatistics(ProfilingSessionRecord record)
        {
            var result = CreateStatistics(record);
            var path = SamplesPath(record.id); if (!File.Exists(path)) return result;
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var start = line.IndexOf("\"values\":[", StringComparison.Ordinal); if (start < 0) continue;
                        start += 10; var end = line.IndexOf(']', start); if (end < 0) continue;
                        var tokens = line.Substring(start, end - start).Split(',');
                        for (var i = 0; i < tokens.Length && i < record.metrics.Length; i++)
                            if (double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) result[record.metrics[i].metricId].Add(value);
                    }
                }
            }
            catch { }
            return result;
        }

        private static Dictionary<string, ProfilingStatistics> CreateStatistics(ProfilingSessionRecord record)
        {
            var result = new Dictionary<string, ProfilingStatistics>(StringComparer.OrdinalIgnoreCase);
            foreach (var metric in record.metrics) result[metric.metricId] = new ProfilingStatistics();
            return result;
        }

        private static void FinalizeStatistics(ProfilingSessionRecord record)
        {
            var source = _liveStatistics ?? ReadStatistics(record);
            var finalized = new List<ProfilingStatisticsRecord>();
            foreach (var metric in record.metrics)
                if (source.TryGetValue(metric.metricId, out var value))
                    finalized.Add(value.ToRecord(metric.metricId));
            record.statistics = finalized.ToArray();
            record.statisticsFinalized = true;
            _liveStatistics = null;
        }

        private static void EnsureFinalizedStatistics(ProfilingSessionRecord record)
        {
            if (record == null || record.IsActive || record.statisticsFinalized) return;
            // Migrate a terminal record written before finalized statistics were cached.
            // New sessions are finalized before their terminal metadata is saved.
            var previousLive = _liveStatistics;
            try
            {
                _liveStatistics = null;
                FinalizeStatistics(record);
                SaveCompleted(record);
            }
            finally { _liveStatistics = previousLive; }
        }

        private static void RecoverRestoreFailure(string reason)
        {
            if (_active == null)
            {
                TryDelete(ActivePath);
                return;
            }

            // FailActive releases the session itself even when it throws, so this only has to report.
            try
            {
                FailActive(reason);
            }
            catch (Exception cleanupException)
            {
                Debug.LogError("[UnionAir] Profiling restore cleanup also failed: " + cleanupException.Message);
            }
        }

        private static void SaveActive() { if (_active == null) return; ProfilingArtifactStore.WriteAtomicJson(ActivePath, _active.ToStorageJson()); SaveCompleted(_active); }
        private static void SaveCompleted(ProfilingSessionRecord record) => ProfilingArtifactStore.WriteAtomicJson(MetadataPath(record.id), record.ToStorageJson());
        private static ProfilingSessionRecord Load(string id)
        {
            if (!IsValidId(id)) return null;
            if (_active != null && _active.id == id) return _active;
            try { var path = MetadataPath(id); return File.Exists(path) ? JsonUtility.FromJson<ProfilingSessionRecord>(File.ReadAllText(path)) : null; } catch { return null; }
        }
        private static List<ProfilingSessionRecord> LoadAll()
        {
            var result = new List<ProfilingSessionRecord>(); if (!Directory.Exists(ProfilingArtifactStore.ProfilingRoot)) return result;
            foreach (var directory in new DirectoryInfo(ProfilingArtifactStore.ProfilingRoot).GetDirectories()) { var record = Load(directory.Name); if (record != null) result.Add(record); }
            result.Sort((a, b) => string.Compare(b.createdAt, a.createdAt, StringComparison.Ordinal)); return result;
        }
        private static int ParseBounded(string value, int fallback, int min, int max) => int.TryParse(value, out var parsed) && parsed >= min && parsed <= max ? parsed : fallback;
        private static string SessionDirectory(string id) => Path.Combine(ProfilingArtifactStore.ProfilingRoot, id);
        private static string MetadataPath(string id) => Path.Combine(SessionDirectory(id), "metadata.json");
        private static string SamplesPath(string id) => Path.Combine(SessionDirectory(id), "samples.ndjson");
        private static string RawPath(string id) => Path.Combine(SessionDirectory(id), "profile.raw");
        private static bool IsValidId(string id) => Guid.TryParseExact(id, "D", out _);
        private static string UtcNow() => DateTime.UtcNow.ToString("o");
        private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    }

    [InitializeOnLoad]
    internal static class ProfilingInit
    {
        static ProfilingInit() => ProfilingService.Initialize();
    }
}

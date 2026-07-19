using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling.Memory;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace LeonAkasaka.UnionAir.Editor
{
    [Serializable]
    internal sealed class MemorySnapshotRecord
    {
        public int schemaVersion = 1;
        public string id = "";
        public string label = "";
        public string state = "capturing";
        public string createdAt = "";
        public string finishedAt = "";
        public string profilingSessionId = "";
        public string testRunId = "";
        public string unityVersion = "";
        public string platform = "";
        public string graphicsApi = "";
        public string scenePath = "";
        public bool isPlaying;
        public long managedUsedBefore;
        public long totalAllocatedBefore;
        public long totalReservedBefore;
        public long managedUsedAfter;
        public long totalAllocatedAfter;
        public long totalReservedAfter;
        public bool snapshotAvailable;
        public long snapshotSizeBytes;
        public string snapshotSha256 = "";
        public string error = "";

        internal string ToJson()
        {
            var sb = new StringBuilder("{\"schemaVersion\":1,\"id\":\"").Append(RestResponse.EscapeJson(id));
            sb.Append("\",\"label\":\"").Append(RestResponse.EscapeJson(label));
            sb.Append("\",\"state\":\"").Append(RestResponse.EscapeJson(state)).Append("\"");
            AppendNullable(sb, "createdAt", createdAt); AppendNullable(sb, "finishedAt", finishedAt);
            sb.Append(",\"related\":{\"profilingSessionId\":").Append(RestResponse.FormatNullableString(EmptyToNull(profilingSessionId)));
            sb.Append(",\"testRunId\":").Append(RestResponse.FormatNullableString(EmptyToNull(testRunId))).Append("}");
            sb.Append(",\"environment\":{\"unityVersion\":\"").Append(RestResponse.EscapeJson(unityVersion));
            sb.Append("\",\"platform\":\"").Append(RestResponse.EscapeJson(platform));
            sb.Append("\",\"graphicsApi\":\"").Append(RestResponse.EscapeJson(graphicsApi));
            sb.Append("\",\"scenePath\":\"").Append(RestResponse.EscapeJson(scenePath));
            sb.Append("\",\"isPlaying\":").Append(RestResponse.FormatBool(isPlaying)).Append("}");
            sb.Append(",\"memory\":{\"before\":{\"managedUsedBytes\":").Append(managedUsedBefore);
            sb.Append(",\"totalAllocatedBytes\":").Append(totalAllocatedBefore).Append(",\"totalReservedBytes\":").Append(totalReservedBefore);
            sb.Append("},\"after\":");
            if (state == "capturing") sb.Append("null}");
            else
            {
                sb.Append("{\"managedUsedBytes\":").Append(managedUsedAfter);
                sb.Append(",\"totalAllocatedBytes\":").Append(totalAllocatedAfter).Append(",\"totalReservedBytes\":").Append(totalReservedAfter).Append("}}");
            }
            sb.Append(",\"artifact\":");
            if (!snapshotAvailable) sb.Append("null");
            else
            {
                sb.Append("{\"projectRelativePath\":\"Library/UnionAir/MemorySnapshots/").Append(RestResponse.EscapeJson(id));
                sb.Append("/snapshot.snap\",\"url\":\"/api/memory-snapshots/").Append(RestResponse.EscapeJson(id));
                sb.Append("/snapshot\",\"sizeBytes\":").Append(snapshotSizeBytes).Append(",\"sha256\":\"").Append(snapshotSha256).Append("\"}");
            }
            if (!string.IsNullOrEmpty(error)) sb.Append(",\"error\":\"").Append(RestResponse.EscapeJson(error)).Append("\"");
            sb.Append("}"); return sb.ToString();
        }

        private static string EmptyToNull(string value) => string.IsNullOrEmpty(value) ? null : value;
        private static void AppendNullable(StringBuilder sb, string name, string value)
            => sb.Append(",\"").Append(name).Append("\":").Append(RestResponse.FormatNullableString(EmptyToNull(value)));
    }

    internal static class MemorySnapshotService
    {
        private static readonly string ActivePath = Path.Combine(ProfilingArtifactStore.SnapshotRoot, "active.json");
        private static MemorySnapshotRecord _active;

        internal static void Initialize()
        {
            if (!File.Exists(ActivePath)) return;
            try
            {
                _active = JsonUtility.FromJson<MemorySnapshotRecord>(File.ReadAllText(ActivePath));
                if (_active != null && _active.state == "capturing")
                {
                    _active.state = "failed";
                    _active.finishedAt = UtcNow();
                    _active.error = "Unity assembly domain reload interrupted the memory snapshot callback.";
                    Save(_active); TryDelete(ActivePath);
                }
            }
            catch { }
            _active = null;
        }

        internal static void Start(UnionAirRequestContext ctx)
        {
            if (_active != null && _active.state == "capturing") { RestResponse.SendError(ctx.Response, "Another memory snapshot is already being captured.", 409); return; }
            if (ProfilingArtifactStore.IsOverQuota()) { RestResponse.SendError(ctx.Response, "Profiling artifact storage exceeds the 5 GiB limit. Delete completed artifacts before capturing again.", 507); return; }
            var body = RequestBodyReader.ReadString(ctx.Request);
            var id = Guid.NewGuid().ToString("D");
            _active = new MemorySnapshotRecord
            {
                id = id, label = RequestBodyReader.GetString(body, "label") ?? "", createdAt = UtcNow(),
                profilingSessionId = RequestBodyReader.GetString(body, "profilingSessionId") ?? "",
                testRunId = RequestBodyReader.GetString(body, "testRunId") ?? "",
                unityVersion = Application.unityVersion, platform = Application.platform.ToString(),
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), scenePath = SceneManager.GetActiveScene().path ?? "",
                isPlaying = EditorApplication.isPlaying, managedUsedBefore = Profiler.GetMonoUsedSizeLong(),
                totalAllocatedBefore = Profiler.GetTotalAllocatedMemoryLong(), totalReservedBefore = Profiler.GetTotalReservedMemoryLong()
            };
            var directory = ProfilingArtifactStore.CreateDirectory(ProfilingArtifactStore.SnapshotRoot, id);
            Save(_active); ProfilingArtifactStore.WriteAtomicJson(ActivePath, JsonUtility.ToJson(_active));
            var temp = Path.GetFullPath(Path.Combine(directory, "snapshot.tmp"));
            try
            {
                MemoryProfiler.TakeSnapshot(temp, OnFinished,
                    CaptureFlags.ManagedObjects | CaptureFlags.NativeObjects | CaptureFlags.NativeAllocations);
            }
            catch (Exception ex)
            {
                Fail(ex.Message); RestResponse.SendError(ctx.Response, "Memory snapshot could not be started: " + ex.Message, 500); return;
            }
            RestResponse.Send(ctx.Response, $"{{\"schemaVersion\":1,\"id\":\"{id}\",\"state\":\"capturing\",\"statusUrl\":\"/api/memory-snapshots/{id}\"}}", 202);
        }

        private static void OnFinished(string path, bool success)
        {
            if (_active == null) return;
            if (!success || string.IsNullOrEmpty(path) || !File.Exists(path)) { Fail("Unity Memory Profiler did not produce a snapshot file."); return; }
            try
            {
                var destination = SnapshotPath(_active.id); if (File.Exists(destination)) File.Delete(destination); File.Move(path, destination);
                var info = new FileInfo(destination); _active.snapshotAvailable = true; _active.snapshotSizeBytes = info.Length;
                _active.snapshotSha256 = ProfilingArtifactStore.Sha256(destination); _active.managedUsedAfter = Profiler.GetMonoUsedSizeLong();
                _active.totalAllocatedAfter = Profiler.GetTotalAllocatedMemoryLong(); _active.totalReservedAfter = Profiler.GetTotalReservedMemoryLong();
                _active.state = "completed"; _active.finishedAt = UtcNow(); Save(_active); var id = _active.id; _active = null; TryDelete(ActivePath);
                ProfilingArtifactStore.Trim(ProfilingArtifactStore.SnapshotRoot, 4, id);
            }
            catch (Exception ex) { Fail("Memory snapshot finalization failed: " + ex.Message); }
        }

        internal static void Status(UnionAirRequestContext ctx)
        {
            var record = Load(ctx.RouteValues["id"]); if (record == null) { RestResponse.SendNotFound(ctx.Response, "Memory snapshot not found."); return; }
            RestResponse.Send(ctx.Response, record.ToJson());
        }

        internal static void List(UnionAirRequestContext ctx)
        {
            var records = LoadAll(); var sb = new StringBuilder("{\"schemaVersion\":1,\"snapshots\":[");
            for (var i = 0; i < records.Count; i++) { if (i > 0) sb.Append(","); sb.Append(records[i].ToJson()); }
            sb.Append("]}"); RestResponse.Send(ctx.Response, sb.ToString());
        }

        internal static void Download(UnionAirRequestContext ctx)
        {
            var record = Load(ctx.RouteValues["id"]); if (record == null || !record.snapshotAvailable) { RestResponse.SendNotFound(ctx.Response, "Memory snapshot artifact is not available."); return; }
            RestResponse.SendArtifactFile(ctx, SnapshotPath(record.id), "application/octet-stream", $"MemorySnapshot-{record.id}.snap");
        }

        internal static void Delete(UnionAirRequestContext ctx)
        {
            var id = ctx.RouteValues["id"]; var record = Load(id); if (record == null) { RestResponse.SendNotFound(ctx.Response, "Memory snapshot not found."); return; }
            if (record.state == "capturing") { RestResponse.SendError(ctx.Response, "A memory snapshot cannot be deleted while it is being captured.", 409); return; }
            ProfilingArtifactStore.TryDeleteDirectory(Path.Combine(ProfilingArtifactStore.SnapshotRoot, id));
            RestResponse.Send(ctx.Response, $"{{\"deleted\":true,\"id\":\"{RestResponse.EscapeJson(id)}\"}}");
        }

        private static void Fail(string error)
        {
            if (_active == null) return; _active.state = "failed"; _active.finishedAt = UtcNow(); _active.error = error;
            _active.managedUsedAfter = Profiler.GetMonoUsedSizeLong(); _active.totalAllocatedAfter = Profiler.GetTotalAllocatedMemoryLong();
            _active.totalReservedAfter = Profiler.GetTotalReservedMemoryLong();
            TryDelete(Path.Combine(DirectoryPath(_active.id), "snapshot.tmp"));
            Save(_active); _active = null; TryDelete(ActivePath);
        }
        private static void Save(MemorySnapshotRecord record) => ProfilingArtifactStore.WriteAtomicJson(MetadataPath(record.id), JsonUtility.ToJson(record));
        private static MemorySnapshotRecord Load(string id)
        {
            if (!Guid.TryParseExact(id, "D", out _)) return null;
            if (_active != null && _active.id == id) return _active;
            try { var path = MetadataPath(id); return File.Exists(path) ? JsonUtility.FromJson<MemorySnapshotRecord>(File.ReadAllText(path)) : null; } catch { return null; }
        }
        private static List<MemorySnapshotRecord> LoadAll()
        {
            var result = new List<MemorySnapshotRecord>(); if (!Directory.Exists(ProfilingArtifactStore.SnapshotRoot)) return result;
            foreach (var directory in new DirectoryInfo(ProfilingArtifactStore.SnapshotRoot).GetDirectories()) { var record = Load(directory.Name); if (record != null) result.Add(record); }
            result.Sort((a, b) => string.Compare(b.createdAt, a.createdAt, StringComparison.Ordinal)); return result;
        }
        private static string DirectoryPath(string id) => Path.Combine(ProfilingArtifactStore.SnapshotRoot, id);
        private static string MetadataPath(string id) => Path.Combine(DirectoryPath(id), "metadata.json");
        private static string SnapshotPath(string id) => Path.Combine(DirectoryPath(id), "snapshot.snap");
        private static string UtcNow() => DateTime.UtcNow.ToString("o");
        private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    }

    [InitializeOnLoad]
    internal static class MemorySnapshotInit
    {
        static MemorySnapshotInit() => MemorySnapshotService.Initialize();
    }
}

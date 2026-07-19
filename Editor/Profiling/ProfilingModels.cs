using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    [Serializable]
    internal sealed class ProfilingMetricRecord
    {
        public string metricId = "";
        public string category = "";
        public string marker = "";
        public string unit = "";
    }

    [Serializable]
    internal sealed class ProfilingStatisticsRecord
    {
        public string metricId = "";
        public int samples;
        public double min;
        public double max;
        public double mean;
        public double p50;
        public double p95;
        public double p99;
        public double first;
        public double last;
        public double delta;
        public int nonZeroSamples;

        internal void AppendJson(StringBuilder sb, string unit)
        {
            sb.Append("{\"unit\":\"").Append(RestResponse.EscapeJson(unit)).Append("\",\"samples\":").Append(samples);
            if (samples == 0) { sb.Append("}"); return; }
            Append(sb, "min", min); Append(sb, "max", max); Append(sb, "mean", mean);
            Append(sb, "p50", p50); Append(sb, "p95", p95); Append(sb, "p99", p99);
            Append(sb, "first", first); Append(sb, "last", last); Append(sb, "delta", delta);
            sb.Append(",\"nonZeroSamples\":").Append(nonZeroSamples).Append("}");
        }

        private static void Append(StringBuilder sb, string name, double value)
            => sb.Append(",\"").Append(name).Append("\":").Append(value.ToString("G17", CultureInfo.InvariantCulture));
    }

    [Serializable]
    internal sealed class ProfilingSessionRecord
    {
        public int schemaVersion = 1;
        public string id = "";
        public string label = "";
        public string state = "armed";
        public string createdAt = "";
        public string startedAt = "";
        public string finishedAt = "";
        public string testRunId = "";
        public bool attachedToTest;
        public int warmupFrames = 60;
        public int maxFrames = 600;
        public float maxDurationSeconds = 30;
        public bool captureRaw;
        public bool continuous = true;
        public int segment;
        public int domainReloadCount;
        public int warmupRemaining;
        public int capturedFrames;
        public double elapsedSeconds;
        public string interruptionReason = "";
        public string error = "";
        public string unityVersion = "";
        public string platform = "";
        public string graphicsApi = "";
        public string scenePath = "";
        public bool isPlaying;
        public ProfilingMetricRecord[] metrics = new ProfilingMetricRecord[0];
        public bool samplesAvailable;
        public long samplesSizeBytes;
        public string samplesSha256 = "";
        public bool rawAvailable;
        public long rawSizeBytes;
        public string rawSha256 = "";
        public bool statisticsFinalized;
        public ProfilingStatisticsRecord[] statistics = new ProfilingStatisticsRecord[0];
        public bool previousProfilerEnabled;
        public bool previousBinaryLog;
        public string previousLogFile = "";

        internal bool IsActive => state == "armed" || state == "warming" || state == "running";

        internal string ToStorageJson() => JsonUtility.ToJson(this);

        internal string ToApiJson(bool includeStatistics)
        {
            var sb = new StringBuilder();
            sb.Append("{\"schemaVersion\":1,\"id\":\"").Append(RestResponse.EscapeJson(id));
            sb.Append("\",\"label\":\"").Append(RestResponse.EscapeJson(label));
            sb.Append("\",\"state\":\"").Append(RestResponse.EscapeJson(state)).Append("\"");
            AppendNullable(sb, "createdAt", createdAt);
            AppendNullable(sb, "startedAt", startedAt);
            AppendNullable(sb, "finishedAt", finishedAt);
            sb.Append(",\"source\":{\"type\":\"").Append(attachedToTest ? "testRun" : "manual").Append("\",\"testRunId\":");
            sb.Append(RestResponse.FormatNullableString(string.IsNullOrEmpty(testRunId) ? null : testRunId)).Append("}");
            sb.Append(",\"configuration\":{\"warmupFrames\":").Append(warmupFrames);
            sb.Append(",\"maxFrames\":").Append(maxFrames).Append(",\"maxDurationSeconds\":");
            sb.Append(maxDurationSeconds.ToString("G9", CultureInfo.InvariantCulture));
            sb.Append(",\"captureRaw\":").Append(RestResponse.FormatBool(captureRaw)).Append(",\"metricIds\":[");
            for (var i = 0; i < metrics.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\"").Append(RestResponse.EscapeJson(metrics[i].metricId)).Append("\"");
            }
            sb.Append("]}");
            sb.Append(",\"environment\":{\"unityVersion\":\"").Append(RestResponse.EscapeJson(unityVersion));
            sb.Append("\",\"target\":\"editor");
            sb.Append("\",\"platform\":\"").Append(RestResponse.EscapeJson(platform));
            sb.Append("\",\"graphicsApi\":\"").Append(RestResponse.EscapeJson(graphicsApi));
            sb.Append("\",\"scenePath\":\"").Append(RestResponse.EscapeJson(scenePath));
            sb.Append("\",\"isPlaying\":").Append(RestResponse.FormatBool(isPlaying)).Append("}");
            sb.Append(",\"sampling\":{\"capturedFrames\":").Append(capturedFrames);
            sb.Append(",\"elapsedSeconds\":").Append(elapsedSeconds.ToString("G17", CultureInfo.InvariantCulture));
            sb.Append(",\"segments\":").Append(Math.Max(0, segment));
            sb.Append(",\"domainReloadCount\":").Append(domainReloadCount);
            sb.Append(",\"continuous\":").Append(RestResponse.FormatBool(continuous));
            sb.Append(",\"interruptionReason\":").Append(RestResponse.FormatNullableString(string.IsNullOrEmpty(interruptionReason) ? null : interruptionReason)).Append("}");
            sb.Append(",\"metrics\":{");
            var first = true;
            if (includeStatistics && statisticsFinalized && statistics != null)
            {
                foreach (var metric in metrics)
                {
                    var value = Array.Find(statistics, item =>
                        string.Equals(item.metricId, metric.metricId, StringComparison.OrdinalIgnoreCase));
                    if (value == null) continue;
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append("\"").Append(RestResponse.EscapeJson(metric.metricId)).Append("\":");
                    value.AppendJson(sb, metric.unit);
                }
            }
            sb.Append("}");
            sb.Append(",\"artifacts\":{");
            AppendArtifact(sb, "samples", samplesAvailable, $"Library/UnionAir/Profiling/{id}/samples.ndjson", $"/api/profiling/sessions/{id}/samples.ndjson", samplesSizeBytes, samplesSha256);
            sb.Append(",");
            AppendArtifact(sb, "profilerRaw", rawAvailable, $"Library/UnionAir/Profiling/{id}/profile.raw", $"/api/profiling/sessions/{id}/profile.raw", rawSizeBytes, rawSha256);
            sb.Append("}");
            if (!string.IsNullOrEmpty(error)) sb.Append(",\"error\":\"").Append(RestResponse.EscapeJson(error)).Append("\"");
            sb.Append("}");
            return sb.ToString();
        }

        private static void AppendNullable(StringBuilder sb, string name, string value)
            => sb.Append(",\"").Append(name).Append("\":").Append(RestResponse.FormatNullableString(string.IsNullOrEmpty(value) ? null : value));

        private static void AppendArtifact(StringBuilder sb, string name, bool available, string path, string url, long size, string sha)
        {
            sb.Append("\"").Append(name).Append("\":");
            if (!available) { sb.Append("null"); return; }
            sb.Append("{\"projectRelativePath\":\"").Append(RestResponse.EscapeJson(path.Replace('\\', '/')));
            sb.Append("\",\"url\":\"").Append(RestResponse.EscapeJson(url));
            sb.Append("\",\"sizeBytes\":").Append(size).Append(",\"sha256\":\"").Append(sha).Append("\"}");
        }
    }

    internal sealed class ProfilingStatistics
    {
        private readonly List<double> _values = new List<double>();
        private double _first;
        private double _last;
        internal void Add(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return;
            if (_values.Count == 0) _first = value;
            _last = value;
            _values.Add(value);
        }

        internal ProfilingStatisticsRecord ToRecord(string metricId)
        {
            _values.Sort();
            var record = new ProfilingStatisticsRecord { metricId = metricId, samples = _values.Count };
            if (_values.Count == 0) return record;
            double sum = 0; var nonZero = 0;
            foreach (var value in _values) { sum += value; if (value != 0) nonZero++; }
            record.min = _values[0]; record.max = _values[_values.Count - 1]; record.mean = sum / _values.Count;
            record.p50 = Percentile(.50); record.p95 = Percentile(.95); record.p99 = Percentile(.99);
            record.first = _first; record.last = _last; record.delta = _last - _first; record.nonZeroSamples = nonZero;
            return record;
        }
        private double Percentile(double p) => _values[(int)Math.Min(_values.Count - 1, Math.Ceiling(p * _values.Count) - 1)];
    }
}

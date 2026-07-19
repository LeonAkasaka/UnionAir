using System;
using System.Text;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    [Serializable]
    internal sealed class TestRunFilters
    {
        public string[] testNames = new string[0];
        public string[] groupNames = new string[0];
        public string[] categoryNames = new string[0];
        public string[] assemblyNames = new string[0];
    }

    [Serializable]
    internal sealed class TestRunRecord
    {
        public string id = "";
        public string mode = "";
        public string state = "queued";
        public string result = "";
        public TestRunFilters filters = new TestRunFilters();
        public string startedAt = "";
        public string finishedAt = "";
        public string currentTest = "";
        public int completed;
        public int total;
        public int passed;
        public int failed;
        public int skipped;
        public int inconclusive;
        public double duration;
        public int assertCount;
        public bool resultFileAvailable;
        public string resultFileSha256 = "";
        public string error = "";
        public string profilingSessionId = "";

        internal bool IsActive => state == "queued" || state == "running" || state == "canceling";

        internal string ToApiJson()
        {
            var sb = new StringBuilder();
            sb.Append("{");
            AppendString(sb, "id", id);
            sb.Append(",");
            AppendString(sb, "state", state);
            sb.Append(",\"result\":");
            sb.Append(RestResponse.FormatNullableString(string.IsNullOrEmpty(result) ? null : result));
            sb.Append(",");
            AppendString(sb, "mode", mode);
            sb.Append(",\"filters\":");
            sb.Append(JsonUtility.ToJson(filters ?? new TestRunFilters()));
            sb.Append(",\"startedAt\":");
            sb.Append(RestResponse.FormatNullableString(string.IsNullOrEmpty(startedAt) ? null : startedAt));
            sb.Append(",\"finishedAt\":");
            sb.Append(RestResponse.FormatNullableString(string.IsNullOrEmpty(finishedAt) ? null : finishedAt));
            sb.Append(",\"currentTest\":");
            sb.Append(RestResponse.FormatNullableString(string.IsNullOrEmpty(currentTest) ? null : currentTest));
            sb.Append($",\"progress\":{{\"completed\":{completed},\"total\":{total}}}");
            sb.Append($",\"summary\":{{\"passed\":{passed},\"failed\":{failed},\"skipped\":{skipped},\"inconclusive\":{inconclusive},\"duration\":{duration.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)},\"assertCount\":{assertCount}}}");
            sb.Append($",\"resultFileAvailable\":{RestResponse.FormatBool(resultFileAvailable)}");
            sb.Append(",\"resultUrl\":");
            sb.Append(RestResponse.FormatNullableString(resultFileAvailable ? $"/api/test-runs/{id}/results.xml" : null));
            sb.Append(",\"profilingSessionId\":");
            sb.Append(RestResponse.FormatNullableString(string.IsNullOrEmpty(profilingSessionId) ? null : profilingSessionId));
            sb.Append(",\"profilingUrl\":");
            sb.Append(RestResponse.FormatNullableString(string.IsNullOrEmpty(profilingSessionId) ? null : $"/api/profiling/sessions/{profilingSessionId}"));
            if (!string.IsNullOrEmpty(error))
            {
                sb.Append(",");
                AppendString(sb, "error", error);
            }
            sb.Append("}");
            return sb.ToString();
        }

        private static void AppendString(StringBuilder sb, string name, string value)
        {
            sb.Append("\"").Append(name).Append("\":\"")
                .Append(RestResponse.EscapeJson(value ?? "")).Append("\"");
        }

    }

    [Serializable]
    internal sealed class LatestResultTransaction
    {
        public string id = "";
        public string sha256 = "";
        public bool hadXml;
        public bool hadJson;
    }
}

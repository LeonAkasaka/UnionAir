using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>One diagnostic captured from a <c>BuildReport</c> step.</summary>
    [Serializable]
    internal sealed class BuildMessageRecord
    {
        /// <summary>Severity taken from the Unity log type: error, warning, or info.</summary>
        public string severity = "";

        /// <summary>Build step the message was reported in.</summary>
        public string step = "";

        public string message = "";
    }

    /// <summary>
    /// Durable snapshot of <c>BuildReport.summary</c>.
    /// </summary>
    /// <remarks>
    /// Snapshotted into plain fields immediately after <c>BuildPipeline.BuildPlayer</c> returns.
    /// A <c>BuildReport</c> is a Unity object backed by native state that does not survive a domain
    /// reload, so holding a reference to it would leave the record readable only until the next
    /// recompile.
    /// </remarks>
    [Serializable]
    internal sealed class BuildReportRecord
    {
        /// <summary><c>succeeded</c>, <c>failed</c>, <c>cancelled</c>, or <c>unknown</c>.</summary>
        public string result = "";

        public string platform = "";
        public string platformGroup = "";
        public string outputPath = "";
        public string startedAt = "";
        public string endedAt = "";
        public double totalTimeSeconds;

        /// <summary>Total output size Unity reported, in bytes.</summary>
        public long totalSizeBytes;

        public int totalErrors;
        public int totalWarnings;

        public List<BuildMessageRecord> messages = new List<BuildMessageRecord>();
        public bool messagesTruncated;
    }

    /// <summary>Durable record of a single player build.</summary>
    [Serializable]
    internal sealed class BuildRecord
    {
        public string id = "";

        /// <summary>Always <c>unionAir</c>; builds are never adopted from an external trigger.</summary>
        public string source = "unionAir";

        /// <summary><c>queued</c>, <c>running</c>, <c>completed</c>, <c>failed</c>, or <c>aborted</c>.</summary>
        public string state = "queued";

        /// <summary><c>succeeded</c>, <c>failed</c>, <c>cancelled</c>, or empty while active.</summary>
        public string result = "";

        public string buildTarget = "";
        public string buildTargetGroup = "";
        public string namedBuildTarget = "";

        public string sessionId = "";
        public string requestedAt = "";
        public string startedAt = "";
        public string finishedAt = "";
        public double durationSeconds;
        public int lifecycleGenerationAtRequest;
        public int lifecycleGenerationAtFinish;

        /// <summary>Resolved build options this build ran with.</summary>
        public bool development;
        public bool allowDebugging;
        public bool connectProfiler;
        public bool deepProfiling;
        public bool waitForPlayerConnection;
        public bool clean;
        public bool strictMode;

        /// <summary>Enabled build scenes, in build index order.</summary>
        public List<string> scenes = new List<string>();

        /// <summary>
        /// Compile record for the player compilation this build ran, when one was produced.
        /// </summary>
        public string compileId = "";

        /// <summary>Project-relative directory holding the output and the report.</summary>
        public string outputDirectory = "";

        /// <summary>Project-relative path Unity was told to build to.</summary>
        public string outputPath = "";

        /// <summary>Size of <see cref="outputDirectory"/> when the build finished, in bytes.</summary>
        public long outputBytes;

        /// <summary>Project-relative path of the report written next to the output.</summary>
        public string reportPath = "";

        public BuildReportRecord report;

        public string error = "";

        internal bool IsActive => state == "queued" || state == "running";

        /// <summary>
        /// Renders the record as the API response shape.
        /// </summary>
        /// <param name="outputAvailable">
        /// Whether the artifact directory still exists. Computed at read time rather than stored,
        /// because retention can remove the output long after the record was written.
        /// </param>
        internal string ToApiJson(bool outputAvailable)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            AppendString(sb, "id", id);
            sb.Append(",");
            AppendString(sb, "source", source);
            sb.Append(",");
            AppendString(sb, "state", state);
            sb.Append(",\"result\":").Append(NullableString(result));
            sb.Append(",");
            AppendString(sb, "buildTarget", buildTarget);
            sb.Append(",");
            AppendString(sb, "buildTargetGroup", buildTargetGroup);
            sb.Append(",");
            AppendString(sb, "namedBuildTarget", namedBuildTarget);
            sb.Append(",\"sessionId\":").Append(NullableString(sessionId));
            sb.Append(",\"requestedAt\":").Append(NullableString(requestedAt));
            sb.Append(",\"startedAt\":").Append(NullableString(startedAt));
            sb.Append(",\"finishedAt\":").Append(NullableString(finishedAt));
            sb.Append(",\"durationSeconds\":")
              .Append(durationSeconds.ToString("G17", CultureInfo.InvariantCulture));
            sb.Append(",\"lifecycleGenerationAtRequest\":").Append(Int(lifecycleGenerationAtRequest));
            sb.Append(",\"lifecycleGenerationAtFinish\":").Append(Int(lifecycleGenerationAtFinish));

            sb.Append(",\"options\":{");
            sb.Append("\"development\":").Append(RestResponse.FormatBool(development));
            sb.Append(",\"allowDebugging\":").Append(RestResponse.FormatBool(allowDebugging));
            sb.Append(",\"connectProfiler\":").Append(RestResponse.FormatBool(connectProfiler));
            sb.Append(",\"deepProfiling\":").Append(RestResponse.FormatBool(deepProfiling));
            sb.Append(",\"waitForPlayerConnection\":").Append(RestResponse.FormatBool(waitForPlayerConnection));
            sb.Append(",\"clean\":").Append(RestResponse.FormatBool(clean));
            sb.Append(",\"strictMode\":").Append(RestResponse.FormatBool(strictMode));
            sb.Append("}");

            sb.Append(",\"scenes\":[");
            for (var i = 0; i < scenes.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(RestResponse.FormatNullableString(scenes[i]));
            }
            sb.Append("]");

            sb.Append(",\"compileId\":").Append(NullableString(compileId));
            sb.Append(",\"outputDirectory\":").Append(NullableString(outputDirectory));
            sb.Append(",\"outputPath\":").Append(NullableString(outputPath));
            sb.Append(",\"outputBytes\":").Append(Long(outputBytes));
            sb.Append(",\"outputAvailable\":").Append(RestResponse.FormatBool(outputAvailable));
            sb.Append(",\"reportPath\":").Append(NullableString(reportPath));

            sb.Append(",\"report\":");
            AppendReport(sb, report);

            sb.Append(",\"error\":").Append(NullableString(error));
            sb.Append(",\"statusUrl\":\"/api/builds/")
              .Append(RestResponse.EscapeJson(id)).Append("\"");
            sb.Append("}");
            return sb.ToString();
        }

        private static void AppendReport(StringBuilder sb, BuildReportRecord report)
        {
            if (report == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append("{");
            AppendString(sb, "result", report.result);
            sb.Append(",");
            AppendString(sb, "platform", report.platform);
            sb.Append(",");
            AppendString(sb, "platformGroup", report.platformGroup);
            sb.Append(",\"outputPath\":").Append(NullableString(report.outputPath));
            sb.Append(",\"startedAt\":").Append(NullableString(report.startedAt));
            sb.Append(",\"endedAt\":").Append(NullableString(report.endedAt));
            sb.Append(",\"totalTimeSeconds\":")
              .Append(report.totalTimeSeconds.ToString("G17", CultureInfo.InvariantCulture));
            sb.Append(",\"totalSizeBytes\":").Append(Long(report.totalSizeBytes));
            sb.Append(",\"totalErrors\":").Append(Int(report.totalErrors));
            sb.Append(",\"totalWarnings\":").Append(Int(report.totalWarnings));

            sb.Append(",\"messages\":[");
            var messages = report.messages ?? new List<BuildMessageRecord>();
            for (var i = 0; i < messages.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("{");
                AppendString(sb, "severity", messages[i].severity);
                sb.Append(",");
                AppendString(sb, "step", messages[i].step);
                sb.Append(",");
                AppendString(sb, "message", messages[i].message);
                sb.Append("}");
            }
            sb.Append("],\"messagesTruncated\":")
              .Append(RestResponse.FormatBool(report.messagesTruncated));
            sb.Append("}");
        }

        /// <summary>Renders the compact shape returned by the collection endpoint.</summary>
        internal string ToSummaryJson(bool outputAvailable)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            AppendString(sb, "id", id);
            sb.Append(",");
            AppendString(sb, "state", state);
            sb.Append(",\"result\":").Append(NullableString(result));
            sb.Append(",");
            AppendString(sb, "buildTarget", buildTarget);
            sb.Append(",\"requestedAt\":").Append(NullableString(requestedAt));
            sb.Append(",\"finishedAt\":").Append(NullableString(finishedAt));
            sb.Append(",\"durationSeconds\":")
              .Append(durationSeconds.ToString("G17", CultureInfo.InvariantCulture));
            sb.Append(",\"outputDirectory\":").Append(NullableString(outputDirectory));
            sb.Append(",\"outputBytes\":").Append(Long(outputBytes));
            sb.Append(",\"outputAvailable\":").Append(RestResponse.FormatBool(outputAvailable));
            sb.Append(",\"compileId\":").Append(NullableString(compileId));
            sb.Append(",\"error\":").Append(NullableString(error));
            sb.Append(",\"statusUrl\":\"/api/builds/")
              .Append(RestResponse.EscapeJson(id)).Append("\"");
            sb.Append("}");
            return sb.ToString();
        }

        private static void AppendString(StringBuilder sb, string name, string value)
            => sb.Append("\"").Append(name).Append("\":\"").Append(RestResponse.EscapeJson(value)).Append("\"");

        private static string NullableString(string value)
            => RestResponse.FormatNullableString(string.IsNullOrEmpty(value) ? null : value);

        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Long(long value) => value.ToString(CultureInfo.InvariantCulture);
    }
}

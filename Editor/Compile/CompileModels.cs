using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>One diagnostic produced by a script compilation cycle.</summary>
    [Serializable]
    internal sealed class CompileMessageRecord
    {
        /// <summary>Severity taken from <c>CompilerMessageType</c>: error, warning, or info.</summary>
        public string severity = "";

        /// <summary>Diagnostic code such as <c>CS0103</c>; empty when the message carries none.</summary>
        public string code = "";

        /// <summary>Project-relative source path; empty for build-system diagnostics.</summary>
        public string file = "";

        /// <summary>1-based line, or 0 when the diagnostic has no source position.</summary>
        public int line;

        /// <summary>1-based column, or 0 when the diagnostic has no source position.</summary>
        public int column;

        /// <summary>Assembly the diagnostic was reported against.</summary>
        public string assembly = "";

        /// <summary>Message text with Unity's position and code prefix removed.</summary>
        public string message = "";

        /// <summary>Original message exactly as the compiler reported it.</summary>
        public string raw = "";
    }

    /// <summary>Per-assembly outcome within a compilation cycle.</summary>
    [Serializable]
    internal sealed class CompileAssemblyRecord
    {
        public string name = "";
        public string path = "";
        public string outputDirectory = "";
        public bool compiled;
        public int errorCount;
        public int warningCount;
    }

    /// <summary>Durable record of a single script compilation cycle.</summary>
    [Serializable]
    internal sealed class CompileRecord
    {
        public string id = "";

        /// <summary><c>unionAir</c> when requested through the API, otherwise <c>external</c>.</summary>
        public string source = "";

        /// <summary><c>queued</c>, <c>running</c>, <c>completed</c>, or <c>aborted</c>.</summary>
        public string state = "queued";

        /// <summary><c>succeeded</c>, <c>upToDate</c>, <c>failed</c>, <c>aborted</c>, or <c>notStarted</c>.</summary>
        public string result = "";

        /// <summary><c>editor</c>, <c>player</c>, or <c>other</c>.</summary>
        public string target = "editor";

        public string sessionId = "";
        public string requestedAt = "";
        public string startedAt = "";
        public string finishedAt = "";
        public double durationSeconds;
        public int lifecycleGenerationAtRequest;
        public int lifecycleGenerationAtFinish;
        public int errorCount;
        public int warningCount;
        public List<CompileAssemblyRecord> assemblies = new List<CompileAssemblyRecord>();

        /// <summary>
        /// Number of assemblies Unity reported as not needing compilation.
        /// </summary>
        /// <remarks>
        /// Stored as a count rather than a list: a routine cycle skips 70+ assemblies, and naming
        /// them added kilobytes to every poll response and to every interval write without saying
        /// anything <c>result</c> and <c>assemblies</c> do not already say.
        /// </remarks>
        public int unchangedAssemblyCount;

        public List<CompileMessageRecord> messages = new List<CompileMessageRecord>();
        public bool messagesTruncated;
        public string error = "";

        internal bool IsActive => state == "queued" || state == "running";

        /// <summary>
        /// Renders the record as the API response shape.
        /// </summary>
        /// <returns>A complete JSON object.</returns>
        /// <remarks>
        /// Hand-written because <c>JsonUtility</c> cannot emit <c>null</c> or nested objects, and
        /// absent positions must be <c>null</c> rather than <c>0</c> so a client does not mistake
        /// a build-system diagnostic for one at line 0.
        /// </remarks>
        internal string ToApiJson()
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
            AppendString(sb, "target", target);
            sb.Append(",\"sessionId\":").Append(NullableString(sessionId));
            sb.Append(",\"requestedAt\":").Append(NullableString(requestedAt));
            sb.Append(",\"startedAt\":").Append(NullableString(startedAt));
            sb.Append(",\"finishedAt\":").Append(NullableString(finishedAt));
            sb.Append(",\"durationSeconds\":")
              .Append(durationSeconds.ToString("G17", CultureInfo.InvariantCulture));
            sb.Append(",\"lifecycleGenerationAtRequest\":").Append(Int(lifecycleGenerationAtRequest));
            sb.Append(",\"lifecycleGenerationAtFinish\":").Append(Int(lifecycleGenerationAtFinish));
            sb.Append(",\"errorCount\":").Append(Int(errorCount));
            sb.Append(",\"warningCount\":").Append(Int(warningCount));

            sb.Append(",\"assemblies\":[");
            for (var i = 0; i < assemblies.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var a = assemblies[i];
                sb.Append("{");
                AppendString(sb, "name", a.name);
                sb.Append(",");
                AppendString(sb, "path", a.path);
                sb.Append(",");
                AppendString(sb, "outputDirectory", a.outputDirectory);
                sb.Append(",\"compiled\":").Append(RestResponse.FormatBool(a.compiled));
                sb.Append(",\"errorCount\":").Append(Int(a.errorCount));
                sb.Append(",\"warningCount\":").Append(Int(a.warningCount));
                sb.Append("}");
            }
            sb.Append("]");

            sb.Append(",\"unchangedAssemblyCount\":").Append(Int(unchangedAssemblyCount));

            sb.Append(",\"messages\":[");
            for (var i = 0; i < messages.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var m = messages[i];
                sb.Append("{");
                AppendString(sb, "severity", m.severity);
                sb.Append(",\"code\":").Append(NullableString(m.code));
                sb.Append(",\"file\":").Append(NullableString(m.file));
                sb.Append(",\"line\":").Append(m.line > 0 ? Int(m.line) : "null");
                sb.Append(",\"column\":").Append(m.column > 0 ? Int(m.column) : "null");
                sb.Append(",\"assembly\":").Append(NullableString(m.assembly));
                sb.Append(",");
                AppendString(sb, "message", m.message);
                sb.Append(",");
                AppendString(sb, "raw", m.raw);
                sb.Append("}");
            }
            sb.Append("]");

            sb.Append(",\"messagesTruncated\":").Append(RestResponse.FormatBool(messagesTruncated));
            sb.Append(",\"error\":").Append(NullableString(error));
            sb.Append("}");
            return sb.ToString();
        }

        private static void AppendString(StringBuilder sb, string name, string value)
        {
            sb.Append("\"").Append(name).Append("\":\"").Append(RestResponse.EscapeJson(value)).Append("\"");
        }

        private static string NullableString(string value)
            => RestResponse.FormatNullableString(string.IsNullOrEmpty(value) ? null : value);

        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}

using System;
using System.Globalization;
using System.Text;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>Durable record of one active build target switch.</summary>
    /// <remarks>
    /// A switch reimports every asset for the new platform and ends in a domain reload, so the
    /// record has to outlive the process state that requested it. It is the only UnionAir record
    /// whose <em>expected</em> path crosses a reload rather than being ended by one.
    /// </remarks>
    [Serializable]
    internal sealed class BuildTargetSwitchRecord
    {
        public string id = "";
        public string source = "unionAir";

        /// <summary><c>queued</c>, <c>switching</c>, <c>completed</c>, <c>failed</c>, or <c>aborted</c>.</summary>
        public string state = "queued";

        /// <summary>Target the caller asked for.</summary>
        public string requestedTarget = "";
        public string requestedTargetGroup = "";
        public string requestedNamedBuildTarget = "";

        /// <summary>Target that was active when the switch was requested.</summary>
        public string previousTarget = "";

        /// <summary>Target that was active when the record reached a terminal state.</summary>
        public string activeTarget = "";

        public string sessionId = "";
        public string requestedAt = "";
        public string startedAt = "";
        public string finishedAt = "";
        public double durationSeconds;
        public int lifecycleGenerationAtRequest;
        public int lifecycleGenerationAtFinish;
        public string error = "";

        internal bool IsActive => state == "queued" || state == "switching";

        internal string ToApiJson()
        {
            var sb = new StringBuilder();
            sb.Append("{");
            AppendString(sb, "id", id);
            sb.Append(",");
            AppendString(sb, "source", source);
            sb.Append(",");
            AppendString(sb, "state", state);
            sb.Append(",");
            AppendString(sb, "requestedTarget", requestedTarget);
            sb.Append(",");
            AppendString(sb, "requestedTargetGroup", requestedTargetGroup);
            sb.Append(",");
            AppendString(sb, "requestedNamedBuildTarget", requestedNamedBuildTarget);
            sb.Append(",\"previousTarget\":").Append(Nullable(previousTarget));
            sb.Append(",\"activeTarget\":").Append(Nullable(activeTarget));
            sb.Append(",\"sessionId\":").Append(Nullable(sessionId));
            sb.Append(",\"requestedAt\":").Append(Nullable(requestedAt));
            sb.Append(",\"startedAt\":").Append(Nullable(startedAt));
            sb.Append(",\"finishedAt\":").Append(Nullable(finishedAt));
            sb.Append(",\"durationSeconds\":")
              .Append(durationSeconds.ToString("G17", CultureInfo.InvariantCulture));
            sb.Append(",\"lifecycleGenerationAtRequest\":")
              .Append(lifecycleGenerationAtRequest.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"lifecycleGenerationAtFinish\":")
              .Append(lifecycleGenerationAtFinish.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"error\":").Append(Nullable(error));
            sb.Append(",\"statusUrl\":\"/api/build/target/")
              .Append(RestResponse.EscapeJson(id)).Append("\"");
            sb.Append("}");
            return sb.ToString();
        }

        private static void AppendString(StringBuilder sb, string name, string value)
            => sb.Append("\"").Append(name).Append("\":\"").Append(RestResponse.EscapeJson(value)).Append("\"");

        private static string Nullable(string value)
            => RestResponse.FormatNullableString(string.IsNullOrEmpty(value) ? null : value);
    }
}

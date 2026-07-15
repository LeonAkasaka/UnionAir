using System.Collections.Generic;
using System.Net;
using System.Text;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class EditorLogsHandler
    {
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var query  = request.QueryString;
            var type   = (query["type"] ?? "all").Trim().ToLowerInvariant();
            var search = query["search"] ?? "";

            int limit = 100;
            if (int.TryParse(query["limit"], out int parsed) && parsed > 0)
                limit = System.Math.Min(parsed, 1000);

            if (type != "error" && type != "warning" && type != "log" &&
                type != "exception" && type != "assert" && type != "all")
            {
                RestResponse.SendError(response,
                    "Invalid log type. Expected log, warning, error, exception, assert, or all.", 400);
                return;
            }

            List<LogStore.LogEntry> entries = LogStore.GetLogs(type, search, limit);

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"count\":{entries.Count},");
            sb.Append("\"logs\":[");

            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var e = entries[i];
                sb.Append("{");
                sb.Append($"\"type\":\"{RestResponse.EscapeJson(e.Type.ToString())}\",");
                sb.Append($"\"message\":\"{RestResponse.EscapeJson(e.Message)}\",");
                sb.Append($"\"stackTrace\":\"{RestResponse.EscapeJson(e.StackTrace)}\",");
                sb.Append($"\"timestamp\":\"{RestResponse.EscapeJson(e.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss"))}\"");
                sb.Append("}");
            }

            sb.Append("]}");
            RestResponse.Send(response, sb.ToString());
        }
    }
}

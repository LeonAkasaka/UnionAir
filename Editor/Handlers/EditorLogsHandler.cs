using System.Collections.Generic;
using System.Net;
using System.Text;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class EditorLogsHandler : IRequestHandler
    {
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "GET" && request.Url.AbsolutePath == "/api/editor/logs";

        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var query  = request.QueryString;
            var type   = query["type"]   ?? "all";
            var search = query["search"] ?? "";

            int limit = 100;
            if (int.TryParse(query["limit"], out int parsed) && parsed > 0)
                limit = System.Math.Min(parsed, 1000);

            // Normalize type parameter
            if (type != "error" && type != "warning" && type != "log" &&
                type != "exception" && type != "assert")
                type = "all";

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
                sb.Append($"\"type\":\"{e.Type}\",");
                sb.Append($"\"message\":\"{RestResponse.EscapeJson(e.Message)}\",");
                sb.Append($"\"stackTrace\":\"{RestResponse.EscapeJson(e.StackTrace)}\",");
                sb.Append($"\"timestamp\":\"{e.Timestamp:yyyy-MM-ddTHH:mm:ss}\"");
                sb.Append("}");
            }

            sb.Append("]}");
            RestResponse.Send(response, sb.ToString());
        }
    }
}

using System.Globalization;
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

            // A negative cursor disables the filter, which is what an absent `since` means.
            long since = -1;
            var sinceRaw = query["since"];
            if (!string.IsNullOrEmpty(sinceRaw))
            {
                if (!long.TryParse(sinceRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out since) ||
                    since < 0)
                {
                    RestResponse.SendError(
                        response, "Query parameter 'since' must be a non-negative integer.", 400);
                    return;
                }
            }

            var result = LogStore.Query(type, search, limit, since);
            var entries = result.Entries;

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"sessionId\":").Append(RestResponse.FormatNullableString(result.SessionId)).Append(",");
            sb.Append($"\"count\":{entries.Count},");
            sb.Append($"\"oldestSequence\":{result.OldestSequence.ToString(CultureInfo.InvariantCulture)},");
            sb.Append($"\"latestSequence\":{result.LatestSequence.ToString(CultureInfo.InvariantCulture)},");
            sb.Append($"\"truncated\":{RestResponse.FormatBool(result.Truncated)},");
            sb.Append($"\"hasMore\":{RestResponse.FormatBool(result.HasMore)},");
            sb.Append("\"logs\":[");

            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var e = entries[i];
                sb.Append("{");
                sb.Append($"\"sequence\":{e.Sequence.ToString(CultureInfo.InvariantCulture)},");
                sb.Append($"\"type\":\"{RestResponse.EscapeJson(e.Type)}\",");
                sb.Append($"\"message\":\"{RestResponse.EscapeJson(e.Message)}\",");
                sb.Append($"\"stackTrace\":\"{RestResponse.EscapeJson(e.StackTrace)}\",");
                sb.Append($"\"timestamp\":\"{e.Timestamp.ToString("o", CultureInfo.InvariantCulture)}\"");
                sb.Append("}");
            }

            sb.Append("]}");
            RestResponse.Send(response, sb.ToString());
        }

        /// <summary>
        /// Streams the raw NDJSON log file, which retains entries already evicted from memory.
        /// </summary>
        /// <param name="context">Request context whose response lifetime the transfer takes over.</param>
        public void HandleDownload(UnionAirRequestContext context)
        {
            var paths = LogStore.GetDownloadFilePaths();
            if (paths.Count == 0)
            {
                RestResponse.SendNotFound(context.Response, "Artifact is not available.");
                return;
            }

            RestResponse.SendArtifactFiles(
                context,
                paths,
                "application/x-ndjson",
                "console.ndjson");
        }
    }
}

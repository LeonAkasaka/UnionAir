using System.Globalization;
using System.Text;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class CompileHandler
    {
        /// <summary>
        /// Returns the in-flight and most recently completed Editor compilation in one response.
        /// </summary>
        /// <remarks>
        /// Both are returned together because a polling client needs them in the same snapshot:
        /// a cycle that has already finished moved from <c>current</c> to <c>latest</c> between
        /// two separate requests.
        /// </remarks>
        public void HandleCollection(UnionAirRequest request, UnionAirResponse response)
        {
            var current = CompileService.Current;
            var latest = CompileService.Latest;

            var sb = new StringBuilder();
            sb.Append("{\"current\":");
            sb.Append(current == null ? "null" : current.ToApiJson());
            sb.Append(",\"latest\":");
            sb.Append(latest == null ? "null" : latest.ToApiJson());
            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        /// <summary>
        /// Requests a script compilation and returns the record to poll.
        /// </summary>
        public void HandleStart(UnionAirRequest request, UnionAirResponse response)
        {
            // PlayModePolicy only covers isPlaying, and recompiling during the transition loses the
            // cycle. Asked of the coordinator rather than EditorApplication so the rejection names
            // the same activity every other endpoint would name.
            if (UnionAirActivityCoordinator.IsActive(UnionAirActivity.PlayMode))
            {
                RestResponse.Send(
                    response,
                    UnionAirActivityDecision.RejectionJson(
                        UnionAirActivityCoordinator.Blocking(UnionAirActivity.PlayMode),
                        "Compilation cannot be requested while the Unity Editor is entering or in Play mode."),
                    409);
                return;
            }

            var body = RequestBodyReader.ReadString(request);
            var refresh = RequestBodyReader.GetBool(body, "refresh") ?? true;
            var clean = RequestBodyReader.GetBool(body, "clean") ?? false;
            var requestId = RequestBodyReader.GetString(body, "requestId");

            if (!string.IsNullOrEmpty(requestId) && !CompileMessageParser.IsValidId(requestId))
            {
                RestResponse.SendError(
                    response,
                    "Body field 'requestId' must contain only letters, digits, hyphens, and underscores, be at most 64 characters, and not be a reserved Windows device name.",
                    400);
                return;
            }

            // A replayed requestId means the caller lost the response, not that it wants a
            // second cycle; point it back at the record it already owns.
            if (!string.IsNullOrEmpty(requestId))
            {
                var existing = CompileService.Find(requestId);
                if (existing != null)
                {
                    RestResponse.Send(response, ExistingRequestJson(existing), 409);
                    return;
                }
            }

            if (CompileService.IsBusy)
            {
                RestResponse.Send(response, ActiveCompileJson(), 409);
                return;
            }

            if (refresh && LoadedSceneDiskChangeGuard.SendConflictIfAny(response))
                return;

            var id = string.IsNullOrEmpty(requestId) ? CompileService.NewId() : requestId;
            var record = CompileService.NewRecord(UnionAirCompileGate.UnionAirSource, id);

            // Nothing is started unless the record is durable. The response below promises an id
            // to poll, and a compilation ends in a domain reload that discards the in-memory copy,
            // so a record that never reached disk would leave that promise unkeepable.
            if (!CompileService.ScheduleStart(record, refresh, clean))
            {
                RestResponse.SendError(
                    response,
                    "The compile record could not be written to Library/UnionAir/Compile, so no compilation was started. " +
                    "The Unity Console carries the underlying file error. Retry once the cause is cleared.",
                    500);
                return;
            }

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"id\":\"{RestResponse.EscapeJson(record.id)}\",");
            sb.Append($"\"state\":\"{RestResponse.EscapeJson(record.state)}\",");
            sb.Append($"\"source\":\"{RestResponse.EscapeJson(record.source)}\",");
            sb.Append("\"sessionId\":").Append(RestResponse.FormatNullableString(record.sessionId)).Append(",");
            sb.Append($"\"lifecycleGenerationAtRequest\":{record.lifecycleGenerationAtRequest.ToString(CultureInfo.InvariantCulture)},");
            sb.Append($"\"statusUrl\":\"/api/compile/{RestResponse.EscapeJson(record.id)}\"");
            sb.Append("}");
            RestResponse.Send(response, sb.ToString(), 202);
        }

        private static string ActiveCompileJson()
        {
            var current = CompileService.Current;
            var id = RestResponse.FormatNullableString(
                UnionAirCompileGate.PublicId ?? (current != null && current.IsActive ? current.id : null));
            var source = RestResponse.FormatNullableString(UnionAirCompileGate.PublicSource);
            var state = RestResponse.FormatNullableString(
                current != null && current.IsActive ? current.state : null);

            var sb = new StringBuilder();
            sb.Append("{\"error\":\"A script compilation is already active.\",\"activeActivity\":");
            UnionAirActivityDecision.AppendActivity(
                sb, UnionAirActivityCoordinator.Blocking(UnionAirActivity.Compile));
            sb.Append($",\"activeCompile\":{{\"id\":{id},\"source\":{source},\"state\":{state}}}}}");
            return sb.ToString();
        }

        private static string ExistingRequestJson(CompileRecord record)
        {
            return "{\"error\":\"A compilation was already requested with this requestId.\"," +
                   $"\"existingCompile\":{record.ToApiJson()}}}";
        }

        /// <summary>Lists retained terminal records as bounded summaries.</summary>
        public void HandleRecords(UnionAirRequest request, UnionAirResponse response)
        {
            CompileRecordQuery query;
            string error;
            if (!CompileDecision.TryCreateRecordQuery(request.QueryString, out query, out error))
            {
                RestResponse.SendError(response, error, 400);
                return;
            }

            bool scanCompleted;
            var records = CompileService.ListRetained(out scanCompleted);
            if (!scanCompleted)
            {
                RestResponse.SendError(
                    response, "Retained compile records could not be enumerated.", 500);
                return;
            }

            var page = CompileDecision.QueryRetained(records, query, out var total);
            var hasMore = (long)query.offset + page.Count < total;

            var sb = new StringBuilder();
            sb.Append("{\"total\":").Append(total.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"offset\":").Append(query.offset.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"limit\":").Append(query.limit.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"hasMore\":").Append(RestResponse.FormatBool(hasMore));
            sb.Append(",\"records\":[");
            for (var i = 0; i < page.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(page[i].ToSummaryJson());
            }
            sb.Append("]}");
            RestResponse.Send(response, sb.ToString());
        }

        /// <summary>
        /// Returns a single retained compilation record by id.
        /// </summary>
        public void HandleById(UnionAirRequestContext context)
        {
            var id = context.RouteValues != null && context.RouteValues.ContainsKey("id")
                ? context.RouteValues["id"]
                : null;

            if (!CompileMessageParser.IsValidId(id))
            {
                RestResponse.SendError(
                    context.Response,
                    "Compile id must contain only letters, digits, hyphens, and underscores and must not be a reserved Windows device name.",
                    400);
                return;
            }

            var record = CompileService.Find(id);
            if (record == null)
            {
                RestResponse.SendNotFound(
                    context.Response,
                    $"Compile record '{id}' was not found. UnionAir retains the most recent " +
                    $"{CompileService.RetainedRecordCount} records for the project.");
                return;
            }

            RestResponse.Send(context.Response, record.ToApiJson());
        }
    }
}

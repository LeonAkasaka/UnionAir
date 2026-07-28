using System.Globalization;
using System.Net;
using System.Text;
using UnityEditor;

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
        public void HandleCollection(HttpListenerRequest request, HttpListenerResponse response)
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
        public void HandleStart(HttpListenerRequest request, HttpListenerResponse response)
        {
            // PlayModePolicy only covers isPlaying, and recompiling during either transition or an
            // asset import loses the cycle.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                RestResponse.SendError(
                    response, "Compilation cannot be requested while the Unity Editor is entering or in Play mode.", 409);
                return;
            }

            if (EditorApplication.isUpdating)
            {
                RestResponse.SendError(
                    response, "Compilation cannot be requested while the Unity Editor is updating assets.", 409);
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
                    "Body field 'requestId' must contain only letters, digits, hyphens, and underscores, and be at most 64 characters.",
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

            var id = string.IsNullOrEmpty(requestId) ? CompileService.NewId() : requestId;
            var record = CompileService.NewRecord(UnionAirCompileGate.UnionAirSource, id);
            CompileService.ScheduleStart(record, refresh, clean);

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

            return "{\"error\":\"A script compilation is already active.\"," +
                   $"\"activeCompile\":{{\"id\":{id},\"source\":{source},\"state\":{state}}}}}";
        }

        private static string ExistingRequestJson(CompileRecord record)
        {
            return "{\"error\":\"A compilation was already requested with this requestId.\"," +
                   $"\"existingCompile\":{record.ToApiJson()}}}";
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
                    "Compile id must contain only letters, digits, hyphens, and underscores.",
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

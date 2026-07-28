using System.Net;
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

using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class BuildTargetHandler
    {
        /// <summary>
        /// Requests a switch of the active build target.
        /// </summary>
        public void HandleSwitch(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            var requested = RequestBodyReader.GetString(body, "buildTarget");
            var requestId = RequestBodyReader.GetString(body, "requestId");

            if (string.IsNullOrEmpty(requested))
            {
                RestResponse.SendError(
                    response,
                    "Body field 'buildTarget' is required. Use a BuildTarget name reported by GET /api/build/targets.",
                    400);
                return;
            }

            if (!string.IsNullOrEmpty(requestId) && !CompileMessageParser.IsValidId(requestId))
            {
                RestResponse.SendError(
                    response,
                    "Body field 'requestId' must contain only letters, digits, hyphens, and underscores, be at most 64 characters, and not be a reserved Windows device name.",
                    400);
                return;
            }

            if (!string.IsNullOrEmpty(requestId))
            {
                var existing = BuildTargetSwitchService.Find(requestId);
                if (existing != null)
                {
                    RestResponse.Send(
                        response,
                        "{\"error\":\"A build target switch was already requested with this requestId.\"," +
                        $"\"existingSwitch\":{existing.ToApiJson()}}}",
                        409);
                    return;
                }
            }

            BuildTargetCatalog.Entry entry;
            if (!TryResolve(requested, out entry))
            {
                RestResponse.SendError(
                    response,
                    "Unknown buildTarget '" + requested + "'. Use a BuildTarget name reported by GET /api/build/targets.",
                    400);
                return;
            }

            // Reported as its own condition rather than as a generic failure: a missing module is
            // fixed by installing it from the Unity Hub, and nothing about a switch failure message
            // would tell a caller that.
            if (!entry.Installed)
            {
                RestResponse.Send(response, ModuleMissingJson(entry), 409);
                return;
            }

            if (BuildTargetSwitchService.IsBusy)
            {
                RestResponse.Send(response, ActiveSwitchJson(), 409);
                return;
            }

            if (EditorUserBuildSettings.activeBuildTarget == entry.Target)
            {
                RestResponse.Send(response, UnchangedJson(entry), 200);
                return;
            }

            var id = string.IsNullOrEmpty(requestId) ? BuildTargetSwitchService.NewId() : requestId;
            var record = BuildTargetSwitchService.NewRecord(
                id, entry.Target, entry.Group, entry.NamedBuildTarget);

            // Nothing is started unless the record is durable. The switch ends in a domain reload
            // and the record is the only thing that survives it to report the outcome.
            if (!BuildTargetSwitchService.ScheduleSwitch(record, entry.Group, entry.Target))
            {
                RestResponse.SendError(
                    response,
                    "The build target switch record could not be written to Library/UnionAir/BuildTargetSwitches, " +
                    "so no switch was started. The Unity Console carries the underlying file error. " +
                    "Retry once the cause is cleared.",
                    500);
                return;
            }

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"id\":\"{RestResponse.EscapeJson(record.id)}\",");
            sb.Append($"\"state\":\"{RestResponse.EscapeJson(record.state)}\",");
            sb.Append($"\"requestedTarget\":\"{RestResponse.EscapeJson(record.requestedTarget)}\",");
            sb.Append($"\"previousTarget\":\"{RestResponse.EscapeJson(record.previousTarget)}\",");
            sb.Append("\"sessionId\":").Append(RestResponse.FormatNullableString(record.sessionId)).Append(",");
            sb.Append($"\"lifecycleGenerationAtRequest\":{record.lifecycleGenerationAtRequest.ToString(CultureInfo.InvariantCulture)},");
            sb.Append($"\"statusUrl\":\"/api/build/target/{RestResponse.EscapeJson(record.id)}\",");
            sb.Append("\"note\":\"Switching reimports every asset for the new platform, recompiles, and ends in a domain reload. This can take minutes on a large project, and UnionAir answers nothing for most of it. Poll GET /api/build/target/{id} through the dropped connection until state leaves queued and switching.\"");
            sb.Append("}");
            RestResponse.Send(response, sb.ToString(), 202);
        }

        /// <summary>
        /// Returns the active target, the in-flight switch, and the retained switch records.
        /// </summary>
        public void HandleCollection(HttpListenerRequest request, HttpListenerResponse response)
        {
            bool scanCompleted;
            var records = BuildTargetSwitchService.ListRetained(out scanCompleted);
            if (!scanCompleted)
            {
                RestResponse.SendError(
                    response, "Retained build target switch records could not be enumerated.", 500);
                return;
            }

            var current = BuildTargetSwitchService.Current;
            var target = EditorUserBuildSettings.activeBuildTarget;

            var sb = new StringBuilder();
            sb.Append("{\"activeBuildTarget\":\"").Append(RestResponse.EscapeJson(target.ToString())).Append("\"");
            sb.Append(",\"activeBuildTargetGroup\":\"")
              .Append(RestResponse.EscapeJson(BuildTargetCatalog.GroupOf(target).ToString())).Append("\"");
            sb.Append(",\"current\":").Append(current == null || !current.IsActive ? "null" : current.ToApiJson());
            sb.Append(",\"total\":").Append(records.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"records\":[");
            for (var i = 0; i < records.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(records[i].ToApiJson());
            }
            sb.Append("]}");
            RestResponse.Send(response, sb.ToString());
        }

        /// <summary>Returns one retained build target switch record.</summary>
        public void HandleById(UnionAirRequestContext context)
        {
            var id = context.RouteValues != null && context.RouteValues.ContainsKey("id")
                ? context.RouteValues["id"]
                : null;

            if (!CompileMessageParser.IsValidId(id))
            {
                RestResponse.SendError(
                    context.Response,
                    "Build target switch id must contain only letters, digits, hyphens, and underscores and must not be a reserved Windows device name.",
                    400);
                return;
            }

            var record = BuildTargetSwitchService.Find(id);
            if (record == null)
            {
                RestResponse.SendNotFound(
                    context.Response,
                    $"Build target switch '{id}' was not found. UnionAir retains the most recent " +
                    $"{BuildTargetSwitchService.RetainedRecordCount} records for the project.");
                return;
            }

            RestResponse.Send(context.Response, record.ToApiJson());
        }

        private static bool TryResolve(string name, out BuildTargetCatalog.Entry entry)
        {
            foreach (var candidate in BuildTargetCatalog.List())
            {
                if (!string.Equals(candidate.Target.ToString(), name, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                entry = candidate;
                return true;
            }

            entry = default(BuildTargetCatalog.Entry);
            return false;
        }

        private static string ModuleMissingJson(BuildTargetCatalog.Entry entry)
        {
            var installed = new List<string>();
            foreach (var candidate in BuildTargetCatalog.List())
                if (candidate.Installed) installed.Add(candidate.Target.ToString());

            var sb = new StringBuilder();
            sb.Append("{\"error\":\"");
            sb.Append(RestResponse.EscapeJson(
                "The platform module for '" + entry.Target + "' is not installed in this Unity Editor. " +
                "Install it through the Unity Hub for this Editor version, then retry."));
            sb.Append("\",\"code\":\"platform_module_not_installed\"");
            sb.Append(",\"buildTarget\":\"").Append(RestResponse.EscapeJson(entry.Target.ToString())).Append("\"");
            sb.Append(",\"installedTargets\":[");
            for (var i = 0; i < installed.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\"").Append(RestResponse.EscapeJson(installed[i])).Append("\"");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string ActiveSwitchJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"error\":\"A build target switch is already active.\",\"activeActivity\":");
            UnionAirActivityDecision.AppendActivity(
                sb, UnionAirActivityCoordinator.Blocking(UnionAirActivity.BuildTargetSwitch));
            sb.Append(",\"activeSwitch\":{\"id\":")
              .Append(RestResponse.FormatNullableString(
                  UnionAirActivityCoordinator.PublicIdOf(UnionAirActivity.BuildTargetSwitch)));
            sb.Append(",\"state\":")
              .Append(RestResponse.FormatNullableString(BuildTargetSwitchService.ActiveState));
            sb.Append("}}");
            return sb.ToString();
        }

        private static string UnchangedJson(BuildTargetCatalog.Entry entry)
        {
            var sb = new StringBuilder();
            sb.Append("{\"state\":\"unchanged\",\"activeBuildTarget\":\"")
              .Append(RestResponse.EscapeJson(entry.Target.ToString())).Append("\"");
            sb.Append(",\"note\":\"");
            sb.Append(RestResponse.EscapeJson(
                "The requested target is already active. Nothing was reimported and no record was created."));
            sb.Append("\"}");
            return sb.ToString();
        }
    }
}

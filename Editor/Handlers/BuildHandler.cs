using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class BuildHandler
    {
        /// <summary>
        /// Requests a player build and returns the record to poll.
        /// </summary>
        public void HandleStart(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            var requestId = RequestBodyReader.GetString(body, "requestId");

            if (!string.IsNullOrEmpty(requestId) && !CompileMessageParser.IsValidId(requestId))
            {
                RestResponse.SendError(
                    response,
                    "Body field 'requestId' must contain only letters, digits, hyphens, and underscores, be at most 64 characters, and not be a reserved Windows device name.",
                    400);
                return;
            }

            // Checked before anything else that could reject: a replayed requestId means the caller
            // lost the 202, not that it wants a second build. A build costs roughly 72 seconds and
            // 95 MB, so answering with the record it already owns matters more here than for a
            // compilation.
            if (!string.IsNullOrEmpty(requestId))
            {
                var existing = BuildService.Find(requestId);
                if (existing != null)
                {
                    RestResponse.Send(
                        response,
                        "{\"error\":\"A build was already requested with this requestId.\"," +
                        $"\"existingBuild\":{existing.ToApiJson(BuildArtifactStore.Exists(existing.id))}}}",
                        409);
                    return;
                }
            }

            if (BuildService.IsBusy)
            {
                RestResponse.Send(response, ActiveBuildJson(), 409);
                return;
            }

            if (!BuildTargetCatalog.IsActiveTargetInstalled())
            {
                RestResponse.SendError(
                    response,
                    "The platform module for the active build target '" +
                    EditorUserBuildSettings.activeBuildTarget +
                    "' is not installed in this Unity Editor. Install it, or switch to a target reported as installed by GET /api/build/targets.",
                    409);
                return;
            }

            var scenes = EnabledScenePaths();
            if (scenes.Count == 0)
            {
                RestResponse.SendError(
                    response,
                    "No enabled scenes are configured in Build Settings. A player build needs at least one; see GET /api/build/settings.",
                    400);
                return;
            }

            if (UnsavedSceneGuard.SendConflictIfAny(response))
                return;

            BuildRequestOptions options;
            string error;
            if (!BuildRequestParser.TryParse(body, ProjectDefaults(), out options, out error))
            {
                RestResponse.SendError(response, error, 400);
                return;
            }

            var id = string.IsNullOrEmpty(requestId) ? BuildService.NewId() : requestId;
            var record = BuildService.NewRecord(id, options, scenes);
            BuildService.ScheduleStart(record);

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"id\":\"{RestResponse.EscapeJson(record.id)}\",");
            sb.Append($"\"state\":\"{RestResponse.EscapeJson(record.state)}\",");
            sb.Append($"\"buildTarget\":\"{RestResponse.EscapeJson(record.buildTarget)}\",");
            sb.Append("\"sessionId\":").Append(RestResponse.FormatNullableString(record.sessionId)).Append(",");
            sb.Append($"\"lifecycleGenerationAtRequest\":{record.lifecycleGenerationAtRequest.ToString(CultureInfo.InvariantCulture)},");
            sb.Append($"\"statusUrl\":\"/api/builds/{RestResponse.EscapeJson(record.id)}\",");
            sb.Append("\"note\":\"The build occupies the Unity main thread. UnionAir answers no request, including this status URL, until it finishes; a Windows player build was measured at roughly 72 seconds. Set client timeouts accordingly and treat a refused connection as expected.\"");
            sb.Append("}");
            RestResponse.Send(response, sb.ToString(), 202);
        }

        /// <summary>
        /// Returns the current build, the retained records, and how much disk they occupy.
        /// </summary>
        public void HandleCollection(HttpListenerRequest request, HttpListenerResponse response)
        {
            bool scanCompleted;
            var records = BuildService.ListRetained(out scanCompleted);
            if (!scanCompleted)
            {
                RestResponse.SendError(response, "Retained build records could not be enumerated.", 500);
                return;
            }

            var current = BuildService.Current;

            var sb = new StringBuilder();
            sb.Append("{\"current\":");
            sb.Append(current == null
                ? "null"
                : current.ToApiJson(BuildArtifactStore.Exists(current.id)));
            sb.Append(",\"total\":").Append(records.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"storage\":{");
            sb.Append("\"root\":\"").Append(RestResponse.EscapeJson(
                BuildArtifactStore.NormalizePath(BuildArtifactStore.Root))).Append("\"");
            sb.Append(",\"totalBytes\":")
              .Append(BuildArtifactStore.TotalBytes().ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"artifactCount\":")
              .Append(BuildArtifactStore.ArtifactCount().ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"maxArtifactCount\":")
              .Append(BuildArtifactStore.RetainedArtifacts.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"maxTotalBytes\":")
              .Append(BuildArtifactStore.MaxTotalBytes.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"retainedRecords\":")
              .Append(BuildService.RetainedRecordCount.ToString(CultureInfo.InvariantCulture));
            sb.Append("},\"records\":[");
            for (var i = 0; i < records.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(records[i].ToSummaryJson(BuildArtifactStore.Exists(records[i].id)));
            }
            sb.Append("]}");
            RestResponse.Send(response, sb.ToString());
        }

        /// <summary>Returns one retained build record.</summary>
        public void HandleById(UnionAirRequestContext context)
        {
            string id;
            if (!TryGetId(context, out id))
                return;

            var record = BuildService.Find(id);
            if (record == null)
            {
                RestResponse.SendNotFound(
                    context.Response,
                    $"Build record '{id}' was not found. UnionAir retains the most recent " +
                    $"{BuildService.RetainedRecordCount} records for the project.");
                return;
            }

            RestResponse.Send(context.Response, record.ToApiJson(BuildArtifactStore.Exists(record.id)));
        }

        /// <summary>Deletes one build record and reclaims its artifacts.</summary>
        public void HandleDelete(UnionAirRequestContext context)
        {
            string id;
            if (!TryGetId(context, out id))
                return;

            var bytes = BuildArtifactStore.DirectoryBytes(id);
            if (!BuildService.Delete(id))
            {
                RestResponse.SendNotFound(context.Response, $"Build record '{id}' was not found.");
                return;
            }

            var remaining = BuildArtifactStore.Exists(id);
            var sb = new StringBuilder();
            sb.Append("{\"deleted\":\"").Append(RestResponse.EscapeJson(id)).Append("\"");
            sb.Append(",\"reclaimedBytes\":")
              .Append((remaining ? 0 : bytes).ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"outputAvailable\":").Append(RestResponse.FormatBool(remaining));
            sb.Append(",\"totalBytes\":")
              .Append(BuildArtifactStore.TotalBytes().ToString(CultureInfo.InvariantCulture));
            sb.Append("}");
            RestResponse.Send(context.Response, sb.ToString());
        }

        private static bool TryGetId(UnionAirRequestContext context, out string id)
        {
            id = context.RouteValues != null && context.RouteValues.ContainsKey("id")
                ? context.RouteValues["id"]
                : null;

            if (CompileMessageParser.IsValidId(id))
                return true;

            RestResponse.SendError(
                context.Response,
                "Build id must contain only letters, digits, hyphens, and underscores and must not be a reserved Windows device name.",
                400);
            return false;
        }

        private static string ActiveBuildJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"error\":\"A player build is already active.\",\"activeActivity\":");
            UnionAirActivityDecision.AppendActivity(
                sb, UnionAirActivityCoordinator.Blocking(UnionAirActivity.Build));
            sb.Append(",\"activeBuild\":{\"id\":")
              .Append(RestResponse.FormatNullableString(
                  UnionAirActivityCoordinator.PublicIdOf(UnionAirActivity.Build)));
            sb.Append(",\"state\":")
              .Append(RestResponse.FormatNullableString(BuildService.ActiveState));
            sb.Append("}}");
            return sb.ToString();
        }

        /// <summary>
        /// Enabled build scenes, in the order Unity assigns build indices.
        /// </summary>
        private static List<string> EnabledScenePaths()
        {
            var paths = new List<string>();
            var scenes = EditorBuildSettings.scenes;
            if (scenes == null) return paths;

            foreach (var scene in scenes)
            {
                if (scene != null && scene.enabled && !string.IsNullOrEmpty(scene.path))
                    paths.Add(scene.path);
            }
            return paths;
        }

        /// <summary>
        /// Build flags currently selected in the project, used when the request omits them.
        /// </summary>
        private static BuildRequestOptions ProjectDefaults()
            => new BuildRequestOptions
            {
                development = EditorUserBuildSettings.development,
                allowDebugging = EditorUserBuildSettings.development && EditorUserBuildSettings.allowDebugging,
                connectProfiler = EditorUserBuildSettings.development && EditorUserBuildSettings.connectProfiler,
                deepProfiling = EditorUserBuildSettings.development && EditorUserBuildSettings.buildWithDeepProfilingSupport,
                waitForPlayerConnection = false,
                clean = false,
                strictMode = false,
            };
    }
}

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Build;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class BuildSettingsHandler
    {
        /// <summary>
        /// Returns the build configuration for one named build target.
        /// </summary>
        public void HandleSettings(UnionAirRequest request, UnionAirResponse response)
        {
            var requested = request.QueryString["namedBuildTarget"] ?? "";
            if (!BuildTargetCatalog.TryResolveNamedBuildTarget(requested, out var namedBuildTarget, out var error))
            {
                RestResponse.SendError(response, error, 400);
                return;
            }

            RestResponse.Send(response, BuildSettingsReader.SettingsJson(namedBuildTarget));
        }

        /// <summary>
        /// Returns the build targets this Editor knows about and whether each module is installed.
        /// </summary>
        public void HandleTargets(UnionAirRequest request, UnionAirResponse response)
        {
            var installedRaw = request.QueryString["installed"];
            if (installedRaw != null &&
                installedRaw != "true" && installedRaw != "false" &&
                installedRaw != "1" && installedRaw != "0")
            {
                RestResponse.SendError(
                    response, "Query parameter 'installed' must be true or false.", 400);
                return;
            }

            var installedOnly = installedRaw == "true" || installedRaw == "1";
            RestResponse.Send(response, BuildSettingsReader.TargetsJson(installedOnly));
        }

        /// <summary>
        /// Changes scripting settings and build flags for one named build target.
        /// </summary>
        public void HandlePatchSettings(UnionAirRequest request, UnionAirResponse response)
        {
            var body = RequestBodyReader.ReadString(request);

            BuildSettingsWritePlan plan;
            string error;
            if (!BuildSettingsWriteParser.TryParseSettings(body, out plan, out error))
            {
                RestResponse.SendError(response, error, 400);
                return;
            }

            NamedBuildTarget namedBuildTarget;
            if (!BuildTargetCatalog.TryResolveNamedBuildTarget(
                    plan.NamedBuildTargetName, out namedBuildTarget, out error))
            {
                RestResponse.SendError(response, error, 400);
                return;
            }

            var results = BuildSettingsWriter.Apply(plan, namedBuildTarget);

            // Written before the response rather than deferred, because the response has to report
            // what actually happened to each change. Unity queues the recompile a define or backend
            // change requests instead of running it inline, so the reload cannot begin before this
            // returns; the caller then observes the cycle through the Compile API.
            SendWriteResult(
                response,
                results,
                plan.TriggersCompilation && BuildSettingsWriter.AnyApplied(results),
                namedBuildTarget);
        }

        /// <summary>
        /// Replaces the build scene list.
        /// </summary>
        public void HandleSetScenes(UnionAirRequest request, UnionAirResponse response)
        {
            var body = RequestBodyReader.ReadString(request);

            List<BuildSceneEntry> scenes;
            string error;
            if (!BuildSettingsWriteParser.TryParseScenes(body, out scenes, out error))
            {
                RestResponse.SendError(response, error, 400);
                return;
            }

            BuildSettingsChangeResult result;
            if (!BuildSettingsWriter.TryApplyScenes(scenes, out result, out error))
            {
                RestResponse.SendError(response, error, 400);
                return;
            }

            SendWriteResult(
                response,
                new List<BuildSettingsChangeResult> { result },
                false,
                BuildTargetCatalog.Active());
        }

        /// <summary>
        /// Writes the shared write-result body: what happened per setting, and the settings that
        /// resulted.
        /// </summary>
        /// <remarks>
        /// The resulting snapshot is included rather than left to a follow-up request, because a
        /// partially applied change is exactly when a caller most needs to know the real state and
        /// least wants to guess it from the outcomes.
        /// </remarks>
        private static void SendWriteResult(
            UnionAirResponse response,
            List<BuildSettingsChangeResult> results,
            bool compilationExpected,
            NamedBuildTarget namedBuildTarget)
        {
            var failed = BuildSettingsWriter.AnyFailed(results);

            var sb = new StringBuilder();
            sb.Append("{\"changes\":");
            BuildSettingsWriter.AppendResults(sb, results);
            sb.Append(",\"persistent\":true");
            sb.Append(",\"compilationExpected\":").Append(RestResponse.FormatBool(compilationExpected));
            sb.Append(",\"lifecycleGeneration\":").Append(UnionAirSession.Generation);
            sb.Append(",\"note\":\"");
            sb.Append(RestResponse.EscapeJson(
                "Changes are permanent. 'project' persistence writes a shared project file that appears as a Git diff; " +
                "'user' persistence writes a local per-user file. Failed changes are not rolled back; 'settings' is the resulting state. " +
                (compilationExpected
                    ? "A script compilation was triggered: poll GET /api/compile and expect a domain reload."
                    : "No script compilation was triggered by this request.")));
            sb.Append("\",\"settings\":");
            sb.Append(BuildSettingsReader.SettingsJson(namedBuildTarget));
            sb.Append("}");

            RestResponse.Send(response, sb.ToString(), failed ? 207 : 200);
        }
    }
}

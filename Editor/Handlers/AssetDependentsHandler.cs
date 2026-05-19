using System.Net;
using System.Text;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// GET /api/assets/dependents?guid={assetGuid}
    /// Returns all assets in the project that have the specified asset as a dependency
    /// (reverse dependency lookup).
    /// </summary>
    internal class AssetDependentsHandler : IRequestHandler
    {
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "GET" && request.Url.AbsolutePath == "/api/assets/dependents";

        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var guid = request.QueryString["guid"] ?? "";
            if (string.IsNullOrEmpty(guid))
            {
                RestResponse.SendError(response, "Missing required query parameter: guid", 400);
                return;
            }

            var targetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(targetPath))
            {
                RestResponse.SendNotFound(response, $"No asset found for GUID: {guid}");
                return;
            }

            var targetTypeName = AssetDatabase.GetMainAssetTypeAtPath(targetPath)?.Name ?? "Unknown";

            // Iterate all assets and check if target appears in their direct dependencies
            var allGuids   = AssetDatabase.FindAssets("", new[] { "Assets" });
            var sb         = new StringBuilder();
            int depCount   = 0;

            sb.Append("{\"asset\":{");
            sb.Append($"\"guid\":\"{RestResponse.EscapeJson(guid)}\",");
            sb.Append($"\"path\":\"{RestResponse.EscapeJson(targetPath)}\",");
            sb.Append($"\"type\":\"{RestResponse.EscapeJson(targetTypeName)}\"");
            sb.Append("},\"dependents\":[");

            foreach (var candidateGuid in allGuids)
            {
                if (candidateGuid == guid) continue;
                var candidatePath = AssetDatabase.GUIDToAssetPath(candidateGuid);
                if (string.IsNullOrEmpty(candidatePath)) continue;

                var deps = AssetDatabase.GetDependencies(candidatePath, false);
                bool found = false;
                foreach (var dep in deps)
                {
                    if (dep == targetPath) { found = true; break; }
                }
                if (!found) continue;

                var typeName = AssetDatabase.GetMainAssetTypeAtPath(candidatePath)?.Name ?? "Unknown";

                if (depCount > 0) sb.Append(",");
                sb.Append("{");
                sb.Append($"\"guid\":\"{RestResponse.EscapeJson(candidateGuid)}\",");
                sb.Append($"\"path\":\"{RestResponse.EscapeJson(candidatePath)}\",");
                sb.Append($"\"type\":\"{RestResponse.EscapeJson(typeName)}\"");
                sb.Append("}");
                depCount++;
            }

            sb.Append($"],\"count\":{depCount}}}");
            RestResponse.Send(response, sb.ToString());
        }
    }
}

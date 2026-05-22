using System.Net;
using System.Text;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class AssetHandler : IRequestHandler
    {
        private const int MaxResults = 500;

        /// <summary>
        /// Determines whether this handler can process the request.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <returns>True when this handler supports the request.</returns>
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "GET" &&
               (request.Url.AbsolutePath == "/api/assets" ||
                request.Url.AbsolutePath.StartsWith("/api/assets/"));

        /// <summary>
        /// Processes the request and writes the HTTP response.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <param name="response">HTTP response to write.</param>
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var absPath = request.Url.AbsolutePath;

            if (absPath == "/api/assets")
                HandleList(request, response);
            else
            {
                var guid = absPath.Substring("/api/assets/".Length);
                HandleDetail(guid, response);
            }
        }

        private static void HandleList(HttpListenerRequest request, HttpListenerResponse response)
        {
            var filterPath   = request.QueryString["path"]   ?? "";
            var filterType   = request.QueryString["type"]   ?? "";
            var searchQuery  = request.QueryString["search"] ?? "";

            var filter = "";
            if (!string.IsNullOrEmpty(filterType))  filter += $"t:{filterType} ";
            if (!string.IsNullOrEmpty(searchQuery)) filter += searchQuery;
            filter = filter.Trim();

            var searchIn = !string.IsNullOrEmpty(filterPath)
                ? new[] { filterPath }
                : new[] { "Assets" };

            string[] guids;
            try   { guids = AssetDatabase.FindAssets(filter, searchIn); }
            catch { guids = new string[0]; }

            var sb = new StringBuilder();
            sb.Append("{\"assets\":[");

            int returned = 0;
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath)) continue;

                var typeName = AssetDatabase.GetMainAssetTypeAtPath(assetPath)?.Name ?? "Unknown";

                if (returned > 0) sb.Append(",");
                sb.Append("{");
                sb.Append($"\"guid\":\"{RestResponse.EscapeJson(guid)}\",");
                sb.Append($"\"path\":\"{RestResponse.EscapeJson(assetPath)}\",");
                sb.Append($"\"type\":\"{RestResponse.EscapeJson(typeName)}\"");
                sb.Append("}");

                returned++;
                if (returned >= MaxResults) break;
            }

            sb.Append($"],\"total\":{guids.Length},\"returned\":{returned}}}");
            RestResponse.Send(response, sb.ToString());
        }

        private static void HandleDetail(string guid, HttpListenerResponse response)
        {
            if (string.IsNullOrEmpty(guid))
            {
                RestResponse.SendError(response, "Missing GUID", 400);
                return;
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendNotFound(response, $"No asset found for GUID: {guid}");
                return;
            }

            var typeName = AssetDatabase.GetMainAssetTypeAtPath(assetPath)?.FullName ?? "Unknown";
            var deps     = AssetDatabase.GetDependencies(assetPath, false);
            var labels   = AssetDatabase.GetLabels(new UnityEngine.GUID(guid));

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"guid\":\"{RestResponse.EscapeJson(guid)}\",");
            sb.Append($"\"path\":\"{RestResponse.EscapeJson(assetPath)}\",");
            sb.Append($"\"type\":\"{RestResponse.EscapeJson(typeName)}\",");

            sb.Append("\"dependencies\":[");
            for (int i = 0; i < deps.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{RestResponse.EscapeJson(deps[i])}\"");
            }
            sb.Append("],\"labels\":[");
            for (int i = 0; i < labels.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{RestResponse.EscapeJson(labels[i])}\"");
            }
            sb.Append("]}");

            RestResponse.Send(response, sb.ToString());
        }
    }
}

using System.Text;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class AssetHandler
    {
        private const int MaxResults = 500;

        public void Handle(UnionAirRequest request, UnionAirResponse response)
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

        private static void HandleList(UnionAirRequest request, UnionAirResponse response)
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

        // Where a client reads the localIdentifier an object reference needs in order to name one
        // object inside a file. Omitted entirely for a path holding only its main asset, which is
        // most of them, so the field appearing at all is the signal that the path cannot be
        // addressed by name alone.
        private static void AppendSubAssets(StringBuilder sb, string assetPath)
        {
            var representations = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath);
            if (representations == null || representations.Length == 0) return;

            sb.Append(",\"subAssets\":[");
            var first = true;
            foreach (var representation in representations)
            {
                if (representation == null) continue;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        representation, out _, out long localId)) continue;

                if (!first) sb.Append(",");
                first = false;

                var id = localId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                sb.Append($"{{\"localIdentifier\":\"{id}\",");
                sb.Append($"\"name\":\"{RestResponse.EscapeJson(representation.name)}\",");
                sb.Append($"\"type\":\"{RestResponse.EscapeJson(representation.GetType().FullName)}\"}}");
            }
            sb.Append("]");
        }

        private static void HandleDetail(string guid, UnionAirResponse response)
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
            var asset    = AssetDatabase.LoadMainAssetAtPath(assetPath);
            var labels   = asset != null ? AssetDatabase.GetLabels(asset) : new string[0];

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
            sb.Append("]");

            AppendSubAssets(sb, assetPath);
            sb.Append("}");

            RestResponse.Send(response, sb.ToString());
        }
    }
}

using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles read operations for ScriptableObject assets:
    ///   GET /api/assets/scriptableobjects           — list assets (filterable by type, path, search)
    ///   GET /api/assets/scriptableobjects/{guid}    — detail with all readable serialized properties
    /// </summary>
    internal class ScriptableObjectReadHandler
    {
        private const int MaxResults = 500;

        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var absPath = request.Url.AbsolutePath;
            if (absPath == "/api/assets/scriptableobjects")
                HandleList(request, response);
            else
            {
                var guid = absPath.Substring("/api/assets/scriptableobjects/".Length);
                HandleDetail(guid, response);
            }
        }

        // ── GET /api/assets/scriptableobjects ────────────────────────────────

        private static void HandleList(HttpListenerRequest request, HttpListenerResponse response)
        {
            var filterType  = request.QueryString["type"]   ?? "";
            var filterPath  = request.QueryString["path"]   ?? "";
            var searchQuery = request.QueryString["search"] ?? "";

            // Build AssetDatabase filter string.
            // When no explicit type is given, default to ScriptableObject to exclude
            // Materials, Textures, and other non-ScriptableObject assets.
            var typeFilter = string.IsNullOrEmpty(filterType) ? "t:ScriptableObject" : $"t:{filterType}";
            var filter = string.IsNullOrEmpty(searchQuery)
                ? typeFilter
                : $"{typeFilter} {searchQuery}";

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

                var typeName = AssetDatabase.GetMainAssetTypeAtPath(assetPath)?.FullName ?? "Unknown";

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

        // ── GET /api/assets/scriptableobjects/{guid} ─────────────────────────

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

            // Load as generic Object first so we can distinguish "file gone (stale GUID cache) → 404"
            // from "file exists but is a different asset type → 400".
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (obj == null)
            {
                RestResponse.SendNotFound(response, $"No asset found for GUID: {guid}");
                return;
            }
            var so = obj as ScriptableObject;
            if (so == null)
            {
                RestResponse.SendError(response, $"Asset is not a ScriptableObject: {assetPath}", 400);
                return;
            }

            var typeName = so.GetType().FullName;

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"guid\":\"{RestResponse.EscapeJson(guid)}\",");
            sb.Append($"\"path\":\"{RestResponse.EscapeJson(assetPath)}\",");
            sb.Append($"\"type\":\"{RestResponse.EscapeJson(typeName)}\",");
            sb.Append("\"properties\":{");

            var serializedObj = new SerializedObject(so);
            var iter = serializedObj.GetIterator();
            bool enterChildren = true;
            bool firstProp = true;

            while (iter.NextVisible(enterChildren))
            {
                enterChildren = false; // visit top-level properties only; do not descend into children
                if (iter.name == "m_Script") continue;

                if (!firstProp) sb.Append(",");
                sb.Append($"\"{RestResponse.EscapeJson(iter.name)}\":");
                SerializedPropertySerializer.SerializePropertyToJson(iter.Copy(), sb);
                firstProp = false;
            }

            sb.Append("}}");
            RestResponse.Send(response, sb.ToString());
        }
    }
}

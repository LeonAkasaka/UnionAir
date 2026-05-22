using System;
using System.Globalization;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// GET /api/search/gameobjects
    /// Searches scene GameObjects using multiple optional AND-combined filters.
    /// </summary>
    internal class SearchGameObjectsHandler : IRequestHandler
    {
        /// <summary>
        /// Determines whether this handler can process the request.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <returns>True when this handler supports the request.</returns>
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "GET" && request.Url.AbsolutePath == "/api/search/gameobjects";

        /// <summary>
        /// Processes the request and writes the HTTP response.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <param name="response">HTTP response to write.</param>
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var qs = request.QueryString;
            var filterName      = qs["name"]      ?? "";
            var filterComponent = qs["component"] ?? "";
            var filterTag       = qs["tag"]        ?? "";
            var filterLayerStr  = qs["layer"];
            var filterActiveStr = qs["active"];
            var filterAssetGuid = qs["assetGuid"]  ?? "";
            bool includeComponents = string.Equals(qs["includeComponents"], "true",
                StringComparison.OrdinalIgnoreCase);

            int? filterLayer = null;
            if (!string.IsNullOrEmpty(filterLayerStr) && int.TryParse(filterLayerStr, out int l))
                filterLayer = l;

            bool? filterActive = null;
            if (!string.IsNullOrEmpty(filterActiveStr))
                filterActive = string.Equals(filterActiveStr, "true", StringComparison.OrdinalIgnoreCase);

            var scene   = EditorSceneManager.GetActiveScene();
            var allGos  = SceneUtils.GetAllGameObjects(scene);

            var sb = new StringBuilder();
            sb.Append("{\"gameObjects\":[");
            int count = 0;

            foreach (var (go, path) in allGos)
            {
                // ── Name filter ────────────────────────────────────────────────
                if (!string.IsNullOrEmpty(filterName) &&
                    go.name.IndexOf(filterName, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // ── Tag filter ─────────────────────────────────────────────────
                if (!string.IsNullOrEmpty(filterTag) && go.tag != filterTag)
                    continue;

                // ── Layer filter ───────────────────────────────────────────────
                if (filterLayer.HasValue && go.layer != filterLayer.Value)
                    continue;

                // ── Active filter ──────────────────────────────────────────────
                if (filterActive.HasValue && go.activeInHierarchy != filterActive.Value)
                    continue;

                // ── Component type filter ──────────────────────────────────────
                if (!string.IsNullOrEmpty(filterComponent))
                {
                    var components = go.GetComponents<Component>();
                    bool hasMatch = false;
                    foreach (var comp in components)
                    {
                        if (comp == null) continue;
                        if (comp.GetType().Name.IndexOf(filterComponent, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            comp.GetType().FullName.IndexOf(filterComponent, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            hasMatch = true;
                            break;
                        }
                    }
                    if (!hasMatch) continue;
                }

                // ── Asset GUID reference filter ────────────────────────────────
                if (!string.IsNullOrEmpty(filterAssetGuid))
                {
                    var components = go.GetComponents<Component>();
                    bool hasRef = false;
                    foreach (var comp in components)
                    {
                        if (SceneUtils.ComponentReferencesAsset(comp, filterAssetGuid))
                        {
                            hasRef = true;
                            break;
                        }
                    }
                    if (!hasRef) continue;
                }

                // ── Matched — append node ──────────────────────────────────────
                if (count > 0) sb.Append(",");
                AppendGameObjectEntry(sb, go, path, includeComponents);
                count++;
            }

            sb.Append($"],\"count\":{count}}}");
            RestResponse.Send(response, sb.ToString());
        }

        private static void AppendGameObjectEntry(
            StringBuilder sb, GameObject go, string path, bool includeComponents)
        {
            var t = go.transform;
            var p = t.localPosition;
            var r = t.localEulerAngles;
            var s = t.localScale;

            sb.Append("{");
            sb.Append($"\"name\":\"{RestResponse.EscapeJson(go.name)}\",");
            sb.Append($"\"path\":\"{RestResponse.EscapeJson(path)}\",");
            sb.Append($"\"isActive\":{Bool(go.activeInHierarchy)},");
            sb.Append($"\"tag\":\"{RestResponse.EscapeJson(go.tag)}\",");
            sb.Append($"\"layer\":{go.layer},");
            sb.Append("\"transform\":{");
            sb.Append($"\"position\":{{\"x\":{F(p.x)},\"y\":{F(p.y)},\"z\":{F(p.z)}}},");
            sb.Append($"\"rotation\":{{\"x\":{F(r.x)},\"y\":{F(r.y)},\"z\":{F(r.z)}}},");
            sb.Append($"\"scale\":{{\"x\":{F(s.x)},\"y\":{F(s.y)},\"z\":{F(s.z)}}}");
            sb.Append("}");

            if (includeComponents)
            {
                sb.Append(",\"components\":[");
                var components = go.GetComponents<Component>();
                bool firstComp = true;
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    if (!firstComp) sb.Append(",");
                    firstComp = false;
                    sb.Append($"{{\"type\":\"{RestResponse.EscapeJson(comp.GetType().FullName)}\"}}");
                }
                sb.Append("]");
            }

            sb.Append("}");
        }

        private static string F(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("G", CultureInfo.InvariantCulture);
        private static string Bool(bool b) => b ? "true" : "false";
    }
}

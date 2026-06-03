using System;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// GET /api/search/gameobjects
    /// Searches scene GameObjects using multiple optional AND-combined filters.
    /// </summary>
    internal class SearchGameObjectsHandler
    {
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

            if (!SceneResolver.TryResolveFromRequest(request, response, null, out var scene))
                return;

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
            sb.Append($"\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))}\",");
            sb.Append($"\"isActive\":{RestResponse.FormatBool(go.activeInHierarchy)},");
            sb.Append($"\"tag\":\"{RestResponse.EscapeJson(go.tag)}\",");
            sb.Append($"\"layer\":{go.layer},");
            sb.Append("\"transform\":{");
            sb.Append($"\"position\":{{\"x\":{RestResponse.FormatFloat(p.x)},\"y\":{RestResponse.FormatFloat(p.y)},\"z\":{RestResponse.FormatFloat(p.z)}}},");
            sb.Append($"\"rotation\":{{\"x\":{RestResponse.FormatFloat(r.x)},\"y\":{RestResponse.FormatFloat(r.y)},\"z\":{RestResponse.FormatFloat(r.z)}}},");
            sb.Append($"\"scale\":{{\"x\":{RestResponse.FormatFloat(s.x)},\"y\":{RestResponse.FormatFloat(s.y)},\"z\":{RestResponse.FormatFloat(s.z)}}}");
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
                    sb.Append("{");
                    sb.Append($"\"type\":\"{RestResponse.EscapeJson(comp.GetType().FullName)}\",");
                    sb.Append($"\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(comp))}\"");
                    sb.Append("}");
                }
                sb.Append("]");
            }

            sb.Append("}");
        }

    }
}

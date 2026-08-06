using System.Text;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class SceneHandler
    {
        public void Handle(UnionAirRequest request, UnionAirResponse response)
        {
            if (request.Url.AbsolutePath == "/api/scene/hierarchy")
                HandleHierarchy(request, response);
            else
                HandleSceneInfo(request, response);
        }

        private static void HandleSceneInfo(UnionAirRequest request, UnionAirResponse response)
        {
            if (!SceneResolver.TryResolveFromRequest(request, response, null, out var scene))
                return;

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"name\":\"{RestResponse.EscapeJson(scene.name)}\",");
            sb.Append($"\"path\":\"{RestResponse.EscapeJson(scene.path)}\",");
            sb.Append($"\"guid\":\"{RestResponse.EscapeJson(UnityEditor.AssetDatabase.AssetPathToGUID(scene.path))}\",");
            sb.Append($"\"isDirty\":{RestResponse.FormatBool(scene.isDirty)},");
            sb.Append($"\"isLoaded\":{RestResponse.FormatBool(scene.isLoaded)},");
            sb.Append($"\"rootCount\":{scene.rootCount}");
            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        private static void HandleHierarchy(UnionAirRequest request, UnionAirResponse response)
        {
            var qs = request.QueryString;

            // ?depth=N — max recursion depth (-1 = unlimited)
            int maxDepth = -1;
            if (int.TryParse(qs["depth"], out int d)) maxDepth = d;

            // ?compact=true — omit transform/tag/layer, add childCount
            bool compact = qs["compact"] == "true" || qs["compact"] == "1";

            // ?limit=N — max total objects (default 500)
            int limit = 500;
            if (int.TryParse(qs["limit"], out int l)) limit = l < 1 ? 1 : l;

            // ?path=<path> — subtree root (null = scene roots)
            var subtreePath = qs["path"];

            if (!SceneResolver.TryResolveFromRequest(request, response, null, out var scene))
                return;

            var sb = new StringBuilder();
            int counter = 0;
            bool truncated = false;

            sb.Append("{\"scene\":\"");
            sb.Append(RestResponse.EscapeJson(scene.name));
            sb.Append("\",");

            // Append query options used (aids LLM in understanding what was filtered)
            if (maxDepth >= 0)  sb.Append($"\"depth\":{maxDepth},");
            if (compact)        sb.Append("\"compact\":true,");
            sb.Append($"\"limit\":{limit},");

            sb.Append("\"objects\":[");

            if (!string.IsNullOrEmpty(subtreePath))
            {
                // Return children of a specific node as top-level objects
                var root = GameObjectUtils.FindByPath(scene, subtreePath);
                if (root == null)
                {
                    RestResponse.SendNotFound(response, $"GameObject not found at path: {subtreePath}");
                    return;
                }
                bool first = true;
                for (int i = 0; i < root.transform.childCount; i++)
                {
                    if (counter >= limit) { truncated = true; break; }
                    if (!first) sb.Append(",");
                    first = false;
                    AppendNode(sb, root.transform.GetChild(i).gameObject,
                        subtreePath + "/" + root.transform.GetChild(i).name,
                        0, maxDepth, compact, limit, ref counter, ref truncated);
                }
            }
            else
            {
                var roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    if (counter >= limit) { truncated = true; break; }
                    if (i > 0) sb.Append(",");
                    AppendNode(sb, roots[i], roots[i].name,
                        0, maxDepth, compact, limit, ref counter, ref truncated);
                }
            }

            sb.Append("],");
            sb.Append($"\"totalReturned\":{counter},");
            sb.Append($"\"truncated\":{RestResponse.FormatBool(truncated)}");
            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        private static void AppendNode(StringBuilder sb, GameObject go, string path,
            int currentDepth, int maxDepth, bool compact, int limit, ref int counter, ref bool truncated)
        {
            if (counter >= limit) { truncated = true; return; }
            counter++;

            var t = go.transform;

            sb.Append("{");
            sb.Append($"\"name\":\"{RestResponse.EscapeJson(go.name)}\",");
            sb.Append($"\"path\":\"{RestResponse.EscapeJson(path)}\",");
            sb.Append($"\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))}\",");
            sb.Append($"\"isActive\":{RestResponse.FormatBool(go.activeInHierarchy)},");

            if (compact)
            {
                sb.Append($"\"childCount\":{t.childCount}");
            }
            else
            {
                sb.Append($"\"tag\":\"{RestResponse.EscapeJson(go.tag)}\",");
                sb.Append($"\"layer\":{go.layer},");
                var p = t.localPosition;
                var r = t.localEulerAngles;
                var s = t.localScale;
                sb.Append("\"transform\":{");
                sb.Append($"\"position\":{{\"x\":{RestResponse.FormatFloat(p.x)},\"y\":{RestResponse.FormatFloat(p.y)},\"z\":{RestResponse.FormatFloat(p.z)}}},");
                sb.Append($"\"rotation\":{{\"x\":{RestResponse.FormatFloat(r.x)},\"y\":{RestResponse.FormatFloat(r.y)},\"z\":{RestResponse.FormatFloat(r.z)}}},");
                sb.Append($"\"scale\":{{\"x\":{RestResponse.FormatFloat(s.x)},\"y\":{RestResponse.FormatFloat(s.y)},\"z\":{RestResponse.FormatFloat(s.z)}}}");
                sb.Append("},\"children\":[");
                AppendChildren(sb, t, path, currentDepth, maxDepth, compact, limit, ref counter, ref truncated);
                sb.Append("]");
            }

            if (compact && (maxDepth < 0 || currentDepth < maxDepth))
            {
                sb.Append(",\"children\":[");
                AppendChildren(sb, t, path, currentDepth, maxDepth, compact, limit, ref counter, ref truncated);
                sb.Append("]");
            }

            sb.Append("}");
        }

        private static void AppendChildren(StringBuilder sb, Transform t, string path,
            int currentDepth, int maxDepth, bool compact, int limit, ref int counter, ref bool truncated)
        {
            if (maxDepth >= 0 && currentDepth >= maxDepth) return;

            bool firstChild = true;
            for (int i = 0; i < t.childCount; i++)
            {
                if (counter >= limit) { truncated = true; break; }
                if (!firstChild) sb.Append(",");
                firstChild = false;
                var child = t.GetChild(i).gameObject;
                AppendNode(sb, child, path + "/" + child.name,
                    currentDepth + 1, maxDepth, compact, limit, ref counter, ref truncated);
            }
        }

    }
}

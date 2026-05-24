using System.Globalization;
using System.Net;
using System.Text;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class SceneHandler : IRequestHandler
    {
        /// <summary>
        /// Determines whether this handler can process the request.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <returns>True when this handler supports the request.</returns>
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "GET" &&
               (request.Url.AbsolutePath == "/api/scene" ||
                request.Url.AbsolutePath == "/api/scene/hierarchy");

        /// <summary>
        /// Processes the request and writes the HTTP response.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <param name="response">HTTP response to write.</param>
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (request.Url.AbsolutePath == "/api/scene/hierarchy")
                HandleHierarchy(request, response);
            else
                HandleSceneInfo(request, response);
        }

        private static void HandleSceneInfo(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (!SceneResolver.TryResolveFromRequest(request, response, null, out var scene))
                return;

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"name\":\"{RestResponse.EscapeJson(scene.name)}\",");
            sb.Append($"\"path\":\"{RestResponse.EscapeJson(scene.path)}\",");
            sb.Append($"\"guid\":\"{RestResponse.EscapeJson(UnityEditor.AssetDatabase.AssetPathToGUID(scene.path))}\",");
            sb.Append($"\"isDirty\":{BoolStr(scene.isDirty)},");
            sb.Append($"\"isLoaded\":{BoolStr(scene.isLoaded)},");
            sb.Append($"\"rootCount\":{scene.rootCount}");
            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        private static void HandleHierarchy(HttpListenerRequest request, HttpListenerResponse response)
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
            var counter = new int[1]; // use array for ref-in-lambda compatibility
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
                    if (counter[0] >= limit) { truncated = true; break; }
                    if (!first) sb.Append(",");
                    first = false;
                    AppendNode(sb, root.transform.GetChild(i).gameObject,
                        subtreePath + "/" + root.transform.GetChild(i).name,
                        0, maxDepth, compact, limit, counter, ref truncated);
                }
            }
            else
            {
                var roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    if (counter[0] >= limit) { truncated = true; break; }
                    if (i > 0) sb.Append(",");
                    AppendNode(sb, roots[i], roots[i].name,
                        0, maxDepth, compact, limit, counter, ref truncated);
                }
            }

            sb.Append("],");
            sb.Append($"\"totalReturned\":{counter[0]},");
            sb.Append($"\"truncated\":{BoolStr(truncated)}");
            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        private static void AppendNode(StringBuilder sb, GameObject go, string path,
            int currentDepth, int maxDepth, bool compact, int limit, int[] counter, ref bool truncated)
        {
            if (counter[0] >= limit) { truncated = true; return; }
            counter[0]++;

            var t = go.transform;

            sb.Append("{");
            sb.Append($"\"name\":\"{RestResponse.EscapeJson(go.name)}\",");
            sb.Append($"\"path\":\"{RestResponse.EscapeJson(path)}\",");
            sb.Append($"\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))}\",");
            sb.Append($"\"isActive\":{BoolStr(go.activeInHierarchy)},");

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
                sb.Append($"\"position\":{{\"x\":{F(p.x)},\"y\":{F(p.y)},\"z\":{F(p.z)}}},");
                sb.Append($"\"rotation\":{{\"x\":{F(r.x)},\"y\":{F(r.y)},\"z\":{F(r.z)}}},");
                sb.Append($"\"scale\":{{\"x\":{F(s.x)},\"y\":{F(s.y)},\"z\":{F(s.z)}}}");
                sb.Append("},\"children\":[");

                if (maxDepth < 0 || currentDepth < maxDepth)
                {
                    int childCount = t.childCount;
                    bool firstChild = true;
                    for (int i = 0; i < childCount; i++)
                    {
                        if (counter[0] >= limit) { truncated = true; break; }
                        if (!firstChild) sb.Append(",");
                        firstChild = false;
                        var child = t.GetChild(i).gameObject;
                        AppendNode(sb, child, path + "/" + child.name,
                            currentDepth + 1, maxDepth, compact, limit, counter, ref truncated);
                    }
                }

                sb.Append("]");
            }

            // In compact mode, still recurse into children if depth allows
            if (compact)
            {
                if (maxDepth < 0 || currentDepth < maxDepth)
                {
                    sb.Append(",\"children\":[");
                    int childCount = t.childCount;
                    bool firstChild = true;
                    for (int i = 0; i < childCount; i++)
                    {
                        if (counter[0] >= limit) { truncated = true; break; }
                        if (!firstChild) sb.Append(",");
                        firstChild = false;
                        var child = t.GetChild(i).gameObject;
                        AppendNode(sb, child, path + "/" + child.name,
                            currentDepth + 1, maxDepth, compact, limit, counter, ref truncated);
                    }
                    sb.Append("]");
                }
            }

            sb.Append("}");
        }

        private static string F(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("G", CultureInfo.InvariantCulture);
        private static string BoolStr(bool b) => b ? "true" : "false";
    }
}

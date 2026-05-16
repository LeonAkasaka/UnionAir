using System.Collections.Generic;
using System.Net;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// GET /api/scene/stats
    /// Returns aggregated statistics about the current scene.
    /// </summary>
    internal class SceneStatsHandler : IRequestHandler
    {
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "GET" && request.Url.AbsolutePath == "/api/scene/stats";

        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var scene  = EditorSceneManager.GetActiveScene();
            var allGos = SceneUtils.GetAllGameObjects(scene);

            int total  = 0;
            int active = 0;
            var componentCounts = new Dictionary<string, int>();
            var tagCounts       = new Dictionary<string, int>();
            var layerCounts     = new Dictionary<int, int>();

            foreach (var (go, _) in allGos)
            {
                total++;
                if (go.activeInHierarchy) active++;

                // Tag counts
                var tag = go.tag ?? "Untagged";
                tagCounts[tag] = tagCounts.TryGetValue(tag, out int tc) ? tc + 1 : 1;

                // Layer counts
                var layer = go.layer;
                layerCounts[layer] = layerCounts.TryGetValue(layer, out int lc) ? lc + 1 : 1;

                // Component type counts (skip Transform to reduce noise)
                foreach (var comp in go.GetComponents<Component>())
                {
                    if (comp == null) continue;
                    var typeName = comp.GetType().Name;
                    if (typeName == "Transform" || typeName == "RectTransform") continue;
                    componentCounts[typeName] =
                        componentCounts.TryGetValue(typeName, out int cc) ? cc + 1 : 1;
                }
            }

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"scene\":\"{RestResponse.EscapeJson(scene.name)}\",");
            sb.Append($"\"totalGameObjects\":{total},");
            sb.Append($"\"activeGameObjects\":{active},");
            sb.Append($"\"inactiveGameObjects\":{total - active},");

            // Component counts (sorted descending)
            sb.Append("\"componentCounts\":{");
            AppendIntDict(sb, componentCounts);
            sb.Append("},");

            // Tag counts
            sb.Append("\"tagCounts\":{");
            AppendIntDict(sb, tagCounts);
            sb.Append("},");

            // Layer counts (keyed by layer name when known, otherwise number)
            sb.Append("\"layerCounts\":{");
            bool firstLayer = true;
            foreach (var kv in layerCounts)
            {
                if (!firstLayer) sb.Append(",");
                firstLayer = false;
                var layerName = LayerMask.LayerToName(kv.Key);
                var key = string.IsNullOrEmpty(layerName) ? kv.Key.ToString() : layerName;
                sb.Append($"\"{RestResponse.EscapeJson(key)}\":{kv.Value}");
            }
            sb.Append("}");

            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        private static void AppendIntDict(StringBuilder sb, Dictionary<string, int> dict)
        {
            bool first = true;
            foreach (var kv in dict)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append($"\"{RestResponse.EscapeJson(kv.Key)}\":{kv.Value}");
            }
        }
    }
}

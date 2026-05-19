using System.Net;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// GET /api/search/asset-refs?guid={assetGuid}
    /// Finds all scene GameObjects/components that reference the specified asset.
    /// </summary>
    internal class SearchAssetRefsHandler : IRequestHandler
    {
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "GET" && request.Url.AbsolutePath == "/api/search/asset-refs";

        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var guid = request.QueryString["guid"] ?? "";
            if (string.IsNullOrEmpty(guid))
            {
                RestResponse.SendError(response, "Missing required query parameter: guid", 400);
                return;
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendNotFound(response, $"No asset found for GUID: {guid}");
                return;
            }

            var assetTypeName = AssetDatabase.GetMainAssetTypeAtPath(assetPath)?.Name ?? "Unknown";
            var scene         = EditorSceneManager.GetActiveScene();
            var allGos        = SceneUtils.GetAllGameObjects(scene);

            var sb = new StringBuilder();
            sb.Append("{\"asset\":{");
            sb.Append($"\"guid\":\"{RestResponse.EscapeJson(guid)}\",");
            sb.Append($"\"path\":\"{RestResponse.EscapeJson(assetPath)}\",");
            sb.Append($"\"type\":\"{RestResponse.EscapeJson(assetTypeName)}\"");
            sb.Append("},\"references\":[");

            int refCount = 0;
            foreach (var (go, goPath) in allGos)
            {
                foreach (var comp in go.GetComponents<Component>())
                {
                    if (comp == null) continue;
                    var propNames = SceneUtils.FindAssetRefsInComponent(comp, guid);
                    foreach (var propName in propNames)
                    {
                        if (refCount > 0) sb.Append(",");
                        sb.Append("{");
                        sb.Append($"\"gameObjectPath\":\"{RestResponse.EscapeJson(goPath)}\",");
                        sb.Append($"\"componentType\":\"{RestResponse.EscapeJson(comp.GetType().FullName)}\",");
                        sb.Append($"\"propertyName\":\"{RestResponse.EscapeJson(propName)}\"");
                        sb.Append("}");
                        refCount++;
                    }
                }
            }

            sb.Append($"],\"count\":{refCount}}}");
            RestResponse.Send(response, sb.ToString());
        }
    }
}

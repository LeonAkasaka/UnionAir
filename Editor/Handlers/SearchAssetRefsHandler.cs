using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// GET /api/search/asset-refs?guid={assetGuid}
    /// Finds all scene GameObjects/components that reference the specified asset.
    /// </summary>
    internal class SearchAssetRefsHandler : IRequestHandler
    {
        /// <summary>
        /// Determines whether this handler can process the request.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <returns>True when this handler supports the request.</returns>
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "GET" && request.Url.AbsolutePath == "/api/search/asset-refs";

        /// <summary>
        /// Processes the request and writes the HTTP response.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <param name="response">HTTP response to write.</param>
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
            if (!SceneResolver.TryResolveFromRequest(request, response, null, out var scene))
                return;

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
                        sb.Append($"\"gameObjectGlobalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))}\",");
                        sb.Append($"\"componentType\":\"{RestResponse.EscapeJson(comp.GetType().FullName)}\",");
                        sb.Append($"\"componentGlobalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(comp))}\",");
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

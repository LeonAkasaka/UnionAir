using System.Net;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles DELETE /api/assets/&lt;guid&gt;
    /// Deletes the asset file and its .meta file via AssetDatabase.DeleteAsset.
    /// </summary>
    internal class AssetDeleteHandler : IRequestHandler
    {
        /// <summary>
        /// Determines whether this handler can process the request.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <returns>True when this handler supports the request.</returns>
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "DELETE" &&
               request.Url.AbsolutePath.StartsWith("/api/assets/") &&
               !request.Url.AbsolutePath.StartsWith("/api/assets/prefabs") &&
               !request.Url.AbsolutePath.StartsWith("/api/assets/materials") &&
               !request.Url.AbsolutePath.StartsWith("/api/assets/move");

        /// <summary>
        /// Processes the request and writes the HTTP response.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <param name="response">HTTP response to write.</param>
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var guid = request.Url.AbsolutePath.Substring("/api/assets/".Length);
            if (string.IsNullOrEmpty(guid))
            {
                RestResponse.SendError(response, "Missing asset GUID in path", 400);
                return;
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendNotFound(response, $"No asset found for GUID: {guid}");
                return;
            }

            bool deleted = AssetDatabase.DeleteAsset(assetPath);
            if (!deleted)
            {
                RestResponse.SendError(response, $"Failed to delete asset: {assetPath}", 500);
                return;
            }

            RestResponse.Send(response, $"{{\"deleted\":\"{RestResponse.EscapeJson(assetPath)}\"}}");
        }
    }
}

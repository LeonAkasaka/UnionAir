using System.Net;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles DELETE /api/assets/&lt;guid&gt;
    /// Deletes the asset file and its .meta file via AssetDatabase.DeleteAsset.
    /// </summary>
    internal class AssetDeleteHandler
    {
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

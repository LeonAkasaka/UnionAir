using System.Collections.Generic;
using System.Net;
using System.Text;
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

            var loadedScenes = LoadedSceneAssetSafety.FindLoadedSceneConflicts(
                assetPath,
                true);
            if (loadedScenes.Count > 0)
            {
                RestResponse.Send(
                    response,
                    AssetDeleteSafety.BuildConflictJson(assetPath, loadedScenes),
                    409);
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

    internal static class AssetDeleteSafety
    {
        private const string ErrorMessage =
            "Cannot delete loaded scenes. Unload them before retrying to avoid deleting " +
            "the backing asset of an open scene.";

        internal static string BuildConflictJson(
            string assetPath,
            IReadOnlyList<LoadedSceneAssetConflict> loadedScenes)
        {
            var sb = new StringBuilder();
            sb.Append("{\"error\":\"");
            sb.Append(RestResponse.EscapeJson(ErrorMessage));
            sb.Append("\",\"code\":\"loaded_scene_delete_blocked\",\"assetPath\":\"");
            sb.Append(RestResponse.EscapeJson(assetPath));
            sb.Append("\",\"loadedScenes\":");
            LoadedSceneAssetSafety.AppendLoadedScenesJson(sb, loadedScenes);
            sb.Append("}");
            return sb.ToString();
        }
    }
}

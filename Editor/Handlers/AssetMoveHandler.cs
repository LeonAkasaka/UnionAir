using System.Net;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles POST /api/assets/move
    /// Moves or renames an asset while preserving its GUID and all project references.
    /// Body: { "guid": "...", "newPath": "Assets/NewFolder/Name.prefab" }
    /// </summary>
    internal class AssetMoveHandler
    {
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body    = RequestBodyReader.ReadString(request);
            var guid    = RequestBodyReader.GetString(body, "guid");
            var newPath = RequestBodyReader.GetString(body, "newPath");

            if (string.IsNullOrEmpty(guid))
            {
                RestResponse.SendError(response, "Missing required field: guid", 400);
                return;
            }
            if (string.IsNullOrEmpty(newPath))
            {
                RestResponse.SendError(response, "Missing required field: newPath", 400);
                return;
            }

            var oldPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(oldPath))
            {
                RestResponse.SendNotFound(response, $"No asset found for GUID: {guid}");
                return;
            }

            // Ensure destination directory exists
            var dir = System.IO.Path.GetDirectoryName(newPath).Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                var parts = dir.Split('/');
                var current = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    var next = current + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(current, parts[i]);
                    current = next;
                }
            }

            var error = AssetDatabase.MoveAsset(oldPath, newPath);
            if (!string.IsNullOrEmpty(error))
            {
                RestResponse.SendError(response, $"Move failed: {error}", 500);
                return;
            }

            LoadedSceneDiskChangeGuard.RecordLoadedScenesAfterAssetMove(oldPath, newPath);

            RestResponse.Send(response,
                $"{{\"from\":\"{RestResponse.EscapeJson(oldPath)}\"," +
                $"\"to\":\"{RestResponse.EscapeJson(newPath)}\"}}");
        }
    }
}

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles POST /api/assets/prefabs
    /// Creates or overwrites a prefab asset from a scene GameObject.
    /// Body: { "source": {"value": "..."}, "assetPath": "Assets/Prefabs/Name.prefab", "mode": "new|replace" }
    /// </summary>
    internal class PrefabCreateHandler
    {
        public void Handle(UnionAirRequest request, UnionAirResponse response)
        {
            var body      = RequestBodyReader.ReadString(request);
            var assetPath = RequestBodyReader.GetString(body, "assetPath");
            var mode      = RequestBodyReader.GetString(body, "mode") ?? "new";
            if (!SceneResolver.TryResolveFromRequest(request, response, body, out var scene))
                return;

            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendError(response, "Missing required field: assetPath", 400);
                return;
            }
            if (!assetPath.EndsWith(".prefab"))
            {
                RestResponse.SendError(response, "assetPath must end with .prefab", 400);
                return;
            }

            if (!ObjectRefUtils.TryReadBody(body, "source", out var source, out var error, out var statusCode) ||
                !ObjectRefUtils.TryResolveGameObject(scene, source, "source", out var go, out error, out statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }
            scene = go.scene;

            // Ensure parent directory exists
            var dir = System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir))
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

            GameObject saved;
            if (mode == "replace")
            {
                saved = PrefabUtility.SaveAsPrefabAsset(go, assetPath);
            }
            else
            {
                // "new" mode: save and connect so the scene instance becomes a prefab instance
                saved = PrefabUtility.SaveAsPrefabAssetAndConnect(
                    go, assetPath, InteractionMode.AutomatedAction);
            }

            if (saved == null)
            {
                RestResponse.SendError(response, $"Failed to save prefab to: {assetPath}", 500);
                return;
            }

            EditorSceneManager.MarkSceneDirty(scene);

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            RestResponse.Send(response,
                $"{{\"assetPath\":\"{RestResponse.EscapeJson(assetPath)}\",\"guid\":\"{RestResponse.EscapeJson(guid)}\",\"sourceGlobalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))}\"}}", 201);
        }
    }
}

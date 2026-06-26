using System.Net;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles POST /api/scene/save — saves the current active scene to disk.
    /// </summary>
    internal class SceneSaveHandler
    {
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            var assetPath = RequestBodyReader.GetString(body, "assetPath");

            var scene = EditorSceneManager.GetActiveScene();

            if (string.IsNullOrEmpty(assetPath))
            {
                if (string.IsNullOrEmpty(scene.path))
                {
                    RestResponse.SendError(response,
                        "Scene has not been saved to disk yet. Provide assetPath in the request body to specify a save location.", 400);
                    return;
                }

                bool saved = EditorSceneManager.SaveScene(scene);
                if (!saved)
                {
                    RestResponse.SendError(response, "Failed to save scene.", 500);
                    return;
                }
            }
            else
            {
                if (!assetPath.EndsWith(".unity", System.StringComparison.OrdinalIgnoreCase))
                {
                    RestResponse.SendError(response, "assetPath must end with .unity", 400);
                    return;
                }

                bool saved = EditorSceneManager.SaveScene(scene, assetPath);
                if (!saved)
                {
                    RestResponse.SendError(response, "Failed to save scene.", 500);
                    return;
                }
            }

            RestResponse.Send(response,
                $"{{\"saved\":\"{RestResponse.EscapeJson(scene.path)}\"}}");
        }
    }
}

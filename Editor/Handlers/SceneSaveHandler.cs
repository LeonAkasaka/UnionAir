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
            var scene = EditorSceneManager.GetActiveScene();

            if (string.IsNullOrEmpty(scene.path))
            {
                RestResponse.SendError(response,
                    "Scene has not been saved to disk yet. Use File > Save As first.", 400);
                return;
            }

            bool saved = EditorSceneManager.SaveScene(scene);
            if (!saved)
            {
                RestResponse.SendError(response, "Failed to save scene.", 500);
                return;
            }

            RestResponse.Send(response,
                $"{{\"saved\":\"{RestResponse.EscapeJson(scene.path)}\"}}");
        }
    }
}

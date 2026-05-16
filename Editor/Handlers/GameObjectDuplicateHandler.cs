using System.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles POST /api/gameobjects/duplicate?path=&lt;path&gt;
    /// Duplicates the target GameObject and places it next to the original.
    /// </summary>
    internal class GameObjectDuplicateHandler : IRequestHandler
    {
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "POST" &&
               request.Url.AbsolutePath == "/api/gameobjects/duplicate";

        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var path = request.QueryString["path"];
            if (string.IsNullOrEmpty(path))
            {
                RestResponse.SendError(response, "Missing required query parameter: path", 400);
                return;
            }

            var go = GameObjectUtils.FindByPath(path);
            if (go == null)
            {
                RestResponse.SendNotFound(response, $"GameObject not found at path: {path}");
                return;
            }

            Undo.SetCurrentGroupName("UnionAir: Duplicate GameObject");
            var group = Undo.GetCurrentGroup();

            var copy = Object.Instantiate(go, go.transform.parent);
            copy.name = GameObjectUtility.GetUniqueNameForSibling(go.transform.parent, go.name);
            Undo.RegisterCreatedObjectUndo(copy, "UnionAir: Duplicate GameObject");

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            var newPath = GameObjectUtils.GetPath(copy);
            RestResponse.Send(response,
                $"{{\"name\":\"{RestResponse.EscapeJson(copy.name)}\",\"path\":\"{RestResponse.EscapeJson(newPath)}\"}}", 201);
        }
    }
}

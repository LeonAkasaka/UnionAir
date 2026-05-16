using System.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles POST /api/gameobjects/reparent
    /// Moves a GameObject under a new parent (or to the root when parentPath is omitted).
    /// Body: { "path": "...", "parentPath": "..." }
    /// </summary>
    internal class GameObjectReparentHandler : IRequestHandler
    {
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "POST" &&
               request.Url.AbsolutePath == "/api/gameobjects/reparent";

        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            var path = RequestBodyReader.GetString(body, "path");
            if (string.IsNullOrEmpty(path))
            {
                RestResponse.SendError(response, "Missing required field: path", 400);
                return;
            }

            var go = GameObjectUtils.FindByPath(path);
            if (go == null)
            {
                RestResponse.SendNotFound(response, $"GameObject not found at path: {path}");
                return;
            }

            var parentPath = RequestBodyReader.GetString(body, "parentPath");
            Transform newParent = null;

            if (!string.IsNullOrEmpty(parentPath))
            {
                var parentGo = GameObjectUtils.FindByPath(parentPath);
                if (parentGo == null)
                {
                    RestResponse.SendNotFound(response, $"Parent not found: {parentPath}");
                    return;
                }
                newParent = parentGo.transform;
            }

            Undo.SetCurrentGroupName("UnionAir: Reparent GameObject");
            var group = Undo.GetCurrentGroup();
            Undo.SetTransformParent(go.transform, newParent, "UnionAir: Reparent GameObject");
            Undo.CollapseUndoOperations(group);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            var newGoPath = GameObjectUtils.GetPath(go);
            RestResponse.Send(response, $"{{\"path\":\"{RestResponse.EscapeJson(newGoPath)}\"}}");
        }
    }
}

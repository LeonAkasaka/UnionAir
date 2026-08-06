using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles POST /api/gameobjects/reparent
    /// Moves a GameObject under a new parent (or to the root when parent is omitted).
    /// Body: { "target": {"value": "..."}, "parent": {"value": "..."} }
    /// </summary>
    internal class GameObjectReparentHandler
    {
        public void Handle(UnionAirRequest request, UnionAirResponse response)
        {
            var body = RequestBodyReader.ReadString(request);

            if (!SceneResolver.TryResolveFromRequest(request, response, body, out var scene))
                return;

            if (!ObjectRefUtils.TryReadBody(body, "target", out var target, out var error, out var statusCode) ||
                !ObjectRefUtils.TryResolveGameObject(scene, target, "target", out var go, out error, out statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }
            scene = go.scene;
            var originalScene = go.scene;

            Transform newParent = null;

            var parentJson = RequestBodyReader.GetObject(body, "parent");
            if (!string.IsNullOrEmpty(parentJson))
            {
                if (!ObjectRefUtils.TryParse(parentJson, "parent", out var parentRef, out error, out statusCode) ||
                    !ObjectRefUtils.TryResolveGameObject(scene, parentRef, "parent", out var parentGo, out error, out statusCode))
                {
                    RestResponse.SendError(response, error, statusCode);
                    return;
                }
                scene = parentGo.scene;
                newParent = parentGo.transform;
            }

            var useUndo = !EditorApplication.isPlaying;
            var group = -1;
            if (useUndo)
            {
                Undo.SetCurrentGroupName("UnionAir: Reparent GameObject");
                group = Undo.GetCurrentGroup();
                Undo.SetTransformParent(go.transform, newParent, "UnionAir: Reparent GameObject");
            }
            else
            {
                go.transform.SetParent(newParent, false);
            }
            if (newParent == null)
                SceneManager.MoveGameObjectToScene(go, scene);
            if (useUndo)
                Undo.CollapseUndoOperations(group);

            if (originalScene != scene)
                SceneUtils.MarkDirtyUnlessPlaying(originalScene);
            SceneUtils.MarkDirtyUnlessPlaying(scene);

            var newGoPath = GameObjectUtils.GetPath(go);
            RestResponse.Send(response, $"{{\"path\":\"{RestResponse.EscapeJson(newGoPath)}\",\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))}\"}}");
        }
    }
}

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles POST /api/gameobjects/duplicate?path=&lt;path&gt;
    /// Duplicates the target GameObject and places it next to the original.
    /// </summary>
    internal class GameObjectDuplicateHandler
    {
        public void Handle(UnionAirRequest request, UnionAirResponse response)
        {
            if (!SceneResolver.TryResolveFromRequest(request, response, null, out var scene))
                return;

            if (!ObjectRefUtils.TryReadQuery(request.QueryString, "source", out var source, out var error, out var statusCode) ||
                !ObjectRefUtils.TryResolveGameObject(scene, source, "source", out var go, out error, out statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }
            scene = go.scene;

            var useUndo = !EditorApplication.isPlaying;
            var group = -1;
            if (useUndo)
            {
                Undo.SetCurrentGroupName("UnionAir: Duplicate GameObject");
                group = Undo.GetCurrentGroup();
            }

            var copy = Object.Instantiate(go, go.transform.parent);
            copy.name = GameObjectUtility.GetUniqueNameForSibling(go.transform.parent, go.name);
            if (useUndo)
                Undo.RegisterCreatedObjectUndo(copy, "UnionAir: Duplicate GameObject");

            if (useUndo)
                Undo.CollapseUndoOperations(group);
            SceneUtils.MarkDirtyUnlessPlaying(scene);

            var newPath = GameObjectUtils.GetPath(copy);
            RestResponse.Send(response,
                $"{{\"name\":\"{RestResponse.EscapeJson(copy.name)}\",\"path\":\"{RestResponse.EscapeJson(newPath)}\",\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(copy))}\"}}", 201);
        }
    }
}

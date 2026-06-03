using System;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles POST /api/gameobjects/primitive
    /// Creates a Unity primitive (Cube, Sphere, Capsule, Cylinder, Plane, Quad)
    /// using GameObject.CreatePrimitive so the mesh and default material are assigned.
    /// Body: { "type": "Cube", "name": "MyCube", "parent": {"value": "..."} }
    /// </summary>
    internal class GameObjectPrimitiveHandler
    {
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body     = RequestBodyReader.ReadString(request);
            var typeName = RequestBodyReader.GetString(body, "type");
            var name     = RequestBodyReader.GetString(body, "name");
            if (!SceneResolver.TryResolveFromRequest(request, response, body, out var scene))
                return;

            if (string.IsNullOrEmpty(typeName))
            {
                RestResponse.SendError(response,
                    "Missing required field: type (Cube|Sphere|Capsule|Cylinder|Plane|Quad)", 400);
                return;
            }

            if (!Enum.TryParse<PrimitiveType>(typeName, true, out var primitiveType))
            {
                RestResponse.SendError(response,
                    $"Unknown primitive type: {typeName}. Valid values: Cube, Sphere, Capsule, Cylinder, Plane, Quad", 400);
                return;
            }

            var useUndo = !EditorApplication.isPlaying;
            var group = -1;
            if (useUndo)
            {
                Undo.SetCurrentGroupName($"UnionAir: Create {primitiveType} Primitive");
                group = Undo.GetCurrentGroup();
            }

            var go = GameObject.CreatePrimitive(primitiveType);
            if (useUndo)
                Undo.RegisterCreatedObjectUndo(go, $"UnionAir: Create {primitiveType}");
            SceneManager.MoveGameObjectToScene(go, scene);

            if (!string.IsNullOrEmpty(name))
                go.name = name;

            var parentJson = RequestBodyReader.GetObject(body, "parent");
            if (!string.IsNullOrEmpty(parentJson))
            {
                if (!ObjectRefUtils.TryParse(parentJson, "parent", out var parentRef, out var error, out var statusCode) ||
                    !ObjectRefUtils.TryResolveGameObject(scene, parentRef, "parent", out var parent, out error, out statusCode))
                {
                    if (EditorApplication.isPlaying)
                        UnityEngine.Object.Destroy(go);
                    else
                        Undo.DestroyObjectImmediate(go);
                    RestResponse.SendError(response, error, statusCode);
                    return;
                }
                scene = parent.scene;
                go.transform.SetParent(parent.transform, false);
            }

            GameObjectUtils.ApplyTransformFromBody(go.transform, body);

            if (useUndo)
                Undo.CollapseUndoOperations(group);
            SceneUtils.MarkDirtyUnlessPlaying(scene);

            var goPath = GameObjectUtils.GetPath(go);
            var components = go.GetComponents<Component>();
            var t = go.transform;
            var p = t.localPosition;
            var r = t.localEulerAngles;
            var s = t.localScale;
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"name\":\"{RestResponse.EscapeJson(go.name)}\",");
            sb.Append($"\"path\":\"{RestResponse.EscapeJson(goPath)}\",");
            sb.Append($"\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))}\",");
            sb.Append($"\"primitiveType\":\"{RestResponse.EscapeJson(primitiveType.ToString())}\",");
            sb.Append("\"transform\":{");
            sb.Append($"\"position\":{{\"x\":{GameObjectUtils.FormatFloat(p.x)},\"y\":{GameObjectUtils.FormatFloat(p.y)},\"z\":{GameObjectUtils.FormatFloat(p.z)}}},");
            sb.Append($"\"rotation\":{{\"x\":{GameObjectUtils.FormatFloat(r.x)},\"y\":{GameObjectUtils.FormatFloat(r.y)},\"z\":{GameObjectUtils.FormatFloat(r.z)}}},");
            sb.Append($"\"scale\":{{\"x\":{GameObjectUtils.FormatFloat(s.x)},\"y\":{GameObjectUtils.FormatFloat(s.y)},\"z\":{GameObjectUtils.FormatFloat(s.z)}}}");
            sb.Append("},");
            sb.Append("\"components\":[");
            for (int i = 0; i < components.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{RestResponse.EscapeJson(components[i].GetType().Name)}\"");
            }
            sb.Append("]}");

            RestResponse.Send(response, sb.ToString(), 201);
        }
    }
}

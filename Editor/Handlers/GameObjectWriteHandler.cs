using System;
using System.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles write operations on GameObjects:
    ///   POST   /api/gameobjects          — create
    ///   DELETE /api/gameobjects?path=    — delete
    ///   PATCH  /api/gameobjects?path=    — update properties
    /// </summary>
    internal class GameObjectWriteHandler
    {
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            switch (request.HttpMethod)
            {
                case "POST":   HandleCreate(request, response); break;
                case "DELETE": HandleDelete(request, response); break;
                case "PATCH":  HandleUpdate(request, response); break;
                default:
                    RestResponse.SendError(response, "Method not allowed", 405);
                    break;
            }
        }

        // ── POST /api/gameobjects ─────────────────────────────────────────────

        private static void HandleCreate(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            var name = RequestBodyReader.GetString(body, "name");
            if (string.IsNullOrEmpty(name))
            {
                RestResponse.SendError(response, "Missing required field: name", 400);
                return;
            }

            if (!SceneResolver.TryResolveFromRequest(request, response, body, out var scene))
                return;

            var useUndo = !EditorApplication.isPlaying;
            var group = -1;
            if (useUndo)
            {
                Undo.SetCurrentGroupName("UnionAir: Create GameObject");
                group = Undo.GetCurrentGroup();
            }

            GameObject go;
            var parentJson = RequestBodyReader.GetObject(body, "parent");
            if (!string.IsNullOrEmpty(parentJson))
            {
                if (!ObjectRefUtils.TryParse(parentJson, "parent", out var parentRef, out var error, out var statusCode) ||
                    !ObjectRefUtils.TryResolveGameObject(scene, parentRef, "parent", out var parent, out error, out statusCode))
                {
                    RestResponse.SendError(response, error, statusCode);
                    return;
                }

                scene = parent.scene;
                go = new GameObject(name);
                if (useUndo)
                    Undo.RegisterCreatedObjectUndo(go, "UnionAir: Create GameObject");
                go.transform.SetParent(parent.transform, false);
            }
            else
            {
                go = new GameObject(name);
                if (useUndo)
                    Undo.RegisterCreatedObjectUndo(go, "UnionAir: Create GameObject");
                SceneManager.MoveGameObjectToScene(go, scene);
            }

            if (useUndo)
                Undo.CollapseUndoOperations(group);
            SceneUtils.MarkDirtyUnlessPlaying(scene);

            var fullPath = GameObjectUtils.GetPath(go);
            RestResponse.Send(response,
                $"{{\"name\":\"{RestResponse.EscapeJson(go.name)}\",\"path\":\"{RestResponse.EscapeJson(fullPath)}\",\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))}\"}}", 201);
        }

        // ── DELETE /api/gameobjects?path= ─────────────────────────────────────

        private static void HandleDelete(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (!SceneResolver.TryResolveFromRequest(request, response, null, out var scene))
                return;

            if (!ObjectRefUtils.TryReadQuery(request.QueryString, "target", out var target, out var error, out var statusCode) ||
                !ObjectRefUtils.TryResolveGameObject(scene, target, "target", out var go, out error, out statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }

            var deletedPath = GameObjectUtils.GetPath(go);
            var deletedId = ObjectIdUtils.GetGlobalObjectId(go);
            scene = go.scene;
            if (EditorApplication.isPlaying)
                UnityEngine.Object.Destroy(go);
            else
            {
                Undo.SetCurrentGroupName("UnionAir: Delete GameObject");
                Undo.DestroyObjectImmediate(go);
            }
            SceneUtils.MarkDirtyUnlessPlaying(scene);

            RestResponse.Send(response, $"{{\"deleted\":\"{RestResponse.EscapeJson(deletedPath)}\",\"globalObjectId\":\"{RestResponse.EscapeJson(deletedId)}\"}}");
        }

        // ── PATCH /api/gameobjects?path= ──────────────────────────────────────

        private static void HandleUpdate(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (!SceneResolver.TryResolveFromRequest(request, response, null, out var scene))
                return;

            if (!ObjectRefUtils.TryReadQuery(request.QueryString, "target", out var target, out var error, out var statusCode) ||
                !ObjectRefUtils.TryResolveGameObject(scene, target, "target", out var go, out error, out statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }
            scene = go.scene;

            var body = RequestBodyReader.ReadString(request);

            var useUndo = !EditorApplication.isPlaying;
            var group = -1;
            if (useUndo)
            {
                Undo.SetCurrentGroupName("UnionAir: Update GameObject");
                group = Undo.GetCurrentGroup();
                Undo.RecordObject(go, "UnionAir: Update GameObject");
                Undo.RecordObject(go.transform, "UnionAir: Update GameObject");
            }

            var newName = RequestBodyReader.GetString(body, "name");
            if (newName != null) go.name = newName;

            var isActive = RequestBodyReader.GetBool(body, "isActive");
            if (isActive.HasValue) go.SetActive(isActive.Value);

            var tag = RequestBodyReader.GetString(body, "tag");
            if (tag != null)
            {
                try { go.tag = tag; }
                catch (Exception) { /* ignore invalid tag */ }
            }

            var layer = RequestBodyReader.GetInt(body, "layer");
            if (layer.HasValue) go.layer = layer.Value;

            ApplyTransform(go.transform, body);

            if (useUndo)
                Undo.CollapseUndoOperations(group);
            SceneUtils.MarkDirtyUnlessPlaying(scene);

            // Return updated GameObject info reusing the existing read handler's data
            var sb = new System.Text.StringBuilder();
            var t = go.transform;
            var p = t.localPosition;
            var r = t.localEulerAngles;
            var s = t.localScale;
            var updatedPath = GameObjectUtils.GetPath(go);

            sb.Append("{");
            sb.Append($"\"name\":\"{RestResponse.EscapeJson(go.name)}\",");
            sb.Append($"\"path\":\"{RestResponse.EscapeJson(updatedPath)}\",");
            sb.Append($"\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))}\",");
            sb.Append($"\"isActive\":{RestResponse.FormatBool(go.activeInHierarchy)},");
            sb.Append($"\"tag\":\"{RestResponse.EscapeJson(go.tag)}\",");
            sb.Append($"\"layer\":{go.layer},");
            sb.Append("\"transform\":{");
            sb.Append($"\"position\":{{\"x\":{RestResponse.FormatFloat(p.x)},\"y\":{RestResponse.FormatFloat(p.y)},\"z\":{RestResponse.FormatFloat(p.z)}}},");
            sb.Append($"\"rotation\":{{\"x\":{RestResponse.FormatFloat(r.x)},\"y\":{RestResponse.FormatFloat(r.y)},\"z\":{RestResponse.FormatFloat(r.z)}}},");
            sb.Append($"\"scale\":{{\"x\":{RestResponse.FormatFloat(s.x)},\"y\":{RestResponse.FormatFloat(s.y)},\"z\":{RestResponse.FormatFloat(s.z)}}}");
            sb.Append("}}");
            RestResponse.Send(response, sb.ToString());
        }

        private static void ApplyTransform(Transform t, string body)
            => GameObjectUtils.ApplyTransformFromBody(t, body);
    }
}

using System;
using System.Globalization;
using System.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles write operations on GameObjects:
    ///   POST   /api/gameobjects          — create
    ///   DELETE /api/gameobjects?path=    — delete
    ///   PATCH  /api/gameobjects?path=    — update properties
    /// </summary>
    internal class GameObjectWriteHandler : IRequestHandler
    {
        /// <summary>
        /// Determines whether this handler can process the request.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <returns>True when this handler supports the request.</returns>
        public bool CanHandle(HttpListenerRequest request)
            => request.Url.AbsolutePath == "/api/gameobjects" &&
               (request.HttpMethod == "POST" ||
                request.HttpMethod == "DELETE" ||
                request.HttpMethod == "PATCH");

        /// <summary>
        /// Processes the request and writes the HTTP response.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <param name="response">HTTP response to write.</param>
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

            var parentPath = RequestBodyReader.GetString(body, "parentPath");

            Undo.SetCurrentGroupName("UnionAir: Create GameObject");
            var group = Undo.GetCurrentGroup();

            GameObject go;
            if (!string.IsNullOrEmpty(parentPath))
            {
                var parent = GameObjectUtils.FindByPath(parentPath);
                if (parent == null)
                {
                    RestResponse.SendError(response, $"Parent not found: {parentPath}", 404);
                    return;
                }
                go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "UnionAir: Create GameObject");
                go.transform.SetParent(parent.transform, false);
            }
            else
            {
                go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "UnionAir: Create GameObject");
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            var fullPath = GameObjectUtils.GetPath(go);
            RestResponse.Send(response,
                $"{{\"name\":\"{RestResponse.EscapeJson(go.name)}\",\"path\":\"{RestResponse.EscapeJson(fullPath)}\"}}", 201);
        }

        // ── DELETE /api/gameobjects?path= ─────────────────────────────────────

        private static void HandleDelete(HttpListenerRequest request, HttpListenerResponse response)
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

            Undo.SetCurrentGroupName("UnionAir: Delete GameObject");
            Undo.DestroyObjectImmediate(go);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            RestResponse.Send(response, $"{{\"deleted\":\"{RestResponse.EscapeJson(path)}\"}}");
        }

        // ── PATCH /api/gameobjects?path= ──────────────────────────────────────

        private static void HandleUpdate(HttpListenerRequest request, HttpListenerResponse response)
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

            var body = RequestBodyReader.ReadString(request);

            Undo.SetCurrentGroupName("UnionAir: Update GameObject");
            var group = Undo.GetCurrentGroup();
            Undo.RecordObject(go, "UnionAir: Update GameObject");
            Undo.RecordObject(go.transform, "UnionAir: Update GameObject");

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

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

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
            sb.Append($"\"isActive\":{(go.activeInHierarchy ? "true" : "false")},");
            sb.Append($"\"tag\":\"{RestResponse.EscapeJson(go.tag)}\",");
            sb.Append($"\"layer\":{go.layer},");
            sb.Append("\"transform\":{");
            sb.Append($"\"position\":{{\"x\":{F(p.x)},\"y\":{F(p.y)},\"z\":{F(p.z)}}},");
            sb.Append($"\"rotation\":{{\"x\":{F(r.x)},\"y\":{F(r.y)},\"z\":{F(r.z)}}},");
            sb.Append($"\"scale\":{{\"x\":{F(s.x)},\"y\":{F(s.y)},\"z\":{F(s.z)}}}");
            sb.Append("}}");
            RestResponse.Send(response, sb.ToString());
        }

        private static void ApplyTransform(Transform t, string body)
        {
            var transformJson = RequestBodyReader.GetObject(body, "transform");
            if (transformJson == null) return;

            var posJson = RequestBodyReader.GetObject(transformJson, "position");
            if (posJson != null)
            {
                var x = RequestBodyReader.GetFloat(posJson, "x");
                var y = RequestBodyReader.GetFloat(posJson, "y");
                var z = RequestBodyReader.GetFloat(posJson, "z");
                t.localPosition = new Vector3(
                    x ?? t.localPosition.x,
                    y ?? t.localPosition.y,
                    z ?? t.localPosition.z);
            }

            var rotJson = RequestBodyReader.GetObject(transformJson, "rotation");
            if (rotJson != null)
            {
                var x = RequestBodyReader.GetFloat(rotJson, "x");
                var y = RequestBodyReader.GetFloat(rotJson, "y");
                var z = RequestBodyReader.GetFloat(rotJson, "z");
                t.localEulerAngles = new Vector3(
                    x ?? t.localEulerAngles.x,
                    y ?? t.localEulerAngles.y,
                    z ?? t.localEulerAngles.z);
            }

            var scaleJson = RequestBodyReader.GetObject(transformJson, "scale");
            if (scaleJson != null)
            {
                var x = RequestBodyReader.GetFloat(scaleJson, "x");
                var y = RequestBodyReader.GetFloat(scaleJson, "y");
                var z = RequestBodyReader.GetFloat(scaleJson, "z");
                t.localScale = new Vector3(
                    x ?? t.localScale.x,
                    y ?? t.localScale.y,
                    z ?? t.localScale.z);
            }
        }

        private static string F(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("G", CultureInfo.InvariantCulture);
    }
}

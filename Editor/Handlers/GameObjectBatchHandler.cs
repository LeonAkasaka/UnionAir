using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// POST /api/gameobjects/batch
    /// Processes multiple GameObject operations in a single request.
    ///
    /// Body:
    /// {
    ///   "operations": [
    ///     {"op":"create",           "name":"Empty",   "parentPath":"Parent"},
    ///     {"op":"create_primitive", "type":"Cube",    "name":"Cube_0", "parentPath":"Map",
    ///      "transform":{"position":{"x":0,"y":0,"z":0}}},
    ///     {"op":"update",           "path":"Obj",     "isActive":false,
    ///      "transform":{"position":{"x":1,"y":0,"z":0}}},
    ///     {"op":"delete",           "path":"OldObj"}
    ///   ]
    /// }
    ///
    /// Response:
    /// { "processed": 4, "failed": 0,
    ///   "results": [{"index":0,"success":true,"path":"Empty"}, ...] }
    ///
    /// All operations share a single Undo group and a single MarkSceneDirty call.
    /// </summary>
    internal class GameObjectBatchHandler : IRequestHandler
    {
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "POST" &&
               request.Url.AbsolutePath == "/api/gameobjects/batch";

        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            var ops  = RequestBodyReader.GetArray(body, "operations");

            if (ops.Count == 0)
            {
                RestResponse.SendError(response, "Missing or empty 'operations' array", 400);
                return;
            }

            Undo.SetCurrentGroupName("UnionAir: Batch GameObject Operations");
            var group = Undo.GetCurrentGroup();

            var results  = new List<(int index, bool success, string pathOrError)>();
            int failures = 0;

            for (int i = 0; i < ops.Count; i++)
            {
                var op = ops[i];
                var opName = RequestBodyReader.GetString(op, "op");

                try
                {
                    switch (opName)
                    {
                        case "create":
                            results.Add((i, true, ExecuteCreate(op)));
                            break;
                        case "create_primitive":
                            results.Add((i, true, ExecuteCreatePrimitive(op)));
                            break;
                        case "update":
                            results.Add((i, true, ExecuteUpdate(op)));
                            break;
                        case "delete":
                            results.Add((i, true, ExecuteDelete(op)));
                            break;
                        default:
                            failures++;
                            results.Add((i, false, $"Unknown op: {opName ?? "(null)"}"));
                            break;
                    }
                }
                catch (Exception ex)
                {
                    failures++;
                    results.Add((i, false, ex.Message));
                }
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"processed\":{ops.Count},");
            sb.Append($"\"failed\":{failures},");
            sb.Append("\"results\":[");
            for (int i = 0; i < results.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var (idx, success, value) = results[i];
                sb.Append("{");
                sb.Append($"\"index\":{idx},");
                sb.Append($"\"success\":{(success ? "true" : "false")},");
                sb.Append(success
                    ? $"\"path\":\"{RestResponse.EscapeJson(value)}\""
                    : $"\"error\":\"{RestResponse.EscapeJson(value)}\"");
                sb.Append("}");
            }
            sb.Append("]}");

            RestResponse.Send(response, sb.ToString(), 207); // 207 Multi-Status
        }

        // ── Operation implementations ────────────────────────────────────────────

        private static string ExecuteCreate(string op)
        {
            var name = RequestBodyReader.GetString(op, "name");
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Missing required field: name");

            var parentPath = RequestBodyReader.GetString(op, "parentPath");

            GameObject go;
            if (!string.IsNullOrEmpty(parentPath))
            {
                var parent = GameObjectUtils.FindByPath(parentPath)
                    ?? throw new ArgumentException($"Parent not found: {parentPath}");
                go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "UnionAir: Batch Create");
                go.transform.SetParent(parent.transform, false);
            }
            else
            {
                go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "UnionAir: Batch Create");
            }

            ApplyTransformFromOp(go.transform, op);
            return GameObjectUtils.GetPath(go);
        }

        private static string ExecuteCreatePrimitive(string op)
        {
            var typeName = RequestBodyReader.GetString(op, "type");
            if (string.IsNullOrEmpty(typeName)) throw new ArgumentException("Missing required field: type");

            if (!Enum.TryParse<PrimitiveType>(typeName, true, out var primitiveType))
                throw new ArgumentException($"Unknown primitive type: {typeName}");

            var name       = RequestBodyReader.GetString(op, "name");
            var parentPath = RequestBodyReader.GetString(op, "parentPath");

            var go = GameObject.CreatePrimitive(primitiveType);
            Undo.RegisterCreatedObjectUndo(go, "UnionAir: Batch CreatePrimitive");

            if (!string.IsNullOrEmpty(name))
                go.name = name;

            if (!string.IsNullOrEmpty(parentPath))
            {
                var parent = GameObjectUtils.FindByPath(parentPath);
                if (parent == null)
                {
                    Undo.DestroyObjectImmediate(go);
                    throw new ArgumentException($"Parent not found: {parentPath}");
                }
                go.transform.SetParent(parent.transform, false);
            }

            ApplyTransformFromOp(go.transform, op);
            return GameObjectUtils.GetPath(go);
        }

        private static string ExecuteUpdate(string op)
        {
            var path = RequestBodyReader.GetString(op, "path");
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Missing required field: path");

            var go = GameObjectUtils.FindByPath(path)
                ?? throw new ArgumentException($"GameObject not found: {path}");

            Undo.RecordObject(go, "UnionAir: Batch Update");
            Undo.RecordObject(go.transform, "UnionAir: Batch Update");

            var newName = RequestBodyReader.GetString(op, "name");
            if (newName != null) go.name = newName;

            var isActive = RequestBodyReader.GetBool(op, "isActive");
            if (isActive.HasValue) go.SetActive(isActive.Value);

            var tag = RequestBodyReader.GetString(op, "tag");
            if (tag != null) { try { go.tag = tag; } catch { /* ignore invalid tag */ } }

            var layer = RequestBodyReader.GetInt(op, "layer");
            if (layer.HasValue) go.layer = layer.Value;

            ApplyTransformFromOp(go.transform, op);
            return GameObjectUtils.GetPath(go);
        }

        private static string ExecuteDelete(string op)
        {
            var path = RequestBodyReader.GetString(op, "path");
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Missing required field: path");

            var go = GameObjectUtils.FindByPath(path)
                ?? throw new ArgumentException($"GameObject not found: {path}");

            var deletedPath = GameObjectUtils.GetPath(go);
            Undo.DestroyObjectImmediate(go);
            return deletedPath;
        }

        private static void ApplyTransformFromOp(Transform t, string op)
        {
            var transformJson = RequestBodyReader.GetObject(op, "transform");
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

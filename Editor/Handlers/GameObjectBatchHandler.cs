using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// POST /api/gameobjects/batch
    /// Processes multiple GameObject operations in a single request.
    ///
    /// Body:
    /// {
    ///   "operations": [
    ///     {"op":"create",           "name":"Empty",   "parent":{"value":"Parent"}},
    ///     {"op":"create_primitive", "type":"Cube",    "name":"Cube_0", "parent":{"value":"Map"},
    ///      "transform":{"position":{"x":0,"y":0,"z":0}}},
    ///     {"op":"update",           "target":{"value":"Obj"}, "isActive":false,
    ///      "transform":{"position":{"x":1,"y":0,"z":0}}},
    ///     {"op":"delete",           "target":{"value":"OldObj"}}
    ///   ]
    /// }
    ///
    /// Response:
    /// { "processed": 4, "failed": 0,
    ///   "results": [{"index":0,"success":true,"path":"Empty"}, ...] }
    ///
    /// All operations share a single Undo group and a single MarkSceneDirty call.
    /// </summary>
    internal class GameObjectBatchHandler
    {
        public void Handle(UnionAirRequest request, UnionAirResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            var ops  = RequestBodyReader.GetArray(body, "operations");
            var defaultScenePath = RequestBodyReader.GetString(body, "scenePath");

            if (ops.Count == 0)
            {
                RestResponse.SendError(response, "Missing or empty 'operations' array", 400);
                return;
            }

            var useUndo = !EditorApplication.isPlaying;
            var group = -1;
            if (useUndo)
            {
                group = UndoGroups.Begin("UnionAir: Batch GameObject Operations");
            }

            var results  = new List<(int index, bool success, string pathOrError, string globalObjectId)>();
            var dirtyScenes = new List<Scene>();
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
                            var createResult = ExecuteCreate(op, defaultScenePath, dirtyScenes);
                            results.Add((i, true, createResult.path, createResult.globalObjectId));
                            break;
                        case "create_primitive":
                            var primitiveResult = ExecuteCreatePrimitive(op, defaultScenePath, dirtyScenes);
                            results.Add((i, true, primitiveResult.path, primitiveResult.globalObjectId));
                            break;
                        case "update":
                            var updateResult = ExecuteUpdate(op, defaultScenePath, dirtyScenes);
                            results.Add((i, true, updateResult.path, updateResult.globalObjectId));
                            break;
                        case "delete":
                            var deleteResult = ExecuteDelete(op, defaultScenePath, dirtyScenes);
                            results.Add((i, true, deleteResult.path, deleteResult.globalObjectId));
                            break;
                        default:
                            failures++;
                            results.Add((i, false, $"Unknown op: {opName ?? "(null)"}", null));
                            break;
                    }
                }
                catch (Exception ex)
                {
                    failures++;
                    results.Add((i, false, ex.Message, null));
                }
            }

            if (useUndo)
                Undo.CollapseUndoOperations(group);
            MarkDirtyScenes(dirtyScenes);

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"processed\":{ops.Count},");
            sb.Append($"\"failed\":{failures},");
            sb.Append("\"results\":[");
            for (int i = 0; i < results.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var (idx, success, value, globalObjectId) = results[i];
                sb.Append("{");
                sb.Append($"\"index\":{idx},");
                sb.Append($"\"success\":{(success ? "true" : "false")},");
                if (success)
                {
                    sb.Append($"\"path\":\"{RestResponse.EscapeJson(value)}\",");
                    sb.Append($"\"globalObjectId\":\"{RestResponse.EscapeJson(globalObjectId)}\"");
                }
                else
                {
                    sb.Append($"\"error\":\"{RestResponse.EscapeJson(value)}\"");
                }
                sb.Append("}");
            }
            sb.Append("]}");

            RestResponse.Send(response, sb.ToString(), 207); // 207 Multi-Status
        }

        // ── Operation implementations ────────────────────────────────────────────

        private static (string path, string globalObjectId) ExecuteCreate(string op, string defaultScenePath, List<Scene> dirtyScenes)
        {
            var scene = ResolveSceneForOp(op, defaultScenePath);
            var name = RequestBodyReader.GetString(op, "name");
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Missing required field: name");

            GameObject go;
            var parentJson = RequestBodyReader.GetObject(op, "parent");
            if (!string.IsNullOrEmpty(parentJson))
            {
                if (!ObjectRefUtils.TryParse(parentJson, "parent", out var parentRef, out var error, out _) ||
                    !ObjectRefUtils.TryResolveGameObject(scene, parentRef, "parent", out var parent, out error, out _))
                    throw new ArgumentException(error);

                scene = parent.scene;
                go = new GameObject(name);
                if (!EditorApplication.isPlaying)
                    Undo.RegisterCreatedObjectUndo(go, "UnionAir: Batch Create");
                go.transform.SetParent(parent.transform, false);
            }
            else
            {
                go = new GameObject(name);
                if (!EditorApplication.isPlaying)
                    Undo.RegisterCreatedObjectUndo(go, "UnionAir: Batch Create");
                SceneManager.MoveGameObjectToScene(go, scene);
            }

            ApplyTransformFromOp(go.transform, op);
            AddDirtyScene(dirtyScenes, scene);
            return (GameObjectUtils.GetPath(go), ObjectIdUtils.GetGlobalObjectId(go));
        }

        private static (string path, string globalObjectId) ExecuteCreatePrimitive(string op, string defaultScenePath, List<Scene> dirtyScenes)
        {
            var scene = ResolveSceneForOp(op, defaultScenePath);
            var typeName = RequestBodyReader.GetString(op, "type");
            if (string.IsNullOrEmpty(typeName)) throw new ArgumentException("Missing required field: type");

            if (!Enum.TryParse<PrimitiveType>(typeName, true, out var primitiveType))
                throw new ArgumentException($"Unknown primitive type: {typeName}");

            var name       = RequestBodyReader.GetString(op, "name");
            var go = GameObject.CreatePrimitive(primitiveType);
            if (!EditorApplication.isPlaying)
                Undo.RegisterCreatedObjectUndo(go, "UnionAir: Batch CreatePrimitive");
            SceneManager.MoveGameObjectToScene(go, scene);

            if (!string.IsNullOrEmpty(name))
                go.name = name;

            var parentJson = RequestBodyReader.GetObject(op, "parent");
            if (!string.IsNullOrEmpty(parentJson))
            {
                if (!ObjectRefUtils.TryParse(parentJson, "parent", out var parentRef, out var error, out _) ||
                    !ObjectRefUtils.TryResolveGameObject(scene, parentRef, "parent", out var parent, out error, out _))
                {
                    if (EditorApplication.isPlaying)
                        UnityEngine.Object.Destroy(go);
                    else
                        Undo.DestroyObjectImmediate(go);
                    throw new ArgumentException(error);
                }
                scene = parent.scene;
                go.transform.SetParent(parent.transform, false);
            }

            ApplyTransformFromOp(go.transform, op);
            AddDirtyScene(dirtyScenes, scene);
            return (GameObjectUtils.GetPath(go), ObjectIdUtils.GetGlobalObjectId(go));
        }

        private static (string path, string globalObjectId) ExecuteUpdate(string op, string defaultScenePath, List<Scene> dirtyScenes)
        {
            var scene = ResolveSceneForOp(op, defaultScenePath);

            if (!ObjectRefUtils.TryReadBody(op, "target", out var target, out var error, out _) ||
                !ObjectRefUtils.TryResolveGameObject(scene, target, "target", out var go, out error, out _))
                throw new ArgumentException(error);
            scene = go.scene;

            if (!EditorApplication.isPlaying)
            {
                Undo.RecordObject(go, "UnionAir: Batch Update");
                Undo.RecordObject(go.transform, "UnionAir: Batch Update");
            }

            var newName = RequestBodyReader.GetString(op, "name");
            if (newName != null) go.name = newName;

            var isActive = RequestBodyReader.GetBool(op, "isActive");
            if (isActive.HasValue) go.SetActive(isActive.Value);

            var tag = RequestBodyReader.GetString(op, "tag");
            if (tag != null) { try { go.tag = tag; } catch { /* ignore invalid tag */ } }

            var layer = RequestBodyReader.GetInt(op, "layer");
            if (layer.HasValue) go.layer = layer.Value;

            ApplyTransformFromOp(go.transform, op);
            AddDirtyScene(dirtyScenes, scene);
            return (GameObjectUtils.GetPath(go), ObjectIdUtils.GetGlobalObjectId(go));
        }

        private static (string path, string globalObjectId) ExecuteDelete(string op, string defaultScenePath, List<Scene> dirtyScenes)
        {
            var scene = ResolveSceneForOp(op, defaultScenePath);

            if (!ObjectRefUtils.TryReadBody(op, "target", out var target, out var error, out _) ||
                !ObjectRefUtils.TryResolveGameObject(scene, target, "target", out var go, out error, out _))
                throw new ArgumentException(error);
            scene = go.scene;

            var deletedPath = GameObjectUtils.GetPath(go);
            var deletedId = ObjectIdUtils.GetGlobalObjectId(go);
            if (EditorApplication.isPlaying)
                UnityEngine.Object.Destroy(go);
            else
                Undo.DestroyObjectImmediate(go);
            AddDirtyScene(dirtyScenes, scene);
            return (deletedPath, deletedId);
        }

        private static Scene ResolveSceneForOp(string op, string defaultScenePath)
        {
            var scenePath = RequestBodyReader.GetString(op, "scenePath") ?? defaultScenePath;
            if (string.IsNullOrEmpty(scenePath))
                return EditorSceneManager.GetActiveScene();

            var status = SceneResolver.ResolveLoaded(scenePath, out var scene, out var error);
            if (status == ResolveStatus.Found) return scene;

            throw new ArgumentException(error);
        }

        private static void AddDirtyScene(List<Scene> dirtyScenes, Scene scene)
        {
            if (!dirtyScenes.Contains(scene))
                dirtyScenes.Add(scene);
        }

        private static void MarkDirtyScenes(List<Scene> dirtyScenes)
        {
            foreach (var scene in dirtyScenes)
                SceneUtils.MarkDirtyUnlessPlaying(scene);
        }

        private static void ApplyTransformFromOp(Transform t, string op)
            => GameObjectUtils.ApplyTransformFromBody(t, op);
    }
}

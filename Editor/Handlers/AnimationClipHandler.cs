using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles AnimationClip asset operations:
    ///   POST   /api/assets/animation-clips                    — create
    ///   GET    /api/assets/animation-clips/{guid}             — read
    ///   POST   /api/assets/animation-clips/{guid}/curves      — add/replace float curves and object reference curves
    ///   DELETE /api/assets/animation-clips/{guid}/curves      — remove curves
    /// </summary>
    internal class AnimationClipHandler
    {
        public void HandleCreate(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            var assetPath = RequestBodyReader.GetString(body, "assetPath");
            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendError(response, "Missing required field: assetPath", 400);
                return;
            }
            if (!assetPath.EndsWith(".anim"))
            {
                RestResponse.SendError(response, "assetPath must end with .anim", 400);
                return;
            }

            AssetUtils.EnsureDirectory(System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/'));

            var clip = new AnimationClip();

            var frameRate = RequestBodyReader.GetFloat(body, "frameRate");
            if (frameRate.HasValue) clip.frameRate = frameRate.Value;

            var wrapModeStr = RequestBodyReader.GetString(body, "wrapMode");
            if (!string.IsNullOrEmpty(wrapModeStr))
                clip.wrapMode = ParseWrapMode(wrapModeStr);

            AssetDatabase.CreateAsset(clip, assetPath);
            AssetDatabase.SaveAssets();

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            RestResponse.Send(response,
                $"{{\"assetPath\":\"{RestResponse.EscapeJson(assetPath)}\",\"guid\":\"{RestResponse.EscapeJson(guid)}\"," +
                $"\"frameRate\":{RestResponse.FormatFloat(clip.frameRate)},\"length\":{RestResponse.FormatFloat(clip.length)}}}",
                201);
        }

        public void HandleRead(HttpListenerRequest request, HttpListenerResponse response, string guid)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendNotFound(response, $"No asset found for GUID: {guid}");
                return;
            }

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (clip == null)
            {
                RestResponse.SendError(response, $"Asset is not an AnimationClip: {assetPath}", 400);
                return;
            }

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"assetPath\":\"{RestResponse.EscapeJson(assetPath)}\",");
            sb.Append($"\"guid\":\"{RestResponse.EscapeJson(guid)}\",");
            sb.Append($"\"frameRate\":{RestResponse.FormatFloat(clip.frameRate)},");
            sb.Append($"\"length\":{RestResponse.FormatFloat(clip.length)},");
            sb.Append($"\"wrapMode\":\"{clip.wrapMode}\",");

            // Float curves
            var floatBindings = AnimationUtility.GetCurveBindings(clip);
            sb.Append($"\"curveCount\":{floatBindings.Length},");
            sb.Append("\"curves\":[");
            for (int i = 0; i < floatBindings.Length; i++)
            {
                if (i > 0) sb.Append(",");
                var b = floatBindings[i];
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                sb.Append("{");
                sb.Append($"\"relativePath\":\"{RestResponse.EscapeJson(b.path)}\",");
                sb.Append($"\"type\":\"{RestResponse.EscapeJson(b.type?.Name ?? "")}\",");
                sb.Append($"\"property\":\"{RestResponse.EscapeJson(b.propertyName)}\",");
                sb.Append($"\"keyCount\":{(curve != null ? curve.keys.Length : 0)},");
                sb.Append("\"keys\":[");
                if (curve != null)
                {
                    for (int k = 0; k < curve.keys.Length; k++)
                    {
                        if (k > 0) sb.Append(",");
                        var key = curve.keys[k];
                        sb.Append("{");
                        sb.Append($"\"time\":{RestResponse.FormatFloat(key.time)},");
                        sb.Append($"\"value\":{RestResponse.FormatFloat(key.value)},");
                        sb.Append($"\"inTangent\":{RestResponse.FormatFloat(key.inTangent)},");
                        sb.Append($"\"outTangent\":{RestResponse.FormatFloat(key.outTangent)}");
                        sb.Append("}");
                    }
                }
                sb.Append("]}");
            }
            sb.Append("],");

            // Object reference curves
            var pptrBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            sb.Append($"\"objectReferenceCurveCount\":{pptrBindings.Length},");
            sb.Append("\"objectReferenceCurves\":[");
            for (int i = 0; i < pptrBindings.Length; i++)
            {
                if (i > 0) sb.Append(",");
                var b = pptrBindings[i];
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, b);
                sb.Append("{");
                sb.Append($"\"relativePath\":\"{RestResponse.EscapeJson(b.path)}\",");
                sb.Append($"\"type\":\"{RestResponse.EscapeJson(b.type?.Name ?? "")}\",");
                sb.Append($"\"property\":\"{RestResponse.EscapeJson(b.propertyName)}\",");
                sb.Append("\"keys\":[");
                for (int k = 0; k < keys.Length; k++)
                {
                    if (k > 0) sb.Append(",");
                    var key = keys[k];
                    sb.Append("{");
                    sb.Append($"\"time\":{RestResponse.FormatFloat(key.time)},");
                    if (key.value != null)
                    {
                        var refPath = AssetDatabase.GetAssetPath(key.value);
                        var refGuid = AssetDatabase.AssetPathToGUID(refPath);
                        sb.Append($"\"guid\":\"{RestResponse.EscapeJson(refGuid)}\",");
                        sb.Append($"\"name\":\"{RestResponse.EscapeJson(key.value.name)}\"");
                    }
                    else
                    {
                        sb.Append("\"guid\":null,\"name\":null");
                    }
                    sb.Append("}");
                }
                sb.Append("]}");
            }
            sb.Append("]}");
            RestResponse.Send(response, sb.ToString());
        }

        public void HandleAddCurves(HttpListenerRequest request, HttpListenerResponse response, string guid)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendNotFound(response, $"No asset found for GUID: {guid}");
                return;
            }

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (clip == null)
            {
                RestResponse.SendError(response, $"Asset is not an AnimationClip: {assetPath}", 400);
                return;
            }

            var body = RequestBodyReader.ReadString(request);
            var addedFloat = new List<string>();
            var addedRef = new List<string>();
            var errors = new List<string>();

            // Float curves
            var curves = RequestBodyReader.GetArray(body, "curves");
            foreach (var curveJson in curves)
            {
                var relativePath = RequestBodyReader.GetString(curveJson, "relativePath") ?? "";
                var typeName = RequestBodyReader.GetString(curveJson, "type") ?? "Transform";
                var property = RequestBodyReader.GetString(curveJson, "property");

                if (string.IsNullOrEmpty(property)) { errors.Add("Curve missing required field: property"); continue; }

                var bindingType = ResolveType(typeName);
                if (bindingType == null) { errors.Add($"Unknown type: {typeName}"); continue; }

                var keys = RequestBodyReader.GetArray(curveJson, "keys");
                if (keys == null || keys.Count == 0) { errors.Add($"Curve '{property}' missing required field: keys"); continue; }

                var keyList = new List<Keyframe>();
                foreach (var keyJson in keys)
                {
                    var time = RequestBodyReader.GetFloat(keyJson, "time") ?? 0f;
                    var value = RequestBodyReader.GetFloat(keyJson, "value") ?? 0f;
                    var inTangent = RequestBodyReader.GetFloat(keyJson, "inTangent") ?? 0f;
                    var outTangent = RequestBodyReader.GetFloat(keyJson, "outTangent") ?? 0f;
                    keyList.Add(new Keyframe(time, value, inTangent, outTangent));
                }

                clip.SetCurve(relativePath, bindingType, property, new AnimationCurve(keyList.ToArray()));
                addedFloat.Add(property);
            }

            // Object reference curves
            var refCurves = RequestBodyReader.GetArray(body, "objectReferenceCurves");
            foreach (var curveJson in refCurves)
            {
                var relativePath = RequestBodyReader.GetString(curveJson, "relativePath") ?? "";
                var typeName = RequestBodyReader.GetString(curveJson, "type") ?? "Transform";
                var property = RequestBodyReader.GetString(curveJson, "property");

                if (string.IsNullOrEmpty(property)) { errors.Add("ObjectReferenceCurve missing required field: property"); continue; }

                var bindingType = ResolveType(typeName);
                if (bindingType == null) { errors.Add($"Unknown type: {typeName}"); continue; }

                var keys = RequestBodyReader.GetArray(curveJson, "keys");
                if (keys == null || keys.Count == 0) { errors.Add($"ObjectReferenceCurve '{property}' missing required field: keys"); continue; }

                var keyList = new List<ObjectReferenceKeyframe>();
                foreach (var keyJson in keys)
                {
                    var time = RequestBodyReader.GetFloat(keyJson, "time") ?? 0f;
                    var refGuid = RequestBodyReader.GetString(keyJson, "guid");

                    UnityEngine.Object refValue = null;
                    if (!string.IsNullOrEmpty(refGuid))
                    {
                        var refPath = AssetDatabase.GUIDToAssetPath(refGuid);
                        if (!string.IsNullOrEmpty(refPath))
                        {
                            // Prefer Sprite sub-asset over Texture2D main asset for m_Sprite curves.
                            // LoadMainAssetAtPath returns Texture2D for Sprite-mode PNGs, causing type mismatch.
                            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(refPath);
                            refValue = sprite != null
                                ? (UnityEngine.Object)sprite
                                : AssetDatabase.LoadMainAssetAtPath(refPath);
                        }
                    }

                    keyList.Add(new ObjectReferenceKeyframe { time = time, value = refValue });
                }

                var binding = EditorCurveBinding.PPtrCurve(relativePath, bindingType, property);
                AnimationUtility.SetObjectReferenceCurve(clip, binding, keyList.ToArray());
                addedRef.Add(property);
            }

            if (addedFloat.Count > 0 || addedRef.Count > 0)
            {
                EditorUtility.SetDirty(clip);
                AssetDatabase.SaveAssets();
            }

            var sb = new StringBuilder();
            sb.Append("{\"added\":[");
            var allAdded = new List<string>(addedFloat);
            allAdded.AddRange(addedRef);
            for (int i = 0; i < allAdded.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{RestResponse.EscapeJson(allAdded[i])}\"");
            }
            sb.Append("],\"addedFloat\":[");
            for (int i = 0; i < addedFloat.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{RestResponse.EscapeJson(addedFloat[i])}\"");
            }
            sb.Append("],\"addedObjectReference\":[");
            for (int i = 0; i < addedRef.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{RestResponse.EscapeJson(addedRef[i])}\"");
            }
            sb.Append("],\"errors\":[");
            for (int i = 0; i < errors.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{RestResponse.EscapeJson(errors[i])}\"");
            }
            sb.Append("]}");
            RestResponse.Send(response, sb.ToString(), errors.Count > 0 && allAdded.Count == 0 ? 400 : 200);
        }

        public void HandleDeleteCurves(HttpListenerRequest request, HttpListenerResponse response, string guid)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendNotFound(response, $"No asset found for GUID: {guid}");
                return;
            }

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (clip == null)
            {
                RestResponse.SendError(response, $"Asset is not an AnimationClip: {assetPath}", 400);
                return;
            }

            var body = RequestBodyReader.ReadString(request);
            var bindings = RequestBodyReader.GetArray(body, "bindings");
            if (bindings == null || bindings.Count == 0)
            {
                RestResponse.SendError(response, "Missing required field: bindings", 400);
                return;
            }

            var removed = new List<string>();
            var errors = new List<string>();

            var pptrBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);

            foreach (var bindingJson in bindings)
            {
                var relativePath = RequestBodyReader.GetString(bindingJson, "relativePath") ?? "";
                var typeName = RequestBodyReader.GetString(bindingJson, "type") ?? "Transform";
                var property = RequestBodyReader.GetString(bindingJson, "property");

                if (string.IsNullOrEmpty(property)) { errors.Add("Binding missing required field: property"); continue; }

                var bindingType = ResolveType(typeName);
                if (bindingType == null) { errors.Add($"Unknown type: {typeName}"); continue; }

                bool isPPtr = false;
                foreach (var b in pptrBindings)
                {
                    if (b.path == relativePath && b.type == bindingType && b.propertyName == property)
                    {
                        isPPtr = true;
                        break;
                    }
                }

                if (isPPtr)
                    AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve(relativePath, bindingType, property), null);
                else
                    clip.SetCurve(relativePath, bindingType, property, null);

                removed.Add(property);
            }

            if (removed.Count > 0)
            {
                EditorUtility.SetDirty(clip);
                AssetDatabase.SaveAssets();
            }

            var sb = new StringBuilder();
            sb.Append("{\"removed\":[");
            for (int i = 0; i < removed.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{RestResponse.EscapeJson(removed[i])}\"");
            }
            sb.Append("],\"errors\":[");
            for (int i = 0; i < errors.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{RestResponse.EscapeJson(errors[i])}\"");
            }
            sb.Append("]}");
            RestResponse.Send(response, sb.ToString());
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static WrapMode ParseWrapMode(string s)
        {
            switch (s.ToLowerInvariant())
            {
                case "once":         return WrapMode.Once;
                case "loop":         return WrapMode.Loop;
                case "pingpong":     return WrapMode.PingPong;
                case "clampforever": return WrapMode.ClampForever;
                default:             return WrapMode.Default;
            }
        }

        internal static Type ResolveType(string typeName)
        {
            switch (typeName)
            {
                // Core Unity types
                case "Transform":               return typeof(Transform);
                case "Animator":                return typeof(Animator);
                case "SkinnedMeshRenderer":     return typeof(SkinnedMeshRenderer);
                case "MeshRenderer":            return typeof(MeshRenderer);
                case "Light":                   return typeof(Light);
                case "Camera":                  return typeof(Camera);
                case "AudioSource":             return typeof(AudioSource);
                case "SpriteRenderer":          return typeof(SpriteRenderer);
                case "RectTransform":           return typeof(RectTransform);
                case "CanvasGroup":             return typeof(CanvasGroup);
                case "GameObject":              return typeof(GameObject);
                // UI types (short names)
                case "Image":                   return typeof(UnityEngine.UI.Image);
                case "RawImage":                return typeof(UnityEngine.UI.RawImage);
                case "Text":                    return typeof(UnityEngine.UI.Text);
                case "Button":                  return typeof(UnityEngine.UI.Button);
                case "Slider":                  return typeof(UnityEngine.UI.Slider);
                case "CanvasRenderer":          return typeof(CanvasRenderer);
                // UI types (fully qualified)
                case "UnityEngine.UI.Image":    return typeof(UnityEngine.UI.Image);
                case "UnityEngine.UI.RawImage": return typeof(UnityEngine.UI.RawImage);
                case "UnityEngine.UI.Text":     return typeof(UnityEngine.UI.Text);
                case "UnityEngine.UI.Button":   return typeof(UnityEngine.UI.Button);
                case "UnityEngine.UI.Slider":   return typeof(UnityEngine.UI.Slider);
            }
            // Fallback: fully qualified name resolution
            return Type.GetType(typeName) ??
                   Type.GetType("UnityEngine." + typeName + ", UnityEngine") ??
                   Type.GetType("UnityEngine." + typeName + ", UnityEngine.CoreModule") ??
                   Type.GetType("UnityEngine.UI." + typeName + ", UnityEngine.UI");
        }
    }
}

using System;
using System.Collections.Generic;
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
        public void HandleCreate(UnionAirRequest request, UnionAirResponse response)
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

        public void HandleRead(UnionAirRequest request, UnionAirResponse response, string guid)
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

        public void HandleAddCurves(UnionAirRequest request, UnionAirResponse response, string guid)
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

        public void HandleDeleteCurves(UnionAirRequest request, UnionAirResponse response, string guid)
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

            var floatBindings = AnimationUtility.GetCurveBindings(clip);
            var pptrBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);

            // Resolve every requested binding against what the clip actually holds before
            // touching anything, so a name that matches nothing is reported rather than
            // counted as a removal.
            var targets = new List<EditorCurveBinding>();
            var targetIsPPtr = new List<bool>();

            // A binding names one curve, so listing it twice in one request is one
            // removal, not two. Without this the second entry matched the same binding,
            // ran a removal that did nothing, and confirmed it absent -- reporting the
            // same name twice under "removed", which is the response describing the
            // request again rather than the result.
            var seen = new List<string>();

            foreach (var bindingJson in bindings)
            {
                var relativePath = RequestBodyReader.GetString(bindingJson, "relativePath") ?? "";
                var typeName = RequestBodyReader.GetString(bindingJson, "type") ?? "Transform";
                var property = RequestBodyReader.GetString(bindingJson, "property");

                if (string.IsNullOrEmpty(property)) { errors.Add("Binding missing required field: property"); continue; }

                var bindingType = ResolveType(typeName);
                if (bindingType == null) { errors.Add($"Unknown type: {typeName}"); continue; }

                // Deduplicated after the type resolves rather than on the raw request
                // text, so two spellings of one type -- "Image" and "UnityEngine.UI.Image"
                // -- count as one binding. Applied to failures too: a name that matches
                // nothing is reported once, however many times it was asked for.
                var key = BindingKey(relativePath, bindingType, property);
                if (seen.Contains(key)) continue;
                seen.Add(key);

                if (TryFindBinding(floatBindings, relativePath, bindingType, property, out var floatMatch))
                {
                    targets.Add(floatMatch);
                    targetIsPPtr.Add(false);
                    continue;
                }

                if (TryFindBinding(pptrBindings, relativePath, bindingType, property, out var pptrMatch))
                {
                    targets.Add(pptrMatch);
                    targetIsPPtr.Add(true);
                    continue;
                }

                // The property name a client writes is not always the one the clip stores:
                // POST accepts "localPosition.y" and Unity expands it to the serialized
                // "m_LocalPosition.x/.y/.z". GET reports the serialized names, so those are
                // what DELETE addresses. Name the alternatives rather than failing blankly.
                errors.Add(
                    $"No curve bound to '{property}' on '{relativePath}' ({typeName}). " +
                    $"Bindings there: {DescribeBindingsAt(floatBindings, pptrBindings, relativePath, bindingType)}");
            }

            for (int i = 0; i < targets.Count; i++)
            {
                // AnimationClip.SetCurve with a null curve does not remove a binding; only
                // the AnimationUtility form does.
                if (targetIsPPtr[i])
                    AnimationUtility.SetObjectReferenceCurve(clip, targets[i], null);
                else
                    AnimationUtility.SetEditorCurve(clip, targets[i], null);
            }

            if (targets.Count > 0)
            {
                EditorUtility.SetDirty(clip);
                AssetDatabase.SaveAssets();
            }

            // Report the outcome, not the intent. A binding still present afterwards is a
            // failure however confidently the removal was attempted.
            var remainingFloat = AnimationUtility.GetCurveBindings(clip);
            var remainingPPtr = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                var stillThere = targetIsPPtr[i]
                    ? TryFindBinding(remainingPPtr, t.path, t.type, t.propertyName, out _)
                    : TryFindBinding(remainingFloat, t.path, t.type, t.propertyName, out _);

                if (stillThere)
                    errors.Add($"Failed to remove '{t.propertyName}' on '{t.path}'.");
                else
                    removed.Add(t.propertyName);
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
            RestResponse.Send(response, sb.ToString(), errors.Count > 0 && removed.Count == 0 ? 400 : 200);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Identity of a binding within one request, for detecting a repeated entry.
        /// Uses the resolved type rather than the name the request spelled it with.
        /// </summary>
        internal static string BindingKey(string path, Type type, string property)
            => $"{path}\n{type.FullName}\n{property}";

        /// <summary>
        /// Finds the binding on the clip matching a path, type, and serialized property name.
        /// </summary>
        internal static bool TryFindBinding(EditorCurveBinding[] bindings, string path, Type type, string property, out EditorCurveBinding match)
        {
            foreach (var b in bindings)
            {
                if (b.path == path && b.type == type && b.propertyName == property)
                {
                    match = b;
                    return true;
                }
            }
            match = default(EditorCurveBinding);
            return false;
        }

        /// <summary>
        /// Lists the serialized property names bound at a path and type, for an error that
        /// tells the caller what it could have asked for.
        /// </summary>
        internal static string DescribeBindingsAt(EditorCurveBinding[] floatBindings, EditorCurveBinding[] pptrBindings, string path, Type type)
        {
            var names = new List<string>();
            foreach (var b in floatBindings)
                if (b.path == path && b.type == type) names.Add(b.propertyName);
            foreach (var b in pptrBindings)
                if (b.path == path && b.type == type) names.Add(b.propertyName);

            return names.Count == 0 ? "none" : string.Join(", ", names.ToArray());
        }

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

        /// <summary>
        /// Resolves the <c>type</c> of a curve binding to a Type.
        ///
        /// This used to be a hand-written switch of about twenty names with a fallback
        /// that prepended <c>UnityEngine.</c> to whatever it was given -- so a name that
        /// already carried the namespace became <c>UnityEngine.UnityEngine.Transform</c>
        /// and resolved to nothing. Every endpoint outside this file already shared
        /// <see cref="ObjectRefUtils.ResolveType"/>, which handles both spellings.
        ///
        /// The base type argument is load-bearing rather than a tightening. The shared
        /// resolver falls back to matching on simple name across every loaded assembly
        /// and returns the first hit, and measured on 6000.0.80f1 the first hit for the
        /// short UI names is the wrong type: <c>Image</c> reaches
        /// <c>UnityEngine.UIElements.Image</c>, <c>Slider</c> reaches
        /// <c>UnityEngine.UIElements.Slider</c>, <c>Button</c> reaches
        /// <c>UnityEngine.InputForUI.PointerEvent+Button</c>, and <c>Text</c> reaches
        /// <c>System.Net.Mime.MediaTypeNames+Text</c>. None of those derive from
        /// <see cref="UnityEngine.Object"/>, so requiring that base type skips them and
        /// lands on the <c>UnityEngine.UI</c> types the switch named explicitly.
        ///
        /// <see cref="UnityEngine.Object"/> rather than <c>Component</c>, because
        /// <c>GameObject</c> is a binding type here and is not a Component.
        /// </summary>
        internal static Type ResolveType(string typeName)
            => ObjectRefUtils.ResolveType(typeName, typeof(UnityEngine.Object));
    }
}

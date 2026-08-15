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

            var imported = AnimationClipOwnership.IsImported(assetPath, out var importerType);
            var clipNames = AnimationClipOwnership.ClipNamesAt(assetPath);

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"assetPath\":\"{RestResponse.EscapeJson(assetPath)}\",");
            sb.Append($"\"guid\":\"{RestResponse.EscapeJson(guid)}\",");

            // The clip's own name, which assetPath and guid do not give: both identify the
            // file, and inside an .fbx that file holds the takes rather than being one.
            sb.Append("\"name\":").Append(RestResponse.FormatNullableString(clip.name)).Append(",");

            // LoadAssetAtPath returns whichever clip the importer lists first, so a path
            // holding several exposes one by GUID and hides the rest. Addressing an
            // individual one is a sub-asset problem this endpoint does not solve; saying
            // that the others exist is what keeps it from presenting one as the whole.
            sb.Append($"\"clipsAtPath\":{clipNames.Length},");
            sb.Append("\"clipNames\":[");
            for (int i = 0; i < clipNames.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(RestResponse.FormatNullableString(clipNames[i]));
            }
            sb.Append("],");

            sb.Append($"\"imported\":{RestResponse.FormatBool(imported)},");
            sb.Append("\"importer\":").Append(RestResponse.FormatNullableString(importerType)).Append(",");
            sb.Append($"\"writable\":{RestResponse.FormatBool(!imported)},");

            sb.Append($"\"frameRate\":{RestResponse.FormatFloat(clip.frameRate)},");
            sb.Append($"\"length\":{RestResponse.FormatFloat(clip.length)},");

            // A WrapMode on the clip object, and not the answer to "does this loop" -- that
            // is settings.loopTime, which the Inspector labels Loop Time. Reported because
            // it is real and writable, next to the settings so the two cannot be confused.
            sb.Append($"\"wrapMode\":\"{clip.wrapMode}\",");

            sb.Append("\"settings\":");
            AnimationClipSettingsJson.Append(sb, AnimationUtility.GetAnimationClipSettings(clip));
            sb.Append(",");

            sb.Append("\"events\":");
            AnimationEventJson.Append(sb, AnimationUtility.GetAnimationEvents(clip));
            sb.Append(",");

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

        // ── PATCH /api/assets/animation-clips/{guid} ─────────────────────────

        public void HandleUpdate(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            if (!TryLoadWritableClip(guid, response, out var clip, out var assetPath)) return;

            var body = RequestBodyReader.ReadString(request);
            if (!RequestBodyReader.TryValidateObjectFields(
                    body, new[] { "frameRate", "wrapMode", "settings" }, out var fieldError))
            {
                RestResponse.SendError(response, fieldError, 400);
                return;
            }

            // Everything is parsed before the first write, so a request that names one bad
            // setting leaves the clip as it was rather than partly updated.
            if (!RequestBodyReader.TryGetFloatValue(body, "frameRate", out var frameRate, out var hasFrameRate))
            {
                RestResponse.SendError(response, "frameRate must be a number.", 400);
                return;
            }
            if (hasFrameRate && frameRate <= 0f)
            {
                RestResponse.SendError(response, "frameRate must be greater than zero.", 400);
                return;
            }

            if (!RequestBodyReader.TryGetStringValue(body, "wrapMode", out var wrapModeStr, out var hasWrapMode))
            {
                RestResponse.SendError(response, "wrapMode must be a string.", 400);
                return;
            }
            var wrapMode = clip.wrapMode;
            if (hasWrapMode && !TryParseWrapMode(wrapModeStr, out wrapMode))
            {
                RestResponse.SendError(response,
                    $"Unknown wrapMode: {wrapModeStr}. Use Once, Loop, PingPong, ClampForever, or Default.", 400);
                return;
            }

            var settingsJson = RequestBodyReader.GetObject(body, "settings");
            var hasSettings = settingsJson != null;
            if (!hasSettings && RequestBodyReader.HasTopLevelField(body, "settings"))
            {
                RestResponse.SendError(response, "settings must be an object.", 400);
                return;
            }

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            var appliedSettings = new List<string>();
            if (hasSettings &&
                !AnimationClipSettingsJson.TryApply(settingsJson, settings, response, out settings, out appliedSettings))
                return;

            var applied = new List<string>();
            if (hasFrameRate) { clip.frameRate = frameRate; applied.Add("frameRate"); }
            if (hasWrapMode) { clip.wrapMode = wrapMode; applied.Add("wrapMode"); }
            if (hasSettings)
            {
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                foreach (var name in appliedSettings) applied.Add("settings." + name);
            }

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            var sb = new StringBuilder();
            sb.Append("{\"assetPath\":").Append(RestResponse.FormatNullableString(assetPath));
            sb.Append(",\"name\":").Append(RestResponse.FormatNullableString(clip.name));
            sb.Append(",\"applied\":[");
            for (int i = 0; i < applied.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(RestResponse.FormatNullableString(applied[i]));
            }
            sb.Append("],\"settings\":");
            AnimationClipSettingsJson.Append(sb, AnimationUtility.GetAnimationClipSettings(clip));
            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        // ── POST /api/assets/animation-clips/{guid}/events ───────────────────

        public void HandleSetEvents(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            if (!TryLoadWritableClip(guid, response, out var clip, out var assetPath)) return;

            var body = RequestBodyReader.ReadString(request);
            if (!RequestBodyReader.TryValidateObjectFields(body, new[] { "events" }, out var fieldError))
            {
                RestResponse.SendError(response, fieldError, 400);
                return;
            }

            if (!AnimationEventJson.TryParse(body, response, out var events, out var present)) return;
            if (!present)
            {
                RestResponse.SendError(response,
                    "Missing required field: events. The array replaces every event on the clip; " +
                    "send [] or use DELETE to clear them.", 400);
                return;
            }

            // Replaced wholesale, because that is how Unity stores them: an ordered array
            // with no identity per entry. Addressing one would mean inventing an identity
            // the format does not have.
            AnimationUtility.SetAnimationEvents(clip, events);

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            var sb = new StringBuilder();
            sb.Append("{\"assetPath\":").Append(RestResponse.FormatNullableString(assetPath));
            sb.Append(",\"eventCount\":").Append(events.Length);
            sb.Append(",\"events\":");
            AnimationEventJson.Append(sb, AnimationUtility.GetAnimationEvents(clip));
            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        // ── DELETE /api/assets/animation-clips/{guid}/events ─────────────────

        public void HandleDeleteEvents(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            if (!TryLoadWritableClip(guid, response, out var clip, out var assetPath)) return;

            var removed = AnimationUtility.GetAnimationEvents(clip).Length;
            AnimationUtility.SetAnimationEvents(clip, new AnimationEvent[0]);

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            RestResponse.Send(response,
                $"{{\"assetPath\":{RestResponse.FormatNullableString(assetPath)},\"removed\":{removed}}}");
        }

        /// <summary>
        /// Loads the clip a write addresses, and refuses it when an importer owns the clip.
        /// </summary>
        private static bool TryLoadWritableClip(
            string guid, UnionAirResponse response, out AnimationClip clip, out string assetPath)
        {
            clip = null;
            assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendNotFound(response, $"No asset found for GUID: {guid}");
                return false;
            }

            clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (clip == null)
            {
                RestResponse.SendError(response, $"Asset is not an AnimationClip: {assetPath}", 400);
                return false;
            }

            return !AnimationClipOwnership.RefuseIfImported(assetPath, response);
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

            // A clip generated by an importer is not an asset a client can write. The write
            // used to go through and answer 200, and the next reimport threw it away.
            if (AnimationClipOwnership.RefuseIfImported(assetPath, response)) return;

            var body = RequestBodyReader.ReadString(request);
            var writes = new List<CurveWrite>();
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

                var curve = new AnimationCurve(keyList.ToArray());

                // Measured before the write rather than derived from the name afterwards:
                // only SetCurve knows what a property name resolves to.
                var produced = ProducedBindings(
                    relativePath, bindingType, property, curve, out var storedNothing);

                clip.SetCurve(relativePath, bindingType, property, curve);
                writes.Add(new CurveWrite(
                    relativePath, bindingType, typeName, property, produced, false, storedNothing));
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

                // SetObjectReferenceCurve addresses one binding exactly, so a PPtr entry
                // resolves to the name it was given and nothing else.
                var binding = EditorCurveBinding.PPtrCurve(relativePath, bindingType, property);
                AnimationUtility.SetObjectReferenceCurve(clip, binding, keyList.ToArray());
                writes.Add(new CurveWrite(
                    relativePath, bindingType, typeName, property, new[] { property }, true, false));
            }

            if (writes.Count > 0)
            {
                EditorUtility.SetDirty(clip);
                AssetDatabase.SaveAssets();
            }

            // Report the outcome, not the intent. A binding the write was expected to
            // produce and that the clip does not hold is a failure however confidently
            // SetCurve returned -- the same rule DELETE applies to its removals.
            var floatAfter = AnimationUtility.GetCurveBindings(clip);
            var pptrAfter = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            var warnings = new List<string>();
            foreach (var write in writes) write.Confirm(floatAfter, pptrAfter, errors);

            // The rotation check reads the clip once every entry has been written, so a
            // quaternion assembled from four entries -- in this request or across several --
            // is judged on what the clip ends up holding rather than on what one entry named.
            var checkedGroups = new List<string>();
            foreach (var write in writes)
            {
                if (!write.IsRotationGroup) continue;

                var group = BindingKey(write.RelativePath, write.Type, RotationGroupName);
                if (checkedGroups.Contains(group)) continue;
                checkedGroups.Add(group);

                WarnIfRotationIsNotUnit(clip, floatAfter, write, warnings);
            }

            // Flat lists across every entry, deduplicated by binding rather than by name:
            // "m_LocalPosition.y" written on two paths is two bindings and appears twice.
            var addedFloat = new List<string>();
            var addedRef = new List<string>();
            var seen = new List<string>();
            foreach (var write in writes)
            {
                foreach (var name in write.Confirmed)
                {
                    var key = BindingKey(write.RelativePath, write.Type, name);
                    if (seen.Contains(key)) continue;
                    seen.Add(key);
                    (write.IsObjectReference ? addedRef : addedFloat).Add(name);
                }
            }

            var sb = new StringBuilder();
            sb.Append("{\"added\":[");
            AppendStrings(sb, addedFloat, addedRef);
            sb.Append("],\"addedFloat\":[");
            AppendStrings(sb, addedFloat);
            sb.Append("],\"addedObjectReference\":[");
            AppendStrings(sb, addedRef);
            sb.Append("],\"curves\":[");
            AppendWrites(sb, writes, false);
            sb.Append("],\"objectReferenceCurves\":[");
            AppendWrites(sb, writes, true);
            sb.Append("],\"errors\":[");
            AppendStrings(sb, errors);
            sb.Append("],\"warnings\":[");
            AppendStrings(sb, warnings);
            sb.Append("]}");

            // 400 when no entry stored what it was asked to, rather than when the clip gained
            // no binding. A group write whose suffix named nothing creates the group and so
            // adds bindings, and answering 200 for it is the failure this endpoint had.
            var anyStored = false;
            foreach (var write in writes) if (!write.Failed) { anyStored = true; break; }
            RestResponse.Send(response, sb.ToString(), errors.Count > 0 && !anyStored ? 400 : 200);
        }

        /// <summary>
        /// One entry of a curve write, and the serialized bindings it turned out to be.
        /// </summary>
        private sealed class CurveWrite
        {
            internal CurveWrite(
                string relativePath, Type type, string typeName,
                string requested, string[] produced, bool isObjectReference, bool storedNothing)
            {
                RelativePath = relativePath;
                Type = type;
                TypeName = typeName;
                Requested = requested;
                Produced = produced;
                IsObjectReference = isObjectReference;
                StoredNothing = storedNothing;
                Confirmed = new List<string>();
            }

            internal string RelativePath { get; private set; }
            internal Type Type { get; private set; }

            /// <summary>The type as the request spelled it, for an error the caller recognises.</summary>
            internal string TypeName { get; private set; }

            internal string Requested { get; private set; }
            internal string[] Produced { get; private set; }
            internal bool IsObjectReference { get; private set; }

            /// <summary>
            /// The entry's keys reached no binding: every binding it produced came out empty.
            /// Measured on the probe rather than on the clip -- see <see cref="ProducedBindings"/>.
            /// </summary>
            internal bool StoredNothing { get; private set; }

            internal List<string> Confirmed { get; private set; }

            /// <summary>The entry did not store what it was asked to, whatever the clip gained.</summary>
            internal bool Failed { get; private set; }

            /// <summary>The entry produced exactly the four components of a Transform quaternion.</summary>
            internal bool IsRotationGroup
            {
                get { return !IsObjectReference && IsRotationComponentSet(Produced); }
            }

            /// <summary>
            /// Keeps the bindings the clip actually holds, and reports the rest as errors.
            /// </summary>
            internal void Confirm(
                EditorCurveBinding[] floatAfter, EditorCurveBinding[] pptrAfter, List<string> errors)
            {
                if (Produced.Length == 0)
                {
                    Failed = true;
                    errors.Add(
                        $"Curve '{Requested}' on '{RelativePath}' ({TypeName}) produced no binding.");
                    return;
                }

                // A group write selects the group by the prefix and the component by the
                // suffix. A suffix naming no component of the group leaves the keys nowhere
                // to land, and the group is created -- or left -- carrying none of them.
                // Unity logs "Can't assign curve because X is not a valid Transform property"
                // to the Editor console when this happens, which no API client can read.
                if (StoredNothing)
                {
                    Failed = true;
                    errors.Add(
                        $"Curve '{Requested}' on '{RelativePath}' ({TypeName}) stored none of its keys: " +
                        $"the {DescribeGroup()} group carries them on one of its components, and " +
                        $"'{Requested}' names none of them. Send one of {string.Join(", ", Produced)}.");
                }

                foreach (var name in Produced)
                {
                    var present = IsObjectReference
                        ? TryFindBinding(pptrAfter, RelativePath, Type, name, out _)
                        : TryFindBinding(floatAfter, RelativePath, Type, name, out _);

                    if (present)
                    {
                        Confirmed.Add(name);
                    }
                    else
                    {
                        Failed = true;
                        errors.Add($"Failed to write '{name}' on '{RelativePath}' ({TypeName}).");
                    }
                }
            }

            /// <summary>The group's name, taken from a produced binding rather than from the request.</summary>
            private string DescribeGroup()
            {
                var first = Produced[0];
                var dot = first.LastIndexOf('.');
                return dot > 0 ? first.Substring(0, dot) : first;
            }
        }

        /// <summary>
        /// The serialized bindings one curve entry resolves to, measured by performing the
        /// write on a throwaway clip.
        ///
        /// <see cref="AnimationClip.SetCurve"/> normalizes the property name and expands a
        /// Transform vector property into every one of its components, filling the ones the
        /// request did not name with that property's default value. Nothing exposes the
        /// mapping: <c>localPosition.y</c> becomes <c>m_LocalPosition.x/.y/.z</c>, and
        /// <c>localEulerAngles.y</c> becomes <c>localEulerAnglesRaw.x/.y/.z</c> -- a name
        /// <see cref="AnimationUtility.GetAnimatableBindings"/> does not report for Transform
        /// at all, so the animatable set cannot stand in for it either.
        ///
        /// A throwaway clip rather than a before-and-after diff of the real one, because a
        /// request that replaces an existing curve creates no binding and a diff would then
        /// report that nothing was written.
        ///
        /// <paramref name="storedNothing"/> answers whether the entry's keys reached any
        /// binding at all. It has to be read here rather than off the saved clip: when the
        /// group already carries curves, a write whose suffix names no component of it is a
        /// complete no-op, and the keys that were already there would pass a check made
        /// afterwards while the keys the request sent were still dropped. On the probe the
        /// entry is the only write, so an empty group is unambiguous. A real component write
        /// never looks like this -- its siblings receive a constant curve carrying keys, not
        /// an empty one -- and `keys` is rejected when empty, so an entry that supplied
        /// nothing cannot reach here either.
        /// </summary>
        private static string[] ProducedBindings(
            string relativePath, Type type, string property, AnimationCurve curve,
            out bool storedNothing)
        {
            var probe = new AnimationClip();
            try
            {
                probe.SetCurve(relativePath, type, property, curve);

                // Every binding on the probe belongs to this entry: it is the only write.
                var bindings = AnimationUtility.GetCurveBindings(probe);
                var names = new string[bindings.Length];
                var anyKeys = false;
                for (int i = 0; i < bindings.Length; i++)
                {
                    names[i] = bindings[i].propertyName;

                    var stored = AnimationUtility.GetEditorCurve(probe, bindings[i]);
                    if (stored != null && stored.length > 0) anyKeys = true;
                }

                storedNothing = bindings.Length > 0 && !anyKeys;
                return names;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        /// <summary>The serialized name of Transform's quaternion rotation group.</summary>
        private const string RotationGroupName = "m_LocalRotation";

        private static readonly string[] RotationComponents =
        {
            RotationGroupName + ".x", RotationGroupName + ".y",
            RotationGroupName + ".z", RotationGroupName + ".w",
        };

        /// <summary>
        /// How far a quaternion's length may sit from 1 before the write is called into
        /// question. Loose enough that a client sending rounded components -- 0.7071 for a
        /// quarter turn -- is not reported, and far tighter than the failure it exists to
        /// catch, which leaves the length at 0.7071 or at 0.
        /// </summary>
        private const float RotationUnitTolerance = 1e-3f;

        /// <summary>
        /// Whether a set of produced bindings is exactly the four components of a Transform
        /// quaternion.
        ///
        /// Keyed on what the entry produced rather than on the name it sent, which settles
        /// both spellings and the type at once: `localRotation` and `m_LocalRotation` reach
        /// the same four bindings, and `m_LocalRotation.y` on a Light is stored verbatim as
        /// one binding and is not a rotation at all.
        /// </summary>
        private static bool IsRotationComponentSet(string[] produced)
        {
            if (produced == null || produced.Length != RotationComponents.Length) return false;

            foreach (var component in RotationComponents)
            {
                var found = false;
                foreach (var name in produced) if (name == component) { found = true; break; }
                if (!found) return false;
            }
            return true;
        }

        /// <summary>
        /// Reports a rotation group whose quaternion is not unit length where it is keyed.
        ///
        /// <see cref="AnimationClip.SetCurve"/> fills the components a request did not name
        /// with <c>0</c>, and the quaternion identity is <c>w = 1</c> -- so a single entry on
        /// one component leaves <c>(0, y, 0, 0)</c>, which normalizes to a half turn whatever
        /// <c>y</c> holds. The write succeeds, the curve is applied, and the rotation is wrong
        /// by an amount nothing in the response would otherwise show.
        ///
        /// Only at the key times, which <see cref="KeyTimes"/> takes across all four curves. A
        /// correctly authored quaternion is not unit length between its keys either -- Unity
        /// interpolates the four components and normalizes on apply, so a check anywhere else
        /// would report every rotation curve ever written.
        ///
        /// This is a warning rather than an error because the write did store something, and
        /// because a caller may complete the quaternion over several requests: a later write
        /// to another component replaces that component and leaves the ones already carrying
        /// curves alone (measured). Such a caller trips this on every request but the last,
        /// and on each of those the clip really is holding a rotation that plays back wrong.
        /// </summary>
        private static void WarnIfRotationIsNotUnit(
            AnimationClip clip, EditorCurveBinding[] floatAfter, CurveWrite write, List<string> warnings)
        {
            var curves = new AnimationCurve[RotationComponents.Length];
            for (int i = 0; i < RotationComponents.Length; i++)
            {
                if (!TryFindBinding(
                        floatAfter, write.RelativePath, write.Type, RotationComponents[i], out var binding))
                    return;

                curves[i] = AnimationUtility.GetEditorCurve(clip, binding);
                if (curves[i] == null) return;
            }

            foreach (var time in KeyTimes(curves))
            {
                var x = curves[0].Evaluate(time);
                var y = curves[1].Evaluate(time);
                var z = curves[2].Evaluate(time);
                var w = curves[3].Evaluate(time);

                var length = Mathf.Sqrt(x * x + y * y + z * z + w * w);
                if (Mathf.Abs(length - 1f) <= RotationUnitTolerance) continue;

                var where = string.IsNullOrEmpty(write.RelativePath) ? "the root" : $"'{write.RelativePath}'";
                warnings.Add(
                    $"Rotation on {where} is not a unit quaternion at t={RestResponse.FormatFloat(time)}: " +
                    $"{RotationGroupName}.x/.y/.z/.w = ({RestResponse.FormatFloat(x)}, {RestResponse.FormatFloat(y)}, " +
                    $"{RestResponse.FormatFloat(z)}, {RestResponse.FormatFloat(w)}), length " +
                    $"{RestResponse.FormatFloat(length)}. SetCurve fills the components a request does not name " +
                    $"with 0, and the quaternion identity is w=1, so one entry leaves a half turn whatever value " +
                    $"it carries. Write rotation as 'localEulerAngles.*', or send all four components.");
                return;
            }
        }

        /// <summary>
        /// Every distinct time any of the curves is keyed at, in order.
        ///
        /// Measured on 6000.0.80f1, a group write resamples the whole group onto the union of
        /// its key times -- writing one component in its own request gives the other three a
        /// key wherever that component has one -- so any single curve's times are already the
        /// group's times, and this returns what iterating one of them would. It is built as a
        /// union anyway: that resampling is undocumented Unity behaviour measured on one
        /// version, and a version that does not do it would leave the check reading times
        /// that only describe part of the group. The union does not depend on it.
        /// </summary>
        private static List<float> KeyTimes(AnimationCurve[] curves)
        {
            var times = new List<float>();
            foreach (var curve in curves)
            {
                foreach (var key in curve.keys)
                {
                    var seen = false;
                    foreach (var time in times)
                        if (Mathf.Approximately(time, key.time)) { seen = true; break; }

                    if (!seen) times.Add(key.time);
                }
            }

            times.Sort();
            return times;
        }

        /// <summary>
        /// Appends the elements of every list as one JSON string array, without the brackets.
        /// </summary>
        private static void AppendStrings(StringBuilder sb, params List<string>[] lists)
        {
            var wrote = false;
            foreach (var values in lists)
            {
                foreach (var value in values)
                {
                    if (wrote) sb.Append(",");
                    sb.Append($"\"{RestResponse.EscapeJson(value)}\"");
                    wrote = true;
                }
            }
        }

        private static void AppendWrites(StringBuilder sb, List<CurveWrite> writes, bool objectReference)
        {
            var wrote = false;
            foreach (var write in writes)
            {
                if (write.IsObjectReference != objectReference) continue;
                if (wrote) sb.Append(",");
                wrote = true;

                sb.Append("{\"relativePath\":").Append(RestResponse.FormatNullableString(write.RelativePath));
                sb.Append(",\"type\":").Append(RestResponse.FormatNullableString(write.Type.Name));
                sb.Append(",\"requested\":").Append(RestResponse.FormatNullableString(write.Requested));
                sb.Append(",\"bindings\":[");
                AppendStrings(sb, write.Confirmed);
                sb.Append("]}");
            }
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

            if (AnimationClipOwnership.RefuseIfImported(assetPath, response)) return;

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
            TryParseWrapMode(s, out var mode);
            return mode;
        }

        /// <summary>
        /// Parses a wrap mode, separating "not one of these" from Default.
        ///
        /// <see cref="ParseWrapMode"/> maps anything it does not know to Default, which is
        /// fine for a create where the field is optional and Default is the fallback, and
        /// wrong for an update: a client sending a misspelled mode would silently get
        /// Default rather than being told.
        /// </summary>
        private static bool TryParseWrapMode(string s, out WrapMode mode)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "once":         mode = WrapMode.Once;         return true;
                case "loop":         mode = WrapMode.Loop;         return true;
                case "pingpong":     mode = WrapMode.PingPong;     return true;
                case "clampforever": mode = WrapMode.ClampForever; return true;
                case "default":      mode = WrapMode.Default;      return true;
            }
            mode = WrapMode.Default;
            return false;
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

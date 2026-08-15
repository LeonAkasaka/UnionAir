using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal sealed class ModelImporterClipPatch
    {
        internal string TakeName;
        internal string Name;
        internal float FirstFrame;
        internal float LastFrame;
        internal WrapMode? WrapMode;
        internal bool? Loop;
        internal bool? LoopTime;
        internal bool? LoopPose;
        internal bool? Mirror;
        internal bool? LockRootRotation;
        internal bool? KeepOriginalOrientation;
        internal float? RotationOffset;
        internal bool? LockRootHeightY;
        internal bool? KeepOriginalPositionY;
        internal bool? HeightFromFeet;
        internal float? HeightOffset;
        internal bool? LockRootPositionXZ;
        internal bool? KeepOriginalPositionXZ;
        internal float? CycleOffset;
        internal bool? HasAdditiveReferencePose;
        internal float? AdditiveReferencePoseFrame;
        internal ClipAnimationMaskType? MaskType;
        internal bool HasMaskSource;
        internal ModelImporterObjectReferenceRequest MaskSource;
        internal bool HasEvents;
        internal AnimationEvent[] Events;
        internal List<ModelImporterObjectReferenceRequest> EventObjectReferences;
    }

    internal static class ModelImporterClipsParser
    {
        private static readonly string[] ClipFields =
        {
            "takeName", "name", "firstFrame", "lastFrame", "wrapMode", "loop", "loopTime",
            "loopPose", "mirror", "lockRootRotation", "keepOriginalOrientation", "rotationOffset",
            "lockRootHeightY", "keepOriginalPositionY", "heightFromFeet", "heightOffset",
            "lockRootPositionXZ", "keepOriginalPositionXZ", "cycleOffset",
            "hasAdditiveReferencePose", "additiveReferencePoseFrame", "maskType", "maskSource", "events"
        };
        private static readonly string[] ReferenceFields = { "guid", "localIdentifier" };
        private static readonly string[] EventFields =
        {
            "time", "functionName", "stringParameter", "floatParameter", "intParameter",
            "objectReferenceParameter", "messageOptions"
        };

        internal static bool TryParse(
            string body, ModelImporterUpdateRequest request, out string error)
        {
            List<string> elements;
            bool present;
            if (!RequestBodyReader.TryGetArrayElements(body, "clips", out elements, out present, out error))
                return false;
            if (!present) return true;

            var result = new List<ModelImporterClipPatch>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < elements.Count; i++)
            {
                var prefix = "clips[" + i + "]";
                var json = elements[i];
                if (!RequestBodyReader.TryValidateObjectFields(json, ClipFields, out error))
                {
                    error = prefix + ": " + error;
                    return false;
                }

                var patch = new ModelImporterClipPatch();
                if (!ReadRequiredString(json, "takeName", prefix + ".takeName", out patch.TakeName, out error) ||
                    !ReadRequiredString(json, "name", prefix + ".name", out patch.Name, out error) ||
                    !ReadRequiredFloat(json, "firstFrame", prefix + ".firstFrame", out patch.FirstFrame, out error) ||
                    !ReadRequiredFloat(json, "lastFrame", prefix + ".lastFrame", out patch.LastFrame, out error) ||
                    !ReadEnum(json, "wrapMode", prefix + ".wrapMode", out patch.WrapMode, out error) ||
                    !ReadBool(json, "loop", prefix + ".loop", out patch.Loop, out error) ||
                    !ReadBool(json, "loopTime", prefix + ".loopTime", out patch.LoopTime, out error) ||
                    !ReadBool(json, "loopPose", prefix + ".loopPose", out patch.LoopPose, out error) ||
                    !ReadBool(json, "mirror", prefix + ".mirror", out patch.Mirror, out error) ||
                    !ReadBool(json, "lockRootRotation", prefix + ".lockRootRotation",
                        out patch.LockRootRotation, out error) ||
                    !ReadBool(json, "keepOriginalOrientation", prefix + ".keepOriginalOrientation",
                        out patch.KeepOriginalOrientation, out error) ||
                    !ReadFloat(json, "rotationOffset", prefix + ".rotationOffset",
                        out patch.RotationOffset, out error) ||
                    !ReadBool(json, "lockRootHeightY", prefix + ".lockRootHeightY",
                        out patch.LockRootHeightY, out error) ||
                    !ReadBool(json, "keepOriginalPositionY", prefix + ".keepOriginalPositionY",
                        out patch.KeepOriginalPositionY, out error) ||
                    !ReadBool(json, "heightFromFeet", prefix + ".heightFromFeet",
                        out patch.HeightFromFeet, out error) ||
                    !ReadFloat(json, "heightOffset", prefix + ".heightOffset",
                        out patch.HeightOffset, out error) ||
                    !ReadBool(json, "lockRootPositionXZ", prefix + ".lockRootPositionXZ",
                        out patch.LockRootPositionXZ, out error) ||
                    !ReadBool(json, "keepOriginalPositionXZ", prefix + ".keepOriginalPositionXZ",
                        out patch.KeepOriginalPositionXZ, out error) ||
                    !ReadFloat(json, "cycleOffset", prefix + ".cycleOffset",
                        out patch.CycleOffset, out error) ||
                    !ReadBool(json, "hasAdditiveReferencePose", prefix + ".hasAdditiveReferencePose",
                        out patch.HasAdditiveReferencePose, out error) ||
                    !ReadFloat(json, "additiveReferencePoseFrame", prefix + ".additiveReferencePoseFrame",
                        out patch.AdditiveReferencePoseFrame, out error) ||
                    !ReadEnum(json, "maskType", prefix + ".maskType", out patch.MaskType, out error))
                    return false;

                if (!names.Add(patch.Name))
                {
                    error = "Duplicate clip name '" + patch.Name + "'.";
                    return false;
                }

                patch.HasMaskSource = RequestBodyReader.HasTopLevelField(json, "maskSource");
                if (patch.HasMaskSource && !TryParseNullableReference(
                        json, "maskSource", prefix + ".maskSource", out patch.MaskSource, out error))
                    return false;

                if (!TryParseEvents(json, prefix, patch, out error)) return false;
                result.Add(patch);
            }

            request.Clips = result;
            error = null;
            return true;
        }

        internal static bool TryResolveReferences(ModelImporterUpdateRequest request, out string error)
        {
            if (request.Clips == null)
            {
                error = null;
                return true;
            }

            for (var i = 0; i < request.Clips.Count; i++)
            {
                var clip = request.Clips[i];
                if (clip.MaskSource != null && !ModelImporterObjectResolver.TryResolve(
                        clip.MaskSource, typeof(AvatarMask), "clips[" + i + "].maskSource", out error))
                    return false;
                if (clip.Events == null) continue;
                for (var j = 0; j < clip.Events.Length; j++)
                {
                    var pending = clip.EventObjectReferences[j];
                    if (pending == null) continue;
                    if (!ModelImporterObjectResolver.TryResolve(
                            pending, typeof(UnityEngine.Object),
                            "clips[" + i + "].events[" + j + "].objectReferenceParameter", out error))
                        return false;
                    clip.Events[j].objectReferenceParameter = pending.Resolved;
                }
            }

            error = null;
            return true;
        }

        internal static bool TryPrepare(
            ModelImporterUpdateRequest request, ModelImporterState before, out string error)
        {
            if (request.Clips == null)
            {
                error = null;
                return true;
            }

            var prepared = new List<ModelImporterClipAnimation>();
            var defaults = before.DefaultClipAnimations ?? new ModelImporterClipAnimation[0];
            for (var i = 0; i < request.Clips.Count; i++)
            {
                var patch = request.Clips[i];
                ModelImporterClipAnimation take = null;
                var matches = 0;
                foreach (var candidate in defaults)
                {
                    if (candidate.takeName != patch.TakeName) continue;
                    take = candidate;
                    matches++;
                }
                if (matches == 0)
                {
                    error = "clips[" + i + "].takeName '" + patch.TakeName +
                            "' does not exist in defaultClipAnimations.";
                    return false;
                }
                if (matches > 1)
                {
                    error = "clips[" + i + "].takeName '" + patch.TakeName + "' is ambiguous.";
                    return false;
                }
                if (patch.FirstFrame > patch.LastFrame)
                {
                    error = "clips[" + i + "] requires firstFrame <= lastFrame.";
                    return false;
                }
                if (patch.FirstFrame < take.firstFrame || patch.LastFrame > take.lastFrame)
                {
                    error = "clips[" + i + "] frame range must stay within take '" + patch.TakeName +
                            "' (" + take.firstFrame + ".." + take.lastFrame + ").";
                    return false;
                }

                var baseline = take;
                var storedMatches = 0;
                foreach (var candidate in before.StoredClipAnimations ?? new ModelImporterClipAnimation[0])
                {
                    if (candidate.takeName != patch.TakeName || candidate.name != patch.Name) continue;
                    baseline = candidate;
                    storedMatches++;
                }
                if (storedMatches > 1)
                {
                    error = "clips[" + i + "] matches more than one stored clip definition.";
                    return false;
                }

                var value = ModelImporterClipsState.CloneClip(baseline);
                value.takeName = patch.TakeName;
                value.name = patch.Name;
                value.firstFrame = patch.FirstFrame;
                value.lastFrame = patch.LastFrame;
                Set(patch.WrapMode, result => value.wrapMode = result);
                Set(patch.Loop, result => value.loop = result);
                Set(patch.LoopTime, result => value.loopTime = result);
                Set(patch.LoopPose, result => value.loopPose = result);
                Set(patch.Mirror, result => value.mirror = result);
                Set(patch.LockRootRotation, result => value.lockRootRotation = result);
                Set(patch.KeepOriginalOrientation, result => value.keepOriginalOrientation = result);
                Set(patch.RotationOffset, result => value.rotationOffset = result);
                Set(patch.LockRootHeightY, result => value.lockRootHeightY = result);
                Set(patch.KeepOriginalPositionY, result => value.keepOriginalPositionY = result);
                Set(patch.HeightFromFeet, result => value.heightFromFeet = result);
                Set(patch.HeightOffset, result => value.heightOffset = result);
                Set(patch.LockRootPositionXZ, result => value.lockRootPositionXZ = result);
                Set(patch.KeepOriginalPositionXZ, result => value.keepOriginalPositionXZ = result);
                Set(patch.CycleOffset, result => value.cycleOffset = result);
                Set(patch.HasAdditiveReferencePose, result => value.hasAdditiveReferencePose = result);
                Set(patch.AdditiveReferencePoseFrame, result => value.additiveReferencePoseFrame = result);
                Set(patch.MaskType, result => value.maskType = result);
                if (patch.HasMaskSource) value.maskSource = patch.MaskSource?.Resolved as AvatarMask;
                if (patch.HasEvents) value.events = ModelImporterClipsState.CloneEvents(patch.Events);
                prepared.Add(value);
            }

            request.PreparedClips = prepared.ToArray();
            error = null;
            return true;
        }

        private static bool TryParseEvents(
            string json, string clipPrefix, ModelImporterClipPatch patch, out string error)
        {
            List<string> elements;
            bool present;
            if (!RequestBodyReader.TryGetArrayElements(json, "events", out elements, out present, out error))
                return false;
            patch.HasEvents = present;
            if (!present) return true;

            var events = new List<AnimationEvent>();
            var references = new List<ModelImporterObjectReferenceRequest>();
            for (var i = 0; i < elements.Count; i++)
            {
                var prefix = clipPrefix + ".events[" + i + "]";
                var eventJson = elements[i];
                if (!RequestBodyReader.TryValidateObjectFields(eventJson, EventFields, out error))
                {
                    error = prefix + ": " + error;
                    return false;
                }
                float time;
                if (!ReadRequiredFloat(eventJson, "time", prefix + ".time", out time, out error)) return false;
                if (time < 0f)
                {
                    error = prefix + ".time must be non-negative.";
                    return false;
                }
                string functionName;
                if (!ReadRequiredString(
                        eventJson, "functionName", prefix + ".functionName", out functionName, out error))
                    return false;
                var value = new AnimationEvent { time = time, functionName = functionName };
                string text;
                bool stringPresent;
                if (!RequestBodyReader.TryGetStringValue(
                        eventJson, "stringParameter", out text, out stringPresent))
                {
                    error = prefix + ".stringParameter must be a JSON string.";
                    return false;
                }
                if (stringPresent) value.stringParameter = text;
                float floatValue;
                bool floatPresent;
                if (!RequestBodyReader.TryGetFloatValue(
                        eventJson, "floatParameter", out floatValue, out floatPresent))
                {
                    error = prefix + ".floatParameter must be a finite JSON number.";
                    return false;
                }
                if (floatPresent) value.floatParameter = floatValue;
                int intValue;
                bool intPresent;
                if (!RequestBodyReader.TryGetIntValue(
                        eventJson, "intParameter", out intValue, out intPresent))
                {
                    error = prefix + ".intParameter must be a JSON integer.";
                    return false;
                }
                if (intPresent) value.intParameter = intValue;
                string options;
                bool optionsPresent;
                if (!RequestBodyReader.TryGetStringValue(
                        eventJson, "messageOptions", out options, out optionsPresent))
                {
                    error = prefix + ".messageOptions must be a JSON string.";
                    return false;
                }
                if (optionsPresent)
                {
                    SendMessageOptions parsed;
                    if (!Enum.TryParse(options, true, out parsed) ||
                        !Enum.IsDefined(typeof(SendMessageOptions), parsed))
                    {
                        error = prefix + ".messageOptions must be DontRequireReceiver or RequireReceiver.";
                        return false;
                    }
                    value.messageOptions = parsed;
                }

                if (RequestBodyReader.HasTopLevelField(eventJson, "objectReferenceParameter"))
                {
                    ModelImporterObjectReferenceRequest reference;
                    if (!TryParseNullableReference(
                            eventJson, "objectReferenceParameter",
                            prefix + ".objectReferenceParameter", out reference, out error)) return false;
                    references.Add(reference);
                }
                else references.Add(null);
                events.Add(value);
            }
            patch.Events = events.ToArray();
            patch.EventObjectReferences = references;
            error = null;
            return true;
        }

        private static bool TryParseNullableReference(
            string json, string key, string path, out ModelImporterObjectReferenceRequest reference,
            out string error)
        {
            reference = null;
            var raw = RequestBodyReader.GetRawValue(json, key);
            if (raw == null)
            {
                error = path + " must be null or a JSON object.";
                return false;
            }
            if (raw.Trim() == "null")
            {
                error = null;
                return true;
            }
            var objectJson = RequestBodyReader.GetObject(json, key);
            if (objectJson == null ||
                !RequestBodyReader.TryValidateObjectFields(objectJson, ReferenceFields, out error))
            {
                error = path + " must contain only 'guid' and optional 'localIdentifier'.";
                return false;
            }
            string guid;
            string local;
            bool guidPresent;
            bool localPresent;
            if (!RequestBodyReader.TryGetStringValue(objectJson, "guid", out guid, out guidPresent) ||
                !guidPresent || string.IsNullOrWhiteSpace(guid))
            {
                error = path + ".guid must be a non-empty JSON string.";
                return false;
            }
            if (!RequestBodyReader.TryGetStringValue(
                    objectJson, "localIdentifier", out local, out localPresent))
            {
                error = path + ".localIdentifier must be a decimal JSON string.";
                return false;
            }
            ulong parsed;
            if (localPresent && !ulong.TryParse(local, out parsed))
            {
                error = path + ".localIdentifier must be an unsigned decimal JSON string.";
                return false;
            }
            reference = new ModelImporterObjectReferenceRequest
            {
                Guid = guid,
                LocalIdentifier = localPresent ? local : null
            };
            error = null;
            return true;
        }

        private static bool ReadRequiredString(
            string json, string key, string path, out string value, out string error)
        {
            bool present;
            if (!RequestBodyReader.TryGetStringValue(json, key, out value, out present) ||
                !present || string.IsNullOrWhiteSpace(value))
            {
                error = path + " must be a non-empty JSON string.";
                return false;
            }
            error = null;
            return true;
        }

        private static bool ReadRequiredFloat(
            string json, string key, string path, out float value, out string error)
        {
            bool present;
            if (!RequestBodyReader.TryGetFloatValue(json, key, out value, out present) || !present)
            {
                error = path + " must be a finite JSON number.";
                return false;
            }
            error = null;
            return true;
        }

        private static bool ReadFloat(
            string json, string key, string path, out float? value, out string error)
        {
            value = null;
            float parsed;
            bool present;
            if (!RequestBodyReader.TryGetFloatValue(json, key, out parsed, out present))
            {
                error = path + " must be a finite JSON number.";
                return false;
            }
            if (present) value = parsed;
            error = null;
            return true;
        }

        private static bool ReadBool(
            string json, string key, string path, out bool? value, out string error)
        {
            value = null;
            bool parsed;
            bool present;
            if (!RequestBodyReader.TryGetBoolValue(json, key, out parsed, out present))
            {
                error = path + " must be a JSON boolean.";
                return false;
            }
            if (present) value = parsed;
            error = null;
            return true;
        }

        private static bool ReadEnum<T>(
            string json, string key, string path, out T? value, out string error) where T : struct
        {
            value = null;
            string text;
            bool present;
            if (!RequestBodyReader.TryGetStringValue(json, key, out text, out present))
            {
                error = path + " must be a JSON string.";
                return false;
            }
            if (!present)
            {
                error = null;
                return true;
            }
            T parsed;
            if (!Enum.TryParse(text, true, out parsed) || !Enum.IsDefined(typeof(T), parsed))
            {
                error = path + " must be one of: " + string.Join(", ", Enum.GetNames(typeof(T))) + ".";
                return false;
            }
            value = parsed;
            error = null;
            return true;
        }

        private static void Set<T>(T? value, Action<T> setter) where T : struct
        {
            if (value.HasValue) setter(value.Value);
        }
    }

    internal static class ModelImporterClipsState
    {
        internal static void Capture(ModelImporter importer, ModelImporterState state)
        {
            state.StoredClipAnimations = CloneClips(importer.clipAnimations);
            state.DefaultClipAnimations = CloneClips(importer.defaultClipAnimations);
        }

        internal static void CloneCollections(ModelImporterState source, ModelImporterState clone)
        {
            clone.StoredClipAnimations = CloneClips(source.StoredClipAnimations);
            clone.DefaultClipAnimations = CloneClips(source.DefaultClipAnimations);
        }

        internal static void ApplyPrepared(
            ModelImporterState state, ModelImporterClipAnimation[] prepared, List<string> changed)
        {
            prepared = prepared ?? new ModelImporterClipAnimation[0];
            if (!ClipsEqual(state.StoredClipAnimations, prepared)) changed.Add("clips");
            state.StoredClipAnimations = CloneClips(prepared);
        }

        internal static void Apply(
            ModelImporter importer, ModelImporterState state, ModelImporterUpdateRequest request)
        {
            if (request?.Clips == null) return;
            importer.clipAnimations = CloneClips(state.StoredClipAnimations);
        }

        internal static bool EqualsState(ModelImporterState left, ModelImporterState right)
            => ClipsEqual(left.StoredClipAnimations, right.StoredClipAnimations) &&
               ClipsEqual(left.DefaultClipAnimations, right.DefaultClipAnimations);

        internal static ModelImporterClipAnimation[] Effective(ModelImporterState state)
            => state.StoredClipAnimations != null && state.StoredClipAnimations.Length > 0
                ? state.StoredClipAnimations
                : state.DefaultClipAnimations ?? new ModelImporterClipAnimation[0];

        internal static bool DerivedFromDefaults(ModelImporterState state)
            => state.StoredClipAnimations == null || state.StoredClipAnimations.Length == 0;

        internal static ModelImporterClipAnimation[] CloneClips(ModelImporterClipAnimation[] source)
        {
            if (source == null) return new ModelImporterClipAnimation[0];
            var result = new ModelImporterClipAnimation[source.Length];
            for (var i = 0; i < source.Length; i++) result[i] = CloneClip(source[i]);
            return result;
        }

        internal static ModelImporterClipAnimation CloneClip(ModelImporterClipAnimation source)
        {
            return new ModelImporterClipAnimation
            {
                takeName = source.takeName,
                name = source.name,
                firstFrame = source.firstFrame,
                lastFrame = source.lastFrame,
                wrapMode = source.wrapMode,
                loop = source.loop,
                rotationOffset = source.rotationOffset,
                heightOffset = source.heightOffset,
                cycleOffset = source.cycleOffset,
                loopTime = source.loopTime,
                loopPose = source.loopPose,
                lockRootRotation = source.lockRootRotation,
                lockRootHeightY = source.lockRootHeightY,
                lockRootPositionXZ = source.lockRootPositionXZ,
                keepOriginalOrientation = source.keepOriginalOrientation,
                keepOriginalPositionY = source.keepOriginalPositionY,
                keepOriginalPositionXZ = source.keepOriginalPositionXZ,
                heightFromFeet = source.heightFromFeet,
                mirror = source.mirror,
                maskType = source.maskType,
                maskSource = source.maskSource,
                events = CloneEvents(source.events),
                curves = source.curves == null ? new ClipAnimationInfoCurve[0] :
                    (ClipAnimationInfoCurve[])source.curves.Clone(),
                additiveReferencePoseFrame = source.additiveReferencePoseFrame,
                hasAdditiveReferencePose = source.hasAdditiveReferencePose
            };
        }

        internal static AnimationEvent[] CloneEvents(AnimationEvent[] source)
        {
            if (source == null) return new AnimationEvent[0];
            var result = new AnimationEvent[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                var value = source[i];
                result[i] = new AnimationEvent
                {
                    time = value.time,
                    functionName = value.functionName,
                    stringParameter = value.stringParameter,
                    floatParameter = value.floatParameter,
                    intParameter = value.intParameter,
                    objectReferenceParameter = value.objectReferenceParameter,
                    messageOptions = value.messageOptions
                };
            }
            return result;
        }

        private static bool ClipsEqual(
            ModelImporterClipAnimation[] left, ModelImporterClipAnimation[] right)
        {
            left = left ?? new ModelImporterClipAnimation[0];
            right = right ?? new ModelImporterClipAnimation[0];
            if (left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++) if (!ClipEqual(left[i], right[i])) return false;
            return true;
        }

        private static bool ClipEqual(ModelImporterClipAnimation left, ModelImporterClipAnimation right)
        {
            return left.takeName == right.takeName && left.name == right.name &&
                   Same(left.firstFrame, right.firstFrame) && Same(left.lastFrame, right.lastFrame) &&
                   left.wrapMode == right.wrapMode && left.loop == right.loop &&
                   left.loopTime == right.loopTime && left.loopPose == right.loopPose &&
                   left.mirror == right.mirror && left.lockRootRotation == right.lockRootRotation &&
                   left.keepOriginalOrientation == right.keepOriginalOrientation &&
                   Same(left.rotationOffset, right.rotationOffset) &&
                   left.lockRootHeightY == right.lockRootHeightY &&
                   left.keepOriginalPositionY == right.keepOriginalPositionY &&
                   left.heightFromFeet == right.heightFromFeet && Same(left.heightOffset, right.heightOffset) &&
                   left.lockRootPositionXZ == right.lockRootPositionXZ &&
                   left.keepOriginalPositionXZ == right.keepOriginalPositionXZ &&
                   Same(left.cycleOffset, right.cycleOffset) &&
                   left.hasAdditiveReferencePose == right.hasAdditiveReferencePose &&
                   Same(left.additiveReferencePoseFrame, right.additiveReferencePoseFrame) &&
                   left.maskType == right.maskType &&
                   ModelImporterObjectIdentity.Same(left.maskSource, right.maskSource) &&
                   EventsEqual(left.events, right.events);
        }

        private static bool EventsEqual(AnimationEvent[] left, AnimationEvent[] right)
        {
            left = left ?? new AnimationEvent[0];
            right = right ?? new AnimationEvent[0];
            if (left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++)
            {
                if (!Same(left[i].time, right[i].time) || left[i].functionName != right[i].functionName ||
                    left[i].stringParameter != right[i].stringParameter ||
                    !Same(left[i].floatParameter, right[i].floatParameter) ||
                    left[i].intParameter != right[i].intParameter ||
                    left[i].messageOptions != right[i].messageOptions ||
                    !ModelImporterObjectIdentity.Same(
                        left[i].objectReferenceParameter, right[i].objectReferenceParameter)) return false;
            }
            return true;
        }

        private static bool Same(float left, float right) => Math.Abs(left - right) < 0.000001f;
    }

    internal static class ModelImporterClipsRules
    {
        internal static bool TryValidate(
            ModelImporterState state, ModelImporterUpdateRequest request, out string error)
        {
            if (request.Clips == null)
            {
                error = null;
                return true;
            }

            var clips = state.StoredClipAnimations ?? new ModelImporterClipAnimation[0];
            for (var i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                if (clip.loopPose && !clip.loopTime)
                {
                    error = "clips[" + i + "].loopPose requires loopTime true.";
                    return false;
                }
                if (clip.hasAdditiveReferencePose &&
                    (clip.additiveReferencePoseFrame < clip.firstFrame ||
                     clip.additiveReferencePoseFrame > clip.lastFrame))
                {
                    error = "clips[" + i + "].additiveReferencePoseFrame must be within the clip frame range.";
                    return false;
                }
                if (!clip.hasAdditiveReferencePose &&
                    request.Clips[i].AdditiveReferencePoseFrame.HasValue)
                {
                    error = "clips[" + i + "].additiveReferencePoseFrame requires hasAdditiveReferencePose true.";
                    return false;
                }
                if (clip.maskType == ClipAnimationMaskType.CopyFromOther && clip.maskSource == null)
                {
                    error = "clips[" + i + "].maskType CopyFromOther requires maskSource.";
                    return false;
                }
                if (clip.maskType != ClipAnimationMaskType.CopyFromOther && clip.maskSource != null)
                {
                    error = "clips[" + i + "].maskSource is allowed only with maskType CopyFromOther.";
                    return false;
                }
                if (clip.mirror && state.AnimationType != ModelImporterAnimationType.Human)
                {
                    error = "clips[" + i + "].mirror is supported only for a Human rig.";
                    return false;
                }
            }

            error = null;
            return true;
        }
    }
}

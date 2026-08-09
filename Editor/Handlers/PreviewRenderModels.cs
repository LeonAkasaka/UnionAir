using System;
using System.Collections.Generic;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal enum PreviewAnimationMode
    {
        None,
        Clip,
        State,
        Parameters
    }

    internal sealed class PreviewRenderRequestModel
    {
        internal string Target;
        internal string ScenePath;
        internal string FocusPath;
        internal int Width = 640;
        internal int Height = 640;
        internal string Format = "png";
        internal int Quality = 85;
        internal float[] Times = { 0f };
        internal PreviewViewSettings View = new PreviewViewSettings();
        internal Color Background = new Color(0.18f, 0.18f, 0.18f, 1f);
        internal PreviewLightingSettings Lighting = new PreviewLightingSettings();
        internal PreviewAnimationSettings Animation = new PreviewAnimationSettings();
    }

    internal sealed class PreviewViewSettings
    {
        internal string Preset = "front";
        internal float Yaw;
        internal float Pitch;
        internal float? Distance;
        internal float FieldOfView = 30f;
        internal float Padding = 0.1f;
    }

    internal sealed class PreviewLightingSettings
    {
        internal float KeyIntensity = 1f;
        internal float FillIntensity = 0.5f;
        internal Color KeyColor = Color.white;
        internal Color FillColor = new Color(0.65f, 0.72f, 1f, 1f);
    }

    internal sealed class PreviewAnimationSettings
    {
        internal PreviewAnimationMode Mode;
        internal string Clip;
        internal string ClipName;
        internal string State;
        internal int Layer;
        internal string AnimatorPath;
        internal readonly List<PreviewAnimatorParameterRequest> Parameters =
            new List<PreviewAnimatorParameterRequest>();
    }

    internal sealed class PreviewAnimatorParameterRequest
    {
        internal string Name;
        internal string RawValue;
    }

    internal sealed class PreviewBindingResult
    {
        internal string Path;
        internal string Type;
        internal string Property;
    }

    internal sealed class PreviewClipResult
    {
        internal string Name;
        internal float Weight;
    }

    internal sealed class PreviewStateResult
    {
        internal int Layer;
        internal int FullPathHash;
        internal int ShortNameHash;
        internal float NormalizedTime;
        internal float Length;
        internal bool Loop;
        internal readonly List<PreviewClipResult> Clips = new List<PreviewClipResult>();
    }

    internal sealed class PreviewFrameResult
    {
        internal float Time;
        internal byte[] Image;
        internal Bounds Bounds;
        internal Vector3 CameraPosition;
        internal Quaternion CameraRotation;
        internal float Distance;
        internal readonly List<PreviewStateResult> States = new List<PreviewStateResult>();
        internal readonly List<PreviewBindingResult> AppliedBindings = new List<PreviewBindingResult>();
        internal readonly List<PreviewBindingResult> SkippedBindings = new List<PreviewBindingResult>();
    }

    internal static class PreviewFraming
    {
        internal static Quaternion CameraRotation(float yaw, float pitch)
        {
            var orbit = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
            var forward = -orbit.normalized;
            var up = Math.Abs(Vector3.Dot(forward, Vector3.up)) > 0.999f
                ? Vector3.forward
                : Vector3.up;
            return Quaternion.LookRotation(forward, up);
        }

        internal static float CalculateDistance(
            Bounds bounds,
            Quaternion cameraRotation,
            float verticalFieldOfView,
            float aspect,
            float padding)
        {
            var forward = cameraRotation * Vector3.forward;
            var right = cameraRotation * Vector3.right;
            var up = cameraRotation * Vector3.up;
            var verticalTangent = Mathf.Tan(verticalFieldOfView * 0.5f * Mathf.Deg2Rad);
            var horizontalTangent = verticalTangent * aspect;
            var usableFraction = 1f - (2f * padding);
            verticalTangent *= usableFraction;
            horizontalTangent *= usableFraction;

            var extents = bounds.extents;
            var distance = 0.01f;
            for (var x = -1; x <= 1; x += 2)
            for (var y = -1; y <= 1; y += 2)
            for (var z = -1; z <= 1; z += 2)
            {
                var offset = new Vector3(extents.x * x, extents.y * y, extents.z * z);
                var depth = Vector3.Dot(offset, forward);
                var horizontal = Math.Abs(Vector3.Dot(offset, right));
                var vertical = Math.Abs(Vector3.Dot(offset, up));

                distance = Math.Max(distance, horizontal / horizontalTangent - depth);
                distance = Math.Max(distance, vertical / verticalTangent - depth);
                distance = Math.Max(distance, 0.01f - depth);
            }

            return distance;
        }

        internal static bool IsFinite(Bounds bounds)
        {
            var squaredMagnitude = bounds.size.sqrMagnitude;
            return IsFinite(bounds.center) && IsFinite(bounds.size) &&
                   IsFinite(squaredMagnitude) && squaredMagnitude > 0.00000001f;
        }

        internal static bool IsFinite(Vector3 value)
            => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        internal static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

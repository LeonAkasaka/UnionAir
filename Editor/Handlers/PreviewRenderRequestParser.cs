using System;
using System.Collections.Generic;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal static class PreviewRenderRequestParser
    {
        internal const int MaxWidth = 1920;
        internal const int MaxHeight = 1080;
        internal const int MaxFrames = 16;
        internal const float MaxTime = 1000000f;
        internal const float MaxDistance = 1000000f;
        internal const long MaxAggregatePixels = 16L * 1024L * 1024L;

        private static readonly string[] RootFields =
        {
            "target", "scenePath", "focusPath", "width", "height", "format", "quality",
            "times", "view", "background", "lighting", "animation"
        };

        internal static bool TryParse(
            string body,
            bool binaryImage,
            out PreviewRenderRequestModel model,
            out string error)
        {
            model = null;
            if (!RequestBodyReader.TryValidateObjectFields(body, RootFields, out error))
                return false;

            var result = new PreviewRenderRequestModel();
            result.Target = RequestBodyReader.GetObject(body, "target");
            if (result.Target == null)
            {
                error = "Field 'target' must be an object reference.";
                return false;
            }
            if (!RequestBodyReader.TryValidateObjectFields(
                    result.Target,
                    new[] { "type", "value", "scenePath", "assetGuid", "assetPath", "assetType", "localIdentifier" },
                    out error))
                return false;

            if (!TryOptionalString(body, "scenePath", out result.ScenePath, out error) ||
                !TryOptionalString(body, "focusPath", out result.FocusPath, out error))
                return false;

            if (!TryIntInRange(body, "width", 1, MaxWidth, ref result.Width, out error) ||
                !TryIntInRange(body, "height", 1, MaxHeight, ref result.Height, out error) ||
                !TryIntInRange(body, "quality", 1, 100, ref result.Quality, out error))
                return false;

            if (!TryOptionalString(body, "format", out var format, out error))
                return false;
            if (format != null)
            {
                result.Format = format.ToLowerInvariant();
                if (result.Format != "png" && result.Format != "jpeg")
                {
                    error = "Field 'format' must be 'png' or 'jpeg'.";
                    return false;
                }
            }

            if (RequestBodyReader.HasTopLevelField(body, "times"))
            {
                if (!RequestBodyReader.TryGetFloatArray(body, "times", out result.Times, out error))
                    return false;
                if (result.Times.Length == 0 || result.Times.Length > MaxFrames)
                {
                    error = "Field 'times' must contain between 1 and 16 values.";
                    return false;
                }
                for (var i = 0; i < result.Times.Length; i++)
                {
                    if (result.Times[i] < 0f || result.Times[i] > MaxTime)
                    {
                        error = "times[" + i + "] must be between 0 and 1000000.";
                        return false;
                    }
                }
            }

            if (binaryImage && result.Times.Length != 1)
            {
                error = "The /image endpoint requires exactly one time.";
                return false;
            }

            if ((long)result.Width * result.Height * result.Times.Length > MaxAggregatePixels)
            {
                error = "The request exceeds the 16777216 aggregate-pixel limit.";
                return false;
            }

            var view = RequestBodyReader.GetObject(body, "view");
            if (RequestBodyReader.HasTopLevelField(body, "view") && view == null)
            {
                error = "Field 'view' must be an object.";
                return false;
            }
            if (view != null && !TryParseView(view, result.View, out error))
                return false;

            var background = RequestBodyReader.GetObject(body, "background");
            if (RequestBodyReader.HasTopLevelField(body, "background") && background == null)
            {
                error = "Field 'background' must be a colour object.";
                return false;
            }
            if (background != null && !TryParseColor(background, "background", result.Background, out result.Background, out error))
                return false;

            var lighting = RequestBodyReader.GetObject(body, "lighting");
            if (RequestBodyReader.HasTopLevelField(body, "lighting") && lighting == null)
            {
                error = "Field 'lighting' must be an object.";
                return false;
            }
            if (lighting != null && !TryParseLighting(lighting, result.Lighting, out error))
                return false;

            var animation = RequestBodyReader.GetObject(body, "animation");
            if (RequestBodyReader.HasTopLevelField(body, "animation") && animation == null)
            {
                error = "Field 'animation' must be an object.";
                return false;
            }
            if (animation != null && !TryParseAnimation(animation, result.Animation, out error))
                return false;

            model = result;
            error = null;
            return true;
        }

        private static bool TryParseView(string json, PreviewViewSettings view, out string error)
        {
            var allowed = new[] { "preset", "yaw", "pitch", "distance", "fieldOfView", "padding" };
            if (!RequestBodyReader.TryValidateObjectFields(json, allowed, out error)) return false;

            if (!TryOptionalString(json, "preset", out var preset, out error)) return false;
            var hasYaw = RequestBodyReader.HasTopLevelField(json, "yaw");
            var hasPitch = RequestBodyReader.HasTopLevelField(json, "pitch");
            if (preset != null && (hasYaw || hasPitch))
            {
                error = "Field 'view' must use either preset or yaw/pitch, not both.";
                return false;
            }

            if (preset != null)
            {
                view.Preset = preset.ToLowerInvariant();
                if (!TryPreset(view.Preset, out view.Yaw, out view.Pitch))
                {
                    error = "Unknown view preset: " + preset + ".";
                    return false;
                }
            }
            else if (hasYaw || hasPitch)
            {
                view.Preset = "custom";
                if (!TryOptionalFloat(json, "yaw", ref view.Yaw, out error) ||
                    !TryOptionalFloat(json, "pitch", ref view.Pitch, out error))
                    return false;
                if (view.Pitch < -90f || view.Pitch > 90f)
                {
                    error = "Field 'view.pitch' must be between -90 and 90.";
                    return false;
                }
            }
            else
            {
                TryPreset(view.Preset, out view.Yaw, out view.Pitch);
            }

            if (RequestBodyReader.HasTopLevelField(json, "distance"))
            {
                var distance = 0f;
                if (!TryOptionalFloat(json, "distance", ref distance, out error)) return false;
                if (distance <= 0f || distance > MaxDistance)
                {
                    error = "Field 'view.distance' must be greater than zero and no more than 1000000.";
                    return false;
                }
                view.Distance = distance;
            }

            if (!TryOptionalFloat(json, "fieldOfView", ref view.FieldOfView, out error) ||
                !TryOptionalFloat(json, "padding", ref view.Padding, out error))
                return false;
            if (view.FieldOfView < 1f || view.FieldOfView > 120f)
            {
                error = "Field 'view.fieldOfView' must be between 1 and 120.";
                return false;
            }
            if (view.Padding < 0f || view.Padding >= 0.5f)
            {
                error = "Field 'view.padding' must be at least zero and less than 0.5.";
                return false;
            }

            return true;
        }

        private static bool TryParseLighting(string json, PreviewLightingSettings lighting, out string error)
        {
            var allowed = new[] { "keyIntensity", "fillIntensity", "keyColor", "fillColor" };
            if (!RequestBodyReader.TryValidateObjectFields(json, allowed, out error)) return false;
            if (!TryOptionalFloat(json, "keyIntensity", ref lighting.KeyIntensity, out error) ||
                !TryOptionalFloat(json, "fillIntensity", ref lighting.FillIntensity, out error))
                return false;
            if (lighting.KeyIntensity < 0f || lighting.KeyIntensity > 8f ||
                lighting.FillIntensity < 0f || lighting.FillIntensity > 8f)
            {
                error = "Lighting intensities must be between 0 and 8.";
                return false;
            }

            var keyColor = RequestBodyReader.GetObject(json, "keyColor");
            if (RequestBodyReader.HasTopLevelField(json, "keyColor") && keyColor == null)
            {
                error = "Field 'lighting.keyColor' must be a colour object.";
                return false;
            }
            if (keyColor != null && !TryParseColor(keyColor, "lighting.keyColor", lighting.KeyColor, out lighting.KeyColor, out error))
                return false;

            var fillColor = RequestBodyReader.GetObject(json, "fillColor");
            if (RequestBodyReader.HasTopLevelField(json, "fillColor") && fillColor == null)
            {
                error = "Field 'lighting.fillColor' must be a colour object.";
                return false;
            }
            return fillColor == null ||
                   TryParseColor(fillColor, "lighting.fillColor", lighting.FillColor, out lighting.FillColor, out error);
        }

        private static bool TryParseAnimation(string json, PreviewAnimationSettings animation, out string error)
        {
            var allowed = new[] { "mode", "clip", "clipName", "state", "layer", "animatorPath", "parameters" };
            if (!RequestBodyReader.TryValidateObjectFields(json, allowed, out error)) return false;
            if (!TryOptionalString(json, "mode", out var mode, out error) || string.IsNullOrEmpty(mode))
            {
                error = error ?? "Field 'animation.mode' is required.";
                return false;
            }

            switch (mode.ToLowerInvariant())
            {
                case "none": animation.Mode = PreviewAnimationMode.None; break;
                case "clip": animation.Mode = PreviewAnimationMode.Clip; break;
                case "state": animation.Mode = PreviewAnimationMode.State; break;
                case "parameters": animation.Mode = PreviewAnimationMode.Parameters; break;
                default:
                    error = "Unknown animation mode: " + mode + ".";
                    return false;
            }

            if (!TryOptionalString(json, "clipName", out animation.ClipName, out error) ||
                !TryOptionalString(json, "state", out animation.State, out error) ||
                !TryOptionalString(json, "animatorPath", out animation.AnimatorPath, out error))
                return false;

            if (!TryIntInRange(json, "layer", 0, int.MaxValue, ref animation.Layer, out error))
                return false;

            animation.Clip = RequestBodyReader.GetObject(json, "clip");
            if (RequestBodyReader.HasTopLevelField(json, "clip") && animation.Clip == null)
            {
                error = "Field 'animation.clip' must be an asset reference object.";
                return false;
            }
            if (animation.Clip != null &&
                !RequestBodyReader.TryValidateObjectFields(
                    animation.Clip,
                    new[] { "assetGuid", "assetPath", "assetType", "localIdentifier" },
                    out error))
                return false;

            if (!RequestBodyReader.TryGetArrayElements(
                    json, "parameters", out var parameters, out var parametersPresent, out error))
                return false;

            var names = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                if (RequestBodyReader.GetObject("{\"item\":" + parameter + "}", "item") == null ||
                    !RequestBodyReader.TryValidateObjectFields(parameter, new[] { "name", "value" }, out error))
                {
                    error = "parameters[" + i + "] must be an object. " + (error ?? "");
                    return false;
                }
                if (!TryOptionalString(parameter, "name", out var name, out error) || string.IsNullOrEmpty(name))
                {
                    error = "parameters[" + i + "].name must be a non-empty string.";
                    return false;
                }
                if (!RequestBodyReader.HasTopLevelField(parameter, "value"))
                {
                    error = "parameters[" + i + "].value is required.";
                    return false;
                }
                if (!names.Add(name))
                {
                    error = "Duplicate Animator parameter: " + name + ".";
                    return false;
                }
                animation.Parameters.Add(new PreviewAnimatorParameterRequest
                {
                    Name = name,
                    RawValue = parameter
                });
            }

            if (animation.Mode == PreviewAnimationMode.Clip && animation.Clip == null)
            {
                error = "Animation mode 'clip' requires animation.clip.";
                return false;
            }
            if (animation.Mode == PreviewAnimationMode.State && string.IsNullOrEmpty(animation.State))
            {
                error = "Animation mode 'state' requires animation.state.";
                return false;
            }
            if (animation.Mode == PreviewAnimationMode.Parameters && (!parametersPresent || animation.Parameters.Count == 0))
            {
                error = "Animation mode 'parameters' requires at least one parameter.";
                return false;
            }

            if (animation.Mode != PreviewAnimationMode.Clip && animation.Clip != null ||
                animation.Mode != PreviewAnimationMode.Clip && animation.ClipName != null ||
                animation.Mode != PreviewAnimationMode.State && animation.State != null ||
                animation.Mode != PreviewAnimationMode.Parameters && parametersPresent ||
                animation.Mode != PreviewAnimationMode.State && RequestBodyReader.HasTopLevelField(json, "layer"))
            {
                error = "Animation fields must match animation.mode.";
                return false;
            }

            return true;
        }

        private static bool TryParseColor(
            string json, string label, Color fallback, out Color color, out string error)
        {
            color = fallback;
            if (!RequestBodyReader.TryValidateObjectFields(json, new[] { "r", "g", "b", "a" }, out error))
                return false;

            var r = fallback.r;
            var g = fallback.g;
            var b = fallback.b;
            var a = fallback.a;
            if (!TryRequiredFloat(json, "r", label, ref r, out error) ||
                !TryRequiredFloat(json, "g", label, ref g, out error) ||
                !TryRequiredFloat(json, "b", label, ref b, out error) ||
                !TryOptionalFloat(json, "a", ref a, out error))
                return false;
            if (r < 0f || r > 1f || g < 0f || g > 1f || b < 0f || b > 1f || a < 0f || a > 1f)
            {
                error = "Fields in '" + label + "' must be between 0 and 1.";
                return false;
            }
            color = new Color(r, g, b, a);
            return true;
        }

        private static bool TryPreset(string preset, out float yaw, out float pitch)
        {
            yaw = 0f;
            pitch = 0f;
            switch (preset)
            {
                case "front": return true;
                case "back": yaw = 180f; return true;
                case "left": yaw = -90f; return true;
                case "right": yaw = 90f; return true;
                case "top": pitch = 90f; return true;
                case "bottom": pitch = -90f; return true;
                case "isometric": yaw = 45f; pitch = 30f; return true;
                default: return false;
            }
        }

        private static bool TryOptionalString(string json, string key, out string value, out string error)
        {
            error = null;
            if (RequestBodyReader.TryGetStringValue(json, key, out value, out var present)) return true;
            error = "Field '" + key + "' must be a string.";
            return false;
        }

        private static bool TryIntInRange(
            string json, string key, int min, int max, ref int value, out string error)
        {
            error = null;
            if (!RequestBodyReader.TryGetIntValue(json, key, out var parsed, out var present))
            {
                error = "Field '" + key + "' must be an integer.";
                return false;
            }
            if (!present) return true;
            if (parsed < min || parsed > max)
            {
                error = "Field '" + key + "' must be between " + min + " and " + max + ".";
                return false;
            }
            value = parsed;
            return true;
        }

        private static bool TryOptionalFloat(string json, string key, ref float value, out string error)
        {
            error = null;
            if (!RequestBodyReader.TryGetFloatValue(json, key, out var parsed, out var present))
            {
                error = "Field '" + key + "' must be a finite number.";
                return false;
            }
            if (present) value = parsed;
            return true;
        }

        private static bool TryRequiredFloat(
            string json, string key, string parent, ref float value, out string error)
        {
            if (!TryOptionalFloat(json, key, ref value, out error)) return false;
            if (RequestBodyReader.HasTopLevelField(json, key)) return true;
            error = "Field '" + parent + "." + key + "' is required.";
            return false;
        }
    }
}

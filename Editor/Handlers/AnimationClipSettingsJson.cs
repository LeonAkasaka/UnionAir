using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Reads and writes <see cref="AnimationClipSettings"/>, which is everything the
    /// Animation Inspector shows above the curve list.
    ///
    /// None of it was reachable, and <c>wrapMode</c> -- the one clip field the read did
    /// report -- is not a substitute for any of it. Measured on 6000.0.80f1, an imported
    /// idle clip reports <c>"wrapMode": "Default"</c> while whether it loops is
    /// <c>loopTime</c>, which the Inspector labels Loop Time. A client reading
    /// <c>wrapMode</c> to find out whether a clip loops gets an answer about something
    /// else.
    /// </summary>
    internal static class AnimationClipSettingsJson
    {
        /// <summary>Every field the endpoints accept, so an unknown one can be rejected by name.</summary>
        internal static readonly string[] Fields =
        {
            "loopTime",
            "loopBlend",
            "cycleOffset",
            "loopBlendOrientation",
            "loopBlendPositionY",
            "loopBlendPositionXZ",
            "keepOriginalOrientation",
            "keepOriginalPositionY",
            "keepOriginalPositionXZ",
            "heightFromFeet",
            "mirror",
            "level",
            "orientationOffsetY",
            "startTime",
            "stopTime",
            "additiveReferencePoseTime",
            "hasAdditiveReferencePose",
        };

        internal static void Append(StringBuilder sb, AnimationClipSettings s)
        {
            sb.Append("{");
            sb.Append($"\"loopTime\":{RestResponse.FormatBool(s.loopTime)},");
            sb.Append($"\"loopBlend\":{RestResponse.FormatBool(s.loopBlend)},");
            sb.Append($"\"cycleOffset\":{RestResponse.FormatFloat(s.cycleOffset)},");
            sb.Append($"\"loopBlendOrientation\":{RestResponse.FormatBool(s.loopBlendOrientation)},");
            sb.Append($"\"loopBlendPositionY\":{RestResponse.FormatBool(s.loopBlendPositionY)},");
            sb.Append($"\"loopBlendPositionXZ\":{RestResponse.FormatBool(s.loopBlendPositionXZ)},");
            sb.Append($"\"keepOriginalOrientation\":{RestResponse.FormatBool(s.keepOriginalOrientation)},");
            sb.Append($"\"keepOriginalPositionY\":{RestResponse.FormatBool(s.keepOriginalPositionY)},");
            sb.Append($"\"keepOriginalPositionXZ\":{RestResponse.FormatBool(s.keepOriginalPositionXZ)},");
            sb.Append($"\"heightFromFeet\":{RestResponse.FormatBool(s.heightFromFeet)},");
            sb.Append($"\"mirror\":{RestResponse.FormatBool(s.mirror)},");
            sb.Append($"\"level\":{RestResponse.FormatFloat(s.level)},");
            sb.Append($"\"orientationOffsetY\":{RestResponse.FormatFloat(s.orientationOffsetY)},");
            sb.Append($"\"startTime\":{RestResponse.FormatFloat(s.startTime)},");
            sb.Append($"\"stopTime\":{RestResponse.FormatFloat(s.stopTime)},");
            sb.Append($"\"additiveReferencePoseTime\":{RestResponse.FormatFloat(s.additiveReferencePoseTime)},");
            sb.Append($"\"hasAdditiveReferencePose\":{RestResponse.FormatBool(s.hasAdditiveReferencePose)}");
            sb.Append("}");
        }

        /// <summary>
        /// Applies the subset of settings a request carries onto a copy of the clip's
        /// current settings, leaving everything it does not name untouched.
        ///
        /// Validated in full before the caller writes anything: the struct is applied in
        /// one <c>SetAnimationClipSettings</c> call, so a value rejected halfway would
        /// otherwise mean a partly-updated struct or none at all depending on field order.
        /// </summary>
        /// <param name="applied">Names of the fields the request set.</param>
        internal static bool TryApply(
            string json, AnimationClipSettings current, UnionAirResponse response,
            out AnimationClipSettings updated, out List<string> applied)
        {
            updated = current;
            applied = new List<string>();

            if (!RequestBodyReader.TryValidateObjectFields(json, Fields, out var fieldError))
            {
                RestResponse.SendError(response, "settings: " + fieldError, 400);
                return false;
            }

            if (!ReadBool(json, "loopTime", response, ref updated.loopTime, applied)) return false;
            if (!ReadBool(json, "loopBlend", response, ref updated.loopBlend, applied)) return false;
            if (!ReadFloat(json, "cycleOffset", response, ref updated.cycleOffset, applied)) return false;
            if (!ReadBool(json, "loopBlendOrientation", response, ref updated.loopBlendOrientation, applied)) return false;
            if (!ReadBool(json, "loopBlendPositionY", response, ref updated.loopBlendPositionY, applied)) return false;
            if (!ReadBool(json, "loopBlendPositionXZ", response, ref updated.loopBlendPositionXZ, applied)) return false;
            if (!ReadBool(json, "keepOriginalOrientation", response, ref updated.keepOriginalOrientation, applied)) return false;
            if (!ReadBool(json, "keepOriginalPositionY", response, ref updated.keepOriginalPositionY, applied)) return false;
            if (!ReadBool(json, "keepOriginalPositionXZ", response, ref updated.keepOriginalPositionXZ, applied)) return false;
            if (!ReadBool(json, "heightFromFeet", response, ref updated.heightFromFeet, applied)) return false;
            if (!ReadBool(json, "mirror", response, ref updated.mirror, applied)) return false;
            if (!ReadFloat(json, "level", response, ref updated.level, applied)) return false;
            if (!ReadFloat(json, "orientationOffsetY", response, ref updated.orientationOffsetY, applied)) return false;
            if (!ReadFloat(json, "startTime", response, ref updated.startTime, applied)) return false;
            if (!ReadFloat(json, "stopTime", response, ref updated.stopTime, applied)) return false;
            if (!ReadFloat(json, "additiveReferencePoseTime", response, ref updated.additiveReferencePoseTime, applied)) return false;
            if (!ReadBool(json, "hasAdditiveReferencePose", response, ref updated.hasAdditiveReferencePose, applied)) return false;

            return true;
        }

        private static bool ReadBool(
            string json, string field, UnionAirResponse response, ref bool target, List<string> applied)
        {
            if (!RequestBodyReader.TryGetBoolValue(json, field, out var value, out var present))
            {
                RestResponse.SendError(response, $"settings.{field} must be a boolean.", 400);
                return false;
            }
            if (!present) return true;

            target = value;
            applied.Add(field);
            return true;
        }

        private static bool ReadFloat(
            string json, string field, UnionAirResponse response, ref float target, List<string> applied)
        {
            if (!RequestBodyReader.TryGetFloatValue(json, field, out var value, out var present))
            {
                RestResponse.SendError(response, $"settings.{field} must be a number.", 400);
                return false;
            }
            if (!present) return true;

            target = value;
            applied.Add(field);
            return true;
        }
    }
}

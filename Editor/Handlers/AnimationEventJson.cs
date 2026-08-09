using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Reads and writes <see cref="AnimationEvent"/>, which is how a clip triggers
    /// gameplay: footsteps, hit frames, VFX spawns. A clip authored entirely through this
    /// API could not carry a single one.
    ///
    /// The list is replaced wholesale rather than edited entry by entry. Unity stores the
    /// events as an ordered array with no identity per entry and rewrites the whole thing
    /// through <c>SetAnimationEvents</c>; addressing one would mean inventing an identity
    /// the format does not have, which is the mistake #67 had to undo for transitions.
    /// </summary>
    internal static class AnimationEventJson
    {
        private static readonly string[] Fields =
        {
            "time",
            "functionName",
            "stringParameter",
            "floatParameter",
            "intParameter",
            "objectReferenceParameter",
            "messageOptions",
        };

        internal static void Append(StringBuilder sb, AnimationEvent[] events)
        {
            sb.Append("[");
            for (int i = 0; i < events.Length; i++)
            {
                if (i > 0) sb.Append(",");
                var e = events[i];
                sb.Append("{");
                sb.Append($"\"time\":{RestResponse.FormatFloat(e.time)},");
                sb.Append("\"functionName\":").Append(RestResponse.FormatNullableString(e.functionName)).Append(",");
                sb.Append("\"stringParameter\":").Append(RestResponse.FormatNullableString(e.stringParameter)).Append(",");
                sb.Append($"\"floatParameter\":{RestResponse.FormatFloat(e.floatParameter)},");
                sb.Append($"\"intParameter\":{e.intParameter},");

                sb.Append("\"objectReferenceParameter\":");
                if (e.objectReferenceParameter == null)
                {
                    sb.Append("null");
                }
                else
                {
                    var path = AssetDatabase.GetAssetPath(e.objectReferenceParameter);
                    var guid = string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
                    sb.Append("{\"guid\":").Append(RestResponse.FormatNullableString(guid));
                    sb.Append(",\"name\":").Append(RestResponse.FormatNullableString(e.objectReferenceParameter.name));
                    sb.Append("}");
                }

                sb.Append($",\"messageOptions\":\"{e.messageOptions}\"");
                sb.Append("}");
            }
            sb.Append("]");
        }

        /// <summary>
        /// Parses the events a request carries, without touching the clip.
        ///
        /// Every element is resolved and checked before the caller writes any of them, so a
        /// list whose fourth entry names a missing asset replaces nothing.
        /// </summary>
        internal static bool TryParse(
            string body, UnionAirResponse response, out AnimationEvent[] events, out bool present)
        {
            events = null;

            if (!RequestBodyReader.TryGetArrayElements(body, "events", out var elements, out present, out var arrayError))
            {
                RestResponse.SendError(response, arrayError, 400);
                return false;
            }
            if (!present) return true;

            var parsed = new List<AnimationEvent>();
            for (int i = 0; i < elements.Count; i++)
            {
                var json = elements[i];
                if (!RequestBodyReader.TryValidateObjectFields(json, Fields, out var fieldError))
                {
                    RestResponse.SendError(response, $"events[{i}]: {fieldError}", 400);
                    return false;
                }

                var functionName = RequestBodyReader.GetString(json, "functionName");
                if (string.IsNullOrEmpty(functionName))
                {
                    RestResponse.SendError(response, $"events[{i}] requires functionName.", 400);
                    return false;
                }

                if (!RequestBodyReader.TryGetFloatValue(json, "time", out var time, out var hasTime))
                {
                    RestResponse.SendError(response, $"events[{i}].time must be a number.", 400);
                    return false;
                }
                if (!hasTime)
                {
                    RestResponse.SendError(response, $"events[{i}] requires time.", 400);
                    return false;
                }

                var e = new AnimationEvent { time = time, functionName = functionName };

                if (!RequestBodyReader.TryGetStringValue(json, "stringParameter", out var s, out var hasString))
                {
                    RestResponse.SendError(response, $"events[{i}].stringParameter must be a string.", 400);
                    return false;
                }
                if (hasString) e.stringParameter = s;

                if (!RequestBodyReader.TryGetFloatValue(json, "floatParameter", out var f, out var hasFloat))
                {
                    RestResponse.SendError(response, $"events[{i}].floatParameter must be a number.", 400);
                    return false;
                }
                if (hasFloat) e.floatParameter = f;

                if (!RequestBodyReader.TryGetIntValue(json, "intParameter", out var n, out var hasInt))
                {
                    RestResponse.SendError(response, $"events[{i}].intParameter must be an integer.", 400);
                    return false;
                }
                if (hasInt) e.intParameter = n;

                if (!RequestBodyReader.TryGetObjectOrNullValue(
                        json, "objectReferenceParameter", out var refJson, out var refIsNull, out var hasRef))
                {
                    RestResponse.SendError(response,
                        $"events[{i}].objectReferenceParameter must be an object such as {{\"guid\":\"...\"}} or null.", 400);
                    return false;
                }
                if (hasRef && !refIsNull)
                {
                    var refGuid = RequestBodyReader.GetString(refJson, "guid");
                    if (string.IsNullOrEmpty(refGuid))
                    {
                        RestResponse.SendError(response, $"events[{i}].objectReferenceParameter requires a guid.", 400);
                        return false;
                    }
                    var refPath = AssetDatabase.GUIDToAssetPath(refGuid);
                    var obj = string.IsNullOrEmpty(refPath)
                        ? null
                        : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(refPath);
                    if (obj == null)
                    {
                        RestResponse.SendNotFound(response,
                            $"events[{i}].objectReferenceParameter: no asset found for GUID {refGuid}");
                        return false;
                    }
                    e.objectReferenceParameter = obj;
                }

                if (!RequestBodyReader.TryGetStringValue(json, "messageOptions", out var options, out var hasOptions))
                {
                    RestResponse.SendError(response, $"events[{i}].messageOptions must be a string.", 400);
                    return false;
                }
                if (hasOptions)
                {
                    if (!TryParseMessageOptions(options, out var parsedOptions))
                    {
                        RestResponse.SendError(response,
                            $"Unknown messageOptions in events[{i}]: {options}. " +
                            "Use DontRequireReceiver or RequireReceiver.", 400);
                        return false;
                    }
                    e.messageOptions = parsedOptions;
                }

                parsed.Add(e);
            }

            events = parsed.ToArray();
            return true;
        }

        private static bool TryParseMessageOptions(string value, out SendMessageOptions options)
        {
            switch ((value ?? "").ToLowerInvariant())
            {
                case "dontrequirereceiver": options = SendMessageOptions.DontRequireReceiver; return true;
                case "requirereceiver": options = SendMessageOptions.RequireReceiver; return true;
            }
            options = SendMessageOptions.DontRequireReceiver;
            return false;
        }
    }
}

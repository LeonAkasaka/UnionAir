using System.Collections.Generic;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Validates the optional <c>inputs</c> list of <c>POST /api/editor/play</c> and turns it into
    /// scheduled event specs.
    /// </summary>
    /// <remarks>
    /// The whole list is validated before Play mode is entered, because entering Play mode causes a
    /// domain reload and the HTTP response has already been sent by then: an event rejected later
    /// could not be reported to the caller. Every failure therefore names the offending element as
    /// <c>inputs[index]</c> so a client knows which entry to fix.
    /// </remarks>
    internal static class InputReplayRequestParser
    {
        internal const string InputsField = "inputs";

        /// <summary>
        /// Parses and validates the replay list.
        /// </summary>
        /// <param name="body">Raw request body.</param>
        /// <param name="events">The validated specs in request order, empty when absent.</param>
        /// <param name="present">Whether the body carried an <c>inputs</c> field at all.</param>
        /// <param name="error">Failure description naming the offending element, or null.</param>
        /// <returns>True when the field is absent or wholly valid; otherwise false.</returns>
        internal static bool TryParse(
            string body,
            out List<InputReplayEventSpec> events,
            out bool present,
            out string error)
        {
            events = new List<InputReplayEventSpec>();

            List<string> elements;
            if (!RequestBodyReader.TryGetArrayElements(body, InputsField, out elements, out present, out error))
                return false;
            if (!present) return true;

            if (elements.Count == 0)
            {
                error = "'inputs' must contain at least one event.";
                return false;
            }

            for (var i = 0; i < elements.Count; i++)
            {
                var element = elements[i].TrimStart();
                if (element.Length == 0 || element[0] != '{')
                    return Fail(i, "must be a JSON object.", out error);

                InputReplayEventSpec spec;
                if (!TryParseEvent(elements[i], i, out spec, out error)) return false;
                events.Add(spec);
            }

            error = null;
            return true;
        }

        private static bool TryParseEvent(string element, int index, out InputReplayEventSpec spec, out string error)
        {
            spec = new InputReplayEventSpec();

            if (!RequestBodyReader.HasTopLevelField(element, "frame"))
                return Fail(index, "'frame' is required.", out error);

            var frame = RequestBodyReader.GetInt(element, "frame");
            if (!frame.HasValue || frame.Value < 0)
                return Fail(index, "'frame' must be a non-negative integer.", out error);
            spec.frame = frame.Value;

            var type = RequestBodyReader.GetString(element, "type");
            if (string.IsNullOrEmpty(type))
                return Fail(index, "'type' is required.", out error);
            spec.type = type;

            // holdFrames expresses duration inside a single immediate operation. On a timeline
            // duration is expressed by placing release on a later frame, so accepting it here
            // would give the same thing two spellings.
            if (RequestBodyReader.HasTopLevelField(element, "holdFrames"))
                return Fail(index, "'holdFrames' is not supported in a replay. Schedule 'release' on a later frame.", out error);

            switch (type)
            {
                case InputReplayEventType.Perform: return TryParsePerform(element, index, spec, out error);
                case InputReplayEventType.Set:     return TryParseSet(element, index, spec, out error);
                case InputReplayEventType.Pointer: return TryParsePointer(element, index, spec, out error);
                default:
                    return Fail(index, $"Unknown type '{type}'. Expected perform, set, or pointer.", out error);
            }
        }

        private static bool TryParsePerform(string element, int index, InputReplayEventSpec spec, out string error)
        {
            var action = RequestBodyReader.GetString(element, "action");
            if (string.IsNullOrEmpty(action))
                return Fail(index, "'action' is required for type 'perform'.", out error);
            spec.action = action;

            if (RequestBodyReader.HasTopLevelField(element, "value"))
                return Fail(index, "Button perform uses 'mode', not 'value'.", out error);

            var mode = RequestBodyReader.GetString(element, "mode");
            if (string.IsNullOrEmpty(mode))
                return Fail(index, "'mode' is required for type 'perform'. Expected press or release.", out error);
            if (mode == "tap")
                return Fail(index, "'tap' is not supported in a replay. Schedule 'press' and a later 'release'.", out error);
            if (mode != "press" && mode != "release")
                return Fail(index, $"Invalid mode '{mode}'. Expected press or release.", out error);

            spec.mode = mode;
            error = null;
            return true;
        }

        private static bool TryParseSet(string element, int index, InputReplayEventSpec spec, out string error)
        {
            var action = RequestBodyReader.GetString(element, "action");
            if (string.IsNullOrEmpty(action))
                return Fail(index, "'action' is required for type 'set'.", out error);
            spec.action = action;

            if (RequestBodyReader.HasTopLevelField(element, "mode"))
                return Fail(index, "Type 'set' uses 'value', not 'mode'.", out error);

            if (!RequestBodyReader.HasTopLevelField(element, "value"))
                return Fail(index, "'value' is required for type 'set'.", out error);

            // A Vector2/Stick action takes [x, y]; an Axis action takes a bare number.
            if (RequestBodyReader.GetRawArray(element, "value") != null)
            {
                float[] values;
                string arrayError;
                if (!RequestBodyReader.TryGetFloatArray(element, "value", out values, out arrayError))
                    return Fail(index, arrayError, out error);
                if (values.Length != 2)
                    return Fail(index, "'value' must be [x, y] for a Vector2 action.", out error);

                spec.valueKind = InputReplayValueKind.Vector2;
                spec.valueX = values[0];
                spec.valueY = values[1];
                error = null;
                return true;
            }

            var scalar = RequestBodyReader.GetFloat(element, "value");
            if (!scalar.HasValue || float.IsNaN(scalar.Value) || float.IsInfinity(scalar.Value))
                return Fail(index, "'value' must be a finite number or [x, y].", out error);

            spec.valueKind = InputReplayValueKind.Scalar;
            spec.valueX = scalar.Value;
            error = null;
            return true;
        }

        private static bool TryParsePointer(string element, int index, InputReplayEventSpec spec, out string error)
        {
            var mode = RequestBodyReader.GetString(element, "mode");
            if (string.IsNullOrEmpty(mode))
                return Fail(index, "'mode' is required for type 'pointer'. Expected press, release, or move.", out error);
            if (mode == "tap")
                return Fail(index, "'tap' is not supported in a replay. Schedule 'press' and a later 'release'.", out error);
            if (mode != "press" && mode != "release" && mode != "move")
                return Fail(index, $"Invalid mode '{mode}'. Expected press, release, or move.", out error);
            spec.mode = mode;

            var button = RequestBodyReader.GetString(element, "button") ?? "left";
            if (button != "left" && button != "right" && button != "middle")
                return Fail(index, $"Invalid button '{button}'. Expected left, right, or middle.", out error);
            spec.button = button;

            // Presence, not validity: a malformed coordinate must reach ScreenPointUtils and be
            // rejected there, rather than passing as "no coordinate given" and releasing at
            // wherever the virtual mouse happened to be.
            var hasPosition =
                RequestBodyReader.HasTopLevelField(element, "position") ||
                RequestBodyReader.HasTopLevelField(element, "normalizedPosition");

            if (!hasPosition)
            {
                // Only a release may reuse wherever the virtual mouse already is.
                if (mode != "release")
                    return Fail(index, "'position' or 'normalizedPosition' is required for pointer press and move.", out error);

                spec.pointKind = InputReplayPointKind.None;
                error = null;
                return true;
            }

            ScreenPointRequest point;
            string pointError;
            int statusCode;
            if (!ScreenPointUtils.TryParse(element, out point, out pointError, out statusCode))
                return Fail(index, pointError, out error);

            spec.pointKind = point.IsNormalized ? InputReplayPointKind.Normalized : InputReplayPointKind.Pixel;
            spec.pointX = point.X;
            spec.pointY = point.Y;
            spec.originTopLeft = point.TopLeft;
            error = null;
            return true;
        }

        private static bool Fail(int index, string message, out string error)
        {
            error = $"{InputsField}[{index}]: {message}";
            return false;
        }
    }
}

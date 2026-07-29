using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// A screen point as requested by a client, before it is resolved against a
    /// particular Game view resolution.
    /// </summary>
    internal readonly struct ScreenPointRequest
    {
        public ScreenPointRequest(bool isNormalized, bool topLeft, float x, float y)
        {
            IsNormalized = isNormalized;
            TopLeft = topLeft;
            X = x;
            Y = y;
        }

        /// <summary>Whether the coordinate is normalized (0-1) rather than in pixels.</summary>
        public bool IsNormalized { get; }

        /// <summary>Whether the coordinate uses a top-left origin rather than Unity's bottom-left.</summary>
        public bool TopLeft { get; }

        public float X { get; }
        public float Y { get; }
    }

    /// <summary>
    /// Shared parsing and resolution for screen-coordinate request bodies.
    /// Accepts either a pixel <c>position</c> or a <c>normalizedPosition</c> (0-1),
    /// with an optional <c>origin</c> of <c>bottomLeft</c> (Unity-native, default)
    /// or <c>topLeft</c> (matches screenshots from <c>/api/editor/capture</c>).
    /// </summary>
    /// <remarks>
    /// Parsing and resolution are separate so that both can be exercised without a Game view:
    /// <see cref="TryParse"/> covers every malformed-input case and never reads <c>Screen</c>, and
    /// <see cref="Resolve"/> takes the resolution as arguments.
    /// </remarks>
    internal static class ScreenPointUtils
    {
        /// <summary>
        /// Parses the screen point described by <paramref name="body"/> without resolving it
        /// against a resolution. Returns false with an error message and HTTP status code 400
        /// when the body is malformed.
        /// </summary>
        public static bool TryParse(
            string body,
            out ScreenPointRequest point,
            out string error,
            out int statusCode)
        {
            point = default(ScreenPointRequest);
            error = null;
            statusCode = 200;

            // Presence and validity are separate questions. Asking GetObject alone would read a
            // malformed value such as "position": 5 or "position": null as an absent field, which
            // turns a typo into silently different behaviour instead of an error.
            var hasPosition = RequestBodyReader.HasTopLevelField(body, "position");
            var hasNormalized = RequestBodyReader.HasTopLevelField(body, "normalizedPosition");

            if (hasPosition && hasNormalized)
            {
                error = "Provide either 'position' or 'normalizedPosition', not both.";
                statusCode = 400;
                return false;
            }
            if (!hasPosition && !hasNormalized)
            {
                error = "Required field 'position' or 'normalizedPosition' is missing.";
                statusCode = 400;
                return false;
            }

            var field = hasPosition ? "position" : "normalizedPosition";
            var pointJson = RequestBodyReader.GetObject(body, field);
            if (pointJson == null)
            {
                error = $"'{field}' must be an object with 'x' and 'y'.";
                statusCode = 400;
                return false;
            }

            var origin = RequestBodyReader.GetString(body, "origin") ?? "bottomLeft";
            bool topLeft;
            if (string.Equals(origin, "bottomLeft", System.StringComparison.OrdinalIgnoreCase))
                topLeft = false;
            else if (string.Equals(origin, "topLeft", System.StringComparison.OrdinalIgnoreCase))
                topLeft = true;
            else
            {
                error = "Invalid origin. Expected bottomLeft or topLeft.";
                statusCode = 400;
                return false;
            }

            var isNormalized = hasNormalized;
            var x = RequestBodyReader.GetFloat(pointJson, "x");
            var y = RequestBodyReader.GetFloat(pointJson, "y");
            if (!x.HasValue || !y.HasValue || !IsFinite(x.Value) || !IsFinite(y.Value))
            {
                error = isNormalized
                    ? "normalizedPosition must include finite 'x' and 'y' values."
                    : "position must include finite 'x' and 'y' pixel values.";
                statusCode = 400;
                return false;
            }

            point = new ScreenPointRequest(isNormalized, topLeft, x.Value, y.Value);
            return true;
        }

        /// <summary>
        /// Resolves a parsed point to a bottom-left-origin pixel coordinate against the given
        /// resolution. Normalized coordinates are clamped; pixel coordinates outside the screen
        /// fail with HTTP status code 422.
        /// </summary>
        public static bool Resolve(
            ScreenPointRequest point,
            int screenWidth,
            int screenHeight,
            out Vector2 screenPos,
            out string error,
            out int statusCode)
        {
            screenPos = default(Vector2);
            error = null;
            statusCode = 200;

            float px, py;
            if (point.IsNormalized)
            {
                var nx = Mathf.Clamp01(point.X);
                var ny = Mathf.Clamp01(point.TopLeft ? 1f - point.Y : point.Y);
                px = nx * screenWidth;
                py = ny * screenHeight;
            }
            else
            {
                px = point.X;
                py = point.TopLeft ? screenHeight - point.Y : point.Y;
                if (px < 0f || px > screenWidth || py < 0f || py > screenHeight)
                {
                    error = $"Position ({point.X}, {point.Y}) is outside the screen ({screenWidth}x{screenHeight}).";
                    statusCode = 422;
                    return false;
                }
            }

            screenPos = new Vector2(px, py);
            return true;
        }

        /// <summary>
        /// Resolves the screen point described by <paramref name="body"/> to a
        /// bottom-left-origin pixel coordinate against the current Game view
        /// resolution (<c>Screen.width</c>/<c>Screen.height</c>).
        /// Returns false with an error message and HTTP status code
        /// (400 for malformed input, 422 for out-of-bounds pixel positions).
        /// </summary>
        public static bool TryResolve(
            string body,
            out Vector2 screenPos,
            out int screenWidth,
            out int screenHeight,
            out string error,
            out int statusCode)
        {
            screenPos = default(Vector2);
            screenWidth = Screen.width;
            screenHeight = Screen.height;

            ScreenPointRequest point;
            if (!TryParse(body, out point, out error, out statusCode)) return false;

            return Resolve(point, screenWidth, screenHeight, out screenPos, out error, out statusCode);
        }

        static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Shared parsing and resolution for screen-coordinate request bodies.
    /// Accepts either a pixel <c>position</c> or a <c>normalizedPosition</c> (0-1),
    /// with an optional <c>origin</c> of <c>bottomLeft</c> (Unity-native, default)
    /// or <c>topLeft</c> (matches screenshots from <c>/api/editor/capture</c>).
    /// </summary>
    internal static class ScreenPointUtils
    {
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
            error = null;
            statusCode = 200;

            var positionJson = RequestBodyReader.GetObject(body, "position");
            var normalizedJson = RequestBodyReader.GetObject(body, "normalizedPosition");

            if (positionJson != null && normalizedJson != null)
            {
                error = "Provide either 'position' or 'normalizedPosition', not both.";
                statusCode = 400;
                return false;
            }
            if (positionJson == null && normalizedJson == null)
            {
                error = "Required field 'position' or 'normalizedPosition' is missing.";
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

            var pointJson = positionJson ?? normalizedJson;
            var x = RequestBodyReader.GetFloat(pointJson, "x");
            var y = RequestBodyReader.GetFloat(pointJson, "y");
            if (!x.HasValue || !y.HasValue || !IsFinite(x.Value) || !IsFinite(y.Value))
            {
                error = positionJson != null
                    ? "position must include finite 'x' and 'y' pixel values."
                    : "normalizedPosition must include finite 'x' and 'y' values.";
                statusCode = 400;
                return false;
            }

            float px, py;
            if (normalizedJson != null)
            {
                var nx = Mathf.Clamp01(x.Value);
                var ny = Mathf.Clamp01(topLeft ? 1f - y.Value : y.Value);
                px = nx * screenWidth;
                py = ny * screenHeight;
            }
            else
            {
                px = x.Value;
                py = topLeft ? screenHeight - y.Value : y.Value;
                if (px < 0f || px > screenWidth || py < 0f || py > screenHeight)
                {
                    error = $"Position ({x.Value}, {y.Value}) is outside the screen ({screenWidth}x{screenHeight}).";
                    statusCode = 422;
                    return false;
                }
            }

            screenPos = new Vector2(px, py);
            return true;
        }

        static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

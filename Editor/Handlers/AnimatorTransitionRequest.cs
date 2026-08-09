using System.Collections.Generic;
using UnityEditor.Animations;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Request parsing shared by the two kinds of transition.
    ///
    /// <see cref="AnimatorStateTransition"/> and <see cref="AnimatorTransition"/> are
    /// different types with almost nothing in common — one carries timing and interruption,
    /// the other carries a destination and nothing else. Conditions are the exception, and
    /// they are parsed here so the two endpoints cannot drift into two spellings of the
    /// same array.
    /// </summary>
    internal static class AnimatorTransitionRequest
    {
        /// <summary>
        /// Reads the conditions array, which replaces a transition's conditions wholesale.
        ///
        /// An element that does not parse is rejected rather than skipped: skipping used to
        /// produce a transition holding fewer conditions than the request listed, reported
        /// as a plain success. An empty array is a request to clear, and an absent field is
        /// a request to leave alone, which is what <paramref name="present"/> separates.
        /// </summary>
        internal static bool TryParseConditions(
            string body, UnionAirResponse response, out AnimatorCondition[] conditions, out bool present)
        {
            conditions = null;

            if (!RequestBodyReader.TryGetArrayElements(body, "conditions", out var elements, out present, out var arrayError))
            {
                RestResponse.SendError(response, arrayError, 400);
                return false;
            }
            if (!present) return true;

            var parsed = new List<AnimatorCondition>();
            for (int i = 0; i < elements.Count; i++)
            {
                var condJson = elements[i];
                var paramName = RequestBodyReader.GetString(condJson, "parameter");
                var modeStr = RequestBodyReader.GetString(condJson, "mode");
                if (string.IsNullOrEmpty(paramName) || string.IsNullOrEmpty(modeStr))
                {
                    RestResponse.SendError(response, $"conditions[{i}] requires parameter and mode.", 400);
                    return false;
                }

                if (!AnimatorTransitionRules.TryParseConditionMode(modeStr, out var mode))
                {
                    RestResponse.SendError(response,
                        $"Unknown condition mode in conditions[{i}]: {modeStr}. " +
                        $"Use one of {AnimatorTransitionRules.ConditionModeNames}.", 400);
                    return false;
                }

                // Absent means 0, which is what If and IfNot use and what Unity writes for
                // them. Present and unusable -- quoted, null, NaN -- is refused: reading it
                // as 0 would move a Greater threshold to zero and report success.
                if (!RequestBodyReader.TryGetFloatValue(condJson, "threshold", out var threshold, out _))
                {
                    RestResponse.SendError(response, $"conditions[{i}].threshold must be a number.", 400);
                    return false;
                }

                parsed.Add(new AnimatorCondition
                {
                    parameter = paramName,
                    mode = mode,
                    threshold = threshold
                });
            }

            conditions = parsed.ToArray();
            return true;
        }
    }
}

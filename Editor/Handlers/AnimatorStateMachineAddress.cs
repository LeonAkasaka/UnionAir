using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Reads and resolves the <c>stateMachinePath</c> a request carries, and answers the
    /// request itself when it does not resolve.
    ///
    /// Kept apart from <see cref="AnimatorStateMachineRules"/>, which decides what a path
    /// means without knowing there is an HTTP request, and shared by both handlers so that
    /// the state endpoints and the state machine endpoints cannot drift into two
    /// interpretations of the same address.
    /// </summary>
    internal static class AnimatorStateMachineAddress
    {
        /// <summary>
        /// Resolves the state machine a request addresses: <c>layerIndex</c> for the layer,
        /// then <c>stateMachinePath</c> from that layer's root.
        ///
        /// An omitted or empty path is the layer's root, which is what every request meant
        /// before the field existed, so nothing that worked changes meaning.
        /// </summary>
        internal static bool TryResolve(
            AnimatorController controller, int layerIndex, string body,
            UnionAirResponse response, out AnimatorStateMachine machine)
        {
            machine = null;

            if (!RequestBodyReader.TryGetStringArray(body, "stateMachinePath", out var path))
            {
                RestResponse.SendError(response,
                    "stateMachinePath must be an array of state machine names, such as [\"Combat\",\"Melee\"].", 400);
                return false;
            }

            var root = controller.layers[layerIndex].stateMachine;
            var result = AnimatorStateMachineRules.TryResolve(root, path, out machine, out var depth, out var matches);

            switch (result)
            {
                case AnimatorStateMachineRules.PathResult.Resolved:
                    return true;

                case AnimatorStateMachineRules.PathResult.Ambiguous:
                    // 409 rather than 400 for the reason #67 gives for transitions: the
                    // request is well formed, and what makes the address unusable is the
                    // controller's own shape.
                    RestResponse.SendError(response,
                        AnimatorStateMachineRules.AmbiguousMessage(path, depth, matches, "stateMachinePath"), 409);
                    machine = null;
                    return false;

                default:
                    RestResponse.SendNotFound(response,
                        AnimatorStateMachineRules.NotFoundMessage(path, depth, "stateMachinePath"));
                    machine = null;
                    return false;
            }
        }

        /// <summary>
        /// Resolves the parent of the addressed machine, for a request that creates or
        /// removes one: the last segment names the machine itself, which for a create does
        /// not exist yet.
        /// </summary>
        /// <param name="path">The full path the request carried, so the caller can name the leaf.</param>
        internal static bool TryResolveParent(
            AnimatorController controller, int layerIndex, string body,
            UnionAirResponse response, out AnimatorStateMachine parent, out string[] path)
        {
            parent = null;

            if (!RequestBodyReader.TryGetStringArray(body, "stateMachinePath", out path))
            {
                RestResponse.SendError(response,
                    "stateMachinePath must be an array of state machine names, such as [\"Combat\",\"Melee\"].", 400);
                return false;
            }

            var parentPath = new string[System.Math.Max(0, path.Length - 1)];
            System.Array.Copy(path, parentPath, parentPath.Length);

            var root = controller.layers[layerIndex].stateMachine;
            var result = AnimatorStateMachineRules.TryResolve(root, parentPath, out parent, out var depth, out var matches);

            switch (result)
            {
                case AnimatorStateMachineRules.PathResult.Resolved:
                    return true;

                case AnimatorStateMachineRules.PathResult.Ambiguous:
                    RestResponse.SendError(response,
                        AnimatorStateMachineRules.AmbiguousMessage(parentPath, depth, matches, "stateMachinePath"), 409);
                    parent = null;
                    return false;

                default:
                    RestResponse.SendNotFound(response,
                        AnimatorStateMachineRules.NotFoundMessage(parentPath, depth, "stateMachinePath"));
                    parent = null;
                    return false;
            }
        }

        /// <summary>
        /// Reads the <c>layerIndex</c> a request addresses, from the body or the query
        /// string, and validates it against the controller.
        ///
        /// The query fallback is not a convenience added here: it is what the transition and
        /// delete endpoints already offer for <c>transitionId</c>, <c>from</c>, <c>to</c>,
        /// and <c>name</c>, and <c>layerIndex</c> is part of the same address. It was
        /// previously read in four places -- one helper with the fallback, one without, and
        /// two validated inline with their own wording -- so <c>?layerIndex=1</c> addressed
        /// layer 1 on the state endpoints and layer 0 on the state machine and blend tree
        /// endpoints, with a 201 either way.
        /// </summary>
        internal static bool TryReadLayerIndex(
            AnimatorController controller, string body, UnionAirRequest request,
            UnionAirResponse response, out int layerIndex)
        {
            layerIndex = 0;

            var fromBody = RequestBodyReader.GetInt(body, "layerIndex");
            if (fromBody.HasValue)
            {
                layerIndex = fromBody.Value;
            }
            else
            {
                // A value that is present and not an integer is refused rather than
                // defaulting to 0, which would act on the base layer and report success.
                var raw = request.QueryString["layerIndex"];
                if (!string.IsNullOrEmpty(raw) &&
                    !int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out layerIndex))
                {
                    RestResponse.SendError(response, $"layerIndex must be an integer: {raw}", 400);
                    return false;
                }
            }

            if (AnimatorLayerRules.TryValidateLayerIndex(layerIndex, controller.layers.Length, out var error))
                return true;

            RestResponse.SendError(response, error, 400);
            return false;
        }

        /// <summary>
        /// Emits a state machine path as the JSON array both sides of the contract use: the
        /// read reports it as <c>path</c>, the writes echo it as <c>stateMachinePath</c>, and
        /// a client feeds one straight back into the other.
        ///
        /// Here rather than in either handler because that round trip only holds while the
        /// two agree exactly. It was written out twice before, identically, which is the
        /// state a format diverges from without failing to compile.
        ///
        /// Not to be confused with a blend tree's <c>childPath</c>, which addresses by
        /// position rather than by name and is emitted by <see cref="BlendTreeHandler"/>.
        /// </summary>
        internal static string PathJson(IReadOnlyList<string> path)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < path.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(RestResponse.FormatNullableString(path[i]));
            }
            return sb.Append("]").ToString();
        }

        internal static AnimatorController LoadController(string guid, UnionAirResponse response)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendNotFound(response, $"No asset found for GUID: {guid}");
                return null;
            }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (controller == null)
                RestResponse.SendError(response, $"Asset is not an AnimatorController: {assetPath}", 400);
            return controller;
        }
    }
}

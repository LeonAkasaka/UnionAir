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
                        AnimatorStateMachineRules.AmbiguousMessage(path, depth, matches), 409);
                    machine = null;
                    return false;

                default:
                    RestResponse.SendNotFound(response,
                        AnimatorStateMachineRules.NotFoundMessage(path, depth));
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
                        AnimatorStateMachineRules.AmbiguousMessage(parentPath, depth, matches), 409);
                    parent = null;
                    return false;

                default:
                    RestResponse.SendNotFound(response,
                        AnimatorStateMachineRules.NotFoundMessage(parentPath, depth));
                    parent = null;
                    return false;
            }
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

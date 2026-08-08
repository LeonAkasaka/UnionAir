using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Serializes an AnimatorState motion to JSON.
    ///
    /// A Motion is either an AnimationClip or a BlendTree, and the two are not
    /// interchangeable to a client: a blend tree is a sub-asset owned by the
    /// controller and has no GUID of its own. Every motion therefore carries a
    /// "type" discriminator, and only a motion that is a distinct asset carries
    /// a non-null "guid".
    /// </summary>
    internal static class MotionJson
    {
        /// <summary>
        /// Deepest blend tree nesting serialized in full. A tree at this depth is
        /// emitted with "truncated": true and no "children", so a client can tell a
        /// boundary from a leaf; an empty "children" array stays available to mean
        /// what it says, since a real blend tree may have no children.
        /// </summary>
        internal const int MaxBlendTreeDepth = 10;

        /// <summary>
        /// Appends a motion as a JSON value, or the literal <c>null</c> when there is none.
        /// </summary>
        /// <param name="clipCountByPath">
        /// Per-response cache for <see cref="CountClipsAtPath"/>. A controller read
        /// serializes one motion per state and several states commonly resolve to the
        /// same imported file, so the sub-asset scan is done once per path.
        /// </param>
        public static void Append(StringBuilder sb, Motion motion, Dictionary<string, int> clipCountByPath)
            => AppendMotion(sb, motion, 0, clipCountByPath);

        private static void AppendMotion(StringBuilder sb, Motion motion, int depth, Dictionary<string, int> clipCountByPath)
        {
            // Unity's overloaded equality reports a destroyed or missing reference as
            // null, so a state whose motion asset was deleted lands here rather than
            // being reported as an Unknown motion.
            if (motion == null)
            {
                sb.Append("null");
                return;
            }

            if (motion is BlendTree tree)
            {
                AppendBlendTree(sb, tree, depth, clipCountByPath);
                return;
            }

            if (motion is AnimationClip clip)
            {
                AppendClip(sb, clip, clipCountByPath);
                return;
            }

            // A Motion subclass this build does not know about. Reported as such rather
            // than forced into one of the two shapes above.
            sb.Append("{\"type\":\"Unknown\",\"guid\":");
            sb.Append(RestResponse.FormatNullableString(GuidOf(AssetDatabase.GetAssetPath(motion))));
            sb.Append(",\"name\":");
            sb.Append(RestResponse.FormatNullableString(motion.name));
            sb.Append("}");
        }

        private static void AppendClip(StringBuilder sb, AnimationClip clip, Dictionary<string, int> clipCountByPath)
        {
            var assetPath = AssetDatabase.GetAssetPath(clip);
            var hasPath = !string.IsNullOrEmpty(assetPath);

            sb.Append("{\"type\":\"AnimationClip\",\"guid\":");
            sb.Append(RestResponse.FormatNullableString(GuidOf(assetPath)));
            sb.Append(",\"name\":");
            sb.Append(RestResponse.FormatNullableString(clip.name));

            // assetPath and clipsAtPath together say how precise the GUID is. An
            // imported clip lives inside a model file, so the GUID identifies the file
            // rather than the clip, and a file holding several takes cannot be
            // addressed take by take. Reporting the count is what lets a client tell an
            // exact reference from an approximate one.
            if (!hasPath)
            {
                sb.Append(",\"assetPath\":null}");
                return;
            }

            sb.Append(",\"assetPath\":");
            sb.Append(RestResponse.FormatNullableString(assetPath));
            sb.Append($",\"clipsAtPath\":{CountClipsAtPath(assetPath, clipCountByPath)}");
            sb.Append("}");
        }

        private static void AppendBlendTree(StringBuilder sb, BlendTree tree, int depth, Dictionary<string, int> clipCountByPath)
        {
            sb.Append("{\"type\":\"BlendTree\",\"guid\":null,\"name\":");
            sb.Append(RestResponse.FormatNullableString(tree.name));

            if (depth >= MaxBlendTreeDepth)
            {
                sb.Append(",\"truncated\":true}");
                return;
            }

            sb.Append($",\"blendType\":\"{tree.blendType}\",\"blendParameter\":");
            sb.Append(RestResponse.FormatNullableString(tree.blendParameter));

            // blendParameterY and a child's directBlendParameter are serialized for every
            // blend type, because Unity stores them for every blend type: a Simple1D tree
            // routinely carries a blendParameterY that the blend never consults. Omitting
            // them where they are unused would hide a value the asset actually holds.
            // Which blend types consult which field is documented rather than inferred here.
            sb.Append(",\"blendParameterY\":");
            sb.Append(RestResponse.FormatNullableString(tree.blendParameterY));

            sb.Append($",\"useAutomaticThresholds\":{RestResponse.FormatBool(tree.useAutomaticThresholds)}");
            sb.Append($",\"minThreshold\":{RestResponse.FormatFloat(tree.minThreshold)}");
            sb.Append($",\"maxThreshold\":{RestResponse.FormatFloat(tree.maxThreshold)}");

            sb.Append(",\"children\":[");
            var children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                if (i > 0) sb.Append(",");
                AppendChild(sb, children[i], depth + 1, clipCountByPath);
            }
            sb.Append("]}");
        }

        private static void AppendChild(StringBuilder sb, ChildMotion child, int depth, Dictionary<string, int> clipCountByPath)
        {
            sb.Append($"{{\"threshold\":{RestResponse.FormatFloat(child.threshold)}");
            sb.Append($",\"position\":{{\"x\":{RestResponse.FormatFloat(child.position.x)},\"y\":{RestResponse.FormatFloat(child.position.y)}}}");
            sb.Append($",\"timeScale\":{RestResponse.FormatFloat(child.timeScale)}");
            sb.Append($",\"cycleOffset\":{RestResponse.FormatFloat(child.cycleOffset)}");
            sb.Append($",\"mirror\":{RestResponse.FormatBool(child.mirror)}");
            sb.Append(",\"directBlendParameter\":");
            sb.Append(RestResponse.FormatNullableString(child.directBlendParameter));
            sb.Append(",\"motion\":");
            AppendMotion(sb, child.motion, depth, clipCountByPath);
            sb.Append("}");
        }

        /// <summary>
        /// Returns the asset GUID for a path, or <c>null</c> when there is none.
        /// An in-memory motion and a sub-asset both land on null rather than on an empty
        /// string, so a client can test one thing to learn whether the motion is fetchable.
        /// </summary>
        private static string GuidOf(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            return string.IsNullOrEmpty(guid) ? null : guid;
        }

        /// <summary>
        /// Counts the AnimationClip objects reachable at an asset path: the main asset
        /// when it is a clip, plus every visible sub-asset that is one.
        /// </summary>
        private static int CountClipsAtPath(string assetPath, Dictionary<string, int> clipCountByPath)
        {
            if (clipCountByPath.TryGetValue(assetPath, out var cached)) return cached;

            var count = AssetDatabase.LoadMainAssetAtPath(assetPath) is AnimationClip ? 1 : 0;
            foreach (var representation in AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath))
            {
                if (representation is AnimationClip) count++;
            }

            clipCountByPath[assetPath] = count;
            return count;
        }
    }
}

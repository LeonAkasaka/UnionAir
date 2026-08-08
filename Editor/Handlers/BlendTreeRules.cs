using System.Collections.Generic;
using UnityEditor.Animations;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// The parts of a blend tree write that can be decided without a live controller:
    /// which blend type a name denotes, and which fields the addressed blend type
    /// actually consults.
    /// </summary>
    internal static class BlendTreeRules
    {
        internal static bool TryParseBlendType(string value, out BlendTreeType type)
        {
            switch ((value ?? "").ToLowerInvariant())
            {
                case "simple1d": type = BlendTreeType.Simple1D; return true;
                case "simpledirectional2d": type = BlendTreeType.SimpleDirectional2D; return true;
                case "freeformdirectional2d": type = BlendTreeType.FreeformDirectional2D; return true;
                case "freeformcartesian2d": type = BlendTreeType.FreeformCartesian2D; return true;
                case "direct": type = BlendTreeType.Direct; return true;
            }
            type = BlendTreeType.Simple1D;
            return false;
        }

        internal static string BlendTypeNames =>
            "Simple1D, SimpleDirectional2D, FreeformDirectional2D, FreeformCartesian2D, Direct";

        internal static bool IsTwoDimensional(BlendTreeType type)
            => type == BlendTreeType.SimpleDirectional2D
            || type == BlendTreeType.FreeformDirectional2D
            || type == BlendTreeType.FreeformCartesian2D;

        /// <summary>
        /// Names the tree-level fields a request set that this tree's blend type does not
        /// consult.
        ///
        /// Stored rather than refused, because Unity stores them too and the read reports
        /// them -- refusing would make the API narrower than the asset. What they must not
        /// do is pass silently.
        /// </summary>
        internal static List<string> CollectIgnoredTreeFields(BlendTreeType type, bool blendParameterYSet)
        {
            var ignored = new List<string>();

            if (blendParameterYSet && !IsTwoDimensional(type))
                ignored.Add($"blendParameterY is stored but not consulted by blendType {type}; it applies to the 2D types.");

            return ignored;
        }

        /// <summary>
        /// Names the child-level fields a request set that will not take effect.
        ///
        /// These are judged against the <em>parent</em>: a child's position is read by the
        /// blend its parent performs, not by anything the child is, and the same holds for
        /// directBlendParameter and for whether a threshold survives.
        /// </summary>
        /// <param name="parentType">Blend type of the tree the child belongs to.</param>
        /// <param name="parentUsesAutomaticThresholds">
        /// Measured on 6000.0.80f1: with automatic thresholds on, a threshold passed to
        /// AddChild came back as 0, so a caller who set one and read it back would find a
        /// number it never sent.
        /// </param>
        internal static List<string> CollectIgnoredChildFields(
            BlendTreeType parentType,
            bool parentUsesAutomaticThresholds,
            bool positionSet,
            bool directBlendParameterSet,
            bool thresholdSet)
        {
            var ignored = new List<string>();

            if (positionSet && !IsTwoDimensional(parentType))
                ignored.Add($"position is stored but not consulted: the parent blendType is {parentType}, " +
                            "and position applies to the 2D types.");

            if (directBlendParameterSet && parentType != BlendTreeType.Direct)
                ignored.Add($"directBlendParameter is stored but not consulted: the parent blendType is {parentType}, " +
                            "and directBlendParameter applies to Direct.");

            if (thresholdSet && parentUsesAutomaticThresholds)
                ignored.Add("threshold is not kept because the parent has useAutomaticThresholds true; Unity recomputes it. " +
                            "Set the parent's useAutomaticThresholds to false to keep a threshold.");

            return ignored;
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the parts of a blend tree write that can be decided without a controller:
    /// which blend type a name denotes, and which fields the addressed blend actually
    /// consults. The second is the one worth pinning down, because a child's position,
    /// directBlendParameter, and threshold are judged against the <em>parent</em> — the
    /// child does not decide whether they are read.
    /// </summary>
    internal sealed class BlendTreeRulesTests
    {
        [TestCase("Simple1D", BlendTreeType.Simple1D)]
        [TestCase("simple1d", BlendTreeType.Simple1D)]
        [TestCase("SimpleDirectional2D", BlendTreeType.SimpleDirectional2D)]
        [TestCase("FreeformDirectional2D", BlendTreeType.FreeformDirectional2D)]
        [TestCase("FreeformCartesian2D", BlendTreeType.FreeformCartesian2D)]
        [TestCase("Direct", BlendTreeType.Direct)]
        public void TryParseBlendType_AcceptsEveryBlendType(string name, BlendTreeType expected)
        {
            Assert.IsTrue(BlendTreeRules.TryParseBlendType(name, out var type));
            Assert.AreEqual(expected, type);
        }

        [TestCase("Nope")]
        [TestCase("")]
        [TestCase(null)]
        public void TryParseBlendType_RejectsAnythingElse(string name)
        {
            Assert.IsFalse(BlendTreeRules.TryParseBlendType(name, out _));
        }

        [Test]
        public void CollectIgnoredTreeFields_ReportsBlendParameterYOnA1DTree()
        {
            var ignored = BlendTreeRules.CollectIgnoredTreeFields(BlendTreeType.Simple1D, blendParameterYSet: true);

            Assert.AreEqual(1, ignored.Count);
            StringAssert.Contains("blendParameterY", ignored[0]);
        }

        [Test]
        public void CollectIgnoredTreeFields_SaysNothingWhenTheTypeConsultsIt()
        {
            Assert.IsEmpty(BlendTreeRules.CollectIgnoredTreeFields(
                BlendTreeType.FreeformCartesian2D, blendParameterYSet: true));
        }

        [Test]
        public void CollectIgnoredChildFields_JudgesPositionAgainstTheParent()
        {
            // A child does not decide whether its position is read; the parent's blend does.
            var ignored = BlendTreeRules.CollectIgnoredChildFields(
                BlendTreeType.Simple1D, parentUsesAutomaticThresholds: false,
                positionSet: true, directBlendParameterSet: false, thresholdSet: false);

            Assert.AreEqual(1, ignored.Count);
            StringAssert.Contains("position", ignored[0]);
            StringAssert.Contains("parent", ignored[0]);
        }

        [Test]
        public void CollectIgnoredChildFields_AcceptsPositionUnderA2DParent()
        {
            Assert.IsEmpty(BlendTreeRules.CollectIgnoredChildFields(
                BlendTreeType.FreeformCartesian2D, parentUsesAutomaticThresholds: false,
                positionSet: true, directBlendParameterSet: false, thresholdSet: false));
        }

        [Test]
        public void CollectIgnoredChildFields_ReportsDirectBlendParameterOutsideDirect()
        {
            var ignored = BlendTreeRules.CollectIgnoredChildFields(
                BlendTreeType.Simple1D, parentUsesAutomaticThresholds: false,
                positionSet: false, directBlendParameterSet: true, thresholdSet: false);

            Assert.AreEqual(1, ignored.Count);
            StringAssert.Contains("directBlendParameter", ignored[0]);
        }

        [Test]
        public void CollectIgnoredChildFields_ReportsAThresholdTheParentWillRecompute()
        {
            // Measured on 6000.0.80f1: AddChild(null, 0.25f) under an automatic-threshold
            // parent stored 0. A caller who set a threshold and read it back would find a
            // number it never sent, so the response has to say so.
            var ignored = BlendTreeRules.CollectIgnoredChildFields(
                BlendTreeType.Simple1D, parentUsesAutomaticThresholds: true,
                positionSet: false, directBlendParameterSet: false, thresholdSet: true);

            Assert.AreEqual(1, ignored.Count);
            StringAssert.Contains("useAutomaticThresholds", ignored[0]);
        }

        [Test]
        public void CollectIgnoredChildFields_SaysNothingWhenThresholdsAreManual()
        {
            Assert.IsEmpty(BlendTreeRules.CollectIgnoredChildFields(
                BlendTreeType.Simple1D, parentUsesAutomaticThresholds: false,
                positionSet: false, directBlendParameterSet: false, thresholdSet: true));
        }

        [Test]
        public void CollectTrees_WalksTheWholeSubtree()
        {
            // What DELETE relies on to clean up after RemoveChild, which detaches a subtree
            // and leaves every tree in it in the asset.
            var root = new BlendTree { name = "Root" };
            var kid = new BlendTree { name = "Kid" };
            var grand = new BlendTree { name = "Grand" };
            try
            {
                kid.children = new[] { new ChildMotion { motion = grand, timeScale = 1f } };
                root.children = new[]
                {
                    new ChildMotion { motion = kid, timeScale = 1f },
                    new ChildMotion { motion = null, timeScale = 1f }
                };

                var found = new List<BlendTree>();
                BlendTreeHandler.CollectTrees(root, found);

                Assert.AreEqual(new[] { root, kid, grand }, found);
            }
            finally
            {
                Object.DestroyImmediate(grand);
                Object.DestroyImmediate(kid);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CollectTrees_IgnoresAClipChildAndANullTree()
        {
            var found = new List<BlendTree>();
            BlendTreeHandler.CollectTrees(null, found);
            Assert.IsEmpty(found);
        }
    }
}

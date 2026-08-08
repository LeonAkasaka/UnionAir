using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the motion serializer against constructed motions. Everything here runs
    /// on objects that were never saved, so the asset-dependent fields -- guid,
    /// assetPath, clipsAtPath -- are exercised only in their "not an asset" form; the
    /// imported-clip case needs a real project and is verified by hand.
    /// </summary>
    internal sealed class MotionJsonTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void DestroyCreatedObjects()
        {
            foreach (var obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
        }

        [Test]
        public void Append_WritesNullForAMissingMotion()
        {
            Assert.AreEqual("null", Serialize(null));
        }

        [Test]
        public void Append_MarksAClipAndReportsThatItIsNotAnAsset()
        {
            var clip = Track(new AnimationClip { name = "Walk" });

            Assert.AreEqual(
                "{\"type\":\"AnimationClip\",\"guid\":null,\"name\":\"Walk\",\"assetPath\":null}",
                Serialize(clip));
        }

        [Test]
        public void Append_MarksABlendTreeAndGivesItNoGuid()
        {
            var tree = Track(NewTree("Locomotion", BlendTreeType.Simple1D, "Speed"));

            Assert.AreEqual(
                "{\"type\":\"BlendTree\",\"guid\":null,\"name\":\"Locomotion\"," +
                "\"blendType\":\"Simple1D\",\"blendParameter\":\"Speed\",\"blendParameterY\":\"Direction\"," +
                "\"useAutomaticThresholds\":false,\"minThreshold\":0,\"maxThreshold\":1,\"children\":[]}",
                Serialize(tree));
        }

        [Test]
        public void Append_WritesEveryChildFieldAndRecursesIntoTheChildMotion()
        {
            var tree = Track(NewTree("Locomotion", BlendTreeType.FreeformCartesian2D, "Speed"));
            var clip = Track(new AnimationClip { name = "Run" });
            tree.children = new[]
            {
                new ChildMotion
                {
                    motion = clip,
                    threshold = 0.8f,
                    position = new Vector2(1.5f, -2f),
                    timeScale = 2f,
                    cycleOffset = 0.25f,
                    mirror = true,
                    directBlendParameter = "Weight"
                }
            };

            StringAssert.Contains(
                "\"children\":[{\"threshold\":0.8,\"position\":{\"x\":1.5,\"y\":-2}," +
                "\"timeScale\":2,\"cycleOffset\":0.25,\"mirror\":true,\"directBlendParameter\":\"Weight\"," +
                "\"motion\":{\"type\":\"AnimationClip\",\"guid\":null,\"name\":\"Run\",\"assetPath\":null}}]",
                Serialize(tree));
        }

        [Test]
        public void Append_DescribesNestedTreesUpToTheDepthLimit()
        {
            var root = TrackedChain(MotionJson.MaxBlendTreeDepth);

            var json = Serialize(root);

            // The tree at the limit is named after its depth and is the one that gets cut.
            StringAssert.Contains(
                $"{{\"type\":\"BlendTree\",\"guid\":null,\"name\":\"Depth{MotionJson.MaxBlendTreeDepth}\",\"truncated\":true}}",
                json);
            StringAssert.DoesNotContain(
                $"\"name\":\"Depth{MotionJson.MaxBlendTreeDepth}\",\"blendType\"",
                json);
        }

        [Test]
        public void Append_DescribesTheDeepestTreeThatFitsInFull()
        {
            var root = TrackedChain(MotionJson.MaxBlendTreeDepth - 1);

            var json = Serialize(root);

            // One level shallower, nothing is cut: the deepest tree still reports its
            // settings and an empty child list rather than a truncation marker.
            StringAssert.Contains(
                $"\"name\":\"Depth{MotionJson.MaxBlendTreeDepth - 1}\",\"blendType\"",
                json);
            StringAssert.DoesNotContain("\"truncated\":true", json);
        }

        // -- Helpers ---------------------------------------------------------

        private static string Serialize(Motion motion)
        {
            var sb = new StringBuilder();
            MotionJson.Append(sb, motion, new Dictionary<string, int>());
            return sb.ToString();
        }

        private T Track<T>(T obj) where T : Object
        {
            _created.Add(obj);
            return obj;
        }

        private static BlendTree NewTree(string name, BlendTreeType type, string parameter)
            => new BlendTree
            {
                name = name,
                blendType = type,
                blendParameter = parameter,
                blendParameterY = "Direction",
                useAutomaticThresholds = false,
                minThreshold = 0f,
                maxThreshold = 1f,
                children = new ChildMotion[0]
            };

        /// <summary>
        /// Builds a chain of blend trees named Depth0..Depth{deepest}, each the only
        /// child of the one above it.
        /// </summary>
        private BlendTree TrackedChain(int deepest)
        {
            var trees = new BlendTree[deepest + 1];
            for (int i = 0; i <= deepest; i++)
            {
                trees[i] = Track(NewTree("Depth" + i, BlendTreeType.Simple1D, "Speed"));
            }
            for (int i = 0; i < deepest; i++)
            {
                trees[i].children = new[] { new ChildMotion { motion = trees[i + 1], timeScale = 1f } };
            }
            return trees[0];
        }
    }
}

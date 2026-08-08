using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the binding lookup that decides what DELETE .../curves acts on. Removal
    /// itself needs a real clip and is verified by hand; what is testable here is the
    /// matching rule, which is what previously let a request that matched nothing be
    /// reported as a removal.
    /// </summary>
    internal sealed class AnimationClipBindingLookupTests
    {
        private static readonly EditorCurveBinding[] FloatBindings =
        {
            EditorCurveBinding.FloatCurve("Hips", typeof(Transform), "m_LocalPosition.x"),
            EditorCurveBinding.FloatCurve("Hips", typeof(Transform), "m_LocalPosition.y"),
            EditorCurveBinding.FloatCurve("Hips", typeof(Transform), "m_LocalPosition.z"),
            EditorCurveBinding.FloatCurve("Head", typeof(Transform), "m_LocalScale.x"),
        };

        private static readonly EditorCurveBinding[] PPtrBindings =
        {
            EditorCurveBinding.PPtrCurve("Body", typeof(SkinnedMeshRenderer), "m_Materials.Array.data[0]"),
        };

        [Test]
        public void TryFindBinding_MatchesOnPathTypeAndSerializedName()
        {
            EditorCurveBinding match;
            Assert.IsTrue(AnimationClipHandler.TryFindBinding(
                FloatBindings, "Hips", typeof(Transform), "m_LocalPosition.y", out match));
            Assert.AreEqual("m_LocalPosition.y", match.propertyName);
            Assert.AreEqual("Hips", match.path);
        }

        [Test]
        public void TryFindBinding_RejectsTheNameTheCallerWroteRatherThanTheOneStored()
        {
            // POST accepts "localPosition.y" and Unity expands it to three serialized
            // bindings. DELETE addresses the stored names, so the written name must not
            // resolve -- reporting it is what tells the caller to use what GET returns.
            EditorCurveBinding match;
            Assert.IsFalse(AnimationClipHandler.TryFindBinding(
                FloatBindings, "Hips", typeof(Transform), "localPosition.y", out match));
        }

        [Test]
        public void TryFindBinding_DistinguishesPathAndType()
        {
            EditorCurveBinding match;
            Assert.IsFalse(AnimationClipHandler.TryFindBinding(
                FloatBindings, "Head", typeof(Transform), "m_LocalPosition.y", out match),
                "a binding on another path must not match");
            Assert.IsFalse(AnimationClipHandler.TryFindBinding(
                FloatBindings, "Hips", typeof(SkinnedMeshRenderer), "m_LocalPosition.y", out match),
                "a binding of another type must not match");
        }

        [Test]
        public void TryFindBinding_FindsObjectReferenceBindings()
        {
            EditorCurveBinding match;
            Assert.IsTrue(AnimationClipHandler.TryFindBinding(
                PPtrBindings, "Body", typeof(SkinnedMeshRenderer), "m_Materials.Array.data[0]", out match));
            Assert.AreEqual("m_Materials.Array.data[0]", match.propertyName);
        }

        [Test]
        public void DescribeBindingsAt_ListsWhatTheCallerCouldHaveAsked()
        {
            Assert.AreEqual(
                "m_LocalPosition.x, m_LocalPosition.y, m_LocalPosition.z",
                AnimationClipHandler.DescribeBindingsAt(FloatBindings, PPtrBindings, "Hips", typeof(Transform)));
        }

        [Test]
        public void DescribeBindingsAt_CoversBothCurveKinds()
        {
            Assert.AreEqual(
                "m_Materials.Array.data[0]",
                AnimationClipHandler.DescribeBindingsAt(FloatBindings, PPtrBindings, "Body", typeof(SkinnedMeshRenderer)));
        }

        [Test]
        public void BindingKey_IsTheSameForARepeatedBinding()
        {
            // What makes a repeated entry detectable: the same path, type, and property
            // produce the same key, so the second entry is skipped rather than removed and
            // reported a second time. The key is built from the resolved Type, so two
            // spellings ResolveType maps together -- "Image" and "UnityEngine.UI.Image" --
            // reach this as one binding.
            Assert.AreEqual(
                AnimationClipHandler.BindingKey("Hips", typeof(Transform), "m_LocalPosition.y"),
                AnimationClipHandler.BindingKey("Hips", typeof(Transform), "m_LocalPosition.y"));
        }

        [Test]
        public void BindingKey_DiffersWhenAnyOfThePathTypeOrPropertyDiffers()
        {
            var baseline = AnimationClipHandler.BindingKey("Hips", typeof(Transform), "m_LocalPosition.y");

            Assert.AreNotEqual(baseline,
                AnimationClipHandler.BindingKey("Head", typeof(Transform), "m_LocalPosition.y"),
                "another path is another binding");
            Assert.AreNotEqual(baseline,
                AnimationClipHandler.BindingKey("Hips", typeof(SkinnedMeshRenderer), "m_LocalPosition.y"),
                "another type is another binding");
            Assert.AreNotEqual(baseline,
                AnimationClipHandler.BindingKey("Hips", typeof(Transform), "m_LocalPosition.z"),
                "another component of an expanded property is another binding");
        }

        [Test]
        public void DescribeBindingsAt_SaysNoneRatherThanEmpty()
        {
            Assert.AreEqual(
                "none",
                AnimationClipHandler.DescribeBindingsAt(FloatBindings, PPtrBindings, "Missing", typeof(Transform)));
        }
    }
}

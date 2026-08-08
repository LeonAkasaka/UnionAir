using NUnit.Framework;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Pins the type names that the animation curve endpoints accept.
    ///
    /// The resolver was a hand-written switch and is now the shared one, so what matters
    /// is that nothing which resolved before stops resolving. Every name the switch
    /// listed is asserted below, with the four ambiguous short names called out
    /// separately: the shared resolver matches on simple name across every loaded
    /// assembly and returns the first hit, which for those four is not the type anyone
    /// means.
    /// </summary>
    internal sealed class AnimationClipTypeResolutionTests
    {
        [TestCase("Transform", typeof(Transform))]
        [TestCase("Animator", typeof(Animator))]
        [TestCase("SkinnedMeshRenderer", typeof(SkinnedMeshRenderer))]
        [TestCase("MeshRenderer", typeof(MeshRenderer))]
        [TestCase("Light", typeof(Light))]
        [TestCase("Camera", typeof(Camera))]
        [TestCase("AudioSource", typeof(AudioSource))]
        [TestCase("SpriteRenderer", typeof(SpriteRenderer))]
        [TestCase("RectTransform", typeof(RectTransform))]
        [TestCase("CanvasGroup", typeof(CanvasGroup))]
        [TestCase("CanvasRenderer", typeof(CanvasRenderer))]
        [TestCase("GameObject", typeof(GameObject))]
        public void ResolveType_KeepsEveryShortNameTheSwitchListed(string typeName, System.Type expected)
        {
            Assert.AreEqual(expected, AnimationClipHandler.ResolveType(typeName));
        }

        [TestCase("Image", typeof(UnityEngine.UI.Image))]
        [TestCase("RawImage", typeof(UnityEngine.UI.RawImage))]
        [TestCase("Text", typeof(UnityEngine.UI.Text))]
        [TestCase("Button", typeof(UnityEngine.UI.Button))]
        [TestCase("Slider", typeof(UnityEngine.UI.Slider))]
        public void ResolveType_PrefersTheUiTypeForAnAmbiguousShortName(string typeName, System.Type expected)
        {
            // Image, Slider, Button, and Text each name something else that the shared
            // resolver reaches first -- UIElements.Image, UIElements.Slider,
            // InputForUI.PointerEvent+Button, MediaTypeNames+Text. None of them derive
            // from UnityEngine.Object, which is what keeps this on the UI type.
            Assert.AreEqual(expected, AnimationClipHandler.ResolveType(typeName));
        }

        [TestCase("UnityEngine.UI.Image", typeof(UnityEngine.UI.Image))]
        [TestCase("UnityEngine.UI.RawImage", typeof(UnityEngine.UI.RawImage))]
        [TestCase("UnityEngine.UI.Text", typeof(UnityEngine.UI.Text))]
        [TestCase("UnityEngine.UI.Button", typeof(UnityEngine.UI.Button))]
        [TestCase("UnityEngine.UI.Slider", typeof(UnityEngine.UI.Slider))]
        public void ResolveType_KeepsEveryQualifiedNameTheSwitchListed(string typeName, System.Type expected)
        {
            Assert.AreEqual(expected, AnimationClipHandler.ResolveType(typeName));
        }

        [TestCase("UnityEngine.Transform", typeof(Transform))]
        [TestCase("UnityEngine.Light", typeof(Light))]
        [TestCase("UnityEngine.Rigidbody", typeof(Rigidbody))]
        [TestCase("UnityEngine.SkinnedMeshRenderer", typeof(SkinnedMeshRenderer))]
        public void ResolveType_NowAcceptsAQualifiedNameOutsideTheUiNamespace(string typeName, System.Type expected)
        {
            // The defect: these answered "Unknown type" while UnityEngine.UI.Image
            // worked, because only the UI ones were written into the switch by hand.
            Assert.AreEqual(expected, AnimationClipHandler.ResolveType(typeName));
        }

        [TestCase("Rigidbody", typeof(Rigidbody))]
        [TestCase("BoxCollider", typeof(BoxCollider))]
        public void ResolveType_StillReachesTypesTheSwitchNeverNamed(string typeName, System.Type expected)
        {
            // These worked through the old fallback and must keep working.
            Assert.AreEqual(expected, AnimationClipHandler.ResolveType(typeName));
        }

        [TestCase("NoSuchTypeAnywhere")]
        [TestCase("")]
        public void ResolveType_AnswersNullForANameThatResolvesToNothing(string typeName)
        {
            Assert.IsNull(AnimationClipHandler.ResolveType(typeName));
        }

        [TestCase("System.String")]
        [TestCase("System.Int32")]
        public void ResolveType_RejectsATypeThatIsNotAUnityObject(string typeName)
        {
            // A curve binds to a Unity object. Without the base type filter these would
            // resolve, and the request would fail later and less clearly.
            Assert.IsNull(AnimationClipHandler.ResolveType(typeName));
        }
    }
}

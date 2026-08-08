using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the checks a layer write must pass before any value reaches Unity.
    ///
    /// These are not defensive tidiness. Measured on 6000.0.80f1, the editing API answers
    /// an illegal layer write by damaging the controller rather than refusing it: pointing
    /// a layer at itself removed a layer silently, and one index past the end crashed the
    /// Editor. So the rules cannot be validated by calling through and inspecting the
    /// result, and what is testable is the decision made before the call.
    /// </summary>
    internal sealed class AnimatorLayerRulesTests
    {
        [TestCase(0, 3)]
        [TestCase(2, 3)]
        public void TryValidateLayerIndex_AcceptsAnIndexInRange(int index, int count)
        {
            Assert.IsTrue(AnimatorLayerRules.TryValidateLayerIndex(index, count, out var error), error);
        }

        [TestCase(3, 3)]
        [TestCase(-1, 3)]
        [TestCase(0, 0)]
        public void TryValidateLayerIndex_RejectsAnIndexOutOfRange(int index, int count)
        {
            Assert.IsFalse(AnimatorLayerRules.TryValidateLayerIndex(index, count, out var error));
            StringAssert.Contains("out of range", error);
        }

        [Test]
        public void TryValidateDelete_RejectsTheBaseLayer()
        {
            // RemoveLayer(0) does not refuse: it promotes the next layer, and on a
            // single-layer controller it leaves zero layers.
            Assert.IsFalse(AnimatorLayerRules.TryValidateDelete(0, 2, out var error));
            StringAssert.Contains("base layer", error);
        }

        [Test]
        public void TryValidateDelete_AcceptsANonBaseLayer()
        {
            Assert.IsTrue(AnimatorLayerRules.TryValidateDelete(1, 2, out var error), error);
        }

        [Test]
        public void TryValidateSyncedLayerIndex_AcceptsNotSyncedAndAnotherLayer()
        {
            Assert.IsTrue(AnimatorLayerRules.TryValidateSyncedLayerIndex(-1, 2, 3, out var e1), e1);
            Assert.IsTrue(AnimatorLayerRules.TryValidateSyncedLayerIndex(0, 2, 3, out var e2), e2);
            Assert.IsTrue(AnimatorLayerRules.TryValidateSyncedLayerIndex(1, 2, 3, out var e3), e3);
        }

        [Test]
        public void TryValidateSyncedLayerIndex_RejectsTheLayerItself()
        {
            // Measured: three layers became two, with no error and no exception.
            Assert.IsFalse(AnimatorLayerRules.TryValidateSyncedLayerIndex(2, 2, 3, out var error));
            StringAssert.Contains("cannot sync with itself", error);
        }

        [TestCase(3)]
        [TestCase(99)]
        [TestCase(-5)]
        public void TryValidateSyncedLayerIndex_RejectsAnIndexOutsideTheLayers(int target)
        {
            // Measured: assigning one past the last index crashed the Editor, so this is
            // bounded from the legal side rather than characterised further.
            Assert.IsFalse(AnimatorLayerRules.TryValidateSyncedLayerIndex(target, 2, 3, out var error));
            StringAssert.Contains("out of range", error);
        }

        [Test]
        public void TryValidateDeleteAgainstSyncs_RejectsDeletingASyncTarget()
        {
            // Layer 2 syncs to layer 1. Deleting layer 1 would leave layer 2 pointing at
            // whatever moved into its place.
            var syncs = new[] { -1, -1, 1 };
            Assert.IsFalse(AnimatorLayerRules.TryValidateDeleteAgainstSyncs(1, syncs, out var error));
            StringAssert.Contains("synced to layer 1", error);
        }

        [Test]
        public void TryValidateDeleteAgainstSyncs_RejectsWhenTheRenumberingWouldMoveATarget()
        {
            // Layer 3 syncs to layer 2. Deleting layer 1 shifts both down, and layer 3's
            // target would then name what is now itself.
            var syncs = new[] { -1, -1, -1, 2 };
            Assert.IsFalse(AnimatorLayerRules.TryValidateDeleteAgainstSyncs(1, syncs, out var error));
            StringAssert.Contains("renumber", error);
        }

        [Test]
        public void TryValidateDeleteAgainstSyncs_IgnoresTheLayerBeingDeleted()
        {
            // The deleted layer's own sync is cleared by the handler before removal, so it
            // is not a reason to refuse the request.
            var syncs = new[] { -1, 0 };
            Assert.IsTrue(AnimatorLayerRules.TryValidateDeleteAgainstSyncs(1, syncs, out var error), error);
        }

        [Test]
        public void TryValidateDeleteAgainstSyncs_AllowsASyncBelowTheDeletedLayer()
        {
            // Layer 3 syncs to layer 0. Deleting layer 1 renumbers layer 3 but not its
            // target, so the reference stays correct.
            var syncs = new[] { -1, -1, -1, 0 };
            Assert.IsTrue(AnimatorLayerRules.TryValidateDeleteAgainstSyncs(1, syncs, out var error), error);
        }
    }
}

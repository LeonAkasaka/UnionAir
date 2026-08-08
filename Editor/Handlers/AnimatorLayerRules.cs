namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// The rules a layer write must satisfy before any value reaches Unity.
    ///
    /// These are checks rather than guards: measured on 6000.0.80f1, the editing APIs
    /// answer an illegal layer write by damaging the controller rather than by refusing
    /// it, so a request cannot be passed through and inspected afterwards.
    /// </summary>
    internal static class AnimatorLayerRules
    {
        /// <summary>Value of <c>syncedLayerIndex</c> meaning "this layer is not synced".</summary>
        internal const int NotSynced = -1;

        internal static bool TryValidateLayerIndex(int layerIndex, int layerCount, out string error)
        {
            if (layerIndex < 0 || layerIndex >= layerCount)
            {
                error = $"layerIndex {layerIndex} is out of range; the controller has {layerCount} layer(s).";
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>
        /// Rejects deleting layer 0.
        ///
        /// <c>AnimatorController.RemoveLayer(0)</c> does not refuse: measured on
        /// 6000.0.80f1 it removes the layer and promotes the next one, and on a
        /// single-layer controller it leaves zero layers, which is not a valid
        /// controller. Neither outcome is what a caller deleting "a layer" means.
        /// </summary>
        internal static bool TryValidateDelete(int layerIndex, int layerCount, out string error)
        {
            if (!TryValidateLayerIndex(layerIndex, layerCount, out error)) return false;

            if (layerIndex == 0)
            {
                error = "Layer 0 is the base layer and cannot be deleted. " +
                        "Removing it leaves the controller without a base layer, which Unity does not reject " +
                        "and which no other endpoint can repair. Delete another layer, or delete the controller asset.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Rejects deleting a layer while another layer's sync target would be disturbed
        /// by the renumbering.
        ///
        /// Removing a layer shifts every higher index down by one, and nothing fixes up a
        /// <c>syncedLayerIndex</c> that pointed at or above it. A layer that synced to the
        /// deleted one is left pointing at whatever moved into its place, and a layer that
        /// synced to a higher index ends up pointing one layer too low -- including, when
        /// the numbers line up, at itself, which was measured to remove a layer silently.
        /// Rather than renumber references, which is a design decision this endpoint does
        /// not own, the request is refused and names what is in the way.
        /// </summary>
        /// <param name="syncedLayerIndices">
        /// Each layer's <c>syncedLayerIndex</c>, in layer order.
        /// </param>
        internal static bool TryValidateDeleteAgainstSyncs(
            int layerIndex, int[] syncedLayerIndices, out string error)
        {
            for (int i = 0; i < syncedLayerIndices.Length; i++)
            {
                if (i == layerIndex) continue;
                var target = syncedLayerIndices[i];
                if (target == NotSynced || target < layerIndex) continue;

                error = target == layerIndex
                    ? $"Layer {i} is synced to layer {layerIndex} and would be left pointing at another layer. " +
                      $"Clear layer {i}'s syncedLayerIndex first."
                    : $"Layer {i} is synced to layer {target}, which deleting layer {layerIndex} would renumber. " +
                      $"Clear layer {i}'s syncedLayerIndex first.";
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>
        /// Rejects a <c>syncedLayerIndex</c> that is not a legal sync target.
        ///
        /// Measured on 6000.0.80f1, assigning an illegal value is destructive rather than
        /// refused. Pointing a layer at itself silently removed a layer from the
        /// controller -- three layers became two with no error. Assigning one past the
        /// last index crashed the Editor outright, which is why the out-of-range case is
        /// bounded here from the legal side rather than characterised further: reproducing
        /// it costs an Editor session.
        /// </summary>
        internal static bool TryValidateSyncedLayerIndex(
            int syncedLayerIndex, int layerIndex, int layerCount, out string error)
        {
            error = null;
            if (syncedLayerIndex == NotSynced) return true;

            if (syncedLayerIndex < 0 || syncedLayerIndex >= layerCount)
            {
                error = $"syncedLayerIndex {syncedLayerIndex} is out of range; " +
                        $"use -1 for no sync, or 0..{layerCount - 1}.";
                return false;
            }

            if (syncedLayerIndex == layerIndex)
            {
                error = $"syncedLayerIndex {syncedLayerIndex} is the layer being updated; " +
                        "a layer cannot sync with itself.";
                return false;
            }
            return true;
        }
    }
}

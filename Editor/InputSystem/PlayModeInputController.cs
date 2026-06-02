namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Exposes Unity Input System actions to external clients during Play mode.
    /// Requires the <c>com.unity.inputsystem</c> package; the entire assembly is
    /// excluded from compilation when the package is absent.
    /// </summary>
    [UnionAirController("playmode/input")]
    internal sealed class PlayModeInputController
    {
        [UnionAirEndpoint("GET", "actions",
            Category = UnionAirEndpointCategories.PlayMode,
            PlayModePolicy = UnionAirPlayModePolicy.Allowed,
            Summary = "Lists all enabled InputActions in the running game. Only available in Play mode.")]
        private void Actions(UnionAirRequestContext ctx)
            => PlayModeInputHandler.HandleActions(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "perform",
            Category = UnionAirEndpointCategories.PlayMode,
            UseRiskOverride = true,
            Risk = UnionAirEndpointRisk.Custom,
            PlayModePolicy = UnionAirPlayModePolicy.Allowed,
            Summary = "Simulates an InputAction via a virtual device. Only available in Play mode.",
            RequiredBody = new string[] { "action" },
            OptionalBody = new string[] { "value" })]
        private void Perform(UnionAirRequestContext ctx)
            => PlayModeInputHandler.HandlePerform(ctx.Request, ctx.Response);
    }
}

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
            Summary = "Performs a Button InputAction through a virtual device. action accepts Map/Action or an unambiguous bare name; ambiguous names return 409 with candidates. mode defaults to tap; unsupported Button bindings fail with 422. Only available in Play mode.",
            RequiredBody = new string[] { "action" },
            OptionalBody = new string[] { "mode" })]
        private void Perform(UnionAirRequestContext ctx)
            => PlayModeInputHandler.HandlePerform(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "set",
            Category = UnionAirEndpointCategories.PlayMode,
            UseRiskOverride = true,
            Risk = UnionAirEndpointRisk.Custom,
            PlayModePolicy = UnionAirPlayModePolicy.Allowed,
            Summary = "Sets an Axis, Vector2, or Stick InputAction value on a virtual device. action accepts Map/Action or an unambiguous bare name; ambiguous names return 409 with candidates. Gamepad values remain until the next set or Play mode cleanup; Mouse scroll is a one-shot delta.",
            RequiredBody = new string[] { "action", "value" })]
        private void Set(UnionAirRequestContext ctx)
            => PlayModeInputHandler.HandleSet(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "pointer",
            Category = UnionAirEndpointCategories.PlayMode,
            UseRiskOverride = true,
            Risk = UnionAirEndpointRisk.Custom,
            PlayModePolicy = UnionAirPlayModePolicy.Allowed,
            Summary = "Simulates a mouse click/press/release/move at a screen coordinate via the virtual mouse, driving the game's own raycast-based hit detection. The response completes after the final input frame (~3-4 player frames for a tap). Only available in Play mode.",
            OptionalBody = new string[] { "position", "normalizedPosition", "origin", "button", "mode", "holdFrames" },
            RequestExample = "{\"normalizedPosition\":{\"x\":0.5,\"y\":0.5},\"origin\":\"topLeft\",\"mode\":\"tap\"}",
            ResponseExample = "{\"success\":true,\"mode\":\"tap\",\"button\":\"left\",\"position\":{\"x\":640,\"y\":360},\"screenSize\":{\"width\":1280,\"height\":720},\"pressFrame\":1204,\"releaseFrame\":1205}")]
        private void Pointer(UnionAirRequestContext ctx)
            => PlayModeInputHandler.HandlePointer(ctx);
    }
}

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Reports the outcome of an input replay started through <c>POST /api/editor/play</c>.
    /// </summary>
    /// <remarks>
    /// Shares the <c>playmode/input</c> route with the Input System assembly's controller but
    /// declares a different endpoint, and lives in the main assembly on purpose: a project without
    /// <c>com.unity.inputsystem</c> can still read why its replay was refused.
    /// </remarks>
    [UnionAirController("playmode/input")]
    internal sealed class PlayModeInputReplayController
    {
        [UnionAirEndpoint("GET", "result",
            Category = UnionAirEndpointCategories.Read,
            PlayModePolicy = UnionAirPlayModePolicy.Allowed,
            TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            Summary = "Returns the input replay scheduled by POST /api/editor/play: the current one while it runs, otherwise the latest completed one. Poll state until it leaves 'queued' and 'running' to learn when the replay finished and the game's state is worth inspecting. Each event reports the frame it was actually observed on, not the frame requested, so a client can prove the schedule held. A failed event does not stop the replay, so check failedCount even when state is 'completed'. Returns 404 when no replay has been recorded.",
            OptionalQuery = new string[] { "id" },
            ResponseExample = "{\"state\":\"completed\",\"events\":[{\"index\":0,\"frame\":5,\"late\":false,\"unityFrame\":1209,\"status\":\"applied\",\"control\":\"/UnionAirVirtualKeyboard/space\",\"error\":null}],\"lateCount\":0,\"failedCount\":0,\"abortReason\":null,\"abortCode\":null,\"id\":\"ir-20260729-083741-cb2984\",\"eventCount\":1,\"appliedCount\":1,\"baseFrame\":1204,\"lastObservedFrame\":5,\"updateMode\":\"dynamic\",\"sessionId\":\"ab453ee8\",\"requestedAt\":\"2026-07-29T08:37:41.0000000Z\",\"startedAt\":\"2026-07-29T08:37:45.0000000Z\",\"finishedAt\":\"2026-07-29T08:37:45.2000000Z\",\"durationSeconds\":0.2,\"lifecycleGenerationAtRequest\":4,\"lifecycleGenerationAtFinish\":5}")]
        private void Result(UnionAirRequestContext ctx)
            => new PlayModeInputReplayHandler().Handle(ctx);
    }
}

namespace LeonAkasaka.UnionAir.Editor
{
    [UnionAirController("compile")]
    internal sealed class CompileController
    {
        [UnionAirEndpoint("GET", "",
            Category = UnionAirEndpointCategories.Read,
            TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            Summary = "Returns the in-flight compilation as 'current' and the most recently completed Editor compilation as 'latest'. Cycles started outside UnionAir are recorded with source 'external'. result is 'failed' (no domain reload follows), 'succeeded' (at least one assembly compiled), or 'upToDate' (nothing needed compiling, so no domain reload follows).",
            ResponseExample = "{\"current\":null,\"latest\":{\"id\":\"c-20260728-034841-a1b2c3\",\"source\":\"external\",\"state\":\"completed\",\"result\":\"failed\",\"target\":\"editor\",\"sessionId\":\"f40cbf3f\",\"requestedAt\":\"2026-07-28T03:48:41.0000000Z\",\"startedAt\":\"2026-07-28T03:48:41.5000000Z\",\"finishedAt\":\"2026-07-28T03:48:45.6000000Z\",\"durationSeconds\":4.1,\"lifecycleGenerationAtRequest\":3,\"lifecycleGenerationAtFinish\":3,\"errorCount\":1,\"warningCount\":0,\"assemblies\":[{\"name\":\"Assembly-CSharp\",\"path\":\"Library/ScriptAssemblies/Assembly-CSharp.dll\",\"outputDirectory\":\"Library/ScriptAssemblies\",\"compiled\":true,\"errorCount\":1,\"warningCount\":0}],\"unchangedAssemblyCount\":72,\"messages\":[{\"severity\":\"error\",\"code\":\"CS0103\",\"file\":\"Assets/Scripts/Player.cs\",\"line\":12,\"column\":9,\"assembly\":\"Assembly-CSharp\",\"message\":\"The name 'bar' does not exist in the current context\",\"raw\":\"Assets/Scripts/Player.cs(12,9): error CS0103: The name 'bar' does not exist in the current context\"}],\"messagesTruncated\":false,\"error\":null}}")]
        private void Collection(UnionAirRequestContext ctx)
            => new CompileHandler().HandleCollection(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            TestRunPolicy = UnionAirTestRunPolicy.Blocked,
            Summary = "Requests a script compilation and returns 202 with the id to poll through GET /api/compile/{id}. The response is sent before any compilation work begins, because refreshing and compiling block the Editor and can end in a domain reload that drops the connection. Returns 409 when a compilation is already active, when the Editor is entering or in Play mode, or while assets are updating.",
            OptionalBody = new string[] { "refresh", "clean", "requestId" },
            RequestExample = "{\"refresh\":true,\"clean\":false}",
            ResponseExample = "{\"id\":\"c-20260728-040030-67c0fd\",\"state\":\"queued\",\"source\":\"unionAir\",\"sessionId\":\"f40cbf3f\",\"lifecycleGenerationAtRequest\":6,\"statusUrl\":\"/api/compile/c-20260728-040030-67c0fd\"}")]
        private void Start(UnionAirRequestContext ctx)
            => new CompileHandler().HandleStart(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "{id}",
            Category = UnionAirEndpointCategories.Read,
            TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            PathParams = new string[] { "id" },
            Summary = "Returns one retained compilation record. Use this instead of 'latest' to confirm a specific cycle finished, because a compilation started from an IDE can replace 'latest' at any time. UnionAir retains the 20 most recent records; evicted ids return 404.")]
        private void ById(UnionAirRequestContext ctx)
            => new CompileHandler().HandleById(ctx);
    }
}

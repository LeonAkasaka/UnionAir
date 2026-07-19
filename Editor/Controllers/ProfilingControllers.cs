namespace LeonAkasaka.UnionAir.Editor
{
    [UnionAirController("profiling/metrics")]
    internal sealed class ProfilingMetricsController
    {
        [UnionAirEndpoint("GET", "", Category = UnionAirEndpointCategories.Profiling,
            Summary = "Lists available Unity Profiler counters and markers.", OptionalQuery = new string[] { "search", "category", "offset", "limit" },
            ResponseExample = "{\"schemaVersion\":1,\"total\":1,\"offset\":0,\"limit\":100,\"metrics\":[{\"metricId\":\"mainThreadTime\",\"category\":\"Internal\",\"marker\":\"Main Thread\",\"unit\":\"ms\",\"dataType\":\"Int64\",\"available\":true}]}")]
        private void List(UnionAirRequestContext ctx) => ProfilingService.Metrics(ctx);
    }

    [UnionAirController("profiling/sessions")]
    internal sealed class ProfilingSessionsController
    {
        [UnionAirEndpoint("POST", "", Category = UnionAirEndpointCategories.Profiling,
            Summary = "Starts an asynchronous Editor profiling session.",
            OptionalBody = new string[] { "label", "metrics", "warmupFrames", "maxFrames", "maxDurationSeconds", "captureRaw" },
            RequestExample = "{\"label\":\"inventory-scroll\",\"metrics\":[\"mainThreadTime\",\"gcAllocInFrame\"],\"warmupFrames\":60,\"maxFrames\":600,\"maxDurationSeconds\":30,\"captureRaw\":false}",
            ResponseExample = "{\"schemaVersion\":1,\"id\":\"...\",\"state\":\"warming\",\"statusUrl\":\"/api/profiling/sessions/...\"}")]
        private void Start(UnionAirRequestContext ctx) => ProfilingService.StartManual(RequestBodyReader.ReadString(ctx.Request), ctx);

        [UnionAirEndpoint("GET", "", Category = UnionAirEndpointCategories.Profiling, TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            Summary = "Lists retained profiling sessions.")]
        private void List(UnionAirRequestContext ctx) => ProfilingService.List(ctx);

        [UnionAirEndpoint("GET", "{id}", Category = UnionAirEndpointCategories.Profiling, TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            Summary = "Returns profiling state, statistics, environment, and artifacts.", PathParams = new string[] { "id" })]
        private void Status(UnionAirRequestContext ctx) => ProfilingService.Status(ctx);

        [UnionAirEndpoint("POST", "{id}/stop", Category = UnionAirEndpointCategories.Profiling, TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            Summary = "Stops and finalizes a profiling session.", PathParams = new string[] { "id" })]
        private void Stop(UnionAirRequestContext ctx) => ProfilingService.Stop(ctx);

        [UnionAirEndpoint("DELETE", "{id}", Category = UnionAirEndpointCategories.Profiling,
            Summary = "Deletes a completed profiling session and its artifacts.", PathParams = new string[] { "id" })]
        private void Delete(UnionAirRequestContext ctx) => ProfilingService.Delete(ctx);

        [UnionAirEndpoint("GET", "{id}/samples.ndjson", Category = UnionAirEndpointCategories.Profiling, TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            Summary = "Downloads frame-level profiling samples as NDJSON.", PathParams = new string[] { "id" })]
        private void Samples(UnionAirRequestContext ctx) => ProfilingService.Samples(ctx);

        [UnionAirEndpoint("GET", "{id}/profile.raw", Category = UnionAirEndpointCategories.Profiling, TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            Summary = "Downloads a Unity Profiler raw capture when enabled.", PathParams = new string[] { "id" })]
        private void Raw(UnionAirRequestContext ctx) => ProfilingService.Raw(ctx);
    }
}

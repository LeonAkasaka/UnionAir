namespace LeonAkasaka.UnionAir.Editor
{
    [UnionAirController("memory-snapshots")]
    internal sealed class MemorySnapshotController
    {
        [UnionAirEndpoint("POST", "", Category = UnionAirEndpointCategories.Profiling,
            Summary = "Starts an asynchronous Unity Memory Profiler snapshot.",
            OptionalBody = new string[] { "label", "profilingSessionId", "testRunId" },
            RequestExample = "{\"label\":\"after-load-cycle\",\"profilingSessionId\":\"optional-related-id\"}",
            ResponseExample = "{\"schemaVersion\":1,\"id\":\"...\",\"state\":\"capturing\",\"statusUrl\":\"/api/memory-snapshots/...\"}")]
        private void Start(UnionAirRequestContext ctx) => MemorySnapshotService.Start(ctx);

        [UnionAirEndpoint("GET", "", Category = UnionAirEndpointCategories.Profiling, TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            Summary = "Lists retained memory snapshots.")]
        private void List(UnionAirRequestContext ctx) => MemorySnapshotService.List(ctx);

        [UnionAirEndpoint("GET", "{id}", Category = UnionAirEndpointCategories.Profiling, TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            Summary = "Returns memory snapshot state, environment, counters, and artifact metadata.", PathParams = new string[] { "id" })]
        private void Status(UnionAirRequestContext ctx) => MemorySnapshotService.Status(ctx);

        [UnionAirEndpoint("GET", "{id}/snapshot", Category = UnionAirEndpointCategories.Profiling, TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            Summary = "Downloads a completed Unity Memory Profiler snapshot.", PathParams = new string[] { "id" })]
        private void Download(UnionAirRequestContext ctx) => MemorySnapshotService.Download(ctx);

        [UnionAirEndpoint("DELETE", "{id}", Category = UnionAirEndpointCategories.Profiling,
            Summary = "Deletes a completed memory snapshot and its metadata.", PathParams = new string[] { "id" })]
        private void Delete(UnionAirRequestContext ctx) => MemorySnapshotService.Delete(ctx);
    }
}

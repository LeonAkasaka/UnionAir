namespace LeonAkasaka.UnionAir.Editor
{
    [UnionAirController("tests")]
    internal sealed class TestsController
    {
        [UnionAirEndpoint("GET", "",
            Category = UnionAirEndpointCategories.TestRunner,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Discovers leaf tests for one test mode. Only one discovery request may run at a time.",
            RequiredQuery = new string[] { "mode" },
            OptionalQuery = new string[] { "search", "assembly", "category", "offset", "limit" })]
        private void List(UnionAirRequestContext ctx)
            => TestDiscoveryHandler.Handle(ctx);
    }

    [UnionAirController("test-runs")]
    internal sealed class TestRunsController
    {
        [UnionAirEndpoint("POST", "",
            Category = UnionAirEndpointCategories.TestRunner,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Starts an asynchronous EditMode or PlayMode Unity Test Framework run.",
            RequiredBody = new string[] { "mode" },
            OptionalBody = new string[] { "testNames", "groupNames", "categoryNames", "assemblyNames", "profiling" },
            RequestExample = "{\"mode\":\"editMode\",\"categoryNames\":[\"Smoke\"],\"profiling\":{\"metrics\":[\"mainThreadTime\"],\"maxFrames\":300}}",
            ResponseExample = "{\"id\":\"...\",\"state\":\"queued\",\"statusUrl\":\"/api/test-runs/...\",\"resultUrl\":\"/api/test-runs/.../results.xml\",\"profilingSessionId\":\"...\",\"profilingUrl\":\"/api/profiling/sessions/...\"}")]
        private void Start(UnionAirRequestContext ctx)
            => TestRunnerService.Start(ctx);

        [UnionAirEndpoint("GET", "{id}",
            Category = UnionAirEndpointCategories.TestRunner,
            TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            Summary = "Returns the current run or the latest completed UnionAir run.",
            PathParams = new string[] { "id" })]
        private void Status(UnionAirRequestContext ctx)
            => TestRunnerService.Status(ctx);

        [UnionAirEndpoint("DELETE", "{id}",
            Category = UnionAirEndpointCategories.TestRunner,
            TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            Summary = "Requests cancellation of the active UnionAir test run.",
            PathParams = new string[] { "id" })]
        private void Cancel(UnionAirRequestContext ctx)
            => TestRunnerService.Cancel(ctx);

        [UnionAirEndpoint("GET", "{id}/results.xml",
            Category = UnionAirEndpointCategories.TestRunner,
            TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            Summary = "Downloads the complete NUnit XML for the latest completed UnionAir run.",
            PathParams = new string[] { "id" })]
        private void Results(UnionAirRequestContext ctx)
            => TestRunnerService.Results(ctx);
    }
}

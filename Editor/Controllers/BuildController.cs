namespace LeonAkasaka.UnionAir.Editor
{
    [UnionAirController("build")]
    internal sealed class BuildController
    {
        [UnionAirEndpoint("GET", "settings",
            Category = UnionAirEndpointCategories.Build,
            PlayModePolicy = UnionAirPlayModePolicy.Allowed,
            TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            UseRiskOverride = true,
            Risk = UnionAirEndpointRisk.None,
            Summary = "Returns the build configuration: active build target, build scenes with the build index Unity assigns them, scripting backend and define symbols for one named build target, and the development and debugging flags a build would use. Use 'namedBuildTarget' to read the scripting settings of a target other than the active one; unknown names return 400.",
            OptionalQuery = new string[] { "namedBuildTarget" },
            ResponseExample = "{\"activeBuildTarget\":\"StandaloneWindows64\",\"activeBuildTargetGroup\":\"Standalone\",\"activeNamedBuildTarget\":\"Standalone\",\"selectedBuildTargetGroup\":\"Standalone\",\"standaloneBuildSubtarget\":\"Player\",\"activeBuildTargetInstalled\":true,\"scenes\":[{\"path\":\"Assets/Scenes/SampleScene.unity\",\"guid\":\"9fc0d4010bbf28b4594abcfb1831a99b\",\"enabled\":true,\"buildIndex\":0}],\"sceneCount\":1,\"enabledSceneCount\":1,\"scripting\":{\"namedBuildTarget\":\"Standalone\",\"scriptingBackend\":\"Mono2x\",\"apiCompatibilityLevel\":\"NET_Standard\",\"il2CppCompilerConfiguration\":\"Release\",\"managedStrippingLevel\":\"Minimal\",\"defineSymbolsRaw\":\"UNIONAIR_SAMPLE\",\"defineSymbols\":[\"UNIONAIR_SAMPLE\"]},\"options\":{\"development\":false,\"allowDebugging\":false,\"connectProfiler\":false,\"buildWithDeepProfilingSupport\":false,\"waitForManagedDebugger\":false},\"player\":{\"productName\":\"TestUnity6\",\"companyName\":\"DefaultCompany\",\"bundleVersion\":\"0.1.0\",\"unityVersion\":\"6000.0.80f1\"}}")]
        private void Settings(UnionAirRequestContext ctx)
            => new BuildSettingsHandler().HandleSettings(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "targets",
            Category = UnionAirEndpointCategories.Build,
            PlayModePolicy = UnionAirPlayModePolicy.Allowed,
            TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            UseRiskOverride = true,
            Risk = UnionAirEndpointRisk.None,
            Summary = "Lists the build targets this Unity installation defines and whether each platform module is installed. Which modules are installed is a property of the Editor, not of the project, so no project file reports it. Pass installed=true to list only buildable targets.",
            OptionalQuery = new string[] { "installed" },
            ResponseExample = "{\"activeBuildTarget\":\"StandaloneWindows64\",\"total\":18,\"installedCount\":2,\"installedOnly\":false,\"targets\":[{\"buildTarget\":\"Android\",\"buildTargetGroup\":\"Android\",\"namedBuildTarget\":\"Android\",\"installed\":false,\"isActive\":false},{\"buildTarget\":\"StandaloneWindows64\",\"buildTargetGroup\":\"Standalone\",\"namedBuildTarget\":\"Standalone\",\"installed\":true,\"isActive\":true}]}")]
        private void Targets(UnionAirRequestContext ctx)
            => new BuildSettingsHandler().HandleTargets(ctx.Request, ctx.Response);
    }

    [UnionAirController("builds")]
    internal sealed class BuildsController
    {
        [UnionAirEndpoint("POST", "",
            Category = UnionAirEndpointCategories.Build,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            TestRunPolicy = UnionAirTestRunPolicy.Blocked,
            BlockedDuring = UnionAirActivity.Compile | UnionAirActivity.AssetUpdate | UnionAirActivity.BuildTargetSwitch,
            Summary = "Requests a player build for the active build target and returns 202 with the id to poll through GET /api/builds/{id}. The record is persisted and the response sent before the build starts, because a build occupies the Unity main thread and UnionAir answers nothing while it runs - roughly 72 seconds for a Windows player. Live progress and cancellation are not offered and are not achievable in process. Output goes to Builds/UnionAir/{id}/ under the project root; the location is never taken from the request. Returns 409 when a loaded scene has unsaved changes, when a build is already active, when the active target's platform module is missing, or when 'requestId' was already used.",
            OptionalBody = new string[] { "requestId", "development", "allowDebugging", "connectProfiler", "deepProfiling", "waitForPlayerConnection", "clean", "strictMode" },
            RequestExample = "{\"requestId\":\"nightly-1\",\"development\":true,\"allowDebugging\":true}",
            ResponseExample = "{\"id\":\"b-20260802-101530-3f9ac1\",\"state\":\"queued\",\"buildTarget\":\"StandaloneWindows64\",\"sessionId\":\"f40cbf3f\",\"lifecycleGenerationAtRequest\":6,\"statusUrl\":\"/api/builds/b-20260802-101530-3f9ac1\",\"note\":\"The build occupies the Unity main thread.\"}")]
        private void Start(UnionAirRequestContext ctx)
            => new BuildHandler().HandleStart(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "",
            Category = UnionAirEndpointCategories.Build,
            PlayModePolicy = UnionAirPlayModePolicy.Allowed,
            TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            UseRiskOverride = true,
            Risk = UnionAirEndpointRisk.None,
            UseActivityOverride = true,
            BlockedDuring = UnionAirActivity.None,
            Summary = "Returns the in-flight build as 'current', the retained build record summaries, and how much disk the retained artifacts occupy. Artifacts are trimmed oldest-first under a count and size cap; the records outlive them, so a summary can report outputAvailable false.",
            ResponseExample = "{\"current\":null,\"total\":1,\"storage\":{\"root\":\"Builds/UnionAir\",\"totalBytes\":99540233,\"artifactCount\":1,\"maxArtifactCount\":3,\"maxTotalBytes\":2147483648,\"retainedRecords\":20},\"records\":[{\"id\":\"b-20260802-101530-3f9ac1\",\"state\":\"completed\",\"result\":\"succeeded\",\"buildTarget\":\"StandaloneWindows64\",\"requestedAt\":\"2026-08-02T10:15:30.0000000Z\",\"finishedAt\":\"2026-08-02T10:16:42.0000000Z\",\"durationSeconds\":72.4,\"outputDirectory\":\"Builds/UnionAir/b-20260802-101530-3f9ac1\",\"outputBytes\":99540233,\"outputAvailable\":true,\"compileId\":\"c-20260802-101533-91ba2c\",\"error\":null,\"statusUrl\":\"/api/builds/b-20260802-101530-3f9ac1\"}]}")]
        private void Collection(UnionAirRequestContext ctx)
            => new BuildHandler().HandleCollection(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "{id}",
            Category = UnionAirEndpointCategories.Build,
            PlayModePolicy = UnionAirPlayModePolicy.Allowed,
            TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            UseRiskOverride = true,
            Risk = UnionAirEndpointRisk.None,
            UseActivityOverride = true,
            BlockedDuring = UnionAirActivity.None,
            PathParams = new string[] { "id" },
            Summary = "Returns one retained build record, including the snapshotted BuildReport summary and its error and warning messages. UnionAir retains the 20 most recent records; evicted ids return 404.")]
        private void ById(UnionAirRequestContext ctx)
            => new BuildHandler().HandleById(ctx);

        [UnionAirEndpoint("DELETE", "{id}",
            Category = UnionAirEndpointCategories.Build,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            TestRunPolicy = UnionAirTestRunPolicy.Blocked,
            BlockedDuring = UnionAirActivity.BuildTargetSwitch,
            PathParams = new string[] { "id" },
            Summary = "Deletes a build record and its artifact directory, reclaiming the disk it occupied. Reports the bytes reclaimed and the remaining total.",
            ResponseExample = "{\"deleted\":\"b-20260802-101530-3f9ac1\",\"reclaimedBytes\":99540233,\"outputAvailable\":false,\"totalBytes\":0}")]
        private void Delete(UnionAirRequestContext ctx)
            => new BuildHandler().HandleDelete(ctx);
    }
}

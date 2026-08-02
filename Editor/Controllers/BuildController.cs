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
}

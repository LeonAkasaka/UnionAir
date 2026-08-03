using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;

[assembly: InternalsVisibleTo("LeonAkasaka.UnionAir.Editor.Tests.TestRunner")]

[assembly: LeonAkasaka.UnionAir.Editor.UnionAirBuiltinAssembly]
[assembly: LeonAkasaka.UnionAir.Editor.UnionAirCategory(
    LeonAkasaka.UnionAir.Editor.UnionAirEndpointCategories.TestRunner,
    DisplayName = "Test Runner",
    Risk = LeonAkasaka.UnionAir.Editor.UnionAirEndpointRisk.Custom |
           LeonAkasaka.UnionAir.Editor.UnionAirEndpointRisk.RequestDependent,
    CanDisable = true,
    EnabledByDefault = false)]

namespace LeonAkasaka.UnionAir.Editor
{
    [InitializeOnLoad]
    internal static class TestRunnerInit
    {
        private static readonly Callbacks CallbackInstance = new Callbacks();

        static TestRunnerInit()
        {
            TestRunnerService.Initialize();
            TestDiscoveryHandler.Initialize();
            TestRunnerApi.RegisterTestCallback(CallbackInstance);
            EditorApplication.update -= TestRunnerService.Update;
            EditorApplication.update += TestRunnerService.Update;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting -= TestRunnerService.FlushCurrentBeforeReload;
            EditorApplication.quitting += TestRunnerService.FlushCurrentBeforeReload;
        }

        private static void OnBeforeAssemblyReload()
        {
            TestRunnerService.FlushCurrentBeforeReload();
            TestRunnerApiProvider.Dispose();
        }

        private sealed class Callbacks : IErrorCallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) => TestRunnerService.OnRunStarted(testsToRun);
            public void RunFinished(ITestResultAdaptor result) => TestRunnerService.OnRunFinished(result);
            public void TestStarted(ITestAdaptor test) => TestRunnerService.OnTestStarted(test);
            public void TestFinished(ITestResultAdaptor result) => TestRunnerService.OnTestFinished(result);
            public void OnError(string message) => TestRunnerService.OnError(message);
        }
    }
}

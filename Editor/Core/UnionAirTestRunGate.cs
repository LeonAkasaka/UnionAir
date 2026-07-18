using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    internal static class UnionAirTestRunGate
    {
        internal const string UnionAirSource = "unionAir";
        internal const string ExternalSource = "external";
        private const string ActiveKey = "UnionAir.TestRun.Active";
        private const string SourceKey = "UnionAir.TestRun.Source";
        private const string RunIdKey = "UnionAir.TestRun.Id";

        internal static bool IsActive => SessionState.GetBool(ActiveKey, false);
        internal static string Source => SessionState.GetString(SourceKey, "");
        internal static string RunId => SessionState.GetString(RunIdKey, "");
        internal static string PublicSource => IsActive && !string.IsNullOrEmpty(Source) ? Source : null;
        internal static string PublicRunId
            => IsActive && Source == UnionAirSource && !string.IsNullOrEmpty(RunId) ? RunId : null;

        internal static void Begin(string source, string runId)
        {
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(SourceKey, source ?? "");
            SessionState.SetString(RunIdKey, runId ?? "");
        }

        internal static void End(string source, string runId = null)
        {
            if (!IsActive || Source != source)
                return;
            if (!string.IsNullOrEmpty(runId) && RunId != runId)
                return;

            SessionState.EraseBool(ActiveKey);
            SessionState.EraseString(SourceKey);
            SessionState.EraseString(RunIdKey);
        }
    }
}

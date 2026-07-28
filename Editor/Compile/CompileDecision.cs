using System.Collections.Generic;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Pure decision helpers for compile state and target classification.
    /// </summary>
    internal static class CompileDecision
    {
        internal static string ResolveCompletedResult(int errorCount, int compiledAssemblyCount)
        {
            if (errorCount > 0) return "failed";
            return compiledAssemblyCount > 0 ? "succeeded" : "upToDate";
        }

        internal static string ResolveAbortedResult(string startedAt)
            => string.IsNullOrEmpty(startedAt) ? "notStarted" : "aborted";

        /// <summary>
        /// Classifies a whole cycle conservatively so non-Editor work cannot replace latest.
        /// </summary>
        internal static string ResolveTarget(
            IReadOnlyList<CompileAssemblyRecord> assemblies,
            string source)
        {
            if (assemblies == null || assemblies.Count == 0)
                return source == UnionAirCompileGate.UnionAirSource ? "editor" : "other";

            var sawEditor = false;
            var sawPlayer = false;
            var sawOther = false;

            for (var i = 0; i < assemblies.Count; i++)
            {
                var assembly = assemblies[i];
                var target = CompileMessageParser.ClassifyTarget(
                    assembly == null ? null : assembly.outputDirectory);
                if (target == "editor") sawEditor = true;
                else if (target == "player") sawPlayer = true;
                else sawOther = true;
            }

            if (sawOther || (sawEditor && sawPlayer)) return "other";
            if (sawPlayer) return "player";
            return sawEditor ? "editor" : "other";
        }

        internal static void NormalizeCompletedTarget(CompileRecord record)
        {
            if (record == null || record.state != "completed") return;
            record.target = ResolveTarget(record.assemblies, record.source);
        }

        /// <summary>
        /// Selects the first eligible record from a newest-first list.
        /// </summary>
        internal static CompileRecord SelectLatestEditor(IReadOnlyList<CompileRecord> newestFirst)
        {
            if (newestFirst == null) return null;

            for (var i = 0; i < newestFirst.Count; i++)
            {
                var record = newestFirst[i];
                NormalizeCompletedTarget(record);
                if (record != null && record.state == "completed" && record.target == "editor")
                    return record;
            }

            return null;
        }
    }
}

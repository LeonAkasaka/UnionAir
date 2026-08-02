using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Globalization;

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

        internal static bool TryCreateRecordQuery(
            NameValueCollection values,
            out CompileRecordQuery query,
            out string error)
        {
            query = new CompileRecordQuery();
            error = null;
            values = values ?? new NameValueCollection();

            if (!TryParseRange(values["offset"], 0, 0, int.MaxValue, out query.offset) ||
                !TryParseRange(values["limit"], 20, 1, 100, out query.limit))
            {
                error = "Query parameter 'offset' must be non-negative and 'limit' must be between 1 and 100.";
                return false;
            }

            if (!TryNormalize(values["target"], new string[] { "editor", "player", "other" }, out query.target))
            {
                error = "Query parameter 'target' must be 'editor', 'player', or 'other'.";
                return false;
            }
            if (!TryNormalize(values["source"], new string[] { "unionAir", "external", "build" }, out query.source))
            {
                error = "Query parameter 'source' must be 'unionAir', 'external', or 'build'.";
                return false;
            }
            if (!TryNormalize(values["state"], new string[] { "completed", "aborted" }, out query.state))
            {
                error = "Query parameter 'state' must be 'completed' or 'aborted'.";
                return false;
            }

            return true;
        }

        internal static List<CompileRecord> QueryRetained(
            IReadOnlyList<CompileRecord> records,
            CompileRecordQuery query,
            out int total)
        {
            var matches = new List<CompileRecord>();
            if (records != null)
            {
                for (var i = 0; i < records.Count; i++)
                {
                    var record = records[i];
                    if (record == null || (record.state != "completed" && record.state != "aborted"))
                        continue;
                    if (!Matches(record.target, query.target) ||
                        !Matches(record.source, query.source) ||
                        !Matches(record.state, query.state))
                        continue;
                    matches.Add(record);
                }
            }

            matches.Sort(CompareRecordsNewestFirst);
            total = matches.Count;

            var page = new List<CompileRecord>();
            var start = Math.Min(query.offset, total);
            var end = (int)Math.Min((long)total, (long)start + query.limit);
            for (var i = start; i < end; i++) page.Add(matches[i]);
            return page;
        }

        internal static int CompareRecordsNewestFirst(CompileRecord left, CompileRecord right)
        {
            var finished = string.CompareOrdinal(right?.finishedAt ?? "", left?.finishedAt ?? "");
            if (finished != 0) return finished;
            var requested = string.CompareOrdinal(right?.requestedAt ?? "", left?.requestedAt ?? "");
            if (requested != 0) return requested;
            return string.CompareOrdinal(right?.id ?? "", left?.id ?? "");
        }

        private static bool TryParseRange(string value, int defaultValue, int min, int max, out int result)
        {
            if (string.IsNullOrEmpty(value))
            {
                result = defaultValue;
                return true;
            }
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) &&
                   result >= min && result <= max;
        }

        private static bool TryNormalize(string value, string[] allowed, out string normalized)
        {
            normalized = "";
            if (string.IsNullOrEmpty(value)) return true;
            foreach (var candidate in allowed)
            {
                if (!string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase)) continue;
                normalized = candidate;
                return true;
            }
            return false;
        }

        private static bool Matches(string value, string filter)
            => string.IsNullOrEmpty(filter) ||
               string.Equals(value, filter, StringComparison.OrdinalIgnoreCase);
    }
}

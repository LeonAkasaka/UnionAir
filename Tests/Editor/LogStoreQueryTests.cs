using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the cursor arithmetic behind <c>GET /api/editor/logs</c>.
    /// </summary>
    /// <remarks>
    /// A client polls with <c>since</c> and trusts <c>truncated</c> to tell it when entries were
    /// lost. Getting either wrong silently drops log lines, which is hard to notice in manual
    /// testing and easy to pin down here.
    /// </remarks>
    internal sealed class LogStoreQueryTests
    {
        private static List<LogStore.LogEntry> Buffer(long firstSequence, params string[] types)
        {
            var entries = new List<LogStore.LogEntry>();
            for (var i = 0; i < types.Length; i++)
            {
                entries.Add(new LogStore.LogEntry
                {
                    Sequence = firstSequence + i,
                    Message = "message " + (firstSequence + i),
                    StackTrace = "",
                    Type = types[i],
                    Timestamp = new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc),
                });
            }
            return entries;
        }

        private static long[] Sequences(LogStore.LogQueryResult result)
        {
            var sequences = new long[result.Entries.Count];
            for (var i = 0; i < result.Entries.Count; i++)
                sequences[i] = result.Entries[i].Sequence;
            return sequences;
        }

        [Test]
        public void Filter_ReturnsNewestFirst()
        {
            var result = LogStore.Filter(Buffer(0, "log", "log", "log"), "all", "", 10, -1);
            CollectionAssert.AreEqual(new long[] { 2, 1, 0 }, Sequences(result));
            Assert.AreEqual(0, result.OldestSequence);
            Assert.AreEqual(2, result.LatestSequence);
        }

        [Test]
        public void Filter_TreatsSinceAsExclusive()
        {
            var result = LogStore.Filter(Buffer(0, "log", "log", "log"), "all", "", 10, 0);
            CollectionAssert.AreEqual(new long[] { 2, 1 }, Sequences(result));
        }

        [Test]
        public void Filter_ReturnsNothingWhenCursorIsCurrent()
        {
            var result = LogStore.Filter(Buffer(0, "log", "log", "log"), "all", "", 10, 2);
            Assert.AreEqual(0, result.Entries.Count);
            Assert.IsFalse(result.Truncated);
        }

        [Test]
        public void Filter_ReturnsNothingWhenCursorIsAhead()
        {
            // A caller that is ahead of the buffer has lost nothing; it is simply early.
            var result = LogStore.Filter(Buffer(0, "log"), "all", "", 10, 99);
            Assert.AreEqual(0, result.Entries.Count);
            Assert.IsFalse(result.Truncated);
        }

        [Test]
        public void Filter_ReportsHasMoreWhenLimitCuts()
        {
            var result = LogStore.Filter(Buffer(0, "log", "log", "log"), "all", "", 1, -1);
            CollectionAssert.AreEqual(new long[] { 2 }, Sequences(result));
            Assert.IsTrue(result.HasMore);
        }

        [Test]
        public void Filter_DoesNotReportHasMoreWhenLimitExactlyFits()
        {
            var result = LogStore.Filter(Buffer(0, "log", "log", "log"), "all", "", 3, -1);
            Assert.AreEqual(3, result.Entries.Count);
            Assert.IsFalse(result.HasMore);
        }

        [Test]
        public void Filter_ReportsTruncatedWhenEntriesWereEvicted()
        {
            // The buffer starts at 10 but the caller last saw 3, so 4..9 are gone.
            var result = LogStore.Filter(Buffer(10, "log", "log"), "all", "", 10, 3);
            Assert.IsTrue(result.Truncated);
        }

        [Test]
        public void Filter_DoesNotReportTruncatedWhenCursorIsContiguous()
        {
            var result = LogStore.Filter(Buffer(10, "log", "log"), "all", "", 10, 9);
            Assert.IsFalse(result.Truncated);
        }

        [Test]
        public void Filter_AppliesCursorBeforeTypeFilter()
        {
            // Truncation must describe lost entries, not entries the type filter removed.
            var buffer = Buffer(10, "error", "log");
            var result = LogStore.Filter(buffer, "error", "", 10, 10);
            Assert.AreEqual(0, result.Entries.Count);
            Assert.IsFalse(result.Truncated);
        }

        [Test]
        public void Filter_FiltersByType()
        {
            var result = LogStore.Filter(Buffer(0, "log", "error", "warning"), "error", "", 10, -1);
            CollectionAssert.AreEqual(new long[] { 1 }, Sequences(result));
        }

        [Test]
        public void Filter_FiltersBySearchCaseInsensitively()
        {
            var result = LogStore.Filter(Buffer(0, "log", "log"), "all", "MESSAGE 1", 10, -1);
            CollectionAssert.AreEqual(new long[] { 1 }, Sequences(result));
        }

        [Test]
        public void Filter_ReportsEmptyBufferWithoutClaimingTruncation()
        {
            var result = LogStore.Filter(new List<LogStore.LogEntry>(), "all", "", 10, 5);
            Assert.AreEqual(0, result.Entries.Count);
            Assert.AreEqual(-1, result.OldestSequence);
            Assert.AreEqual(-1, result.LatestSequence);
            Assert.IsFalse(result.Truncated);
        }

        [Test]
        public void FormatLine_EscapesNewlinesSoOneEntryStaysOneLine()
        {
            var entry = new LogStore.LogEntry
            {
                Sequence = 7,
                Message = "line one\nline two",
                StackTrace = "at Foo()\nat Bar()",
                Type = "error",
                Timestamp = new DateTime(2026, 7, 28, 1, 2, 3, DateTimeKind.Utc),
            };

            var line = LogStore.FormatLine(entry);

            Assert.IsFalse(line.Contains("\n"), "An NDJSON record must not span lines.");
            StringAssert.Contains("\"sequence\":7", line);
            StringAssert.Contains("\\nline two", line);
            StringAssert.Contains("2026-07-28T01:02:03", line);
        }

        [Test]
        public void DownloadPaths_IncludeSameSessionPredecessorOldestFirst()
        {
            CollectionAssert.AreEqual(
                new string[] { "console.1.ndjson", "console.ndjson" },
                LogStore.BuildDownloadFilePaths(
                    "console.ndjson",
                    "console.1.ndjson",
                    true,
                    true,
                    true));
            CollectionAssert.AreEqual(
                new string[] { "console.ndjson" },
                LogStore.BuildDownloadFilePaths(
                    "console.ndjson",
                    "console.1.ndjson",
                    false,
                    true,
                    true));
        }

        [Test]
        public void DownloadPaths_TolerateOneMissingFile()
        {
            CollectionAssert.AreEqual(
                new string[] { "console.1.ndjson" },
                LogStore.BuildDownloadFilePaths(
                    "console.ndjson",
                    "console.1.ndjson",
                    true,
                    false,
                    true));
            Assert.AreEqual(
                0,
                LogStore.BuildDownloadFilePaths(
                    "console.ndjson",
                    "console.1.ndjson",
                    true,
                    false,
                    false).Count);
        }

        [Test]
        public void RotationThreshold_IncludesExactBoundary()
        {
            Assert.IsFalse(LogStore.ShouldRotate(LogStore.RotateThresholdBytes - 1, 0));
            Assert.IsTrue(LogStore.ShouldRotate(LogStore.RotateThresholdBytes, 0));
        }

        [Test]
        public void RotationBacksOffAfterAFailedAttempt()
        {
            // A rotation that failed leaves the file over the threshold. Retrying on the very next
            // line would close and reopen the writer for every Console message.
            var size = LogStore.RotateThresholdBytes;
            var nextAttempt = size + LogStore.RotateRetryIntervalBytes;

            Assert.IsFalse(LogStore.ShouldRotate(size, nextAttempt));
            Assert.IsFalse(LogStore.ShouldRotate(nextAttempt - 1, nextAttempt));
            Assert.IsTrue(LogStore.ShouldRotate(nextAttempt, nextAttempt));
        }

        [Test]
        public void CopyStreams_ConcatenatesBoundedSnapshots()
        {
            var oldBytes = Encoding.UTF8.GetBytes("old\nignored");
            var newBytes = Encoding.UTF8.GetBytes("new\n");
            using (var oldStream = new MemoryStream(oldBytes))
            using (var newStream = new MemoryStream(newBytes))
            using (var output = new MemoryStream())
            {
                RestResponse.CopyStreams(
                    new Stream[] { oldStream, newStream },
                    new long[] { 4, newBytes.Length },
                    output);

                Assert.AreEqual("old\nnew\n", Encoding.UTF8.GetString(output.ToArray()));
            }
        }

        [Test]
        public void CopyStreams_StopsInsteadOfSplicingWhenAStreamEndsEarly()
        {
            // Announcing more bytes than a file holds must not append the next file onto a
            // partial record: a truncated transfer is recoverable, a corrupt one is not.
            var truncated = Encoding.UTF8.GetBytes("old\n");
            var following = Encoding.UTF8.GetBytes("new\n");
            using (var oldStream = new MemoryStream(truncated))
            using (var newStream = new MemoryStream(following))
            using (var output = new MemoryStream())
            {
                RestResponse.CopyStreams(
                    new Stream[] { oldStream, newStream },
                    new long[] { truncated.Length + 8, following.Length },
                    output);

                Assert.AreEqual("old\n", Encoding.UTF8.GetString(output.ToArray()));
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    internal sealed class CompileRecordQueryTests
    {
        [Test]
        public void TryCreateRecordQuery_UsesBoundedDefaults()
        {
            CompileRecordQuery query;
            string error;

            Assert.IsTrue(CompileDecision.TryCreateRecordQuery(
                new NameValueCollection(), out query, out error));
            Assert.AreEqual(0, query.offset);
            Assert.AreEqual(20, query.limit);
            Assert.AreEqual("", query.target);
            Assert.IsNull(error);
        }

        [Test]
        public void TryCreateRecordQuery_NormalizesFiltersCaseInsensitively()
        {
            var values = new NameValueCollection
            {
                { "offset", "1" },
                { "limit", "100" },
                { "target", "PLAYER" },
                { "source", "UNIONAIR" },
                { "state", "ABORTED" },
            };

            CompileRecordQuery query;
            string error;
            Assert.IsTrue(CompileDecision.TryCreateRecordQuery(values, out query, out error));
            Assert.AreEqual(1, query.offset);
            Assert.AreEqual(100, query.limit);
            Assert.AreEqual("player", query.target);
            Assert.AreEqual("unionAir", query.source);
            Assert.AreEqual("aborted", query.state);
        }

        [TestCase("offset", "-1")]
        [TestCase("offset", "nope")]
        [TestCase("limit", "0")]
        [TestCase("limit", "101")]
        [TestCase("target", "server")]
        [TestCase("source", "manual")]
        [TestCase("state", "running")]
        public void TryCreateRecordQuery_RejectsInvalidValues(string name, string value)
        {
            CompileRecordQuery query;
            string error;
            Assert.IsFalse(CompileDecision.TryCreateRecordQuery(
                new NameValueCollection { { name, value } }, out query, out error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void QueryRetained_OrdersNewestFirstWithDeterministicTies()
        {
            var records = new List<CompileRecord>
            {
                Record("old", "2026-08-01T00:00:01.0000000Z", "2026-08-01T00:00:00.0000000Z"),
                Record("tie-a", "2026-08-01T00:00:03.0000000Z", "2026-08-01T00:00:02.0000000Z"),
                Record("tie-b", "2026-08-01T00:00:03.0000000Z", "2026-08-01T00:00:02.0000000Z"),
                Record("middle", "2026-08-01T00:00:02.0000000Z", "2026-08-01T00:00:01.0000000Z"),
            };

            int total;
            var page = CompileDecision.QueryRetained(
                records, new CompileRecordQuery(), out total);

            Assert.AreEqual(4, total);
            CollectionAssert.AreEqual(
                new string[] { "tie-b", "tie-a", "middle", "old" },
                page.ConvertAll(record => record.id));
        }

        [Test]
        public void QueryRetained_FiltersBeforePaginationAndExcludesActiveRecords()
        {
            var records = new List<CompileRecord>
            {
                Record("new-player", "2026-08-01T00:00:05.0000000Z", target: "player"),
                Record("editor-two", "2026-08-01T00:00:04.0000000Z", target: "editor"),
                Record("editor-one", "2026-08-01T00:00:03.0000000Z", target: "editor"),
                Record("aborted", "2026-08-01T00:00:02.0000000Z", state: "aborted", target: "editor"),
                Record("active", "", state: "running", target: "editor"),
            };

            int total;
            var page = CompileDecision.QueryRetained(
                records,
                new CompileRecordQuery
                {
                    offset = 1,
                    limit = 1,
                    target = "editor",
                    source = "external",
                    state = "completed",
                },
                out total);

            Assert.AreEqual(2, total);
            Assert.AreEqual(1, page.Count);
            Assert.AreEqual("editor-one", page[0].id);
        }

        [Test]
        public void QueryRetained_ReturnsEmptyPageForEmptyHistory()
        {
            int total;
            var page = CompileDecision.QueryRetained(
                new List<CompileRecord>(), new CompileRecordQuery(), out total);

            Assert.AreEqual(0, total);
            Assert.IsEmpty(page);
        }

        [Test]
        public void LoadRetainedNewestFirst_SkipsUnreadableRecords()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "UnionAir-CompileRecordQueryTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            try
            {
                var valid = Record("valid", "2026-08-01T00:00:01.0000000Z");
                File.WriteAllText(Path.Combine(directory, "valid.json"), JsonUtility.ToJson(valid));
                File.WriteAllText(Path.Combine(directory, "broken.json"), "{not-json");
                LogAssert.Expect(
                    LogType.Warning,
                    new Regex("Could not read a stored compile record"));

                bool completed;
                var records = CompileService.LoadRetainedNewestFirst(directory, out completed);

                Assert.IsTrue(completed);
                Assert.AreEqual(1, records.Count);
                Assert.AreEqual("valid", records[0].id);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static CompileRecord Record(
            string id,
            string finishedAt,
            string requestedAt = "2026-08-01T00:00:00.0000000Z",
            string state = "completed",
            string target = "other")
        {
            return new CompileRecord
            {
                id = id,
                source = "external",
                state = state,
                result = state == "completed" ? "succeeded" : "aborted",
                target = target,
                requestedAt = requestedAt,
                finishedAt = finishedAt,
            };
        }
    }
}

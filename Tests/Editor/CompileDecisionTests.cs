using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    internal sealed class CompileDecisionTests
    {
        private static CompileAssemblyRecord Assembly(string directory)
            => new CompileAssemblyRecord { outputDirectory = directory };

        [Test]
        public void ResolveTarget_RequiresEveryOutputToBeEditor()
        {
            Assert.AreEqual(
                "editor",
                CompileDecision.ResolveTarget(
                    new List<CompileAssemblyRecord> { Assembly("Library/ScriptAssemblies") },
                    "external"));
            Assert.AreEqual(
                "player",
                CompileDecision.ResolveTarget(
                    new List<CompileAssemblyRecord> { Assembly("Library/PlayerScriptAssemblies") },
                    "external"));
            Assert.AreEqual(
                "other",
                CompileDecision.ResolveTarget(
                    new List<CompileAssemblyRecord>
                    {
                        Assembly("Library/ScriptAssemblies"),
                        Assembly("Library/PlayerScriptAssemblies"),
                    },
                    "external"));
            Assert.AreEqual(
                "other",
                CompileDecision.ResolveTarget(
                    new List<CompileAssemblyRecord>
                    {
                        Assembly("Library/ScriptAssemblies"),
                        Assembly("Temp/Custom"),
                    },
                    "external"));
        }

        [Test]
        public void ResolveTarget_ClassifiesZeroOutputBySource()
        {
            var none = new List<CompileAssemblyRecord>();
            Assert.AreEqual("editor", CompileDecision.ResolveTarget(none, "unionAir"));
            Assert.AreEqual("other", CompileDecision.ResolveTarget(none, "external"));
        }

        [Test]
        public void ResolveResult_CoversEveryTerminalBranch()
        {
            Assert.AreEqual("failed", CompileDecision.ResolveCompletedResult(1, 1));
            Assert.AreEqual("succeeded", CompileDecision.ResolveCompletedResult(0, 1));
            Assert.AreEqual("upToDate", CompileDecision.ResolveCompletedResult(0, 0));
            Assert.AreEqual("notStarted", CompileDecision.ResolveAbortedResult(""));
            Assert.AreEqual("aborted", CompileDecision.ResolveAbortedResult("2026-07-28T00:00:00Z"));
        }

        [Test]
        public void SelectLatestEditor_SkipsMisclassifiedNewerRecords()
        {
            var mixed = new CompileRecord
            {
                id = "mixed",
                source = "external",
                state = "completed",
                target = "editor",
                assemblies = new List<CompileAssemblyRecord>
                {
                    Assembly("Library/ScriptAssemblies"),
                    Assembly("Library/PlayerScriptAssemblies"),
                },
            };
            var editor = new CompileRecord
            {
                id = "editor",
                source = "external",
                state = "completed",
                target = "editor",
                assemblies = new List<CompileAssemblyRecord>
                {
                    Assembly("Library/ScriptAssemblies"),
                },
            };

            var selected = CompileDecision.SelectLatestEditor(
                new List<CompileRecord> { mixed, editor });

            Assert.AreSame(editor, selected);
            Assert.AreEqual("other", mixed.target);
        }

        [Test]
        public void RecordPath_StaysBelowRecordsDirectory()
        {
            string path;
            Assert.IsTrue(CompileService.TryGetRecordPath("my-run-1", out path));
            StringAssert.EndsWith(
                Path.Combine("Compile", "records", "my-run-1.json"),
                path);
            Assert.IsFalse(CompileService.TryGetRecordPath("../escape", out path));
        }

        [Test]
        public void Retention_KeepsProtectedRecordWithinExactLimit()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "UnionAir-CompileDecisionTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            try
            {
                for (var i = 0; i < 22; i++)
                {
                    var path = Path.Combine(directory, "record-" + i + ".json");
                    File.WriteAllText(path, "{}");
                    File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(i));
                }

                CompileService.TrimRecordFiles(directory, 20, "record-0");

                Assert.AreEqual(20, Directory.GetFiles(directory, "*.json").Length);
                Assert.IsTrue(File.Exists(Path.Combine(directory, "record-0.json")));
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void Retention_ContinuesPastAnUndeletableRecord()
        {
            // One record that cannot be deleted must not stop the rest of the trim, or retention
            // degrades silently and the directory grows without bound.
            //
            // The failure is injected rather than produced by holding the file open: that only
            // fails on Windows. On Unix, unlink succeeds for an open file and the inode outlives
            // the directory entry, so a lock-based test would assert the opposite there.
            var directory = Path.Combine(
                Path.GetTempPath(),
                "UnionAir-CompileDecisionTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            try
            {
                for (var i = 0; i < 23; i++)
                {
                    var path = Path.Combine(directory, "record-" + i + ".json");
                    File.WriteAllText(path, "{}");
                    File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(i));
                }

                // record-0 is the oldest, so it sorts into the delete range.
                var attempted = new List<string>();
                CompileService.TrimRecordFiles(directory, 20, "", file =>
                {
                    attempted.Add(Path.GetFileNameWithoutExtension(file.Name));
                    if (Path.GetFileNameWithoutExtension(file.Name) == "record-0")
                        throw new IOException("The process cannot access the file.");
                    file.Delete();
                });

                CollectionAssert.AreEquivalent(
                    new string[] { "record-0", "record-1", "record-2" },
                    attempted,
                    "The trim must attempt every record past the limit.");
                Assert.IsTrue(
                    File.Exists(Path.Combine(directory, "record-0.json")),
                    "The record whose deletion failed should survive.");
                Assert.AreEqual(
                    21,
                    Directory.GetFiles(directory, "*.json").Length,
                    "Every deletable record past the limit should still have been trimmed.");
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}

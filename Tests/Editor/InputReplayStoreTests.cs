using System;
using System.IO;
using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers durable storage of replay records against a real temporary directory.
    /// </summary>
    internal sealed class InputReplayStoreTests
    {
        private static string NewTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "UnionAirInputReplayTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static InputReplayRecord Record(string id)
        {
            var record = new InputReplayRecord { id = id, state = InputReplayState.Running, eventCount = 1 };
            record.inputs.Add(new InputReplayEventSpec
            {
                frame = 5,
                type = InputReplayEventType.Perform,
                action = "Player/Jump",
                mode = "press"
            });
            record.events.Add(new InputReplayEventResult { index = 0 });
            return record;
        }

        [Test]
        public void NewId_IsSortableAndSafeAsAFileName()
        {
            var id = InputReplayStore.NewId();
            StringAssert.StartsWith("ir-", id);
            Assert.IsTrue(CompileMessageParser.IsValidId(id), "The id doubles as a file name and route value.");
        }

        [Test]
        public void NewId_DoesNotCollide()
        {
            Assert.AreNotEqual(InputReplayStore.NewId(), InputReplayStore.NewId());
        }

        [Test]
        public void WriteAndRead_RoundTripsARecord()
        {
            var directory = NewTempDirectory();
            try
            {
                var path = Path.Combine(directory, "current.json");
                InputReplayStore.Write(path, Record("ir-1"));

                var restored = InputReplayStore.Read(path);
                Assert.IsNotNull(restored);
                Assert.AreEqual("ir-1", restored.id);
                Assert.AreEqual(1, restored.inputs.Count);
                Assert.AreEqual("Player/Jump", restored.inputs[0].action);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void Write_ReplacesAnExistingRecord()
        {
            var directory = NewTempDirectory();
            try
            {
                var path = Path.Combine(directory, "current.json");
                InputReplayStore.Write(path, Record("ir-1"));
                InputReplayStore.Write(path, Record("ir-2"));

                Assert.AreEqual("ir-2", InputReplayStore.Read(path).id);
                Assert.IsFalse(File.Exists(path + ".tmp"), "The atomic write must not leave a temp file behind.");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void Read_ReturnsNullForAMissingFile()
        {
            var directory = NewTempDirectory();
            try
            {
                Assert.IsNull(InputReplayStore.Read(Path.Combine(directory, "absent.json")));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void Read_ReturnsNullForAMalformedFile()
        {
            var directory = NewTempDirectory();
            try
            {
                var path = Path.Combine(directory, "current.json");
                File.WriteAllText(path, "{ this is not json");
                Assert.IsNull(InputReplayStore.Read(path));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void Read_RestoresEmptyListsWhenTheStoredJsonOmittedThem()
        {
            var directory = NewTempDirectory();
            try
            {
                var path = Path.Combine(directory, "current.json");
                File.WriteAllText(path, "{\"id\":\"ir-1\",\"state\":\"queued\"}");

                var restored = InputReplayStore.Read(path);
                Assert.IsNotNull(restored);
                Assert.IsNotNull(restored.inputs, "Callers iterate these without a null check.");
                Assert.IsNotNull(restored.events);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void Delete_IgnoresAnAbsentFile()
        {
            var directory = NewTempDirectory();
            try
            {
                Assert.DoesNotThrow(() => InputReplayStore.Delete(Path.Combine(directory, "absent.json")));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}

using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    internal sealed class LoadedSceneDiskChangeGuardTests
    {
        [Test]
        public void TryComputeHash_DetectsContentChanges()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "first", Encoding.UTF8);
                Assert.IsTrue(LoadedSceneDiskChangeGuard.TryComputeHash(
                    path, out var firstHash, out var firstReason));
                Assert.IsNull(firstReason);

                File.WriteAllText(path, "second", Encoding.UTF8);
                Assert.IsTrue(LoadedSceneDiskChangeGuard.TryComputeHash(
                    path, out var secondHash, out var secondReason));
                Assert.IsNull(secondReason);
                Assert.AreNotEqual(firstHash, secondHash);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void TryComputeHash_ClassifiesMissingFiles()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "unionair-missing-" + System.Guid.NewGuid().ToString("N") + ".unity");

            Assert.IsFalse(LoadedSceneDiskChangeGuard.TryComputeHash(
                path, out var hash, out var reason));
            Assert.AreEqual("", hash);
            Assert.AreEqual("missing", reason);
        }

        [Test]
        public void ToHex_UsesStableLowercaseEncoding()
        {
            Assert.AreEqual(
                "000f10ff",
                LoadedSceneDiskChangeGuard.ToHex(new byte[] { 0x00, 0x0f, 0x10, 0xff }));
        }

        [TestCase("Assets/Scenes/Level.unity", "Assets/Scenes", true)]
        [TestCase("assets/scenes/Level.unity", "Assets/Scenes/", true)]
        [TestCase("Assets/Scenes", "Assets/Scenes", true)]
        [TestCase("Assets/Scenes2/Level.unity", "Assets/Scenes", false)]
        [TestCase("Assets/Other/Level.unity", "Assets/Scenes", false)]
        public void IsSameOrDescendantPath_RequiresASeparatorBoundary(
            string candidate,
            string root,
            bool expected)
        {
            Assert.AreEqual(
                expected,
                LoadedSceneDiskChangeGuard.IsSameOrDescendantPath(candidate, root));
        }

        [Test]
        public void BuildConflictJson_PreservesOrderAndEscapesFields()
        {
            var conflicts = new List<LoadedSceneDiskConflict>
            {
                new LoadedSceneDiskConflict
                {
                    path = "Assets/Scenes/First.unity",
                    name = "First",
                    isDirty = true,
                    isActive = false,
                    reason = "modified",
                },
                new LoadedSceneDiskConflict
                {
                    path = "Assets/Scenes/Second\"Scene.unity",
                    name = "Second\"Scene",
                    isDirty = false,
                    isActive = true,
                    reason = "missing",
                },
            };

            Assert.AreEqual(
                "{\"error\":\"Cannot refresh assets while loaded scenes have external file changes. " +
                "Unload them before retrying to avoid Unity's interactive Reload dialog.\"," +
                "\"code\":\"loaded_scene_external_change_blocked\",\"loadedScenes\":[" +
                "{\"path\":\"Assets/Scenes/First.unity\",\"name\":\"First\",\"isDirty\":true," +
                "\"isActive\":false,\"reason\":\"modified\"}," +
                "{\"path\":\"Assets/Scenes/Second\\\"Scene.unity\",\"name\":\"Second\\\"Scene\"," +
                "\"isDirty\":false,\"isActive\":true,\"reason\":\"missing\"}" +
                "]}",
                LoadedSceneDiskChangeGuard.BuildConflictJson(conflicts));
        }

        [Test]
        public void BuildAbortReason_ListsEveryConflictInOrder()
        {
            var conflicts = new List<LoadedSceneDiskConflict>
            {
                new LoadedSceneDiskConflict
                {
                    path = "Assets/Scenes/First.unity",
                    reason = "modified",
                },
                new LoadedSceneDiskConflict
                {
                    path = "Assets/Scenes/Second.unity",
                    reason = "unreadable",
                },
            };

            Assert.AreEqual(
                "Asset refresh was blocked because loaded scenes changed externally: " +
                "Assets/Scenes/First.unity (modified), Assets/Scenes/Second.unity (unreadable)",
                LoadedSceneDiskChangeGuard.BuildAbortReason(conflicts));
        }
    }
}

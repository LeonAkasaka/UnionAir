using System;
using System.IO;
using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    internal sealed class ProjectPathsTests
    {
        [Test]
        public void ResolveProjectRoot_UsesTheAssetsParent()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "UnionAir-ProjectPathsTests-" + Guid.NewGuid().ToString("N"));
            var assets = Path.Combine(root, "Assets");
            Directory.CreateDirectory(assets);
            try
            {
                Assert.AreEqual(
                    Path.GetFullPath(root),
                    UnionAirProjectPaths.ResolveProjectRoot(assets));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }
    }
}

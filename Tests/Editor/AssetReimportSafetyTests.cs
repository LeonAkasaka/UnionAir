using System.Collections.Generic;
using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    internal sealed class AssetReimportSafetyTests
    {
        [TestCase(
            "Assets/Scenes/Level.unity",
            false,
            "Assets/Scenes/Level.unity",
            true)]
        [TestCase(
            "assets/scenes/level.UNITY",
            false,
            "Assets/Scenes/Level.unity",
            true)]
        [TestCase(
            "Assets/Scenes/Level.unity/",
            false,
            "Assets/Scenes/Level.unity",
            true)]
        [TestCase(
            "Assets/Scenes",
            true,
            "Assets/Scenes/Level.unity",
            true)]
        [TestCase(
            @"Assets\Scenes\",
            true,
            "Assets/Scenes/Nested/Level.unity",
            true)]
        [TestCase(
            "Assets/Scenes",
            false,
            "Assets/Scenes/Level.unity",
            false)]
        [TestCase(
            "Assets/Scenes",
            true,
            "Assets/Scenes2/Level.unity",
            false)]
        [TestCase(
            "Assets/Scenes/Other.unity",
            false,
            "Assets/Scenes/Level.unity",
            false)]
        [TestCase(
            "Assets/Scenes",
            true,
            "Assets/Scenes/Readme.asset",
            false)]
        [TestCase(
            "Assets/Scenes",
            true,
            "",
            false)]
        public void IsScenePathTargeted_CoversExactAndRecursivePaths(
            string assetPath,
            bool recursive,
            string scenePath,
            bool expected)
        {
            Assert.AreEqual(
                expected,
                LoadedSceneAssetSafety.IsScenePathTargeted(
                    assetPath,
                    recursive,
                    scenePath));
        }

        [Test]
        public void BuildConflictJson_PreservesOrderAndEscapesFields()
        {
            var conflicts = new List<LoadedSceneAssetConflict>
            {
                new LoadedSceneAssetConflict
                {
                    path = "Assets/Scenes/First.unity",
                    name = "First",
                    isDirty = true,
                    isActive = false,
                },
                new LoadedSceneAssetConflict
                {
                    path = "Assets/Scenes/Second\"Scene.unity",
                    name = "Second\"Scene",
                    isDirty = false,
                    isActive = true,
                },
            };

            Assert.AreEqual(
                "{\"error\":\"Cannot reimport loaded scenes. Unload them before retrying to avoid Unity's interactive Reload dialog.\"," +
                "\"code\":\"loaded_scene_reimport_blocked\",\"assetPath\":\"Assets/Scenes\\\"Quoted\"," +
                "\"loadedScenes\":[" +
                "{\"path\":\"Assets/Scenes/First.unity\",\"name\":\"First\",\"isDirty\":true,\"isActive\":false}," +
                "{\"path\":\"Assets/Scenes/Second\\\"Scene.unity\",\"name\":\"Second\\\"Scene\",\"isDirty\":false,\"isActive\":true}" +
                "]}",
                AssetReimportSafety.BuildConflictJson("Assets/Scenes\"Quoted", conflicts));
        }
    }
}

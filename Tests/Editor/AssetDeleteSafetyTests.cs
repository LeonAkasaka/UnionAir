using System.Collections.Generic;
using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    internal sealed class AssetDeleteSafetyTests
    {
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
                "{\"error\":\"Cannot delete loaded scenes. Unload them before retrying to avoid " +
                "deleting the backing asset of an open scene.\"," +
                "\"code\":\"loaded_scene_delete_blocked\",\"assetPath\":\"Assets/Scenes\\\"Quoted\"," +
                "\"loadedScenes\":[" +
                "{\"path\":\"Assets/Scenes/First.unity\",\"name\":\"First\",\"isDirty\":true,\"isActive\":false}," +
                "{\"path\":\"Assets/Scenes/Second\\\"Scene.unity\",\"name\":\"Second\\\"Scene\",\"isDirty\":false,\"isActive\":true}" +
                "]}",
                AssetDeleteSafety.BuildConflictJson("Assets/Scenes\"Quoted", conflicts));
        }
    }
}

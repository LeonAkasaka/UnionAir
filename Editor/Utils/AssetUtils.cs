using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Shared utilities for asset file system operations.
    /// </summary>
    internal static class AssetUtils
    {
        /// <summary>
        /// Ensures that all folders in <paramref name="folderPath"/> exist in the AssetDatabase,
        /// creating any missing intermediate folders.
        /// </summary>
        /// <param name="folderPath">A slash-separated folder path starting with "Assets".</param>
        public static void EnsureDirectory(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath)) return;
            var parts = folderPath.Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}

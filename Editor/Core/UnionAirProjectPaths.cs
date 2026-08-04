using System;
using System.IO;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>Resolves UnionAir's project-local integration paths from Unity's Assets path.</summary>
    internal static class UnionAirProjectPaths
    {
        internal static string ProjectRoot => ResolveProjectRoot(Application.dataPath);

        internal static string IntegrationDirectory =>
            Path.Combine(ProjectRoot, ".unionair");

        internal static string EndpointPath =>
            Path.Combine(IntegrationDirectory, "endpoint.txt");

        internal static string SettingsPath =>
            Path.Combine(IntegrationDirectory, "settings.json");

        internal static string IgnorePath =>
            Path.Combine(IntegrationDirectory, ".gitignore");

        internal static string ResolveProjectRoot(string assetsPath)
        {
            if (string.IsNullOrWhiteSpace(assetsPath))
                throw new ArgumentException("Unity's Assets path is empty.", nameof(assetsPath));

            var fullPath = Path.GetFullPath(assetsPath);
            var parent = Directory.GetParent(fullPath);
            if (parent == null)
                throw new InvalidOperationException(
                    $"Could not resolve the Unity project root from '{assetsPath}'.");
            return parent.FullName;
        }

        internal static string IntegrationDirectoryFor(string projectRoot)
            => Path.Combine(Path.GetFullPath(projectRoot), ".unionair");
    }
}

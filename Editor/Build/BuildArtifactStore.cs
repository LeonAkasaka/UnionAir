using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Owns the on-disk location of player build output.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Artifacts live under <c>Builds/UnionAir/{id}/</c> in the project root, deliberately
    /// <b>not</b> under <c>Library/UnionAir/</c> where compile records, profiling artifacts, and
    /// memory snapshots live. Unity regenerates <c>Library/</c> whenever it decides to, which would
    /// either destroy a ~95 MB artifact silently or orphan it from the record that names it. A
    /// player build is a user-facing deliverable rather than an Editor-internal diagnostic, and it
    /// belongs where a person would look for it.
    /// </para>
    /// <para>
    /// Git exclusion is made deterministic by writing a <c>.gitignore</c> containing <c>*</c> into
    /// the directory when it is created. UnionAir cannot rely on the consuming project's
    /// <c>.gitignore</c>: Unity's standard template excludes <c>/[Bb]uilds/</c>, but not every
    /// project uses it, so git visibility would vary per project and could not be documented as
    /// behavior. The measured output is also close enough to GitHub's 100 MiB file limit that an
    /// accidental commit is a serious hazard rather than an untidy one.
    /// </para>
    /// </remarks>
    internal static class BuildArtifactStore
    {
        /// <summary>
        /// Number of build artifact directories retained.
        /// </summary>
        /// <remarks>
        /// Far smaller than the profiling quota, which would retain roughly fifty builds. These are
        /// hundred-megabyte directories in the user's project rather than in <c>Library/</c>, and a
        /// client normally cares about the build it just requested and perhaps the one before it.
        /// </remarks>
        internal const int RetainedArtifacts = 3;

        /// <summary>Total size cap across retained artifacts.</summary>
        internal const long MaxTotalBytes = 2L * 1024L * 1024L * 1024L;

        internal static readonly string Root = Path.Combine("Builds", "UnionAir");

        /// <summary>
        /// Creates the artifact directory for a build and makes it invisible to git.
        /// </summary>
        /// <param name="id">Build id; also the directory name.</param>
        /// <returns>The project-relative directory path.</returns>
        internal static string CreateDirectory(string id)
        {
            var path = Path.Combine(Root, id);
            Directory.CreateDirectory(path);

            // Written at creation rather than at completion: a build that fails partway through
            // still leaves output behind, and that output must not be committable either.
            var ignorePath = Path.Combine(path, ".gitignore");
            if (!File.Exists(ignorePath))
                File.WriteAllText(ignorePath, "*\n");

            return NormalizePath(path);
        }

        /// <summary>Project-relative directory for a build id, whether or not it exists.</summary>
        internal static string DirectoryFor(string id) => NormalizePath(Path.Combine(Root, id));

        internal static bool Exists(string id)
        {
            try { return Directory.Exists(Path.Combine(Root, id)); }
            catch { return false; }
        }

        /// <summary>Total bytes currently held by all retained artifacts.</summary>
        internal static long TotalBytes() => ProfilingArtifactStore.DirectorySize(Root);

        internal static long DirectoryBytes(string id)
            => ProfilingArtifactStore.DirectorySize(Path.Combine(Root, id));

        /// <summary>Number of artifact directories currently on disk.</summary>
        internal static int ArtifactCount()
        {
            try
            {
                return Directory.Exists(Root)
                    ? new DirectoryInfo(Root).GetDirectories().Length
                    : 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Removes artifact directories past the count and size caps, oldest first.
        /// </summary>
        /// <param name="protectedId">Build that must survive regardless of age.</param>
        /// <remarks>
        /// Follows <see cref="ProfilingArtifactStore.Trim"/>, but with its own caps: sharing the
        /// 5 GB profiling quota would retain roughly fifty builds.
        /// </remarks>
        internal static void Trim(string protectedId)
        {
            try
            {
                if (!Directory.Exists(Root)) return;

                var directories = new List<DirectoryInfo>(new DirectoryInfo(Root).GetDirectories());
                directories.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                for (var i = RetainedArtifacts; i < directories.Count; i++)
                {
                    if (directories[i].Name == protectedId) continue;
                    TryDelete(directories[i].FullName);
                }

                directories = new List<DirectoryInfo>(new DirectoryInfo(Root).GetDirectories());
                directories.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
                foreach (var directory in directories)
                {
                    if (TotalBytes() <= MaxTotalBytes) break;
                    if (directory.Name == protectedId) continue;
                    TryDelete(directory.FullName);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnionAir] Could not trim retained build artifacts: " + ex.Message);
            }
        }

        /// <summary>
        /// Deletes one build's artifact directory.
        /// </summary>
        /// <returns><c>true</c> when the directory no longer exists afterwards.</returns>
        internal static bool Delete(string id)
        {
            var path = Path.Combine(Root, id);
            TryDelete(path);
            return !Directory.Exists(path);
        }

        private static void TryDelete(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnionAir] Could not delete a build artifact: " + ex.Message);
            }
        }

        /// <summary>
        /// Returns the file or directory name Unity should build to for a target.
        /// </summary>
        /// <param name="productName">Player product name from <c>PlayerSettings</c>.</param>
        /// <param name="target">Active build target.</param>
        /// <remarks>
        /// Unity's <c>locationPathName</c> means different things per platform: an executable file
        /// for desktop targets, an archive for Android, and a directory for WebGL and the Apple
        /// platforms. Targets with no known extension get a bare name, which Unity treats as a
        /// directory, so an unfamiliar platform degrades to the common case instead of producing an
        /// executable with a wrong suffix.
        /// </remarks>
        internal static string OutputFileName(string productName, BuildTarget target)
        {
            var name = SanitizeProductName(productName);
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return name + ".exe";
                case BuildTarget.StandaloneOSX:
                    return name + ".app";
                case BuildTarget.Android:
                    return name + (EditorUserBuildSettings.buildAppBundle ? ".aab" : ".apk");
                default:
                    return name;
            }
        }

        /// <summary>
        /// Reduces a product name to something safe to use as a file name.
        /// </summary>
        /// <remarks>
        /// A product name is free text a project author can set to anything, including characters
        /// no file system accepts and path separators that would place output outside the artifact
        /// directory. The output location is server-controlled, and that has to hold even when the
        /// name it is derived from is not.
        /// </remarks>
        internal static string SanitizeProductName(string productName)
        {
            if (string.IsNullOrEmpty(productName))
                return "player";

            var sb = new StringBuilder(productName.Length);
            foreach (var ch in productName)
            {
                if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == ' ')
                    sb.Append(ch);
            }

            var sanitized = sb.ToString().Trim();
            return sanitized.Length == 0 ? "player" : sanitized;
        }

        internal static string NormalizePath(string path)
            => string.IsNullOrEmpty(path) ? "" : path.Replace('\\', '/');
    }
}

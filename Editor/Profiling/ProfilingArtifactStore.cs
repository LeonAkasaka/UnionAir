using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace LeonAkasaka.UnionAir.Editor
{
    internal static class ProfilingArtifactStore
    {
        internal const long MaxTotalBytes = 5L * 1024L * 1024L * 1024L;
        internal static readonly string Root = Path.Combine("Library", "UnionAir");
        internal static readonly string ProfilingRoot = Path.Combine(Root, "Profiling");
        internal static readonly string SnapshotRoot = Path.Combine(Root, "MemorySnapshots");

        internal static bool IsOverQuota() => DirectorySize(Root) >= MaxTotalBytes;

        internal static string CreateDirectory(string root, string id)
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, id);
            Directory.CreateDirectory(path);
            return path;
        }

        internal static void WriteAtomicJson(string path, string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Root);
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        /// <summary>
        /// Writes a record atomically and reports whether it reached disk.
        /// </summary>
        /// <param name="path">Destination path.</param>
        /// <param name="json">Serialized record.</param>
        /// <param name="error">Message from the last failed attempt, or <c>null</c> on success.</param>
        /// <returns><c>true</c> when the record is durable.</returns>
        /// <remarks>
        /// Used where a caller must not proceed unless the record survives — a liveness flag opened
        /// for a record that was never written has nothing left to close it after a domain reload.
        /// <para>
        /// Retried once, immediately. On Windows <c>File.Replace</c> fails transiently when a
        /// virus scanner or the search indexer holds the destination for the moment it takes to
        /// swap, and a second attempt normally succeeds. Further attempts would only delay
        /// reporting the failures that do not clear on their own — a full disk, a read-only
        /// directory, a path the Editor cannot write.
        /// </para>
        /// </remarks>
        internal static bool TryWriteAtomicJson(string path, string json, out string error)
        {
            error = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    WriteAtomicJson(path, json);
                    error = null;
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }
            return false;
        }

        internal static string Sha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        internal static void Trim(string root, int keep, string protectedId)
        {
            if (!Directory.Exists(root)) return;
            var directories = new List<DirectoryInfo>(new DirectoryInfo(root).GetDirectories());
            directories.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            for (var i = keep; i < directories.Count; i++)
            {
                if (directories[i].Name == protectedId) continue;
                TryDeleteDirectory(directories[i].FullName);
            }

            directories = new List<DirectoryInfo>(new DirectoryInfo(root).GetDirectories());
            directories.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
            foreach (var directory in directories)
            {
                if (DirectorySize(Root) <= MaxTotalBytes) break;
                if (directory.Name == protectedId) continue;
                TryDeleteDirectory(directory.FullName);
            }
        }

        internal static long DirectorySize(string path)
        {
            if (!Directory.Exists(path)) return 0;
            long total = 0;
            try
            {
                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(file).Length; }
                    catch { }
                }
            }
            catch { }
            return total;
        }

        internal static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch (Exception ex) { UnityEngine.Debug.LogWarning("[UnionAir] Could not delete profiling artifact: " + ex.Message); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>Publishes the active API Base URL to the project-local discovery file.</summary>
    internal static class UnionAirEndpointDiscovery
    {
        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);
        private static readonly string[] RequiredIgnoreLines =
        {
            ".gitignore",
            "endpoint.txt",
            "*.tmp"
        };

        internal static string LastWarning { get; private set; }

        internal static string FormatBaseUrl(int port)
            => $"http://localhost:{port}/api/";

        internal static void Publish(int port)
        {
            string error;
            if (TryPublish(UnionAirProjectPaths.ProjectRoot, port, out error))
            {
                LastWarning = null;
                return;
            }

            ReportWarning(error);
        }

        internal static void RemoveOwned(int port)
        {
            bool removed;
            string error;
            if (!TryRemoveOwned(
                    UnionAirProjectPaths.ProjectRoot,
                    FormatBaseUrl(port),
                    out removed,
                    out error))
                ReportWarning(error);
        }

        internal static void ClearStaleAtEditorStart()
        {
            string error;
            if (!TryClearStale(UnionAirProjectPaths.ProjectRoot, out error))
                ReportWarning(error);
        }

        internal static bool TryPublish(string projectRoot, int port, out string error)
        {
            error = null;
            if (port < 1 || port > 65535)
            {
                error = $"Cannot publish endpoint discovery for invalid port {port}.";
                return false;
            }

            try
            {
                var directory = UnionAirProjectPaths.IntegrationDirectoryFor(projectRoot);
                Directory.CreateDirectory(directory);
                EnsureIgnoreFile(Path.Combine(directory, ".gitignore"));
                WriteAtomicText(
                    Path.Combine(directory, "endpoint.txt"),
                    FormatBaseUrl(port) + "\n");
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to publish .unionair/endpoint.txt: {ex.Message}";
                return false;
            }
        }

        internal static bool TryRemoveOwned(
            string projectRoot,
            string expectedBaseUrl,
            out bool removed,
            out string error)
        {
            removed = false;
            error = null;
            try
            {
                var path = Path.Combine(
                    UnionAirProjectPaths.IntegrationDirectoryFor(projectRoot),
                    "endpoint.txt");
                if (!File.Exists(path))
                    return true;

                var discovered = File.ReadAllText(path, Utf8WithoutBom).Trim();
                if (!string.Equals(discovered, expectedBaseUrl, StringComparison.Ordinal))
                    return true;

                File.Delete(path);
                removed = true;
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to remove .unionair/endpoint.txt: {ex.Message}";
                return false;
            }
        }

        internal static bool TryClearStale(string projectRoot, out string error)
        {
            error = null;
            try
            {
                var path = Path.Combine(
                    UnionAirProjectPaths.IntegrationDirectoryFor(projectRoot),
                    "endpoint.txt");
                if (File.Exists(path))
                    File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to clear stale .unionair/endpoint.txt: {ex.Message}";
                return false;
            }
        }

        internal static void WriteAtomicText(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
                throw new InvalidOperationException($"'{path}' has no parent directory.");

            Directory.CreateDirectory(directory);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(
                           temp,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                using (var writer = new StreamWriter(stream, Utf8WithoutBom))
                {
                    writer.Write(content);
                    writer.Flush();
                    stream.Flush(true);
                }

                if (File.Exists(path))
                    File.Replace(temp, path, null);
                else
                    File.Move(temp, path);
            }
            finally
            {
                try
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                catch
                {
                    // A leftover temporary file is ignored by .unionair/.gitignore.
                }
            }
        }

        private static void EnsureIgnoreFile(string path)
        {
            var lines = new List<string>();
            if (File.Exists(path))
                lines.AddRange(File.ReadAllLines(path, Utf8WithoutBom));

            var changed = !File.Exists(path);
            foreach (var required in RequiredIgnoreLines)
            {
                var present = false;
                foreach (var line in lines)
                {
                    if (string.Equals(line.Trim(), required, StringComparison.Ordinal))
                    {
                        present = true;
                        break;
                    }
                }

                if (present) continue;
                lines.Add(required);
                changed = true;
            }

            if (changed)
                WriteAtomicText(path, string.Join("\n", lines.ToArray()) + "\n");
        }

        private static void ReportWarning(string message)
        {
            LastWarning = message;
            Debug.LogWarning("[UnionAir] " + message);
        }
    }
}

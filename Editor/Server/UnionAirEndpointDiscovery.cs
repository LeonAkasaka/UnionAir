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
            try
            {
                string error;
                string warning;
                if (!TryPublish(
                        UnionAirProjectPaths.ProjectRoot,
                        port,
                        out error,
                        out warning))
                {
                    ReportWarning(error);
                    return;
                }

                if (!string.IsNullOrEmpty(warning))
                    ReportWarning(warning);
                else
                    ClearWarning();
            }
            catch (Exception ex)
            {
                ReportWarning(
                    $"Failed to resolve the project path for endpoint publication: {ex.Message}");
            }
        }

        internal static void RemoveOwned(int port)
        {
            try
            {
                bool removed;
                string error;
                if (!TryRemoveOwned(
                        UnionAirProjectPaths.ProjectRoot,
                        FormatBaseUrl(port),
                        out removed,
                        out error))
                    ReportWarning(error);
                else
                    ClearWarning();
            }
            catch (Exception ex)
            {
                ReportWarning(
                    $"Failed to resolve the project path for endpoint removal: {ex.Message}");
            }
        }

        internal static void ClearStaleAtEditorStart()
        {
            try
            {
                string error;
                if (!TryClearStale(UnionAirProjectPaths.ProjectRoot, out error))
                    ReportWarning(error);
                else
                    ClearWarning();
            }
            catch (Exception ex)
            {
                ReportWarning(
                    $"Failed to resolve the project path for stale endpoint cleanup: {ex.Message}");
            }
        }

        internal static bool TryPublish(string projectRoot, int port, out string error)
        {
            string warning;
            return TryPublish(projectRoot, port, out error, out warning);
        }

        internal static bool TryPublish(
            string projectRoot,
            int port,
            out string error,
            out string warning)
        {
            error = null;
            warning = null;
            if (port < 1 || port > 65535)
            {
                error = $"Cannot publish endpoint discovery for invalid port {port}.";
                return false;
            }

            try
            {
                var directory = UnionAirProjectPaths.IntegrationDirectoryFor(projectRoot);
                WriteAtomicText(
                    Path.Combine(directory, "endpoint.txt"),
                    FormatBaseUrl(port) + "\n");
            }
            catch (Exception ex)
            {
                error = $"Failed to publish .unionair/endpoint.txt: {ex.Message}";
                return false;
            }

            string ignoreError;
            if (!TryEnsureIgnore(projectRoot, out ignoreError))
                warning = ignoreError;
            return true;
        }

        internal static bool TryEnsureIgnore(string projectRoot, out string error)
        {
            error = null;
            try
            {
                var directory = UnionAirProjectPaths.IntegrationDirectoryFor(projectRoot);
                EnsureIgnoreFile(Path.Combine(directory, ".gitignore"));
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to maintain .unionair/.gitignore: {ex.Message}";
                return false;
            }
        }

        internal static void UpdateIgnoreWarning(string warning)
        {
            if (!string.IsNullOrEmpty(warning))
            {
                ReportWarning(warning);
                return;
            }

            if (LastWarning != null &&
                LastWarning.StartsWith(
                    "Failed to maintain .unionair/.gitignore:",
                    StringComparison.Ordinal))
                ClearWarning();
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
            Exception lastError = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    WriteAtomicTextOnce(path, content);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            throw lastError;
        }

        private static void WriteAtomicTextOnce(string path, string content)
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
            var exists = File.Exists(path);
            var existing = exists ? File.ReadAllText(path, Utf8WithoutBom) : string.Empty;
            bool changed;
            var updated = AddRequiredIgnoreRules(existing, out changed);
            if (!exists || changed)
                WriteAtomicText(path, updated);
        }

        internal static string AddRequiredIgnoreRules(string existing, out bool changed)
        {
            existing = existing ?? string.Empty;
            var lines = new List<string>();
            using (var reader = new StringReader(existing))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                    lines.Add(line);
            }

            var missing = new List<string>();
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
                missing.Add(required);
            }

            changed = missing.Count > 0;
            if (!changed)
                return existing;

            var newline = DetectLineEnding(existing);
            var updated = new StringBuilder(existing);
            if (updated.Length > 0 &&
                updated[updated.Length - 1] != '\r' &&
                updated[updated.Length - 1] != '\n')
                updated.Append(newline);
            foreach (var required in missing)
                updated.Append(required).Append(newline);
            return updated.ToString();
        }

        private static string DetectLineEnding(string content)
        {
            for (var i = 0; i < content.Length; i++)
            {
                if (content[i] == '\r')
                    return i + 1 < content.Length && content[i + 1] == '\n'
                        ? "\r\n"
                        : "\r";
                if (content[i] == '\n')
                    return "\n";
            }
            return Environment.NewLine;
        }

        private static void ClearWarning()
        {
            LastWarning = null;
        }

        private static void ReportWarning(string message)
        {
            LastWarning = message;
            Debug.LogWarning("[UnionAir] " + message);
        }
    }
}

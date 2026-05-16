using System.Collections.Generic;
using System.IO;
using System.Net;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Utility for reading and lightly parsing HTTP request bodies.
    /// </summary>
    internal static class RequestBodyReader
    {
        /// <summary>Reads the entire request body as a UTF-8 string.</summary>
        public static string ReadString(HttpListenerRequest request)
        {
            if (request.ContentLength64 == 0) return string.Empty;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                return reader.ReadToEnd();
        }

        /// <summary>
        /// Extracts a string value from a flat JSON object body.
        /// Handles simple cases: <c>"key": "value"</c> and <c>"key": null</c>.
        /// Returns null when the key is absent.
        /// </summary>
        public static string GetString(string json, string key)
        {
            var token = FindToken(json, key);
            if (token == null) return null;
            token = token.Trim();
            if (token == "null") return null;
            if (token.Length >= 2 && token[0] == '"' && token[token.Length - 1] == '"')
                return token.Substring(1, token.Length - 2)
                    .Replace("\\\"", "\"").Replace("\\\\", "\\")
                    .Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");
            return token;
        }

        /// <summary>
        /// Extracts a bool value from a flat JSON object body.
        /// Returns null when the key is absent or the value is not a JSON boolean.
        /// </summary>
        public static bool? GetBool(string json, string key)
        {
            var token = FindToken(json, key);
            if (token == null) return null;
            token = token.Trim();
            if (token == "true")  return true;
            if (token == "false") return false;
            return null;
        }

        /// <summary>
        /// Extracts an int value from a flat JSON object body.
        /// Returns null when the key is absent or the value cannot be parsed.
        /// </summary>
        public static int? GetInt(string json, string key)
        {
            var token = FindToken(json, key);
            if (token == null) return null;
            if (int.TryParse(token.Trim(), out int v)) return v;
            return null;
        }

        /// <summary>
        /// Extracts a float value from a flat JSON object body.
        /// Returns null when the key is absent or the value cannot be parsed.
        /// </summary>
        public static float? GetFloat(string json, string key)
        {
            var token = FindToken(json, key);
            if (token == null) return null;
            if (float.TryParse(token.Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v)) return v;
            return null;
        }

        /// <summary>
        /// Extracts a nested JSON object as a raw substring from a flat JSON body.
        /// Returns null when the key is absent.
        /// </summary>
        public static string GetObject(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var searchKey = $"\"{key}\"";
            int keyIdx = json.IndexOf(searchKey);
            if (keyIdx < 0) return null;

            int colonIdx = json.IndexOf(':', keyIdx + searchKey.Length);
            if (colonIdx < 0) return null;

            int start = colonIdx + 1;
            while (start < json.Length && json[start] != '{') start++;
            if (start >= json.Length) return null;

            int depth = 0;
            int end = start;
            while (end < json.Length)
            {
                if (json[end] == '{') depth++;
                else if (json[end] == '}') { depth--; if (depth == 0) break; }
                end++;
            }
            return json.Substring(start, end - start + 1);
        }

        /// <summary>
        /// Extracts a JSON array value for the given key and returns each element as a raw JSON string.
        /// Handles nested objects/arrays. Returns an empty list when the key is absent or not an array.
        /// </summary>
        public static List<string> GetArray(string json, string key)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(json)) return result;

            var searchKey = $"\"{key}\"";
            int keyIdx = json.IndexOf(searchKey);
            if (keyIdx < 0) return result;

            int colonIdx = json.IndexOf(':', keyIdx + searchKey.Length);
            if (colonIdx < 0) return result;

            int start = colonIdx + 1;
            while (start < json.Length && json[start] != '[') start++;
            if (start >= json.Length) return result;

            start++; // skip '['

            while (start < json.Length)
            {
                // Skip whitespace between elements
                while (start < json.Length && (json[start] == ' ' || json[start] == '\t' ||
                                               json[start] == '\r' || json[start] == '\n' || json[start] == ','))
                    start++;

                if (start >= json.Length || json[start] == ']') break;

                if (json[start] == '{')
                {
                    // Extract balanced object
                    int depth = 0;
                    int end = start;
                    bool inString = false;
                    while (end < json.Length)
                    {
                        var c = json[end];
                        if (!inString)
                        {
                            if (c == '"') inString = true;
                            else if (c == '{') depth++;
                            else if (c == '}') { depth--; if (depth == 0) break; }
                        }
                        else
                        {
                            if (c == '\\') { end++; } // skip escaped char
                            else if (c == '"') inString = false;
                        }
                        end++;
                    }
                    result.Add(json.Substring(start, end - start + 1));
                    start = end + 1;
                }
                else
                {
                    // Non-object element (string, number, bool) — skip
                    int end = start;
                    while (end < json.Length && json[end] != ',' && json[end] != ']') end++;
                    start = end;
                }
            }

            return result;
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Finds the raw value token for a given key in a simple (non-nested) JSON object.
        /// Returns null if the key is not found.
        /// </summary>
        private static string FindToken(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;

            var searchKey = $"\"{key}\"";
            int keyIdx = json.IndexOf(searchKey);
            if (keyIdx < 0) return null;

            int colonIdx = json.IndexOf(':', keyIdx + searchKey.Length);
            if (colonIdx < 0) return null;

            int start = colonIdx + 1;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t')) start++;
            if (start >= json.Length) return null;

            // Quoted string
            if (json[start] == '"')
            {
                int end = start + 1;
                while (end < json.Length)
                {
                    if (json[end] == '\\') { end += 2; continue; }
                    if (json[end] == '"') break;
                    end++;
                }
                return json.Substring(start, end - start + 1);
            }

            // Non-quoted scalar (bool, number, null)
            {
                int end = start;
                while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != '\n')
                    end++;
                return json.Substring(start, end - start).Trim();
            }
        }
    }
}

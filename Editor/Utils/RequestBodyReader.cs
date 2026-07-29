using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Utility for reading and lightly parsing HTTP request bodies.
    /// </summary>
    /// <remarks>
    /// This helper intentionally supports the simple JSON shapes used by UnionAir handlers. It is not a
    /// general-purpose JSON parser.
    /// </remarks>
    public static class RequestBodyReader
    {
        private static readonly ConditionalWeakTable<HttpListenerRequest, BodyCache> CachedBodies =
            new ConditionalWeakTable<HttpListenerRequest, BodyCache>();

        /// <summary>
        /// Reads the entire request body as a string using the request encoding.
        /// </summary>
        /// <param name="request">HTTP request whose body should be read.</param>
        /// <returns>The request body, or an empty string when the request has no body.</returns>
        public static string ReadString(HttpListenerRequest request)
        {
            BodyCache cached;
            if (CachedBodies.TryGetValue(request, out cached))
                return cached.Value;

            if (request.ContentLength64 == 0) return string.Empty;
            string body;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                body = reader.ReadToEnd();

            CachedBodies.Add(request, new BodyCache(body));
            return body;
        }

        private sealed class BodyCache
        {
            public BodyCache(string value)
            {
                Value = value;
            }

            public string Value { get; }
        }

        /// <summary>
        /// Extracts a string value from a flat JSON object body.
        /// Handles simple cases: <c>"key": "value"</c> and <c>"key": null</c>.
        /// Returns null when the key is absent.
        /// </summary>
        /// <param name="json">JSON object text to inspect.</param>
        /// <param name="key">Field name to read.</param>
        /// <returns>The string value, a raw scalar token, or null when absent/null.</returns>
        public static string GetString(string json, string key)
        {
            var token = FindToken(json, key);
            if (token == null) return null;
            token = token.Trim();
            if (token == "null") return null;
            if (token.Length >= 2 && token[0] == '"' && token[token.Length - 1] == '"')
                return UnescapeJsonString(token.Substring(1, token.Length - 2));
            return token;
        }

        /// <summary>
        /// Extracts a string value directly from a request body, combining ReadString and GetString.
        /// </summary>
        /// <param name="request">HTTP request whose body should be read.</param>
        /// <param name="key">Field name to read.</param>
        /// <returns>The string value, or null when absent/null.</returns>
        public static string GetString(HttpListenerRequest request, string key)
            => GetString(ReadString(request), key);

        /// <summary>
        /// Extracts a bool value directly from a request body, combining ReadString and GetBool.
        /// </summary>
        public static bool? GetBool(HttpListenerRequest request, string key)
            => GetBool(ReadString(request), key);

        /// <summary>
        /// Extracts an int value directly from a request body, combining ReadString and GetInt.
        /// </summary>
        public static int? GetInt(HttpListenerRequest request, string key)
            => GetInt(ReadString(request), key);

        /// <summary>
        /// Extracts a float value directly from a request body, combining ReadString and GetFloat.
        /// </summary>
        public static float? GetFloat(HttpListenerRequest request, string key)
            => GetFloat(ReadString(request), key);

        /// <summary>
        /// Extracts a nested JSON object directly from a request body, combining ReadString and GetObject.
        /// </summary>
        public static string GetObject(HttpListenerRequest request, string key)
            => GetObject(ReadString(request), key);

        /// <summary>
        /// Extracts a JSON array directly from a request body, combining ReadString and GetArray.
        /// </summary>
        public static List<string> GetArray(HttpListenerRequest request, string key)
            => GetArray(ReadString(request), key);

        /// <summary>
        /// Extracts an optional top-level JSON array whose elements must all be strings.
        /// </summary>
        /// <remarks>
        /// A missing key is valid and returns an empty array. The method returns false when the
        /// key is present but its value is not a well-formed array of JSON strings.
        /// </remarks>
        /// <param name="json">JSON object text to inspect.</param>
        /// <param name="key">Top-level field name to read.</param>
        /// <param name="values">The decoded strings, or an empty array when the key is absent.</param>
        /// <returns>True when the field is absent or is a valid string array; otherwise false.</returns>
        public static bool TryGetStringArray(string json, string key, out string[] values)
        {
            values = new string[0];
            if (string.IsNullOrEmpty(json)) return true;

            int keyIdx = FindTopLevelKey(json, key);
            if (keyIdx < 0) return true;

            int colonIdx = json.IndexOf(':', keyIdx);
            if (colonIdx < 0) return false;

            int position = colonIdx + 1;
            SkipWhitespace(json, ref position);
            if (position >= json.Length || json[position] != '[') return false;
            position++;

            var result = new List<string>();
            SkipWhitespace(json, ref position);
            if (position < json.Length && json[position] == ']')
            {
                values = result.ToArray();
                return true;
            }

            while (position < json.Length)
            {
                string value;
                if (!TryReadJsonString(json, ref position, out value)) return false;
                result.Add(value);

                SkipWhitespace(json, ref position);
                if (position >= json.Length) return false;
                if (json[position] == ']')
                {
                    values = result.ToArray();
                    return true;
                }
                if (json[position] != ',') return false;
                position++;
                SkipWhitespace(json, ref position);
            }

            return false;
        }

        /// <summary>
        /// Extracts a bool value from a flat JSON object body.
        /// Returns null when the key is absent or the value is not a JSON boolean.
        /// </summary>
        /// <param name="json">JSON object text to inspect.</param>
        /// <param name="key">Field name to read.</param>
        /// <returns>The boolean value, or null when absent or invalid.</returns>
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
        /// <param name="json">JSON object text to inspect.</param>
        /// <param name="key">Field name to read.</param>
        /// <returns>The integer value, or null when absent or invalid.</returns>
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
        /// <param name="json">JSON object text to inspect.</param>
        /// <param name="key">Field name to read.</param>
        /// <returns>The floating-point value, or null when absent or invalid.</returns>
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
        /// <param name="json">JSON object text to inspect.</param>
        /// <param name="key">Field name to read.</param>
        /// <returns>The nested object as raw JSON, or null when absent.</returns>
        public static string GetObject(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int keyIdx = FindTopLevelKey(json, key);
            if (keyIdx < 0) return null;

            int colonIdx = json.IndexOf(':', keyIdx);
            if (colonIdx < 0) return null;

            int start = colonIdx + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            if (start >= json.Length || json[start] != '{') return null;

            int depth = 0;
            int end = start;
            bool inString = false;
            while (end < json.Length)
            {
                var c = json[end];
                if (inString)
                {
                    if (c == '\\') end++;
                    else if (c == '"') inString = false;
                }
                else if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) break; }
                end++;
            }
            if (end >= json.Length || depth != 0) return null;
            return json.Substring(start, end - start + 1);
        }

        /// <summary>
        /// Extracts a JSON array value for the given key and returns each element as a raw JSON string.
        /// Handles nested objects/arrays. Returns an empty list when the key is absent or not an array.
        /// </summary>
        /// <param name="json">JSON object text to inspect.</param>
        /// <param name="key">Field name to read.</param>
        /// <returns>Raw JSON strings for object elements in the array.</returns>
        public static List<string> GetArray(string json, string key)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(json)) return result;

            int keyIdx = FindTopLevelKey(json, key);
            if (keyIdx < 0) return result;

            int colonIdx = json.IndexOf(':', keyIdx);
            if (colonIdx < 0) return result;

            int start = colonIdx + 1;
            // Anchored to this key's own value: scanning forward for the next '[' anywhere in the
            // document would latch onto an unrelated later array when the value is not one.
            SkipWhitespace(json, ref start);
            if (start >= json.Length || json[start] != '[') return result;

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

        /// <summary>
        /// Extracts a top-level JSON array as its raw <c>[...]</c> token, brackets included.
        /// Returns null when the key is absent or its value is not an array.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="GetArray(string,string)"/> this preserves scalar elements, so it suits
        /// numeric arrays such as <c>"value": [1.0, 0.0]</c>.
        /// </remarks>
        /// <param name="json">JSON object text to inspect.</param>
        /// <param name="key">Top-level field name to read.</param>
        /// <returns>The raw array token, or null when absent or not an array.</returns>
        public static string GetRawArray(string json, string key)
        {
            int start;
            if (!TryFindArrayStart(json, key, out start)) return null;

            int end = start;
            if (!TrySkipValue(json, ref end)) return null;
            return json.Substring(start, end - start);
        }

        /// <summary>
        /// Extracts a top-level JSON array and returns every element as raw JSON text, including
        /// scalars, and reports malformed input rather than degrading to an empty list.
        /// </summary>
        /// <remarks>
        /// This is the strict counterpart to <see cref="GetArray(string,string)"/>. It distinguishes
        /// an absent key from a present-but-invalid one, and error messages identify the offending
        /// element as <c>key[index]</c> so callers can report which entry a client must fix.
        /// </remarks>
        /// <param name="json">JSON object text to inspect.</param>
        /// <param name="key">Top-level field name to read.</param>
        /// <param name="elements">Raw JSON text of each element; empty when the key is absent.</param>
        /// <param name="present">Whether the key exists at the top level.</param>
        /// <param name="error">Failure description naming the offending element, or null on success.</param>
        /// <returns>True when the field is absent or is a well-formed array; otherwise false.</returns>
        public static bool TryGetArrayElements(
            string json,
            string key,
            out List<string> elements,
            out bool present,
            out string error)
        {
            elements = new List<string>();
            present = false;
            error = null;
            if (string.IsNullOrEmpty(json)) return true;

            int keyIdx = FindTopLevelKey(json, key);
            if (keyIdx < 0) return true;
            present = true;

            int colonIdx = json.IndexOf(':', keyIdx);
            if (colonIdx < 0)
            {
                error = $"'{key}' is malformed.";
                return false;
            }

            int position = colonIdx + 1;
            SkipWhitespace(json, ref position);
            if (position >= json.Length || json[position] != '[')
            {
                error = $"'{key}' must be a JSON array.";
                return false;
            }
            position++; // skip '['

            while (true)
            {
                SkipWhitespace(json, ref position);
                if (position >= json.Length)
                {
                    error = $"'{key}' is not a well-formed JSON array.";
                    return false;
                }
                if (json[position] == ']') return true;

                int valueStart = position;
                if (!TrySkipValue(json, ref position))
                {
                    error = $"{key}[{elements.Count}] is not a well-formed JSON value.";
                    return false;
                }
                elements.Add(json.Substring(valueStart, position - valueStart));

                SkipWhitespace(json, ref position);
                if (position >= json.Length)
                {
                    error = $"'{key}' is not a well-formed JSON array.";
                    return false;
                }
                if (json[position] == ']') return true;
                if (json[position] != ',')
                {
                    error = $"'{key}' is not a well-formed JSON array.";
                    return false;
                }

                position++; // skip ','
                SkipWhitespace(json, ref position);
                if (position < json.Length && json[position] == ']')
                {
                    error = $"'{key}' is not a well-formed JSON array.";
                    return false;
                }
            }
        }

        /// <summary>
        /// Extracts a required top-level array of finite numbers, such as <c>"value": [1.0, 0.0]</c>.
        /// </summary>
        /// <param name="json">JSON object text to inspect.</param>
        /// <param name="key">Top-level field name to read.</param>
        /// <param name="values">The parsed numbers, or null on failure.</param>
        /// <param name="error">Failure description naming the offending element, or null on success.</param>
        /// <returns>True when the field is a well-formed array of finite numbers.</returns>
        public static bool TryGetFloatArray(string json, string key, out float[] values, out string error)
        {
            values = null;

            List<string> elements;
            bool present;
            if (!TryGetArrayElements(json, key, out elements, out present, out error)) return false;
            if (!present)
            {
                error = $"Required field '{key}' is missing.";
                return false;
            }

            var parsed = new float[elements.Count];
            for (int i = 0; i < elements.Count; i++)
            {
                float value;
                if (!float.TryParse(elements[i].Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out value) ||
                    float.IsNaN(value) || float.IsInfinity(value))
                {
                    error = $"{key}[{i}] must be a finite number.";
                    return false;
                }
                parsed[i] = value;
            }

            values = parsed;
            return true;
        }

        /// <summary>Returns whether a field is present at the top level of a JSON object.</summary>
        public static bool HasTopLevelField(string json, string key)
            => !string.IsNullOrEmpty(json) && FindTopLevelKey(json, key) >= 0;

        // ── Private helpers ──────────────────────────────────────────────────────

        private static string UnescapeJsonString(string s)
        {
            if (s.IndexOf('\\') < 0) return s;

            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                i++;
                switch (s[i])
                {
                    case '"':  sb.Append('"');  break;
                    case '\\': sb.Append('\\'); break;
                    case '/':  sb.Append('/');  break;
                    case 'b':  sb.Append('\b'); break;
                    case 'f':  sb.Append('\f'); break;
                    case 'n':  sb.Append('\n'); break;
                    case 'r':  sb.Append('\r'); break;
                    case 't':  sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 < s.Length && IsHex(s, i + 1, 4))
                        {
                            sb.Append((char)System.Convert.ToInt32(s.Substring(i + 1, 4), 16));
                            i += 4;
                        }
                        else
                        {
                            sb.Append('u');
                        }
                        break;
                    default: sb.Append(s[i]); break;
                }
            }
            return sb.ToString();
        }

        private static bool TryReadJsonString(string json, ref int position, out string value)
        {
            value = null;
            if (position >= json.Length || json[position] != '"') return false;

            int start = ++position;
            while (position < json.Length)
            {
                if (json[position] == '\\')
                {
                    if (position + 1 >= json.Length) return false;
                    var escape = json[position + 1];
                    if (escape == 'u')
                    {
                        if (position + 5 >= json.Length || !IsHex(json, position + 2, 4)) return false;
                        position += 6;
                        continue;
                    }
                    if (escape != '"' && escape != '\\' && escape != '/' &&
                        escape != 'b' && escape != 'f' && escape != 'n' &&
                        escape != 'r' && escape != 't')
                        return false;
                    position += 2;
                    continue;
                }
                if (json[position] == '"')
                {
                    value = UnescapeJsonString(json.Substring(start, position - start));
                    position++;
                    return true;
                }
                if (json[position] < ' ') return false;
                position++;
            }
            return false;
        }

        private static void SkipWhitespace(string json, ref int position)
        {
            while (position < json.Length && char.IsWhiteSpace(json[position])) position++;
        }

        /// <summary>
        /// Locates the opening bracket of a top-level array value, anchored to the key's own value
        /// rather than to the next bracket appearing anywhere in the document.
        /// </summary>
        private static bool TryFindArrayStart(string json, string key, out int start)
        {
            start = -1;
            if (string.IsNullOrEmpty(json)) return false;

            int keyIdx = FindTopLevelKey(json, key);
            if (keyIdx < 0) return false;

            int colonIdx = json.IndexOf(':', keyIdx);
            if (colonIdx < 0) return false;

            int position = colonIdx + 1;
            SkipWhitespace(json, ref position);
            if (position >= json.Length || json[position] != '[') return false;

            start = position;
            return true;
        }

        /// <summary>
        /// Advances past one complete JSON value, leaving <paramref name="position"/> just after it.
        /// Handles objects, arrays, strings, and scalars, and is string- and escape-aware so that
        /// brackets inside string literals do not affect nesting depth.
        /// </summary>
        private static bool TrySkipValue(string json, ref int position)
        {
            if (position >= json.Length) return false;

            var first = json[position];
            if (first == '"')
            {
                string ignored;
                return TryReadJsonString(json, ref position, out ignored);
            }

            if (first == '{' || first == '[')
            {
                var open = first;
                var close = first == '{' ? '}' : ']';
                int depth = 0;
                while (position < json.Length)
                {
                    var c = json[position];
                    if (c == '"')
                    {
                        string ignored;
                        if (!TryReadJsonString(json, ref position, out ignored)) return false;
                        continue;
                    }
                    if (c == open)
                    {
                        depth++;
                    }
                    else if (c == close)
                    {
                        depth--;
                        position++;
                        if (depth == 0) return true;
                        continue;
                    }
                    position++;
                }
                return false;
            }

            // Scalar: number, true, false, or null.
            int scalarStart = position;
            while (position < json.Length)
            {
                var c = json[position];
                if (c == ',' || c == ']' || c == '}' || char.IsWhiteSpace(c)) break;
                position++;
            }
            return position > scalarStart;
        }

        private static bool IsHex(string s, int start, int count)
        {
            for (int i = start; i < start + count && i < s.Length; i++)
            {
                var c = s[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Finds the raw value token for a given key in a simple (non-nested) JSON object.
        /// Returns null if the key is not found.
        /// </summary>
        private static string FindToken(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;

            int keyIdx = FindTopLevelKey(json, key);
            if (keyIdx < 0) return null;

            int colonIdx = json.IndexOf(':', keyIdx);
            if (colonIdx < 0) return null;

            int start = colonIdx + 1;
            // All whitespace, not just spaces and tabs: pretty-printed bodies put the value
            // on the line after the colon, and stopping at the newline would read it as absent.
            SkipWhitespace(json, ref start);
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

        private static int FindTopLevelKey(string json, string key)
        {
            int depth = 0;
            for (int i = 0; i < json.Length; i++)
            {
                var c = json[i];
                if (c == '"')
                {
                    int start = i + 1;
                    int end = start;
                    while (end < json.Length)
                    {
                        if (json[end] == '\\') { end += 2; continue; }
                        if (json[end] == '"') break;
                        end++;
                    }

                    if (depth == 1 && end < json.Length)
                    {
                        var candidate = json.Substring(start, end - start);
                        if (candidate == key)
                        {
                            int after = end + 1;
                            while (after < json.Length && (json[after] == ' ' || json[after] == '\t' || json[after] == '\r' || json[after] == '\n'))
                                after++;
                            if (after < json.Length && json[after] == ':')
                                return start - 1;
                        }
                    }

                    i = end;
                    continue;
                }

                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
            }

            return -1;
        }
    }
}

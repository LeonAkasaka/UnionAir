using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LeonAkasaka.UnionAir.Editor
{
    internal sealed class UnionAirProjectSettingsDocument
    {
        internal int Port;
        internal bool AutoStart;
        internal readonly HashSet<string> EnabledCategories =
            new HashSet<string>(StringComparer.Ordinal);
        internal bool CustomHandlers;
        internal bool AllowSceneChanges;
    }

    internal static class UnionAirProjectSettingsParser
    {
        private enum JsonKind
        {
            Object,
            Array,
            String,
            Number,
            Boolean,
            Null
        }

        private sealed class JsonValue
        {
            internal JsonKind Kind;
            internal Dictionary<string, JsonValue> Object;
            internal List<JsonValue> Array;
            internal string Text;
            internal bool Boolean;
        }

        private sealed class Reader
        {
            private readonly string _json;
            private int _position;

            internal Reader(string json)
            {
                _json = json ?? "";
            }

            internal bool TryRead(out JsonValue value, out string error)
            {
                value = null;
                error = null;
                if (_json.Length > 0 && _json[0] == '\uFEFF')
                {
                    error = "Project settings must use UTF-8 without a byte-order mark.";
                    return false;
                }
                SkipWhitespace();
                if (!TryReadValue(out value, out error)) return false;
                SkipWhitespace();
                if (_position != _json.Length)
                {
                    error = $"Unexpected JSON content at character {_position}.";
                    return false;
                }
                return true;
            }

            private bool TryReadValue(out JsonValue value, out string error)
            {
                value = null;
                error = null;
                if (_position >= _json.Length)
                {
                    error = "Unexpected end of JSON.";
                    return false;
                }

                switch (_json[_position])
                {
                    case '{': return TryReadObject(out value, out error);
                    case '[': return TryReadArray(out value, out error);
                    case '"':
                        string text;
                        if (!TryReadString(out text, out error)) return false;
                        value = new JsonValue { Kind = JsonKind.String, Text = text };
                        return true;
                    case 't': return TryReadLiteral("true", new JsonValue { Kind = JsonKind.Boolean, Boolean = true }, out value, out error);
                    case 'f': return TryReadLiteral("false", new JsonValue { Kind = JsonKind.Boolean, Boolean = false }, out value, out error);
                    case 'n': return TryReadLiteral("null", new JsonValue { Kind = JsonKind.Null }, out value, out error);
                    default: return TryReadNumber(out value, out error);
                }
            }

            private bool TryReadObject(out JsonValue value, out string error)
            {
                value = new JsonValue
                {
                    Kind = JsonKind.Object,
                    Object = new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                };
                error = null;
                _position++;
                SkipWhitespace();
                if (Consume('}')) return true;

                while (true)
                {
                    string key;
                    if (!TryReadString(out key, out error)) return false;
                    if (value.Object.ContainsKey(key))
                    {
                        error = $"Duplicate JSON field '{key}'.";
                        return false;
                    }
                    SkipWhitespace();
                    if (!Consume(':'))
                    {
                        error = $"Expected ':' after field '{key}'.";
                        return false;
                    }
                    SkipWhitespace();
                    JsonValue child;
                    if (!TryReadValue(out child, out error)) return false;
                    value.Object.Add(key, child);
                    SkipWhitespace();
                    if (Consume('}')) return true;
                    if (!Consume(','))
                    {
                        error = "Expected ',' or '}' in JSON object.";
                        return false;
                    }
                    SkipWhitespace();
                    if (_position < _json.Length && _json[_position] == '}')
                    {
                        error = "Trailing commas are not valid JSON.";
                        return false;
                    }
                }
            }

            private bool TryReadArray(out JsonValue value, out string error)
            {
                value = new JsonValue { Kind = JsonKind.Array, Array = new List<JsonValue>() };
                error = null;
                _position++;
                SkipWhitespace();
                if (Consume(']')) return true;

                while (true)
                {
                    JsonValue child;
                    if (!TryReadValue(out child, out error)) return false;
                    value.Array.Add(child);
                    SkipWhitespace();
                    if (Consume(']')) return true;
                    if (!Consume(','))
                    {
                        error = "Expected ',' or ']' in JSON array.";
                        return false;
                    }
                    SkipWhitespace();
                    if (_position < _json.Length && _json[_position] == ']')
                    {
                        error = "Trailing commas are not valid JSON.";
                        return false;
                    }
                }
            }

            private bool TryReadString(out string value, out string error)
            {
                value = null;
                error = null;
                if (!Consume('"'))
                {
                    error = $"Expected a JSON string at character {_position}.";
                    return false;
                }

                var result = new StringBuilder();
                while (_position < _json.Length)
                {
                    var c = _json[_position++];
                    if (c == '"')
                    {
                        value = result.ToString();
                        return true;
                    }
                    if (c < ' ')
                    {
                        error = "JSON strings cannot contain unescaped control characters.";
                        return false;
                    }
                    if (c != '\\')
                    {
                        result.Append(c);
                        continue;
                    }
                    if (_position >= _json.Length)
                    {
                        error = "Incomplete JSON escape sequence.";
                        return false;
                    }
                    var escaped = _json[_position++];
                    switch (escaped)
                    {
                        case '"': result.Append('"'); break;
                        case '\\': result.Append('\\'); break;
                        case '/': result.Append('/'); break;
                        case 'b': result.Append('\b'); break;
                        case 'f': result.Append('\f'); break;
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        case 'u':
                            if (_position + 4 > _json.Length)
                            {
                                error = "Incomplete JSON Unicode escape.";
                                return false;
                            }
                            int code;
                            if (!int.TryParse(
                                    _json.Substring(_position, 4),
                                    NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture,
                                    out code))
                            {
                                error = "Invalid JSON Unicode escape.";
                                return false;
                            }
                            result.Append((char)code);
                            _position += 4;
                            break;
                        default:
                            error = $"Invalid JSON escape '\\{escaped}'.";
                            return false;
                    }
                }
                error = "Unterminated JSON string.";
                return false;
            }

            private bool TryReadNumber(out JsonValue value, out string error)
            {
                value = null;
                error = null;
                var start = _position;
                if (_position < _json.Length && _json[_position] == '-') _position++;
                if (_position >= _json.Length || !char.IsDigit(_json[_position]))
                {
                    error = $"Invalid JSON value at character {start}.";
                    return false;
                }
                if (_json[_position] == '0') _position++;
                else while (_position < _json.Length && char.IsDigit(_json[_position])) _position++;
                if (_position < _json.Length && (_json[_position] == '.' || _json[_position] == 'e' || _json[_position] == 'E'))
                {
                    error = "Project settings accept integer JSON numbers only.";
                    return false;
                }
                value = new JsonValue
                {
                    Kind = JsonKind.Number,
                    Text = _json.Substring(start, _position - start)
                };
                return true;
            }

            private bool TryReadLiteral(string literal, JsonValue literalValue, out JsonValue value, out string error)
            {
                value = null;
                error = null;
                if (_position + literal.Length > _json.Length ||
                    string.CompareOrdinal(_json, _position, literal, 0, literal.Length) != 0)
                {
                    error = $"Invalid JSON value at character {_position}.";
                    return false;
                }
                _position += literal.Length;
                value = literalValue;
                return true;
            }

            private void SkipWhitespace()
            {
                while (_position < _json.Length && char.IsWhiteSpace(_json[_position])) _position++;
            }

            private bool Consume(char value)
            {
                if (_position >= _json.Length || _json[_position] != value) return false;
                _position++;
                return true;
            }
        }

        private static readonly HashSet<string> BuiltInCategories = new HashSet<string>(StringComparer.Ordinal)
        {
            UnionAirEndpointCategories.Read,
            UnionAirEndpointCategories.SceneWrite,
            UnionAirEndpointCategories.AssetWrite,
            UnionAirEndpointCategories.PlayMode,
            UnionAirEndpointCategories.EditorActions,
            UnionAirEndpointCategories.TestRunner,
            UnionAirEndpointCategories.Profiling,
            UnionAirEndpointCategories.Build
        };

        internal static bool TryParse(
            string json,
            ISet<string> knownCustomCategories,
            out UnionAirProjectSettingsDocument document,
            out string error)
        {
            document = null;
            JsonValue root;
            if (!new Reader(json).TryRead(out root, out error)) return false;
            if (!RequireObject(root, "root", out error)) return false;
            if (!RequireFields(root, "root", out error, "schemaVersion", "server", "api", "playMode")) return false;

            int schemaVersion;
            if (!RequireInt(root.Object["schemaVersion"], "schemaVersion", out schemaVersion, out error)) return false;
            if (schemaVersion != 1)
            {
                error = $"Unsupported schemaVersion {schemaVersion}; expected 1.";
                return false;
            }

            var server = root.Object["server"];
            if (!RequireObject(server, "server", out error) ||
                !RequireFields(server, "server", out error, "port", "autoStart")) return false;
            int port;
            bool autoStart;
            if (!RequireInt(server.Object["port"], "server.port", out port, out error) ||
                !RequireBool(server.Object["autoStart"], "server.autoStart", out autoStart, out error)) return false;
            if (!UnionAirPortAllocator.IsValidConfiguredPort(port))
            {
                error = "server.port must be 0 (Automatic) or a Fixed port from 1 through 65535.";
                return false;
            }

            var api = root.Object["api"];
            if (!RequireObject(api, "api", out error) ||
                !RequireFields(api, "api", out error, "enabledCategories", "customHandlers")) return false;
            bool customHandlers;
            if (!RequireBool(api.Object["customHandlers"], "api.customHandlers", out customHandlers, out error)) return false;
            var categories = api.Object["enabledCategories"];
            if (categories.Kind != JsonKind.Array)
            {
                error = "api.enabledCategories must be an array of strings.";
                return false;
            }

            var playMode = root.Object["playMode"];
            if (!RequireObject(playMode, "playMode", out error) ||
                !RequireFields(playMode, "playMode", out error, "allowSceneChanges")) return false;
            bool allowSceneChanges;
            if (!RequireBool(playMode.Object["allowSceneChanges"], "playMode.allowSceneChanges", out allowSceneChanges, out error)) return false;

            var parsed = new UnionAirProjectSettingsDocument
            {
                Port = port,
                AutoStart = autoStart,
                CustomHandlers = customHandlers,
                AllowSceneChanges = allowSceneChanges
            };
            for (var i = 0; i < categories.Array.Count; i++)
            {
                var category = categories.Array[i];
                if (category.Kind != JsonKind.String)
                {
                    error = $"api.enabledCategories[{i}] must be a string.";
                    return false;
                }
                var id = category.Text;
                if (id == UnionAirEndpointCategories.Read)
                {
                    error = "The always-enabled 'read' category must not appear in api.enabledCategories.";
                    return false;
                }
                if (!IsKnownCategory(id, knownCustomCategories))
                {
                    error = $"Unknown category identifier '{id}'.";
                    return false;
                }
                if (!parsed.EnabledCategories.Add(id))
                {
                    error = $"Duplicate category identifier '{id}'.";
                    return false;
                }
                if (id.StartsWith("custom:", StringComparison.Ordinal) && !customHandlers)
                {
                    error = $"Category '{id}' requires api.customHandlers to be true.";
                    return false;
                }
            }

            document = parsed;
            return true;
        }

        internal static string Serialize(UnionAirProjectSettingsDocument document)
        {
            var categories = new List<string>(document.EnabledCategories);
            categories.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            sb.Append("{\n  \"schemaVersion\": 1,\n  \"server\": {\n    \"port\": ")
              .Append(document.Port)
              .Append(",\n    \"autoStart\": ")
              .Append(document.AutoStart ? "true" : "false")
              .Append("\n  },\n  \"api\": {\n    \"enabledCategories\": [");
            for (var i = 0; i < categories.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("\n      \"").Append(Escape(categories[i])).Append('"');
            }
            if (categories.Count > 0) sb.Append('\n').Append("    ");
            sb.Append("],\n    \"customHandlers\": ")
              .Append(document.CustomHandlers ? "true" : "false")
              .Append("\n  },\n  \"playMode\": {\n    \"allowSceneChanges\": ")
              .Append(document.AllowSceneChanges ? "true" : "false")
              .Append("\n  }\n}\n");
            return sb.ToString();
        }

        private static bool IsKnownCategory(string id, ISet<string> knownCustomCategories)
        {
            if (BuiltInCategories.Contains(id)) return true;
            if (!id.StartsWith("custom:", StringComparison.Ordinal)) return false;
            var customId = id.Substring("custom:".Length);
            return customId.Length > 0 && knownCustomCategories != null && knownCustomCategories.Contains(customId);
        }

        private static bool RequireObject(JsonValue value, string path, out string error)
        {
            error = null;
            if (value.Kind == JsonKind.Object) return true;
            error = $"{path} must be a JSON object.";
            return false;
        }

        private static bool RequireFields(JsonValue value, string path, out string error, params string[] fields)
        {
            error = null;
            var expected = new HashSet<string>(fields, StringComparer.Ordinal);
            foreach (var key in value.Object.Keys)
            {
                if (expected.Contains(key)) continue;
                error = $"Unknown field '{(path == "root" ? key : path + "." + key)}'.";
                return false;
            }
            foreach (var field in fields)
            {
                if (value.Object.ContainsKey(field)) continue;
                error = $"Required field '{(path == "root" ? field : path + "." + field)}' is missing.";
                return false;
            }
            return true;
        }

        private static bool RequireInt(JsonValue value, string path, out int result, out string error)
        {
            result = 0;
            error = null;
            if (value.Kind == JsonKind.Number && int.TryParse(value.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)) return true;
            error = $"{path} must be an integer.";
            return false;
        }

        private static bool RequireBool(JsonValue value, string path, out bool result, out string error)
        {
            result = false;
            error = null;
            if (value.Kind == JsonKind.Boolean)
            {
                result = value.Boolean;
                return true;
            }
            error = $"{path} must be a boolean.";
            return false;
        }

        private static string Escape(string value)
            => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    internal static class UnionAirProjectSettingsLoader
    {
        internal static UnionAirProjectSettingsState Load(
            string path,
            ISet<string> knownCustomCategories,
            out UnionAirProjectSettingsDocument document,
            out string error)
        {
            document = null;
            error = null;
            if (!System.IO.File.Exists(path))
                return UnionAirProjectSettingsState.Missing;

            try
            {
                UnionAirProjectSettingsDocument parsed;
                string parseError;
                if (UnionAirProjectSettingsParser.TryParse(
                        System.IO.File.ReadAllText(path, new UTF8Encoding(false)),
                        knownCustomCategories,
                        out parsed,
                        out parseError))
                {
                    document = parsed;
                    return UnionAirProjectSettingsState.Valid;
                }

                error = parseError;
                return UnionAirProjectSettingsState.Invalid;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return UnionAirProjectSettingsState.Invalid;
            }
        }
    }
}

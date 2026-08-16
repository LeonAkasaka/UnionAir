using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles material asset operations:
    ///   POST  /api/assets/materials               — create a new material
    ///   PATCH /api/assets/materials?guid=&lt;guid&gt; — update material properties
    /// </summary>
    internal class MaterialWriteHandler
    {
        private static readonly string[] TextureReferenceFields =
        {
            "assetGuid", "assetPath", "assetType"
        };

        private static readonly string[] ColorComponentFields = { "r", "g", "b", "a" };
        private static readonly string[] VectorComponentFields = { "x", "y", "z", "w" };

        public void Handle(UnionAirRequest request, UnionAirResponse response)
        {
            if (request.HttpMethod == "POST")
                HandleCreate(request, response);
            else
                HandleUpdate(request, response);
        }

        // ── POST /api/assets/materials ───────────────────────────────────────

        private static void HandleCreate(UnionAirRequest request, UnionAirResponse response)
        {
            var body       = RequestBodyReader.ReadString(request);
            var assetPath  = RequestBodyReader.GetString(body, "assetPath");
            var shaderName = RequestBodyReader.GetString(body, "shader") ?? "Standard";

            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendError(response, "Missing required field: assetPath", 400);
                return;
            }
            if (!assetPath.EndsWith(".mat"))
            {
                RestResponse.SendError(response, "assetPath must end with .mat", 400);
                return;
            }

            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                RestResponse.SendError(response, $"Shader not found: {shaderName}", 400);
                return;
            }

            // Ensure directory exists
            AssetUtils.EnsureDirectory(System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/'));

            var mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, assetPath);
            AssetDatabase.SaveAssets();

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            RestResponse.Send(response,
                $"{{\"assetPath\":\"{RestResponse.EscapeJson(assetPath)}\",\"guid\":\"{RestResponse.EscapeJson(guid)}\"," +
                $"\"shader\":\"{RestResponse.EscapeJson(shaderName)}\"}}", 201);
        }

        // ── PATCH /api/assets/materials?guid= ───────────────────────────────

        private static void HandleUpdate(UnionAirRequest request, UnionAirResponse response)
        {
            var guid = request.QueryString["guid"];
            if (string.IsNullOrEmpty(guid))
            {
                RestResponse.SendError(response, "Missing required query parameter: guid", 400);
                return;
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendNotFound(response, $"No asset found for GUID: {guid}");
                return;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (mat == null)
            {
                RestResponse.SendError(response, $"Asset is not a Material: {assetPath}", 400);
                return;
            }

            var body = RequestBodyReader.ReadString(request);
            var propsJson = RequestBodyReader.GetObject(body, "properties");
            if (string.IsNullOrEmpty(propsJson))
            {
                RestResponse.SendError(
                    response,
                    RequestBodyReader.HasTopLevelField(body, "properties")
                        ? "Field 'properties' must be a JSON object."
                        : "Missing required field: properties",
                    400);
                return;
            }

            // Every key the request sent has to be accounted for, so the names are read before
            // anything is written: a key that names no shader property is the client's typo, and
            // answering 200 with it missing from "updated" is not an answer a client can act on.
            if (!RequestBodyReader.TryGetTopLevelFieldNames(
                    propsJson, out var requestedKeys, out var keyError))
            {
                RestResponse.SendError(response, $"Invalid 'properties': {keyError}", 400);
                return;
            }

            if (!TryPlanMaterialWrites(mat, propsJson, requestedKeys, out var writes, out var error, out var statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }

            foreach (var write in writes) write(mat);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            // The plan refused the request outright if any key could not be written, so "updated"
            // is the keys the request sent — a client can compare the two and find them equal.
            var sb = new StringBuilder();
            sb.Append("{\"updated\":[");
            for (int i = 0; i < requestedKeys.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{RestResponse.EscapeJson(requestedKeys[i])}\"");
            }
            sb.Append("]}");
            RestResponse.Send(response, sb.ToString());
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves one write per requested key, or refuses the whole request.
        /// </summary>
        /// <remarks>
        /// The request drives this walk, not the shader. Walking the shader's properties instead
        /// can only report what it recognised, never what the client sent and this endpoint did
        /// not understand, which is the half a client needs. Nothing is applied until every key
        /// has resolved, so a refused request leaves the material as it was.
        /// </remarks>
        private static bool TryPlanMaterialWrites(
            Material mat,
            string propsJson,
            List<string> requestedKeys,
            out List<Action<Material>> writes,
            out string error,
            out int statusCode)
        {
            writes = new List<Action<Material>>();
            error = null;
            statusCode = 400;

            var shader = mat.shader;
            if (shader == null)
            {
                error = $"Material has no shader: {mat.name}";
                return false;
            }

            var declared = new Dictionary<string, UnityEngine.Rendering.ShaderPropertyType>(
                StringComparer.Ordinal);
            var propCount = shader.GetPropertyCount();
            for (int i = 0; i < propCount; i++)
                declared[shader.GetPropertyName(i)] = shader.GetPropertyType(i);

            foreach (var key in requestedKeys)
            {
                if (!declared.TryGetValue(key, out var propertyType))
                {
                    error = $"No property named '{key}' on shader '{shader.name}'. " +
                            "Property names are the ones the shader declares, and are case-sensitive.";
                    return false;
                }

                switch (propertyType)
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                    {
                        var obj = RequestBodyReader.GetObject(propsJson, key);
                        if (obj == null)
                        {
                            error = $"Property '{key}' is a Color and expects a JSON object with r, g, b and a.";
                            return false;
                        }
                        var current = mat.GetColor(key);
                        if (!TryReadComponents(
                                obj, key, "Color", ColorComponentFields,
                                new[] { current.r, current.g, current.b, current.a },
                                out var parts, out error))
                            return false;
                        var color = new Color(parts[0], parts[1], parts[2], parts[3]);
                        writes.Add(m => m.SetColor(key, color));
                        break;
                    }
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                    {
                        if (!RequestBodyReader.TryGetFloatValue(propsJson, key, out var value, out _))
                        {
                            error = $"Property '{key}' is a {propertyType} and expects a JSON number.";
                            return false;
                        }
                        writes.Add(m => m.SetFloat(key, value));
                        break;
                    }
                    case UnityEngine.Rendering.ShaderPropertyType.Int:
                    {
                        if (!RequestBodyReader.TryGetIntValue(propsJson, key, out var value, out _))
                        {
                            error = $"Property '{key}' is an Integer and expects a JSON integer.";
                            return false;
                        }
                        writes.Add(m => m.SetInteger(key, value));
                        break;
                    }
                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    {
                        var obj = RequestBodyReader.GetObject(propsJson, key);
                        if (obj == null)
                        {
                            error = $"Property '{key}' is a Vector and expects a JSON object with x, y, z and w.";
                            return false;
                        }
                        var current = mat.GetVector(key);
                        if (!TryReadComponents(
                                obj, key, "Vector", VectorComponentFields,
                                new[] { current.x, current.y, current.z, current.w },
                                out var parts, out error))
                            return false;
                        var vector = new Vector4(parts[0], parts[1], parts[2], parts[3]);
                        writes.Add(m => m.SetVector(key, vector));
                        break;
                    }
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    {
                        if (!TryResolveTexture(propsJson, key, out var texture, out error, out statusCode))
                            return false;
                        writes.Add(m => m.SetTexture(key, texture));
                        break;
                    }
                    default:
                    {
                        error = $"Property '{key}' has shader property type {propertyType}, " +
                                "which this endpoint cannot write.";
                        return false;
                    }
                }
            }

            return true;
        }

        // An omitted component keeps the material's current value, which is what makes a partial
        // colour useful. An unknown, duplicate or non-numeric one is refused instead, so a value
        // the request carried cannot go missing between the key check and the write.
        private static bool TryReadComponents(
            string obj,
            string key,
            string typeName,
            string[] fields,
            float[] current,
            out float[] values,
            out string error)
        {
            values = null;

            if (!RequestBodyReader.TryValidateObjectFields(obj, fields, out var objectError))
            {
                error = $"Invalid {typeName} property '{key}': {objectError}";
                return false;
            }

            var read = new float[fields.Length];
            var anyPresent = false;
            for (int i = 0; i < fields.Length; i++)
            {
                if (!RequestBodyReader.TryGetFloatValue(obj, fields[i], out var value, out var present))
                {
                    error = $"Property '{key}' expects a JSON number for '{fields[i]}'.";
                    return false;
                }
                read[i] = present ? value : current[i];
                anyPresent |= present;
            }

            if (!anyPresent)
            {
                error = $"Property '{key}' is a {typeName} and expects at least one of " +
                        $"{string.Join(", ", fields)}.";
                return false;
            }

            values = read;
            error = null;
            return true;
        }

        // The object reference every other write accepts, so a texture reported by
        // GET /api/gameobjects can be sent back without translation.
        private static bool TryResolveTexture(
            string propsJson, string key, out Texture texture, out string error, out int statusCode)
        {
            texture = null;
            error = null;
            statusCode = 400;

            var rawValue = RequestBodyReader.GetRawValue(propsJson, key);
            if (rawValue == null)
            {
                // The key was read from the top level, so no value here means the value is present
                // and unreadable -- an unescaped backslash in a Windows path is the likely one --
                // rather than the field being absent.
                error = $"Property '{key}' is not a well-formed JSON value.";
                return false;
            }

            rawValue = rawValue.Trim();
            if (rawValue == "null") return true;

            if (rawValue.Length == 0 || rawValue[0] != '{')
            {
                error = $"Property '{key}' is a Texture and expects null, or a JSON object naming an " +
                        "asset with assetGuid or assetPath. A bare GUID string is not an object reference.";
                return false;
            }

            if (!RequestBodyReader.TryValidateObjectFields(
                    rawValue, TextureReferenceFields, out var objectError))
            {
                error = $"Invalid object reference property '{key}': {objectError}";
                return false;
            }

            if (!ObjectReferenceResolverUtils.TryReadReferenceField(
                    rawValue, "assetType", $"property '{key}'",
                    out var requestedTypeName, out error, out statusCode))
                return false;

            var requestedType = ObjectReferenceResolverUtils.ResolveOptionalReferenceType(
                requestedTypeName,
                $"property '{key}'",
                "Unknown object reference type for {0}: {1}",
                out error,
                out statusCode);
            if (error != null) return false;

            if (!ObjectReferenceResolverUtils.TryReadReferenceField(
                    rawValue, "assetGuid", $"property '{key}'",
                    out var assetGuid, out error, out statusCode) ||
                !ObjectReferenceResolverUtils.TryReadReferenceField(
                    rawValue, "assetPath", $"property '{key}'",
                    out var assetPath, out error, out statusCode))
                return false;

            if (!ObjectReferenceResolverUtils.TryResolveAssetReference(
                    assetGuid,
                    assetPath,
                    typeof(Texture),
                    requestedType,
                    $"property '{key}'",
                    "Object reference {0} requires assetGuid or assetPath.",
                    "Asset not found for {0} with GUID: {1}",
                    "Asset not found or incompatible for {0}: {1}",
                    "Resolved object for {0} is not assignable to field type {1}.",
                    out var value,
                    out error,
                    out statusCode))
                return false;

            texture = value as Texture;
            return true;
        }

    }
}

using System;
using System.Collections.Generic;
using System.Net;
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
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (request.HttpMethod == "POST")
                HandleCreate(request, response);
            else
                HandleUpdate(request, response);
        }

        // ── POST /api/assets/materials ───────────────────────────────────────

        private static void HandleCreate(HttpListenerRequest request, HttpListenerResponse response)
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

        private static void HandleUpdate(HttpListenerRequest request, HttpListenerResponse response)
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
                RestResponse.SendError(response, "Missing required field: properties", 400);
                return;
            }

            var updated = ApplyMaterialProperties(mat, propsJson);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            var sb = new StringBuilder();
            sb.Append("{\"updated\":[");
            for (int i = 0; i < updated.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{RestResponse.EscapeJson(updated[i])}\"");
            }
            sb.Append("]}");
            RestResponse.Send(response, sb.ToString());
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static List<string> ApplyMaterialProperties(Material mat, string propsJson)
        {
            var updated = new List<string>();

            // Iterate over shader properties to find matching names
            int propCount = mat.shader.GetPropertyCount();
            for (int i = 0; i < propCount; i++)
            {
                var propName = mat.shader.GetPropertyName(i);
                if (propsJson.IndexOf($"\"{propName}\"", StringComparison.Ordinal) < 0) continue;

                var propType = mat.shader.GetPropertyType(i);

                switch (propType)
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                    {
                        var obj = RequestBodyReader.GetObject(propsJson, propName);
                        if (obj != null)
                        {
                            var r = RequestBodyReader.GetFloat(obj, "r") ?? mat.GetColor(propName).r;
                            var g = RequestBodyReader.GetFloat(obj, "g") ?? mat.GetColor(propName).g;
                            var b = RequestBodyReader.GetFloat(obj, "b") ?? mat.GetColor(propName).b;
                            var a = RequestBodyReader.GetFloat(obj, "a") ?? mat.GetColor(propName).a;
                            mat.SetColor(propName, new Color(r, g, b, a));
                            updated.Add(propName);
                        }
                        break;
                    }
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                    {
                        var v = RequestBodyReader.GetFloat(propsJson, propName);
                        if (v.HasValue) { mat.SetFloat(propName, v.Value); updated.Add(propName); }
                        break;
                    }
                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    {
                        var obj = RequestBodyReader.GetObject(propsJson, propName);
                        if (obj != null)
                        {
                            var cur = mat.GetVector(propName);
                            var x = RequestBodyReader.GetFloat(obj, "x") ?? cur.x;
                            var y = RequestBodyReader.GetFloat(obj, "y") ?? cur.y;
                            var z = RequestBodyReader.GetFloat(obj, "z") ?? cur.z;
                            var w = RequestBodyReader.GetFloat(obj, "w") ?? cur.w;
                            mat.SetVector(propName, new Vector4(x, y, z, w));
                            updated.Add(propName);
                        }
                        break;
                    }
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    {
                        var texObj = RequestBodyReader.GetObject(propsJson, propName);
                        if (texObj != null)
                        {
                            var texGuid = RequestBodyReader.GetString(texObj, "guid");
                            if (!string.IsNullOrEmpty(texGuid))
                            {
                                var texPath = AssetDatabase.GUIDToAssetPath(texGuid);
                                var tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
                                if (tex != null) { mat.SetTexture(propName, tex); updated.Add(propName); }
                            }
                        }
                        break;
                    }
                }
            }

            return updated;
        }

    }
}

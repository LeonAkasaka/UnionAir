using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles texture importer settings:
    ///   PATCH /api/assets/texture-importer/{guid} — update import settings and reimport
    /// </summary>
    internal class TextureImporterHandler
    {
        public void HandleUpdate(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendNotFound(response, $"No asset found for GUID: {guid}");
                return;
            }

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                RestResponse.SendError(response, $"Asset is not a texture: {assetPath}", 400);
                return;
            }

            var body = RequestBodyReader.ReadString(request);
            var changed = false;

            var textureTypeStr = RequestBodyReader.GetString(body, "textureType");
            if (!string.IsNullOrEmpty(textureTypeStr))
            {
                TextureImporterType type;
                switch (textureTypeStr.ToLowerInvariant())
                {
                    case "sprite":       type = TextureImporterType.Sprite;       break;
                    case "default":      type = TextureImporterType.Default;      break;
                    case "normalmap":    type = TextureImporterType.NormalMap;    break;
                    case "gui":          type = TextureImporterType.GUI;          break;
                    case "cursor":       type = TextureImporterType.Cursor;       break;
                    case "cookie":       type = TextureImporterType.Cookie;       break;
                    case "lightmap":     type = TextureImporterType.Lightmap;     break;
                    case "singlechannel":type = TextureImporterType.SingleChannel;break;
                    default:
                        RestResponse.SendError(response,
                            $"Unknown textureType: {textureTypeStr}. Use Sprite, Default, NormalMap, GUI, Cursor, Cookie, Lightmap, or SingleChannel.", 400);
                        return;
                }
                importer.textureType = type;
                changed = true;
            }

            if (importer.textureType == TextureImporterType.Sprite)
            {
                var spriteModeStr = RequestBodyReader.GetString(body, "spriteMode");
                if (!string.IsNullOrEmpty(spriteModeStr))
                {
                    switch (spriteModeStr.ToLowerInvariant())
                    {
                        case "single":   importer.spriteImportMode = SpriteImportMode.Single;   break;
                        case "multiple": importer.spriteImportMode = SpriteImportMode.Multiple; break;
                        case "polygon":  importer.spriteImportMode = SpriteImportMode.Polygon;  break;
                    }
                    changed = true;
                }
                else if (changed)
                {
                    importer.spriteImportMode = SpriteImportMode.Single;
                }

                var ppu = RequestBodyReader.GetFloat(body, "pixelsPerUnit");
                if (ppu.HasValue) { importer.spritePixelsPerUnit = ppu.Value; changed = true; }
            }

            if (!changed)
            {
                RestResponse.SendError(response, "No recognized fields to update. Supported: textureType, spriteMode, pixelsPerUnit.", 400);
                return;
            }

            importer.SaveAndReimport();

            RestResponse.Send(response,
                $"{{\"guid\":\"{RestResponse.EscapeJson(guid)}\"," +
                $"\"assetPath\":\"{RestResponse.EscapeJson(assetPath)}\"," +
                $"\"textureType\":\"{importer.textureType}\"," +
                $"\"spriteMode\":\"{importer.spriteImportMode}\"," +
                $"\"pixelsPerUnit\":{RestResponse.FormatFloat(importer.spritePixelsPerUnit)}}}");
        }
    }
}

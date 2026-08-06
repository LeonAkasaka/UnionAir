using System.Collections.Generic;
using System.Text;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class AssetReimportHandler
    {
        public void Handle(UnionAirRequest request, UnionAirResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            if (!EditorTargetUtils.TryResolveAssetPath(
                    RequestBodyReader.GetString(body, "guid"),
                    RequestBodyReader.GetString(body, "assetPath"),
                    "asset",
                    true,
                    out var guid,
                    out var assetPath,
                    out var error,
                    out var statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }

            var recursive = RequestBodyReader.GetBool(body, "recursive") == true;
            var loadedScenes = LoadedSceneAssetSafety.FindLoadedSceneConflicts(
                assetPath,
                recursive);
            if (loadedScenes.Count > 0)
            {
                RestResponse.Send(
                    response,
                    AssetReimportSafety.BuildConflictJson(assetPath, loadedScenes),
                    409);
                return;
            }

            var options = ImportAssetOptions.Default;
            if (recursive)
                options |= ImportAssetOptions.ImportRecursive;
            if (RequestBodyReader.GetBool(body, "forceUpdate") == true)
                options |= ImportAssetOptions.ForceUpdate;

            AssetDatabase.ImportAsset(assetPath, options);
            guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
            {
                RestResponse.SendError(response, "Asset import did not register a GUID: " + assetPath, 422);
                return;
            }
            RestResponse.Send(response,
                "{\"reimported\":true,\"guid\":\"" + RestResponse.EscapeJson(guid) +
                "\",\"assetPath\":\"" + RestResponse.EscapeJson(assetPath) +
                "\",\"isCompiling\":" + (EditorApplication.isCompiling ? "true" : "false") +
                ",\"isUpdating\":" + (EditorApplication.isUpdating ? "true" : "false") + "}");
        }
    }

    internal static class AssetReimportSafety
    {
        private const string ErrorMessage =
            "Cannot reimport loaded scenes. Unload them before retrying to avoid Unity's interactive Reload dialog.";

        internal static string BuildConflictJson(
            string assetPath,
            IReadOnlyList<LoadedSceneAssetConflict> loadedScenes)
        {
            var sb = new StringBuilder();
            sb.Append("{\"error\":\"");
            sb.Append(RestResponse.EscapeJson(ErrorMessage));
            sb.Append("\",\"code\":\"loaded_scene_reimport_blocked\",\"assetPath\":\"");
            sb.Append(RestResponse.EscapeJson(assetPath));
            sb.Append("\",\"loadedScenes\":");
            LoadedSceneAssetSafety.AppendLoadedScenesJson(sb, loadedScenes);
            sb.Append("}");
            return sb.ToString();
        }
    }
}

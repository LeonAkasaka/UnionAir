using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class AssetOpenHandler
    {
        public void Handle(UnionAirRequest request, UnionAirResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            if (!EditorTargetUtils.TryResolveAssetPath(
                    RequestBodyReader.GetString(body, "guid"),
                    RequestBodyReader.GetString(body, "assetPath"),
                    "asset",
                    out var guid,
                    out var assetPath,
                    out var error,
                    out var statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }

            if (!AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath)))
            {
                RestResponse.SendError(response, "Asset could not be opened: " + assetPath, 422);
                return;
            }

            RestResponse.Send(response,
                "{\"opened\":true,\"guid\":\"" + RestResponse.EscapeJson(guid) +
                "\",\"assetPath\":\"" + RestResponse.EscapeJson(assetPath) + "\"}");
        }
    }
}

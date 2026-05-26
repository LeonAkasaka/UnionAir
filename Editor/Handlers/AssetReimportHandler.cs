using System.Net;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class AssetReimportHandler : IRequestHandler
    {
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "POST" && request.Url.AbsolutePath == "/api/assets/reimport";

        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
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

            var options = ImportAssetOptions.Default;
            if (RequestBodyReader.GetBool(body, "recursive") == true)
                options |= ImportAssetOptions.ImportRecursive;
            if (RequestBodyReader.GetBool(body, "forceUpdate") == true)
                options |= ImportAssetOptions.ForceUpdate;

            AssetDatabase.ImportAsset(assetPath, options);
            RestResponse.Send(response,
                "{\"reimported\":true,\"guid\":\"" + RestResponse.EscapeJson(guid) +
                "\",\"assetPath\":\"" + RestResponse.EscapeJson(assetPath) +
                "\",\"isCompiling\":" + (EditorApplication.isCompiling ? "true" : "false") +
                ",\"isUpdating\":" + (EditorApplication.isUpdating ? "true" : "false") + "}");
        }
    }
}

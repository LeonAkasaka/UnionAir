using System.Collections.Generic;
using System.Net;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Routes incoming HTTP requests to the appropriate <see cref="IRequestHandler"/>.
    /// </summary>
    internal class RestRouter
    {
        private readonly List<IRequestHandler> _handlers = new List<IRequestHandler>();

        public void Register(IRequestHandler handler)
        {
            _handlers.Add(handler);
        }

        public void Handle(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            // CORS preflight
            if (request.HttpMethod == "OPTIONS")
            {
                RestResponse.AddCorsHeaders(response);
                response.StatusCode = 204;
                return;
            }

            // Permission gate
            if (request.HttpMethod != "GET")
            {
                if (IsAssetMutation(request))
                {
                    if (!UnionAirSettings.AssetWriteEnabled)
                    {
                        RestResponse.SendError(response,
                            "Asset Write API is disabled. Enable it in Window > UnionAir > REST Bridge.", 403);
                        return;
                    }
                }
                else if (IsPlayMutation(request))
                {
                    if (!UnionAirSettings.PlayModeEnabled)
                    {
                        RestResponse.SendError(response,
                            "Play Mode API is disabled. Enable it in Window > UnionAir > REST Bridge.", 403);
                        return;
                    }
                }
                else
                {
                    if (!UnionAirSettings.WriteEnabled)
                    {
                        RestResponse.SendError(response,
                            "Write API is disabled. Enable it in Window > UnionAir > REST Bridge.", 403);
                        return;
                    }
                }
            }

            foreach (var handler in _handlers)
            {
                if (handler.CanHandle(request))
                {
                    handler.Handle(request, response);
                    return;
                }
            }

            RestResponse.SendNotFound(response,
                $"No handler for {request.HttpMethod} {request.Url.AbsolutePath}");
        }

        /// <summary>
        /// Returns true for requests that mutate assets on disk (require AssetWriteEnabled).
        /// All other mutating requests require WriteEnabled.
        /// </summary>
        private static bool IsAssetMutation(HttpListenerRequest request)
        {
            var path = request.Url.AbsolutePath;
            return path.StartsWith("/api/assets") || path == "/api/scene/save" || path == "/api/editor/refresh";
        }

        /// <summary>
        /// Returns true for requests that control play mode (require PlayModeEnabled).
        /// </summary>
        private static bool IsPlayMutation(HttpListenerRequest request)
        {
            var path = request.Url.AbsolutePath;
            return path == "/api/editor/play"  ||
                   path == "/api/editor/stop"  ||
                   path == "/api/editor/pause" ||
                   path == "/api/editor/step";
        }
    }
}

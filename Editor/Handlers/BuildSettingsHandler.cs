using System.Net;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class BuildSettingsHandler
    {
        /// <summary>
        /// Returns the build configuration for one named build target.
        /// </summary>
        public void HandleSettings(HttpListenerRequest request, HttpListenerResponse response)
        {
            var requested = request.QueryString["namedBuildTarget"] ?? "";
            if (!BuildTargetCatalog.TryResolveNamedBuildTarget(requested, out var namedBuildTarget, out var error))
            {
                RestResponse.SendError(response, error, 400);
                return;
            }

            RestResponse.Send(response, BuildSettingsReader.SettingsJson(namedBuildTarget));
        }

        /// <summary>
        /// Returns the build targets this Editor knows about and whether each module is installed.
        /// </summary>
        public void HandleTargets(HttpListenerRequest request, HttpListenerResponse response)
        {
            var installedRaw = request.QueryString["installed"];
            if (installedRaw != null &&
                installedRaw != "true" && installedRaw != "false" &&
                installedRaw != "1" && installedRaw != "0")
            {
                RestResponse.SendError(
                    response, "Query parameter 'installed' must be true or false.", 400);
                return;
            }

            var installedOnly = installedRaw == "true" || installedRaw == "1";
            RestResponse.Send(response, BuildSettingsReader.TargetsJson(installedOnly));
        }
    }
}

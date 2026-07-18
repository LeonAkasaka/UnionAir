using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;
using UnityEditor.PackageManager;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class HelpHandler
    {
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var packageInfo = PackageInfo.FindForAssembly(Assembly.GetExecutingAssembly());
            var packageName = packageInfo?.name ?? "com.leonakasaka.unionair";
            var displayName = packageInfo?.displayName ?? "UnionAir - Unity REST Bridge";
            var version = packageInfo?.version ?? "";
            var baseUrl = $"{request.Url.Scheme}://{request.Url.Authority}/api";

            var sb = new StringBuilder();
            sb.Append("{");
            AppendString(sb, "name", packageName);
            sb.Append(",");
            AppendString(sb, "displayName", displayName);
            sb.Append(",");
            AppendString(sb, "version", version);
            sb.Append(",");
            AppendString(sb, "baseUrl", baseUrl);
            sb.Append(",");
            AppendString(sb, "description", "UnionAir exposes Unity Editor state and selected Editor operations as a local REST API.");
            sb.Append(",");
            AppendCategories(sb, request);
            sb.Append(",");
            AppendEndpoints(sb, request);
            sb.Append("}");

            RestResponse.Send(response, sb.ToString());
        }

        private static void AppendCategories(StringBuilder sb, HttpListenerRequest request)
        {
            var includeDisabled = IsTrue(request.QueryString["includeDisabled"]);
            var source = (request.QueryString["source"] ?? "all").ToLowerInvariant();
            var categoryFilter = request.QueryString["category"] ?? "";

            sb.Append("\"categories\":[");
            var first = true;
            foreach (var category in UnionAirRouteRegistry.Categories)
            {
                if (!includeDisabled && !category.Enabled && category.Source == UnionAirRouteSource.Custom)
                    continue;
                if (source == "builtin" && category.Source != UnionAirRouteSource.Builtin)
                    continue;
                if (source == "custom" && category.Source != UnionAirRouteSource.Custom)
                    continue;
                if (!string.IsNullOrEmpty(categoryFilter) &&
                    !string.Equals(category.Id, categoryFilter, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!first) sb.Append(",");
                first = false;
                AppendCategory(sb, category);
            }
            sb.Append("]");
        }

        private static void AppendCategory(StringBuilder sb, UnionAirCategoryDefinition category)
        {
            sb.Append("{");
            AppendString(sb, "id", category.Id);
            sb.Append(",");
            AppendString(sb, "displayName", category.DisplayName);
            sb.Append(",");
            AppendString(sb, "source", SourceName(category.Source));
            sb.Append(",");
            sb.Append("\"enabled\":");
            sb.Append(category.Enabled ? "true" : "false");
            sb.Append(",");
            sb.Append("\"canDisable\":");
            sb.Append(category.CanDisable ? "true" : "false");
            sb.Append(",");
            sb.Append("\"enabledByDefault\":");
            sb.Append(category.EnabledByDefault ? "true" : "false");
            sb.Append(",");
            AppendRiskArray(sb, "risk", category.Risk);
            if (!string.IsNullOrEmpty(category.Error))
            {
                sb.Append(",");
                AppendString(sb, "error", category.Error);
            }
            sb.Append("}");
        }

        private static void AppendEndpoints(StringBuilder sb, HttpListenerRequest request)
        {
            var includeDisabled = IsTrue(request.QueryString["includeDisabled"]);
            var source = (request.QueryString["source"] ?? "all").ToLowerInvariant();
            var detail = (request.QueryString["detail"] ?? "").ToLowerInvariant() == "full";
            var categoryFilter = request.QueryString["category"] ?? "";
            var endpoints = UnionAirRouteRegistry.Descriptors;

            sb.Append("\"endpoints\":[");
            var first = true;
            foreach (var endpoint in endpoints)
            {
                if (!includeDisabled && !endpoint.Enabled && endpoint.Source == UnionAirRouteSource.Custom)
                    continue;
                if (source == "builtin" && endpoint.Source != UnionAirRouteSource.Builtin)
                    continue;
                if (source == "custom" && endpoint.Source != UnionAirRouteSource.Custom)
                    continue;
                if (!string.IsNullOrEmpty(categoryFilter) &&
                    !string.Equals(endpoint.Category, categoryFilter, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!first) sb.Append(",");
                first = false;
                AppendEndpoint(sb, endpoint, detail);
            }
            sb.Append("]");
        }

        private static bool IsTrue(string value)
        {
            return value == "true" || value == "1";
        }

        private static void AppendEndpoint(StringBuilder sb, UnionAirEndpointDescriptor endpoint, bool detail)
        {
            sb.Append("{");
            AppendString(sb, "method", endpoint.Method);
            sb.Append(",");
            AppendString(sb, "path", endpoint.Path);
            sb.Append(",");
            AppendString(sb, "routeTemplate", endpoint.RouteTemplate);
            sb.Append(",");
            AppendString(sb, "source", SourceName(endpoint.Source));
            sb.Append(",");
            sb.Append("\"enabled\":");
            sb.Append(endpoint.Enabled ? "true" : "false");
            sb.Append(",");
            AppendString(sb, "category", endpoint.Category);
            sb.Append(",");
            AppendString(sb, "summary", endpoint.Summary);
            sb.Append(",");
            AppendRiskArray(sb, "risk", endpoint.Risk);
            sb.Append(",");
            AppendString(sb, "playModePolicy", PlayModePolicyName(endpoint.PlayModePolicy));
            sb.Append(",");
            AppendString(sb, "testRunPolicy", TestRunPolicyName(endpoint.TestRunPolicy));
            sb.Append(",");
            AppendStringArray(sb, "pathParams", endpoint.PathParams);
            sb.Append(",");
            AppendStringArray(sb, "requiredQuery", endpoint.RequiredQuery);
            sb.Append(",");
            AppendStringArray(sb, "optionalQuery", endpoint.OptionalQuery);
            sb.Append(",");
            AppendStringArray(sb, "requiredBody", endpoint.RequiredBody);
            sb.Append(",");
            AppendStringArray(sb, "optionalBody", endpoint.OptionalBody);
            if (detail)
            {
                if (!string.IsNullOrEmpty(endpoint.RequestExample))
                {
                    sb.Append(",");
                    sb.Append("\"requestExample\":");
                    sb.Append(endpoint.RequestExample);
                }
                if (!string.IsNullOrEmpty(endpoint.ResponseExample))
                {
                    sb.Append(",");
                    sb.Append("\"responseExample\":");
                    sb.Append(endpoint.ResponseExample);
                }
            }
            if (!string.IsNullOrEmpty(endpoint.Error))
            {
                sb.Append(",");
                AppendString(sb, "error", endpoint.Error);
            }
            sb.Append("}");
        }

        private static string SourceName(UnionAirRouteSource source)
            => source == UnionAirRouteSource.Custom ? "custom" : "builtin";

        private static string PlayModePolicyName(UnionAirPlayModePolicy policy)
        {
            switch (policy)
            {
                case UnionAirPlayModePolicy.Blocked:
                    return "blocked";
                case UnionAirPlayModePolicy.ExplicitOptIn:
                    return "explicitOptIn";
                default:
                    return "allowed";
            }
        }

        private static string TestRunPolicyName(UnionAirTestRunPolicy policy)
            => policy == UnionAirTestRunPolicy.Allowed ? "allowed" : "blocked";

        private static void AppendRiskArray(StringBuilder sb, string key, UnionAirEndpointRisk risk)
        {
            sb.Append("\"");
            sb.Append(key);
            sb.Append("\":[");
            var first = true;
            if (risk == UnionAirEndpointRisk.None)
                AppendRiskName(sb, "readOnly", ref first);
            if ((risk & UnionAirEndpointRisk.SceneUpdate) != 0)
                AppendRiskName(sb, "sceneUpdate", ref first);
            if ((risk & UnionAirEndpointRisk.AssetUpdate) != 0)
                AppendRiskName(sb, "assetUpdate", ref first);
            if ((risk & UnionAirEndpointRisk.PlayMode) != 0)
                AppendRiskName(sb, "playMode", ref first);
            if ((risk & UnionAirEndpointRisk.Custom) != 0)
                AppendRiskName(sb, "custom", ref first);
            if ((risk & UnionAirEndpointRisk.RequestDependent) != 0)
                AppendRiskName(sb, "requestDependent", ref first);
            if ((risk & UnionAirEndpointRisk.EditorState) != 0)
                AppendRiskName(sb, "editorState", ref first);
            sb.Append("]");
        }

        private static void AppendRiskName(StringBuilder sb, string name, ref bool first)
        {
            if (!first) sb.Append(",");
            first = false;
            sb.Append("\"");
            sb.Append(name);
            sb.Append("\"");
        }

        private static void AppendString(StringBuilder sb, string key, string value)
        {
            sb.Append("\"");
            sb.Append(key);
            sb.Append("\":\"");
            sb.Append(RestResponse.EscapeJson(value));
            sb.Append("\"");
        }

        private static void AppendStringArray(StringBuilder sb, string key, IReadOnlyList<string> values)
        {
            sb.Append("\"");
            sb.Append(key);
            sb.Append("\":[");
            if (values != null)
            {
                for (var i = 0; i < values.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append("\"");
                    sb.Append(RestResponse.EscapeJson(values[i]));
                    sb.Append("\"");
                }
            }
            sb.Append("]");
        }
    }
}

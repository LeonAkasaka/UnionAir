using System;
using System.Collections.Generic;
using System.Reflection;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Runtime metadata for a route discovered from <see cref="UnionAirEndpointAttribute"/>.
    /// </summary>
    /// <remarks>
    /// Descriptors are used by the router, <c>GET /api/help</c>, and the UnionAir EditorWindow endpoint list.
    /// They are read-only for consumers; enabled state and errors are managed by the route registry.
    /// </remarks>
    public sealed class UnionAirEndpointDescriptor
    {
        internal UnionAirEndpointDescriptor(
            string method,
            string path,
            string routeTemplate,
            string controllerRoute,
            UnionAirRouteSource source,
            string category,
            UnionAirCategoryDefinition categoryDefinition,
            UnionAirPlayModePolicy playModePolicy,
            bool useRiskOverride,
            UnionAirEndpointRisk riskOverride,
            string summary,
            string[] pathParams,
            string[] requiredQuery,
            string[] optionalQuery,
            string[] requiredBody,
            string[] optionalBody,
            string requestExample,
            string responseExample,
            object target,
            MethodInfo handler,
            int declarationOrder)
        {
            Method = method;
            Path = path;
            RouteTemplate = routeTemplate;
            ControllerRoute = controllerRoute;
            Source = source;
            Category = category;
            CategoryDefinition = categoryDefinition;
            PlayModePolicy = playModePolicy;
            UseRiskOverride = useRiskOverride;
            RiskOverride = riskOverride;
            Summary = summary;
            PathParams = pathParams ?? new string[0];
            RequiredQuery = requiredQuery ?? new string[0];
            OptionalQuery = optionalQuery ?? new string[0];
            RequiredBody = requiredBody ?? new string[0];
            OptionalBody = optionalBody ?? new string[0];
            RequestExample = requestExample ?? "";
            ResponseExample = responseExample ?? "";
            Target = target;
            Handler = handler;
            DeclarationOrder = declarationOrder;
            Segments = CompileSegments(path);
            Key = source + ":" + method + ":" + path;
        }

        /// <summary>
        /// HTTP method handled by the endpoint.
        /// </summary>
        public string Method { get; }

        /// <summary>
        /// Absolute public API path, including the <c>/api</c> prefix.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Route template used by the attribute router.
        /// </summary>
        public string RouteTemplate { get; }

        /// <summary>
        /// Normalized controller route segment without the <c>/api</c> or <c>/api/custom</c> prefix.
        /// </summary>
        public string ControllerRoute { get; }

        /// <summary>
        /// Indicates whether this endpoint is built into UnionAir or provided by a custom assembly.
        /// </summary>
        public UnionAirRouteSource Source { get; }

        /// <summary>
        /// Category identifier associated with this endpoint.
        /// </summary>
        public string Category { get; }

        /// <summary>
        /// Resolved category metadata for this endpoint.
        /// </summary>
        public UnionAirCategoryDefinition CategoryDefinition { get; }

        /// <summary>
        /// Risk metadata inherited from <see cref="CategoryDefinition"/>.
        /// </summary>
        public UnionAirEndpointRisk Risk => UseRiskOverride
            ? RiskOverride
            : CategoryDefinition?.Risk ?? UnionAirEndpointRisk.Custom;

        /// <summary>
        /// Whether this endpoint reports risk metadata that differs from its category.
        /// </summary>
        public bool UseRiskOverride { get; }

        /// <summary>
        /// Endpoint-specific risk metadata used when <see cref="UseRiskOverride"/> is true.
        /// </summary>
        public UnionAirEndpointRisk RiskOverride { get; }

        /// <summary>
        /// Whether this endpoint may be called while the Unity Editor is in Play mode.
        /// </summary>
        public UnionAirPlayModePolicy PlayModePolicy { get; }

        /// <summary>
        /// Short help text for the endpoint.
        /// </summary>
        public string Summary { get; }

        /// <summary>
        /// Stable descriptor key used for diagnostics and display.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Route template parameter names required by this endpoint.
        /// </summary>
        public string[] PathParams { get; }

        /// <summary>
        /// Required query string parameter names.
        /// </summary>
        public string[] RequiredQuery { get; }

        /// <summary>
        /// Optional query string parameter names.
        /// </summary>
        public string[] OptionalQuery { get; }

        /// <summary>
        /// Required JSON body field names.
        /// </summary>
        public string[] RequiredBody { get; }

        /// <summary>
        /// Optional JSON body field names.
        /// </summary>
        public string[] OptionalBody { get; }

        /// <summary>
        /// Example JSON request body included in <c>GET /api/help?detail=full</c> output.
        /// </summary>
        public string RequestExample { get; }

        /// <summary>
        /// Example JSON response body included in <c>GET /api/help?detail=full</c> output.
        /// </summary>
        public string ResponseExample { get; }

        /// <summary>
        /// Current enabled state after applying category settings and route conflict checks.
        /// </summary>
        public bool Enabled { get; internal set; } = true;

        /// <summary>
        /// Route discovery or conflict message associated with this endpoint, if any.
        /// </summary>
        public string Error { get; internal set; } = "";
        internal object Target { get; }
        internal MethodInfo Handler { get; }
        internal int DeclarationOrder { get; }
        internal RouteSegment[] Segments { get; }

        internal int LiteralSegmentCount
        {
            get
            {
                var count = 0;
                foreach (var segment in Segments)
                    if (!segment.IsParameter) count++;
                return count;
            }
        }

        internal bool TryMatch(string absolutePath, Dictionary<string, string> routeValues)
        {
            var requestSegments = SplitPath(absolutePath);
            if (requestSegments.Length != Segments.Length)
                return false;

            for (var i = 0; i < Segments.Length; i++)
            {
                var segment = Segments[i];
                var requestSegment = Uri.UnescapeDataString(requestSegments[i]);
                if (segment.IsParameter)
                {
                    routeValues[segment.Value] = requestSegment;
                }
                else if (!string.Equals(segment.Value, requestSegment, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static RouteSegment[] CompileSegments(string path)
        {
            var parts = SplitPath(path);
            var result = new RouteSegment[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var isParameter = part.Length > 2 && part[0] == '{' && part[part.Length - 1] == '}';
                result[i] = new RouteSegment(isParameter ? part.Substring(1, part.Length - 2) : part, isParameter);
            }
            return result;
        }

        private static string[] SplitPath(string path)
        {
            return path.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }

    internal readonly struct RouteSegment
    {
        public RouteSegment(string value, bool isParameter)
        {
            Value = value;
            IsParameter = isParameter;
        }

        /// <summary>
        /// Literal segment text or route parameter name.
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// Whether this segment captures a route parameter.
        /// </summary>
        public bool IsParameter { get; }
    }
}

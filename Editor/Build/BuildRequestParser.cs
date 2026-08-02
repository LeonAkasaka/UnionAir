namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>Build options resolved for one request.</summary>
    internal struct BuildRequestOptions
    {
        internal bool development;
        internal bool allowDebugging;
        internal bool connectProfiler;
        internal bool deepProfiling;
        internal bool waitForPlayerConnection;
        internal bool clean;
        internal bool strictMode;
    }

    /// <summary>
    /// Parses and validates the allowlisted build options a request may set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only these options are accepted. The output location is never taken from the request, and
    /// neither is the build target — switching targets is a lifecycle operation of its own, not a
    /// parameter of a build.
    /// </para>
    /// <para>
    /// Omitted options fall back to what the project's Build Settings window currently has
    /// selected, so a build requested with an empty body is the build a person would get by
    /// pressing Build. An override applies to that one build and is not written back to the
    /// project.
    /// </para>
    /// </remarks>
    internal static class BuildRequestParser
    {
        /// <summary>
        /// Resolves the options for a request.
        /// </summary>
        /// <param name="body">Raw JSON request body; may be empty.</param>
        /// <param name="defaults">Project defaults to fall back to per field.</param>
        /// <param name="options">Resolved options.</param>
        /// <param name="error">Message describing why the request was rejected.</param>
        /// <returns><c>false</c> when the request must be answered with <c>400</c>.</returns>
        internal static bool TryParse(
            string body,
            BuildRequestOptions defaults,
            out BuildRequestOptions options,
            out string error)
        {
            error = null;
            options = defaults;

            if (!Read(body, "development", defaults.development, ref options.development, out error) ||
                !Read(body, "allowDebugging", defaults.allowDebugging, ref options.allowDebugging, out error) ||
                !Read(body, "connectProfiler", defaults.connectProfiler, ref options.connectProfiler, out error) ||
                !Read(body, "deepProfiling", defaults.deepProfiling, ref options.deepProfiling, out error) ||
                !Read(body, "waitForPlayerConnection", defaults.waitForPlayerConnection,
                    ref options.waitForPlayerConnection, out error) ||
                !Read(body, "clean", false, ref options.clean, out error) ||
                !Read(body, "strictMode", false, ref options.strictMode, out error))
                return false;

            // Unity's own Build Settings window disables all four unless Development Build is
            // checked, and BuildPipeline silently drops them otherwise. Rejecting is better than
            // producing a build that quietly is not what was asked for.
            if (!options.development &&
                (options.allowDebugging || options.connectProfiler ||
                 options.deepProfiling || options.waitForPlayerConnection))
            {
                error = "Body fields 'allowDebugging', 'connectProfiler', 'deepProfiling', and " +
                        "'waitForPlayerConnection' require 'development' to be true.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Reads an optional boolean, rejecting a field that is present but is not one.
        /// </summary>
        /// <remarks>
        /// <c>GetBool</c> returns <c>null</c> both for an absent field and for a value that is not
        /// a JSON boolean, so treating <c>null</c> as "absent" would let <c>"development": "true"</c>
        /// fall through to the project default and produce a build the caller did not ask for,
        /// with nothing in the response saying so.
        /// </remarks>
        private static bool Read(string body, string field, bool fallback, ref bool value, out string error)
        {
            error = null;
            var parsed = RequestBodyReader.GetBool(body, field);
            if (parsed.HasValue)
            {
                value = parsed.Value;
                return true;
            }

            if (RequestBodyReader.HasTopLevelField(body, field))
            {
                error = $"Body field '{field}' must be a JSON boolean.";
                return false;
            }

            value = fallback;
            return true;
        }
    }
}

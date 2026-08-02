namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Runtime metadata for an API category discovered by the UnionAir route registry.
    /// </summary>
    /// <remarks>
    /// Category definitions are the source of truth for category enablement, display grouping, and risk
    /// metadata exposed through <c>GET /api/help</c>.
    /// </remarks>
    public sealed class UnionAirCategoryDefinition
    {
        internal UnionAirCategoryDefinition(
            string id,
            string displayName,
            UnionAirRouteSource source,
            UnionAirEndpointRisk risk,
            bool canDisable,
            bool enabledByDefault,
            UnionAirActivity blockedDuring = UnionAirActivity.None)
        {
            Id = id;
            DisplayName = string.IsNullOrEmpty(displayName) ? id : displayName;
            Source = source;
            Risk = risk;
            CanDisable = canDisable;
            EnabledByDefault = enabledByDefault;
            BlockedDuring = blockedDuring;
            Key = source + ":" + id;
        }

        /// <summary>
        /// Stable category identifier referenced by endpoints.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Human-readable category label.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Indicates whether this category is built into UnionAir or provided by a custom assembly.
        /// </summary>
        public UnionAirRouteSource Source { get; }

        /// <summary>
        /// Side-effect risk metadata for endpoints in this category.
        /// </summary>
        public UnionAirEndpointRisk Risk { get; }

        /// <summary>
        /// Whether users can enable or disable this category in the EditorWindow.
        /// </summary>
        public bool CanDisable { get; }

        /// <summary>
        /// Whether this category is enabled before user overrides are applied.
        /// </summary>
        public bool EnabledByDefault { get; }

        /// <summary>
        /// Editor activities during which endpoints in this category are rejected by default.
        /// </summary>
        public UnionAirActivity BlockedDuring { get; }

        /// <summary>
        /// Stable persistence key used for storing category enablement overrides.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Current enabled state after applying defaults, user overrides, and custom handler settings.
        /// </summary>
        public bool Enabled { get; internal set; }

        /// <summary>
        /// Discovery or conflict message associated with this category, if any.
        /// </summary>
        public string Error { get; internal set; } = "";
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;

namespace LeonAkasaka.UnionAir.Editor
{
    internal static class UnionAirRouteRegistry
    {
        private static List<UnionAirEndpointDescriptor> _descriptors;
        private static List<UnionAirCategoryDefinition> _categories;

        /// <summary>
        /// Gets the currently discovered endpoint descriptors.
        /// </summary>
        public static IReadOnlyList<UnionAirEndpointDescriptor> Descriptors
        {
            get
            {
                if (_descriptors == null)
                    Refresh();
                return _descriptors;
            }
        }

        /// <summary>
        /// Gets the currently discovered category definitions.
        /// </summary>
        public static IReadOnlyList<UnionAirCategoryDefinition> Categories
        {
            get
            {
                if (_categories == null)
                    Refresh();
                return _categories;
            }
        }

        /// <summary>
        /// Rebuilds the route and category descriptor tables from loaded Editor assemblies.
        /// </summary>
        public static void Refresh()
        {
            var descriptors = new List<UnionAirEndpointDescriptor>();
            var categories = CreateBuiltInCategories();
            var order = 0;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract)
                        continue;

                    var controller = (UnionAirControllerAttribute)Attribute.GetCustomAttribute(
                        type, typeof(UnionAirControllerAttribute), false);

                    if (controller == null)
                        continue;

                    var source = IsBuiltinAssembly(type.Assembly)
                        ? UnionAirRouteSource.Builtin
                        : UnionAirRouteSource.Custom;
                    var controllerRoute = controller.Route;

                    AddCategoryAttributes(categories, type.Assembly.GetCustomAttributes(typeof(UnionAirCategoryAttribute), false), source);
                    AddCategoryAttributes(categories, type.GetCustomAttributes(typeof(UnionAirCategoryAttribute), false), source);

                    object target;
                    try { target = Activator.CreateInstance(type); }
                    catch { continue; }

                    var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    foreach (var method in methods)
                    {
                        var endpoint = (UnionAirEndpointAttribute)Attribute.GetCustomAttribute(
                            method, typeof(UnionAirEndpointAttribute), false);
                        if (endpoint == null)
                            continue;

                        var parameters = method.GetParameters();
                        if (parameters.Length != 1 || parameters[0].ParameterType != typeof(UnionAirRequestContext))
                            continue;

                        descriptors.Add(CreateDescriptor(
                            endpoint, controllerRoute, source, target, method, categories, order++));
                    }
                }
            }

            ApplyCategoryState(categories);
            ApplyRouteStateAndCollisions(descriptors);
            descriptors.Sort(CompareDescriptors);
            categories.Sort(CompareCategories);
            _descriptors = descriptors;
            _categories = categories;
        }

        private static UnionAirEndpointDescriptor CreateDescriptor(
            UnionAirEndpointAttribute endpoint,
            string controllerRoute,
            UnionAirRouteSource source,
            object target,
            MethodInfo method,
            List<UnionAirCategoryDefinition> categories,
            int order)
        {
            var controller = NormalizeRoute(controllerRoute);
            var action = NormalizeRoute(endpoint.Route);
            var prefix = source == UnionAirRouteSource.Custom ? "custom" : "";
            var routeTemplate = CombineRoutes("/api", prefix, controller, action);
            var category = string.IsNullOrEmpty(endpoint.Category)
                ? UnionAirEndpointCategories.Read
                : endpoint.Category;
            var categoryDefinition = FindOrCreateCategory(categories, source, category);

            return new UnionAirEndpointDescriptor(
                endpoint.Method.ToUpperInvariant(),
                routeTemplate,
                routeTemplate,
                controller,
                source,
                category,
                categoryDefinition,
                endpoint.PlayModePolicy,
                endpoint.UseRiskOverride,
                endpoint.Risk,
                endpoint.Summary,
                endpoint.PathParams,
                endpoint.RequiredQuery,
                endpoint.OptionalQuery,
                endpoint.RequiredBody,
                endpoint.OptionalBody,
                endpoint.RequestExample,
                endpoint.ResponseExample,
                target,
                method,
                order);
        }

        private static void ApplyCategoryState(List<UnionAirCategoryDefinition> categories)
        {
            foreach (var category in categories)
            {
                category.Enabled = category.Source == UnionAirRouteSource.Custom && !UnionAirSettings.CustomHandlersEnabled
                    ? false
                    : !category.CanDisable ||
                      UnionAirSettings.IsCategoryEnabled(category.Key, category.EnabledByDefault);
            }
        }

        private static void ApplyRouteStateAndCollisions(List<UnionAirEndpointDescriptor> descriptors)
        {
            var seenCustom = new Dictionary<string, UnionAirEndpointDescriptor>();
            var builtins = new HashSet<string>();

            foreach (var descriptor in descriptors)
            {
                var key = descriptor.Method + " " + descriptor.Path;
                if (descriptor.Source == UnionAirRouteSource.Builtin)
                    builtins.Add(key);
            }

            foreach (var descriptor in descriptors)
            {
                if (descriptor.Source != UnionAirRouteSource.Custom)
                {
                    descriptor.Enabled = descriptor.CategoryDefinition == null || descriptor.CategoryDefinition.Enabled;
                    continue;
                }

                var key = descriptor.Method + " " + descriptor.Path;
                descriptor.Enabled = descriptor.CategoryDefinition != null && descriptor.CategoryDefinition.Enabled;

                if (builtins.Contains(key))
                {
                    descriptor.Enabled = false;
                    descriptor.Error = "Route collides with a built-in endpoint.";
                }
                else if (seenCustom.TryGetValue(key, out var previous))
                {
                    descriptor.Enabled = false;
                    previous.Enabled = false;
                    descriptor.Error = "Route collides with another custom endpoint.";
                    previous.Error = descriptor.Error;
                }
                else
                {
                    seenCustom[key] = descriptor;
                }
            }
        }

        private static List<UnionAirCategoryDefinition> CreateBuiltInCategories()
        {
            return new List<UnionAirCategoryDefinition>
            {
                new UnionAirCategoryDefinition(
                    UnionAirEndpointCategories.Read,
                    "Read",
                    UnionAirRouteSource.Builtin,
                    UnionAirEndpointRisk.None,
                    false,
                    true),
                new UnionAirCategoryDefinition(
                    UnionAirEndpointCategories.SceneWrite,
                    "Scene Write",
                    UnionAirRouteSource.Builtin,
                    UnionAirEndpointRisk.SceneUpdate,
                    true,
                    false),
                new UnionAirCategoryDefinition(
                    UnionAirEndpointCategories.AssetWrite,
                    "Asset Write",
                    UnionAirRouteSource.Builtin,
                    UnionAirEndpointRisk.AssetUpdate,
                    true,
                    false),
                new UnionAirCategoryDefinition(
                    UnionAirEndpointCategories.PlayMode,
                    "Play Mode",
                    UnionAirRouteSource.Builtin,
                    UnionAirEndpointRisk.PlayMode,
                    true,
                    false),
                new UnionAirCategoryDefinition(
                    UnionAirEndpointCategories.EditorActions,
                    "Editor Actions",
                    UnionAirRouteSource.Builtin,
                    UnionAirEndpointRisk.EditorState | UnionAirEndpointRisk.RequestDependent,
                    true,
                    false)
            };
        }

        private static void AddCategoryAttributes(
            List<UnionAirCategoryDefinition> categories,
            object[] attributes,
            UnionAirRouteSource source)
        {
            foreach (UnionAirCategoryAttribute attribute in attributes)
            {
                if (string.IsNullOrEmpty(attribute.Id))
                    continue;

                var existing = FindCategory(categories, source, attribute.Id);
                if (existing != null)
                    continue;

                categories.Add(new UnionAirCategoryDefinition(
                    attribute.Id,
                    attribute.DisplayName,
                    source,
                    attribute.Risk,
                    source == UnionAirRouteSource.Custom && attribute.CanDisable,
                    source == UnionAirRouteSource.Builtin || attribute.EnabledByDefault));
            }
        }

        private static UnionAirCategoryDefinition FindOrCreateCategory(
            List<UnionAirCategoryDefinition> categories,
            UnionAirRouteSource source,
            string id)
        {
            var category = FindCategory(categories, source, id);
            if (category != null)
                return category;

            category = new UnionAirCategoryDefinition(
                id,
                id,
                source,
                source == UnionAirRouteSource.Custom ? UnionAirEndpointRisk.Custom : UnionAirEndpointRisk.None,
                source == UnionAirRouteSource.Custom,
                source == UnionAirRouteSource.Builtin);
            categories.Add(category);
            if (source == UnionAirRouteSource.Custom)
                category.Error = "Category is not explicitly defined.";
            return category;
        }

        private static UnionAirCategoryDefinition FindCategory(
            List<UnionAirCategoryDefinition> categories,
            UnionAirRouteSource source,
            string id)
        {
            foreach (var category in categories)
            {
                if (category.Source == source && category.Id == id)
                    return category;
            }
            return null;
        }

        private static int CompareCategories(UnionAirCategoryDefinition left, UnionAirCategoryDefinition right)
        {
            var source = left.Source.CompareTo(right.Source);
            if (source != 0) return source;
            return string.Compare(left.Id, right.Id, StringComparison.Ordinal);
        }

        private static int CompareDescriptors(UnionAirEndpointDescriptor left, UnionAirEndpointDescriptor right)
        {
            var source = left.Source.CompareTo(right.Source);
            if (source != 0) return source;

            var literal = right.LiteralSegmentCount.CompareTo(left.LiteralSegmentCount);
            if (literal != 0) return literal;

            var length = right.Segments.Length.CompareTo(left.Segments.Length);
            if (length != 0) return length;

            return left.DeclarationOrder.CompareTo(right.DeclarationOrder);
        }

        private static bool IsBuiltinAssembly(System.Reflection.Assembly assembly)
        {
            return assembly == typeof(RestRouter).Assembly ||
                   Attribute.IsDefined(assembly, typeof(UnionAirBuiltinAssemblyAttribute));
        }

        private static string NormalizeRoute(string route)
        {
            return (route ?? "").Trim('/');
        }

        private static string CombineRoutes(params string[] routes)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var route in routes)
            {
                var normalized = NormalizeRoute(route);
                if (string.IsNullOrEmpty(normalized)) continue;
                sb.Append('/');
                sb.Append(normalized);
            }
            return sb.Length == 0 ? "/" : sb.ToString();
        }
    }
}

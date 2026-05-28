using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class EditorMenuItemsHandler : IRequestHandler
    {
        private const int DefaultLimit = 1000;
        private const int MaxLimit = 5000;

        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "GET" && request.Url.AbsolutePath == "/api/editor/menu-items";

        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var root = NormalizePath(request.QueryString["root"]);
            var search = request.QueryString["search"] ?? "";
            var includeFolders = !IsFalse(request.QueryString["includeFolders"]);
            var includeAttributeFallback = !IsFalse(request.QueryString["includeAttributeFallback"]);
            var limit = ClampLimit(request.QueryString["limit"]);
            var collectionLimit = string.IsNullOrEmpty(search) ? limit : MaxLimit;
            var warnings = new List<string>();

            var items = new Dictionary<string, MenuItemInfo>(StringComparer.Ordinal);
            var mode = "unsupportedApi";
            var isComplete = TryCollectUnsupportedMenuItems(root, includeFolders, collectionLimit, items);
            if (!isComplete)
            {
                mode = "menuItemAttributes";
                warnings.Add("UnityEditor.Unsupported.GetSubmenus was not available; built-in Unity menu items may be incomplete.");
            }

            if ((!isComplete || includeAttributeFallback) && items.Count < collectionLimit)
                CollectAttributeMenuItems(root, collectionLimit, items);

            var filtered = FilterItems(items.Values, search, limit);
            var sb = new StringBuilder();
            sb.Append("{");
            AppendString(sb, "enumerationMode", mode);
            sb.Append(",");
            sb.Append("\"isComplete\":");
            sb.Append(isComplete ? "true" : "false");
            sb.Append(",");
            AppendString(sb, "root", root);
            sb.Append(",");
            sb.Append("\"count\":");
            sb.Append(filtered.Count);
            sb.Append(",\"items\":[");
            for (var i = 0; i < filtered.Count; i++)
            {
                if (i > 0) sb.Append(",");
                AppendItem(sb, filtered[i]);
            }
            sb.Append("],\"warnings\":[");
            for (var i = 0; i < warnings.Count; i++)
            {
                if (i > 0) sb.Append(",");
                AppendStringValue(sb, warnings[i]);
            }
            sb.Append("]}");

            RestResponse.Send(response, sb.ToString());
        }

        private static bool TryCollectUnsupportedMenuItems(
            string root,
            bool includeFolders,
            int limit,
            Dictionary<string, MenuItemInfo> items)
        {
            var unsupportedType = typeof(EditorApplication).Assembly.GetType("UnityEditor.Unsupported");
            var getSubmenus = unsupportedType?.GetMethod(
                "GetSubmenus",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string) },
                null);
            if (getSubmenus == null || getSubmenus.ReturnType != typeof(string[]))
                return false;

            var roots = string.IsNullOrEmpty(root) ? DefaultRoots() : new[] { root };
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (var menuRoot in roots)
                WalkMenu(menuRoot, getSubmenus, includeFolders, limit, visited, items);
            return true;
        }

        private static void WalkMenu(
            string path,
            MethodInfo getSubmenus,
            bool includeFolders,
            int limit,
            HashSet<string> visited,
            Dictionary<string, MenuItemInfo> items)
        {
            path = NormalizePath(path);
            if (string.IsNullOrEmpty(path) || !visited.Add(path) || items.Count >= limit)
                return;

            string[] children;
            try
            {
                children = getSubmenus.Invoke(null, new object[] { path }) as string[];
            }
            catch
            {
                return;
            }

            var isFolder = children != null && children.Length > 0;
            if (includeFolders || !isFolder)
                AddItem(items, path, isFolder, "unityMenu");

            if (children == null)
                return;

            foreach (var child in children)
            {
                if (items.Count >= limit)
                    return;

                var childPath = NormalizePath(child);
                if (string.IsNullOrEmpty(childPath))
                    continue;

                if (!IsUnderRoot(childPath, path))
                    childPath = path + "/" + childPath;

                WalkMenu(childPath, getSubmenus, includeFolders, limit, visited, items);
            }
        }

        private static void CollectAttributeMenuItems(
            string root,
            int limit,
            Dictionary<string, MenuItemInfo> items)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }

                foreach (var type in types)
                {
                    if (type == null)
                        continue;

                    var methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    foreach (var method in methods)
                    {
                        var attributes = method.GetCustomAttributes(typeof(MenuItem), false);
                        foreach (var attribute in attributes)
                        {
                            if (items.Count >= limit)
                                return;
                            if (IsValidationMenuItem(attribute))
                                continue;

                            var path = NormalizePath(GetMenuItemPath(attribute));
                            if (string.IsNullOrEmpty(path) || !MatchesRoot(path, root))
                                continue;

                            AddItem(items, path, false, "menuItemAttribute");
                        }
                    }
                }
            }
        }

        private static List<MenuItemInfo> FilterItems(
            IEnumerable<MenuItemInfo> source,
            string search,
            int limit)
        {
            var result = new List<MenuItemInfo>();
            foreach (var item in source)
            {
                if (!string.IsNullOrEmpty(search) &&
                    item.Path.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                result.Add(item);
            }

            result.Sort((left, right) => string.Compare(left.Path, right.Path, StringComparison.Ordinal));
            if (result.Count > limit)
                result.RemoveRange(limit, result.Count - limit);
            return result;
        }

        private static void AddItem(
            Dictionary<string, MenuItemInfo> items,
            string path,
            bool isFolder,
            string source)
        {
            path = NormalizePath(path);
            if (string.IsNullOrEmpty(path) || items.ContainsKey(path))
                return;

            var split = path.LastIndexOf('/');
            var name = split >= 0 ? path.Substring(split + 1) : path;
            var parent = split >= 0 ? path.Substring(0, split) : "";
            var depth = path.Split('/').Length - 1;
            items[path] = new MenuItemInfo(path, name, parent, depth, isFolder, source);
        }

        private static string[] DefaultRoots()
        {
            return new[]
            {
                "File",
                "Edit",
                "Assets",
                "GameObject",
                "Component",
                "Window",
                "Tools",
                "Help"
            };
        }

        private static bool MatchesRoot(string path, string root)
        {
            if (string.IsNullOrEmpty(root))
                return true;
            return path == root || path.StartsWith(root + "/", StringComparison.Ordinal);
        }

        private static bool IsUnderRoot(string path, string root)
        {
            return path == root || path.StartsWith(root + "/", StringComparison.Ordinal);
        }

        private static string NormalizePath(string path)
        {
            return (path ?? "").Trim().Trim('/');
        }

        private static bool IsFalse(string value)
        {
            return value == "false" || value == "0";
        }

        private static string GetMenuItemPath(object attribute)
        {
            return GetAttributeString(attribute, "menuItem") ??
                   GetAttributeString(attribute, "itemName") ??
                   "";
        }

        private static bool IsValidationMenuItem(object attribute)
        {
            return GetAttributeBool(attribute, "validate") ||
                   GetAttributeBool(attribute, "isValidateFunction");
        }

        private static string GetAttributeString(object attribute, string name)
        {
            var type = attribute.GetType();
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
                return field.GetValue(attribute) as string;

            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetValue(attribute, null) as string;
        }

        private static bool GetAttributeBool(object attribute, string name)
        {
            var type = attribute.GetType();
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(bool))
                return (bool)field.GetValue(attribute);

            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.PropertyType == typeof(bool))
                return (bool)property.GetValue(attribute, null);

            return false;
        }

        private static int ClampLimit(string value)
        {
            if (!int.TryParse(value, out var limit))
                return DefaultLimit;
            if (limit < 1)
                return 1;
            return limit > MaxLimit ? MaxLimit : limit;
        }

        private static void AppendItem(StringBuilder sb, MenuItemInfo item)
        {
            sb.Append("{");
            AppendString(sb, "path", item.Path);
            sb.Append(",");
            AppendString(sb, "name", item.Name);
            sb.Append(",");
            AppendString(sb, "parent", item.Parent);
            sb.Append(",");
            sb.Append("\"depth\":");
            sb.Append(item.Depth);
            sb.Append(",\"isFolder\":");
            sb.Append(item.IsFolder ? "true" : "false");
            sb.Append(",");
            AppendString(sb, "source", item.Source);
            sb.Append("}");
        }

        private static void AppendString(StringBuilder sb, string key, string value)
        {
            sb.Append("\"");
            sb.Append(key);
            sb.Append("\":");
            AppendStringValue(sb, value);
        }

        private static void AppendStringValue(StringBuilder sb, string value)
        {
            sb.Append("\"");
            sb.Append(RestResponse.EscapeJson(value));
            sb.Append("\"");
        }

        private readonly struct MenuItemInfo
        {
            public MenuItemInfo(string path, string name, string parent, int depth, bool isFolder, string source)
            {
                Path = path;
                Name = name;
                Parent = parent;
                Depth = depth;
                IsFolder = isFolder;
                Source = source;
            }

            public string Path { get; }
            public string Name { get; }
            public string Parent { get; }
            public int Depth { get; }
            public bool IsFolder { get; }
            public string Source { get; }
        }
    }
}

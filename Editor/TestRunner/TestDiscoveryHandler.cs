using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;

namespace LeonAkasaka.UnionAir.Editor
{
    internal static class TestDiscoveryHandler
    {
        private static UnionAirResponse _pendingResponse;

        internal static bool IsPending => _pendingResponse != null;

        internal static void Initialize()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= AbortPending;
            AssemblyReloadEvents.beforeAssemblyReload += AbortPending;
        }

        internal static void Handle(UnionAirRequestContext ctx)
        {
            TestMode mode;
            string modeName;
            if (!TestRunnerService.TryParseMode(ctx.Request.QueryString["mode"], out mode, out modeName))
            {
                RestResponse.SendError(ctx.Response, "Query parameter 'mode' must be 'editMode' or 'playMode'.", 400);
                return;
            }

            int offset;
            int limit;
            if (!TryParseRange(ctx.Request.QueryString["offset"], 0, 0, int.MaxValue, out offset) ||
                !TryParseRange(ctx.Request.QueryString["limit"], 100, 1, 1000, out limit))
            {
                RestResponse.SendError(ctx.Response, "'offset' must be non-negative and 'limit' must be between 1 and 1000.", 400);
                return;
            }

            if (_pendingResponse != null)
            {
                RestResponse.SendError(ctx.Response, "Another test discovery request is already running.", 409);
                return;
            }

            var search = ctx.Request.QueryString["search"] ?? "";
            var assembly = ctx.Request.QueryString["assembly"] ?? "";
            var category = ctx.Request.QueryString["category"] ?? "";
            ctx.Defer();
            _pendingResponse = ctx.Response;

            try
            {
                TestRunnerApiProvider.Instance.RetrieveTestList(mode, root =>
                    Complete(root, modeName, search, assembly, category, offset, limit));
            }
            catch (Exception ex)
            {
                CompleteError("Test discovery could not be started: " + ex.Message, 500);
            }
        }

        private static void Complete(
            ITestAdaptor root,
            string mode,
            string search,
            string assembly,
            string category,
            int offset,
            int limit)
        {
            if (_pendingResponse == null)
                return;

            try
            {
                var matches = new List<TestItem>();
                Collect(root, "", search, assembly, category, matches);
                var count = Math.Min(limit, Math.Max(0, matches.Count - offset));
                var sb = new StringBuilder();
                sb.Append("{");
                AppendString(sb, "mode", mode);
                sb.Append($",\"total\":{matches.Count},\"offset\":{offset},\"limit\":{limit},\"tests\":[");
                for (var i = 0; i < count; i++)
                {
                    if (i > 0) sb.Append(",");
                    AppendTest(sb, matches[offset + i]);
                }
                sb.Append("]}");
                RestResponse.Send(_pendingResponse, sb.ToString());
            }
            catch (Exception ex)
            {
                RestResponse.SendError(_pendingResponse, "Test discovery failed: " + ex.Message, 500);
            }
            finally
            {
                ClosePending();
            }
        }

        private static void Collect(
            ITestAdaptor test,
            string assemblyName,
            string search,
            string assembly,
            string category,
            List<TestItem> result)
        {
            if (test == null)
                return;
            if (test.IsTestAssembly)
                assemblyName = test.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    ? test.Name.Substring(0, test.Name.Length - 4)
                    : test.Name;

            if (test.HasChildren)
            {
                foreach (var child in test.Children)
                    Collect(child, assemblyName, search, assembly, category, result);
                return;
            }

            if (!Contains(test.Name, search) && !Contains(test.FullName, search) && !Contains(test.UniqueName, search))
                return;
            if (!string.IsNullOrEmpty(assembly) && !string.Equals(assemblyName, assembly, StringComparison.OrdinalIgnoreCase))
                return;
            if (!string.IsNullOrEmpty(category) && !ContainsCategory(test.Categories, category))
                return;

            result.Add(new TestItem
            {
                test = test,
                assembly = assemblyName
            });
        }

        private static bool Contains(string value, string search)
            => string.IsNullOrEmpty(search) ||
               (!string.IsNullOrEmpty(value) && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);

        private static bool ContainsCategory(string[] categories, string category)
        {
            if (categories == null) return false;
            foreach (var value in categories)
                if (string.Equals(value, category, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool TryParseRange(string value, int defaultValue, int min, int max, out int result)
        {
            if (string.IsNullOrEmpty(value))
            {
                result = defaultValue;
                return true;
            }
            return int.TryParse(value, out result) && result >= min && result <= max;
        }

        private static void AppendTest(StringBuilder sb, TestItem item)
        {
            var test = item.test;
            sb.Append("{");
            AppendString(sb, "name", test.Name);
            sb.Append(",");
            AppendString(sb, "fullName", test.FullName);
            sb.Append(",");
            AppendString(sb, "uniqueName", test.UniqueName);
            sb.Append(",");
            AppendString(sb, "assembly", item.assembly);
            sb.Append(",\"categories\":[");
            var categories = test.Categories ?? new string[0];
            for (var i = 0; i < categories.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\"").Append(RestResponse.EscapeJson(categories[i])).Append("\"");
            }
            sb.Append("],");
            AppendString(sb, "runState", test.RunState.ToString());
            sb.Append(",");
            AppendString(sb, "description", test.Description);
            sb.Append(",");
            AppendString(sb, "skipReason", test.SkipReason);
            sb.Append("}");
        }

        private static void AppendString(StringBuilder sb, string name, string value)
            => sb.Append("\"").Append(name).Append("\":\"")
                .Append(RestResponse.EscapeJson(value ?? "")).Append("\"");

        private static void AbortPending()
            => CompleteError("Test discovery was interrupted by an assembly reload.", 409);

        private static void CompleteError(string message, int statusCode)
        {
            if (_pendingResponse == null)
                return;
            try
            {
                RestResponse.SendError(_pendingResponse, message, statusCode);
            }
            catch (Exception)
            {
                // The client may have disconnected while the response was deferred.
            }
            finally
            {
                ClosePending();
            }
        }

        private static void ClosePending()
        {
            try
            {
                _pendingResponse?.Close();
            }
            finally
            {
                _pendingResponse = null;
            }
        }

        private sealed class TestItem
        {
            internal ITestAdaptor test;
            internal string assembly;
        }
    }
}

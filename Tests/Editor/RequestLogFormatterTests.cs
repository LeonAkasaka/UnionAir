using System.Text;
using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers how a captured exchange is described, independently of the windows that draw it.
    /// </summary>
    internal sealed class RequestLogFormatterTests
    {
        private static RequestLogEntry Completed(
            string method = "GET",
            string path = "/api/health",
            string query = "",
            string requestBody = null,
            int status = 200,
            string responseContentType = "application/json; charset=utf-8",
            string responseBody = "{\"status\":\"ok\"}")
        {
            var entry = new RequestLogEntry
            {
                Method = method,
                RequestOrigin = "http://localhost:51801",
                Path = path,
                Query = query,
                RequestBody = requestBody,
                RequestBodyLength = requestBody == null ? 0 : requestBody.Length,
                StatusCode = status,
                ResponseContentType = responseContentType,
                Completed = true,
                DurationMs = 12.34,
            };

            if (responseBody != null)
            {
                entry.ResponseBody = Encoding.UTF8.GetBytes(responseBody);
                entry.ResponseBodyLength = entry.ResponseBody.Length;
                entry.ResponseBodyCaptured = true;
            }
            return entry;
        }

        // ── curl ────────────────────────────────────────────────────────────

        [Test]
        public void BuildCurl_OmitsTheStopParsingTokenForEveryShellThatDoesNotNeedIt()
        {
            var entry = Completed("POST", "/api/test", requestBody: "{\"a\":\"x y\"}");

            StringAssert.DoesNotContain(
                "--%", RequestLogFormatter.BuildCurl(entry, CurlShell.Bash));
            StringAssert.DoesNotContain(
                "--%", RequestLogFormatter.BuildCurl(entry, CurlShell.PowerShell7));
        }

        [Test]
        public void BuildCurl_UsesCurlExeSoPowerShellDoesNotResolveTheAlias()
        {
            var command = RequestLogFormatter.BuildCurl(Completed());

            StringAssert.StartsWith("curl.exe ", command);
        }

        [Test]
        public void BuildCurl_OmitsTheMethodFlagForGet()
        {
            var command = RequestLogFormatter.BuildCurl(Completed());

            Assert.AreEqual("curl.exe 'http://localhost:51801/api/health'", command);
        }

        [Test]
        public void BuildCurl_UsesTheOriginCapturedWithTheEntry()
        {
            var entry = Completed();
            entry.RequestOrigin = "http://localhost:54321";

            Assert.AreEqual(
                "curl.exe 'http://localhost:54321/api/health'",
                RequestLogFormatter.BuildCurl(entry));
        }

        [Test]
        public void BuildCurl_IncludesTheQueryString()
        {
            var command = RequestLogFormatter.BuildCurl(
                Completed(path: "/api/gameobjects", query: "?scenePath=Main"));

            StringAssert.Contains("'http://localhost:51801/api/gameobjects?scenePath=Main'", command);
        }

        [Test]
        public void BuildCurl_SendsTheContentTypeTheApiRequiresWithABody()
        {
            // Without this header UnionAir answers 415, which is the mistake most easily made
            // when writing the command by hand.
            var command = RequestLogFormatter.BuildCurl(
                Completed("POST", "/api/gameobjects", requestBody: "{\"name\":\"Cube\"}"));

            StringAssert.Contains("-X POST", command);
            StringAssert.Contains("-H 'Content-Type: application/json'", command);
            StringAssert.Contains("-d '{\"name\":\"Cube\"}'", command);
        }

        [Test]
        public void BuildCurl_FramesAnEmptyPostExplicitly()
        {
            // Windows HttpListener answers 411 for a POST with no length and no chunking.
            var command = RequestLogFormatter.BuildCurl(
                Completed("POST", "/api/editor/refresh"));

            StringAssert.Contains("-H 'Content-Length: 0'", command);
            StringAssert.DoesNotContain("-d", command);
        }

        [Test]
        public void BuildCurl_NeverSendsAnOriginHeader()
        {
            // UnionAir rejects any request that carries one, so a command with it always fails.
            var command = RequestLogFormatter.BuildCurl(
                Completed("POST", "/api/gameobjects", requestBody: "{\"a\":1}"));

            StringAssert.DoesNotContain("Origin", command);
        }

        [Test]
        public void BuildCurl_IsNotOfferedForATruncatedRequestBody()
        {
            var entry = Completed("POST", "/api/big", requestBody: null);
            entry.RequestBodyTruncated = true;

            Assert.IsFalse(RequestLogFormatter.CanBuildCurl(entry));
            Assert.AreEqual("", RequestLogFormatter.BuildCurl(entry));
        }

        [Test]
        public void BuildCurl_IsNotOfferedWithoutACapturedOrigin()
        {
            var entry = Completed();
            entry.RequestOrigin = null;

            Assert.IsFalse(RequestLogFormatter.CanBuildCurl(entry));
            Assert.AreEqual("", RequestLogFormatter.BuildCurl(entry));
        }

        [Test]
        public void Quote_ClosesAndReopensAroundAnEmbeddedSingleQuote()
        {
            Assert.AreEqual("'it'\\''s'", RequestLogFormatter.Quote("it's", CurlShell.Bash));
        }

        [Test]
        public void Quote_LeavesASingleQuoteAloneForWindowsPowerShell()
        {
            // Past --% the value is Windows' to parse, not PowerShell's.
            Assert.AreEqual(
                "\"it's\"", RequestLogFormatter.Quote("it's", CurlShell.WindowsPowerShell));
        }

        [Test]
        public void Quote_EscapesInnerDoubleQuotesForWindowsPowerShell()
        {
            Assert.AreEqual(
                "\"{\\\"name\\\":\\\"Cube\\\"}\"",
                RequestLogFormatter.Quote("{\"name\":\"Cube\"}", CurlShell.WindowsPowerShell));
        }

        [Test]
        public void EscapeForWindowsArgv_UsesTwoNPlusOneBackslashesBeforeAQuote()
        {
            // The case the obvious implementation gets wrong: a JSON body carrying an escaped
            // quote. One backslash reaches the quote, so three are written plus the escape.
            // Verbatim strings: a backslash is literal and a quote is written twice.
            Assert.AreEqual(
                @"said \\\""hi\\\""",
                RequestLogFormatter.EscapeForWindowsArgv(@"said \""hi\"""));
        }

        [Test]
        public void EscapeForWindowsArgv_LeavesBackslashesNotBeforeAQuoteAlone()
        {
            // A Windows path in a JSON body, where JSON has already doubled each separator.
            Assert.AreEqual(
                @"C:\\Assets\\My Prefab.prefab",
                RequestLogFormatter.EscapeForWindowsArgv(@"C:\\Assets\\My Prefab.prefab"));
        }

        [Test]
        public void EscapeForWindowsArgv_DoublesTrailingBackslashes()
        {
            // The closing quote follows them, so they would otherwise escape it.
            Assert.AreEqual("x\\\\", RequestLogFormatter.EscapeForWindowsArgv("x\\"));
        }

        [Test]
        public void Quote_LeavesInnerDoubleQuotesAloneForBash()
        {
            Assert.AreEqual(
                "'{\"name\":\"Cube\"}'",
                RequestLogFormatter.Quote("{\"name\":\"Cube\"}", CurlShell.Bash));
        }

        [Test]
        public void BuildCurl_QuotesTheWholeCommandForWindowsPowerShell()
        {
            var command = RequestLogFormatter.BuildCurl(
                Completed("POST", "/api/gameobjects", requestBody: "{\"name\":\"Cube\"}"),
                CurlShell.WindowsPowerShell);

            StringAssert.StartsWith("curl.exe --% ", command,
                "Without the stop-parsing token 5.1 splits the body at its first space.");
            StringAssert.Contains("-d \"{\\\"name\\\":\\\"Cube\\\"}\"", command);
            StringAssert.Contains("-H \"Content-Type: application/json\"", command);
        }


        [Test]
        public void Quote_DoublesTheSingleQuoteForPowerShell7()
        {
            Assert.AreEqual("'it''s'", RequestLogFormatter.Quote("it's", CurlShell.PowerShell7));
        }

        [Test]
        public void Quote_LeavesInnerDoubleQuotesAloneForPowerShell7()
        {
            // 7 passes a single-quoted argument through intact. Escaping the double quotes the
            // way 5.1 needs would reach curl literally and make the body invalid JSON.
            Assert.AreEqual(
                "'{\"name\":\"Cube\"}'",
                RequestLogFormatter.Quote("{\"name\":\"Cube\"}", CurlShell.PowerShell7));
        }

        [Test]
        public void BuildCurl_DefaultsToBash()
        {
            var entry = Completed("POST", "/api/test", requestBody: "{\"a\":\"it's\"}");

            Assert.AreEqual(
                RequestLogFormatter.BuildCurl(entry, CurlShell.Bash),
                RequestLogFormatter.BuildCurl(entry));
        }

        [TestCase(CurlShell.Bash)]
        [TestCase(CurlShell.PowerShell7)]
        [TestCase(CurlShell.WindowsPowerShell)]
        public void ShellLabel_NamesEveryMode(CurlShell shell)
        {
            Assert.IsNotEmpty(RequestLogFormatter.ShellLabel(shell));
        }

        // ── Bodies ──────────────────────────────────────────────────────────

        [Test]
        public void ResponseBodyText_DescribesABinaryPayloadInsteadOfRenderingIt()
        {
            var entry = Completed(responseContentType: "image/png", responseBody: null);
            entry.ResponseBodyLength = 2 * 1024 * 1024;

            bool clipped;
            var text = RequestLogFormatter.ResponseBodyText(entry, out clipped);

            StringAssert.Contains("image/png", text);
            StringAssert.Contains("2 MB", text);
            StringAssert.Contains("not captured", text);
            Assert.IsFalse(clipped);
        }

        [Test]
        public void ResponseBodyText_SaysWhereCaptureStopped()
        {
            var entry = Completed(responseBody: "{\"a\":1}");
            entry.ResponseBodyTruncated = true;
            entry.ResponseBodyLength = 900000;

            bool clipped;
            var text = RequestLogFormatter.ResponseBodyText(entry, out clipped);

            StringAssert.Contains("truncated", text);
        }

        [Test]
        public void ResponseBodyText_ClipsAVeryLongBodyForDisplay()
        {
            var entry = Completed(
                responseBody: new string('x', RequestLogFormatter.MaxDisplayChars + 100));

            bool clipped;
            var text = RequestLogFormatter.ResponseBodyText(entry, out clipped);

            Assert.IsTrue(clipped);
            Assert.AreEqual(RequestLogFormatter.MaxDisplayChars, text.Length);
        }

        [Test]
        public void RequestBodyText_ExplainsAnUncapturedBody()
        {
            var entry = Completed("POST", "/api/big");
            entry.RequestBodyTruncated = true;
            entry.RequestBodyLength = 1024 * 1024;

            bool clipped;
            var text = RequestLogFormatter.RequestBodyText(entry, out clipped);

            StringAssert.Contains("not captured", text);
            StringAssert.Contains("1 MB", text);
        }

        [Test]
        public void ResponseBodyText_ReportsAnIncompleteExchange()
        {
            var entry = new RequestLogEntry { Method = "GET", Path = "/api/slow" };

            bool clipped;
            Assert.AreEqual(
                "(response has not completed)",
                RequestLogFormatter.ResponseBodyText(entry, out clipped));
        }

        // ── Summaries ───────────────────────────────────────────────────────

        [Test]
        public void RequestSummary_CarriesTheRequestLineAndHeaders()
        {
            var entry = Completed("POST", "/api/gameobjects", "?scenePath=Main");
            entry.RequestHeaders = "User-Agent: curl/8.0\nAccept: */*";

            var summary = RequestLogFormatter.RequestSummary(entry);

            StringAssert.Contains("POST /api/gameobjects?scenePath=Main", summary);
            StringAssert.Contains("User-Agent: curl/8.0", summary);
            StringAssert.Contains("Accept: */*", summary);
        }

        [Test]
        public void RequestSummary_OmitsTheQueryAndHeaderBlockWhenThereAreNone()
        {
            var summary = RequestLogFormatter.RequestSummary(Completed());

            Assert.AreEqual("GET /api/health\n", summary);
        }

        [Test]
        public void ResponseSummary_CarriesStatusContentTypeDurationAndSize()
        {
            var summary = RequestLogFormatter.ResponseSummary(Completed());

            StringAssert.Contains("200", summary);
            StringAssert.Contains("application/json; charset=utf-8", summary);
            StringAssert.Contains("12.3 ms", summary);
            StringAssert.Contains("15 B", summary);
        }

        [Test]
        public void ResponseSummary_OmitsAnAbsentContentType()
        {
            var entry = Completed(status: 204, responseContentType: null, responseBody: null);

            var summary = RequestLogFormatter.ResponseSummary(entry);

            StringAssert.Contains("204", summary);
            StringAssert.Contains("0 B", summary);
        }

        [Test]
        public void ResponseSummary_ReportsAnIncompleteExchange()
        {
            Assert.AreEqual(
                "In progress",
                RequestLogFormatter.ResponseSummary(
                    new RequestLogEntry { Method = "GET", Path = "/api/slow" }));
        }

        [Test]
        public void RequestBodyText_SeparatesAnUnreadableBodyFromAnOversizedOne()
        {
            var entry = Completed("POST", "/api/test");
            entry.RequestBodyUnreadable = true;

            bool clipped;
            var text = RequestLogFormatter.RequestBodyText(entry, out clipped);

            StringAssert.Contains("could not be read", text);
            StringAssert.DoesNotContain("cap", text,
                "A body that was never too large must not be blamed on the cap.");
        }

        [Test]
        public void SummaryLine_CarriesStatusMethodPathAndDuration()
        {
            var line = RequestLogFormatter.SummaryLine(
                Completed("POST", "/api/gameobjects", "?scenePath=Main"));

            StringAssert.Contains("200", line);
            StringAssert.Contains("POST /api/gameobjects?scenePath=Main", line);
            StringAssert.Contains("12.3 ms", line);
        }

        [Test]
        public void SummaryLine_MarksAnExchangeThatHasNotCompleted()
        {
            var line = RequestLogFormatter.SummaryLine(
                new RequestLogEntry { Method = "GET", Path = "/api/slow" });

            StringAssert.Contains("...", line);
        }

        [TestCase(512, "512 B")]
        [TestCase(2048, "2 KB")]
        [TestCase(1572864, "1.5 MB")]
        public void FormatBytes_ScalesToAReadableUnit(long bytes, string expected)
        {
            Assert.AreEqual(expected, RequestLogFormatter.FormatBytes(bytes));
        }

        [TestCase(0.5, "0.5 ms")]
        [TestCase(999.0, "999 ms")]
        [TestCase(1500.0, "1.5 s")]
        public void FormatDuration_ScalesToAReadableUnit(double ms, string expected)
        {
            Assert.AreEqual(expected, RequestLogFormatter.FormatDuration(ms));
        }

        [Test]
        public void SuggestFileName_DerivesFromThePathAndContentType()
        {
            Assert.AreEqual(
                "api-health-response.json",
                RequestLogFormatter.SuggestFileName(Completed(), true));
            Assert.AreEqual(
                "api-health-request.json",
                RequestLogFormatter.SuggestFileName(Completed(), false));
        }
    }
}

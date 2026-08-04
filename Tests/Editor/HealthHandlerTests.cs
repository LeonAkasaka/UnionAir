using NUnit.Framework;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    internal sealed class HealthHandlerTests
    {
        [Test]
        public void BuildResponseJson_IdentifiesTheProjectAndEscapesItsPath()
        {
            Assert.AreEqual(
                "{\"status\":\"ok\",\"unityVersion\":\"6000.0.80f1\"," +
                "\"projectPath\":\"C:\\\\Work\\\\Project\\\"One\"}",
                HealthHandler.BuildResponseJson(
                    "6000.0.80f1",
                    "C:\\Work\\Project\"One"));
        }
    }
}

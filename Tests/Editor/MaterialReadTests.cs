using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the material read, and the promise that makes it worth having: a value it reports
    /// can be sent straight back to <c>PATCH /api/assets/materials</c>.
    /// </summary>
    /// <remarks>
    /// The write had no matching read. A client could set <c>_Color</c> and had no way to ask what
    /// <c>_Color</c> was, so making a variant of an existing material — the ordinary way a
    /// colourway is authored — meant reading the <c>.shader</c> file for property names and
    /// rebuilding every value by hand.
    ///
    /// Both handlers are driven rather than the <see cref="Material"/> inspected, because what is
    /// being promised is that one endpoint's output is the other's input, and only running both
    /// shows it.
    /// </remarks>
    internal sealed class MaterialReadTests
    {
        private const string Dir = "Assets/UnionAirMaterialReadTests";
        private const string MaterialPath = Dir + "/Test.mat";
        private const string TexturePath = Dir + "/Test.png";

        private string _guid;
        private Material _material;
        private Texture2D _texture;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "UnionAirMaterialReadTests");

            // Standard rather than a pipeline shader: it ships with every Unity install, and it
            // declares Color, Range, Float and Texture properties plus hidden ones, which is the
            // spread this endpoint has to describe.
            AssetDatabase.DeleteAsset(MaterialPath);
            AssetDatabase.CreateAsset(new Material(Shader.Find("Standard")), MaterialPath);

            AssetDatabase.DeleteAsset(TexturePath);
            var texture = new Texture2D(2, 2);
            System.IO.File.WriteAllBytes(TexturePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(TexturePath);

            AssetDatabase.SaveAssets();
            _material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            _texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            _guid = AssetDatabase.AssetPathToGUID(MaterialPath);
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(Dir);

        // ── What the read reports ────────────────────────────────────────────

        [Test]
        public void Read_ReportsTheShaderByTheNameThatCreatesAMaterial()
        {
            // The same string POST /api/assets/materials takes, so a material can be recreated.
            StringAssert.Contains("\"shader\":\"Standard\"", Read());
        }

        [Test]
        public void Read_ReportsEveryDeclaredPropertyOnce()
        {
            var body = Read();
            var shader = _material.shader;

            var seen = new HashSet<string>();
            for (var i = 0; i < shader.GetPropertyCount(); i++)
            {
                var name = shader.GetPropertyName(i);
                Assert.IsTrue(seen.Add(name), "duplicate in the shader itself: " + name);
                StringAssert.Contains("\"name\":\"" + name + "\"", body);
            }

            Assert.AreEqual(shader.GetPropertyCount(), Occurrences(body, "\"name\":\""), body);
        }

        [Test]
        public void Read_ReportsTheRenderQueueAndKeywords()
        {
            // Both are read-only here, and reported because they are usually why a material built
            // from another one's property values still does not look the same.
            var body = Read();
            StringAssert.Contains("\"renderQueue\":", body);
            StringAssert.Contains("\"keywords\":[", body);
        }

        [Test]
        public void Read_ReportsARangeWithItsLimitsAndAFloatWithout()
        {
            var body = Read();

            StringAssert.Contains(
                "\"name\":\"_Glossiness\",\"type\":\"Range\",\"value\":0.5,\"range\":{\"min\":0,\"max\":1}", body);
            StringAssert.Contains("\"name\":\"_BumpScale\",\"type\":\"Float\",\"value\":1,\"flags\":[]", body);
        }

        [Test]
        public void Read_ReportsShaderPropertyFlagsByName()
        {
            // Unfiltered, and by name rather than as an enum value, so a flag a later Unity adds
            // still arrives readable. Standard hides four of its properties and marks two as
            // normal maps.
            var body = Read();
            StringAssert.Contains("\"name\":\"_Mode\",\"type\":\"Float\",\"value\":0,\"flags\":[\"HideInInspector\"]", body);
            StringAssert.Contains("\"name\":\"_BumpMap\",\"type\":\"Texture\",\"value\":null,\"flags\":[\"Normal\"]", body);
        }

        [Test]
        public void Read_ReportsAnUnassignedTextureAsNull()
        {
            // null is what the write takes to clear a texture, so the empty case round trips as
            // well as the assigned one.
            StringAssert.Contains("\"name\":\"_MainTex\",\"type\":\"Texture\",\"value\":null", Read());
        }

        [Test]
        public void Read_ReportsATextureInTheVocabularyTheWriteAccepts()
        {
            _material.SetTexture("_MainTex", _texture);

            var body = Read();
            StringAssert.Contains("\"assetPath\":\"" + TexturePath + "\"", body);
            StringAssert.Contains("\"assetGuid\":\"" + AssetDatabase.AssetPathToGUID(TexturePath) + "\"", body);
            StringAssert.Contains("\"assetType\":\"UnityEngine.Texture2D\"", body);
        }

        // ── The round trip ───────────────────────────────────────────────────

        [Test]
        public void AValueTheReadReportsIsAValueTheWriteAccepts()
        {
            _material.SetColor("_Color", new Color(0.25f, 0.5f, 0.75f, 1f));
            _material.SetFloat("_Glossiness", 0.375f);
            _material.SetTexture("_MainTex", _texture);

            // Lifted out of the response rather than rebuilt, so the write receives exactly what a
            // client would have echoed.
            var before = Read();
            var body = "{\"properties\":{" +
                       "\"_Color\":" + Value(before, "_Color") + "," +
                       "\"_Glossiness\":" + Value(before, "_Glossiness") + "," +
                       "\"_MainTex\":" + Value(before, "_MainTex") + "}}";

            var response = Patch(body);
            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"_Color\"", response.Body);
            StringAssert.Contains("\"_Glossiness\"", response.Body);
            StringAssert.Contains("\"_MainTex\"", response.Body);

            Assert.AreEqual(Value(before, "_Color"), Value(Read(), "_Color"));
            Assert.AreEqual(Value(before, "_Glossiness"), Value(Read(), "_Glossiness"));
            Assert.AreEqual(Value(before, "_MainTex"), Value(Read(), "_MainTex"));
        }

        [Test]
        public void AnUnassignedTextureRoundTripsAsNull()
        {
            var before = Read();
            Assert.AreEqual("null", Value(before, "_MainTex"));

            var response = Patch("{\"properties\":{\"_MainTex\":null}}");
            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.AreEqual("null", Value(Read(), "_MainTex"));
        }

        // ── Errors ───────────────────────────────────────────────────────────

        [Test]
        public void AGuidNamingNoAssetIs404()
        {
            var response = ReadResponse(new string('0', 32));
            Assert.AreEqual(404, response.StatusCode, response.Body);
        }

        [Test]
        public void AGuidNamingSomethingThatIsNotAMaterialIs400()
        {
            var response = ReadResponse(AssetDatabase.AssetPathToGUID(TexturePath));
            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("not a Material", response.Body);
        }

        [Test]
        public void AMissingGuidIs400()
        {
            var response = ReadResponse(null);
            Assert.AreEqual(400, response.StatusCode, response.Body);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private string Read()
        {
            var response = ReadResponse(_guid);
            Assert.AreEqual(200, response.StatusCode, response.Body);
            return response.Body;
        }

        private static FakeResponse ReadResponse(string guid)
        {
            var response = new FakeResponse();
            new MaterialReadHandler().Handle(response, guid);
            return response;
        }

        private FakeResponse Patch(string body)
        {
            var request = new FakeRequest(
                "PATCH", "/api/assets/materials?guid=" + _guid).WithJsonBody(body);
            var response = new FakeResponse();
            new MaterialWriteHandler().Handle(request, response);
            return response;
        }

        /// <summary>The JSON value the read reported for one property, lifted out of the response.</summary>
        private static string Value(string body, string propertyName)
        {
            var key = "\"name\":\"" + propertyName + "\",";
            var start = body.IndexOf(key, StringComparison.Ordinal);
            Assert.Greater(start, -1, propertyName + " missing from the read: " + body);

            const string valueKey = "\"value\":";
            start = body.IndexOf(valueKey, start, StringComparison.Ordinal) + valueKey.Length;

            if (body[start] != '{')
                return body.Substring(start, body.IndexOfAny(new[] { ',', '}' }, start) - start);

            var depth = 0;
            for (var i = start; i < body.Length; i++)
            {
                if (body[i] == '{') depth++;
                else if (body[i] == '}' && --depth == 0)
                    return body.Substring(start, i - start + 1);
            }

            Assert.Fail("unterminated value for " + propertyName + ": " + body);
            return null;
        }

        private static int Occurrences(string body, string value)
        {
            var count = 0;
            for (var i = body.IndexOf(value, StringComparison.Ordinal);
                 i >= 0;
                 i = body.IndexOf(value, i + value.Length, StringComparison.Ordinal))
                count++;
            return count;
        }
    }
}

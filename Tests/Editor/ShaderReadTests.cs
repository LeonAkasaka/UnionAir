using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the shader read, and the one thing it exists to report: whether Unity accepted the
    /// last import of a shader, and with what messages.
    /// </summary>
    /// <remarks>
    /// The shaders are written as source and imported inside the test rather than loaded from a
    /// fixture, because the failing case is only interesting if Unity actually rejected it. A
    /// committed broken <c>.shader</c> would report an error in every project that opened the
    /// package, and a mock would prove nothing about what <c>ShaderUtil</c> caches.
    ///
    /// The failing shader is broken at the ShaderLab level rather than inside HLSL. A ShaderLab
    /// parse error is produced while the asset is imported, which is when the messages this
    /// endpoint reports are cached; an HLSL error can wait for a variant to be compiled.
    /// </remarks>
    internal sealed class ShaderReadTests
    {
        private const string Dir = "Assets/UnionAirShaderReadTests";
        private const string ShaderPath = Dir + "/Read.shader";
        private const string BrokenShaderPath = Dir + "/Broken.shader";
        private const string PartlyBrokenShaderPath = Dir + "/PartlyBroken.shader";
        private const string UnsupportedShaderPath = Dir + "/Unsupported.shader";
        private const string FallbackShaderPath = Dir + "/UnsupportedWithFallback.shader";
        private const string PipelineTaggedShaderPath = Dir + "/PipelineTagged.shader";
        private const string TexturePath = Dir + "/Test.png";

        private const string ShaderName = "UnionAir/ReadTest";

        private const string Source = @"Shader ""UnionAir/ReadTest""
{
    Properties
    {
        _Color (""Tint"", Color) = (1, 0.5, 0.25, 1)
        _Cutoff (""Cutoff"", Range(0, 1)) = 0.25
        _Offset (""Offset"", Vector) = (1, 2, 3, 4)
        [HideInInspector] _Hidden (""Hidden"", Float) = 7
        [Toggle(UNIONAIR_TEST_ON)] _Toggle (""Toggle"", Float) = 0
        [MainTexture] _MainTex (""Base Map"", 2D) = ""white"" {}
    }
    SubShader
    {
        Tags { ""RenderType"" = ""Opaque"" }
        LOD 200
        Pass
        {
            Name ""UnionAirForward""
            Tags { ""LightMode"" = ""UnionAirTest"" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ UNIONAIR_TEST_ON
            #include ""UnityCG.cginc""
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };
            fixed4 _Color;
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
            fixed4 frag (v2f i) : SV_Target { return _Color; }
            ENDCG
        }
    }
}
";

        // An unknown property type, which ShaderLab rejects while parsing the asset.
        private const string BrokenSource = @"Shader ""UnionAir/BrokenReadTest""
{
    Properties
    {
        _Color (""Tint"", NotAShaderPropertyType) = (1, 1, 1, 1)
    }
    SubShader
    {
        Pass { }
    }
}
";

        // Imports, carries an error, and is still a shader Unity draws with: the first subshader
        // fails to compile and the second does not. This is the case that separates isSupported
        // from hasError, and the reason the structure is not suppressed by the latter.
        private const string PartlyBrokenSource = @"Shader ""UnionAir/PartlyBrokenReadTest""
{
    SubShader
    {
        Tags { ""RenderType"" = ""Opaque"" }
        LOD 300
        Pass
        {
            Name ""BadPass""
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            float4 vert (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag () : SV_Target { return undefined_symbol_here; }
            ENDCG
        }
    }
    SubShader
    {
        Tags { ""RenderType"" = ""Opaque"" }
        LOD 100
        Pass
        {
            Name ""GoodPass""
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            float4 vert (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag () : SV_Target { return fixed4(1, 1, 1, 1); }
            ENDCG
        }
    }
}
";

        // Imports without a single error and still reports isSupported false, because its only
        // pass is excluded for every renderer an editor runs on. This is the case that separates
        // "can this shader be used here" from "did Unity read this file", and the reason the
        // structure is not suppressed by isSupported.
        private const string UnsupportedSource = @"Shader ""UnionAir/UnsupportedReadTest""
{
    Properties
    {
        _Color (""Tint"", Color) = (1, 0.5, 0.25, 1)
    }
    SubShader
    {
        Tags { ""RenderType"" = ""Opaque"" }
        LOD 250
        Pass
        {
            Name ""ExcludedPass""
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma exclude_renderers d3d11 d3d11_9x glcore gles3 vulkan metal xboxone ps4 switch
            #include ""UnityCG.cginc""
            float4 vert (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag () : SV_Target { return fixed4(1, 1, 1, 1); }
            ENDCG
        }
    }
}
";

        // The same shader given a Fallback, which makes isSupported true while the subshaders
        // reported become the fallback's rather than this file's.
        private const string FallbackSource = @"Shader ""UnionAir/FallbackReadTest""
{
    Properties
    {
        _Color (""Tint"", Color) = (1, 0.5, 0.25, 1)
    }
    SubShader
    {
        Tags { ""RenderType"" = ""Opaque"" }
        LOD 250
        Pass
        {
            Name ""ExcludedPass""
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma exclude_renderers d3d11 d3d11_9x glcore gles3 vulkan metal xboxone ps4 switch
            #include ""UnityCG.cginc""
            float4 vert (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag () : SV_Target { return fixed4(1, 1, 1, 1); }
            ENDCG
        }
    }
    Fallback ""Diffuse""
}
";

        // Two subshaders, one claiming a render pipeline by tag and one claiming none. The tag
        // decides which subshader a pipeline may select, and it is the only thing in the response
        // that says which pipeline a shader was written for. Both subshaders are here so the test
        // reads the same way in a built-in project and a URP one: a subshader tagged for a pipeline
        // that is not active is skipped, and the untagged one is always a candidate, so one of them
        // is selectable either way and neither project needs a Fallback.
        private const string PipelineTaggedSource = @"Shader ""UnionAir/PipelineTaggedReadTest""
{
    SubShader
    {
        Tags { ""RenderPipeline"" = ""UniversalPipeline"" ""RenderType"" = ""Opaque"" }
        LOD 300
        Pass
        {
            Name ""TaggedPass""
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            float4 vert (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag () : SV_Target { return fixed4(1, 1, 1, 1); }
            ENDCG
        }
    }
    SubShader
    {
        Tags { ""RenderType"" = ""Opaque"" }
        LOD 100
        Pass
        {
            Name ""UntaggedPass""
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            float4 vert (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag () : SV_Target { return fixed4(1, 1, 1, 1); }
            ENDCG
        }
    }
}
";

        private string _guid;
        private Shader _shader;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "UnionAirShaderReadTests");

            Import(ShaderPath, Source);

            var texture = new Texture2D(2, 2);
            System.IO.File.WriteAllBytes(TexturePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(TexturePath);

            _guid = AssetDatabase.AssetPathToGUID(ShaderPath);
            _shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.IsNotNull(_shader, "the fixture shader did not import: " + ShaderPath);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            AssetDatabase.DeleteAsset(Dir);
        }

        // ── What the read reports ────────────────────────────────────────────

        [Test]
        public void Read_ReportsTheShaderByTheNameThatCreatesAMaterial()
        {
            // The same string POST /api/assets/materials takes, which is not the file name.
            var body = Read();
            StringAssert.Contains("\"name\":\"" + ShaderName + "\"", body);
            StringAssert.Contains("\"assetPath\":\"" + ShaderPath + "\"", body);
            StringAssert.Contains("\"guid\":\"" + _guid + "\"", body);
        }

        [Test]
        public void Read_ReportsEveryDeclaredPropertyOnce()
        {
            var body = Properties(Read());

            var seen = new HashSet<string>();
            for (var i = 0; i < _shader.GetPropertyCount(); i++)
            {
                var name = _shader.GetPropertyName(i);
                Assert.IsTrue(seen.Add(name), "duplicate in the shader itself: " + name);
                StringAssert.Contains("\"name\":\"" + name + "\"", body);
            }

            Assert.AreEqual(6, _shader.GetPropertyCount(), "the fixture declares six properties");
            Assert.AreEqual(_shader.GetPropertyCount(), Occurrences(body, "\"name\":\""), body);
        }

        [Test]
        public void Read_ReportsARangeWithItsDeclaredDefaultAndLimits()
        {
            // The value a new material starts at, which is the question a client creating one has
            // and which the material read can only answer after the material exists.
            StringAssert.Contains(
                "\"name\":\"_Cutoff\",\"type\":\"Range\",\"description\":\"Cutoff\"," +
                "\"defaultValue\":0.25,\"range\":{\"min\":0,\"max\":1}",
                Read());
        }

        [Test]
        public void Read_ReportsAVectorDefaultInTheVocabularyTheMaterialWriteAccepts()
        {
            StringAssert.Contains(
                "\"name\":\"_Offset\",\"type\":\"Vector\",\"description\":\"Offset\"," +
                "\"defaultValue\":{\"x\":1,\"y\":2,\"z\":3,\"w\":4}",
                Read());
        }

        [Test]
        public void Read_ReportsATextureDefaultAsItsBuiltinNameAndItsDimension()
        {
            // A built-in texture name rather than an object reference, because that is what the
            // declaration carries and no asset exists to point at.
            StringAssert.Contains(
                "\"name\":\"_MainTex\",\"type\":\"Texture\",\"description\":\"Base Map\"," +
                "\"defaultValue\":\"white\",\"textureDimension\":\"Tex2D\"",
                Read());
        }

        [Test]
        public void Read_ReportsShaderPropertyFlagsByName()
        {
            // The same spelling GET /api/assets/materials/{guid} uses, from the same helper.
            // Unity turns some declaration attributes into flags rather than leaving them in
            // `attributes`: [HideInInspector] and [MainTexture] both arrive here.
            var body = Read();
            StringAssert.Contains("\"name\":\"_Hidden\"", body);
            StringAssert.Contains("\"flags\":[\"HideInInspector\"]", body);
            StringAssert.Contains("\"name\":\"_MainTex\"", body);
            StringAssert.Contains("\"flags\":[\"MainTexture\"]", body);
        }

        [Test]
        public void Read_ReportsTheDeclarationAttributesUnityDidNotTurnIntoFlags()
        {
            // [Toggle(...)] is how a keyword becomes reachable from a property, and it is not a
            // flag -- so a client reading `flags` alone cannot tell which property drives which
            // keyword. It arrives with its argument, which is the keyword's name.
            StringAssert.Contains("\"attributes\":[\"Toggle(UNIONAIR_TEST_ON)\"]", Read());
        }

        [Test]
        public void Read_ReportsTheEffectiveKeywordSpaceAndNotOnlyTheDeclarations()
        {
            // A material only stores the keywords it has enabled, so the valid set is reachable
            // only from the shader. What Unity exposes is the effective local keyword space, which
            // is wider than the file: the fixture declares exactly one keyword through
            // multi_compile and the space also carries keywords Unity adds by itself. A client
            // must not read a name here as evidence that it appears in the source.
            var body = Read();
            StringAssert.Contains("\"name\":\"UNIONAIR_TEST_ON\"", body);
            StringAssert.Contains("\"name\":\"STEREO_INSTANCING_ON\"", body);
        }

        [Test]
        public void Read_ReportsTheActiveSubshaderAndItsPasses()
        {
            var body = Read();
            StringAssert.Contains("\"activeSubshaderIndex\":0", body);
            StringAssert.Contains("\"levelOfDetail\":200", body);
            StringAssert.Contains(
                "\"name\":\"UnionAirForward\",\"lightMode\":\"UnionAirTest\",\"isGrabPass\":false", body);
        }

        [Test]
        public void Read_ReportsASubshaderWithNoRenderPipelineTagAsNull()
        {
            // The fixture declares no RenderPipeline tag, which is how a built-in-pipeline
            // subshader reads. Absence is null rather than "", the same as an untagged pass.
            StringAssert.Contains("\"levelOfDetail\":200,\"renderPipeline\":null", Read());
        }

        [Test]
        public void Read_ReportsTheRenderPipelineTagOfEachSubshader()
        {
            // The tag that answers "which pipeline is this shader for", which is the first question
            // when picking a shader for a material. It is per subshader, not per shader: the same
            // file can carry a URP subshader and a built-in one, and only the tag tells them apart.
            // Reading it from the file works for a hand-written shader and not for a generated one.
            Import(PipelineTaggedShaderPath, PipelineTaggedSource);

            var response = ReadResponse(AssetDatabase.AssetPathToGUID(PipelineTaggedShaderPath));
            Assert.AreEqual(200, response.StatusCode, response.Body);

            StringAssert.Contains("\"levelOfDetail\":300,\"renderPipeline\":\"UniversalPipeline\"", response.Body);
            StringAssert.Contains("\"levelOfDetail\":100,\"renderPipeline\":null", response.Body);
        }

        [Test]
        public void Read_ReportsAnImportedShaderAsHavingNoErrors()
        {
            var body = Read();
            StringAssert.Contains("\"hasError\":false", body);
            StringAssert.Contains("\"messages\":[]", body);
            StringAssert.Contains("\"isSupported\":true", body);
        }

        // ── The failing import ───────────────────────────────────────────────

        [Test]
        public void AShaderUnityRejectedReportsTheErrorAndNoStructure()
        {
            // Unity logs the parse error to the console as it imports; the point of the endpoint
            // is that a client gets it without reading the console.
            LogAssert.ignoreFailingMessages = true;
            Import(BrokenShaderPath, BrokenSource);

            var response = ReadResponse(AssetDatabase.AssetPathToGUID(BrokenShaderPath));
            Assert.AreEqual(200, response.StatusCode, response.Body);

            StringAssert.Contains("\"hasError\":true", response.Body);
            StringAssert.Contains("\"severity\":\"Error\"", response.Body);
            Assert.IsFalse(response.Body.Contains("\"messages\":[]"), response.Body);

            // Unity substituted its error shader, so nothing structural describes what the client
            // wrote. Measured on 6000.0.80f1 before this was suppressed, the response reported
            // name "", properties [], four stereo keywords the shader never declared, and a
            // passCount of 3 against the one pass in the file -- all of it Unity's error shader,
            // and none of it distinguishable from a real answer. The whole group goes together so
            // that isSupported is the one thing a client has to check.
            StringAssert.Contains("\"isSupported\":false", response.Body);
            StringAssert.Contains("\"name\":null", response.Body);
            StringAssert.Contains(
                "\"renderQueue\":null,\"maximumLOD\":null,\"subshaderCount\":null," +
                "\"passCount\":null,\"keywords\":null,\"properties\":null," +
                "\"activeSubshaderIndex\":null,\"subshaders\":null",
                response.Body);
        }

        [Test]
        public void AShaderThatCarriesAnErrorAndStillWorksReportsItsStructure()
        {
            // The case that decides the rule. The first subshader fails to compile and the second
            // does not, so Unity keeps using the shader: measured on 6000.0.80f1, hasError is true
            // while isSupported is true. Suppressing the structure on hasError -- which this
            // endpoint did before -- hid the subshader Unity actually selected for a shader that
            // was working, which is the case a client most needs described.
            LogAssert.ignoreFailingMessages = true;
            Import(PartlyBrokenShaderPath, PartlyBrokenSource);

            var response = ReadResponse(AssetDatabase.AssetPathToGUID(PartlyBrokenShaderPath));
            Assert.AreEqual(200, response.StatusCode, response.Body);

            StringAssert.Contains("\"hasError\":true", response.Body);
            StringAssert.Contains("\"isSupported\":true", response.Body);
            Assert.IsFalse(
                response.Body.Contains("\"subshaders\":null"),
                "the structure of a working shader was suppressed: " + response.Body);
            StringAssert.Contains("\"name\":\"GoodPass\"", response.Body);
        }

        [Test]
        public void AShaderUnityCannotRunHereStillReportsWhatItDeclares()
        {
            // isSupported is Unity's capability signal -- whether the shader runs on this GPU, with
            // fallbacks considered -- and not a statement about the import. This shader has no
            // errors at all; suppressing its structure on isSupported would discard a correct
            // declaration, which is what this endpoint exists to report.
            LogAssert.ignoreFailingMessages = true;
            Import(UnsupportedShaderPath, UnsupportedSource);

            var response = ReadResponse(AssetDatabase.AssetPathToGUID(UnsupportedShaderPath));
            Assert.AreEqual(200, response.StatusCode, response.Body);

            StringAssert.Contains("\"isSupported\":false", response.Body);
            StringAssert.Contains("\"hasError\":false", response.Body);
            StringAssert.Contains("\"name\":\"UnionAir/UnsupportedReadTest\"", response.Body);

            Assert.IsFalse(
                response.Body.Contains("\"properties\":null"),
                "a shader with no errors had its declaration discarded: " + response.Body);
            StringAssert.Contains("\"name\":\"_Color\"", response.Body);
            StringAssert.Contains("\"defaultValue\":{\"r\":1,\"g\":0.5,\"b\":0.25,\"a\":1}", response.Body);
        }

        [Test]
        public void SubshadersAreWhatUnityCompiled_WhichCanBeTheFallbacks()
        {
            // The other half of why isSupported cannot gate the structure: with a Fallback the
            // shader reports isSupported true, and the subshaders reported are the fallback's.
            // Measured on 6000.0.80f1, this shader reports the same two subshaders and four passes
            // as Legacy Shaders/Diffuse, while its properties stay its own. The endpoint does not
            // pretend to detect the substitution; the reference documents it.
            LogAssert.ignoreFailingMessages = true;
            Import(FallbackShaderPath, FallbackSource);

            var response = ReadResponse(AssetDatabase.AssetPathToGUID(FallbackShaderPath));
            Assert.AreEqual(200, response.StatusCode, response.Body);

            StringAssert.Contains("\"isSupported\":true", response.Body);
            Assert.IsFalse(response.Body.Contains("\"subshaders\":null"), response.Body);

            // The declaration survives even though the compiled structure did not.
            StringAssert.Contains("\"name\":\"_Color\"", response.Body);

            // The pass this file declares is gone, replaced by the fallback's.
            Assert.IsFalse(
                response.Body.Contains("\"name\":\"ExcludedPass\""),
                "expected the fallback's passes, not this shader's: " + response.Body);
        }

        [Test]
        public void APlatformlessMessageReportsNoPlatformRatherThanANumber()
        {
            // A ShaderLab parse error happens before any graphics API is involved, and Unity
            // reports its platform as an undefined enum value -- ToString yields "-1" on
            // 6000.0.80f1. A number in a field documented as an API name is not something a client
            // switching on the string can use.
            LogAssert.ignoreFailingMessages = true;
            Import(BrokenShaderPath, BrokenSource);

            var body = ReadResponse(AssetDatabase.AssetPathToGUID(BrokenShaderPath)).Body;
            StringAssert.Contains("\"platform\":null", body);
            Assert.IsFalse(body.Contains("\"platform\":\"-1\""), body);
        }

        // ── The lookup by name ───────────────────────────────────────────────

        [Test]
        public void ReadByName_ReportsTheSameShaderAsTheGuidLookup()
        {
            var byName = NameResponse(ShaderName);
            Assert.AreEqual(200, byName.StatusCode, byName.Body);
            Assert.AreEqual(Read(), byName.Body);
        }

        [Test]
        public void ReadByName_FindsAShaderThatShipsWithUnity()
        {
            // The case the GUID lookup cannot serve: a shader a material can name but the project
            // does not own as an asset.
            var response = NameResponse("Standard");
            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"name\":\"Standard\"", response.Body);
            StringAssert.Contains("\"name\":\"_Glossiness\"", response.Body);
        }

        // ── Errors ───────────────────────────────────────────────────────────

        [Test]
        public void AGuidNamingNoAssetIs404()
        {
            var response = ReadResponse(new string('0', 32));
            Assert.AreEqual(404, response.StatusCode, response.Body);
        }

        [Test]
        public void AGuidNamingSomethingThatIsNotAShaderIs400()
        {
            var response = ReadResponse(AssetDatabase.AssetPathToGUID(TexturePath));
            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("not a Shader", response.Body);
        }

        [Test]
        public void AMissingGuidIs400()
        {
            Assert.AreEqual(400, ReadResponse(null).StatusCode);
        }

        [Test]
        public void AMissingNameIs400()
        {
            var response = NameResponse(null);
            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("name", response.Body);
        }

        [Test]
        public void ANameNoShaderCarriesIs404()
        {
            // The same answer POST /api/assets/materials would give the name, found before the
            // material is created rather than after.
            var response = NameResponse("UnionAir/NoSuchShader");
            Assert.AreEqual(404, response.StatusCode, response.Body);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static void Import(string path, string source)
        {
            System.IO.File.WriteAllText(path, source);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        private string Read()
        {
            var response = ReadResponse(_guid);
            Assert.AreEqual(200, response.StatusCode, response.Body);
            return response.Body;
        }

        private static FakeResponse ReadResponse(string guid)
        {
            var response = new FakeResponse();
            new ShaderReadHandler().HandleByGuid(response, guid);
            return response;
        }

        private static FakeResponse NameResponse(string name)
        {
            var query = name == null ? "" : "?name=" + Uri.EscapeDataString(name);
            var request = new FakeRequest("GET", "/api/assets/shaders" + query);
            var response = new FakeResponse();
            new ShaderReadHandler().HandleByName(request, response);
            return response;
        }

        /// <summary>The properties array alone, so a count is not thrown off by other names.</summary>
        private static string Properties(string body)
        {
            const string key = "\"properties\":[";
            var start = body.IndexOf(key, StringComparison.Ordinal);
            Assert.Greater(start, -1, "no properties in the read: " + body);
            start += key.Length;

            var depth = 1;
            for (var i = start; i < body.Length; i++)
            {
                if (body[i] == '[') depth++;
                else if (body[i] == ']' && --depth == 0)
                    return body.Substring(start, i - start);
            }

            Assert.Fail("unterminated properties array: " + body);
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

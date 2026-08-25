using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers the material-to-shader compatibility read, and the one thing it exists to report:
    /// where a material and its shader have drifted apart since the material was created.
    /// </summary>
    /// <remarks>
    /// The drift is produced rather than fixtured. A material is created from a shader, given
    /// values and a keyword, and then the shader is rewritten and reimported underneath it — which
    /// is the sequence a client performs and the only way the stale state is real. A committed
    /// <c>.mat</c> carrying a hidden property would be a file nobody could explain, and a mock
    /// would prove nothing about what Unity actually keeps in <c>m_SavedProperties</c>.
    /// </remarks>
    internal sealed class MaterialShaderCompatibilityTests
    {
        private const string Dir = "Assets/UnionAirCompatTests";
        private const string ShaderPath = Dir + "/Compat.shader";
        private const string MaterialPath = Dir + "/Compat.mat";
        private const string TexturePath = Dir + "/Test.png";

        // Declares everything the material will end up carrying: a colour, a float, and a texture
        // with a scale and an offset, plus a keyword the material will enable.
        private const string BeforeSource = @"Shader ""UnionAir/CompatTest""
{
    Properties
    {
        _Color (""Tint"", Color) = (1, 1, 1, 1)
        _Extra (""Extra"", Float) = 0
        _OldTex (""OldTex"", 2D) = ""white"" {}
    }
    SubShader
    {
        Tags { ""RenderType"" = ""Opaque"" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ UNIONAIR_COMPAT_ON
            #include ""UnityCG.cginc""
            float4 vert (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag () : SV_Target { return fixed4(1, 1, 1, 1); }
            ENDCG
        }
    }
}
";

        // The same shader after an edit: _Extra and _OldTex are gone, _Added is new, and the
        // keyword has been renamed. Each of the three sections of the response has one cause here.
        private const string AfterSource = @"Shader ""UnionAir/CompatTest""
{
    Properties
    {
        _Color (""Tint"", Color) = (1, 1, 1, 1)
        _Added (""Added"", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { ""RenderType"" = ""Opaque"" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ UNIONAIR_RENAMED_ON
            #include ""UnityCG.cginc""
            float4 vert (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag () : SV_Target { return fixed4(1, 1, 1, 1); }
            ENDCG
        }
    }
}
";

        // The same shader with _Extra retyped rather than removed. The name survives, so a check
        // that only asks whether the shader still declares the name reports nothing wrong -- while
        // the float the material holds has become unreachable.
        private const string RetypedSource = @"Shader ""UnionAir/CompatTest""
{
    Properties
    {
        _Color (""Tint"", Color) = (1, 1, 1, 1)
        _Extra (""Extra"", Color) = (0, 0, 0, 1)
        _OldTex (""OldTex"", 2D) = ""white"" {}
    }
    SubShader
    {
        Tags { ""RenderType"" = ""Opaque"" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ UNIONAIR_COMPAT_ON
            #include ""UnityCG.cginc""
            float4 vert (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag () : SV_Target { return fixed4(1, 1, 1, 1); }
            ENDCG
        }
    }
}
";

        // The same shader with _Extra retyped from Float to Integer. Unity stores an Int in its own
        // map, so this is the one type change where the old entry and the new one are both numeric
        // -- and the one a blanket "an Int may live in m_Floats" tolerance would wave through.
        private const string RetypedToIntSource = @"Shader ""UnionAir/CompatTest""
{
    Properties
    {
        _Color (""Tint"", Color) = (1, 1, 1, 1)
        _Extra (""Extra"", Integer) = 0
        _OldTex (""OldTex"", 2D) = ""white"" {}
    }
    SubShader
    {
        Tags { ""RenderType"" = ""Opaque"" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ UNIONAIR_COMPAT_ON
            #include ""UnityCG.cginc""
            float4 vert (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag () : SV_Target { return fixed4(1, 1, 1, 1); }
            ENDCG
        }
    }
}
";

        // An unknown property type, which ShaderLab rejects while parsing the asset, so Unity gets
        // nothing out of the file at all.
        private const string BrokenSource = @"Shader ""UnionAir/CompatTest""
{
    Properties
    {
        _Color (""Tint"", NotAShaderPropertyType) = (1, 1, 1, 1)
    }
    SubShader { Pass { } }
}
";

        private string _guid;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "UnionAirCompatTests");

            var texture = new Texture2D(2, 2);
            System.IO.File.WriteAllBytes(TexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(TexturePath);

            Import(ShaderPath, BeforeSource);

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.IsNotNull(shader, "the fixture shader did not import: " + ShaderPath);

            var mat = new Material(shader);
            mat.SetColor("_Color", Color.red);
            mat.SetFloat("_Extra", 42f);
            mat.SetTexture("_OldTex", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            mat.SetTextureScale("_OldTex", new Vector2(2f, 3f));
            mat.SetTextureOffset("_OldTex", new Vector2(0.25f, 0.5f));
            mat.EnableKeyword("UNIONAIR_COMPAT_ON");
            AssetDatabase.CreateAsset(mat, MaterialPath);
            AssetDatabase.SaveAssets();

            _guid = AssetDatabase.AssetPathToGUID(MaterialPath);
            Assert.IsNotEmpty(_guid, "the fixture material did not get a GUID");
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            AssetDatabase.DeleteAsset(Dir);
        }

        // ── A pair that agrees ───────────────────────────────────────────────

        [Test]
        public void AMaterialAndItsOwnShaderReportNoDisagreement()
        {
            // The baseline that gives the other tests meaning: everything the material carries is
            // declared, everything declared is carried, and the enabled keyword is in the space.
            // Unity writes an entry for every declared property when the material is created, so
            // unsetProperties is empty here rather than listing the properties nobody touched.
            var body = Read();
            StringAssert.Contains("\"comparable\":true", body);
            StringAssert.Contains("\"reason\":null", body);
            StringAssert.Contains("\"staleProperties\":[]", body);
            StringAssert.Contains("\"unsetProperties\":[]", body);
            StringAssert.Contains("\"invalidKeywords\":[]", body);
        }

        [Test]
        public void TheShaderIsIdentifiedSoTheClientCanGoAndReadIt()
        {
            var body = Read();
            StringAssert.Contains("\"name\":\"UnionAir/CompatTest\"", body);
            StringAssert.Contains("\"assetPath\":\"" + ShaderPath + "\"", body);
            StringAssert.Contains(
                "\"guid\":\"" + AssetDatabase.AssetPathToGUID(ShaderPath) + "\"", body);
        }

        // ── After the shader is edited underneath the material ───────────────

        [Test]
        public void APropertyTheShaderNoLongerDeclaresIsReportedWithTheValueItStillHolds()
        {
            // The case nothing else reports. GET /api/assets/materials/{guid} walks the shader's
            // declarations, so _Extra vanishes from it entirely -- Unity keeps the value in the
            // .mat file and hides it, and a renamed property looks like a lost value with no
            // explanation. The value is reported because "you lost something" and "here is what
            // you lost" are different answers.
            Drift();

            var body = Read();
            StringAssert.Contains("\"comparable\":true", body);
            StringAssert.Contains("\"name\":\"_Extra\",\"storage\":\"Float\",\"value\":42", body);
        }

        [Test]
        public void AStaleTextureReportsTheScaleAndOffsetThatGoWithIt()
        {
            // A texture property stores a reference, a scale and an offset, and all three are lost
            // together. Reporting only the reference would leave two of them unaccounted for.
            Drift();

            var body = Read();
            StringAssert.Contains("\"name\":\"_OldTex\",\"storage\":\"Texture\"", body);
            StringAssert.Contains("\"scale\":{\"x\":2,\"y\":3}", body);
            StringAssert.Contains("\"offset\":{\"x\":0.25,\"y\":0.5}", body);
            StringAssert.Contains("\"assetPath\":\"" + TexturePath + "\"", body);
        }

        [Test]
        public void APropertyAddedToTheShaderAfterwardsIsReportedWithItsDefault()
        {
            // Not broken -- the material uses the declared default -- but nobody chose it, and the
            // material read cannot tell it apart from a value someone did choose. The default is
            // reported so that finding out does not take a second call to the shader read.
            Drift();

            StringAssert.Contains(
                "\"name\":\"_Added\",\"type\":\"Range\",\"defaultValue\":0.5", Read());
        }

        [Test]
        public void AKeywordTheShaderNoLongerHasRoomForIsReported()
        {
            // Unity does not prune a material's keywords when its shader changes, so the material
            // keeps claiming a keyword that no longer exists and nothing says so. The check is
            // against the shader's effective local keyword space, which is what Unity exposes.
            Drift();

            var body = Read();
            StringAssert.Contains("\"invalidKeywords\":[\"UNIONAIR_COMPAT_ON\"]", body);
        }

        [Test]
        public void APropertyStillDeclaredIsNeitherStaleNorUnset()
        {
            // _Color survives the edit, so it must appear in neither list. Without this the two
            // lists could both be "everything" and every other assertion would still pass.
            Drift();

            var body = Read();
            Assert.IsFalse(body.Contains("\"name\":\"_Color\""),
                "a property the shader still declares was reported as drift: " + body);
        }

        [Test]
        public void APropertyThatKeptItsNameAndChangedItsTypeIsReportedToo()
        {
            // The case a name-only check misses, and the reason this endpoint compares storage
            // against the declaration rather than just asking whether the name survives.
            // Measured on 6000.0.80f1: with _Extra redeclared from Float to Color, m_Floats still
            // holds _Extra at 42, m_Colors gains an _Extra at the declared default, and
            // GetColor("_Extra") answers that default -- so the 42 is exactly as lost as a dropped
            // property's value, while the shader still declares the name.
            Import(ShaderPath, RetypedSource);

            var body = Read();
            StringAssert.Contains("\"comparable\":true", body);
            StringAssert.Contains("\"name\":\"_Extra\",\"storage\":\"Float\",\"value\":42", body);

            // And the live entry is not reported as drift, so the two do not collapse into
            // "everything is stale".
            Assert.IsFalse(
                body.Contains("\"name\":\"_Extra\",\"storage\":\"Color\""),
                "the entry the current declaration reads was reported as unreachable: " + body);
        }

        [Test]
        public void AFloatStrandedByAChangeToIntIsReportedRatherThanExcusedAsIntStorage()
        {
            // An Int declaration reads m_Ints, and m_Floats is accepted for it only when the
            // material has no m_Ints entry of that name -- which is the older serialization, not
            // this. Measured on 6000.0.80f1 with _Extra redeclared from Float to Integer: m_Ints
            // gained _Extra at the declared default, m_Floats still held _Extra at 42, and both
            // GetInteger("_Extra") and GetFloat("_Extra") answered 0. Accepting m_Floats for every
            // Int would take that 42 for the live value and report the pair as agreeing.
            Import(ShaderPath, RetypedToIntSource);

            var body = Read();
            StringAssert.Contains("\"comparable\":true", body);
            StringAssert.Contains("\"name\":\"_Extra\",\"storage\":\"Float\",\"value\":42", body);

            // The entry the Int declaration does read is not drift.
            Assert.IsFalse(
                body.Contains("\"name\":\"_Extra\",\"storage\":\"Int\""),
                "the entry the current declaration reads was reported as unreachable: " + body);
        }

        // ── When the pair cannot be compared ─────────────────────────────────

        [Test]
        public void AShaderUnityCouldNotReadRefusesTheComparisonRatherThanAnsweringIt()
        {
            // The shader Unity substitutes declares no properties, so a comparison would report
            // every value the material carries as stale -- the exact opposite of the truth, and
            // indistinguishable from a real answer. The lists are null together so that
            // 'comparable' is the one thing a client has to check.
            LogAssert.ignoreFailingMessages = true;
            Import(ShaderPath, BrokenSource);

            var body = Read();
            StringAssert.Contains("\"comparable\":false", body);
            StringAssert.Contains("\"reason\":\"shaderNotRead\"", body);
            StringAssert.Contains(
                "\"staleProperties\":null,\"unsetProperties\":null,\"invalidKeywords\":null", body);
        }

        [Test]
        public void AMaterialWhoseShaderAssetIsGoneRefusesForItsOwnReason()
        {
            // Unity replaces the shader with its internal error shader, which carries no error and
            // has a name -- so the ShaderLab test does not catch it and it needs its own. Measured
            // on 6000.0.80f1: name Hidden/InternalErrorShader, ShaderHasError false, zero
            // properties. The shader block still says which shader the material ended up on, which
            // is most of the answer in this state.
            LogAssert.ignoreFailingMessages = true;
            AssetDatabase.DeleteAsset(ShaderPath);

            var body = Read();
            StringAssert.Contains("\"comparable\":false", body);
            StringAssert.Contains("\"reason\":\"shaderMissing\"", body);
            StringAssert.Contains("\"name\":\"Hidden/InternalErrorShader\"", body);
        }

        // ── Errors ───────────────────────────────────────────────────────────

        [Test]
        public void AGuidNamingNoAssetIs404()
        {
            Assert.AreEqual(404, ReadResponse(new string('0', 32)).StatusCode);
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
            Assert.AreEqual(400, ReadResponse(null).StatusCode);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>Rewrites the shader under the material, which is what produces the drift.</summary>
        private static void Drift() => Import(ShaderPath, AfterSource);

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
            new MaterialShaderCompatibilityHandler().Handle(response, guid);
            return response;
        }
    }
}

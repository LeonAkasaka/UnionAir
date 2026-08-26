using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers what <c>GET /api/assets/shaders/{guid}</c> reports for a <c>.shadergraph</c>, which
    /// the endpoint shipped without ever having been read against one.
    /// </summary>
    /// <remarks>
    /// The reference and the handler both used to say that a Shader Graph asset exposes none of its
    /// properties, keywords or passes in readable form. Measured on 6000.0.80f1 with Shader Graph
    /// 17.0.4 and URP 17.0.4, that is false: the importer generates a shader and makes it the
    /// asset's main object, so <c>AssetDatabase.LoadAssetAtPath&lt;Shader&gt;</c> returns it and
    /// every field is answered. These tests are the coverage that claim never had.
    ///
    /// The fixtures are written as source and imported inside the test, the way
    /// <see cref="ShaderReadTests"/>'s are: a committed graph that fails to import would report an
    /// error in every project that opened the package.
    ///
    /// Nothing here references a Shader Graph or URP type, so the package still compiles and
    /// <c>GET /api/help</c> still answers in a project that has neither. The tests skip themselves
    /// instead, because their assertions are about what those packages generate.
    /// </remarks>
    internal sealed class ShaderGraphReadTests
    {
        private const string Dir = "Assets/UnionAirShaderGraphReadTests";
        private const string GraphPath = Dir + "/Readable.shadergraph";
        private const string UnreadableGraphPath = Dir + "/Unreadable.shadergraph";
        private const string UnbuildableGraphPath = Dir + "/Unbuildable.shadergraph";

        // The name is the graph's category joined to the file name, and the file states only the
        // category. That join is why the read is the only way to learn the string a material
        // carries and POST /api/assets/materials takes.
        private const string GraphShaderName = "UnionAir Tests/Readable";

        /// <summary>
        /// The smallest graph Shader Graph accepts here: one URP unlit target and one blackboard
        /// property in one unnamed category, no nodes and no blocks.
        /// </summary>
        /// <remarks>
        /// Small on purpose. A graph saved by the Shader Graph window runs to tens of kilobytes of
        /// node and slot records, none of which this endpoint reads. What it reads is what the
        /// importer generates out of the target and the blackboard, and those are the only two
        /// things here.
        /// </remarks>
        private const string GraphSource = @"{
    ""m_SGVersion"": 3,
    ""m_Type"": ""UnityEditor.ShaderGraph.GraphData"",
    ""m_ObjectId"": ""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1"",
    ""m_Properties"": [ { ""m_Id"": ""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa2"" } ],
    ""m_Keywords"": [],
    ""m_Dropdowns"": [],
    ""m_CategoryData"": [ { ""m_Id"": ""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa5"" } ],
    ""m_Nodes"": [],
    ""m_GroupDatas"": [],
    ""m_StickyNoteDatas"": [],
    ""m_Edges"": [],
    ""m_VertexContext"": { ""m_Position"": { ""x"": 0.0, ""y"": 0.0 }, ""m_Blocks"": [] },
    ""m_FragmentContext"": { ""m_Position"": { ""x"": 0.0, ""y"": 200.0 }, ""m_Blocks"": [] },
    ""m_PreviewData"": { ""serializedMesh"": { ""m_SerializedMesh"": """", ""m_Guid"": """" }, ""preventRotation"": false },
    ""m_Path"": ""UnionAir Tests"",
    ""m_GraphPrecision"": 1,
    ""m_PreviewMode"": 2,
    ""m_OutputNode"": { ""m_Id"": """" },
    ""m_SubDatas"": [],
    ""m_ActiveTargets"": [ { ""m_Id"": ""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa3"" } ]
}

{
    ""m_SGVersion"": 3,
    ""m_Type"": ""UnityEditor.ShaderGraph.Internal.ColorShaderProperty"",
    ""m_ObjectId"": ""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa2"",
    ""m_Guid"": { ""m_GuidSerialized"": ""11111111-2222-3333-4444-555555555555"" },
    ""m_Name"": ""Union Tint"",
    ""m_DefaultRefNameVersion"": 1,
    ""m_RefNameGeneratedByDisplayName"": ""Union Tint"",
    ""m_DefaultReferenceName"": ""_UnionTint"",
    ""m_OverrideReferenceName"": """",
    ""m_GeneratePropertyBlock"": true,
    ""m_UseCustomSlotLabel"": false,
    ""m_CustomSlotLabel"": """",
    ""m_DismissedVersion"": 0,
    ""m_Precision"": 0,
    ""overrideHLSLDeclaration"": false,
    ""hlslDeclarationOverride"": 0,
    ""m_Hidden"": false,
    ""m_PerRendererData"": false,
    ""m_customAttributes"": [],
    ""m_Value"": { ""r"": 0.25, ""g"": 0.5, ""b"": 0.75, ""a"": 1.0 },
    ""isMainColor"": false,
    ""m_ColorMode"": 0
}

{
    ""m_SGVersion"": 0,
    ""m_Type"": ""UnityEditor.ShaderGraph.CategoryData"",
    ""m_ObjectId"": ""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa5"",
    ""m_Name"": """",
    ""m_ChildObjectList"": [ { ""m_Id"": ""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa2"" } ]
}

{
    ""m_SGVersion"": 0,
    ""m_Type"": ""UnityEditor.Rendering.Universal.ShaderGraph.UniversalTarget"",
    ""m_ObjectId"": ""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa3"",
    ""m_ActiveSubTarget"": { ""m_Id"": ""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa4"" },
    ""m_AllowMaterialOverride"": false,
    ""m_SurfaceType"": 0,
    ""m_ZTestMode"": 4,
    ""m_ZWriteControl"": 0,
    ""m_AlphaMode"": 0,
    ""m_RenderFace"": 2,
    ""m_AlphaClip"": false,
    ""m_CastShadows"": true,
    ""m_ReceiveShadows"": true,
    ""m_SupportsLODCrossFade"": false,
    ""m_CustomEditorGUI"": """",
    ""m_SupportVFX"": false
}

{
    ""m_SGVersion"": 0,
    ""m_Type"": ""UnityEditor.Rendering.Universal.ShaderGraph.UniversalUnlitSubTarget"",
    ""m_ObjectId"": ""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa4""
}
";

        /// <summary>Not JSON at all, so the importer throws and produces no object.</summary>
        private const string UnreadableGraphSource = @"{ ""m_SGVersion"": 3, ""m_Type"": ";

        /// <summary>JSON the importer parses and can build nothing out of.</summary>
        private const string UnbuildableGraphSource = "{}";

        [SetUp]
        public void SetUp()
        {
            if (!ShaderGraphWithUniversalTarget)
                Assert.Ignore("needs com.unity.shadergraph and a URP target; this project has neither");

            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "UnionAirShaderGraphReadTests");
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            AssetDatabase.DeleteAsset(Dir);
        }

        // -- What a graph reports ---------------------------------------------

        [Test]
        public void AGraphIsReadUnderTheNameTheImporterGenerated()
        {
            // The claim this endpoint shipped with was that none of this is readable.
            var body = Read(Import(GraphPath, GraphSource));
            StringAssert.Contains("\"name\":\"" + GraphShaderName + "\"", body);
            StringAssert.Contains("\"assetPath\":\"" + GraphPath + "\"", body);
        }

        [Test]
        public void AGraphReportsItsBlackboardPropertyWithTheDefaultAMaterialWouldStartAt()
        {
            StringAssert.Contains(
                "\"name\":\"_UnionTint\",\"type\":\"Color\",\"description\":\"Union Tint\"," +
                "\"defaultValue\":{\"r\":0.25,\"g\":0.5,\"b\":0.75,\"a\":1}",
                Read(Import(GraphPath, GraphSource)));
        }

        [Test]
        public void AGraphReportsThePipelineAndPassesTheImporterGenerated()
        {
            // The target is a URP one, and the generated subshader carries the tag that says so.
            var body = Read(Import(GraphPath, GraphSource));
            StringAssert.Contains("\"renderPipeline\":\"UniversalPipeline\"", body);
            StringAssert.Contains("\"name\":\"Universal Forward\"", body);
        }

        [Test]
        public void AGraphIsReachableByTheNameThatCreatesAMaterial()
        {
            Import(GraphPath, GraphSource);

            var byName = NameResponse(GraphShaderName);
            Assert.AreEqual(200, byName.StatusCode, byName.Body);
            StringAssert.Contains("\"assetPath\":\"" + GraphPath + "\"", byName.Body);
        }

        // -- What a graph that failed reports ---------------------------------

        [Test]
        public void AGraphThatProducedNoShaderReportsTheImportFailureRatherThanDenyingItIsAShader()
        {
            // Before the import log was read, this answered "Asset is not a Shader", which is true
            // of the object Unity holds and misleading about the asset, and left the client with
            // nothing to act on.
            LogAssert.ignoreFailingMessages = true;

            var response = ReadResponse(Import(UnreadableGraphPath, UnreadableGraphSource));

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("failed to import", response.Body);
            StringAssert.Contains("\"hasImportError\":true", response.Body);
            StringAssert.Contains("\"severity\":\"Error\"", response.Body);
        }

        [Test]
        public void AGraphTheImporterCouldNotBuildIsIndistinguishableFromAnEmptyShader()
        {
            // A measured limit, asserted so that it is noticed if it ever stops being true.
            //
            // Measured on 6000.0.80f1 with Shader Graph 17.0.4: a graph the importer parses and
            // cannot build is replaced by ShaderGraphImporter's own error shader, renamed to the
            // name the graph would have had. The substitute compiles cleanly, so the shader
            // compiler has nothing to say, and this path writes nothing to the import log either,
            // so neither diagnostic channel reports it. ShaderProvenance.WasNotRead does not catch
            // it, because the name is not empty and there is no compiler error to pair it with.
            LogAssert.ignoreFailingMessages = true;

            var body = Read(Import(UnbuildableGraphPath, UnbuildableGraphSource));

            // Neither channel. The compiler's, because the substitute compiled:
            StringAssert.Contains("\"hasError\":false", body);
            StringAssert.Contains("\"hasWarnings\":false", body);
            StringAssert.Contains("\"messages\":[]", body);

            // and the importer's, because this path does not write to the import log. This is the
            // assertion the reference's boundary table rests on: without it the documented limit
            // could stop being true and nothing would say so.
            StringAssert.Contains("\"hasImportError\":false", body);
            StringAssert.Contains("\"hasImportWarnings\":false", body);
            StringAssert.Contains("\"importMessages\":[]", body);

            // What is left is a shader-shaped answer describing a shader that does not exist.
            StringAssert.Contains("\"properties\":[]", body);
            StringAssert.Contains("\"passCount\":1", body);
        }

        // -- Helpers ----------------------------------------------------------

        /// <summary>
        /// Whether the packages the fixture's target needs are in the project.
        /// </summary>
        /// <remarks>
        /// Looked up by full type name across the loaded assemblies rather than by an
        /// assembly-qualified name, so the check does not encode which assembly URP ships the type
        /// in. It is a check, not a reference: the package compiles with neither package present.
        /// </remarks>
        private static bool ShaderGraphWithUniversalTarget
        {
            get
            {
                const string target = "UnityEditor.Rendering.Universal.ShaderGraph.UniversalTarget";
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetType(target, false) != null) return true;
                }
                return false;
            }
        }

        private static string Import(string path, string source)
        {
            System.IO.File.WriteAllText(path, source);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.AssetPathToGUID(path);
        }

        private static string Read(string guid)
        {
            var response = ReadResponse(guid);
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
            var request = new FakeRequest("GET", "/api/assets/shaders?name=" + Uri.EscapeDataString(name));
            var response = new FakeResponse();
            new ShaderReadHandler().HandleByName(request, response);
            return response;
        }
    }
}

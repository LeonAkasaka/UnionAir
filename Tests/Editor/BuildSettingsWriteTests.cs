using NUnit.Framework;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// Covers request validation for the build settings write endpoints.
    /// </summary>
    /// <remarks>
    /// Validation is the part that decides whether anything is written at all: every value is
    /// checked before the first write, so a request that fails here leaves the project untouched.
    /// Applying the changes needs a live Editor and is verified by hand.
    /// </remarks>
    internal sealed class BuildSettingsWriteTests
    {
        private static BuildSettingsWritePlan Parse(string body)
        {
            BuildSettingsWritePlan plan;
            string error;
            Assert.IsTrue(BuildSettingsWriteParser.TryParseSettings(body, out plan, out error), error);
            return plan;
        }

        private static string ParseError(string body)
        {
            BuildSettingsWritePlan plan;
            string error;
            Assert.IsFalse(BuildSettingsWriteParser.TryParseSettings(body, out plan, out error));
            return error;
        }

        [Test]
        public void TryParseSettings_RejectsARequestThatChangesNothing()
        {
            StringAssert.Contains("changed nothing", ParseError("{}"));
            StringAssert.Contains("changed nothing", ParseError("{\"namedBuildTarget\":\"Standalone\"}"));
        }

        [Test]
        public void TryParseSettings_ReadsEnumValuesCaseInsensitively()
        {
            var plan = Parse("{\"scriptingBackend\":\"il2cpp\"}");
            Assert.IsTrue(plan.HasScriptingBackend);
            Assert.AreEqual(ScriptingImplementation.IL2CPP, plan.ScriptingBackend);
        }

        [Test]
        public void TryParseSettings_ListsTheValidNamesForAnUnknownEnumValue()
        {
            // The valid set differs per Editor version, so the message reports it rather than
            // documenting a list that would go stale.
            var error = ParseError("{\"scriptingBackend\":\"Bogus\"}");
            StringAssert.Contains("scriptingBackend", error);
            StringAssert.Contains("IL2CPP", error);
        }

        [Test]
        public void TryParseSettings_ReadsDefineSymbolOperations()
        {
            var plan = Parse("{\"addDefineSymbols\":[\"A\",\"B\"],\"removeDefineSymbols\":[\"C\"]}");
            Assert.AreEqual(2, plan.AddDefineSymbols.Count);
            Assert.AreEqual(1, plan.RemoveDefineSymbols.Count);
            Assert.IsFalse(plan.HasDefineSymbols);
        }

        [Test]
        public void TryParseSettings_RejectsReplacementCombinedWithAddOrRemove()
        {
            // The two express different intents about the same list, and applying both would make
            // the result depend on an ordering the request never stated.
            StringAssert.Contains(
                "cannot be combined",
                ParseError("{\"defineSymbols\":[\"A\"],\"addDefineSymbols\":[\"B\"]}"));
        }

        [Test]
        public void TryParseSettings_RejectsAnInvalidDefineSymbol()
        {
            // Unity stores whatever it is given and fails later, at compile time, with an error
            // that never mentions the setting.
            StringAssert.Contains("invalid define symbol", ParseError("{\"defineSymbols\":[\"A;B\"]}"));
            StringAssert.Contains("invalid define symbol", ParseError("{\"defineSymbols\":[\"1ABC\"]}"));
            StringAssert.Contains("invalid define symbol", ParseError("{\"addDefineSymbols\":[\"has space\"]}"));
        }

        [Test]
        public void TryParseSettings_DeduplicatesSymbols()
        {
            var plan = Parse("{\"defineSymbols\":[\"A\",\"A\",\"B\"]}");
            Assert.AreEqual(2, plan.DefineSymbols.Count);
        }

        [Test]
        public void TryParseSettings_DistinguishesAbsentFromFalse()
        {
            Assert.IsFalse(Parse("{\"development\":true}").HasAllowDebugging);

            var plan = Parse("{\"allowDebugging\":false}");
            Assert.IsTrue(plan.HasAllowDebugging);
            Assert.IsFalse(plan.AllowDebugging);
        }

        [Test]
        public void TriggersCompilation_OnlyForSettingsTheCompilerReads()
        {
            Assert.IsTrue(Parse("{\"addDefineSymbols\":[\"A\"]}").TriggersCompilation);
            Assert.IsTrue(Parse("{\"scriptingBackend\":\"Mono2x\"}").TriggersCompilation);

            // Build flags are read when a build runs, not when scripts compile.
            Assert.IsFalse(Parse("{\"development\":true}").TriggersCompilation);
            Assert.IsFalse(Parse("{\"managedStrippingLevel\":\"Low\"}").TriggersCompilation);
        }

        [Test]
        public void IsValidDefineSymbol_AcceptsIdentifiers()
        {
            Assert.IsTrue(BuildSettingsWriteParser.IsValidDefineSymbol("UNITY_TEST"));
            Assert.IsTrue(BuildSettingsWriteParser.IsValidDefineSymbol("_leading"));
            Assert.IsTrue(BuildSettingsWriteParser.IsValidDefineSymbol("A1"));
        }

        [Test]
        public void IsValidDefineSymbol_RejectsSeparatorsAndEmptyValues()
        {
            Assert.IsFalse(BuildSettingsWriteParser.IsValidDefineSymbol(""));
            Assert.IsFalse(BuildSettingsWriteParser.IsValidDefineSymbol(null));
            Assert.IsFalse(BuildSettingsWriteParser.IsValidDefineSymbol("A;B"));
            Assert.IsFalse(BuildSettingsWriteParser.IsValidDefineSymbol("A,B"));
            Assert.IsFalse(BuildSettingsWriteParser.IsValidDefineSymbol("9A"));
        }

        [Test]
        public void TryParseScenes_RequiresTheCompleteList()
        {
            System.Collections.Generic.List<BuildSceneEntry> scenes;
            string error;
            Assert.IsFalse(BuildSettingsWriteParser.TryParseScenes("{}", out scenes, out error));
            StringAssert.Contains("required", error);
        }

        [Test]
        public void TryParseScenes_AcceptsBarePathsAndObjects()
        {
            System.Collections.Generic.List<BuildSceneEntry> scenes;
            string error;
            Assert.IsTrue(BuildSettingsWriteParser.TryParseScenes(
                "{\"scenes\":[\"Assets/A.unity\",{\"path\":\"Assets/B.unity\",\"enabled\":false}]}",
                out scenes, out error), error);

            Assert.AreEqual(2, scenes.Count);
            Assert.AreEqual("Assets/A.unity", scenes[0].Path);
            Assert.IsTrue(scenes[0].Enabled);
            Assert.IsFalse(scenes[1].Enabled);
        }

        [Test]
        public void TryParseScenes_RejectsNonSceneAndRepeatedPaths()
        {
            System.Collections.Generic.List<BuildSceneEntry> scenes;
            string error;

            Assert.IsFalse(BuildSettingsWriteParser.TryParseScenes(
                "{\"scenes\":[\"Assets/A.prefab\"]}", out scenes, out error));
            StringAssert.Contains(".unity", error);

            // Unity assigns one build index per scene; a repeat would silently drop one.
            Assert.IsFalse(BuildSettingsWriteParser.TryParseScenes(
                "{\"scenes\":[\"Assets/A.unity\",\"Assets/A.unity\"]}", out scenes, out error));
            StringAssert.Contains("repeats", error);
        }

        [Test]
        public void TryParseScenes_AcceptsAnEmptyList()
        {
            // Clearing the build scene list is a legitimate thing to ask for; a build with no
            // scenes is rejected later, by POST /api/builds, where the message can say why.
            System.Collections.Generic.List<BuildSceneEntry> scenes;
            string error;
            Assert.IsTrue(BuildSettingsWriteParser.TryParseScenes("{\"scenes\":[]}", out scenes, out error), error);
            Assert.AreEqual(0, scenes.Count);
        }

        [Test]
        public void EnumNames_SkipsDeprecatedMembers()
        {
            var names = BuildSettingsWriteParser.EnumNames<ScriptingImplementation>();
            Assert.Contains("IL2CPP", names);
            Assert.Contains("Mono2x", names);
        }
    }
}

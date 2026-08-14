using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor.Tests
{
    /// <summary>
    /// The same key accounting as <see cref="ComponentPropertyKeyTests"/>, on the second write
    /// path. It runs its own loop over a different iteration, so a fix applied only to components
    /// leaves this endpoint answering 200 for writes that did not happen.
    /// </summary>
    internal sealed class ScriptableObjectPropertyKeyTests
    {
        private const string Dir = "Assets/UnionAirPropertyKeyTests";
        private const string AssetPath = Dir + "/Fixture.asset";
        private const string CreatedAssetPath = Dir + "/Created.asset";

        private string _guid;
        private UnionAirPropertyKeyFixture _asset;

        [SetUp]
        public void CreateAsset()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "UnionAirPropertyKeyTests");

            AssetDatabase.DeleteAsset(AssetPath);
            AssetDatabase.DeleteAsset(CreatedAssetPath);
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<UnionAirPropertyKeyFixture>(), AssetPath);
            AssetDatabase.SaveAssets();

            _guid = AssetDatabase.AssetPathToGUID(AssetPath);
            _asset = AssetDatabase.LoadAssetAtPath<UnionAirPropertyKeyFixture>(AssetPath);
        }

        [TearDown]
        public void DeleteAsset()
        {
            AssetDatabase.DeleteAsset(Dir);
        }

        [Test]
        public void AKeyNamingNoPropertyIsRejected()
        {
            var response = Patch("{\"properties\":{\"dispalyName\":\"typo\"}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("dispalyName", response.Body);
            Assert.AreEqual("start", _asset.displayName);
        }

        [Test]
        public void AValueOfTheWrongTypeIsRejected()
        {
            var response = Patch("{\"properties\":{\"cooldown\":\"2.5\"}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("JSON number", response.Body);
            Assert.AreEqual(1f, _asset.cooldown);
        }

        [Test]
        public void AStringPropertyRejectsANonStringValue()
        {
            var response = Patch("{\"properties\":{\"displayName\":123}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("JSON string", response.Body);
            Assert.AreEqual("start", _asset.displayName);
        }

        [Test]
        public void ACompositeMemberOfTheWrongTypeIsRejected()
        {
            var response = Patch("{\"properties\":{\"offset\":{\"x\":\"9\"}}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("offset.x", response.Body);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), _asset.offset);
        }

        [Test]
        public void AnEmptyCompositeObjectIsRejected()
        {
            var response = Patch("{\"properties\":{\"offset\":{}}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("at least", response.Body);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), _asset.offset);
        }

        [Test]
        public void AnObjectReferenceWithAnUnknownMemberIsRejected()
        {
            var response = Patch(
                "{\"properties\":{\"reference\":{\"assetPath\":\"" + AssetPath +
                "\",\"typo\":1}}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("typo", response.Body);
            Assert.IsNull(_asset.reference);
        }

        [Test]
        public void AnObjectReferenceWithADuplicateMemberIsRejected()
        {
            var response = Patch(
                "{\"properties\":{\"reference\":{\"assetPath\":\"" + AssetPath +
                "\",\"assetPath\":\"" + AssetPath + "\"}}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("Duplicate field 'assetPath'", response.Body);
            Assert.IsNull(_asset.reference);
        }

        [Test]
        public void ACompositeObjectCanPatchOneMember()
        {
            var response = Patch("{\"properties\":{\"offset\":{\"x\":9}}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.AreEqual(new Vector3(9f, 2f, 3f), _asset.offset);
        }

        [Test]
        public void ADuplicatePropertyKeyIsRejected()
        {
            var response = Patch("{\"properties\":{\"cooldown\":2.5,\"cooldown\":\"bad\"}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("Duplicate field 'cooldown'", response.Body);
            Assert.AreEqual(1f, _asset.cooldown);
        }

        // ── The three array addresses ────────────────────────────────────────

        [Test]
        public void AWholeArrayIsReplaced()
        {
            var response = Patch("{\"properties\":{\"tags\":[\"fire\",\"aoe\"]}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"tags\"", response.Body);
            CollectionAssert.AreEqual(new[] { "fire", "aoe" }, _asset.tags);
        }

        [Test]
        public void AnArrayElementIsWrittenInPlace()
        {
            // This endpoint walks top-level properties only, so it never reached an element path
            // and reported one as naming nothing. The address resolves through the array instead.
            Patch("{\"properties\":{\"tags\":[\"fire\",\"aoe\"]}}");

            var response = Patch("{\"properties\":{\"tags.Array.data[1]\":\"single\"}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            CollectionAssert.AreEqual(new[] { "fire", "single" }, _asset.tags);
        }

        [Test]
        public void AnArraySizeResizes()
        {
            var response = Patch("{\"properties\":{\"tags.Array.size\":3}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.AreEqual(3, _asset.tags.Length);
        }

        [Test]
        public void AnObjectReferenceElementResolvesAnAsset()
        {
            var response = Patch(
                "{\"properties\":{\"references\":[{\"assetPath\":\"" + AssetPath + "\"}]}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.AreEqual(1, _asset.references.Length);
            Assert.AreSame(_asset, _asset.references[0]);
        }

        [Test]
        public void AnArrayOfUnwritableElementsIsRejectedForWhatItHolds()
        {
            var response = Patch("{\"properties\":{\"entries\":[]}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("entries", response.Body);
            StringAssert.Contains("cannot write", response.Body);
            Assert.AreEqual(1, _asset.entries.Length, "clearing must not have reached the asset");
        }

        [Test]
        public void AnArrayOfUnwritableElementsIsRejectedForWhatAResizeWouldGiveIt()
        {
            // Empty, so there is nothing to inspect until the resize has happened. Refusing only
            // on the way in would let an empty array of unwritable elements be filled.
            var response = Patch("{\"properties\":{\"spares\":[{\"hp\":1}]}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("spares", response.Body);
            StringAssert.Contains("cannot write", response.Body);
            Assert.AreEqual(0, _asset.spares.Length);
        }

        [Test]
        public void AnUnwritableElementTypeIsRefusedThroughItsLengthToo()
        {
            var response = Patch("{\"properties\":{\"spares.Array.size\":2}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            Assert.AreEqual(0, _asset.spares.Length);
        }

        [Test]
        public void ANestedKeyDoesNotSelectAnotherProperty()
        {
            // "cooldown" appears only inside the value of another field.
            var response = Patch("{\"properties\":{\"displayName\":\"{\\\"cooldown\\\":9}\"}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.AreEqual(1f, _asset.cooldown, "the nested name must not have been written");
        }

        [Test]
        public void ATopLevelKeyStillWrites()
        {
            var response = Patch("{\"properties\":{\"displayName\":\"Fireball\",\"cooldown\":2.5}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            Assert.AreEqual("Fireball", _asset.displayName);
            Assert.AreEqual(2.5f, _asset.cooldown);
        }

        [Test]
        public void WritingTheScriptIsRejected()
        {
            // m_Script exists on a ScriptableObject and on a MonoBehaviour, and on no built-in
            // component -- so this is the path where refusing it is reachable at all.
            var response = Patch("{\"properties\":{\"m_Script\":null}}");

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("cannot be written", response.Body);
        }

        [Test]
        public void AnEmptyPropertiesObjectIsAccepted()
        {
            var response = Patch("{\"properties\":{}}");

            Assert.AreEqual(200, response.StatusCode, response.Body);
            StringAssert.Contains("\"updated\":[]", response.Body);
        }

        [Test]
        public void CreateRejectsInvalidInitialPropertiesWithoutCreatingAnAsset()
        {
            var transientCountBefore = CountTransientFixtures();
            var body = "{\"typeName\":\"" + typeof(UnionAirPropertyKeyFixture).FullName +
                       "\",\"assetPath\":\"" + CreatedAssetPath +
                       "\",\"properties\":{\"entries\":[{\"hp\":1}]}}";
            var request = new FakeRequest("POST", "/api/assets/scriptableobjects")
                .WithJsonBody(body);
            var response = new FakeResponse();

            new ScriptableObjectWriteHandler().Handle(request, response);

            Assert.AreEqual(400, response.StatusCode, response.Body);
            StringAssert.Contains("array", response.Body);
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<UnionAirPropertyKeyFixture>(CreatedAssetPath));
            Assert.AreEqual(
                transientCountBefore,
                CountTransientFixtures(),
                "a rejected create must destroy its unsaved ScriptableObject instance");
        }

        private static int CountTransientFixtures()
        {
            var count = 0;
            foreach (var instance in Resources.FindObjectsOfTypeAll<UnionAirPropertyKeyFixture>())
            {
                if (!EditorUtility.IsPersistent(instance)) count++;
            }
            return count;
        }

        private FakeResponse Patch(string body)
        {
            var request = new FakeRequest(
                    "PATCH",
                    "/api/assets/scriptableobjects?guid=" + Uri.EscapeDataString(_guid))
                .WithJsonBody(body);
            var response = new FakeResponse();

            new ScriptableObjectWriteHandler().Handle(request, response);
            return response;
        }
    }
}

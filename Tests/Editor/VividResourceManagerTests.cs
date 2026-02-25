using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VividRP.Runtime.Utility;

namespace VividRP.Tests.Editor
{
    public class VividResourceManagerTests
    {
        [SetUp]
        public void SetUp()
        {
            ResetResourceManagerState();
        }

        [TearDown]
        public void TearDown()
        {
            ResetResourceManagerState();
        }

        [Test]
        public void Initialize_AssignsShaderFields_WhenResourcePathIsValid()
        {
            VividResourceManager.Initialize();

            Assert.That(VividResources.BlitShader, Is.Not.Null);
            Assert.That(VividResources.FullScreenUVShader, Is.Not.Null);
        }

        [Test]
        public void Initialize_IsIdempotent_WhenCalledMultipleTimes()
        {
            VividResourceManager.Initialize();
            var firstBlitShader = VividResources.BlitShader;

            VividResourceManager.Initialize();

            Assert.That(VividResources.BlitShader, Is.SameAs(firstBlitShader));
        }

        [Test]
        public void LoadAndAssignField_LogsError_WhenRequiredResourceMissing()
        {
            var managerType = typeof(VividResourceManager);
            var method = managerType.GetMethod(
                "LoadAndAssignField",
                BindingFlags.NonPublic | BindingFlags.Static);
            var field = typeof(TestResourceContainer).GetField(
                nameof(TestResourceContainer.MissingRequired),
                BindingFlags.Public | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);
            Assert.That(field, Is.Not.Null);

            LogAssert.Expect(
                LogType.Error,
                "[VividRP] Failed to load required resource 'VividRP/DefinitelyMissingResource' for field 'VividRP.Tests.Editor.VividResourceManagerTests+TestResourceContainer.MissingRequired'.");

            var attribute = new ResourcePathAttribute("VividRP/DefinitelyMissingResource");
            method.Invoke(null, new object[] { field, attribute });
        }

        [Test]
        public void Get_LoadsTextAssetFromResources_WhenPathExists()
        {
            var textAsset = VividResourceManager.Get<TextAsset>("VividRP/VividResourceManagerTestsText");

            Assert.That(textAsset, Is.Not.Null);
            Assert.That(textAsset.text, Is.EqualTo("vividrp-resource-test"));
        }

        private static void ResetResourceManagerState()
        {
            var managerType = typeof(VividResourceManager);
            var initializedField = managerType.GetField("s_Initialized", BindingFlags.NonPublic | BindingFlags.Static);
            var cacheField = managerType.GetField("s_LoadedResources", BindingFlags.NonPublic | BindingFlags.Static);

            initializedField?.SetValue(null, false);

            if (cacheField?.GetValue(null) is IDictionary<string, UnityEngine.Object> cache)
                cache.Clear();

            VividResources.BlitShader = null;
            VividResources.FullScreenUVShader = null;
            TestResourceContainer.MissingRequired = null;
        }

        private static class TestResourceContainer
        {
            public static TextAsset MissingRequired;
        }
    }
}

using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public class VividRenderPipelineAssetGpuDrivenTests
    {
        [Test]
        public void Asset_DefaultsToGpuDrivenDisabled()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();

            try
            {
                Assert.That(asset.EnableGPUDriven, Is.False);
                Assert.That(asset.EnableGPUDrivenDebugOverlay, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void SerializedObject_UpdatesGpuDrivenProperty()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();

            try
            {
                var serializedObject = new SerializedObject(asset);
                var property = serializedObject.FindProperty("m_EnableGPUDriven");
                var debugOverlayProperty = serializedObject.FindProperty("m_EnableGPUDrivenDebugOverlay");

                Assert.That(property, Is.Not.Null);
                Assert.That(debugOverlayProperty, Is.Not.Null);

                property.boolValue = true;
                debugOverlayProperty.boolValue = true;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(asset.EnableGPUDriven, Is.True);
                Assert.That(asset.EnableGPUDrivenDebugOverlay, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}

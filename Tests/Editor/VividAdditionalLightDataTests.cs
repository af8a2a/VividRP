using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class VividAdditionalLightDataTests
    {
        private GameObject m_GameObject;

        [SetUp]
        public void SetUp()
        {
            RuntimeHelpers.RunClassConstructor(typeof(VividAdditionalLightDataEditorUtility).TypeHandle);
            m_GameObject = new GameObject("Vivid Light Test");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_GameObject);
        }

        [Test]
        public void GetVividAdditionalLightData_AddsComponent_WhenMissing()
        {
            var light = m_GameObject.AddComponent<Light>();

            var additionalData = light.GetVividAdditionalLightData();

            Assert.That(additionalData, Is.Not.Null);
            Assert.That(light.GetComponent<VividAdditionalLightData>(), Is.SameAs(additionalData));
        }

        [Test]
        public void VividSerializedLight_ExposesAdditionalLightProperties_WhenLightIsWrapped()
        {
            var light = m_GameObject.AddComponent<Light>();
            var serializedObject = new SerializedObject(light);
            var serializedLight = new VividSerializedLight(serializedObject);

            Assert.That(serializedLight.lightsAdditionalData, Has.Length.EqualTo(1));
            Assert.That(serializedLight.usePipelineSettings, Is.Not.Null);
            Assert.That(serializedLight.customShadowLayers, Is.Not.Null);
            Assert.That(serializedLight.shadowRenderingLayers, Is.Not.Null);
        }

        [Test]
        public void ObjectFactory_AddsAdditionalLightData_WhenLightComponentIsCreated()
        {
            ObjectFactory.AddComponent<Light>(m_GameObject);

            var additionalData = m_GameObject.GetComponent<VividAdditionalLightData>();

            Assert.That(additionalData, Is.Not.Null);
            Assert.That((additionalData.hideFlags & HideFlags.HideInInspector) != 0, Is.True);
        }

        [Test]
        public void NormalizeUnsupportedLightUnit_ResetsDirectionalLightsToLux()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.lightUnit = LightUnit.Candela;
            light.luxAtDistance = 12.0f;

            VividLightIntensityUnitUtility.NormalizeUnsupportedLightUnit(light);

            Assert.That(light.lightUnit, Is.EqualTo(LightUnit.Lux));
            Assert.That(light.luxAtDistance, Is.EqualTo(1.0f));
        }

        [Test]
        public void NormalizeUnsupportedLightUnit_PreservesSupportedLuxOnPointLights()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.lightUnit = LightUnit.Lux;
            light.luxAtDistance = 5.0f;

            VividLightIntensityUnitUtility.NormalizeUnsupportedLightUnit(light);

            Assert.That(light.lightUnit, Is.EqualTo(LightUnit.Lux));
            Assert.That(light.luxAtDistance, Is.EqualTo(5.0f));
        }
    }
}

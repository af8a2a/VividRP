using System.Runtime.CompilerServices;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class VividAdditionalLightDataTests
    {
        private static readonly MethodInfo s_LateUpdateMethod =
            typeof(VividAdditionalLightData).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

        private GameObject m_GameObject;

        [SetUp]
        public void SetUp()
        {
            RuntimeHelpers.RunClassConstructor(typeof(VividAdditionalLightDataEditorUtility).TypeHandle);
            VividLightRenderDatabase.instance.Clear();
            m_GameObject = new GameObject("Vivid Light Test");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_GameObject);
            VividLightRenderDatabase.instance.Clear();
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
            var light = ObjectFactory.AddComponent<Light>(m_GameObject);

            var additionalData = m_GameObject.GetComponent<VividAdditionalLightData>();

            Assert.That(additionalData, Is.Not.Null);
            Assert.That((additionalData.hideFlags & HideFlags.HideInInspector) != 0, Is.True);
            Assert.That(light.lightUnit, Is.EqualTo(LightUnit.Lumen));
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
        [TestCase(LightUnit.Lux)]
        [TestCase(LightUnit.Lumen)]
        [TestCase(LightUnit.Candela)]
        [TestCase(LightUnit.Ev100)]
        public void NormalizeUnsupportedLightUnit_PreservesSupportedUnitsOnPointLights(LightUnit unit)
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.lightUnit = unit;
            light.luxAtDistance = 5.0f;

            VividLightIntensityUnitUtility.NormalizeUnsupportedLightUnit(light);

            Assert.That(light.lightUnit, Is.EqualTo(unit));
            Assert.That(light.luxAtDistance, Is.EqualTo(5.0f));
        }

        [Test]
        public void AdditionalLightDataPropertySetters_UpdateTrackedLightRenderData_WhenShadowOverridesChange()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.renderingLayerMask = 9;

            var additionalData = light.GetVividAdditionalLightData();

            VividLightRenderDatabase.instance.Clear();

            additionalData.usePipelineSettings = false;
            additionalData.customShadowLayers = true;
            additionalData.shadowRenderingLayers = (RenderingLayerMask)23u;

            Assert.That(VividLightRenderDatabase.instance.TryGetLightData(light, out var trackedLightData), Is.True);
            Assert.That(trackedLightData.shadowRenderingLayerMask, Is.EqualTo(23u));
            Assert.That(trackedLightData.renderingLayerMask, Is.EqualTo(9u));
            Assert.That((trackedLightData.flags & VividLightRenderDataFlags.UsePipelineSettings) != 0, Is.False);
            Assert.That((trackedLightData.flags & VividLightRenderDataFlags.CustomShadowLayers) != 0, Is.True);
        }

        [Test]
        public void UpdateLightData_RefreshesTrackedSnapshot_WhenLightPropertiesChange()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Color.red;
            light.intensity = 1.5f;
            light.range = 4.0f;
            light.transform.position = new Vector3(1.0f, 2.0f, 3.0f);

            var additionalData = light.GetVividAdditionalLightData();
            var database = VividLightRenderDatabase.instance;

            database.Clear();
            database.UpdateLightData(light, additionalData);

            light.color = Color.cyan;
            light.intensity = 3.0f;
            light.range = 8.0f;
            light.transform.position = new Vector3(-2.0f, 1.0f, 0.5f);
            light.transform.forward = Vector3.right;

            var trackedLightData = database.UpdateLightData(light, additionalData);

            Assert.That(database.lightCount, Is.EqualTo(1));
            Assert.That(database.TryGetLightData(light.GetEntityId(), out var trackedLightDataByEntity), Is.True);
            Assert.That(trackedLightDataByEntity.lightEntityId, Is.EqualTo(light.GetEntityId()));
            Assert.That(trackedLightData.positionWS.x, Is.EqualTo(-2.0f).Within(0.0001f));
            Assert.That(trackedLightData.positionWS.y, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(trackedLightData.positionWS.z, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(trackedLightData.forwardWS.x, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(trackedLightData.forwardWS.y, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(trackedLightData.forwardWS.z, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(trackedLightData.color.x, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(trackedLightData.color.y, Is.EqualTo(3.0f).Within(0.0001f));
            Assert.That(trackedLightData.color.z, Is.EqualTo(3.0f).Within(0.0001f));
            Assert.That(trackedLightData.range, Is.EqualTo(8.0f).Within(0.0001f));
            Assert.That(trackedLightData.inverseRangeSquared, Is.EqualTo(1.0f / 64.0f).Within(0.0001f));
        }

        [Test]
        public void OnDisable_UnregistersTrackedLight_WhenAdditionalLightDataIsDisabled()
        {
            var light = m_GameObject.AddComponent<Light>();
            var additionalData = light.GetVividAdditionalLightData();
            var database = VividLightRenderDatabase.instance;

            database.Clear();
            database.RegisterLight(additionalData);

            Assert.That(database.lightCount, Is.EqualTo(1));

            additionalData.enabled = false;

            Assert.That(database.lightCount, Is.Zero);
        }

        [Test]
        public void LateUpdate_DoesNotRefreshTrackedLightData_WhenLightIsNotAnimated()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Color.white;
            light.intensity = 1.0f;

            var additionalData = light.GetVividAdditionalLightData();
            var database = VividLightRenderDatabase.instance;

            database.Clear();
            database.UpdateLightData(light, additionalData);

            light.intensity = 4.0f;

            InvokeLateUpdate(additionalData);

            Assert.That(database.TryGetLightData(light, out var trackedLightData), Is.True);
            Assert.That(trackedLightData.intensity, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(trackedLightData.color.x, Is.EqualTo(1.0f).Within(0.0001f));
        }

        [Test]
        public void LateUpdate_RefreshesTrackedLightData_WhenLightHasAnimator()
        {
            m_GameObject.AddComponent<Animator>();

            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Color.white;
            light.intensity = 1.0f;
            light.range = 4.0f;

            var additionalData = light.GetVividAdditionalLightData();
            var database = VividLightRenderDatabase.instance;

            database.Clear();
            database.UpdateLightData(light, additionalData);

            light.intensity = 4.0f;
            light.range = 8.0f;

            InvokeLateUpdate(additionalData);

            Assert.That(database.TryGetLightData(light, out var trackedLightData), Is.True);
            Assert.That(trackedLightData.intensity, Is.EqualTo(4.0f).Within(0.0001f));
            Assert.That(trackedLightData.color.x, Is.EqualTo(4.0f).Within(0.0001f));
            Assert.That(trackedLightData.range, Is.EqualTo(8.0f).Within(0.0001f));
        }

        private static void InvokeLateUpdate(VividAdditionalLightData additionalData)
        {
            Assert.That(s_LateUpdateMethod, Is.Not.Null);
            s_LateUpdateMethod.Invoke(additionalData, null);
        }
    }
}

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
            Assert.That(serializedLight.enableRayTracedShadow, Is.Not.Null);
            Assert.That(serializedLight.rayTracedShadowRayLength, Is.Not.Null);
            Assert.That(serializedLight.rayTracedShadowRayBias, Is.Not.Null);
            Assert.That(serializedLight.rayTracedShadowDistantRayBias, Is.Not.Null);
            Assert.That(serializedLight.rayTracedShadowSunAngularDiameter, Is.Not.Null);
            Assert.That(serializedLight.interactsWithSky, Is.Not.Null);
            Assert.That(serializedLight.angularDiameter, Is.Not.Null);
            Assert.That(serializedLight.diameterMultiplierMode, Is.Not.Null);
            Assert.That(serializedLight.diameterMultiplier, Is.Not.Null);
            Assert.That(serializedLight.diameterOverride, Is.Not.Null);
            Assert.That(serializedLight.celestialBodyShadingSource, Is.Not.Null);
            Assert.That(serializedLight.sunLightOverride, Is.Not.Null);
            Assert.That(serializedLight.sunColor, Is.Not.Null);
            Assert.That(serializedLight.sunIntensity, Is.Not.Null);
            Assert.That(serializedLight.moonPhase, Is.Not.Null);
            Assert.That(serializedLight.moonPhaseRotation, Is.Not.Null);
            Assert.That(serializedLight.earthshine, Is.Not.Null);
            Assert.That(serializedLight.flareSize, Is.Not.Null);
            Assert.That(serializedLight.flareTint, Is.Not.Null);
            Assert.That(serializedLight.flareFalloff, Is.Not.Null);
            Assert.That(serializedLight.flareMultiplier, Is.Not.Null);
            Assert.That(serializedLight.surfaceTexture, Is.Not.Null);
            Assert.That(serializedLight.surfaceTint, Is.Not.Null);
            Assert.That(serializedLight.distance, Is.Not.Null);
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
        public void RayTracedShadowSettings_DefaultToExpectedValues_OnDirectionalLights()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Directional;

            var additionalData = light.GetVividAdditionalLightData();

            Assert.That(additionalData.enableRayTracedShadow, Is.False);
            Assert.That(
                additionalData.rayTracedShadowRayLength,
                Is.EqualTo(VividAdditionalLightData.DefaultRayTracedShadowRayLength));
            Assert.That(
                additionalData.rayTracedShadowRayBias,
                Is.EqualTo(VividAdditionalLightData.DefaultRayTracedShadowRayBias));
            Assert.That(
                additionalData.rayTracedShadowDistantRayBias,
                Is.EqualTo(VividAdditionalLightData.DefaultRayTracedShadowDistantRayBias));
            Assert.That(
                additionalData.rayTracedShadowSunAngularDiameter,
                Is.EqualTo(VividAdditionalLightData.DefaultRayTracedShadowSunAngularDiameter));
            Assert.That(additionalData.supportsRayTracedShadow, Is.True);
            Assert.That(additionalData.isRayTracedShadowActive, Is.False);
        }

        [Test]
        public void RayTracedShadowSettings_RemainSerializedButInactive_OnNonDirectionalLights()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Point;

            var additionalData = light.GetVividAdditionalLightData();
            additionalData.enableRayTracedShadow = true;
            additionalData.rayTracedShadowRayLength = 24.0f;
            additionalData.rayTracedShadowRayBias = 0.02f;
            additionalData.rayTracedShadowDistantRayBias = 0.08f;
            additionalData.rayTracedShadowSunAngularDiameter = 1.2f;

            Assert.That(additionalData.enableRayTracedShadow, Is.True);
            Assert.That(additionalData.rayTracedShadowRayLength, Is.EqualTo(24.0f));
            Assert.That(additionalData.rayTracedShadowRayBias, Is.EqualTo(0.02f));
            Assert.That(additionalData.rayTracedShadowDistantRayBias, Is.EqualTo(0.08f));
            Assert.That(additionalData.rayTracedShadowSunAngularDiameter, Is.EqualTo(1.2f));
            Assert.That(additionalData.supportsRayTracedShadow, Is.False);
            Assert.That(additionalData.isRayTracedShadowActive, Is.False);
        }

        [Test]
        public void CelestialBodySettings_DefaultToExpectedValues_OnDirectionalLights()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Directional;

            var additionalData = light.GetVividAdditionalLightData();

            Assert.That(additionalData.interactsWithSky, Is.True);
            Assert.That(
                additionalData.angularDiameter,
                Is.EqualTo(VividAdditionalLightData.DefaultCelestialBodyAngularDiameter));
            Assert.That(additionalData.diameterMultiplierMode, Is.False);
            Assert.That(additionalData.diameterMultiplier, Is.EqualTo(1.0f));
            Assert.That(
                additionalData.diameterOverride,
                Is.EqualTo(VividAdditionalLightData.DefaultCelestialBodyAngularDiameter));
            Assert.That(
                additionalData.celestialBodyShadingSource,
                Is.EqualTo(VividAdditionalLightData.CelestialBodyShadingSource.Emission));
            Assert.That(additionalData.sunLightOverride, Is.Null);
            Assert.That(additionalData.sunColor, Is.EqualTo(Color.white));
            Assert.That(
                additionalData.sunIntensity,
                Is.EqualTo(VividAdditionalLightData.DefaultManualSunIntensity));
            Assert.That(additionalData.moonPhase, Is.EqualTo(0.2f));
            Assert.That(additionalData.moonPhaseRotation, Is.EqualTo(0.0f));
            Assert.That(additionalData.earthshine, Is.EqualTo(1.0f));
            Assert.That(additionalData.flareSize, Is.EqualTo(2.0f));
            Assert.That(additionalData.flareTint, Is.EqualTo(Color.white));
            Assert.That(additionalData.flareFalloff, Is.EqualTo(4.0f));
            Assert.That(additionalData.flareMultiplier, Is.EqualTo(1.0f));
            Assert.That(additionalData.surfaceTexture, Is.Null);
            Assert.That(additionalData.surfaceTint, Is.EqualTo(Color.white));
            Assert.That(
                additionalData.distance,
                Is.EqualTo(VividAdditionalLightData.DefaultCelestialBodyDistance));
        }

        [Test]
        public void VividLightEditor_ShowsDirectionalRayTracedShadowControls_OnlyForDirectionalLights()
        {
            var directionalLight = m_GameObject.AddComponent<Light>();
            directionalLight.type = LightType.Directional;

            var serializedDirectionalLight = new VividSerializedLight(new SerializedObject(directionalLight));

            Assert.That(
                VividLightEditor.ShouldShowDirectionalRayTracedShadowControls(serializedDirectionalLight),
                Is.True);
            Assert.That(
                VividLightEditor.ShouldExpandDirectionalRayTracedShadowControls(serializedDirectionalLight),
                Is.False);

            serializedDirectionalLight.enableRayTracedShadow.boolValue = true;

            Assert.That(
                VividLightEditor.ShouldExpandDirectionalRayTracedShadowControls(serializedDirectionalLight),
                Is.True);

            var pointLightObject = new GameObject("Vivid Point Light Test");

            try
            {
                var pointLight = pointLightObject.AddComponent<Light>();
                pointLight.type = LightType.Point;

                var serializedPointLight = new VividSerializedLight(new SerializedObject(pointLight));

                Assert.That(
                    VividLightEditor.ShouldShowDirectionalRayTracedShadowControls(serializedPointLight),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(pointLightObject);
            }
        }

        [Test]
        public void UpdateLightData_RefreshesTrackedSnapshot_WhenLightPropertiesChange()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.lightUnit = LightUnit.Candela;
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
        public void UpdateLightData_ConvertsPointLightLumenIntoNativeCandelaForTrackedColor()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.lightUnit = LightUnit.Lumen;
            light.color = Color.white;
            light.intensity = 4.0f * Mathf.PI;

            var trackedLightData = VividLightRenderDatabase.instance.UpdateLightData(light, light.GetVividAdditionalLightData());

            Assert.That(trackedLightData.intensity, Is.EqualTo(4.0f * Mathf.PI).Within(0.0001f));
            Assert.That(trackedLightData.color.x, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(trackedLightData.color.y, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(trackedLightData.color.z, Is.EqualTo(1.0f).Within(0.0001f));
        }

        [Test]
        public void UpdateLightData_ConvertsSpotLightLumenIntoNativeCandelaForTrackedColor()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.lightUnit = LightUnit.Lumen;
            light.enableSpotReflector = true;
            light.spotAngle = 60.0f;
            light.color = Color.white;
            light.intensity = LightUnitUtils.GetSolidAngleFromSpotLight(light.spotAngle);

            var trackedLightData = VividLightRenderDatabase.instance.UpdateLightData(light, light.GetVividAdditionalLightData());

            Assert.That(trackedLightData.color.x, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(trackedLightData.color.y, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(trackedLightData.color.z, Is.EqualTo(1.0f).Within(0.0001f));
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
            light.lightUnit = LightUnit.Candela;
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
            light.lightUnit = LightUnit.Candela;
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

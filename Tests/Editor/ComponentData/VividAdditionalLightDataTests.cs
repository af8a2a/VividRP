using System.IO;
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
        private static readonly MethodInfo s_OnValidateMethod =
            typeof(VividAdditionalLightData).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);

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
        public void OnValidate_SetsBoundingSphereOverride_ForRectangleAreaLight()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Rectangle;
            light.range = 4.0f;
            light.areaSize = new Vector2(2.0f, 6.0f);

            var additionalData = light.GetVividAdditionalLightData();

            Assert.That(s_OnValidateMethod, Is.Not.Null);
            s_OnValidateMethod.Invoke(additionalData, null);

            Assert.That(light.useBoundingSphereOverride, Is.True);
            Assert.That(light.boundingSphereOverride.x, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(light.boundingSphereOverride.y, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(light.boundingSphereOverride.z, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(light.boundingSphereOverride.w, Is.EqualTo(4.0f + 0.5f * light.areaSize.magnitude).Within(0.0001f));
        }

        [Test]
        public void OnValidate_SetsBoundingSphereOverride_ForTubeAreaLight()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Tube;
            light.range = 3.0f;
            light.areaSize = new Vector2(8.0f, 0.0f);

            var additionalData = light.GetVividAdditionalLightData();

            Assert.That(s_OnValidateMethod, Is.Not.Null);
            s_OnValidateMethod.Invoke(additionalData, null);

            Assert.That(light.useBoundingSphereOverride, Is.True);
            Assert.That(light.boundingSphereOverride.x, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(light.boundingSphereOverride.y, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(light.boundingSphereOverride.z, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(light.boundingSphereOverride.w, Is.EqualTo(7.0f).Within(0.0001f));
        }

        [Test]
        public void OnValidate_KeepsRectangleBoundingSphereConservative_WhenBarnDoorIsActive()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Rectangle;
            light.range = 4.0f;
            light.areaSize = new Vector2(2.0f, 6.0f);

            var additionalData = light.GetVividAdditionalLightData();
            additionalData.barnDoorAngle = 15.0f;
            additionalData.barnDoorLength = 3.0f;

            Assert.That(s_OnValidateMethod, Is.Not.Null);
            s_OnValidateMethod.Invoke(additionalData, null);

            Assert.That(light.useBoundingSphereOverride, Is.True);
            Assert.That(light.boundingSphereOverride.w, Is.EqualTo(4.0f + 0.5f * light.areaSize.magnitude).Within(0.0001f));
        }

        [Test]
        public void OnValidate_ClearsBoundingSphereOverride_WhenLightStopsBeingAreaLight()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Rectangle;
            light.range = 2.0f;
            light.areaSize = new Vector2(4.0f, 2.0f);

            var additionalData = light.GetVividAdditionalLightData();

            Assert.That(s_OnValidateMethod, Is.Not.Null);
            s_OnValidateMethod.Invoke(additionalData, null);
            Assert.That(light.useBoundingSphereOverride, Is.True);

            light.type = LightType.Point;
            s_OnValidateMethod.Invoke(additionalData, null);

            Assert.That(light.useBoundingSphereOverride, Is.False);
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
            Assert.That(serializedLight.screenSpaceShadowQuality, Is.Not.Null);
            Assert.That(serializedLight.shadowAtlasResolution, Is.Not.Null);
            Assert.That(serializedLight.depthBias, Is.Not.Null);
            Assert.That(serializedLight.normalBias, Is.Not.Null);
            Assert.That(serializedLight.slopeBias, Is.Not.Null);
            Assert.That(serializedLight.dirLightPCSSBlockerSampleCount, Is.Not.Null);
            Assert.That(serializedLight.dirLightPCSSFilterSampleCount, Is.Not.Null);
            Assert.That(serializedLight.dirLightPCSSMaxPenumbraSize, Is.Not.Null);
            Assert.That(serializedLight.dirLightPCSSMaxSamplingDistance, Is.Not.Null);
            Assert.That(serializedLight.dirLightPCSSMinFilterSizeTexels, Is.Not.Null);
            Assert.That(serializedLight.dirLightPCSSMinFilterMaxAngularDiameter, Is.Not.Null);
            Assert.That(serializedLight.dirLightPCSSBlockerSearchAngularDiameter, Is.Not.Null);
            Assert.That(serializedLight.dirLightPCSSBlockerSamplingClumpExponent, Is.Not.Null);
            Assert.That(serializedLight.barnDoorAngle, Is.Not.Null);
            Assert.That(serializedLight.barnDoorLength, Is.Not.Null);
            Assert.That(serializedLight.affectsVolumetric, Is.Not.Null);
            Assert.That(serializedLight.volumetricDimmer, Is.Not.Null);
            Assert.That(serializedLight.volumetricFadeDistance, Is.Not.Null);
            Assert.That(serializedLight.volumetricShadowDimmer, Is.Not.Null);
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
        public void AdditionalLightDataPropertySetters_UpdateTrackedLightRenderData_WhenAreaBarnDoorChanges()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Rectangle;
            light.areaSize = new Vector2(3.0f, 1.5f);

            var additionalData = light.GetVividAdditionalLightData();

            VividLightRenderDatabase.instance.Clear();

            additionalData.barnDoorAngle = 42.0f;
            additionalData.barnDoorLength = 0.2f;

            Assert.That(VividLightRenderDatabase.instance.TryGetLightData(light, out var trackedLightData), Is.True);
            Assert.That(trackedLightData.barnDoorAngle, Is.EqualTo(42.0f).Within(0.0001f));
            Assert.That(trackedLightData.barnDoorLength, Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void AdditionalLightDataPropertySetters_UpdateTrackedLightRenderData_WhenVolumetricSettingsChange()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Point;

            var additionalData = light.GetVividAdditionalLightData();

            VividLightRenderDatabase.instance.Clear();

            additionalData.volumetricDimmer = 2.5f;
            additionalData.volumetricFadeDistance = 250.0f;
            additionalData.volumetricShadowDimmer = 0.35f;

            Assert.That(VividLightRenderDatabase.instance.TryGetLightData(light, out var trackedLightData), Is.True);
            Assert.That((trackedLightData.flags & VividLightRenderDataFlags.AffectVolumetric) != 0, Is.True);
            Assert.That(trackedLightData.volumetricDimmer, Is.EqualTo(2.5f).Within(0.0001f));
            Assert.That(trackedLightData.volumetricFadeDistance, Is.EqualTo(250.0f).Within(0.0001f));
            Assert.That(trackedLightData.volumetricShadowDimmer, Is.EqualTo(0.35f).Within(0.0001f));

            additionalData.affectsVolumetric = false;

            Assert.That(VividLightRenderDatabase.instance.TryGetLightData(light, out trackedLightData), Is.True);
            Assert.That((trackedLightData.flags & VividLightRenderDataFlags.AffectVolumetric) != 0, Is.False);
            Assert.That(trackedLightData.volumetricDimmer, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(trackedLightData.volumetricShadowDimmer, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(trackedLightData.volumetricFadeDistance, Is.EqualTo(250.0f).Within(0.0001f));
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
        public void ShadowBiasSettings_DefaultToExpectedValues_OnDirectionalLights()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Directional;

            var additionalData = light.GetVividAdditionalLightData();

            Assert.That(
                additionalData.screenSpaceShadowQuality,
                Is.EqualTo(VividAdditionalLightData.DefaultScreenSpaceShadowQuality));
            Assert.That(
                additionalData.shadowAtlasResolution,
                Is.EqualTo(VividAdditionalLightData.CSMShadowAtlasResolution.Resolution4096));
            Assert.That(
                additionalData.resolvedShadowAtlasResolution,
                Is.EqualTo(VividAdditionalLightData.DefaultShadowAtlasResolution));
            Assert.That(additionalData.depthBias, Is.EqualTo(VividAdditionalLightData.DefaultShadowDepthBias));
            Assert.That(additionalData.normalBias, Is.EqualTo(VividAdditionalLightData.DefaultShadowNormalBias));
            Assert.That(additionalData.slopeBias, Is.EqualTo(VividAdditionalLightData.DefaultShadowSlopeBias));
            Assert.That(
                additionalData.dirLightPCSSBlockerSampleCount,
                Is.EqualTo(VividAdditionalLightData.DefaultDirLightPCSSBlockerSampleCount));
            Assert.That(
                additionalData.dirLightPCSSFilterSampleCount,
                Is.EqualTo(VividAdditionalLightData.DefaultDirLightPCSSFilterSampleCount));
            Assert.That(
                additionalData.dirLightPCSSMaxPenumbraSize,
                Is.EqualTo(VividAdditionalLightData.DefaultDirLightPCSSMaxPenumbraSize));
            Assert.That(
                additionalData.dirLightPCSSMaxSamplingDistance,
                Is.EqualTo(VividAdditionalLightData.DefaultDirLightPCSSMaxSamplingDistance));
            Assert.That(
                additionalData.dirLightPCSSMinFilterSizeTexels,
                Is.EqualTo(VividAdditionalLightData.DefaultDirLightPCSSMinFilterSizeTexels));
            Assert.That(
                additionalData.dirLightPCSSMinFilterMaxAngularDiameter,
                Is.EqualTo(VividAdditionalLightData.DefaultDirLightPCSSMinFilterMaxAngularDiameter));
            Assert.That(
                additionalData.dirLightPCSSBlockerSearchAngularDiameter,
                Is.EqualTo(VividAdditionalLightData.DefaultDirLightPCSSBlockerSearchAngularDiameter));
            Assert.That(
                additionalData.dirLightPCSSBlockerSamplingClumpExponent,
                Is.EqualTo(VividAdditionalLightData.DefaultDirLightPCSSBlockerSamplingClumpExponent));
        }

        [Test]
        public void ShadowBiasSettings_ClampToExpectedRanges()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Directional;

            var additionalData = light.GetVividAdditionalLightData();
            additionalData.depthBias = 99.0f;
            additionalData.normalBias = -1.0f;
            additionalData.slopeBias = 99.0f;
            additionalData.dirLightPCSSBlockerSampleCount = 999;
            additionalData.dirLightPCSSFilterSampleCount = 0;
            additionalData.dirLightPCSSMaxPenumbraSize = -1.0f;
            additionalData.dirLightPCSSMaxSamplingDistance = -1.0f;
            additionalData.dirLightPCSSMinFilterSizeTexels = -1.0f;
            additionalData.dirLightPCSSMinFilterMaxAngularDiameter = -1.0f;
            additionalData.dirLightPCSSBlockerSearchAngularDiameter = -1.0f;
            additionalData.dirLightPCSSBlockerSamplingClumpExponent = 999.0f;

            additionalData.shadowAtlasResolution = (VividAdditionalLightData.CSMShadowAtlasResolution)12345;
            additionalData.screenSpaceShadowQuality = (VividAdditionalLightData.CSMScreenSpaceShadowQuality)12345;

            Assert.That(
                additionalData.screenSpaceShadowQuality,
                Is.EqualTo(VividAdditionalLightData.DefaultScreenSpaceShadowQuality));
            Assert.That(
                additionalData.shadowAtlasResolution,
                Is.EqualTo(VividAdditionalLightData.CSMShadowAtlasResolution.Resolution4096));
            Assert.That(
                additionalData.resolvedShadowAtlasResolution,
                Is.EqualTo(VividAdditionalLightData.DefaultShadowAtlasResolution));
            Assert.That(additionalData.depthBias, Is.EqualTo(VividAdditionalLightData.MaxShadowDepthBias));
            Assert.That(additionalData.normalBias, Is.EqualTo(0.0f));
            Assert.That(additionalData.slopeBias, Is.EqualTo(VividAdditionalLightData.MaxShadowSlopeBias));
            Assert.That(additionalData.dirLightPCSSBlockerSampleCount, Is.EqualTo(VividAdditionalLightData.MaxPCSSSampleCount));
            Assert.That(additionalData.dirLightPCSSFilterSampleCount, Is.EqualTo(VividAdditionalLightData.MinPCSSSampleCount));
            Assert.That(additionalData.dirLightPCSSMaxPenumbraSize, Is.EqualTo(0.0f));
            Assert.That(additionalData.dirLightPCSSMaxSamplingDistance, Is.EqualTo(0.0f));
            Assert.That(additionalData.dirLightPCSSMinFilterSizeTexels, Is.EqualTo(0.0f));
            Assert.That(additionalData.dirLightPCSSMinFilterMaxAngularDiameter, Is.EqualTo(0.0f));
            Assert.That(additionalData.dirLightPCSSBlockerSearchAngularDiameter, Is.EqualTo(0.0f));
            Assert.That(
                additionalData.dirLightPCSSBlockerSamplingClumpExponent,
                Is.EqualTo(VividAdditionalLightData.MaxDirLightPCSSBlockerSamplingClumpExponent));
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
        public void AreaLightSettings_DefaultToExpectedBarnDoorValues_OnRectangleLights()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Rectangle;

            var additionalData = light.GetVividAdditionalLightData();

            Assert.That(additionalData.barnDoorAngle, Is.EqualTo(VividAdditionalLightData.DefaultBarnDoorAngle));
            Assert.That(additionalData.barnDoorLength, Is.EqualTo(VividAdditionalLightData.DefaultBarnDoorLength));
        }

        [Test]
        public void AreaLightSettings_ClampBarnDoorToHdrpRange()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Rectangle;

            var additionalData = light.GetVividAdditionalLightData();
            additionalData.barnDoorAngle = 120.0f;
            additionalData.barnDoorLength = -1.0f;

            Assert.That(additionalData.barnDoorAngle, Is.EqualTo(90.0f));
            Assert.That(additionalData.barnDoorLength, Is.EqualTo(0.0f));
        }

        [Test]
        public void VolumetricSettings_DefaultToHdrpStyleValues()
        {
            var light = m_GameObject.AddComponent<Light>();

            var additionalData = light.GetVividAdditionalLightData();

            Assert.That(additionalData.affectsVolumetric, Is.True);
            Assert.That(additionalData.volumetricDimmer, Is.EqualTo(VividAdditionalLightData.DefaultVolumetricDimmer));
            Assert.That(additionalData.volumetricFadeDistance, Is.EqualTo(VividAdditionalLightData.DefaultVolumetricFadeDistance));
            Assert.That(additionalData.volumetricShadowDimmer, Is.EqualTo(VividAdditionalLightData.DefaultVolumetricShadowDimmer));
        }

        [Test]
        public void VolumetricSettings_ClampToHdrpRanges()
        {
            var light = m_GameObject.AddComponent<Light>();

            var additionalData = light.GetVividAdditionalLightData();
            additionalData.volumetricDimmer = 99.0f;
            additionalData.volumetricFadeDistance = -1.0f;
            additionalData.volumetricShadowDimmer = 99.0f;

            Assert.That(additionalData.volumetricDimmer, Is.EqualTo(VividAdditionalLightData.MaxVolumetricDimmer));
            Assert.That(additionalData.volumetricFadeDistance, Is.EqualTo(0.0f));
            Assert.That(additionalData.volumetricShadowDimmer, Is.EqualTo(1.0f));

            additionalData.affectsVolumetric = false;

            Assert.That(additionalData.volumetricDimmer, Is.EqualTo(0.0f));
            Assert.That(additionalData.volumetricShadowDimmer, Is.EqualTo(0.0f));
        }

        [Test]
        public void CelestialBodySettings_ClampAngularDiameterToHdrpRange()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Directional;

            var additionalData = light.GetVividAdditionalLightData();
            additionalData.angularDiameter = 120.0f;

            Assert.That(additionalData.angularDiameter, Is.EqualTo(90.0f));
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
        public void VividLightEditor_ShowsVolumetricFadeDistanceControls_OnlyForLocalLights()
        {
            var directionalLight = m_GameObject.AddComponent<Light>();
            directionalLight.type = LightType.Directional;

            var serializedDirectionalLight = new VividSerializedLight(new SerializedObject(directionalLight));

            Assert.That(
                VividLightEditor.ShouldShowVolumetricFadeDistanceControls(serializedDirectionalLight),
                Is.False);

            var pointLightObject = new GameObject("Vivid Point Light Volumetric Test");

            try
            {
                var pointLight = pointLightObject.AddComponent<Light>();
                pointLight.type = LightType.Point;

                var serializedPointLight = new VividSerializedLight(new SerializedObject(pointLight));

                Assert.That(
                    VividLightEditor.ShouldShowVolumetricFadeDistanceControls(serializedPointLight),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(pointLightObject);
            }
        }

        [Test]
        public void VividLightEditor_ShowsDirectionalCelestialBodyControls_OnlyForDirectionalLights()
        {
            var directionalLight = m_GameObject.AddComponent<Light>();
            directionalLight.type = LightType.Directional;

            var serializedDirectionalLight = new VividSerializedLight(new SerializedObject(directionalLight));

            Assert.That(
                VividLightEditor.ShouldShowDirectionalPhysicallyBasedSkyControls(serializedDirectionalLight),
                Is.True);

            var pointLightObject = new GameObject("Vivid Point Light Celestial Test");

            try
            {
                var pointLight = pointLightObject.AddComponent<Light>();
                pointLight.type = LightType.Point;

                var serializedPointLight = new VividSerializedLight(new SerializedObject(pointLight));

                Assert.That(
                    VividLightEditor.ShouldShowDirectionalPhysicallyBasedSkyControls(serializedPointLight),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(pointLightObject);
            }
        }

        [Test]
        public void VividLightEditor_ShowsDirectionalShadowBiasControls_OnlyForDirectionalLights()
        {
            var directionalLight = m_GameObject.AddComponent<Light>();
            directionalLight.type = LightType.Directional;

            var serializedDirectionalLight = new VividSerializedLight(new SerializedObject(directionalLight));

            Assert.That(
                VividLightEditor.ShouldShowDirectionalShadowBiasControls(serializedDirectionalLight),
                Is.True);

            var pointLightObject = new GameObject("Vivid Point Light Shadow Bias Test");

            try
            {
                var pointLight = pointLightObject.AddComponent<Light>();
                pointLight.type = LightType.Point;

                var serializedPointLight = new VividSerializedLight(new SerializedObject(pointLight));

                Assert.That(
                    VividLightEditor.ShouldShowDirectionalShadowBiasControls(serializedPointLight),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(pointLightObject);
            }
        }

        [Test]
        public void VividLightEditor_ShowsDirectionalPCSSControls_OnlyForDirectionalLightsUsingVeryHighQuality()
        {
            var directionalLight = m_GameObject.AddComponent<Light>();
            directionalLight.type = LightType.Directional;

            var serializedDirectionalLight = new VividSerializedLight(new SerializedObject(directionalLight));

            Assert.That(
                VividLightEditor.ShouldShowDirectionalPCSSControls(serializedDirectionalLight),
                Is.False);

            serializedDirectionalLight.screenSpaceShadowQuality.intValue =
                (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.VeryHigh;

            Assert.That(
                VividLightEditor.ShouldShowDirectionalPCSSControls(serializedDirectionalLight),
                Is.True);

            var pointLightObject = new GameObject("Vivid Point Light PCSS Test");

            try
            {
                var pointLight = pointLightObject.AddComponent<Light>();
                pointLight.type = LightType.Point;

                var serializedPointLight = new VividSerializedLight(new SerializedObject(pointLight));
                serializedPointLight.screenSpaceShadowQuality.intValue =
                    (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.VeryHigh;

                Assert.That(
                    VividLightEditor.ShouldShowDirectionalPCSSControls(serializedPointLight),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(pointLightObject);
            }
        }

        [Test]
        public void VividLightEditor_UsesHdrpStyleCelestialBodyPanel()
        {
            var source = File.ReadAllText(GetPackageFilePath("Editor", "ComponentEditor", "VividLightEditor.cs"));

            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Celestial Body\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Affect Physically Based Sky\""));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Angular Diameter Multiplier\""));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Shading\""));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Phase\""));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Phase Rotation\""));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Surface Color\""));
            Assert.That(source, Does.Contain("private static readonly string[] s_DiameterModeNames = { \"Multiply\", \"Override\" };"));
            Assert.That(source, Does.Contain("EditorGUILayout.PropertyField(m_SerializedLight.angularDiameter, s_AngularDiameterLabel);"));
            Assert.That(source, Does.Contain("DrawCelestialBodyAngularDiameterField();"));
            Assert.That(source, Does.Contain("DrawCelestialBodySurfaceColorField();"));
            Assert.That(source, Does.Contain("The Celestial Body cannot receive lighting from itself."));
            Assert.That(source, Does.Contain("The Sun Light needs to be a directional light."));
            Assert.That(source, Does.Contain("EditorGUILayout.PropertyField(m_SerializedLight.flareFalloff, s_FlareFalloffLabel);"));
            Assert.That(source, Does.Contain("EditorGUILayout.PropertyField(m_SerializedLight.flareTint, s_FlareTintLabel);"));
        }

        [Test]
        public void VividLightEditor_ShowsAreaBarnDoorControls_OnlyForRectangleLights()
        {
            var rectangleLight = m_GameObject.AddComponent<Light>();
            rectangleLight.type = LightType.Rectangle;

            var serializedRectangleLight = new VividSerializedLight(new SerializedObject(rectangleLight));

            Assert.That(
                VividLightEditor.ShouldShowAreaBarnDoorControls(serializedRectangleLight),
                Is.True);

            var tubeLightObject = new GameObject("Vivid Tube Light Barn Door Test");

            try
            {
                var tubeLight = tubeLightObject.AddComponent<Light>();
                tubeLight.type = LightType.Tube;

                var serializedTubeLight = new VividSerializedLight(new SerializedObject(tubeLight));

                Assert.That(
                    VividLightEditor.ShouldShowAreaBarnDoorControls(serializedTubeLight),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(tubeLightObject);
            }
        }

        [Test]
        public void VividLightEditor_UsesHdrpStyleAreaBarnDoorPanel()
        {
            var source = File.ReadAllText(GetPackageFilePath("Editor", "ComponentEditor", "VividLightEditor.cs"));

            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Barn Door\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Angle\""));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Length\""));
            Assert.That(source, Does.Contain("DrawAreaBarnDoorInspector();"));
            Assert.That(source, Does.Contain("m_SerializedLight.barnDoorAngle"));
            Assert.That(source, Does.Contain("m_SerializedLight.barnDoorLength"));
        }

        [Test]
        public void VividLightEditor_UsesDirectionalShadowBiasPanel()
        {
            var source = File.ReadAllText(GetPackageFilePath("Editor", "ComponentEditor", "VividLightEditor.cs"));

            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"CSM Shadow\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Screen Space Quality\""));
            Assert.That(source, Does.Contain("private static readonly GUIContent[] s_ScreenSpaceShadowQualityOptionLabels ="));
            Assert.That(source, Does.Contain("Low (PCF 3x3)"));
            Assert.That(source, Does.Contain("Medium (PCF 5x5)"));
            Assert.That(source, Does.Contain("High (PCF 7x7)"));
            Assert.That(source, Does.Contain("Very High (PCSS)"));
            Assert.That(source, Does.Contain("DrawDirectionalScreenSpaceShadowQualityField();"));
            Assert.That(source, Does.Contain("m_SerializedLight.screenSpaceShadowQuality"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Atlas Resolution\""));
            Assert.That(source, Does.Contain("private static readonly GUIContent[] s_ShadowAtlasResolutionOptionLabels ="));
            Assert.That(source, Does.Contain("EditorGUILayout.IntPopup("));
            Assert.That(source, Does.Contain("m_SerializedLight.shadowAtlasResolution"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"PCSS\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Max Penumbra Size\""));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Max Sampling Distance\""));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Min Filter\""));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Blocker Search Angular Diameter\""));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Blocker Sample Count\""));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Filter Sample Count\""));
            Assert.That(source, Does.Contain("DrawDirectionalPCSSFields();"));
            Assert.That(source, Does.Contain("m_SerializedLight.dirLightPCSSMaxPenumbraSize"));
            Assert.That(source, Does.Contain("m_SerializedLight.dirLightPCSSFilterSampleCount"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Depth Bias\""));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Normal Bias\""));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Slope-Scale Depth Bias\""));
            Assert.That(source, Does.Contain("EditorGUILayout.Slider(m_SerializedLight.depthBias, 0.0f, 10.0f, s_DepthBiasLabel);"));
            Assert.That(source, Does.Contain("EditorGUILayout.Slider(m_SerializedLight.normalBias, 0.0f, 10.0f, s_NormalBiasLabel);"));
            Assert.That(source, Does.Contain("EditorGUILayout.Slider(m_SerializedLight.slopeBias, 0.0f, 5.0f, s_SlopeBiasLabel);"));
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
            light.shapeRadius = 0.1f;
            light.transform.position = new Vector3(1.0f, 2.0f, 3.0f);

            var additionalData = light.GetVividAdditionalLightData();
            var database = VividLightRenderDatabase.instance;

            database.Clear();
            database.UpdateLightData(light, additionalData);

            light.color = Color.cyan;
            light.intensity = 3.0f;
            light.range = 8.0f;
            light.shapeRadius = 0.35f;
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
            Assert.That(trackedLightData.shapeRadius, Is.EqualTo(0.35f).Within(0.0001f));
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

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packageRoots = new[]
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }
    }
}

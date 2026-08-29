using System.Runtime.CompilerServices;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

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
        public void OnValidate_SetsBoundingSphereOverride_ForProjectorBoxLight()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Box;
            light.range = 10.0f;
            light.areaSize = new Vector2(4.0f, 2.0f);

            var additionalData = light.GetVividAdditionalLightData();

            Assert.That(s_OnValidateMethod, Is.Not.Null);
            s_OnValidateMethod.Invoke(additionalData, null);

            Assert.That(light.useBoundingSphereOverride, Is.True);
            Assert.That(light.boundingSphereOverride.x, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(light.boundingSphereOverride.y, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(light.boundingSphereOverride.z, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(light.boundingSphereOverride.w, Is.EqualTo(Mathf.Sqrt(105.0f)).Within(0.0001f));
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
            Assert.That(serializedLight.screenSpaceShadowQuality, Is.Not.Null);
            Assert.That(serializedLight.shadowMapResolution, Is.Not.Null);
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
            Assert.That(serializedLight.dirLightBendSSSMaxRayDistance, Is.Not.Null);
            Assert.That(serializedLight.dirLightBendSSSSurfaceThickness, Is.Not.Null);
            Assert.That(serializedLight.dirLightBendSSSBilinearThreshold, Is.Not.Null);
            Assert.That(serializedLight.dirLightBendSSSShadowContrast, Is.Not.Null);
            Assert.That(serializedLight.dirLightBendSSSIgnoreEdgePixels, Is.Not.Null);
            Assert.That(serializedLight.dirLightBendSSSUsePrecisionOffset, Is.Not.Null);
            Assert.That(serializedLight.dirLightBendSSSBilinearSamplingOffsetMode, Is.Not.Null);
            Assert.That(serializedLight.barnDoorAngle, Is.Not.Null);
            Assert.That(serializedLight.barnDoorLength, Is.Not.Null);
            Assert.That(serializedLight.affectsVolumetric, Is.Not.Null);
            Assert.That(serializedLight.volumetricDimmer, Is.Not.Null);
            Assert.That(serializedLight.volumetricFadeDistance, Is.Not.Null);
            Assert.That(serializedLight.volumetricShadowDimmer, Is.Not.Null);
            Assert.That(serializedLight.interactsWithSky, Is.Not.Null);
            Assert.That(serializedLight.enableTimeOfDay, Is.Not.Null);
            Assert.That(serializedLight.timeOfDay, Is.Not.Null);
            Assert.That(serializedLight.angularDiameter, Is.Not.Null);
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
        public void NormalizeUnsupportedLightUnit_ResetsSerializedBoxLightsToLux()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Box;
            light.lightUnit = LightUnit.Lumen;
            light.luxAtDistance = 12.0f;
            var serializedObject = new SerializedObject(light);
            var settings = new LightEditor.Settings(serializedObject);
            settings.OnEnable();
            settings.Update();

            var changed = VividLightIntensityUnitUtility.NormalizeUnsupportedLightUnit(settings);
            settings.ApplyModifiedProperties();

            Assert.That(changed, Is.True);
            Assert.That(light.lightUnit, Is.EqualTo(LightUnit.Lux));
            Assert.That(light.luxAtDistance, Is.EqualTo(1.0f));
        }

        [Test]
        public void NormalizeBoxSpotLightUnit_ResetsUnitWhenSerializedTypeHasNotSynced()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Box;
            light.lightUnit = LightUnit.Lumen;
            light.luxAtDistance = 12.0f;
            var serializedObject = new SerializedObject(light);
            var settings = new LightEditor.Settings(serializedObject);
            settings.OnEnable();
            settings.Update();
            settings.lightType.SetEnumValue(LightType.Spot);

            var changed = VividLightIntensityUnitUtility.NormalizeBoxSpotLightUnit(settings);

            Assert.That(changed, Is.True);
            Assert.That(settings.lightUnit.GetEnumValue<LightUnit>(), Is.EqualTo(LightUnit.Lux));
            Assert.That(settings.luxAtDistance.floatValue, Is.EqualTo(1.0f));
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
                VividAdditionalLightData.DefaultRayTracedShadowSunAngularDiameter,
                Is.EqualTo(VividAdditionalLightData.DefaultCelestialBodyAngularDiameter));
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
            additionalData.angularDiameter = 1.2f;

            Assert.That(additionalData.enableRayTracedShadow, Is.True);
            Assert.That(additionalData.rayTracedShadowRayLength, Is.EqualTo(24.0f));
            Assert.That(additionalData.rayTracedShadowRayBias, Is.EqualTo(0.02f));
            Assert.That(additionalData.rayTracedShadowDistantRayBias, Is.EqualTo(0.08f));
            Assert.That(additionalData.angularDiameter, Is.EqualTo(1.2f));
            Assert.That(additionalData.supportsRayTracedShadow, Is.False);
            Assert.That(additionalData.isRayTracedShadowActive, Is.False);
        }

        [Test]
        public void RayTracedShadowSunAngularDiameter_AliasesSharedAngularDiameter()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Directional;

            var additionalData = light.GetVividAdditionalLightData();
#pragma warning disable CS0618
            additionalData.rayTracedShadowSunAngularDiameter = 1.2f;
            Assert.That(additionalData.angularDiameter, Is.EqualTo(1.2f));

            additionalData.angularDiameter = 2.4f;
            Assert.That(additionalData.rayTracedShadowSunAngularDiameter, Is.EqualTo(2.4f));
#pragma warning restore CS0618
        }

        [Test]
        public void LegacyCelestialBodyDiameterOverride_AliasesSharedAngularDiameter()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Directional;

            var additionalData = light.GetVividAdditionalLightData();
#pragma warning disable CS0618
            additionalData.diameterOverride = 3.0f;
            Assert.That(additionalData.angularDiameter, Is.EqualTo(3.0f));

            additionalData.angularDiameter = 4.0f;
            Assert.That(additionalData.diameterOverride, Is.EqualTo(4.0f));
#pragma warning restore CS0618
        }

        [Test]
        public void DirectionalRayTracedShadowRequest_UsesSharedAngularDiameter()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Hard;

            var additionalData = light.GetVividAdditionalLightData();
            additionalData.enableRayTracedShadow = true;
            additionalData.angularDiameter = 1.7f;

            var lightData = new VividLightData
            {
                directionalLights = new[]
                {
                    new VividLightData.DirectionalLightData
                    {
                        directionWS = Vector3.down
                    }
                },
                directionalLightCount = 1,
                mainDirectionalLightIndex = 0,
                mainDirectionalLightEntityId = light.GetEntityId()
            };

            var request = DirectionalRayTracedShadowPass.ResolveShadowRequest(lightData, true, true);

            Assert.That(request.ShouldTrace, Is.True);
            Assert.That(request.SunAngularDiameter, Is.EqualTo(1.7f));
        }

        [Test]
        public void OnValidate_MigratesLegacyRayTracedShadowSunAngularDiameter_ToSharedAngularDiameter()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Directional;

            var additionalData = light.GetVividAdditionalLightData();
            var legacyAngularDiameterField = typeof(VividAdditionalLightData).GetField(
                "m_RayTracedShadowSunAngularDiameter",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var migratedField = typeof(VividAdditionalLightData).GetField(
                "m_MigratedRayTracedShadowSunAngularDiameter",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(legacyAngularDiameterField, Is.Not.Null);
            Assert.That(migratedField, Is.Not.Null);
            additionalData.angularDiameter = VividAdditionalLightData.DefaultCelestialBodyAngularDiameter;
            legacyAngularDiameterField.SetValue(additionalData, 1.2f);
            migratedField.SetValue(additionalData, false);

            Assert.That(s_OnValidateMethod, Is.Not.Null);
            s_OnValidateMethod.Invoke(additionalData, null);

            Assert.That(additionalData.angularDiameter, Is.EqualTo(1.2f));
        }

        [Test]
        public void OnValidate_MigratesLegacyCelestialBodyDiameterOverride_ToSharedAngularDiameter()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Directional;

            var additionalData = light.GetVividAdditionalLightData();
            var legacyOverrideField = typeof(VividAdditionalLightData).GetField(
                "m_DiameterOverride",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var migratedField = typeof(VividAdditionalLightData).GetField(
                "m_MigratedCelestialBodyAngularDiameter",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(legacyOverrideField, Is.Not.Null);
            Assert.That(migratedField, Is.Not.Null);
            additionalData.angularDiameter = VividAdditionalLightData.DefaultCelestialBodyAngularDiameter;
            legacyOverrideField.SetValue(additionalData, 1.4f);
            migratedField.SetValue(additionalData, false);

            Assert.That(s_OnValidateMethod, Is.Not.Null);
            s_OnValidateMethod.Invoke(additionalData, null);

            Assert.That(additionalData.angularDiameter, Is.EqualTo(1.4f));
            Assert.That(additionalData.resolvedAngularDiameter, Is.EqualTo(1.4f));
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
                additionalData.shadowMapResolution,
                Is.EqualTo(VividAdditionalLightData.CSMShadowMapResolution.Resolution2048));
            Assert.That(
                additionalData.resolvedShadowMapResolution,
                Is.EqualTo(VividAdditionalLightData.DefaultShadowMapResolution));
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
            Assert.That(
                additionalData.dirLightBendSSSMaxRayDistance,
                Is.EqualTo(VividAdditionalLightData.DefaultDirLightBendSSSMaxRayDistance));
            Assert.That(
                additionalData.dirLightBendSSSSurfaceThickness,
                Is.EqualTo(VividAdditionalLightData.DefaultDirLightBendSSSSurfaceThickness));
            Assert.That(
                additionalData.dirLightBendSSSBilinearThreshold,
                Is.EqualTo(VividAdditionalLightData.DefaultDirLightBendSSSBilinearThreshold));
            Assert.That(
                additionalData.dirLightBendSSSShadowContrast,
                Is.EqualTo(VividAdditionalLightData.DefaultDirLightBendSSSShadowContrast));
            Assert.That(
                additionalData.dirLightBendSSSIgnoreEdgePixels,
                Is.EqualTo(VividAdditionalLightData.DefaultDirLightBendSSSIgnoreEdgePixels));
            Assert.That(
                additionalData.dirLightBendSSSUsePrecisionOffset,
                Is.EqualTo(VividAdditionalLightData.DefaultDirLightBendSSSUsePrecisionOffset));
            Assert.That(
                additionalData.dirLightBendSSSBilinearSamplingOffsetMode,
                Is.EqualTo(VividAdditionalLightData.DefaultDirLightBendSSSBilinearSamplingOffsetMode));
        }

        [TestCase(1024, 512)]
        [TestCase(2048, 1024)]
        [TestCase(4096, 2048)]
        [TestCase(8192, 4096)]
        public void ShadowMapResolution_MigratesLegacyAtlasResolution(
            int legacyAtlasResolution,
            int expectedCascadeResolution)
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Directional;

            var additionalData = light.GetVividAdditionalLightData();
            var legacyResolutionField = typeof(VividAdditionalLightData).GetField(
                "m_LegacyShadowAtlasResolution",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(legacyResolutionField, Is.Not.Null);
            legacyResolutionField.SetValue(additionalData, legacyAtlasResolution);

            Assert.That(s_OnValidateMethod, Is.Not.Null);
            s_OnValidateMethod.Invoke(additionalData, null);

            Assert.That(
                additionalData.resolvedShadowMapResolution,
                Is.EqualTo(expectedCascadeResolution));
            Assert.That(legacyResolutionField.GetValue(additionalData), Is.EqualTo(0));
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
            additionalData.dirLightBendSSSMaxRayDistance = 999.0f;
            additionalData.dirLightBendSSSSurfaceThickness = 999.0f;
            additionalData.dirLightBendSSSBilinearThreshold = -1.0f;
            additionalData.dirLightBendSSSShadowContrast = -1.0f;
            additionalData.dirLightBendSSSIgnoreEdgePixels = true;
            additionalData.dirLightBendSSSUsePrecisionOffset = true;
            additionalData.dirLightBendSSSBilinearSamplingOffsetMode = true;

            additionalData.shadowMapResolution = (VividAdditionalLightData.CSMShadowMapResolution)12345;
            additionalData.screenSpaceShadowQuality = (VividAdditionalLightData.CSMScreenSpaceShadowQuality)12345;

            Assert.That(
                additionalData.screenSpaceShadowQuality,
                Is.EqualTo(VividAdditionalLightData.DefaultScreenSpaceShadowQuality));
            Assert.That(
                additionalData.shadowMapResolution,
                Is.EqualTo(VividAdditionalLightData.CSMShadowMapResolution.Resolution2048));
            Assert.That(
                additionalData.resolvedShadowMapResolution,
                Is.EqualTo(VividAdditionalLightData.DefaultShadowMapResolution));
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
            Assert.That(
                additionalData.dirLightBendSSSMaxRayDistance,
                Is.EqualTo(VividAdditionalLightData.MaxDirLightBendSSSMaxRayDistance));
            Assert.That(
                additionalData.dirLightBendSSSSurfaceThickness,
                Is.EqualTo(VividAdditionalLightData.MaxDirLightBendSSSSurfaceThickness));
            Assert.That(
                additionalData.dirLightBendSSSBilinearThreshold,
                Is.EqualTo(VividAdditionalLightData.MinDirLightBendSSSBilinearThreshold));
            Assert.That(
                additionalData.dirLightBendSSSShadowContrast,
                Is.EqualTo(VividAdditionalLightData.MinDirLightBendSSSShadowContrast));
            Assert.That(additionalData.dirLightBendSSSIgnoreEdgePixels, Is.True);
            Assert.That(additionalData.dirLightBendSSSUsePrecisionOffset, Is.True);
            Assert.That(additionalData.dirLightBendSSSBilinearSamplingOffsetMode, Is.True);
        }

        [Test]
        public void ShadowBiasSettings_AcceptsUnrealScreenSpaceShadowQuality()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Directional;

            var additionalData = light.GetVividAdditionalLightData();
            additionalData.screenSpaceShadowQuality = VividAdditionalLightData.CSMScreenSpaceShadowQuality.Unreal;

            Assert.That(
                additionalData.screenSpaceShadowQuality,
                Is.EqualTo(VividAdditionalLightData.CSMScreenSpaceShadowQuality.Unreal));
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
            Assert.That(
                additionalData.resolvedAngularDiameter,
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
        public void TimeOfDaySettings_DefaultToExpectedValues_OnDirectionalLights()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Directional;

            var additionalData = light.GetVividAdditionalLightData();

            Assert.That(additionalData.enableTimeOfDay, Is.False);
            Assert.That(additionalData.timeOfDay, Is.EqualTo(VividAdditionalLightData.DefaultTimeOfDay));
            Assert.That(
                additionalData.timeOfDayMaximumLux,
                Is.EqualTo(VividAdditionalLightData.DefaultTimeOfDayMaximumLux));
            Assert.That(additionalData.supportsTimeOfDay, Is.True);
        }

        [Test]
        public void TimeOfDaySettings_ApplyDirectionalRotationAndLux_WhenEnabled()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.lightUnit = LightUnit.Candela;
            light.luxAtDistance = 12.0f;

            var additionalData = light.GetVividAdditionalLightData();
            var database = VividLightRenderDatabase.instance;

            database.Clear();

            additionalData.enableTimeOfDay = true;

            Assert.That(light.lightUnit, Is.EqualTo(LightUnit.Lux));
            Assert.That(light.luxAtDistance, Is.EqualTo(1.0f));
            Assert.That(light.intensity, Is.GreaterThan(100000.0f));
            Assert.That(light.transform.forward.y, Is.LessThan(-0.99f));
            Assert.That(database.TryGetLightData(light, out var trackedLightData), Is.True);
            Assert.That(trackedLightData.intensity, Is.EqualTo(light.intensity).Within(0.0001f));

            additionalData.timeOfDay = 6.0f;

            Assert.That(light.intensity, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(light.transform.forward.x, Is.LessThan(-0.99f));
            Assert.That(database.TryGetLightData(light, out trackedLightData), Is.True);
            Assert.That(trackedLightData.intensity, Is.EqualTo(0.0f).Within(0.0001f));
        }

        [Test]
        public void TimeOfDaySettings_DoNotApply_OnNonDirectionalLights()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.lightUnit = LightUnit.Candela;
            light.intensity = 7.0f;
            light.luxAtDistance = 5.0f;

            var additionalData = light.GetVividAdditionalLightData();

            additionalData.enableTimeOfDay = true;
            additionalData.timeOfDay = 18.0f;

            Assert.That(additionalData.enableTimeOfDay, Is.False);
            Assert.That(additionalData.supportsTimeOfDay, Is.False);
            Assert.That(light.type, Is.EqualTo(LightType.Point));
            Assert.That(light.lightUnit, Is.EqualTo(LightUnit.Candela));
            Assert.That(light.luxAtDistance, Is.EqualTo(5.0f));
            Assert.That(light.intensity, Is.EqualTo(7.0f));
        }

        [Test]
        public void EvaluateTimeOfDaySun_ProducesExpectedAzimuthAndLux()
        {
            var noon = VividAdditionalLightData.EvaluateTimeOfDaySun(
                12.0f,
                VividAdditionalLightData.DefaultTimeOfDayMaximumLux);

            Assert.That(noon.elevationDegrees, Is.EqualTo(90.0f).Within(0.0001f));
            Assert.That(noon.azimuthDegrees, Is.EqualTo(180.0f).Within(0.0001f));
            Assert.That(noon.directionToSun.y, Is.GreaterThan(0.99f));
            Assert.That(noon.lux, Is.EqualTo(VividAdditionalLightData.DefaultTimeOfDayMaximumLux).Within(0.1f));

            var sunrise = VividAdditionalLightData.EvaluateTimeOfDaySun(
                6.0f,
                VividAdditionalLightData.DefaultTimeOfDayMaximumLux);

            Assert.That(sunrise.elevationDegrees, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(sunrise.azimuthDegrees, Is.EqualTo(90.0f).Within(0.0001f));
            Assert.That(sunrise.directionToSun.x, Is.GreaterThan(0.99f));
            Assert.That(sunrise.lux, Is.EqualTo(0.0f).Within(0.0001f));
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
        public void VividLightEditor_ShowsDirectionalTimeOfDayControls_OnlyForDirectionalLights()
        {
            var directionalLight = m_GameObject.AddComponent<Light>();
            directionalLight.type = LightType.Directional;

            var serializedDirectionalLight = new VividSerializedLight(new SerializedObject(directionalLight));

            Assert.That(
                VividLightEditor.ShouldShowDirectionalTimeOfDayControls(serializedDirectionalLight),
                Is.True);

            var pointLightObject = new GameObject("Vivid Point Light Time Of Day Test");

            try
            {
                var pointLight = pointLightObject.AddComponent<Light>();
                pointLight.type = LightType.Point;

                var serializedPointLight = new VividSerializedLight(new SerializedObject(pointLight));

                Assert.That(
                    VividLightEditor.ShouldShowDirectionalTimeOfDayControls(serializedPointLight),
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
        public void VividLightEditor_ShowsPunctualShapeRadiusControls_ForPointAndConeSpotLights()
        {
            var spotLight = m_GameObject.AddComponent<Light>();
            spotLight.type = LightType.Spot;

            var serializedSpotLight = new VividSerializedLight(new SerializedObject(spotLight));

            Assert.That(
                VividLightEditor.ShouldShowPunctualShapeRadiusControls(serializedSpotLight),
                Is.True);

            var pointLightObject = new GameObject("Vivid Point Light Shape Radius Test");
            var directionalLightObject = new GameObject("Vivid Directional Light Shape Radius Test");
            var boxLightObject = new GameObject("Vivid Box Light Shape Radius Test");

            try
            {
                var pointLight = pointLightObject.AddComponent<Light>();
                pointLight.type = LightType.Point;
                var serializedPointLight = new VividSerializedLight(new SerializedObject(pointLight));

                Assert.That(
                    VividLightEditor.ShouldShowPunctualShapeRadiusControls(serializedPointLight),
                    Is.True);

                var directionalLight = directionalLightObject.AddComponent<Light>();
                directionalLight.type = LightType.Directional;
                var serializedDirectionalLight = new VividSerializedLight(new SerializedObject(directionalLight));

                Assert.That(
                    VividLightEditor.ShouldShowPunctualShapeRadiusControls(serializedDirectionalLight),
                    Is.False);

                var boxLight = boxLightObject.AddComponent<Light>();
                boxLight.type = LightType.Box;
                var serializedBoxLight = new VividSerializedLight(new SerializedObject(boxLight));

                Assert.That(
                    VividLightEditor.ShouldShowPunctualShapeRadiusControls(serializedBoxLight),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(pointLightObject);
                Object.DestroyImmediate(directionalLightObject);
                Object.DestroyImmediate(boxLightObject);
            }
        }

        [Test]
        public void VividLightEditor_ShowsSpotShapeControls_ForConeAndBoxSpotLights()
        {
            var spotLight = m_GameObject.AddComponent<Light>();
            spotLight.type = LightType.Spot;

            var serializedSpotLight = new VividSerializedLight(new SerializedObject(spotLight));

            Assert.That(
                VividLightEditor.ShouldShowSpotShapeControls(serializedSpotLight),
                Is.True);

            var boxLightObject = new GameObject("Vivid Box Light Spot Shape Test");
            var pointLightObject = new GameObject("Vivid Point Light Spot Shape Test");

            try
            {
                var boxLight = boxLightObject.AddComponent<Light>();
                boxLight.type = LightType.Box;
                var serializedBoxLight = new VividSerializedLight(new SerializedObject(boxLight));

                Assert.That(
                    VividLightEditor.ShouldShowSpotShapeControls(serializedBoxLight),
                    Is.True);

                var pointLight = pointLightObject.AddComponent<Light>();
                pointLight.type = LightType.Point;
                var serializedPointLight = new VividSerializedLight(new SerializedObject(pointLight));

                Assert.That(
                    VividLightEditor.ShouldShowSpotShapeControls(serializedPointLight),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(boxLightObject);
                Object.DestroyImmediate(pointLightObject);
            }
        }

        [Test]
        public void VividLightEditor_MapsSpotLightShape_ToUnityLightType()
        {
            Assert.That(
                VividLightEditor.GetSpotLightShape(LightType.Spot),
                Is.EqualTo(VividLightEditor.SpotLightShape.Cone));
            Assert.That(
                VividLightEditor.GetSpotLightShape(LightType.Box),
                Is.EqualTo(VividLightEditor.SpotLightShape.Box));
            Assert.That(
                VividLightEditor.GetLightTypeForSpotLightShape(VividLightEditor.SpotLightShape.Cone),
                Is.EqualTo(LightType.Spot));
            Assert.That(
                VividLightEditor.GetLightTypeForSpotLightShape(VividLightEditor.SpotLightShape.Box),
                Is.EqualTo(LightType.Box));
        }

        [Test]
        public void VividLightEditor_MapsBoxLightType_ToSpotGeneralType()
        {
            Assert.That(
                VividLightEditor.GetGeneralLightType(LightType.Box),
                Is.EqualTo(VividLightEditor.GeneralLightType.Spot));
            Assert.That(
                VividLightEditor.GetGeneralLightType(LightType.Spot),
                Is.EqualTo(VividLightEditor.GeneralLightType.Spot));
            Assert.That(
                VividLightEditor.GetGeneralLightType(LightType.Directional),
                Is.EqualTo(VividLightEditor.GeneralLightType.Directional));
        }

        [Test]
        public void VividLightEditor_MapsGeneralLightType_ToUnityLightType()
        {
            Assert.That(
                VividLightEditor.GetLightTypeForGeneralLightType(VividLightEditor.GeneralLightType.Spot),
                Is.EqualTo(LightType.Spot));
            Assert.That(
                VividLightEditor.GetLightTypeForGeneralLightType(VividLightEditor.GeneralLightType.Directional),
                Is.EqualTo(LightType.Directional));
            Assert.That(
                VividLightEditor.GetLightTypeForGeneralLightType(VividLightEditor.GeneralLightType.Point),
                Is.EqualTo(LightType.Point));
            Assert.That(
                VividLightEditor.GetLightTypeForGeneralLightType(VividLightEditor.GeneralLightType.Rectangle),
                Is.EqualTo(LightType.Rectangle));
            Assert.That(
                VividLightEditor.GetLightTypeForGeneralLightType(VividLightEditor.GeneralLightType.Disc),
                Is.EqualTo(LightType.Disc));
            Assert.That(
                VividLightEditor.GetLightTypeForGeneralLightType(VividLightEditor.GeneralLightType.Tube),
                Is.EqualTo(LightType.Tube));
        }

        [Test]
        public void VividLightEditor_DrawsBoxSpotLightGizmo_ForBoxLightsEvenWhenRangeIsZero()
        {
            var boxLight = m_GameObject.AddComponent<Light>();
            boxLight.type = LightType.Box;
            boxLight.range = 10.0f;

            Assert.That(
                VividLightEditor.ShouldDrawBoxSpotLightGizmo(boxLight),
                Is.True);

            boxLight.range = 0.0f;

            Assert.That(
                VividLightEditor.ShouldDrawBoxSpotLightGizmo(boxLight),
                Is.True);

            var spotLightObject = new GameObject("Vivid Spot Light Gizmo Test");

            try
            {
                var spotLight = spotLightObject.AddComponent<Light>();
                spotLight.type = LightType.Spot;
                spotLight.range = 10.0f;

                Assert.That(
                    VividLightEditor.ShouldDrawBoxSpotLightGizmo(spotLight),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(spotLightObject);
            }
        }

        [Test]
        public void VividLightEditor_SkipsCoreIntensityModifiers_ForBoxSpotLights()
        {
            var boxLight = m_GameObject.AddComponent<Light>();
            boxLight.type = LightType.Box;
            var settings = new LightEditor.Settings(new SerializedObject(boxLight));
            settings.OnEnable();
            settings.Update();
            settings.lightType.SetEnumValue(LightType.Spot);

            Assert.That(
                VividLightEditor.ShouldDrawBoxSpotLightIntensity(settings),
                Is.True);
            Assert.That(
                VividLightEditor.ShouldDrawCoreLightIntensityModifiers(settings),
                Is.False);

            var spotLightObject = new GameObject("Vivid Spot Light Intensity Test");

            try
            {
                var spotLight = spotLightObject.AddComponent<Light>();
                spotLight.type = LightType.Spot;
                var spotSettings = new LightEditor.Settings(new SerializedObject(spotLight));
                spotSettings.OnEnable();
                spotSettings.Update();

                Assert.That(
                    VividLightEditor.ShouldDrawBoxSpotLightIntensity(spotSettings),
                    Is.False);
                Assert.That(
                    VividLightEditor.ShouldDrawCoreLightIntensityModifiers(spotSettings),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(spotLightObject);
            }
        }

        [Test]
        public void VividLightEditor_BoxSpotLightGizmoCorners_UseAreaSizeAndRange()
        {
            var corners = VividLightEditor.GetBoxSpotLightGizmoLocalCorners(4.0f, 2.0f, 10.0f);

            Assert.That(corners, Has.Length.EqualTo(8));
            AssertVector3(corners[0], new Vector3(2.0f, 1.0f, 0.0f));
            AssertVector3(corners[1], new Vector3(-2.0f, 1.0f, 0.0f));
            AssertVector3(corners[2], new Vector3(-2.0f, -1.0f, 0.0f));
            AssertVector3(corners[3], new Vector3(2.0f, -1.0f, 0.0f));
            AssertVector3(corners[4], new Vector3(2.0f, 1.0f, 10.0f));
            AssertVector3(corners[5], new Vector3(-2.0f, 1.0f, 10.0f));
            AssertVector3(corners[6], new Vector3(-2.0f, -1.0f, 10.0f));
            AssertVector3(corners[7], new Vector3(2.0f, -1.0f, 10.0f));
        }

        [Test]
        public void VividLightEditor_SanitizesBoxSpotLightHandleValues_ToPositiveDimensions()
        {
            var sanitizedValues = VividLightEditor.SanitizeBoxSpotLightHandleValues(
                new Vector3(-1.0f, 0.0f, -2.0f));

            Assert.That(sanitizedValues.x, Is.GreaterThan(0.0f));
            Assert.That(sanitizedValues.y, Is.GreaterThan(0.0f));
            Assert.That(sanitizedValues.z, Is.GreaterThan(0.0f));
        }

        [Test]
        public void VividLightEditor_AppliesBoxSpotLightHandleValues_ToAreaSizeAndRange()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Box;
            light.areaSize = new Vector2(1.0f, 1.0f);
            light.range = 1.0f;

            var additionalData = light.GetVividAdditionalLightData();

            VividLightEditor.ApplyBoxSpotLightHandleValues(light, new Vector3(4.0f, 2.0f, 10.0f));

            Assert.That(light.areaSize.x, Is.EqualTo(4.0f).Within(0.0001f));
            Assert.That(light.areaSize.y, Is.EqualTo(2.0f).Within(0.0001f));
            Assert.That(light.range, Is.EqualTo(10.0f).Within(0.0001f));
            Assert.That(additionalData, Is.Not.Null);
            Assert.That(light.useBoundingSphereOverride, Is.True);
            Assert.That(light.boundingSphereOverride.w, Is.EqualTo(Mathf.Sqrt(105.0f)).Within(0.0001f));
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

            serializedDirectionalLight.screenSpaceShadowQuality.intValue =
                (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Unreal;

            Assert.That(
                VividLightEditor.ShouldShowDirectionalPCSSControls(serializedDirectionalLight),
                Is.False);

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
        public void VividLightEditor_ShowsDirectionalBendSSSControls_OnlyForDirectionalLightsUsingUnrealQuality()
        {
            var directionalLight = m_GameObject.AddComponent<Light>();
            directionalLight.type = LightType.Directional;

            var serializedDirectionalLight = new VividSerializedLight(new SerializedObject(directionalLight));

            Assert.That(
                VividLightEditor.ShouldShowDirectionalBendSSSControls(serializedDirectionalLight),
                Is.False);

            serializedDirectionalLight.screenSpaceShadowQuality.intValue =
                (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Unreal;

            Assert.That(
                VividLightEditor.ShouldShowDirectionalBendSSSControls(serializedDirectionalLight),
                Is.True);

            serializedDirectionalLight.screenSpaceShadowQuality.intValue =
                (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.VeryHigh;

            Assert.That(
                VividLightEditor.ShouldShowDirectionalBendSSSControls(serializedDirectionalLight),
                Is.False);

            var pointLightObject = new GameObject("Vivid Point Light Bend SSS Test");

            try
            {
                var pointLight = pointLightObject.AddComponent<Light>();
                pointLight.type = LightType.Point;

                var serializedPointLight = new VividSerializedLight(new SerializedObject(pointLight));
                serializedPointLight.screenSpaceShadowQuality.intValue =
                    (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Unreal;

                Assert.That(
                    VividLightEditor.ShouldShowDirectionalBendSSSControls(serializedPointLight),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(pointLightObject);
            }
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
        public void VividLightRenderDatabase_TracksDirectionalAngularDiameter()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Directional;
            var additionalData = light.GetVividAdditionalLightData();
            additionalData.angularDiameter = 1.25f;

            var trackedLightData =
                VividLightRenderDatabase.instance.UpdateLightData(
                    light,
                    additionalData);

            Assert.That(
                trackedLightData.angularDiameter,
                Is.EqualTo(1.25f).Within(0.000001f));
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
            Assert.That(trackedLightData.rangeAttenuationScale, Is.EqualTo(1.0f / 64.0f).Within(0.0001f));
            Assert.That(trackedLightData.rangeAttenuationBias, Is.EqualTo(1.0f).Within(0.0001f));
        }

        [Test]
        public void UpdateLightData_UsesNativePointLightIntensityForTrackedColor_WhenDisplayedAsLumen()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.lightUnit = LightUnit.Lumen;
            light.color = Color.white;
            light.intensity = 4.0f * Mathf.PI;

            var trackedLightData = VividLightRenderDatabase.instance.UpdateLightData(light, light.GetVividAdditionalLightData());

            Assert.That(trackedLightData.intensity, Is.EqualTo(4.0f * Mathf.PI).Within(0.0001f));
            Assert.That(trackedLightData.color.x, Is.EqualTo(4.0f * Mathf.PI).Within(0.0001f));
            Assert.That(trackedLightData.color.y, Is.EqualTo(4.0f * Mathf.PI).Within(0.0001f));
            Assert.That(trackedLightData.color.z, Is.EqualTo(4.0f * Mathf.PI).Within(0.0001f));
        }

        [Test]
        public void UpdateLightData_AppliesColorTemperature_WhenColorTemperatureEnabled()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.lightUnit = LightUnit.Lux;
            light.color = Color.white;
            light.intensity = 130000.0f;
            light.useColorTemperature = true;
            light.colorTemperature = 5500.0f;

            var trackedLightData = VividLightRenderDatabase.instance.UpdateLightData(light, light.GetVividAdditionalLightData());
            var expected = Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature) * light.intensity;

            Assert.That(trackedLightData.color.x, Is.EqualTo(expected.r).Within(0.1f));
            Assert.That(trackedLightData.color.y, Is.EqualTo(expected.g).Within(0.1f));
            Assert.That(trackedLightData.color.z, Is.EqualTo(expected.b).Within(0.1f));
        }

        [Test]
        public void UpdateLightData_IgnoresHiddenColorTint_WhenUsingColorTemperature()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.lightUnit = LightUnit.Lux;
            light.color = new Color(1.0f, 0.9431372f, 0.9f, 1.0f);
            light.intensity = 130000.0f;
            light.useColorTemperature = true;
            light.colorTemperature = 5500.0f;

            var trackedLightData = VividLightRenderDatabase.instance.UpdateLightData(light, light.GetVividAdditionalLightData());
            var expected = Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature) * light.intensity;

            Assert.That(trackedLightData.color.x, Is.EqualTo(expected.r).Within(0.1f));
            Assert.That(trackedLightData.color.y, Is.EqualTo(expected.g).Within(0.1f));
            Assert.That(trackedLightData.color.z, Is.EqualTo(expected.b).Within(0.1f));
        }

        [Test]
        public void UpdateLightData_UsesNativeSpotLightIntensityForTrackedColor_WhenDisplayedAsLumen()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.lightUnit = LightUnit.Lumen;
            light.enableSpotReflector = true;
            light.spotAngle = 60.0f;
            light.color = Color.white;
            light.intensity = 7.0f;

            var trackedLightData = VividLightRenderDatabase.instance.UpdateLightData(light, light.GetVividAdditionalLightData());

            Assert.That(trackedLightData.color.x, Is.EqualTo(7.0f).Within(0.0001f));
            Assert.That(trackedLightData.color.y, Is.EqualTo(7.0f).Within(0.0001f));
            Assert.That(trackedLightData.color.z, Is.EqualTo(7.0f).Within(0.0001f));
        }

        [Test]
        public void UpdateLightData_KeepsTrackedColorStable_WhenDisplayUnitChanges()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = Color.white;
            light.intensity = 5.0f;

            var database = VividLightRenderDatabase.instance;

            light.lightUnit = LightUnit.Candela;
            var candelaData = database.UpdateLightData(light, light.GetVividAdditionalLightData());

            light.lightUnit = LightUnit.Lumen;
            var lumenData = database.UpdateLightData(light, light.GetVividAdditionalLightData());

            light.lightUnit = LightUnit.Lux;
            var luxData = database.UpdateLightData(light, light.GetVividAdditionalLightData());

            Assert.That(candelaData.color.x, Is.EqualTo(5.0f).Within(0.0001f));
            Assert.That(lumenData.color.x, Is.EqualTo(candelaData.color.x).Within(0.0001f));
            Assert.That(luxData.color.x, Is.EqualTo(candelaData.color.x).Within(0.0001f));
        }

        [Test]
        public void CompleteSceneLightPrepare_BuildsPreparedSceneSnapshot_WhenPlayerLoopDidNotRun()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.lightUnit = LightUnit.Candela;
            light.color = Color.white;
            light.intensity = 1.0f;
            light.range = 4.0f;
            light.transform.position = new Vector3(1.0f, 0.0f, 0.0f);

            var additionalData = light.GetVividAdditionalLightData();
            var database = VividLightRenderDatabase.instance;

            database.Clear();
            database.RegisterLight(additionalData);

            light.intensity = 4.0f;
            light.range = 8.0f;
            light.transform.position = new Vector3(3.0f, 0.0f, 0.0f);

            database.CompleteSceneLightPrepare();

            Assert.That(database.sceneLightData.Count, Is.EqualTo(1));
            Assert.That(database.sceneLightData[0].intensity, Is.EqualTo(4.0f).Within(0.0001f));
            Assert.That(database.sceneLightData[0].range, Is.EqualTo(8.0f).Within(0.0001f));
            Assert.That(database.sceneLightData[0].positionWS.x, Is.EqualTo(3.0f).Within(0.0001f));
        }

        [Test]
        public void BuildSceneLightSnapshotAndSchedulePrepare_SkipsDisabledLights()
        {
            var light = m_GameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 4.0f;

            var additionalData = light.GetVividAdditionalLightData();
            var database = VividLightRenderDatabase.instance;

            database.Clear();
            database.RegisterLight(additionalData);

            light.enabled = false;

            database.BuildSceneLightSnapshotAndSchedulePrepare(false);
            database.CompleteSceneLightPrepare();

            Assert.That(database.sceneLightData.Count, Is.Zero);
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

        private static void AssertVector3(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f));
        }
    }
}

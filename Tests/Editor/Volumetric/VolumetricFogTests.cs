using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class VolumetricFogTests
    {
        [Test]
        public void VividVolumetricFogVolume_IsActive_ReturnsEnabledState()
        {
            var fog = ScriptableObject.CreateInstance<VividVolumetricFogVolume>();

            try
            {
                fog.enabled.value = false;
                Assert.That(fog.IsActive(), Is.False);

                fog.enabled.value = true;
                Assert.That(fog.IsActive(), Is.True);

                fog.volumetricFog.value = false;
                Assert.That(fog.IsActive(), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(fog);
            }
        }

        [Test]
        public void VividVolumetricFogVolume_DefaultsMatchHdrpFogPanel()
        {
            var fog = ScriptableObject.CreateInstance<VividVolumetricFogVolume>();

            try
            {
                Assert.That(fog.meanFreePath.value, Is.EqualTo(VividVolumetricFogVolume.DefaultMeanFreePath));
                Assert.That(fog.maximumHeight.value, Is.EqualTo(500.0f));
                Assert.That(fog.maxFogDistance.value, Is.EqualTo(VividVolumetricFogVolume.DefaultMaxFogDistance));
                Assert.That(fog.volumetricFog.value, Is.True);
                Assert.That(fog.globalLightProbeDimmer.value, Is.EqualTo(0.0f));
                Assert.That(fog.depthExtent.value, Is.EqualTo(VividVolumetricFogVolume.DefaultDepthExtent));
                Assert.That(fog.denoisingMode.value, Is.EqualTo(VividVolumetricFogDenoisingMode.Both));
                Assert.That(fog.sliceDistributionUniformity.value, Is.EqualTo(1.0f));
                Assert.That(fog.tier.value, Is.EqualTo(VividVolumetricFogQualityTier.Custom));
                Assert.That(fog.volumetricFogBudget.value, Is.EqualTo(VividVolumetricFogVolume.DefaultVolumetricFogBudget));
                Assert.That(fog.volumetricLightingDensityCutoff.value, Is.EqualTo(0.0f));
                Assert.That(fog.multipleScatteringIntensity.value, Is.EqualTo(0.0f));
            }
            finally
            {
                Object.DestroyImmediate(fog);
            }
        }

        [Test]
        public void VividVolumetricFogVolumeEditor_UsesCustomEditor()
        {
            var fog = ScriptableObject.CreateInstance<VividVolumetricFogVolume>();
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(fog);

                Assert.That(editor, Is.Not.Null);
                Assert.That(editor.GetType().Name, Is.EqualTo("VividVolumetricFogVolumeEditor"));
            }
            finally
            {
                if (editor != null)
                    Object.DestroyImmediate(editor);

                Object.DestroyImmediate(fog);
            }
        }

        [Test]
        public void VividVolumetricFogVolumeEditor_OnEnable_InitializesSerializedParameters()
        {
            var fog = ScriptableObject.CreateInstance<VividVolumetricFogVolume>();
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(fog);

                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var editorType = editor.GetType();
                Assert.That(editorType.GetField("m_Enabled", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_MeanFreePath", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_BaseHeight", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_MaximumHeight", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_MaxFogDistance", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_ColorMode", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_Tint", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_MipFogNear", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_MipFogFar", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_MipFogMaxMip", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_VolumetricFog", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_Albedo", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_GlobalLightProbeDimmer", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_DepthExtent", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_Tier", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_FogControlMode", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_VolumetricFogBudget", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_ResolutionDepthRatio", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_ScreenResolutionPercentage", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_VolumeSliceCount", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_DenoisingMode", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_DirectionalLightsOnly", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_Anisotropy", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_VolumetricLightingDensityCutoff", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_MultipleScatteringIntensity", flags)?.GetValue(editor), Is.Not.Null);
            }
            finally
            {
                if (editor != null)
                    Object.DestroyImmediate(editor);

                Object.DestroyImmediate(fog);
            }
        }

        [Test]
        public void VividVolumetricFogDenoisingMode_UsesHdrpPanelOptions()
        {
            Assert.That((int)VividVolumetricFogDenoisingMode.None, Is.EqualTo(0));
            Assert.That((int)VividVolumetricFogDenoisingMode.Gaussian, Is.EqualTo(1));
            Assert.That((int)VividVolumetricFogDenoisingMode.Reprojection, Is.EqualTo(2));
            Assert.That((int)VividVolumetricFogDenoisingMode.Both, Is.EqualTo(3));
            Assert.That(VividVolumetricUtility.UsesTemporalReprojection(VividVolumetricFogDenoisingMode.Reprojection), Is.True);
            Assert.That(VividVolumetricUtility.UsesTemporalReprojection(VividVolumetricFogDenoisingMode.Both), Is.True);
            Assert.That(VividVolumetricUtility.UsesTemporalReprojection(VividVolumetricFogDenoisingMode.Gaussian), Is.False);
        }

        [Test]
        public void ResolveQuality_UsesManualResolutionAndSlices_WhenManual()
        {
            var fog = ScriptableObject.CreateInstance<VividVolumetricFogVolume>();

            try
            {
                fog.tier.value = VividVolumetricFogQualityTier.Custom;
                fog.fogControlMode.value = VividVolumetricFogControlMode.Manual;
                fog.screenResolutionPercentage.value = 25.0f;
                fog.volumeSliceCount.value = 32;

                VividVolumetricUtility.ResolveQuality(fog, out var screenPercentage, out var sliceCount);

                Assert.That(screenPercentage, Is.EqualTo(25.0f).Within(0.0001f));
                Assert.That(sliceCount, Is.EqualTo(32));
            }
            finally
            {
                Object.DestroyImmediate(fog);
            }
        }

        [Test]
        public void ResolveQuality_UsesHdrpBalanceFormula_WhenBalance()
        {
            var fog = ScriptableObject.CreateInstance<VividVolumetricFogVolume>();

            try
            {
                fog.tier.value = VividVolumetricFogQualityTier.Custom;
                fog.fogControlMode.value = VividVolumetricFogControlMode.Balance;
                fog.volumetricFogBudget.value = 0.25f;
                fog.resolutionDepthRatio.value = 0.5f;

                VividVolumetricUtility.ResolveQuality(fog, out var screenPercentage, out var sliceCount);

                Assert.That(screenPercentage, Is.EqualTo(11.71875f).Within(0.0001f));
                Assert.That(sliceCount, Is.EqualTo(64));
            }
            finally
            {
                Object.DestroyImmediate(fog);
            }
        }

        [Test]
        public void ResolveQuality_UsesTierPreset_WhenTierIsNotCustom()
        {
            var fog = ScriptableObject.CreateInstance<VividVolumetricFogVolume>();

            try
            {
                fog.tier.value = VividVolumetricFogQualityTier.Medium;
                fog.fogControlMode.value = VividVolumetricFogControlMode.Manual;
                fog.screenResolutionPercentage.value = 50.0f;
                fog.volumeSliceCount.value = 512;

                VividVolumetricUtility.ResolveQuality(fog, out var screenPercentage, out var sliceCount);

                Assert.That(screenPercentage, Is.EqualTo(VividVolumetricFogVolume.DefaultScreenResolutionPercentage));
                Assert.That(sliceCount, Is.EqualTo(VividVolumetricFogVolume.DefaultVolumeSliceCount));
            }
            finally
            {
                Object.DestroyImmediate(fog);
            }
        }

        [Test]
        public void VividRenderPipelineGlobalSettings_ClampsLocalFogCapacity()
        {
            var globalSettings = ScriptableObject.CreateInstance<VividRenderPipelineGlobalSettings>();

            try
            {
                globalSettings.MaxLocalVolumetricFogCount = 4096;

                Assert.That(globalSettings.MaxLocalVolumetricFogCount, Is.EqualTo(VividLocalVolumetricFogManager.AbsoluteMaxVisibleLocalVolumetricFogCount));
            }
            finally
            {
                Object.DestroyImmediate(globalSettings);
            }
        }

        [Test]
        public void ResolveMaxLocalVolumetricFogCount_UsesGlobalSettings()
        {
            var globalSettings = ScriptableObject.CreateInstance<VividRenderPipelineGlobalSettings>();

            try
            {
                globalSettings.MaxLocalVolumetricFogCount = 7;

                Assert.That(
                    VividVolumetricUtility.ResolveMaxLocalVolumetricFogCount(globalSettings),
                    Is.EqualTo(7));
                Assert.That(
                    VividVolumetricUtility.ResolveMaxLocalVolumetricFogCount(null),
                    Is.EqualTo(VividLocalVolumetricFogManager.DefaultMaxVisibleLocalVolumetricFogCount));
            }
            finally
            {
                Object.DestroyImmediate(globalSettings);
            }
        }

        [Test]
        public void ComputeVBufferParameters_UsesHdrpDefaultFractionAndEncodesDepth()
        {
            var parameters = VividVolumetricUtility.ComputeVBufferParameters(
                1920,
                1080,
                VividVolumetricFogVolume.DefaultScreenResolutionPercentage,
                64,
                100.0f,
                0.5f);

            Assert.That(parameters.ViewportWidth, Is.EqualTo(240));
            Assert.That(parameters.ViewportHeight, Is.EqualTo(135));
            Assert.That(parameters.SliceCount, Is.EqualTo(64));
            Assert.That(parameters.DepthEncodingParams.z, Is.EqualTo(-0.7f).Within(0.0001f));
            Assert.That(parameters.DepthDecodingParams.x, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(parameters.DecodeLogarithmicDepth(parameters.EncodeLogarithmicDepth(10.0f)), Is.EqualTo(10.0f).Within(0.0001f));
            Assert.That(parameters.ComputeSliceLength(63), Is.GreaterThan(parameters.ComputeSliceLength(0)));
            Assert.That(parameters.LastSliceDistance, Is.GreaterThan(parameters.NearClipPlane));
            Assert.That(parameters.LastSliceDistance, Is.LessThan(parameters.NearClipPlane + parameters.DepthExtent));
        }

        [Test]
        public void BuildShaderVariables_EncodesHdrpVBufferGeometry()
        {
            var gameObject = new GameObject("Volumetric Camera");
            var camera = gameObject.AddComponent<Camera>();
            camera.fieldOfView = 60.0f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000.0f;
            var cameraData = new VividCameraData
            {
                camera = camera,
                actualWidth = 1920,
                actualHeight = 1080,
                pixelWidth = 1920,
                pixelHeight = 1080
            };

            try
            {
                var settings = VividVolumetricFogSettings.Disabled(1920, 1080);
                var shaderVariables = VividVolumetricUtility.BuildShaderVariables(settings, 1920, 1080, 0, cameraData);
                var vBuffer = settings.VBufferParameters;

                Assert.That(shaderVariables._VBufferDepthEncodingParams, Is.EqualTo(vBuffer.DepthEncodingParams));
                Assert.That(shaderVariables._VBufferDepthDecodingParams, Is.EqualTo(vBuffer.DepthDecodingParams));
                Assert.That(shaderVariables._VBufferLightingViewportScale.x, Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(shaderVariables._VBufferLightingViewportScale.y, Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(shaderVariables._VBufferLightingViewportScale.z, Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(shaderVariables._VBufferLightingViewportLimit.x, Is.EqualTo((vBuffer.ViewportWidth - 0.5f) / vBuffer.ViewportWidth).Within(0.0001f));
                Assert.That(shaderVariables._VBufferLightingViewportLimit.y, Is.EqualTo((vBuffer.ViewportHeight - 0.5f) / vBuffer.ViewportHeight).Within(0.0001f));
                Assert.That(shaderVariables._VBufferLightingViewportLimit.z, Is.EqualTo((vBuffer.SliceCount - 0.5f) / vBuffer.SliceCount).Within(0.0001f));
                Assert.That(shaderVariables._VBufferGeometryParams.x, Is.EqualTo(vBuffer.UnitDepthTexelSpacing).Within(0.0001f));
                Assert.That(shaderVariables._VBufferGeometryParams.z, Is.EqualTo(vBuffer.LastSliceDistance).Within(0.0001f));
                Assert.That(shaderVariables._VBufferGeometryParams.w, Is.EqualTo(0.0f).Within(0.0001f));
                Assert.That(shaderVariables._VBufferLocalFogParams.z, Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(shaderVariables._VBufferCoordToViewDirWS.m00, Is.Not.EqualTo(0.0f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void BuildShaderVariables_EncodesHdrpHeightFogDensityModel()
        {
            var vBuffer = VividVolumetricUtility.ComputeVBufferParameters(
                1920,
                1080,
                VividVolumetricFogVolume.DefaultScreenResolutionPercentage,
                64,
                100.0f,
                0.5f);
            var settings = new VividVolumetricFogSettings(
                true,
                Vector3.one,
                1.0f,
                10.0f,
                60.0f,
                0.25f,
                1.0f,
                100.0f,
                0.5f,
                VividVolumetricFogDenoisingMode.None,
                true,
                false,
                0.0f,
                vBuffer);

            var shaderVariables = VividVolumetricUtility.BuildShaderVariables(settings, 1920, 1080, 0);
            var scaleHeight = VividVolumetricUtility.ComputeHeightFogScaleHeight(10.0f, 60.0f);

            Assert.That(scaleHeight, Is.EqualTo(50.0f * VividVolumetricUtility.HeightFogScaleHeightFromLayerDepth).Within(0.0001f));
            Assert.That(VividVolumetricUtility.ComputeHeightFogMultiplier(5.0f, 10.0f, 60.0f), Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(VividVolumetricUtility.ComputeHeightFogMultiplier(10.0f, 10.0f, 60.0f), Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(VividVolumetricUtility.ComputeHeightFogMultiplier(60.0f, 10.0f, 60.0f), Is.EqualTo(0.001f).Within(0.0001f));
            Assert.That(shaderVariables._VBufferFogHeightParams.x, Is.EqualTo(10.0f).Within(0.0001f));
            Assert.That(shaderVariables._VBufferFogHeightParams.y, Is.EqualTo(60.0f).Within(0.0001f));
            Assert.That(shaderVariables._VBufferFogHeightParams.z, Is.EqualTo(1.0f / scaleHeight).Within(0.0001f));
            Assert.That(shaderVariables._VBufferFogHeightParams.w, Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public void BuildShaderVariables_ClampsDensityCutoffToNonNegative()
        {
            var vBuffer = VividVolumetricUtility.ComputeVBufferParameters(
                1920,
                1080,
                VividVolumetricFogVolume.DefaultScreenResolutionPercentage,
                64,
                100.0f,
                0.5f);
            var settings = new VividVolumetricFogSettings(
                true,
                Vector3.one,
                1.0f,
                0.0f,
                50.0f,
                0.0f,
                1.0f,
                100.0f,
                0.5f,
                VividVolumetricFogDenoisingMode.None,
                true,
                false,
                -1.0f,
                vBuffer);

            var shaderVariables = VividVolumetricUtility.BuildShaderVariables(settings, 1920, 1080, 0);

            Assert.That(shaderVariables._VBufferFogControlParams.z, Is.EqualTo(0.0f).Within(0.0001f));
        }

        [Test]
        public void ComputeMaxZDilationRadius_UsesHdrpScreenRatioThresholds()
        {
            Assert.That(VividVolumetricUtility.ComputeMaxZDilationRadius(6.25f), Is.EqualTo(2));
            Assert.That(VividVolumetricUtility.ComputeMaxZDilationRadius(12.5f), Is.EqualTo(1));
            Assert.That(VividVolumetricUtility.ComputeMaxZDilationRadius(50.0f), Is.EqualTo(0));
        }

        [Test]
        public void LocalVolumetricFog_ConvertToEngineData_EncodesScatteringAndFade()
        {
            var gameObject = new GameObject("Local Volumetric Fog");
            var fog = gameObject.AddComponent<VividLocalVolumetricFog>();
            SetLocalFogBoundProxy(fog, new BoundProxyShape
            {
                shape = BoundProxyShapeType.Box,
                size = new Vector3(10.0f, 20.0f, 40.0f)
            });

            try
            {
                var parameters = VividLocalVolumetricFogArtistParameters.CreateDefault();
                parameters.albedo = new Color(0.5f, 0.25f, 0.125f);
                parameters.meanFreePath = 10.0f;
                parameters.anisotropy = 0.35f;
                parameters.positiveFade = new Vector3(0.1f, 0.2f, 0.4f);
                parameters.negativeFade = new Vector3(0.2f, 0.4f, 0.8f);
                fog.parameters = parameters;

                var data = fog.ConvertToEngineData(null);

                Assert.That(data.scatteringExtinction.w, Is.EqualTo(0.1f).Within(0.0001f));
                Assert.That(data.scatteringExtinction.x, Is.EqualTo(0.05f).Within(0.0001f));
                Assert.That(data.positiveFade.x, Is.EqualTo(10.0f).Within(0.0001f));
                Assert.That(data.negativeFade.z, Is.EqualTo(1.25f).Within(0.0001f));
                Assert.That(data.distanceFade.x, Is.GreaterThan(0.0f));
                Assert.That(data.parameters.x, Is.EqualTo(0.35f).Within(0.0001f));
                Assert.That(data.parameters.y, Is.EqualTo((float)VividLocalVolumetricFogBlendingMode.Additive));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void LocalVolumetricFog_ConvertToEngineData_UsesEditorBlendDistanceWhenRuntimeFadeIsStale()
        {
            var gameObject = new GameObject("Local Volumetric Fog Editor Fade");
            var fog = gameObject.AddComponent<VividLocalVolumetricFog>();
            SetLocalFogBoundProxy(fog, new BoundProxyShape
            {
                shape = BoundProxyShapeType.Box,
                size = new Vector3(10.0f, 20.0f, 40.0f)
            });

            try
            {
                var parameters = VividLocalVolumetricFogArtistParameters.CreateDefault();
                parameters.m_EditorAdvancedFade = false;
                parameters.m_EditorUniformFade = 2.0f;
                parameters.positiveFade = Vector3.one * 0.1f;
                parameters.negativeFade = Vector3.one * 0.1f;
                SetLocalFogRawParameters(fog, parameters);

                var data = fog.ConvertToEngineData(null);

                Assert.That(data.positiveFade.x, Is.EqualTo(5.0f).Within(0.0001f));
                Assert.That(data.positiveFade.y, Is.EqualTo(10.0f).Within(0.0001f));
                Assert.That(data.positiveFade.z, Is.EqualTo(20.0f).Within(0.0001f));
                Assert.That(data.negativeFade.x, Is.EqualTo(5.0f).Within(0.0001f));
                Assert.That(data.negativeFade.y, Is.EqualTo(10.0f).Within(0.0001f));
                Assert.That(data.negativeFade.z, Is.EqualTo(20.0f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void LocalVolumetricFog_EnumsMatchHdrpBlendOrdering()
        {
            Assert.That((int)VividLocalVolumetricFogBlendingMode.Overwrite, Is.EqualTo(0));
            Assert.That((int)VividLocalVolumetricFogBlendingMode.Additive, Is.EqualTo(1));
            Assert.That((int)VividLocalVolumetricFogBlendingMode.Multiply, Is.EqualTo(2));
            Assert.That((int)VividLocalVolumetricFogBlendingMode.Min, Is.EqualTo(3));
            Assert.That((int)VividLocalVolumetricFogBlendingMode.Max, Is.EqualTo(4));
        }

        [Test]
        public void LocalVolumetricFog_DefaultsMatchHdrpArtistParameters()
        {
            var parameters = VividLocalVolumetricFogArtistParameters.CreateDefault();

            Assert.That(parameters.blendingMode, Is.EqualTo(VividLocalVolumetricFogBlendingMode.Additive));
            Assert.That(parameters.maskMode, Is.EqualTo(VividLocalVolumetricFogMaskMode.Texture));
            Assert.That(parameters.positiveFade, Is.EqualTo(Vector3.one * 0.1f));
            Assert.That(parameters.negativeFade, Is.EqualTo(Vector3.one * 0.1f));
            Assert.That(parameters.m_EditorUniformFade, Is.EqualTo(0.1f));
            Assert.That(parameters.m_EditorPositiveFade, Is.EqualTo(Vector3.one * 0.1f));
            Assert.That(parameters.m_EditorNegativeFade, Is.EqualTo(Vector3.one * 0.1f));
            Assert.That(parameters.m_EditorAdvancedFade, Is.False);
            Assert.That(parameters.distanceFadeStart, Is.EqualTo(10000.0f));
            Assert.That(parameters.distanceFadeEnd, Is.EqualTo(10000.0f));
        }

        [Test]
        public void LocalVolumetricFog_ApplyEditorFade_ConvertsUniformDistanceToNormalizedFaceFade()
        {
            var parameters = VividLocalVolumetricFogArtistParameters.CreateDefault();
            parameters.m_EditorUniformFade = 2.0f;
            parameters.m_EditorAdvancedFade = false;

            parameters.ApplyEditorFade(new Vector3(10.0f, 20.0f, 40.0f));

            AssertVector3(parameters.positiveFade, new Vector3(0.2f, 0.1f, 0.05f));
            AssertVector3(parameters.negativeFade, new Vector3(0.2f, 0.1f, 0.05f));
        }

        [Test]
        public void LocalVolumetricFog_ApplyEditorFade_UsesAdvancedPerAxisFade()
        {
            var parameters = VividLocalVolumetricFogArtistParameters.CreateDefault();
            parameters.m_EditorAdvancedFade = true;
            parameters.m_EditorPositiveFade = new Vector3(0.8f, 0.6f, 0.2f);
            parameters.m_EditorNegativeFade = new Vector3(0.6f, 0.7f, 0.3f);

            parameters.ApplyEditorFade(Vector3.one);

            Assert.That(parameters.positiveFade.x + parameters.negativeFade.x, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(parameters.positiveFade.y + parameters.negativeFade.y, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(parameters.positiveFade.z + parameters.negativeFade.z, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(parameters.positiveFade, Is.EqualTo(parameters.m_EditorPositiveFade));
            Assert.That(parameters.negativeFade, Is.EqualTo(parameters.m_EditorNegativeFade));
        }

        [Test]
        public void LocalVolumetricFog_OnAfterDeserialize_InitializesEditorFadeFromVersionOneRuntimeFade()
        {
            var gameObject = new GameObject("Local Volumetric Fog Migration");
            var fog = gameObject.AddComponent<VividLocalVolumetricFog>();
            SetLocalFogBoundProxy(fog, new BoundProxyShape
            {
                shape = BoundProxyShapeType.Box,
                size = new Vector3(10.0f, 20.0f, 40.0f)
            });

            try
            {
                var parameters = VividLocalVolumetricFogArtistParameters.CreateDefault();
                parameters.positiveFade = new Vector3(0.2f, 0.1f, 0.05f);
                parameters.negativeFade = new Vector3(0.2f, 0.1f, 0.05f);
                SetLocalFogRawParameters(fog, parameters);
                SetLocalFogSerializationVersion(fog, 1);

                fog.OnAfterDeserialize();

                var migratedParameters = fog.parameters;
                Assert.That(migratedParameters.m_EditorUniformFade, Is.EqualTo(2.0f).Within(0.0001f));
                Assert.That(migratedParameters.m_EditorAdvancedFade, Is.False);
                AssertVector3(migratedParameters.m_EditorPositiveFade, new Vector3(0.2f, 0.1f, 0.05f));
                AssertVector3(migratedParameters.m_EditorNegativeFade, new Vector3(0.2f, 0.1f, 0.05f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void VividLocalVolumetricFogEditor_BlendVolumeUtility_ComputesUniformAndAdvancedBoxes()
        {
            var gameObject = new GameObject("Local Volumetric Fog Blend Tool");
            var fog = gameObject.AddComponent<VividLocalVolumetricFog>();
            var shape = new BoundProxyShape
            {
                shape = BoundProxyShapeType.Box,
                center = new Vector3(1.0f, 2.0f, 3.0f),
                size = new Vector3(10.0f, 20.0f, 40.0f)
            };
            SetLocalFogBoundProxy(fog, shape);

            try
            {
                var parameters = VividLocalVolumetricFogArtistParameters.CreateDefault();
                parameters.m_EditorAdvancedFade = false;
                parameters.m_EditorUniformFade = 2.0f;
                SetLocalFogRawParameters(fog, parameters);

                AssertVector3(VividLocalVolumetricFogEditor.CenterBlendLocalPosition(fog), shape.center);
                AssertVector3(VividLocalVolumetricFogEditor.BlendSize(fog), new Vector3(6.0f, 16.0f, 36.0f));

                parameters.m_EditorAdvancedFade = true;
                parameters.m_EditorPositiveFade = new Vector3(0.1f, 0.2f, 0.25f);
                parameters.m_EditorNegativeFade = new Vector3(0.3f, 0.1f, 0.25f);
                SetLocalFogRawParameters(fog, parameters);

                AssertVector3(VividLocalVolumetricFogEditor.CenterBlendLocalPosition(fog), new Vector3(2.0f, 1.0f, 3.0f));
                AssertVector3(VividLocalVolumetricFogEditor.BlendSize(fog), new Vector3(6.0f, 14.0f, 20.0f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void LocalVolumetricFogManager_VisibilityStateControlsRegistration()
        {
            var gameObject = new GameObject("Local Volumetric Fog Visibility");
            var fog = gameObject.AddComponent<VividLocalVolumetricFog>();

            try
            {
                Assert.That(VividLocalVolumetricFogManager.Contains(fog), Is.True);

                fog.UpdateLocalVolumetricFogVisibility(false);
                Assert.That(VividLocalVolumetricFogManager.Contains(fog), Is.False);

                fog.UpdateLocalVolumetricFogVisibility(true);
                Assert.That(VividLocalVolumetricFogManager.Contains(fog), Is.True);

                fog.enabled = false;
                Assert.That(VividLocalVolumetricFogManager.Contains(fog), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void LocalVolumetricFogManager_PrepareVisibleFogs_UsesRequestedBufferCapacity()
        {
            var fogObjects = new[]
            {
                new GameObject("Local Volumetric Fog Capacity 0"),
                new GameObject("Local Volumetric Fog Capacity 1"),
                new GameObject("Local Volumetric Fog Capacity 2")
            };

            try
            {
                foreach (var fogObject in fogObjects)
                    fogObject.AddComponent<VividLocalVolumetricFog>();

                int visibleCount = VividLocalVolumetricFogManager.PrepareVisibleFogs(null, 2);

                Assert.That(visibleCount, Is.EqualTo(2));
                Assert.That(VividLocalVolumetricFogManager.volumeBoundsBuffer.desc.Count, Is.EqualTo(2));
                Assert.That(VividLocalVolumetricFogManager.visibleGlobalIndicesBuffer.desc.Count, Is.EqualTo(2));
                Assert.That(VividLocalVolumetricFogManager.globalIndirectArgsBuffer.desc.Count, Is.EqualTo(2));
                Assert.That(VividLocalVolumetricFogManager.globalIndirectionBuffer.desc.Count, Is.EqualTo(2));
                Assert.That(VividLocalVolumetricFogManager.volumetricMaterialDataBuffer.desc.Count, Is.EqualTo(2 * VividLocalVolumetricFogManager.MaxVolumetricMaterialViewCount));

                visibleCount = VividLocalVolumetricFogManager.PrepareVisibleFogs(null, 0);

                Assert.That(visibleCount, Is.EqualTo(0));
                Assert.That(VividLocalVolumetricFogManager.volumeBoundsBuffer.desc.Count, Is.EqualTo(1));
                Assert.That(VividLocalVolumetricFogManager.volumetricMaterialDataBuffer.desc.Count, Is.EqualTo(VividLocalVolumetricFogManager.MaxVolumetricMaterialViewCount));
            }
            finally
            {
                foreach (var fogObject in fogObjects)
                    Object.DestroyImmediate(fogObject);

                VividLocalVolumetricFogManager.PrepareVisibleFogs(
                    null,
                    VividLocalVolumetricFogManager.DefaultMaxVisibleLocalVolumetricFogCount);
                VividLocalVolumetricFogManager.Dispose();
            }
        }

        [Test]
        public void LocalVolumetricFogManager_ResolveFrustumPlanes_ReusesPlaneArray()
        {
            var cameraObject = new GameObject("Local Volumetric Fog Frustum Camera");

            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                var planes = VividLocalVolumetricFogManager.ResolveFrustumPlanes(camera);

                System.GC.Collect();
                var allocatedBefore = System.GC.GetAllocatedBytesForCurrentThread();
                var reusedPlaneArray = true;
                for (var index = 0; index < 32; index++)
                    reusedPlaneArray &= ReferenceEquals(VividLocalVolumetricFogManager.ResolveFrustumPlanes(camera), planes);

                var allocatedBytes = System.GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                Assert.That(reusedPlaneArray, Is.True);
                Assert.That(allocatedBytes, Is.Zero);
                Assert.That(VividLocalVolumetricFogManager.ResolveFrustumPlanes(null), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void CreateLocalVolumetricFogGameObject_AddsFogSelectsObjectAndParentsToContext()
        {
            var parent = new GameObject("Local Fog Menu Parent");
            GameObject fogObject = null;

            try
            {
                fogObject = VividLocalVolumetricFogMenuItems.CreateLocalVolumetricFogGameObject(parent);

                Assert.That(fogObject.name, Is.EqualTo("Local Volumetric Fog"));
                Assert.That(fogObject.transform.parent, Is.EqualTo(parent.transform));
                Assert.That(fogObject.GetComponent<VividLocalVolumetricFog>(), Is.Not.Null);
                Assert.That(UnityEditor.Selection.activeGameObject, Is.EqualTo(fogObject));
                AssertVector3(fogObject.GetComponent<VividLocalVolumetricFog>().BoundProxyShape.GetSanitizedSize(), Vector3.one);
            }
            finally
            {
                UnityEditor.Selection.activeGameObject = null;

                if (fogObject != null)
                    Object.DestroyImmediate(fogObject);

                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void LocalVolumetricFog_TryCreateBoundProxyWorldData_UsesLocalVolumetricFogFeature()
        {
            var gameObject = new GameObject("Local Volumetric Fog Bounds");
            gameObject.transform.position = new Vector3(3.0f, 4.0f, 5.0f);
            gameObject.transform.rotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
            var fog = gameObject.AddComponent<VividLocalVolumetricFog>();
            var shape = new BoundProxyShape
            {
                shape = BoundProxyShapeType.Box,
                center = new Vector3(1.0f, 2.0f, 3.0f),
                size = new Vector3(10.0f, 20.0f, 40.0f),
                radius = 8.0f
            };
            SetLocalFogBoundProxy(fog, shape);

            try
            {
                bool created = fog.TryCreateBoundProxyWorldData(out BoundProxyWorldData worldData);

                Assert.That(created, Is.True);
                Assert.That(worldData.feature, Is.EqualTo(BoundProxyFeature.LocalVolumetricFog));
                Assert.That(worldData.shape, Is.EqualTo(BoundProxyShapeType.Box));
                AssertVector3(worldData.worldCenter, gameObject.transform.position + gameObject.transform.rotation * shape.center);
                AssertVector3(worldData.boxSize, shape.size);
                Assert.That(worldData.sphereRadius, Is.EqualTo(0.0f));
                Assert.That(worldData.worldAabb.size.x, Is.GreaterThan(0.0f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void LocalVolumetricFog_GetBounds_UsesBoundProxyWorldAabb()
        {
            var gameObject = new GameObject("Local Volumetric Fog Bounds");
            gameObject.transform.position = new Vector3(3.0f, 4.0f, 5.0f);
            gameObject.transform.rotation = Quaternion.Euler(0.0f, 45.0f, 0.0f);
            var fog = gameObject.AddComponent<VividLocalVolumetricFog>();
            var shape = new BoundProxyShape
            {
                shape = BoundProxyShapeType.Box,
                center = new Vector3(0.0f, 1.0f, 0.0f),
                size = new Vector3(2.0f, 4.0f, 6.0f)
            };
            SetLocalFogBoundProxy(fog, shape);

            try
            {
                Bounds expected = gameObject.transform.CalculateWorldAabb(shape);

                Bounds actual = fog.GetBounds();

                AssertVector3(actual.center, expected.center);
                AssertVector3(actual.size, expected.size);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void LocalVolumetricFog_ConvertToVolumeBounds_UsesHdrpOrientedBoxPacking()
        {
            var gameObject = new GameObject("Local Volumetric Fog Volume Bounds");
            gameObject.transform.position = new Vector3(1.0f, 2.0f, 3.0f);
            gameObject.transform.rotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
            var fog = gameObject.AddComponent<VividLocalVolumetricFog>();
            SetLocalFogBoundProxy(fog, new BoundProxyShape
            {
                shape = BoundProxyShapeType.Box,
                center = new Vector3(0.0f, 1.0f, 0.0f),
                size = new Vector3(2.0f, 4.0f, 6.0f)
            });

            try
            {
                var bounds = fog.ConvertToVolumeBounds();

                Assert.That(VividVolumetricMaterialBounds.Stride, Is.EqualTo(48));
                Assert.That(VividVolumetricMaterialRenderingData.Stride, Is.EqualTo(160));
                AssertVector3(bounds.center, gameObject.transform.position + gameObject.transform.rotation * new Vector3(0.0f, 1.0f, 0.0f));
                AssertVector3(new Vector3(bounds.extentX, bounds.extentY, bounds.extentZ), new Vector3(1.0f, 2.0f, 3.0f));
                Assert.That(bounds.right.magnitude, Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(bounds.up.magnitude, Is.EqualTo(1.0f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void VolumetricDensityPass_InitializesStableResources()
        {
            IRenderPass renderPass = new VolumetricDensityPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures.Select(entry => entry.Name), Is.EquivalentTo(new[]
            {
                "CameraDepth",
                "VBufferDensity",
                "VBufferAnisotropy"
            }));
            Assert.That(resources.RenderLists.Select(entry => entry.Name), Is.EquivalentTo(new[]
            {
                "FogVolumeVFXRenderList"
            }));
            Assert.That(resources.Buffers.Select(entry => entry.Name), Is.EquivalentTo(new[]
            {
                "VolumeBounds",
                "VolumetricVisibleGlobalIndices",
                "VolumetricGlobalIndirectArgs",
                "VolumetricGlobalIndirection",
                "VolumetricMaterialData"
            }));
            Assert.That(resources.Textures.Single(entry => entry.Name == "CameraDepth").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures.Single(entry => entry.Name == "VBufferDensity").Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(resources.Textures.Single(entry => entry.Name == "VBufferAnisotropy").Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(resources.RenderLists.Single(entry => entry.Name == "FogVolumeVFXRenderList").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "VolumeBounds").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "VolumetricVisibleGlobalIndices").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "VolumetricGlobalIndirectArgs").Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "VolumetricGlobalIndirection").Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "VolumetricMaterialData").Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "VolumetricVisibleGlobalIndices").Buffer.desc.Target, Is.EqualTo(GraphicsBuffer.Target.Raw));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "VolumetricGlobalIndirectArgs").Buffer.desc.Target, Is.EqualTo(GraphicsBuffer.Target.IndirectArguments));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "VolumetricGlobalIndirection").Buffer.desc.Target, Is.EqualTo(GraphicsBuffer.Target.Raw));
        }

        [Test]
        public void VolumetricMaxZPass_InitializesDepthAwareResource()
        {
            var pass = new VolumetricMaxZPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1280;
            cameraData.actualHeight = 720;

            pass.Prepare(frameData);

            var resources = ((IRenderPass)pass).Initialize();
            var maxZ = resources.Textures.Single(entry => entry.Name == "VBufferMaxZ");

            Assert.That(resources.Textures.Select(entry => entry.Name), Is.EquivalentTo(new[]
            {
                "CameraDepth",
                "VBufferMaxZ8x",
                "VBufferMaxZFinalMask",
                "VBufferMaxZ"
            }));
            Assert.That(maxZ.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(maxZ.Texture.desc.Dimension, Is.EqualTo(TextureDimension.Tex2D));
            Assert.That(maxZ.Texture.desc.Width, Is.EqualTo(80));
            Assert.That(maxZ.Texture.desc.Height, Is.EqualTo(45));
            Assert.That(maxZ.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32_SFloat));
            Assert.That(maxZ.Texture.desc.EnableRandomWrite, Is.True);

            var maxZ8x = resources.Textures.Single(entry => entry.Name == "VBufferMaxZ8x");
            var finalMask = resources.Textures.Single(entry => entry.Name == "VBufferMaxZFinalMask");
            Assert.That(maxZ8x.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(maxZ8x.IsTransient, Is.True);
            Assert.That(maxZ8x.Texture.desc.Width, Is.EqualTo(160));
            Assert.That(maxZ8x.Texture.desc.Height, Is.EqualTo(90));
            Assert.That(finalMask.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(finalMask.IsTransient, Is.True);
            Assert.That(finalMask.Texture.desc.Width, Is.EqualTo(80));
            Assert.That(finalMask.Texture.desc.Height, Is.EqualTo(45));
        }

        [Test]
        public void VolumetricLightingPass_InitializesVBufferAndLightingResources()
        {
            IRenderPass renderPass = new VolumetricLightingPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures.Select(entry => entry.Name), Is.EquivalentTo(new[]
            {
                "CameraDepth",
                "DirectionalShadowTexture",
                "CSMShadowAtlas",
                "VBufferMaxZ",
                "VBufferDensity",
                "VBufferAnisotropy",
                "VBufferLighting",
                "VBufferLightingFiltered"
            }));
            Assert.That(resources.Buffers.Select(entry => entry.Name), Is.EquivalentTo(new[]
            {
                "DirectionalLights",
                "PunctualLights",
                "AreaLights",
                "BigTileLightList",
                "BigTileVolumetricLightList",
                "LayeredOffset",
                "LayeredLightList",
                "LogBaseBuffer"
            }));
            Assert.That(resources.Textures.Single(entry => entry.Name == "CameraDepth").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures.Single(entry => entry.Name == "DirectionalShadowTexture").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures.Single(entry => entry.Name == "CSMShadowAtlas").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures.Single(entry => entry.Name == "VBufferMaxZ").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures.Single(entry => entry.Name == "VBufferDensity").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures.Single(entry => entry.Name == "VBufferAnisotropy").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures.Single(entry => entry.Name == "VBufferLighting").Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(resources.Textures.Single(entry => entry.Name == "VBufferLightingFiltered").Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(resources.Textures.Single(entry => entry.Name == "VBufferLightingFiltered").IsTransient, Is.True);
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("VBufferHistory"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("VBufferFeedback"));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "BigTileLightList").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "BigTileVolumetricLightList").Access, Is.EqualTo(AccessFlags.Read));
        }

        [Test]
        public void VolumetricDensityPass_Prepare_ConfiguresThreeDimensionalVBuffer()
        {
            var pass = new VolumetricDensityPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1280;
            cameraData.actualHeight = 720;

            pass.Prepare(frameData);

            var resources = ((IRenderPass)pass).Initialize();
            var vBuffer = resources.Textures.Single(entry => entry.Name == "VBufferDensity").Texture;
            var anisotropy = resources.Textures.Single(entry => entry.Name == "VBufferAnisotropy").Texture;

            Assert.That(vBuffer.desc.Dimension, Is.EqualTo(TextureDimension.Tex3D));
            Assert.That(vBuffer.desc.Width, Is.EqualTo(160));
            Assert.That(vBuffer.desc.Height, Is.EqualTo(90));
            Assert.That(vBuffer.desc.Slices, Is.EqualTo(VividVolumetricFogVolume.DefaultVolumeSliceCount));
            Assert.That(vBuffer.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(vBuffer.desc.EnableRandomWrite, Is.True);
            Assert.That(anisotropy.desc.Dimension, Is.EqualTo(TextureDimension.Tex3D));
            Assert.That(anisotropy.desc.Width, Is.EqualTo(160));
            Assert.That(anisotropy.desc.Height, Is.EqualTo(90));
            Assert.That(anisotropy.desc.Slices, Is.EqualTo(VividVolumetricFogVolume.DefaultVolumeSliceCount));
            Assert.That(anisotropy.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16_SFloat));
            Assert.That(anisotropy.desc.EnableRandomWrite, Is.True);
        }

        [Test]
        public void BuildRegistrations_IncludesActiveVolumetricPasses()
        {
            var registrations = RenderPassNodeRegistryBuilder.BuildRegistrations(new[]
            {
                typeof(VolumetricDensityPass),
                typeof(VolumetricMaxZPass),
                typeof(VolumetricLightingPass)
            });

            var nodeNames = registrations.Select(registration => registration.NodeClassName).ToArray();

            Assert.That(nodeNames, Does.Contain(nameof(VolumetricDensityPass)));
            Assert.That(nodeNames, Does.Contain(nameof(VolumetricMaxZPass)));
            Assert.That(nodeNames, Does.Contain(nameof(VolumetricLightingPass)));
        }

        private static void SetLocalFogBoundProxy(VividLocalVolumetricFog fog, BoundProxyShape shape)
        {
            typeof(VividLocalVolumetricFog)
                .GetField("m_BoundProxy", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(fog, shape);
        }

        private static void SetLocalFogRawParameters(
            VividLocalVolumetricFog fog,
            VividLocalVolumetricFogArtistParameters parameters)
        {
            typeof(VividLocalVolumetricFog)
                .GetField("m_Parameters", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(fog, parameters);
        }

        private static void SetLocalFogSerializationVersion(VividLocalVolumetricFog fog, int version)
        {
            typeof(VividLocalVolumetricFog)
                .GetField("m_SerializationVersion", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(fog, version);
        }

        private static void AssertVector3(Vector3 actual, Vector3 expected, float tolerance = 0.0001f)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance));
        }
    }
}

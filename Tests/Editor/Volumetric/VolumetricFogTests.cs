using System.IO;
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
        public void VividVolumetricFogVolumeEditor_SourceUsesHdrpStyleSectionsAndQualityModes()
        {
            var source = File.ReadAllText(GetPackageFilePath("Editor", "VolumeEditor", "VividVolumetricFogVolumeEditor.cs"));

            Assert.That(source, Does.Contain("[CustomEditor(typeof(VividVolumetricFogVolume))]"));
            Assert.That(source, Does.Contain("PropertyFetcher<VividVolumetricFogVolume>"));
            Assert.That(source, Does.Contain("PropertyField(m_Enabled, s_StateLabel);"));
            Assert.That(source, Does.Not.Contain("DrawStateField"));
            Assert.That(source, Does.Not.Contain("EditorGUI.IntPopup"));
            Assert.That(source, Does.Contain("Fog Attenuation Distance"));
            Assert.That(source, Does.Contain("Max Fog Distance"));
            Assert.That(source, Does.Contain("Volumetric Fog Distance"));
            Assert.That(source, Does.Contain("GI Dimmer"));
            Assert.That(source, Does.Contain("ShouldDisableColorModeSettings"));
            Assert.That(source, Does.Contain("ShouldDisableVolumetricSettings"));
            Assert.That(source, Does.Contain("ShouldShowCustomQualitySettings"));
            Assert.That(source, Does.Contain("ShouldShowBalanceQualitySettings"));
            Assert.That(source, Does.Contain("ShouldShowManualQualitySettings"));
            Assert.That(source, Does.Contain("PropertyField(m_FogControlMode);"));
            Assert.That(source, Does.Contain("PropertyField(m_VolumetricFogBudget);"));
            Assert.That(source, Does.Contain("PropertyField(m_ResolutionDepthRatio);"));
            Assert.That(source, Does.Contain("PropertyField(m_ScreenResolutionPercentage);"));
            Assert.That(source, Does.Contain("PropertyField(m_VolumeSliceCount);"));
            Assert.That(source, Does.Contain("PropertyField(m_DenoisingMode);"));
            Assert.That(source, Does.Contain("PropertyField(m_Tier);"));
            Assert.That(source, Does.Contain("PropertyField(m_MultipleScatteringIntensity);"));
            Assert.That(source, Does.Contain("Maximum Height is clamped above Base Height at runtime."));
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
        public void LocalVolumetricFog_RuntimeAttributesMatchHdrpObject()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Volumetric", "VividLocalVolumetricFog.cs"));

            Assert.That(source, Does.Contain("[AddComponentMenu(\"Rendering/Local Volumetric Fog\")]"));
            Assert.That(source, Does.Contain("[Icon(\"Packages/com.unity.render-pipelines.core/Editor/Icons/Processed/LocalVolumetricFog Icon.asset\")]"));
            Assert.That(source, Does.Contain("SceneVisibilityManager.visibilityChanged"));
            Assert.That(source, Does.Contain("SceneView.duringSceneGui"));
            Assert.That(source, Does.Contain("PrefabStageUtility.GetCurrentPrefabStage"));
            Assert.That(source, Does.Contain("CoreUtils.IsSceneViewPrefabStageContextHidden"));
            Assert.That(source, Does.Contain("UpdateLocalVolumetricFogVisibility(bool isVisible)"));
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
        public void LocalVolumetricFogMenuItems_DefinesGameObjectMenuEntryLikeHdrp()
        {
            var source = File.ReadAllText(GetPackageFilePath("Editor", "ComponentEditor", "VividLocalVolumetricFogMenuItems.cs"));

            Assert.That(VividLocalVolumetricFogMenuItems.CreateLocalVolumetricFogMenuPath, Is.EqualTo("GameObject/Rendering/Local Volumetric Fog"));
            Assert.That(source, Does.Contain("[MenuItem(CreateLocalVolumetricFogMenuPath"));
            Assert.That(source, Does.Contain("priority = 12"));
            Assert.That(source, Does.Contain("CreateLocalVolumetricFogGameObject(menuCommand.context as GameObject)"));
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
                Bounds expected = BoundProxyUtility.CalculateWorldAabb(gameObject.transform, shape);

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
                "VBufferLightingFiltered",
                "VBufferHistory",
                "VBufferFeedback"
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
            Assert.That(resources.Textures.Single(entry => entry.Name == "VBufferHistory").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures.Single(entry => entry.Name == "VBufferFeedback").Access, Is.EqualTo(AccessFlags.ReadWrite));
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

        [Test]
        public void VolumetricShaderSources_DefineExpectedKernelsAndResources()
        {
            var densitySource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Volumetric", "VolumetricDensity.compute"));
            var maxZSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Volumetric", "VolumetricMaxZ.compute"));
            var materialSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Volumetric", "VolumetricMaterial.compute"));
            var localVoxelizeSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Volumetric", "LocalVolumetricFogVoxelize.shader"));
            var lightingSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Volumetric", "VolumetricLighting.compute"));
            var densityPassSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Volumetric", "VolumetricDensityPass.cs"));
            var localFogSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Volumetric", "VividLocalVolumetricFog.cs"));
            var localFogManagerSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Volumetric", "VividLocalVolumetricFogManager.cs"));
            var lightingPassSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Volumetric", "VolumetricLightingPass.cs"));
            var lightGridPassSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Lighting", "LightGridPass.cs"));
            var bigTileLightListSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Lighting", "lightlistbuild-bigtile.compute"));
            var clusteredLightingSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "ClusteredLighting.hlsl"));
            var lightingLoopSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "LightingLoop.hlsl"));
            var clusteredLightingDataSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "VividClusteredLightingData.cs"));

            Assert.That(densitySource, Does.Contain("#pragma kernel ClearVBufferDensity"));
            Assert.That(densitySource, Does.Contain("#pragma kernel VoxelizeVBufferDensity"));
            Assert.That(densitySource, Does.Contain("RWTexture3D<float> _VBufferAnisotropy"));
            Assert.That(densitySource, Does.Contain("[numthreads(8, 8, 1)]"));
            Assert.That(densitySource, Does.Contain("for (uint slice = 0; slice < (uint)_VBufferSliceCount; slice++)"));
            Assert.That(densitySource, Does.Not.Contain("_LocalVolumetricFogs"));
            Assert.That(densitySource, Does.Not.Contain("_LocalVolumetricFogMask0"));
            Assert.That(densitySource, Does.Not.Contain("AccumulateLocalFog"));
            Assert.That(densitySource, Does.Contain("ComputeHeightFogMultiplier"));
            Assert.That(densitySource, Does.Contain("exp(-heightAboveBase * rcpScaleHeight)"));
            Assert.That(densitySource, Does.Contain("_VBufferFogRcpScaleHeight"));
            Assert.That(densitySource, Does.Contain("_VBufferAnisotropy[dispatchThreadId] = 0.0"));
            Assert.That(densitySource, Does.Contain("_VBufferAnisotropy[voxelCoord3D] = safeExtinction * clamp(_VBufferFogAnisotropy, -0.95, 0.95)"));
            Assert.That(densitySource, Does.Not.Contain("_VBufferDensityCutoff"));
            Assert.That(densitySource, Does.Not.Contain("extinction <= _VBufferDensityCutoff"));
            Assert.That(localVoxelizeSource, Does.Contain("Hidden/VividRP/LocalVolumetricFogVoxelize"));
            Assert.That(localVoxelizeSource, Does.Contain("Tags { \"LightMode\" = \"FogVolumeVoxelize\" }"));
            Assert.That(localVoxelizeSource, Does.Contain("Blend [_FogVolumeSrcColorBlend] [_FogVolumeDstColorBlend]"));
            Assert.That(localVoxelizeSource, Does.Contain("_FogVolumeAnisotropy(\"Anisotropy\", Float)"));
            Assert.That(localVoxelizeSource, Does.Contain("StructuredBuffer<VividVolumetricMaterialRenderingData> _VolumetricMaterialData"));
            Assert.That(localVoxelizeSource, Does.Contain("ByteAddressBuffer _VolumetricGlobalIndirectionBuffer"));
            Assert.That(localVoxelizeSource, Does.Contain("SV_RenderTargetArrayIndex"));
            Assert.That(localVoxelizeSource, Does.Contain("return GetVBufferSliceDistance((float)sliceIndex + 0.5)"));
            Assert.That(localVoxelizeSource, Does.Not.Contain("((float)sliceIndex + 0.5) * _VBufferRcpSliceCount + _VBufferRcpSliceCount"));
            Assert.That(localVoxelizeSource, Does.Contain("VolumeRendering.hlsl"));
            Assert.That(localVoxelizeSource, Does.Contain("ComputeVolumeFadeFactor"));
            Assert.That(localVoxelizeSource, Does.Not.Contain("return saturate(fade)"));
            Assert.That(localVoxelizeSource, Does.Contain("#pragma multi_compile_fragment _ _ENABLE_VOLUMETRIC_FOG_MASK"));
            Assert.That(localVoxelizeSource, Does.Contain("_Mask(\"Mask\", 3D)"));
            Assert.That(localVoxelizeSource, Does.Contain("float3 _ScrollSpeed"));
            Assert.That(localVoxelizeSource, Does.Contain("float3 _Tiling"));
            Assert.That(localVoxelizeSource, Does.Contain("float _AlphaOnlyTexture"));
            Assert.That(localVoxelizeSource, Does.Contain("float _FogVolumeAnisotropy"));
            Assert.That(localVoxelizeSource, Does.Contain("_VolumetricMask"));
            Assert.That(localVoxelizeSource, Does.Contain("SAMPLER(sampler_VolumetricMask)"));
            Assert.That(localVoxelizeSource, Does.Contain("SAMPLER(sampler_Mask)"));
            Assert.That(localVoxelizeSource, Does.Contain("struct SurfaceDescriptionInputs"));
            Assert.That(localVoxelizeSource, Does.Contain("SurfaceDescriptionFunction"));
            Assert.That(localVoxelizeSource, Does.Contain("BuildFragInputs"));
            Assert.That(localVoxelizeSource, Does.Contain("GetVolumeData"));
            Assert.That(localVoxelizeSource, Does.Not.Contain("float3 viewDirectionWS : TEXCOORD"));
            Assert.That(localVoxelizeSource, Does.Not.Contain("ComputeWorldSpacePosition(output.positionCS, UNITY_MATRIX_I_VP)"));
            Assert.That(localVoxelizeSource, Does.Not.Contain("output.viewDirectionWS = GetWorldSpaceViewDir(positionRWS)"));
            Assert.That(localVoxelizeSource, Does.Not.Contain("float3 rayCenterDirWS = normalize(-input.viewDirectionWS)"));
            Assert.That(localVoxelizeSource, Does.Contain("GetVBufferRayDirectionWSFromPixelCoord(input.positionCS.xy)"));
            Assert.That(localVoxelizeSource, Does.Contain("GetVolumeData(fragInputs, -rayCenterDirWS, albedo, extinction)"));
            Assert.That(localVoxelizeSource, Does.Contain("GetCurrentViewPosition() + sliceDistance * rayCenterDirWS"));
            Assert.That(localVoxelizeSource, Does.Contain("GetAbsolutePositionWS(voxelCenterRWS - _VolumetricMaterialObbCenter)"));
            Assert.That(localVoxelizeSource, Does.Not.Contain("_WorldSpaceCameraPos + sliceDistance * rayCenterDirWS"));
            Assert.That(localVoxelizeSource, Does.Contain("input.uv0.xyz * max(_VolumetricTiling, 1e-4) + _VolumetricScroll"));
            Assert.That(localVoxelizeSource, Does.Contain("maskValue = 0.5 > _VolumetricAlphaOnlyTexture ? maskSample : alphaOnlyMask"));
            Assert.That(localVoxelizeSource, Does.Contain("input.uv0.xyz * max(_Tiling, 1e-4) + _ScrollSpeed * input.TimeParameters.x"));
            Assert.That(localVoxelizeSource, Does.Contain("maskValue = 0.5 > _AlphaOnlyTexture ? maskSample : alphaOnlyMask"));
            Assert.That(localVoxelizeSource, Does.Contain("extinction *= ExtinctionFromMeanFreePath(_FogVolumeFogDistanceProperty)"));
            Assert.That(localVoxelizeSource, Does.Not.Contain("outColor=extinction"));
            Assert.That(localVoxelizeSource, Does.Contain("albedo *= _FogVolumeSingleScatteringAlbedo.rgb"));
            Assert.That(localVoxelizeSource, Does.Contain("float fade = ComputeFadeFactor(voxelCenterNDC, sliceDistance)"));
            Assert.That(localVoxelizeSource, Does.Contain("out float outAnisotropy : SV_Target1"));
            Assert.That(localVoxelizeSource, Does.Contain("outAnisotropy = extinction * anisotropy"));
            Assert.That(localVoxelizeSource, Does.Not.Contain("saturate(coordNDC * max(_VolumetricTiling"));
            Assert.That(localVoxelizeSource, Does.Not.Contain("? maskSample.a : maskSample.r"));
            Assert.That(localVoxelizeSource, Does.Contain("LOCALVOLUMETRICFOGBLENDINGMODE_MULTIPLY"));
            Assert.That(maxZSource, Does.Contain("#pragma kernel ComputeMaxZ"));
            Assert.That(maxZSource, Does.Contain("#pragma kernel ComputeFinalMask"));
            Assert.That(maxZSource, Does.Contain("#pragma kernel DilateMask"));
            Assert.That(maxZSource, Does.Contain("Texture2D<float> _InputTexture"));
            Assert.That(maxZSource, Does.Contain("RWTexture2D<float> _OutputTexture"));
            Assert.That(maxZSource, Does.Contain("groupshared float gs_MaxDepth"));
            Assert.That(maxZSource, Does.Contain("_SrcOffsetAndLimit"));
            Assert.That(maxZSource, Does.Contain("_DilationWidth"));
            Assert.That(maxZSource, Does.Contain("GetDepthToDownsample"));
            Assert.That(maxZSource, Does.Contain("IsVBufferFarDepth"));
            Assert.That(maxZSource, Does.Contain("LinearEyeDepth(deviceDepth, _ZBufferParams)"));
            Assert.That(maxZSource, Does.Contain("VBUFFER_MAX_Z_FAR_DEPTH"));
            Assert.That(maxZSource, Does.Contain("LoadInputMaxZ"));
            Assert.That(maxZSource, Does.Contain("DilateMask"));
            Assert.That(materialSource, Does.Contain("#pragma kernel ClearVolumetricMaterialRenderingParameters"));
            Assert.That(materialSource, Does.Contain("#pragma kernel ComputeVolumetricMaterialRenderingParameters"));
            Assert.That(materialSource, Does.Contain("StructuredBuffer<VividVolumetricMaterialBounds> _VolumeBounds"));
            Assert.That(materialSource, Does.Contain("ByteAddressBuffer _VolumetricVisibleGlobalIndicesBuffer"));
            Assert.That(materialSource, Does.Contain("RWBuffer<uint> _VolumetricGlobalIndirectArgsBuffer"));
            Assert.That(materialSource, Does.Contain("RWByteAddressBuffer _VolumetricGlobalIndirectionBuffer"));
            Assert.That(materialSource, Does.Contain("RWStructuredBuffer<VividVolumetricMaterialRenderingData> _VolumetricMaterialData"));
            Assert.That(materialSource, Does.Contain("uint _ViewCount"));
            Assert.That(materialSource, Does.Not.Contain("_VolumetricViewCount"));
            Assert.That(materialSource, Does.Contain("DistanceToSliceCoord"));
            Assert.That(materialSource, Does.Contain("DistanceToStartSlice"));
            Assert.That(materialSource, Does.Contain("DistanceToStopSlice"));
            Assert.That(materialSource, Does.Contain("DepthDistance"));
            Assert.That(materialSource, Does.Contain("GetOBBCenterRWS"));
            Assert.That(materialSource, Does.Contain("GetCameraRelativePositionWS(obb.center)"));
            Assert.That(materialSource, Does.Contain("float3 cameraPositionRWS = GetCurrentViewPosition()"));
            Assert.That(materialSource, Does.Contain("length(positionRWS - cameraPositionRWS)"));
            Assert.That(materialSource, Does.Contain("DistanceDistanceToOBB"));
            Assert.That(materialSource, Does.Contain("float3 forward = normalize(cross(right, up))"));
            Assert.That(materialSource, Does.Contain("ComputeCubeVerticesOrder"));
            Assert.That(materialSource, Does.Contain("#if USE_VERTEX_CUBE_SLICING"));
            Assert.That(materialSource, Does.Contain("ComputeCubeVerticesOrder(volumeIndex)"));
            Assert.That(materialSource, Does.Contain("_VolumetricVisibleGlobalIndicesBuffer.Load(volumeIndex << 2)"));
            Assert.That(materialSource, Does.Contain("TransformWorldToView(positionRWS)"));
            Assert.That(materialSource, Does.Contain("TransformWViewToHClip(positionVS)"));
            Assert.That(materialSource, Does.Contain("_VolumetricGlobalIndirectionBuffer.Store"));
            Assert.That(lightingSource, Does.Contain("#pragma kernel VolumetricLighting"));
            Assert.That(lightingSource, Does.Contain("#pragma kernel FilterVolumetricLighting"));
            Assert.That(lightingSource, Does.Contain("Texture2D<float> _VBufferMaxZ"));
            Assert.That(lightingSource, Does.Contain("Texture3D<float4> _VBufferHistory"));
            Assert.That(lightingSource, Does.Contain("RWTexture3D<float4> _VBufferFeedback"));
            Assert.That(lightingSource, Does.Contain("uint _VBufferHistoryIsValid"));
            Assert.That(lightingSource, Does.Contain("float4 _VBufferSampleOffset"));
            Assert.That(lightingSource, Does.Contain("float _VBufferMaxZEnabled"));
            Assert.That(lightingSource, Does.Contain("GetVBufferMaxOpaqueGeometryDistance"));
            Assert.That(lightingSource, Does.Contain("_VBufferMaxZ.GetDimensions"));
            Assert.That(lightingSource, Does.Contain("float maxDistance = max(maxLinearEyeDepth / forwardDistance, fallbackDistance)"));
            Assert.That(lightingSource, Does.Contain("LightingLoop.hlsl"));
            Assert.That(lightingSource, Does.Contain("EntityLighting.hlsl"));
            Assert.That(lightingSource, Does.Contain("StructuredBuffer<float4> _VolumetricAmbientProbeBuffer"));
            Assert.That(lightingSource, Does.Contain("float3 EvaluateVolumetricAmbientProbe(float3 normalWS)"));
            Assert.That(lightingSource, Does.Contain("SampleSH9(_VolumetricAmbientProbeBuffer, SafeNormalize(normalWS))"));
            Assert.That(lightingSource, Does.Contain("GeometricTools.hlsl"));
            Assert.That(lightingSource, Does.Contain("[numthreads(8, 8, 1)]"));
            Assert.That(lightingSource, Does.Contain("struct JitteredRay"));
            Assert.That(lightingSource, Does.Contain("BuildVBufferJitteredRay"));
            Assert.That(lightingSource, Does.Contain("ray.centerDirWS"));
            Assert.That(lightingSource, Does.Contain("ray.jitterDirWS"));
            Assert.That(lightingSource, Does.Contain("ray.xDirDerivWS"));
            Assert.That(lightingSource, Does.Contain("ray.yDirDerivWS"));
            Assert.That(lightingSource, Does.Contain("GetVBufferJitteredRayStartDistance"));
            Assert.That(lightingSource, Does.Contain("GetVBufferOpaqueGeometryDistanceForRay"));
            Assert.That(lightingSource, Does.Contain("ComputeHistoryWeight"));
            Assert.That(lightingSource, Does.Contain("SampleVBufferHistory"));
            Assert.That(lightingSource, Does.Contain("mul(_PrevViewProjMatrix, float4(positionWS, 1.0))"));
            Assert.That(lightingSource, Does.Contain("distance(positionWS, _VBufferPrevCameraPositionWS.xyz)"));
            Assert.That(lightingSource, Does.Contain("EncodeLogarithmicDepthGeneralized(linearDistance, _VBufferPrevDepthEncodingParams)"));
            Assert.That(lightingSource, Does.Contain("FillVolumetricLightingBuffer"));
            Assert.That(lightingSource, Does.Contain("uint2 groupId : SV_GroupID"));
            Assert.That(lightingSource, Does.Contain("uint2 groupThreadId : SV_GroupThreadID"));
            Assert.That(lightingSource, Does.Contain("for (; slice < sliceCount; slice++)"));
            Assert.That(lightingSource, Does.Contain("ShouldEvaluateVBufferLighting"));
            Assert.That(lightingSource, Does.Contain("extinction > _VBufferDensityCutoff"));
            Assert.That(lightingSource, Does.Contain("Texture3D<float> _VBufferAnisotropy"));
            Assert.That(lightingSource, Does.Contain("float3 scattering = max(density.rgb, 0.0)"));
            Assert.That(lightingSource, Does.Contain("float extinction = max(density.a, 0.0)"));
            Assert.That(lightingSource, Does.Contain("float anisotropyMoment = _VBufferAnisotropy.Load(int4(voxelCoord, 0))"));
            Assert.That(lightingSource, Does.Contain("float anisotropy = extinction > FLT_MIN ? anisotropyMoment / extinction : _VBufferFogAnisotropy"));
            Assert.That(lightingSource, Does.Contain("voxelOpticalDepth = extinction * dt"));
            Assert.That(lightingSource, Does.Contain("float perPixelRandomOffset = GenerateVBufferRandom(vBufferPixel)"));
            Assert.That(lightingSource, Does.Contain("float rndVal = frac(perPixelRandomOffset + _VBufferSampleOffset.z)"));
            Assert.That(lightingSource, Does.Contain("ImportanceSampleHomogeneousMedium(rndVal, extinction, dt, tOffset, weight)"));
            Assert.That(lightingSource, Does.Not.Contain("float weight = TransmittanceIntegralHomogeneousMedium(extinction, dt)"));
            Assert.That(lightingSource, Does.Contain("float3 sampleWS = ray.originWS + t * ray.jitterDirWS"));
            Assert.That(lightingSource, Does.Contain("Texture2D<float> _CSMShadowAtlas"));
            Assert.That(lightingSource, Does.Contain("SamplerComparisonState sampler_CSMShadowAtlas"));
            Assert.That(lightingSource, Does.Contain("_CSMShadowAtlas.SampleCmpLevelZero"));
            Assert.That(lightingSource, Does.Contain("float shadow = SampleDirectionalShadow(voxelCoord.xy, i, light, sampleWS, ray.jitterDirWS);"));
            Assert.That(lightingSource, Does.Contain("directionalLight.shadowStrength * directionalLight.volumetricShadowDimmer"));
            Assert.That(lightingSource, Does.Not.Contain("_DirectionalShadowTexture.SampleLevel"));
            Assert.That(lightingSource, Does.Not.Contain("float2 uv = cameraPixel / max(cameraDimensions"));
            Assert.That(lightingSource, Does.Contain("Compute the exponential moving average over 'n' frames"));
            Assert.That(lightingSource, Does.Contain("Reminder: our voxels are sphere-capped right frustums"));
            Assert.That(lightingSource, Does.Contain("Accurately compute the center of the voxel in the log space"));
            Assert.That(lightingSource, Does.Contain("phaseCurrFrame' becomes temporarily unstable"));
            Assert.That(lightingSource, Does.Contain("A Fresh Look at Generalized Sampling"));
            Assert.That(lightingSource, Does.Contain("float3 L = SafeNormalize(light.directionWS);"));
            Assert.That(lightingSource, Does.Not.Contain("float3 L = SafeNormalize(-light.directionWS);"));
            Assert.That(lightingSource, Does.Contain("LinearizeRGBD(voxelValue)"));
            Assert.That(lightingSource, Does.Contain("DelinearizeRGBD(normalizedBlendValue * dt)"));
            Assert.That(lightingSource, Does.Contain("StoreVBufferFeedback(voxelCoord, normalizedBlendValue)"));
            Assert.That(lightingSource, Does.Contain("normalizedBlendValue = lerp(normalizedVoxelValue, reprojValue, ComputeHistoryWeight())"));
            Assert.That(lightingSource, Does.Contain("SafeDiv(aggregate.radianceComplete.r, aggregate.radianceNoPhase.r)"));
            Assert.That(lightingSource, Does.Contain("TransmittanceIntegralHomogeneousMedium"));
            Assert.That(lightingSource, Does.Contain("totalRadiance += transmittanceToSlice * scattering"));
            Assert.That(lightingSource, Does.Not.Contain("totalRadiance += transmittanceToSlice * density.rgb"));
            Assert.That(lightingSource, Does.Contain("opticalDepth += 0.5 * blendOpticalDepth"));
            Assert.That(lightingSource, Does.Not.Contain("opticalDepth += 0.5 * voxelOpticalDepth"));
            Assert.That(lightingSource, Does.Contain("StoreIntegratedVBufferLighting"));
            Assert.That(lightingSource, Does.Contain("light.affectVolumetric"));
            Assert.That(lightingSource, Does.Contain("light.volumetricDimmer"));
            Assert.That(lightingSource, Does.Contain("light.volumetricFadeDistance"));
            Assert.That(lightingSource, Does.Contain("uint _VolumetricUseBigTileLightList"));
            Assert.That(lightingSource, Does.Not.Contain("_PunctualLightCount"));
            Assert.That(clusteredLightingSource, Does.Contain("StructuredBuffer<uint> g_vBigTileLightList"));
            Assert.That(clusteredLightingSource, Does.Contain("uint _NumTileBigTileX"));
            Assert.That(clusteredLightingSource, Does.Contain("uint _NumTileBigTileY"));
            Assert.That(lightingLoopSource, Does.Contain("uint _AreaLightCount"));
            Assert.That(lightingLoopSource, Does.Contain("VividBigTileLightingLoopContext"));
            Assert.That(lightingLoopSource, Does.Contain("GetBigTileLightCount"));
            Assert.That(lightingLoopSource, Does.Contain("GetBigTileLightIndex"));
            Assert.That(lightingLoopSource, Does.Contain("LoadBigTilePunctualLight"));
            Assert.That(lightingSource, Does.Not.Contain("VBUFFER_MAX_PUNCTUAL_LIGHTS"));
            Assert.That(lightingSource, Does.Contain("VividLightingLoop::CreateBigTile"));
            Assert.That(lightingSource, Does.Contain("VividLightingLoop::GetBigTileLightCount(bigTileLoop)"));
            Assert.That(lightingSource, Does.Contain("VividLightingLoop::LoadBigTilePunctualLight(bigTileLoop, lightOffset)"));
            Assert.That(lightingSource, Does.Not.Contain("GetVolumetricBigTileIndex"));
            Assert.That(lightingSource, Does.Not.Contain("FetchBigTileLightIndex"));
            Assert.That(lightingSource, Does.Contain("if (_VolumetricUseBigTileLightList == 0u || _NumTileBigTileX == 0u || _NumTileBigTileY == 0u)"));
            Assert.That(lightingSource, Does.Contain("EvaluateVoxelLightingLocal"));
            Assert.That(lightingSource, Does.Contain("ImportanceSamplePunctualLight"));
            Assert.That(lightingSource, Does.Contain("IntersectRayCone"));
            Assert.That(lightingSource, Does.Not.Contain("IntersectRaySphere"));
            Assert.That(lightingSource, Does.Contain("VIVID_PUNCTUAL_LIGHT_TYPE_SPOT"));
            Assert.That(lightingSource, Does.Contain("angleScale"));
            Assert.That(lightingSource, Does.Contain("angleOffset"));
            Assert.That(lightingSource, Does.Contain("coneAxisX = light.rightWS"));
            Assert.That(lightingSource, Does.Contain("coneAxisY = light.upWS"));
            Assert.That(lightingSource, Does.Contain("float lightSqRadius = max(light.shapeRadiusSquared, 1e-4)"));
            Assert.That(lightingSource, Does.Contain("float3 samplePosWS = ray.originWS + ray.jitterDirWS * t"));
            Assert.That(lightingSource, Does.Contain("float weight = TransmittanceHomogeneousMedium(extinction, t - t0) * rcpPdf"));
            Assert.That(lightingSource, Does.Contain("phaseConstant * lightingRadiance + probeRadiance"));
            Assert.That(lightingSource, Does.Not.Contain("EvaluateClusteredPunctualLighting"));
            Assert.That(lightingSource, Does.Contain("#define VBUFFER_FILTER_GAUSSIAN_SIGMA 1.0"));
            Assert.That(lightingSource, Does.Contain("#define VBUFFER_FILTER_SIZE_1D (VBUFFER_FILTER_GROUP_SIZE_XY + 2)"));
            Assert.That(lightingSource, Does.Contain("groupshared float4 gs_VBufferFilterCache"));
            Assert.That(lightingSource, Does.Contain("GroupMemoryBarrierWithGroupSync"));
            Assert.That(lightingSource, Does.Contain("Gaussian(length(float2(idx, idx2)), VBUFFER_FILTER_GAUSSIAN_SIGMA)"));
            Assert.That(lightingSource, Does.Not.Contain("for (int z = -1; z <= 1; z++)"));
            Assert.That(lightingSource, Does.Contain("if (hasMaxZ && t0 * 0.99 > ray.maxDist)"));
            Assert.That(lightingSource, Does.Contain("break;"));
            Assert.That(File.Exists(GetPackageFilePath("Runtime", "RenderPass", "Core", "Volumetric", "VolumetricFogCompositePass.cs")), Is.False);
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "Volumetric", "VolumetricFogComposite.shader")), Is.False);
            Assert.That(densityPassSource, Does.Contain("UnsafePass, IAllowGlobalStateModificationPass"));
            Assert.That(densityPassSource, Does.Contain("FogVolumeVoxelize"));
            Assert.That(densityPassSource, Does.Contain("VolumetricFogVFX"));
            Assert.That(densityPassSource, Does.Contain("VolumetricFogVFXOverdrawDebug"));
            Assert.That(densityPassSource, Does.Contain("RecordFogVolumeAndVFXVoxelization"));
            Assert.That(densityPassSource, Does.Contain("DrawRendererList"));
            Assert.That(densityPassSource, Does.Contain("VBufferAnisotropyId = Shader.PropertyToID(\"_VBufferAnisotropy\")"));
            Assert.That(densityPassSource, Does.Contain("Name = \"VBufferAnisotropy\", Access = AccessFlags.Write"));
            Assert.That(densityPassSource, Does.Contain("SetComputeBufferParam(m_VolumetricMaterialShader, m_ComputeMaterialKernel, VolumeBoundsId"));
            Assert.That(densityPassSource, Does.Contain("ViewCountId = Shader.PropertyToID(\"_ViewCount\")"));
            Assert.That(densityPassSource, Does.Contain("SetComputeIntParam(m_VolumetricMaterialShader, ViewCountId, m_ViewCount)"));
            Assert.That(densityPassSource, Does.Contain("CoreUtils.DivRoundUp(m_MaterialFogCount * m_ViewCount, ComputeMaterialThreadGroupSizeX)"));
            Assert.That(densityPassSource, Does.Contain("InsertVolumetricMaterialComputeToDrawFence(cmd)"));
            Assert.That(densityPassSource, Does.Contain("CreateGraphicsFence("));
            Assert.That(densityPassSource, Does.Contain("SynchronisationStageFlags.ComputeProcessing"));
            Assert.That(densityPassSource, Does.Contain("WaitOnAsyncGraphicsFence(fence, SynchronisationStageFlags.AllGPUOperations)"));
            Assert.That(densityPassSource, Does.Contain("PrepareVisibleFogs(camera, m_Settings.MaxLocalVolumetricFogCount)"));
            Assert.That(densityPassSource, Does.Contain("CoreUtils.SetRenderTarget(cmd, m_VolumetricMaterialTargets"));
            Assert.That(densityPassSource, Does.Contain("VividLocalVolumetricFogManager.RecordVolumetricMaterialDrawCalls(cmd)"));
            Assert.That(densityPassSource, Does.Contain("if (m_FogVolumeVFXRenderList?.IsValid == true)"));
            Assert.That(densityPassSource, Does.Not.Contain("LocalVolumetricFogsId"));
            Assert.That(localFogSource, Does.Contain("RecordVolumetricMaterialDrawCall("));
            Assert.That(localFogSource, Does.Contain("cmd.DrawProceduralIndirect("));
            Assert.That(localFogSource, Does.Contain("m_VoxelizationPassIndex"));
            Assert.That(localFogSource, Does.Not.Contain("Graphics.RenderPrimitivesIndexedIndirect"));
            Assert.That(localFogSource, Does.Contain("FogVolumeScrollSpeedId = Shader.PropertyToID(\"_ScrollSpeed\")"));
            Assert.That(localFogSource, Does.Contain("FogVolumeTilingId = Shader.PropertyToID(\"_Tiling\")"));
            Assert.That(localFogSource, Does.Contain("FogVolumeAlphaOnlyTextureId = Shader.PropertyToID(\"_AlphaOnlyTexture\")"));
            Assert.That(localFogSource, Does.Contain("FogVolumeAnisotropyId = Shader.PropertyToID(\"_FogVolumeAnisotropy\")"));
            Assert.That(localFogSource, Does.Contain("parameters.albedo.gamma"));
            Assert.That(localFogSource, Does.Contain("SetFloat(FogVolumeAnisotropyId, parameters.anisotropy)"));
            Assert.That(localFogSource, Does.Contain("SetTexture(FogVolumeMaskId, mask)"));
            Assert.That(localFogSource, Does.Contain("SetVector(FogVolumeScrollSpeedId, parameters.textureScrollingSpeed)"));
            Assert.That(localFogSource, Does.Contain("!material.HasProperty(VolumetricMaskModeId)"));
            Assert.That(localFogSource, Does.Contain("material.EnableKeyword(\"_ENABLE_VOLUMETRIC_FOG_MASK\")"));
            Assert.That(localFogSource, Does.Contain("material.DisableKeyword(\"_ENABLE_VOLUMETRIC_FOG_MASK\")"));
            Assert.That(localFogSource, Does.Contain("ResolveVoxelizationMaterial"));
            Assert.That(localFogSource, Does.Contain("ConfigureTextureMaskProperties"));
            Assert.That(localFogManagerSource, Does.Contain("DefaultVoxelizationShaderName = \"Hidden/VividRP/LocalVolumetricFogVoxelize\""));
            Assert.That(localFogManagerSource, Does.Contain("SetupFogVolumeBlendMode"));
            Assert.That(localFogManagerSource, Does.Contain("PrepareVolumetricMaterialDrawCalls(materialCount)"));
            Assert.That(localFogManagerSource, Does.Contain("RecordVolumetricMaterialDrawCalls(CommandBuffer cmd)"));
            Assert.That(localFogManagerSource, Does.Contain("index * IndirectDrawIndexedArgsStride"));
            Assert.That(localFogManagerSource, Does.Not.Contain("LocalVolumetricFogs\""));
            Assert.That(lightingPassSource, Does.Contain("cmd.DispatchCompute(m_Shader, m_LightingKernel, m_DispatchX, m_DispatchY, 1)"));
            Assert.That(lightingPassSource, Does.Contain("CSMShadowAtlasId = Shader.PropertyToID(\"_CSMShadowAtlas\")"));
            Assert.That(lightingPassSource, Does.Contain("CSMViewProjMatricesId = Shader.PropertyToID(\"_CSMViewProjMatrices\")"));
            Assert.That(lightingPassSource, Does.Contain("Name = \"CSMShadowAtlas\", Access = AccessFlags.Read"));
            Assert.That(lightingPassSource, Does.Contain("PrepareDirectionalShadowParameters(frameData)"));
            Assert.That(lightingPassSource, Does.Contain("BindDirectionalShadowParameters(context, cmd, kernel)"));
            Assert.That(lightingPassSource, Does.Contain("VBufferMaxZId = Shader.PropertyToID(\"_VBufferMaxZ\")"));
            Assert.That(lightingPassSource, Does.Contain("VBufferMaxZEnabledId = Shader.PropertyToID(\"_VBufferMaxZEnabled\")"));
            Assert.That(lightingPassSource, Does.Contain("VBufferAnisotropyId = Shader.PropertyToID(\"_VBufferAnisotropy\")"));
            Assert.That(lightingPassSource, Does.Contain("Name = \"VBufferAnisotropy\", Access = AccessFlags.Read"));
            Assert.That(lightingPassSource, Does.Contain("VBufferHistoryId = Shader.PropertyToID(\"_VBufferHistory\")"));
            Assert.That(lightingPassSource, Does.Contain("VBufferFeedbackId = Shader.PropertyToID(\"_VBufferFeedback\")"));
            Assert.That(lightingPassSource, Does.Contain("VBufferHistoryIsValidId = Shader.PropertyToID(\"_VBufferHistoryIsValid\")"));
            Assert.That(lightingPassSource, Does.Contain("VBufferSampleOffsetId = Shader.PropertyToID(\"_VBufferSampleOffset\")"));
            Assert.That(lightingPassSource, Does.Contain("ReferenceEquals(m_VBufferMaxZ, m_LocalVBufferMaxZ)"));
            Assert.That(lightingPassSource, Does.Contain("VolumetricMaxZPass.MaxZTileSize"));
            Assert.That(lightingPassSource, Does.Contain("BindVBufferMaxZ(context, cmd, kernel)"));
            Assert.That(lightingPassSource, Does.Contain("Name = \"VBufferHistory\", Access = AccessFlags.Read"));
            Assert.That(lightingPassSource, Does.Contain("Name = \"VBufferFeedback\", Access = AccessFlags.ReadWrite"));
            Assert.That(lightingPassSource, Does.Contain("PrepareVBufferHistory"));
            Assert.That(lightingPassSource, Does.Contain("AllocHistoryTexture("));
            Assert.That(lightingPassSource, Does.Contain("CameraRelativeSystem<VolumetricLightingHistoryState>"));
            Assert.That(lightingPassSource, Does.Contain("m_CurrentHistoryState = ResolveHistoryState(cameraData.camera)"));
            Assert.That(lightingPassSource, Does.Contain("m_CurrentHistoryState?.HasLastVBufferParameters"));
            Assert.That(lightingPassSource, Does.Contain("m_CurrentHistoryState.LastVBufferParameters = m_LastVBufferParameters"));
            Assert.That(lightingPassSource, Does.Contain("ResolveVolumetricFrameIndex(cameraData)"));
            Assert.That(lightingPassSource, Does.Contain("m_CurrentHistoryState.FrameIndex++"));
            Assert.That(lightingPassSource, Does.Contain("AreVBufferParametersCompatible(m_LastVBufferParameters, m_Settings.VBufferParameters)"));
            Assert.That(lightingPassSource, Does.Contain("Raw camera clip planes are intentionally omitted"));
            Assert.That(lightingPassSource, Does.Not.Contain("Approximately(previousParameters.FarClipPlane, currentParameters.FarClipPlane)"));
            Assert.That(lightingPassSource, Does.Contain("m_Settings.TemporalReprojectionEnabled"));
            Assert.That(lightingPassSource, Does.Contain("ComputeVBufferSampleOffset"));
            Assert.That(lightingPassSource, Does.Contain("ResolvePreviousCameraPositionWS"));
            Assert.That(lightingPassSource, Does.Contain("FilterKernelName = \"FilterVolumetricLighting\""));
            Assert.That(lightingPassSource, Does.Contain("m_FilterDispatchZ = Mathf.Max(m_Settings.VBufferParameters.SliceCount, 1)"));
            Assert.That(lightingPassSource, Does.Contain("cmd.DispatchCompute(m_Shader, m_FilterKernel, m_DispatchX, m_DispatchY, m_FilterDispatchZ)"));
            Assert.That(lightingPassSource, Does.Not.Contain("PunctualLightCountId = Shader.PropertyToID(\"_PunctualLightCount\")"));
            Assert.That(lightingPassSource, Does.Contain("AreaLightCountId = Shader.PropertyToID(\"_AreaLightCount\")"));
            Assert.That(lightingPassSource, Does.Contain("BigTileLightListId = Shader.PropertyToID(\"g_vBigTileLightList\")"));
            Assert.That(lightingPassSource, Does.Contain("VolumetricUseBigTileLightListId = Shader.PropertyToID(\"_VolumetricUseBigTileLightList\")"));
            Assert.That(lightingPassSource, Does.Contain("NumTileBigTileXId = Shader.PropertyToID(\"_NumTileBigTileX\")"));
            Assert.That(lightingPassSource, Does.Contain("BigTileSizeId = Shader.PropertyToID(\"_BigTileSize\")"));
            Assert.That(lightingPassSource, Does.Contain("Name = \"BigTileLightList\", Access = AccessFlags.Read"));
            Assert.That(lightingPassSource, Does.Contain("Name = \"BigTileVolumetricLightList\", Access = AccessFlags.Read"));
            Assert.That(lightingPassSource, Does.Contain("SetLightLoopBuffer(cmd, kernel, BigTileLightListId, GetBigTileVolumetricLightListBufferForBinding())"));
            Assert.That(lightingPassSource, Does.Not.Contain("cmd.SetComputeIntParam(m_Shader, PunctualLightCountId"));
            Assert.That(lightingPassSource, Does.Contain("cmd.SetComputeIntParam(m_Shader, AreaLightCountId, m_AreaLightCount)"));
            Assert.That(lightingPassSource, Does.Contain("cmd.SetComputeIntParam(m_Shader, BigTileSizeId, LightGridPass.ClusterBigTileSize)"));
            Assert.That(lightingPassSource, Does.Contain("cmd.SetComputeIntParam(m_Shader, VolumetricUseBigTileLightListId"));
            Assert.That(lightingPassSource, Does.Contain("m_PunctualLightCount = HasBoundPunctualLightBuffer()"));
            Assert.That(lightingPassSource, Does.Contain("m_AreaLightCount = HasBoundAreaLightBuffer()"));
            Assert.That(lightingPassSource, Does.Contain("m_SupportsVolumetricBigTileLightList = supportsClusteredFiniteLights"));
            Assert.That(lightingPassSource, Does.Contain("HasBoundBigTileVolumetricLightListBuffer()"));
            Assert.That(lightingPassSource, Does.Contain("m_FrameDataBigTileVolumetricLightListBuffer = clusteredLightingData.bigTileVolumetricLightList"));
            Assert.That(lightingPassSource, Does.Contain("|| m_FrameDataBigTileVolumetricLightListBuffer != null"));
            Assert.That(lightGridPassSource, Does.Contain("Name = \"BigTileLightList\""));
            Assert.That(lightGridPassSource, Does.Contain("Name = \"BigTileVolumetricLightList\""));
            Assert.That(lightGridPassSource, Does.Contain("BigTileVolumetricLightListId = Shader.PropertyToID(\"g_vVolumetricLightList\")"));
            Assert.That(lightGridPassSource, Does.Not.Contain("GENERATE_VOLUMETRIC_BIGTILE"));
            Assert.That(lightGridPassSource, Does.Not.Contain("SetKeyword"));
            Assert.That(lightGridPassSource, Does.Contain("clusteredLightingData.bigTileLightList = m_BigTileLightListBuffer"));
            Assert.That(lightGridPassSource, Does.Contain("clusteredLightingData.bigTileVolumetricLightList = m_BigTileVolumetricLightListBuffer"));
            Assert.That(lightGridPassSource, Does.Contain("clusteredLightingData.bigTileCountX = m_ClusterBigTileCountX"));
            Assert.That(bigTileLightListSource, Does.Contain("RWStructuredBuffer<uint> g_vVolumetricLightList"));
            Assert.That(bigTileLightListSource, Does.Contain("LightAffectVolumetric"));
            Assert.That(bigTileLightListSource, Does.Contain("g_vVolumetricLightList[bigTileOffset + i]"));
            Assert.That(bigTileLightListSource, Does.Not.Contain("GENERATE_VOLUMETRIC_BIGTILE"));
            Assert.That(clusteredLightingDataSource, Does.Contain("public RenderGraphBuffer bigTileLightList"));
            Assert.That(clusteredLightingDataSource, Does.Contain("public RenderGraphBuffer bigTileVolumetricLightList"));
            Assert.That(clusteredLightingDataSource, Does.Contain("public int bigTileCountX"));

            var vBufferSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Volumetric", "VBuffer.hlsl"));
            Assert.That(vBufferSource, Does.Contain("DecodeLogarithmicDepthGeneralized"));
            Assert.That(vBufferSource, Does.Contain("_VBufferCoordToViewDirWS"));
            Assert.That(vBufferSource, Does.Contain("_VBufferDepthDecodingParams"));
            Assert.That(vBufferSource, Does.Contain("_VBufferIsOrthographic"));
            Assert.That(vBufferSource, Does.Contain("IsVBufferFarDepth"));
            Assert.That(vBufferSource, Does.Contain("float4 SampleVBuffer(TEXTURE3D_PARAM(VBuffer, clampSampler)"));
            Assert.That(vBufferSource, Does.Contain("BiquadraticFilter(1.0 - fc, weights, offsets)"));
            Assert.That(vBufferSource, Does.Contain("vBufferViewportScale.xy * vBufferRcpViewportSize"));
            Assert.That(vBufferSource, Does.Contain("float3 vBufferViewportScale"));
            Assert.That(vBufferSource, Does.Contain("float3 vBufferViewportLimit"));
            Assert.That(vBufferSource, Does.Not.Contain("_VBufferLightingViewportScale3"));
            Assert.That(vBufferSource, Does.Not.Contain("_VBufferLightingViewportLimit3"));

            var volumetricVariablesSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Volumetric", "ShaderVariablesVolumetric.hlsl"));
            Assert.That(volumetricVariablesSource, Does.Contain("_VBufferLightingViewportScale"));
            Assert.That(volumetricVariablesSource, Does.Contain("_VBufferLightingViewportLimit"));
            Assert.That(volumetricVariablesSource, Does.Contain("_VBufferMaxZDilationRadius"));
        }

        [Test]
        public void VividLocalVolumetricFogEditor_UsesBoundProxySceneHandles()
        {
            var editorSource = File.ReadAllText(GetPackageFilePath("Editor", "ComponentEditor", "VividLocalVolumetricFogEditor.cs"));

            Assert.That(editorSource, Does.Contain("k_EditShape = EditMode.SceneViewEditMode.ReflectionProbeBox"));
            Assert.That(editorSource, Does.Contain("k_EditBlend = EditMode.SceneViewEditMode.GridBox"));
            Assert.That(editorSource, Does.Contain("VividLocalVolumetricFogModifyInfluenceVolumeTool"));
            Assert.That(editorSource, Does.Contain("VividLocalVolumetricFogModifyBaseShapeTool"));
            Assert.That(editorSource, Does.Contain("[EditorTool(Description, typeof(VividLocalVolumetricFog)"));
            Assert.That(editorSource, Does.Contain("PreMatCube"));
            Assert.That(editorSource, Does.Contain("EditCollider"));
            Assert.That(editorSource, Does.Contain("BoundProxyEditorUtility.TryDrawSceneHandles"));
            Assert.That(editorSource, Does.Contain("DrawBlendSceneHandle"));
            Assert.That(editorSource, Does.Contain("CenterBlendLocalPosition"));
            Assert.That(editorSource, Does.Contain("BlendSize"));
            Assert.That(editorSource, Does.Contain("allowCenterHandle: true"));
            Assert.That(editorSource, Does.Contain("DrawGizmo"));
            Assert.That(editorSource, Does.Contain("Single Scattering Albedo"));
            Assert.That(editorSource, Does.Contain("Fog Distance"));
            Assert.That(editorSource, Does.Contain("Mask Mode"));
            Assert.That(editorSource, Does.Contain("Per Axis Control"));
            Assert.That(editorSource, Does.Contain("FindParameter(\"m_EditorUniformFade\")"));
            Assert.That(editorSource, Does.Contain("FindParameter(\"m_EditorPositiveFade\")"));
            Assert.That(editorSource, Does.Contain("FindParameter(\"m_EditorNegativeFade\")"));
            Assert.That(editorSource, Does.Contain("FindParameter(\"m_EditorAdvancedFade\")"));
            Assert.That(editorSource, Does.Contain("ApplyEditorFadeToRuntimeProperties"));
            Assert.That(editorSource, Does.Not.Contain("DrawProperty(m_PositiveFade, s_PositiveFadeLabel);"));
            Assert.That(editorSource, Does.Not.Contain("DrawProperty(m_NegativeFade, s_NegativeFadeLabel);"));
            Assert.That(editorSource, Does.Contain("Mask Texture"));
            Assert.That(editorSource, Does.Contain("Mask Material"));
            Assert.That(editorSource, Does.Contain("DrawMaterialInspector"));
            Assert.That(editorSource, Does.Contain("FogVolumeVoxelize"));
            Assert.That(editorSource, Does.Contain("EditorGUILayout.Foldout"));
        }

        private static string GetPackageFilePath(params string[] path)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp"),
                Path.Combine(projectRoot, "Packages", "Custom_URP")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(path));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(path));
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

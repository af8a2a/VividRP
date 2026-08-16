using System;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.UIElements;
using VividRP.Editor.RenderPipeline;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using Object = UnityEngine.Object;

namespace VividRP.Tests
{
    public class VividRenderPipelineAssetEditorTests
    {
        private const string GlobalSettingsPath =
            "Assets/Tests/VividRP/VividRenderPipelineAssetEditorGlobalSettings.asset";

        private const string DefaultVolumeProfilePath =
            "Assets/Tests/VividRP/VividRenderPipelineAssetEditorDefaultVolume.asset";

        private const string AlternateVolumeProfilePath =
            "Assets/Tests/VividRP/VividRenderPipelineAssetEditorAltVolume.asset";

        private RenderPipelineAsset m_PreviousGraphicsPipeline;
        private RenderPipelineAsset m_PreviousQualityPipeline;
        private bool m_PreviousUseScriptableRenderPipelineBatching;
        private VividRenderPipelineGlobalSettings m_GlobalSettings;
        private VividRenderPipelineAsset m_PipelineAsset;

        private static void DeleteAssetIfExists(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                AssetDatabase.DeleteAsset(path);
        }

        [SetUp]
        public void SetUp()
        {
            m_PreviousGraphicsPipeline = GraphicsSettings.defaultRenderPipeline;
            m_PreviousQualityPipeline = QualitySettings.renderPipeline;
            m_PreviousUseScriptableRenderPipelineBatching = GraphicsSettings.useScriptableRenderPipelineBatching;

            DeleteAssetIfExists(AlternateVolumeProfilePath);
            DeleteAssetIfExists(DefaultVolumeProfilePath);
            DeleteAssetIfExists(GlobalSettingsPath);

            m_GlobalSettings =
                RenderPipelineGlobalSettingsUtils.Create<VividRenderPipelineGlobalSettings>(GlobalSettingsPath);
            EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<VividRenderPipeline>(m_GlobalSettings);
            VividDefaultVolumeProfileEditorUtility.EnsureDefaultVolumeProfile(m_GlobalSettings,
                DefaultVolumeProfilePath);

            m_PipelineAsset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();
        }

        [TearDown]
        public void TearDown()
        {
            if (VolumeManager.instance.isInitialized)
                VolumeManager.instance.Deinitialize();

            GraphicsSettings.useScriptableRenderPipelineBatching = m_PreviousUseScriptableRenderPipelineBatching;
            GraphicsSettings.defaultRenderPipeline = m_PreviousGraphicsPipeline;
            QualitySettings.renderPipeline = m_PreviousQualityPipeline;
            EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<VividRenderPipeline>(null);

            if (m_PipelineAsset != null)
                Object.DestroyImmediate(m_PipelineAsset);

            DeleteAssetIfExists(AlternateVolumeProfilePath);
            DeleteAssetIfExists(DefaultVolumeProfilePath);
            DeleteAssetIfExists(GlobalSettingsPath);
        }

        [Test]
        public void CreateInspectorGUI_BuildsRenderGraphAndRenderingOptionFields()
        {
            var editor = UnityEditor.Editor.CreateEditor(m_PipelineAsset, typeof(VividRenderPipelineAssetEditor));

            try
            {
                var root = editor.CreateInspectorGUI();

                Assert.That(root.Q<PropertyField>("vivid-rp-asset-render-graph-field"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-auto-exposure-implementation-field"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-async-compute-field"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-gpu-driven-field"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-gpu-driven-occlusion-culling-field"), Is.Not.Null);
                Assert.That(root.Q<Foldout>("vivid-rp-asset-virtual-texture-foldout"), Is.Not.Null);
                Assert.That(
                    root.Q<PropertyField>("vivid-rp-asset-gpu-driven-vt-physical-pool-quality-field"),
                    Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-vt-io-backend-field"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-vt-max-residency-allocations-field"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-vt-max-prefetch-allocations-field"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-vt-max-page-uploads-field"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-vt-max-upload-mib-field"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-vt-max-in-flight-chunks-field"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-vt-decode-concurrency-field"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-vt-decoded-cache-budget-field"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-gpu-driven-decal-field"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-decal-technique-field"), Is.Not.Null);
                Assert.That(root.Q<HelpBox>("vivid-rp-asset-terrain-rvt-decal-dependencies"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-gpu-driven-debug-overlay-field"), Is.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-srp-batcher-field"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-support-probe-volume-field"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-probe-volume-sh-bands-field"), Is.Not.Null);
                Assert.That(root.Q<Foldout>("vivid-rp-asset-reflection-probe-atlas-foldout"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-reflection-probe-atlas-resolution-field"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-reflection-probe-atlas-format-field"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-reflection-probe-atlas-last-valid-cube-mip-field"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-reflection-probe-atlas-decrease-res-to-fit-field"), Is.Not.Null);
                Assert.That(root.Q<Foldout>("vivid-rp-asset-local-volumetric-fog-foldout"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-volumetric-fog-control-mode-field"), Is.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-volumetric-fog-budget-field"), Is.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-volumetric-fog-resolution-depth-ratio-field"), Is.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-volumetric-fog-screen-resolution-percentage-field"), Is.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-volumetric-fog-volume-slice-count-field"), Is.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-max-local-volumetric-fog-count-field"), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        [Test]
        public void Asset_DefaultsToAsyncComputeAndSRPBatcherEnabled()
        {
            Assert.That(m_PipelineAsset.AutoExposureImplementation, Is.EqualTo(AutoExposureImplementationPath.Unreal));
            Assert.That(m_PipelineAsset.EnableAsyncCompute, Is.True);
            Assert.That(m_PipelineAsset.EnableGPUDriven, Is.False);
            Assert.That(m_PipelineAsset.EnableGPUDrivenOcclusionCulling, Is.True);
            Assert.That(m_PipelineAsset.EnableGPUDrivenDecal, Is.False);
            Assert.That(m_PipelineAsset.DecalTechnique, Is.EqualTo(VividDecalTechnique.ClusteredBindless));
            Assert.That(m_PipelineAsset.EnableSRPBatcher, Is.True);
            Assert.That(m_PipelineAsset.SupportProbeVolume, Is.False);
            Assert.That(m_PipelineAsset.ProbeVolumeSHBands, Is.EqualTo(ProbeVolumeSHBands.SphericalHarmonicsL2));
            Assert.That(m_PipelineAsset.ReflectionProbeAtlasResolution, Is.EqualTo(VividReflectionProbeAtlasResolution.Resolution4096x4096));
            Assert.That(m_PipelineAsset.ReflectionProbeAtlasFormat, Is.EqualTo(VividReflectionProbeAtlasFormat.R16G16B16A16));
            Assert.That(m_PipelineAsset.ReflectionProbeAtlasDimensions, Is.EqualTo(new Vector2Int(4096, 4096)));
            Assert.That(m_PipelineAsset.ReflectionProbeAtlasLastValidCubeMip, Is.EqualTo(3));
            Assert.That(m_PipelineAsset.ReflectionProbeAtlasDecreaseResToFit, Is.True);
        }

        [Test]
        public void SerializedObject_WritesGPUDrivenDecalToggle_ToAssetProperty()
        {
            var serializedObject = new SerializedObject(m_PipelineAsset);
            var property = serializedObject.FindProperty("m_EnableGPUDrivenDecal");

            Assert.That(property, Is.Not.Null);

            property.boolValue = true;
            serializedObject.ApplyModifiedProperties();

            Assert.That(m_PipelineAsset.EnableGPUDrivenDecal, Is.True);
        }

        [Test]
        public void TerrainRVTDecalTechnique_ShowsDependencyWarningWithoutFallback()
        {
            m_PipelineAsset.DecalTechnique = VividDecalTechnique.TerrainRuntimeVirtualTexture;
            var editor = UnityEditor.Editor.CreateEditor(
                m_PipelineAsset,
                typeof(VividRenderPipelineAssetEditor));
            try
            {
                VisualElement root = editor.CreateInspectorGUI();
                HelpBox helpBox = root.Q<HelpBox>("vivid-rp-asset-terrain-rvt-decal-dependencies");

                Assert.That(helpBox, Is.Not.Null);
                Assert.That(helpBox.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(helpBox.messageType, Is.EqualTo(HelpBoxMessageType.Warning));
                Assert.That(helpBox.text, Does.Contain("GPUDriven"));
                Assert.That(helpBox.text, Does.Contain("does not fall back"));
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        [Test]
        public void ApplySRPBatcherSetting_UsesAssetToggle()
        {
            m_PipelineAsset.EnableSRPBatcher = false;
            VividRenderPipeline.ApplySRPBatcherSetting(m_PipelineAsset);

            Assert.That(GraphicsSettings.useScriptableRenderPipelineBatching, Is.False);

            m_PipelineAsset.EnableSRPBatcher = true;
            VividRenderPipeline.ApplySRPBatcherSetting(m_PipelineAsset);

            Assert.That(GraphicsSettings.useScriptableRenderPipelineBatching, Is.True);
        }

        [Test]
        public void ShouldEnableAsyncCompute_ReturnsFalse_WhenPipelineSettingIsDisabled()
        {
            var passDefinition = new RenderGraphPassDefinition
            {
                EnableAsyncCompute = true,
            };

            var enabled = PassRecorder.ShouldEnableAsyncCompute(false, new MaterialClassificationPass(), passDefinition);

            Assert.That(enabled, Is.False);
        }

        [Test]
        public void TryResolveRequiredBlitShaders_FallsBack_WhenResourceFieldsAreMissing()
        {
            var resources = new VividRPCoreResources();

            var resolved = VividRenderPipeline.TryResolveRequiredBlitShaders(
                resources,
                out var coreBlitShader,
                out var coreBlitColorAndDepthShader);

            Assert.That(resolved, Is.True);
            Assert.That(coreBlitShader, Is.Not.Null);
            Assert.That(coreBlitColorAndDepthShader, Is.Not.Null);
        }
    }

    public class VividRenderPipelineRenderGraphRecoveryTests
    {
        private sealed class DummyPassData
        {
        }

        [Test]
        public void TryRecordAndExecuteRenderGraph_ResetsRenderGraph_WhenRecordingThrows()
        {
            var renderGraph = new RenderGraph("VividRP Test RenderGraph");
            var cmdBuffer = CommandBufferPool.Get("VividRP Test");

            try
            {
                var renderGraphParameters = new RenderGraphParameters
                {
                    commandBuffer = cmdBuffer,
                    currentFrameIndex = 1,
                    executionId = default,
                    invalidContextForTesting = true,
                };

                var failed = VividRenderPipeline.TryRecordAndExecuteRenderGraph(
                    renderGraph,
                    renderGraphParameters,
                    () => throw new InvalidOperationException("Simulated RenderGraph failure"));

                Assert.That(failed, Is.False);

                var succeeded = VividRenderPipeline.TryRecordAndExecuteRenderGraph(
                    renderGraph,
                    renderGraphParameters,
                    () =>
                    {
                        using var builder = renderGraph.AddUnsafePass<DummyPassData>("RecoveryPass", out _);
                        builder.AllowPassCulling(false);
                        builder.SetRenderFunc<DummyPassData>(static (data, context) => { });
                    });

                Assert.That(succeeded, Is.True);
            }
            finally
            {
                cmdBuffer.Clear();
                CommandBufferPool.Release(cmdBuffer);
                renderGraph.Cleanup();
            }
        }
    }
}

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
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-async-compute-field"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-gpu-driven-field"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-rp-asset-srp-batcher-field"), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        [Test]
        public void Asset_DefaultsToAsyncComputeAndSRPBatcherEnabled()
        {
            Assert.That(m_PipelineAsset.EnableAsyncCompute, Is.True);
            Assert.That(m_PipelineAsset.EnableGPUDriven, Is.False);
            Assert.That(m_PipelineAsset.EnableSRPBatcher, Is.True);
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

            var enabled = PassRecorder.ShouldEnableAsyncCompute(false, new ClassificationPass(), passDefinition);

            Assert.That(enabled, Is.False);
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

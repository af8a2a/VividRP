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

            GraphicsSettings.defaultRenderPipeline = m_PreviousGraphicsPipeline;
            QualitySettings.renderPipeline = m_PreviousQualityPipeline;
            EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<VividRenderPipeline>(null);

            if (m_PipelineAsset != null)
                Object.DestroyImmediate(m_PipelineAsset);

            DeleteAssetIfExists(AlternateVolumeProfilePath);
            DeleteAssetIfExists(DefaultVolumeProfilePath);
            DeleteAssetIfExists(GlobalSettingsPath);
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

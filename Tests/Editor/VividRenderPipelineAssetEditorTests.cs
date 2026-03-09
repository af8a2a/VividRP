using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using VividRP.Editor.RenderPipeline;
using VividRP.Runtime;

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
}
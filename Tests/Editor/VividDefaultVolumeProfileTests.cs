using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Editor.RenderPipeline;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class VividDefaultVolumeProfileTests
    {
        private const string GlobalSettingsPath = "Assets/Tests/VividRP/VividRenderPipelineGlobalSettings.asset";
        private const string DefaultVolumeProfilePath = "Assets/Tests/VividRP/VividDefaultVolumeProfile.asset";

        private RenderPipelineAsset m_PreviousGraphicsPipeline;
        private RenderPipelineAsset m_PreviousQualityPipeline;
        private VividRenderPipelineAsset m_PipelineAsset;
        private VividRenderPipelineGlobalSettings m_GlobalSettings;

        [SetUp]
        public void SetUp()
        {
            m_PreviousGraphicsPipeline = GraphicsSettings.defaultRenderPipeline;
            m_PreviousQualityPipeline = QualitySettings.renderPipeline;

            DeleteAssetIfExists(DefaultVolumeProfilePath);
            DeleteAssetIfExists(GlobalSettingsPath);

            m_GlobalSettings = RenderPipelineGlobalSettingsUtils.Create<VividRenderPipelineGlobalSettings>(GlobalSettingsPath);
            EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<VividRenderPipeline>(m_GlobalSettings);

            m_PipelineAsset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();
            GraphicsSettings.defaultRenderPipeline = m_PipelineAsset;
            QualitySettings.renderPipeline = m_PipelineAsset;
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

            DeleteAssetIfExists(DefaultVolumeProfilePath);
            DeleteAssetIfExists(GlobalSettingsPath);
        }

        [Test]
        public void Create_PopulatesDefaultVolumeProfileSettings_WhenGlobalSettingsAssetIsCreated()
        {
            var settings = m_GlobalSettings.GetSettings<VividDefaultVolumeProfileSettings>();

            Assert.That(settings, Is.Not.Null);
        }

        [Test]
        public void EnsureDefaultVolumeProfile_CreatesAndAssignsProfile_WhenMissing()
        {
            var settings = m_GlobalSettings.GetSettings<VividDefaultVolumeProfileSettings>();
            settings.volumeProfile = null;

            var profile = VividDefaultVolumeProfileEditorUtility.EnsureDefaultVolumeProfile(
                m_GlobalSettings,
                DefaultVolumeProfilePath);

            Assert.That(profile, Is.Not.Null);
            Assert.That(settings.volumeProfile, Is.SameAs(profile));
            Assert.That(AssetDatabase.GetAssetPath(profile), Is.EqualTo(DefaultVolumeProfilePath));
        }

        [Test]
        public void EnsureDefaultVolumeProfile_ReusesExistingProfile_WhenAssetAlreadyExists()
        {
            CoreUtils.EnsureFolderTreeInAssetFilePath(DefaultVolumeProfilePath);
            var existingProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(existingProfile, DefaultVolumeProfilePath);

            var profile = VividDefaultVolumeProfileEditorUtility.EnsureDefaultVolumeProfile(
                m_GlobalSettings,
                DefaultVolumeProfilePath);
            var settings = m_GlobalSettings.GetSettings<VividDefaultVolumeProfileSettings>();

            Assert.That(profile, Is.SameAs(existingProfile));
            Assert.That(settings.volumeProfile, Is.SameAs(existingProfile));
        }

        private static void DeleteAssetIfExists(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                AssetDatabase.DeleteAsset(path);
        }
    }
}

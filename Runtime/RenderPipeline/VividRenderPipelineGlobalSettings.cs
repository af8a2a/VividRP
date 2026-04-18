using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [DisplayInfo(name = "VividRP Global Settings", order = CoreUtils.Sections.section1)]
    [SupportedOnRenderPipeline(typeof(VividRenderPipelineAsset))]
    [DisplayName("VividRP")]
    public class VividRenderPipelineGlobalSettings
        : RenderPipelineGlobalSettings<VividRenderPipelineGlobalSettings, VividRenderPipeline>
    {
        [SerializeField]
#pragma warning disable 618
        private ProbeVolumeSceneData m_APVSceneData;
#pragma warning restore 618

        [SerializeField]
        private RenderPipelineGraphicsSettingsContainer m_Settings = new();

        protected override List<IRenderPipelineGraphicsSettings> settingsList => m_Settings.settingsList;

        internal T GetSettings<T>() where T : class, IRenderPipelineGraphicsSettings
        {
            foreach (var settings in m_Settings.settingsList)
            {
                if (settings is T typedSettings)
                    return typedSettings;
            }

            return null;
        }

        public override void Initialize(RenderPipelineGlobalSettings source = null)
        {
            EnsureSettings<VividDefaultVolumeProfileSettings>();
            EnsureCoreGraphicsSettings("UnityEngine.Rendering.ProbeVolumeGlobalSettings");
        }

#pragma warning disable 618
        internal ProbeVolumeSceneData GetOrCreateAPVSceneData()
        {
            if (m_APVSceneData == null)
                m_APVSceneData = new ProbeVolumeSceneData(this);

            m_APVSceneData.SetParentObject(this);
            return m_APVSceneData;
        }
#pragma warning restore 618

        private void EnsureSettings<T>() where T : class, IRenderPipelineGraphicsSettings, new()
        {
            if (GetSettings<T>() != null)
                return;

            m_Settings.settingsList.Add(new T());
        }

        private void EnsureCoreGraphicsSettings(string settingsTypeName)
        {
            Type settingsType = typeof(IProbeVolumeEnabledRenderPipeline).Assembly.GetType(settingsTypeName);
            if (settingsType == null || !typeof(IRenderPipelineGraphicsSettings).IsAssignableFrom(settingsType))
                return;

            foreach (var settings in m_Settings.settingsList)
            {
                if (settingsType.IsInstanceOfType(settings))
                    return;
            }

            if (Activator.CreateInstance(settingsType, true) is IRenderPipelineGraphicsSettings settingsInstance)
                m_Settings.settingsList.Add(settingsInstance);
        }

#if UNITY_EDITOR
        internal static VividRenderPipelineGlobalSettings Ensure(bool canCreateNewAsset = true)
        {
            VividRenderPipelineGlobalSettings currentInstance = GraphicsSettings.
                GetSettingsForRenderPipeline<VividRenderPipeline>() as VividRenderPipelineGlobalSettings;

            if (RenderPipelineGlobalSettingsUtils.TryEnsure<VividRenderPipelineGlobalSettings, VividRenderPipeline>(
                    ref currentInstance, "Assets/VividRPGlobalSettings.asset", canCreateNewAsset))
            {
                if (currentInstance != null)
                    currentInstance.Initialize();
            }

            return currentInstance;
        }
#endif
    }
}

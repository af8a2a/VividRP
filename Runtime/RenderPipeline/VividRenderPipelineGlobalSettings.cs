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

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(VividRenderPipelineAsset))]
    internal sealed class VividDefaultVolumeProfileSettings : IDefaultVolumeProfileSettings
    {
        [SerializeField]
        private VolumeProfile m_VolumeProfile;

        int IRenderPipelineGraphicsSettings.version => 1;

        public VolumeProfile volumeProfile
        {
            get => m_VolumeProfile;
            set => this.SetValueAndNotify(ref m_VolumeProfile, value, nameof(m_VolumeProfile));
        }
    }
}

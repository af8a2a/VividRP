using System;

namespace UnityEngine.Rendering.Universal
{

    partial class UniversalRenderPipelineAsset
    {

        // Sky Settings
        [SerializeField] SkyResolution m_SkyReflectionSize = SkyResolution._512;

        /// <summary>
        /// Resolution of the sky reflection cubemap.
        /// </summary>
        public SkyResolution skyReflectionSize
        {
            get => m_SkyReflectionSize;
            internal set => m_SkyReflectionSize = value;
        }

    }
}
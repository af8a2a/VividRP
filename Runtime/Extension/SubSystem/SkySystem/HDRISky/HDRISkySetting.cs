using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenu("Sky/HDRI Sky")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [SkyUniqueID((int)SkyType.HDRI)]
    public sealed class HDRISkySetting : SkySettings
    {
        private static Cubemap m_DefaultHdriSky;

        protected override void OnEnable()
        {
            base.OnEnable();

            if (m_DefaultHdriSky == null)
            {
                var runtimeTextures = GraphicsSettings.GetRenderPipelineSettings<SkyRuntimeResources>();
                m_DefaultHdriSky = runtimeTextures.defaultHDRISky;
            }
        }

        /// <summary>Cubemap used to render the HDRI sky.</summary>
        [Tooltip("Specify the cubemap uses to render the sky.")]
        public CubemapParameter hdriSky = new CubemapParameter(m_DefaultHdriSky);

        /// <summary>
        /// Returns the hash code of the HDRI sky parameters.
        /// </summary>
        /// <returns>The hash code of the HDRI sky parameters.</returns>
        public override int GetHashCode()
        {
            int hash = base.GetHashCode();

            unchecked
            {
#if UNITY_2019_3 // In 2019.3, when we call GetHashCode on a VolumeParameter it generate garbage (due to the boxing of the generic parameter)
                hash = hdriSky.value != null ? hash * 23 + hdriSky.value.GetHashCode() : hash;   
#else
                hash = hash * 23 + hdriSky.GetHashCode();
#endif
            }

            return hash;
        }

        /// <summary>
        /// Returns HDRISky Renderer type.
        /// </summary>
        /// <returns></returns>
        public override Type GetSkyRendererType()
        {
            return typeof(HDRISkyRenderer);
        }
    }
}
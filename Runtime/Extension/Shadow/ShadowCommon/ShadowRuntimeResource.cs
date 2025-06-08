using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Features.Shadow.ScreenSpaceShadow.PCSSShadow
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class ShadowRuntimeResource : IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;


        [SerializeField] [ResourcePath("Textures/ShadowRamp/DirectionalShadowRamp.png")]
        private Texture2D m_DefaultDirShadowRampTex;

        /// <summary>
        /// Default directional shadowramp texture.
        /// </summary>
        public Texture2D defaultDirShadowRampTex
        {
            get => m_DefaultDirShadowRampTex;
            set => this.SetValueAndNotify(ref m_DefaultDirShadowRampTex, value, nameof(m_DefaultDirShadowRampTex));
        }
        
        
        [SerializeField] [ResourcePath("Runtime/Extension/Shadow/UberScreenSpaceShadow/Shader/ScreenSpaceShadowsClassify.compute")]
        private ComputeShader m_ShadowClassifyShader;

        /// <summary>
        /// Default directional shadowramp texture.
        /// </summary>
        public ComputeShader shadowClassifyShader
        {
            get => m_ShadowClassifyShader;
            set => this.SetValueAndNotify(ref m_ShadowClassifyShader, value, nameof(m_ShadowClassifyShader));
        }

        [SerializeField] [ResourcePath("Runtime/Extension/Shadow/UberScreenSpaceShadow/Shader/ScreenSpaceShadowmap.compute")]
        private ComputeShader m_ShadowmapShader;

        /// <summary>
        /// Default directional shadowramp texture.
        /// </summary>
        public ComputeShader shadowmapShader
        {
            get => m_ShadowmapShader;
            set => this.SetValueAndNotify(ref m_ShadowmapShader, value, nameof(m_ShadowmapShader));
        }

        
    }
}
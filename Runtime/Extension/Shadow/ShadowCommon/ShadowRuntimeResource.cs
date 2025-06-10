using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

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
        
        
        [SerializeField] [ResourcePath("Runtime/Extension/Shadow/UberScreenSpaceShadow/Shader/ScreenSpaceShadowClassify.compute")]
        private ComputeShader m_ShadowClassifyShader;

        /// <summary>
        /// Default directional shadowramp texture.
        /// </summary>
        public ComputeShader shadowClassifyShader
        {
            get => m_ShadowClassifyShader;
            set => this.SetValueAndNotify(ref m_ShadowClassifyShader, value, nameof(m_ShadowClassifyShader));
        }

        [SerializeField] [ResourcePath("Runtime/Extension/Shadow/UberScreenSpaceShadow/Shader/ScreenSpaceShadowResolve.compute")]
        private ComputeShader m_ShadowmapResolveShader;

        /// <summary>
        /// Default directional shadowramp texture.
        /// </summary>
        public ComputeShader shadowmapResolveShader
        {
            get => m_ShadowmapResolveShader;
            set => this.SetValueAndNotify(ref m_ShadowmapResolveShader, value, nameof(m_ShadowmapResolveShader));
        }

        [SerializeField] [ResourcePath("Runtime/Extension/Shadow/UberScreenSpaceShadow/Shader/ScreenSpaceShadowFilter.compute")]
        private ComputeShader m_shadowmapFilterShader;

        /// <summary>
        /// Default directional shadowramp texture.
        /// </summary>
        public ComputeShader shadowmapFilterShader
        {
            get => m_shadowmapFilterShader;
            set => this.SetValueAndNotify(ref m_shadowmapFilterShader, value, nameof(m_shadowmapFilterShader));
        }

    }
}
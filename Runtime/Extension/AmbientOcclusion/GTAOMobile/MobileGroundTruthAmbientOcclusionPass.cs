using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    // The SSAO Pass
    public class MobileGroundTruthAmbientOcclusionPass : ScriptableRenderPass
    {
        // Properties
        //private bool isRendererDeferred => m_Renderer != null && m_Renderer is UniversalRenderer && ((UniversalRenderer)m_Renderer).renderingMode == RenderingMode.Deferred;
        private bool isRendererDeferred = false;

        // Private Variables
        private bool m_SupportsR8RenderTextureFormat =
            SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8);

        private Material m_Material;
        private Vector4[] m_CameraTopLeftCorner = new Vector4[2];
        private Vector4[] m_CameraXExtent = new Vector4[2];
        private Vector4[] m_CameraYExtent = new Vector4[2];
        private Vector4[] m_CameraZExtent = new Vector4[2];
        private Matrix4x4[] m_CameraViewProjections = new Matrix4x4[2];
        private ProfilingSampler m_ProfilingSampler = new ProfilingSampler("GTAOMobile");

        private MobileGroundTruthAmbientOcclusion m_CurrentSettings;

        // Constants
        private const string k_SSAOTextureName = "_ScreenSpaceOcclusionTexture";
        private const string k_SSAOAmbientOcclusionParamName = "_AmbientOcclusionParam";

        // Statics

        private static readonly int s_SSAOParamsID = Shader.PropertyToID("_SSAOParams");
        private static readonly int s_SSAOFinalTextureID = Shader.PropertyToID(k_SSAOTextureName);


        private static readonly int s_CameraViewXExtentID = Shader.PropertyToID("_CameraViewXExtent");
        private static readonly int s_CameraViewYExtentID = Shader.PropertyToID("_CameraViewYExtent");
        private static readonly int s_CameraViewZExtentID = Shader.PropertyToID("_CameraViewZExtent");
        private static readonly int s_ProjectionParams2ID = Shader.PropertyToID("_ProjectionParams2");
        private static readonly int s_CameraViewProjectionsID = Shader.PropertyToID("_CameraViewProjections");

        private static readonly int s_CameraViewTopLeftCornerID =
            Shader.PropertyToID("_CameraViewTopLeftCorner");

        private static readonly int s_CameraDepthTextureID = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int s_CameraNormalsTextureID = Shader.PropertyToID("_CameraNormalsTexture");

        private static readonly int SSAO_UVToView_ID = Shader.PropertyToID("_SSAO_UVToView");

        private enum ShaderPasses
        {
            AO = 0,
            BlurHorizontal = 1,
            BlurVertical = 2,
            BlurFinal = 3,
            AfterOpaque = 4
        }


        internal MobileGroundTruthAmbientOcclusionPass()
        {
        }

        // Structs
        private struct SSAOMaterialParams
        {
            internal bool orthographicCamera;
            internal int sampleCount;
            internal bool sourceDepthNormals;
            internal bool sourceDepthHigh;
            internal bool sourceDepthMedium;
            internal bool sourceDepthLow;
            internal Vector4 ssaoParams;

            internal SSAOMaterialParams(ref MobileGroundTruthAmbientOcclusion settings, bool isOrthographic)
            {
                bool isUsingDepthNormals =
                    settings.Source == DepthSource.DepthNormals;
                orthographicCamera = isOrthographic;
                sampleCount = settings.SampleCount.value;
                sourceDepthNormals =
                    settings.Source == DepthSource.DepthNormals;
                sourceDepthHigh = !isUsingDepthNormals &&
                                  settings.NormalSamples.value == NormalQuality.High;
                sourceDepthMedium = !isUsingDepthNormals && settings.NormalSamples.value ==
                    NormalQuality.Medium;
                sourceDepthLow = !isUsingDepthNormals &&
                                 settings.NormalSamples == NormalQuality.Low;
                ssaoParams = new Vector4(
                    settings.Intensity.value, // Intensity
                    settings.Radius.value, // Radius
                    1.0f / (settings.Downsample.value ? 2 : 1), // Downsampling
                    settings.SampleCount.value // Sample count
                );
            }

            internal bool Equals(ref SSAOMaterialParams other)
            {
                return orthographicCamera == other.orthographicCamera
                       && sampleCount == other.sampleCount
                       && sourceDepthNormals == other.sourceDepthNormals
                       && sourceDepthHigh == other.sourceDepthHigh
                       && sourceDepthMedium == other.sourceDepthMedium
                       && sourceDepthLow == other.sourceDepthLow
                       && ssaoParams == other.ssaoParams
                    ;
            }
        }

        private SSAOMaterialParams m_SSAOParamsPrev = new SSAOMaterialParams();

        internal bool Setup(MobileGroundTruthAmbientOcclusion featureSettings, Material material)
        {
            m_Material = material;
            m_CurrentSettings = featureSettings;

            DepthSource source;
            if (isRendererDeferred)
            {
                renderPassEvent = featureSettings.AfterOpaque.value
                    ? RenderPassEvent.AfterRenderingOpaques
                    : RenderPassEvent.AfterRenderingGbuffer;
                source = DepthSource.DepthNormals;
            }
            else
            {
                // Rendering after PrePasses is usually correct except when depth priming is in play:
                // then we rely on a depth resolve taking place after the PrePasses in order to have it ready for SSAO.
                // Hence we set the event to RenderPassEvent.AfterRenderingPrePasses + 1 at the earliest.
                renderPassEvent = featureSettings.AfterOpaque.value
                    ? RenderPassEvent.AfterRenderingOpaques
                    : RenderPassEvent.AfterRenderingPrePasses + 1;
                source = m_CurrentSettings.Source.value;
            }


            switch (source)
            {
                case DepthSource.Depth:
                    ConfigureInput(ScriptableRenderPassInput.Depth);
                    break;
                case DepthSource.DepthNormals:
                    ConfigureInput(ScriptableRenderPassInput
                        .Normal); // need depthNormal prepass for forward-only geometry
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return m_Material != null
                   && m_CurrentSettings.Intensity.value > 0.0f
                   && m_CurrentSettings.Radius.value > 0.0f
                   && m_CurrentSettings.SampleCount.value > 0;
        }


        private void InitSSAOPassData(ref PassData data)
        {
            data.material = m_Material;
            data.afterOpaque = m_CurrentSettings.AfterOpaque.value;
            data.directLightingStrength = m_CurrentSettings.DirectLightingStrength.value;
        }

        internal class PassData
        {
            internal bool afterOpaque;
            internal Material material;
            internal float directLightingStrength;
            internal bool Downsample = false;
            internal float Intensity = 3.0f;
            internal float DirectLightingStrength = 0.25f;
            internal float Radius = 0.035f;
            internal int SampleCount = 4;

            internal TextureHandle cameraColor;
            internal TextureHandle AOTexture;
            internal TextureHandle finalTexture;
            internal TextureHandle blurTexture;
            internal TextureHandle cameraNormalsTexture;
        }

        private void SetupKeywordsAndParameters(ref MobileGroundTruthAmbientOcclusion settings,
            ref UniversalCameraData cameraData)
        {
#if ENABLE_VR && ENABLE_XR_MODULE
                int eyeCount = cameraData.xr.enabled && cameraData.xr.singlePassEnabled ? 2 : 1;
#else
            int eyeCount = 1;
#endif

            for (int eyeIndex = 0; eyeIndex < eyeCount; eyeIndex++)
            {
                Matrix4x4 view = cameraData.GetViewMatrix(eyeIndex);
                Matrix4x4 proj = cameraData.GetProjectionMatrix(eyeIndex);
                m_CameraViewProjections[eyeIndex] = proj * view;

                // camera view space without translation, used by SSAO.hlsl ReconstructViewPos() to calculate view vector.
                Matrix4x4 cview = view;
                cview.SetColumn(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                Matrix4x4 cviewProj = proj * cview;
                Matrix4x4 cviewProjInv = cviewProj.inverse;

                Vector4 topLeftCorner = cviewProjInv.MultiplyPoint(new Vector4(-1, 1, -1, 1));
                Vector4 topRightCorner = cviewProjInv.MultiplyPoint(new Vector4(1, 1, -1, 1));
                Vector4 bottomLeftCorner = cviewProjInv.MultiplyPoint(new Vector4(-1, -1, -1, 1));
                Vector4 farCentre = cviewProjInv.MultiplyPoint(new Vector4(0, 0, 1, 1));
                m_CameraTopLeftCorner[eyeIndex] = topLeftCorner;
                m_CameraXExtent[eyeIndex] = topRightCorner - topLeftCorner;
                m_CameraYExtent[eyeIndex] = bottomLeftCorner - topLeftCorner;
                m_CameraZExtent[eyeIndex] = farCentre;
            }

            m_Material.SetVector(s_ProjectionParams2ID,
                new Vector4(1.0f / cameraData.camera.nearClipPlane, 0.0f, 0.0f, 0.0f));
            m_Material.SetMatrixArray(s_CameraViewProjectionsID, m_CameraViewProjections);
            m_Material.SetVectorArray(s_CameraViewTopLeftCornerID, m_CameraTopLeftCorner);
            m_Material.SetVectorArray(s_CameraViewXExtentID, m_CameraXExtent);
            m_Material.SetVectorArray(s_CameraViewYExtentID, m_CameraYExtent);
            m_Material.SetVectorArray(s_CameraViewZExtentID, m_CameraZExtent);


            // Setting keywords can be somewhat expensive on low-end platforms.
            // Previous params are cached to avoid setting the same keywords every frame.
            SSAOMaterialParams matParams = new SSAOMaterialParams(ref settings, cameraData.camera.orthographic);
            bool ssaoParamsDirty =
                !m_SSAOParamsPrev.Equals(ref matParams); // Checks if the parameters have changed.
            bool isParamsPropertySet =
                m_Material.HasProperty(s_SSAOParamsID); // Checks if the parameters have been set on the material.
            if (!ssaoParamsDirty && isParamsPropertySet)
                return;

            m_SSAOParamsPrev = matParams;
            CoreUtils.SetKeyword(m_Material, MobileGroundTruthAmbientOcclusionFeature.k_OrthographicCameraKeyword,
                matParams.orthographicCamera);
            CoreUtils.SetKeyword(m_Material, MobileGroundTruthAmbientOcclusionFeature.k_SourceDepthNormalsKeyword,
                matParams.sourceDepthNormals);
            CoreUtils.SetKeyword(m_Material, MobileGroundTruthAmbientOcclusionFeature.k_NormalReconstructionHighKeyword,
                matParams.sourceDepthHigh);
            CoreUtils.SetKeyword(m_Material, MobileGroundTruthAmbientOcclusionFeature.k_NormalReconstructionMediumKeyword,
                matParams.sourceDepthMedium);
            CoreUtils.SetKeyword(m_Material, MobileGroundTruthAmbientOcclusionFeature.k_NormalReconstructionLowKeyword,
                matParams.sourceDepthLow);
            m_Material.SetVector(s_SSAOParamsID, matParams.ssaoParams);
        }


        private void CreateRenderTextureHandles(RenderGraph renderGraph, UniversalResourceData resourceData,
            UniversalCameraData cameraData, out TextureHandle aoTexture, out TextureHandle blurTexture,
            out TextureHandle finalTexture)
        {
            // Descriptor for the final blur pass
            RenderTextureDescriptor finalTextureDescriptor = cameraData.cameraTargetDescriptor;
            finalTextureDescriptor.colorFormat = m_SupportsR8RenderTextureFormat
                ? RenderTextureFormat.R8
                : RenderTextureFormat.ARGB32;
            finalTextureDescriptor.depthStencilFormat = GraphicsFormat.None;
            finalTextureDescriptor.msaaSamples = 1;

            // Descriptor for the AO and Blur passes
            int downsampleDivider = m_CurrentSettings.Downsample.value ? 2 : 1;
            bool useRedComponentOnly = m_SupportsR8RenderTextureFormat;

            RenderTextureDescriptor aoBlurDescriptor = finalTextureDescriptor;
            aoBlurDescriptor.colorFormat =
                useRedComponentOnly ? RenderTextureFormat.R8 : RenderTextureFormat.ARGB32;
            aoBlurDescriptor.width /= downsampleDivider;
            aoBlurDescriptor.height /= downsampleDivider;

            // Handles
            aoTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, aoBlurDescriptor,
                "_SSAO_OcclusionTexture0", false, FilterMode.Bilinear);
            finalTexture = m_CurrentSettings.AfterOpaque.value
                ? resourceData.activeColorTexture
                : UniversalRenderer.CreateRenderGraphTexture(renderGraph, finalTextureDescriptor, k_SSAOTextureName,
                    false, FilterMode.Bilinear);

            blurTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, aoBlurDescriptor,
                "_SSAO_OcclusionTexture1", false, FilterMode.Bilinear);

            if (!m_CurrentSettings.AfterOpaque.value)
                resourceData.ssaoTexture = finalTexture;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();


            CreateRenderTextureHandles(renderGraph,
                resourceData,
                cameraData,
                out TextureHandle aoTexture,
                out TextureHandle blurTexture,
                out TextureHandle finalTexture);

            // Get the resources
            TextureHandle cameraDepthTexture = resourceData.cameraDepthTexture;
            TextureHandle cameraNormalsTexture = resourceData.cameraNormalsTexture;

            SetupKeywordsAndParameters(ref m_CurrentSettings, ref cameraData);
            using (IUnsafeRenderGraphBuilder builder =
                   renderGraph.AddUnsafePass<PassData>("Blit SSAO", out var passData, m_ProfilingSampler))
            {
                // Shader keyword changes are considered as global state modifications
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                // Fill in the Pass data...
                InitSSAOPassData(ref passData);
                passData.cameraColor = resourceData.cameraColor;
                passData.AOTexture = aoTexture;
                passData.finalTexture = finalTexture;
                passData.blurTexture = blurTexture;

                // Declare input textures
                builder.UseTexture(passData.AOTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.blurTexture, AccessFlags.ReadWrite);
                if (cameraDepthTexture.IsValid())
                    builder.UseTexture(cameraDepthTexture, AccessFlags.Read);
                if (m_CurrentSettings.Source == DepthSource.DepthNormals &&
                    cameraNormalsTexture.IsValid())
                {
                    builder.UseTexture(cameraNormalsTexture, AccessFlags.Read);
                    passData.cameraNormalsTexture = cameraNormalsTexture;
                }

                // The global SSAO texture only needs to be set if After Opaque is disabled...
                if (!passData.afterOpaque && finalTexture.IsValid())
                {
                    builder.UseTexture(passData.finalTexture, AccessFlags.ReadWrite);
                    builder.SetGlobalTextureAfterPass(finalTexture, s_SSAOFinalTextureID);
                }


                builder.SetRenderFunc((PassData data, UnsafeGraphContext rgContext) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(rgContext.cmd);
                    RenderBufferLoadAction finalLoadAction = data.afterOpaque
                        ? RenderBufferLoadAction.Load
                        : RenderBufferLoadAction.DontCare;

                    // Setup
                    if (data.cameraColor.IsValid())
                        PostProcessUtils.SetSourceSize(cmd, data.cameraColor);

                    if (data.cameraNormalsTexture.IsValid())
                        data.material.SetTexture(s_CameraNormalsTextureID, data.cameraNormalsTexture);


                    // AO Pass
                    Blitter.BlitCameraTexture(cmd, data.AOTexture, data.AOTexture, RenderBufferLoadAction.DontCare,
                        RenderBufferStoreAction.Store, data.material, (int)ShaderPasses.AO);


                    // Bilateral
                    Blitter.BlitCameraTexture(cmd, data.AOTexture, data.blurTexture,
                        RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, data.material,
                        (int)ShaderPasses.BlurHorizontal);
                    Blitter.BlitCameraTexture(cmd, data.blurTexture, data.AOTexture,
                        RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, data.material,
                        (int)ShaderPasses.BlurVertical);
                    Blitter.BlitCameraTexture(cmd, data.AOTexture, data.finalTexture, finalLoadAction,
                        RenderBufferStoreAction.Store, data.material,
                        (int)(data.afterOpaque
                            ? ShaderPasses.AfterOpaque
                            : ShaderPasses.BlurFinal));

                    // We only want URP shaders to sample SSAO if After Opaque is disabled...
                    if (!data.afterOpaque)
                    {
                        rgContext.cmd.SetKeyword(ShaderGlobalKeywords.ScreenSpaceOcclusion, true);
                        rgContext.cmd.SetGlobalVector(k_SSAOAmbientOcclusionParamName,
                            new Vector4(1f, 0f, 0f, data.directLightingStrength));
                    }
                });
            }
        }

        /// <inheritdoc/>
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
            {
                throw new ArgumentNullException("cmd");
            }

            if (!m_CurrentSettings.AfterOpaque.value)
            {
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.ScreenSpaceOcclusion, false);
            }
        }
    }
}
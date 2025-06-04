using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class TransparentGBufferPass : ScriptableRenderPass
    {
        const string m_ProfilerTag = "Path Tracing Transparent GBuffer";
        private readonly ProfilingSampler m_ProfilingSampler = new ProfilingSampler(m_ProfilerTag);

        private List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>();
        private FilteringSettings m_filter;

        // Depth Priming.
        private RenderStateBlock m_RenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);

        public RTHandle m_TransparentGBuffer0;
        public RTHandle m_TransparentGBuffer1;
        public RTHandle m_TransparentGBuffer2;
        private RTHandle[] m_TransparentGBuffers;


        // Shader Property IDs
        private static readonly int _MaxSample = Shader.PropertyToID("_MaxSample");
        private static readonly int _Sample = Shader.PropertyToID("_Sample");
        private static readonly int _MaxSteps = Shader.PropertyToID("_MaxSteps");
        private static readonly int _StepSize = Shader.PropertyToID("_StepSize");
        private static readonly int _MaxBounce = Shader.PropertyToID("_MaxBounce");
        private static readonly int _RayCount = Shader.PropertyToID("_RayCount");
        private static readonly int _TemporalIntensity = Shader.PropertyToID("_TemporalIntensity");
        private static readonly int _MaxBrightness = Shader.PropertyToID("_MaxBrightness");
        private static readonly int _IsProbeCamera = Shader.PropertyToID("_IsProbeCamera");
        private static readonly int _BackDepthEnabled = Shader.PropertyToID("_BackDepthEnabled");
        private static readonly int _IsAccumulationPaused = Shader.PropertyToID("_IsAccumulationPaused");
        private static readonly int _PrevInvViewProjMatrix = Shader.PropertyToID("_PrevInvViewProjMatrix");
        private static readonly int _PrevCameraPositionWS = Shader.PropertyToID("_PrevCameraPositionWS");
        private static readonly int _PixelSpreadAngleTangent = Shader.PropertyToID("_PixelSpreadAngleTangent");
        private static readonly int _FrameIndex = Shader.PropertyToID("_FrameIndex");

        private static readonly int _PathTracingEmissionTexture = Shader.PropertyToID("_PathTracingEmissionTexture");
        private static readonly int _CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int _CameraDepthAttachment = Shader.PropertyToID("_CameraDepthAttachment");
        private static readonly int _CameraBackDepthTexture = Shader.PropertyToID("_CameraBackDepthTexture");
        private static readonly int _CameraBackNormalsTexture = Shader.PropertyToID("_CameraBackNormalsTexture");

        private static readonly int _PathTracingAccumulationTexture =
            Shader.PropertyToID("_PathTracingAccumulationTexture");

        private static readonly int _PathTracingHistoryTexture = Shader.PropertyToID("_PathTracingHistoryTexture");

        private static readonly int _PathTracingHistoryEmissionTexture =
            Shader.PropertyToID("_PathTracingHistoryEmissionTexture");

        private static readonly int _PathTracingSampleTexture = Shader.PropertyToID("_PathTracingSampleTexture");

        private static readonly int _PathTracingHistorySampleTexture =
            Shader.PropertyToID("_PathTracingHistorySampleTexture");

        private static readonly int _PathTracingHistoryDepthTexture =
            Shader.PropertyToID("_PathTracingHistoryDepthTexture");

        private static readonly int _TransparentGBuffer0 = Shader.PropertyToID("_TransparentGBuffer0");
        private static readonly int _TransparentGBuffer1 = Shader.PropertyToID("_TransparentGBuffer1");
        private static readonly int _TransparentGBuffer2 = Shader.PropertyToID("_TransparentGBuffer2");
        private static readonly int _GBuffer0 = Shader.PropertyToID("_GBuffer0");
        private static readonly int _GBuffer1 = Shader.PropertyToID("_GBuffer1");
        private static readonly int _GBuffer2 = Shader.PropertyToID("_GBuffer2");


        public TransparentGBufferPass(string[] PassNames)
        {
            profilingSampler = new ProfilingSampler(m_ProfilerTag);
            RenderQueueRange queue = RenderQueueRange.transparent; // new RenderQueueRange(3000, 3000);
            m_filter = new FilteringSettings(queue);
            if (PassNames != null && PassNames.Length > 0)
            {
                foreach (var passName in PassNames)
                    m_ShaderTagIdList.Add(new ShaderTagId(passName));
            }
        }

        // From "URP-Package/Runtime/DeferredLights.cs".
        public GraphicsFormat GetGBufferFormat(int index)
        {
            if (index == 0) // sRGB albedo, materialFlags
                return QualitySettings.activeColorSpace == ColorSpace.Linear
                    ? GraphicsFormat.R8G8B8A8_SRGB
                    : GraphicsFormat.R8G8B8A8_UNorm;
            else if (index == 1) // sRGB specular, occlusion
                return GraphicsFormat.R8G8B8A8_UNorm;
            else if (index == 2) // normal normal normal packedSmoothness
                // NormalWS range is -1.0 to 1.0, so we need a signed render texture.
#if UNITY_2023_2_OR_NEWER
                if (SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_SNorm, GraphicsFormatUsage.Render))
#else
                if (SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_SNorm, FormatUsage.Render))
#endif
                    return GraphicsFormat.R8G8B8A8_SNorm;
                else
                    return GraphicsFormat.R16G16B16A16_SFloat;
            else
                return GraphicsFormat.None;
        }

        // This class stores the data needed by the pass, passed as parameter to the delegate function that executes the pass
        private class PassData
        {
            internal bool isOpenGL;

            internal RendererListHandle rendererListHandle;
        }

        // This static method is used to execute the pass and passed as the RenderFunc delegate to the RenderGraph render pass
        static void ExecutePass(PassData data, RasterGraphContext context)
        {
            if (data.isOpenGL)
                context.cmd.ClearRenderTarget(true, true, Color.black);
            else
                // We have to also clear previous color so that the "background" will remain empty (black) when moving the camera.
                context.cmd.ClearRenderTarget(false, true, Color.clear);

            context.cmd.DrawRendererList(data.rendererListHandle);
        }

        // This is where the renderGraph handle can be accessed.
        // Each ScriptableRenderPass can use the RenderGraph handle to add multiple render passes to the render graph
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // add a raster render pass to the render graph, specifying the name and the data type that will be passed to the ExecutePass function
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(m_ProfilerTag, out var passData))
            {
                // UniversalResourceData contains all the texture handles used by the renderer, including the active color and depth textures
                // The active color and depth textures are the main color and depth buffers that the camera renders into
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
                desc.msaaSamples = 1;
                desc.depthBufferBits = 0;

                // Albedo.rgb + MaterialFlags.a
                desc.graphicsFormat = GetGBufferFormat(0);
                TextureHandle gBuffer0Handle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc,
                    name: "_TransparentGBuffer0", false, FilterMode.Point, TextureWrapMode.Clamp);

                // Specular.rgb + Occlusion.a
                desc.graphicsFormat = GetGBufferFormat(1);
                TextureHandle gBuffer1Handle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc,
                    name: "_TransparentGBuffer1", false, FilterMode.Point, TextureWrapMode.Clamp);

                // NormalWS.rgb + Smoothness.a
                desc.graphicsFormat = GetGBufferFormat(2);
                TextureHandle gBuffer2Handle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc,
                    name: "_TransparentGBuffer2", false, FilterMode.Point, TextureWrapMode.Clamp);

                // [OpenGL] Reusing the depth buffer seems to cause black glitching artifacts, so clear the existing depth.
                bool isOpenGL = (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3) ||
                                (SystemInfo.graphicsDeviceType ==
                                 GraphicsDeviceType.OpenGLCore); // GLES 2 is deprecated.

                m_RenderStateBlock.depthState = new DepthState(true, CompareFunction.LessEqual);
                m_RenderStateBlock.mask |= RenderStateMask.Depth;

                m_RenderStateBlock.blendState = new BlendState
                {
                    blendState0 = new RenderTargetBlendState
                    {
                        destinationColorBlendMode = BlendMode.Zero,
                        sourceColorBlendMode = BlendMode.One,
                        destinationAlphaBlendMode = BlendMode.Zero,
                        sourceAlphaBlendMode = BlendMode.One,
                        colorBlendOperation = BlendOp.Add,
                        alphaBlendOperation = BlendOp.Add,
                        writeMask = ColorWriteMask.All
                    },

                    blendState1 = new RenderTargetBlendState
                    {
                        destinationColorBlendMode = BlendMode.Zero,
                        sourceColorBlendMode = BlendMode.One,
                        destinationAlphaBlendMode = BlendMode.Zero,
                        sourceAlphaBlendMode = BlendMode.One,
                        colorBlendOperation = BlendOp.Add,
                        alphaBlendOperation = BlendOp.Add,
                        writeMask = ColorWriteMask.All
                    },

                    blendState2 = new RenderTargetBlendState
                    {
                        destinationColorBlendMode = BlendMode.Zero,
                        sourceColorBlendMode = BlendMode.One,
                        destinationAlphaBlendMode = BlendMode.Zero,
                        sourceAlphaBlendMode = BlendMode.One,
                        colorBlendOperation = BlendOp.Add,
                        alphaBlendOperation = BlendOp.Add,
                        writeMask = ColorWriteMask.All
                    }
                };
                m_RenderStateBlock.mask |= RenderStateMask.Blend;

                // GBuffer cannot store surface data from transparent objects.
                SortingCriteria sortingCriteria = cameraData.defaultOpaqueSortFlags;
                RendererListDesc rendererListDesc = new RendererListDesc(m_ShaderTagIdList[0],
                    universalRenderingData.cullResults, cameraData.camera);
                rendererListDesc.stateBlock = m_RenderStateBlock;
                rendererListDesc.sortingCriteria = sortingCriteria;
                rendererListDesc.renderQueueRange = m_filter.renderQueueRange;

                // Setup pass data
                passData.isOpenGL = isOpenGL;
                passData.rendererListHandle = renderGraph.CreateRendererList(rendererListDesc);

                // We declare the RendererList we just created as an input dependency to this pass, via UseRendererList()
                builder.UseRendererList(passData.rendererListHandle);

                builder.SetRenderAttachment(gBuffer0Handle, 0);
                builder.SetRenderAttachment(gBuffer1Handle, 1);
                builder.SetRenderAttachment(gBuffer2Handle, 2);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);

                builder.SetGlobalTextureAfterPass(gBuffer0Handle, _TransparentGBuffer0);
                builder.SetGlobalTextureAfterPass(gBuffer1Handle, _TransparentGBuffer1);
                builder.SetGlobalTextureAfterPass(gBuffer2Handle, _TransparentGBuffer2);

                // We disable culling for this pass for the demonstrative purpose of this sample, as normally this pass would be culled,
                // since the destination texture is not used anywhere else
                //builder.AllowGlobalStateModification(true);
                //builder.AllowPassCulling(false);

                // Assign the ExecutePass function to the render pass delegate, which will be called by the render graph when executing the pass
                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));
            }
        }

        public void Dispose()
        {
            m_TransparentGBuffer0?.Release();
            m_TransparentGBuffer1?.Release();
            m_TransparentGBuffer2?.Release();
        }
    }
}
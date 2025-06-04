using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Features.Core
{
    public enum AccurateThickness
    {
        [InspectorName("Disable")] [Tooltip("Do not render back-face data.")]
        None = 0,

        [InspectorName("Depth")] [Tooltip("Render back-face depth.")]
        DepthOnly = 1,

        [InspectorName("Depth + Normals")] [Tooltip("Render back-face depth and normals.")]
        DepthNormals = 2
    }

    public class BackfaceDepthPass : ScriptableRenderPass
    {
        const string m_ProfilerTag = "Backface Data";

        public AccurateThickness m_AccurateThickness=AccurateThickness.DepthNormals ;

        private RenderStateBlock m_DepthRenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);

        private static readonly int _CameraBackDepthTexture = Shader.PropertyToID("_CameraBackDepthTexture");
        private static readonly int _CameraBackNormalsTexture = Shader.PropertyToID("_CameraBackNormalsTexture");

        private class PassData
        {
            internal RendererListHandle rendererListHandle;
        }

        public BackfaceDepthPass()
        {
            
            profilingSampler = new ProfilingSampler(m_ProfilerTag);
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques - 1;

        }

        static void ExecutePass(PassData data, RasterGraphContext context)
        {
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

                var depthDesc = cameraData.cameraTargetDescriptor;
                depthDesc.msaaSamples = 1;

                // Render backface depth
                if (m_AccurateThickness == AccurateThickness.DepthOnly)
                {
                    TextureHandle backDepthHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, depthDesc,
                        name: "_CameraBackDepthTexture", true, FilterMode.Point, TextureWrapMode.Clamp);

                    RendererListDesc rendererListDesc = new RendererListDesc(new ShaderTagId("DepthOnly"),
                        universalRenderingData.cullResults, cameraData.camera);
                    m_DepthRenderStateBlock.depthState = new DepthState(true, CompareFunction.LessEqual);
                    m_DepthRenderStateBlock.mask |= RenderStateMask.Depth;
                    m_DepthRenderStateBlock.rasterState = new RasterState(CullMode.Front);
                    m_DepthRenderStateBlock.mask |= RenderStateMask.Raster;
                    rendererListDesc.stateBlock = m_DepthRenderStateBlock;
                    rendererListDesc.sortingCriteria = cameraData.defaultOpaqueSortFlags;
                    rendererListDesc.renderQueueRange = RenderQueueRange.all;

                    passData.rendererListHandle = renderGraph.CreateRendererList(rendererListDesc);

                    // We declare the RendererList we just created as an input dependency to this pass, via UseRendererList()
                    builder.UseRendererList(passData.rendererListHandle);

                    builder.SetRenderAttachmentDepth(backDepthHandle);

                    builder.SetGlobalTextureAfterPass(backDepthHandle, _CameraBackDepthTexture);

                    // We disable culling for this pass for the demonstrative purpose of this sample, as normally this pass would be culled,
                    // since the destination texture is not used anywhere else
                    //builder.AllowGlobalStateModification(true);
                    //builder.AllowPassCulling(false);

                    // Assign the ExecutePass function to the render pass delegate, which will be called by the render graph when executing the pass
                    builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));
                }
                // Render backface depth + normals
                else if (m_AccurateThickness == AccurateThickness.DepthNormals)
                {
                    TextureHandle backDepthHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, depthDesc,
                        name: "_CameraBackDepthTexture", true, FilterMode.Point, TextureWrapMode.Clamp);

                    var normalsDesc = cameraData.cameraTargetDescriptor;
                    normalsDesc.msaaSamples = 1;
                    // normal normal normal packedSmoothness
                    // NormalWS range is -1.0 to 1.0, so we need a signed render texture.
                    normalsDesc.depthStencilFormat = GraphicsFormat.None;
#if UNITY_2023_2_OR_NEWER
                    if (SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_SNorm, GraphicsFormatUsage.Render))
#else
                    if (SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_SNorm, FormatUsage.Render))
#endif
                        normalsDesc.graphicsFormat = GraphicsFormat.R8G8B8A8_SNorm;
                    else
                        normalsDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;

                    TextureHandle backNormalsHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph,
                        normalsDesc, name: "_CameraBackNormalsTexture", true, FilterMode.Point, TextureWrapMode.Clamp);

                    RendererListDesc rendererListDesc = new RendererListDesc(new ShaderTagId("DepthNormals"),
                        universalRenderingData.cullResults, cameraData.camera);
                    m_DepthRenderStateBlock.depthState = new DepthState(true, CompareFunction.LessEqual);
                    m_DepthRenderStateBlock.mask |= RenderStateMask.Depth;
                    m_DepthRenderStateBlock.rasterState = new RasterState(CullMode.Front);
                    m_DepthRenderStateBlock.mask |= RenderStateMask.Raster;
                    rendererListDesc.stateBlock = m_DepthRenderStateBlock;
                    rendererListDesc.sortingCriteria = cameraData.defaultOpaqueSortFlags;
                    rendererListDesc.renderQueueRange = RenderQueueRange.all;

                    passData.rendererListHandle = renderGraph.CreateRendererList(rendererListDesc);

                    // We declare the RendererList we just created as an input dependency to this pass, via UseRendererList()
                    builder.UseRendererList(passData.rendererListHandle);

                    builder.SetRenderAttachment(backNormalsHandle, 0);
                    builder.SetRenderAttachmentDepth(backDepthHandle);

                    builder.SetGlobalTextureAfterPass(backNormalsHandle, _CameraBackNormalsTexture);
                    builder.SetGlobalTextureAfterPass(backDepthHandle, _CameraBackDepthTexture);

                    // We disable culling for this pass for the demonstrative purpose of this sample, as normally this pass would be culled,
                    // since the destination texture is not used anywhere else
                    //builder.AllowGlobalStateModification(true);
                    //builder.AllowPassCulling(false);

                    // Assign the ExecutePass function to the render pass delegate, which will be called by the render graph when executing the pass
                    builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));
                }
            }
        }
    }
}
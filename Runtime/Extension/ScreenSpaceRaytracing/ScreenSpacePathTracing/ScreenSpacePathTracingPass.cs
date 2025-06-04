using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class ScreenSpacePathTracingPass : ScriptableRenderPass
    {
        const string m_ProfilerTag = "Screen Space Path Tracing";

        private Material m_PathTracingMaterial;
        private RTHandle sourceHandle;

        // Time
        private int frameCount = 0;

        private static readonly int _CameraDepthAttachment = Shader.PropertyToID("_CameraDepthAttachment");
        private static readonly int _CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int _PathTracingEmissionTexture = Shader.PropertyToID("_PathTracingEmissionTexture");
        private static readonly int _GBuffer0 = Shader.PropertyToID("_GBuffer0");
        private static readonly int _GBuffer1 = Shader.PropertyToID("_GBuffer1");
        private static readonly int _GBuffer2 = Shader.PropertyToID("_GBuffer2");
        private static readonly int _FrameIndex = Shader.PropertyToID("_FrameIndex");

        public ScreenSpacePathTracingPass(Material material)
        {
            m_PathTracingMaterial = material;
            profilingSampler = new ProfilingSampler(m_ProfilerTag);
        }

        // This class stores the data needed by the pass, passed as parameter to the delegate function that executes the pass
        private class PassData
        {
            internal Material pathTracingMaterial;

            internal TextureHandle cameraColorTargetHandle;
            internal TextureHandle cameraDepthTargetHandle;
            internal TextureHandle cameraDepthTextureHandle;
            internal TextureHandle emissionHandle;

            // GBuffers created by URP
            internal bool localGBuffers;
            internal TextureHandle gBuffer0Handle;
            internal TextureHandle gBuffer1Handle;
            internal TextureHandle gBuffer2Handle;
        }

        // This static method is used to execute the pass and passed as the RenderFunc delegate to the RenderGraph render pass
        static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            if (data.cameraDepthTextureHandle.IsValid())
                data.pathTracingMaterial.SetTexture(_CameraDepthTexture, data.cameraDepthTextureHandle);

            if (data.localGBuffers)
            {
                data.pathTracingMaterial.SetTexture(_GBuffer0, data.gBuffer0Handle);
                data.pathTracingMaterial.SetTexture(_GBuffer1, data.gBuffer1Handle);
                data.pathTracingMaterial.SetTexture(_GBuffer2, data.gBuffer2Handle);
            }
            else
            {
                // Global gbuffer textures
                data.pathTracingMaterial.SetTexture(_GBuffer0, null);
                data.pathTracingMaterial.SetTexture(_GBuffer1, null);
                data.pathTracingMaterial.SetTexture(_GBuffer2, null);
            }

            data.pathTracingMaterial.SetTexture(_CameraDepthAttachment, data.cameraDepthTargetHandle);
            data.pathTracingMaterial.SetTexture(_PathTracingEmissionTexture, data.emissionHandle);

            Blitter.BlitCameraTexture(cmd, data.cameraColorTargetHandle, data.emissionHandle);

            Blitter.BlitCameraTexture(cmd, data.emissionHandle, data.cameraColorTargetHandle,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, data.pathTracingMaterial, pass: 0);
        }

        // This is where the renderGraph handle can be accessed.
        // Each ScriptableRenderPass can use the RenderGraph handle to add multiple render passes to the render graph
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // add an unsafe render pass to the render graph, specifying the name and the data type that will be passed to the ExecutePass function
            using (var builder = renderGraph.AddUnsafePass<PassData>(m_ProfilerTag, out var passData))
            {
                // UniversalResourceData contains all the texture handles used by the renderer, including the active color and depth textures
                // The active color and depth textures are the main color and depth buffers that the camera renders into
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                m_PathTracingMaterial.SetFloat(_FrameIndex, frameCount);
                frameCount += 33;
                frameCount %= 64000;

                RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0; // Color and depth cannot be combined in RTHandles
                desc.stencilFormat = GraphicsFormat.None;
                desc.msaaSamples = 1;

                TextureHandle emissionHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc,
                    name: "_PathTracingEmissionTexture", false, FilterMode.Point, TextureWrapMode.Clamp);
                builder.SetGlobalTextureAfterPass(emissionHandle, _PathTracingEmissionTexture);

                ConfigureInput(ScriptableRenderPassInput.Depth);

                // Fill up the passData with the data needed by the pass
                passData.pathTracingMaterial = m_PathTracingMaterial;
                passData.cameraColorTargetHandle = resourceData.activeColorTexture;
                passData.cameraDepthTargetHandle = resourceData.activeDepthTexture;
                passData.emissionHandle = emissionHandle;

                // UnsafePasses don't setup the outputs using UseTextureFragment/UseTextureFragmentDepth, you should specify your writes with UseTexture instead
                builder.UseTexture(passData.cameraColorTargetHandle, AccessFlags.ReadWrite);
                builder.UseTexture(passData.cameraDepthTargetHandle, AccessFlags.Write);
                builder.UseTexture(passData.emissionHandle, AccessFlags.ReadWrite);

                passData.localGBuffers = resourceData.gBuffer[0].IsValid();

                if (passData.localGBuffers)
                {
                    passData.gBuffer0Handle = resourceData.gBuffer[0];
                    passData.gBuffer1Handle = resourceData.gBuffer[1];
                    passData.gBuffer2Handle = resourceData.gBuffer[2];

                    builder.UseTexture(passData.gBuffer0Handle, AccessFlags.Read);
                    builder.UseTexture(passData.gBuffer1Handle, AccessFlags.Read);
                    builder.UseTexture(passData.gBuffer2Handle, AccessFlags.Read);
                }

                // We disable culling for this pass for the demonstrative purpose of this sample, as normally this pass would be culled,
                // since the destination texture is not used anywhere else
                //builder.AllowGlobalStateModification(true);
                //builder.AllowPassCulling(false);

                // Assign the ExecutePass function to the render pass delegate, which will be called by the render graph when executing the pass
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
            }
        }
        public void Dispose()
        {
            sourceHandle?.Release();
        }

    }
}
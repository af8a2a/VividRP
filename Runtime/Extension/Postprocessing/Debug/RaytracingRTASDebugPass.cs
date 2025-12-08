using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

namespace UnityEngine.Rendering.Universal
{
    public class RaytracingRTASDebugPass : ScriptableRenderPass
    {
        private Material m_Material;

        class RTASDebugPassData
        {
            // Camera data
            public int actualWidth;
            public int actualHeight;

            // Evaluation parameters
            public int debugMode;
            public uint layerMask;
            public Matrix4x4 pixelCoordToViewDirWS;

            // Other parameters
            public RayTracingShader debugRTASRT;
            public RayTracingAccelerationStructure rayTracingAccelerationStructure;

            // Output
            public TextureHandle outputTexture;

//Blit to Backbuffer
            public UniversalCameraData cameraData;
            public TextureHandle source;
            public TextureHandle destination;
        }

        static ProfilingSampler RaytracingBuildAccelerationStructureDebug = new ProfilingSampler("RaytracingBuildAccelerationStructureDebug");


        static uint LayerFromRTASDebugView(RTASDebugView debugView)
        {
            switch (debugView)
            {
                case RTASDebugView.Shadows:
                {
                    return (uint)RayTracingRendererFlag.CastShadow;
                }
                case RTASDebugView.AmbientOcclusion:
                {
                    return (uint)RayTracingRendererFlag.AmbientOcclusion;
                }
                case RTASDebugView.GlobalIllumination:
                {
                    return (uint)RayTracingRendererFlag.GlobalIllumination;
                }
                case RTASDebugView.Reflections:
                {
                    return (uint)RayTracingRendererFlag.Reflection;
                }
                case RTASDebugView.RecursiveRayTracing:
                {
                    return (uint)RayTracingRendererFlag.RecursiveRendering;
                }
                case RTASDebugView.PathTracing:
                {
                    return (uint)RayTracingRendererFlag.PathTracing;
                }
                default:
                {
                    return (uint)RayTracingRendererFlag.All;
                }
            }
        }

        static readonly string _RaytracingAccelerationStructureName = "_RaytracingAccelerationStructure";
        static readonly string _PixelCoordToViewDirWS = "_PixelCoordToViewDirWS";
        static readonly string m_RTASDebugRTKernel = "RTASDebug";


        internal void RenderRTASDebug(RenderGraph renderGraph,
            UniversalCameraData cameraData,
            RTASDebugView rtasDebugView,
            RTASDebugMode rtasDebugMode,
            TextureHandle dstColor
        )
        {
            // If the ray tracing state is not valid, we cannot evaluate the debug view

            if (!cameraData.rayTracingSystem.GetRayTracingState())
                return;

            TextureHandle rtas;
            using (var builder = renderGraph.AddComputePass<RTASDebugPassData>("Debug view of the RTAS", out var passData,
                       RaytracingBuildAccelerationStructureDebug))
            {
                builder.EnableAsyncCompute(false);

                // Camera data
                passData.actualWidth = cameraData.actualWidth;
                passData.actualHeight = cameraData.actualHeight;

                // Evaluation parameters
                passData.debugMode = (int)rtasDebugMode;
                passData.layerMask = LayerFromRTASDebugView(rtasDebugView);

                passData.pixelCoordToViewDirWS = cameraData.GetPixelCoordToViewDirWSMatrix();

                // Other parameters
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<UniversalRenderPipelineDebugShaders>();

                passData.debugRTASRT = runtimeShader.debugRTASRT;
                passData.rayTracingAccelerationStructure = cameraData.rayTracingSystem.RequestAccelerationStructure();


                // Depending of if we will have to denoise (or not), we need to allocate the final format, or a bigger texture
                passData.outputTexture = (renderGraph.CreateTexture(new TextureDesc(cameraData.pixelWidth,cameraData.pixelHeight)
                {
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    enableRandomWrite = true,
                    name = "RTAS Debug",
                    clearBuffer = true,
                }));
                builder.UseTexture(passData.outputTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc<RTASDebugPassData>((data, ctx) =>
                {
                    var cmd = ctx.cmd;

                    // Define the shader pass to use for the reflection pass
                    cmd.SetRayTracingShaderPass(data.debugRTASRT, "DebugDXR");

                    // Set the acceleration structure for the pass
                    cmd.SetRayTracingAccelerationStructure(data.debugRTASRT, _RaytracingAccelerationStructureName,
                        data.rayTracingAccelerationStructure);

                    // Layer mask
                    cmd.SetRayTracingIntParam(data.debugRTASRT, "_DebugMode", data.debugMode);
                    cmd.SetRayTracingIntParam(data.debugRTASRT, "_LayerMask", (int)data.layerMask);
                    cmd.SetRayTracingMatrixParam(data.debugRTASRT, _PixelCoordToViewDirWS, data.pixelCoordToViewDirWS);

                    // Set the output texture
                    cmd.SetRayTracingTextureParam(data.debugRTASRT, "_OutputDebugBuffer", data.outputTexture);

                    // Evaluate the debug view
                    cmd.DispatchRays(data.debugRTASRT, m_RTASDebugRTKernel, (uint)data.actualWidth, (uint)data.actualHeight, 1);

                });
                rtas = passData.outputTexture;
            }
            using (var builder = renderGraph.AddRasterRenderPass<RTASDebugPassData>("Copy RTAS View", out var passData,
                       RaytracingBuildAccelerationStructureDebug))
            {

                passData.source = rtas;
                passData.destination = dstColor;
                passData.cameraData = cameraData;
                builder.SetRenderAttachment(dstColor,0);
                builder.UseTexture(passData.source);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc<RTASDebugPassData>((data, ctx) =>
                {

                    var cmd = ctx.cmd;
                    bool isRenderToBackBufferTarget = !data.cameraData.isSceneViewCamera;
#if ENABLE_VR && ENABLE_XR_MODULE
                    if (data.cameraData.xr.enabled)
                        isRenderToBackBufferTarget = new RenderTargetIdentifier(((RTHandle)data.destination).nameID, 0, CubemapFace.Unknown, -1) ==
                                                     new RenderTargetIdentifier(data.cameraData.xr.renderTarget, 0, CubemapFace.Unknown, -1);
#endif
                    Vector4 scaleBias = RenderingUtils.GetFinalBlitScaleBias(ctx, data.source, data.destination);
                    if (isRenderToBackBufferTarget)
                        cmd.SetViewport(data.cameraData.pixelRect);


                    Blitter.BlitTexture(cmd, data.source, scaleBias, 0, true);
                });
            }

        }
    }
}
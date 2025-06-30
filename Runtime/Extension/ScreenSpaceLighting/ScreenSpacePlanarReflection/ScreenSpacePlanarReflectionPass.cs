using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class ScreenSpacePlanarReflectionPass : ScriptableRenderPass
    {
        static readonly int SHADER_NUMTHREAD_X = 8;
        static readonly int SHADER_NUMTHREAD_Y = 8;

        static readonly int RTSize = Shader.PropertyToID("_RTSize");
        static readonly int cameraDirection = Shader.PropertyToID("_CameraDirection");
        static readonly int cameraUpDirection = Shader.PropertyToID("_CameraUpDirection");
        static readonly int screenLRStretchIntensity = Shader.PropertyToID("_ScreenLRStretchIntensity");
        static readonly int screenLRStretchThreshold = Shader.PropertyToID("_ScreenLRStretchThreshold");
        static readonly int finalTintColor = Shader.PropertyToID("_FinalTintColor");
        static readonly int planeID = Shader.PropertyToID("_PlaneID");
        static readonly int _SSPRPlaneIdRT = Shader.PropertyToID("_SSPRPlaneIdRT");
        static readonly int planeCount = Shader.PropertyToID("_PlaneCount");
        static readonly int _PlaneHeights = Shader.PropertyToID("_PlaneHeights");
        static readonly int ColorRT = Shader.PropertyToID("ColorRT");
        static readonly int posWSyRT = Shader.PropertyToID("PosWSyRT");
        static readonly int cameraOpaqueTexture = Shader.PropertyToID("_CameraOpaqueTexture");
        static readonly int HashRT = Shader.PropertyToID("HashRT");
        static readonly int _SSPRTexture = Shader.PropertyToID("_SSPRTexture");


        public bool shouldRemoveFlickerFinalControl = true;
        public bool enablePerPlatformAutoSafeGuard = true;

        Material drawPlaneIdMat = null;

        Vector4[] planeHeights = new Vector4[4];


        static class Profiling
        {
            public static ProfilingSampler SSPR = new ProfilingSampler("Screen Space Planar Reflection");
            public static ProfilingSampler PlaneID = new ProfilingSampler("Draw Plane ID");
        }


        public ScreenSpacePlanarReflectionPass()
        {
            for (int i = 0; i < planeHeights.Length; i++)
            {
                planeHeights[i] = Vector4.zero;
            }

            drawPlaneIdMat = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/DrawSSPRPlaneID"));
        }


        class PassData
        {
            public Material DrawPlaneIdMat;

            public ComputeShader SSPRShader;

            public List<ScreenSpacePlanarReflectionPlane> planes;

            public int NonMobilePathClearKernelID;
            public int NonMobilePathRenderHashRTKernelID;
            public int FillHolesKernelID;
            public int NonMobilePathResolveColorRTKernelID;


            public int width, height;
            public ScreenSpacePlanarReflection Setting;
            public UniversalCameraData CameraData;

            public bool shouldRemoveFlickerFinalControl;
            public bool enablePerPlatformAutoSafeGuard;
            public bool fillHole;

            public Vector4[] planeHeights;

            public TextureHandle CameraColorTexture;
            public TextureHandle ColorRT;
            public TextureHandle ColorTransientRT;

            public TextureHandle PosWSyRT;
            public TextureHandle PackedDataRT;
            public TextureHandle SSPRPlaneIdRT;
        }


        static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            int dispatchThreadGroupXCount = RenderingUtilsExt.DivRoundUp(data.width, 8);
            int dispatchThreadGroupYCount = RenderingUtilsExt.DivRoundUp(data.height, 8);

            var cs = data.SSPRShader;
            using (new ProfilingScope(cmd, Profiling.PlaneID))
            {
                cmd.SetRenderTarget(data.SSPRPlaneIdRT, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare);
                cmd.ClearRenderTarget(false, true, Color.clear);

                //TODO:剔除
                data.planes.Sort((x, y) => x.transform.position.y.CompareTo(y.transform.position.y));
                for (int i = 0; i < data.planes.Count; i++)
                {
                    Renderer renderer = data.planes[i].plandeRenderer;
                    data.planeHeights[i / 4][i % 4] = renderer.transform.position.y + 0.1f;

                    if (renderer && renderer.isVisible && renderer.enabled)
                    {
                        cmd.SetGlobalFloat(planeID, i + 1);
                        cmd.DrawRenderer(renderer, data.DrawPlaneIdMat);
                    }
                }
            }

            using (new ProfilingScope(cmd, Profiling.SSPR))
            {
                cmd.SetComputeIntParam(cs, planeCount, data.planes.Count);
                cmd.SetComputeVectorArrayParam(cs, _PlaneHeights, data.planeHeights);
                //cmd.ResolveAntiAliasedSurface(renderingData.cameraData.renderer.cameraColorTargetHandle);
                cmd.SetComputeVectorParam(cs, RTSize, new Vector2(data.width, data.height));

                cmd.SetComputeVectorParam(cs, cameraDirection, data.CameraData.camera.transform.forward);
                cmd.SetComputeVectorParam(cs, cameraUpDirection, data.CameraData.camera.transform.up);

                cmd.SetComputeFloatParam(cs, screenLRStretchIntensity, data.Setting.screenLRStretchIntensity.value);
                cmd.SetComputeFloatParam(cs, screenLRStretchThreshold, data.Setting.screenLRStretchThreshold.value);
                cmd.SetComputeVectorParam(cs, finalTintColor, data.Setting.tintColor.value);


                ////////////////////////////////////////////////
                //Non-Mobile Path (PC/console)
                ////////////////////////////////////////////////

                cmd.SetComputeTextureParam(cs, data.NonMobilePathClearKernelID, ColorRT, data.ColorRT);
                cmd.SetComputeTextureParam(cs, data.NonMobilePathClearKernelID, HashRT, data.PackedDataRT);
                cmd.DispatchCompute(cs,
                    data.NonMobilePathClearKernelID,
                    dispatchThreadGroupXCount,
                    dispatchThreadGroupYCount,
                    1);

                cmd.SetComputeTextureParam(cs, data.NonMobilePathRenderHashRTKernelID, _SSPRPlaneIdRT, data.SSPRPlaneIdRT);
                cmd.SetComputeTextureParam(cs, data.NonMobilePathRenderHashRTKernelID, HashRT, data.PackedDataRT);
                cmd.DispatchCompute(cs,
                    data.NonMobilePathRenderHashRTKernelID,
                    dispatchThreadGroupXCount,
                    dispatchThreadGroupYCount,
                    1);

                if (data.fillHole)
                {
                    cmd.SetComputeTextureParam(cs, data.FillHolesKernelID, HashRT, data.PackedDataRT);
                    cmd.DispatchCompute(cs,
                        data.FillHolesKernelID,
                        Mathf.CeilToInt(dispatchThreadGroupXCount / 2f),
                        Mathf.CeilToInt(dispatchThreadGroupYCount / 2f),
                        1);
                }

                cmd.SetComputeTextureParam(cs,
                    data.NonMobilePathResolveColorRTKernelID,
                    cameraOpaqueTexture,
                    data.CameraColorTexture);

                cmd.SetComputeTextureParam(cs, data.NonMobilePathResolveColorRTKernelID, ColorRT, data.ColorRT);
                cmd.SetComputeTextureParam(cs, data.NonMobilePathResolveColorRTKernelID, HashRT, data.PackedDataRT);
                cmd.DispatchCompute(cs,
                    data.NonMobilePathResolveColorRTKernelID,
                    dispatchThreadGroupXCount,
                    dispatchThreadGroupYCount,
                    1);

            }

            CoreUtils.SetKeyword(cmd, "_SSPR", true);
        }


        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<ScreenSpacePlanarReflectionRuntimeResource>();


            using (var builder = renderGraph.AddUnsafePass<PassData>("SSPR", out var passData))
            {
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                var cameraData = frameData.Get<UniversalCameraData>();

                var sspr = VolumeManager.instance.stack.GetComponent<ScreenSpacePlanarReflection>();

                var resouceData = frameData.Get<UniversalResourceData>();

                passData.SSPRShader = runtimeShader.SSPRShader;
                passData.DrawPlaneIdMat = drawPlaneIdMat;
                passData.width = cameraData.scaledWidth / 2;
                passData.height = cameraData.scaledHeight / 2;
                passData.planeHeights = planeHeights;
                passData.planes = PlaneManager.instance.Planes;
                passData.CameraData = cameraData;
                passData.Setting = sspr;
                passData.fillHole = sspr.fillHole.value;
                passData.NonMobilePathClearKernelID = passData.SSPRShader.FindKernel("NonMobilePathClear");
                passData.NonMobilePathRenderHashRTKernelID = passData.SSPRShader.FindKernel("NonMobilePathRenderHashRT");
                passData.FillHolesKernelID = passData.SSPRShader.FindKernel("FillHoles");
                passData.NonMobilePathResolveColorRTKernelID = passData.SSPRShader.FindKernel("NonMobilePathResolveColorRT");

                passData.ColorTransientRT = builder.CreateTransientTexture(new TextureDesc(cameraData.scaledWidth / 2, cameraData.scaledHeight / 2)
                {
                    enableRandomWrite = true,
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                });


                passData.PosWSyRT = builder.CreateTransientTexture(new TextureDesc(cameraData.scaledWidth / 2, cameraData.scaledHeight / 2)
                {
                    enableRandomWrite = true,
                    format = GraphicsFormat.R16_SFloat,
                });

                passData.PackedDataRT = builder.CreateTransientTexture(new TextureDesc(cameraData.scaledWidth / 2, cameraData.scaledHeight / 2)
                {
                    enableRandomWrite = true,
                    format = GraphicsFormat.R32_SInt,
                });

                passData.SSPRPlaneIdRT = builder.CreateTransientTexture(new TextureDesc(cameraData.scaledWidth / 8, cameraData.scaledHeight / 8)
                {
                    enableRandomWrite = true,
                    format = GraphicsFormat.R16_SFloat,
                });


                passData.ColorRT = renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth / 2, cameraData.scaledHeight / 2)
                {
                    enableRandomWrite = true,
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                });


                passData.CameraColorTexture = resouceData.activeColorTexture;

                builder.UseTexture(passData.CameraColorTexture, AccessFlags.Read);
                builder.UseTexture(passData.ColorRT, AccessFlags.Write);
                builder.SetRenderFunc<PassData>(ExecutePass);
                builder.SetGlobalTextureAfterPass(passData.ColorRT, _SSPRTexture);
            }
        }


        /// If user enabled PerPlatformAutoSafeGuard, this function will return true if we should use mobile path
        bool ShouldUseSinglePassUnsafeAllowFlickeringDirectResolve()
        {
            if (enablePerPlatformAutoSafeGuard)
            {
                //if RInt RT is not supported, use mobile path
                if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RInt))
                    return true;

                //tested Metal(even on a Mac) can't use InterlockedMin().
                //so if metal, use mobile path
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal)
                    return true;
#if UNITY_EDITOR
                //PC(DirectX) can use RenderTextureFormat.RInt + InterlockedMin() without any problem, use Non-Mobile path.
                //Non-Mobile path will NOT produce any flickering
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11 ||
                    SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12)
                    return false;
#elif UNITY_ANDROID
            //- samsung galaxy A70(Adreno612) will fail if use RenderTextureFormat.RInt + InterlockedMin() in compute shader
            //- but Lenovo S5(Adreno506) is correct, WTF???
            //because behavior is different between android devices, we assume all android are not safe to use RenderTextureFormat.RInt + InterlockedMin() in compute shader
            //so android always go mobile path
            return true;
#endif
            }

            //let user decide if we still don't know the correct answer
            return !shouldRemoveFlickerFinalControl;
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            CoreUtils.SetKeyword(cmd, "_SSPR", false);
        }
    }
}
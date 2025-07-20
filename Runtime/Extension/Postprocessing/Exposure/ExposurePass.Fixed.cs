using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public partial class ExposurePass
    {

        internal static void SetExposureTextureToEmpty(RTHandle exposureTexture)
        {
            var tex = new Texture2D(1, 1, GraphicsFormat.R16G16_SFloat, TextureCreationFlags.None);
            tex.SetPixel(0, 0, new Color(1f, ColorUtils.ConvertExposureToEV100(1f), 0f, 0f));
            tex.Apply();
            Graphics.Blit(tex, exposureTexture);
            CoreUtils.Destroy(tex);
        }

        internal bool AllocateExposureTextures(HistoryFrameRTSystem historyFrameRTSystem)
        {
            if (historyFrameRTSystem.GetCurrentFrameRT(HistoryFrameType.Exposure) == null)
            {
                historyFrameRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.Exposure);

                RTHandle Allocator(string id, int frameIndex, RTHandleSystem rtHandleSystem)
                {
                    // r: multiplier, g: EV100
                    var rt = rtHandleSystem.Alloc(1, 1, colorFormat: GraphicsFormat.R32G32_SFloat,
                        enableRandomWrite: true, name: $"{id} Exposure Texture {frameIndex}"
                    );
                    SetExposureTextureToEmpty(rt);
                    return rt;
                }

                historyFrameRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.Exposure, Allocator, 2);

                return true;
            }

            return false;
        }


        internal bool ReAllocatedExposureTexturesIfNeeded(HistoryFrameRTSystem historyRTSystem, out RTHandle currFrameRT,
            out RTHandle prevFrameRT)
        {
            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.Exposure);
            bool vaild = true;

            if (curTexture == null)
            {
                vaild = false;

                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.Exposure);

                AllocateExposureTextures(historyRTSystem);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.Exposure);
            prevFrameRT = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.Exposure);
            return vaild;
        }

        
        class FixedExposurePassData
        {
            public ComputeShader shader;
            public int kernelID;
            
            public TextureHandle CurrentExposureTextures;
            public TextureHandle PreviousExposureTexture;
        }

        void DoFixedExposure(RenderGraph renderGraph, ContextContainer frameData)
        {
            var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<ExposureRuntimeShader>();
            ComputeShader cs = runtimeShader.exposureCS;
            var setting = VolumeManager.instance.stack.GetComponent<ExposureSetting>();
            int kernel = 0;
            Vector4 exposureParams;
            Vector4 exposureParams2 = new Vector4(0.0f, 0.0f, ColorUtils.lensImperfectionExposureScale, ColorUtils.s_LightMeterCalibrationConstant);

            var cameraData = frameData.Get<UniversalCameraData>();
            if (setting.mode.value == ExposureMode.Fixed
// #if UNITY_EDITOR
//                 || HDAdditionalSceneViewSettings.sceneExposureOverriden && cameraData.camera.cameraType == CameraType.SceneView
// #endif
               )
            {
                kernel = cs.FindKernel("KFixedExposure");
                exposureParams = new Vector4(setting.compensation.value, setting.fixedExposure.value, 0f, 0f);

// #if UNITY_EDITOR
//                 if (cameraData.camera.cameraType == CameraType.SceneView)
//                 {
//                     exposureParams = new Vector4(0.0f, HDAdditionalSceneViewSettings.sceneExposure, 0f, 0f);
//                 }
// #endif
            }
            else // ExposureMode.UsePhysicalCamera
            {
                kernel = cs.FindKernel("KManualCameraExposure");
                exposureParams = new Vector4(setting.compensation.value, cameraData.camera.aperture, cameraData.camera.shutterSpeed, cameraData.camera.iso);
            }

            using (var builder = renderGraph.AddComputePass<FixedExposurePassData>("Fixed Exposure", out var data))
            {
                var exposureResoureData = frameData.Get<UniversalResourceData>();

                var historyRTSystem = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);
                ReAllocatedExposureTexturesIfNeeded(historyRTSystem, out var prev, out var curr);

                data.CurrentExposureTextures = renderGraph.ImportTexture(curr);
                data.PreviousExposureTexture = renderGraph.ImportTexture(prev);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.UseTexture(data.PreviousExposureTexture);
                builder.UseTexture(data.CurrentExposureTextures,AccessFlags.Write);

                data.shader = cs;
                data.kernelID = kernel;
                builder.SetRenderFunc<FixedExposurePassData>((passData, context) =>
                {
                    var cmd = context.cmd;

                    cmd.SetComputeVectorParam(passData.shader, ShaderIDs._ExposureParams, exposureParams);
                    cmd.SetComputeVectorParam(passData.shader, ShaderIDs._ExposureParams2, exposureParams2);

                    cmd.SetComputeTextureParam(passData.shader, passData.kernelID, ShaderIDs._OutputTexture, passData.CurrentExposureTextures);
                    cmd.DispatchCompute(passData.shader, passData.kernelID, 1, 1, 1);

                    cmd.SetGlobalTexture(ShaderIDs._ExposureTexture, passData.CurrentExposureTextures);
                    cmd.SetGlobalTexture(ShaderIDs._PrevExposureTexture, passData.PreviousExposureTexture);
                });

                exposureResoureData.currentExposure = data.CurrentExposureTextures;
                exposureResoureData.previousExposure = data.PreviousExposureTexture;
            }
        }
    }
}
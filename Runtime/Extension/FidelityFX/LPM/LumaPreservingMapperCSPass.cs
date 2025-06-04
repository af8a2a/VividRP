using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class LumaPreservingMapperCSPass : ScriptableRenderPass
    {
        private ComputeShader _computeShader;
        private int threadGroupWorkRegionDim = 16;
        private int kernelID;
        // private GraphicsBuffer buffer;


        public LumaPreservingMapperCSPass()
        {
            // BufferDesc desc = new BufferDesc(32, sizeof(UInt32));
            // buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 32, sizeof(UInt32));
        }

        public void Setup(ComputeShader computeShader)
        {
            requiresIntermediateTexture = true;
            _computeShader = computeShader;
            kernelID = computeShader.FindKernel("LPM_CS");
        }

        internal class PassData
        {
            internal ComputeShader ComputeShader;
            internal int KernelID;

            internal Vector2 displayMinMaxLuminance;

            internal Vector2 dispatchXY;

            // internal bool Fp16Packed = false;
            // internal bool WaveFrontOperation = false;
            internal LumaPreservingMapper PreservingMapperSetting;
            internal TextureHandle InputTexture;
            internal TextureHandle OutputTexture;
        }

        int FFX_DIVIDE_ROUNDING_UP(int x, int y)
        {
            return (x + y - 1) / y;
        }


        static void ExecutePass(PassData data, ComputeGraphContext computeGraphContext)
        {
            var cmd = computeGraphContext.cmd;
            var setting = data.PreservingMapperSetting;

            foreach (var keyword in data.ComputeShader.shaderKeywords)
            {
                CoreUtils.SetKeyword(data.ComputeShader, keyword, false);
            }

            switch (setting.colorSpace.value)
            {
                case FfxLpmColorSpace.FfxLpmColorSpaceRec709:
                    CoreUtils.SetKeyword(data.ComputeShader, "FFX_LPM_ColorSpace_REC709", true);
                    break;
                case FfxLpmColorSpace.FfxLpmColorSpaceP3:
                    CoreUtils.SetKeyword(data.ComputeShader, "FFX_LPM_ColorSpace_P3", true);

                    break;
                case FfxLpmColorSpace.FfxLpmColorSpaceRec2020:
                    CoreUtils.SetKeyword(data.ComputeShader, "FFX_LPM_ColorSpace_REC2020", true);

                    break;
                case FfxLpmColorSpace.FfxLpmColorSapceDisplay:
                    CoreUtils.SetKeyword(data.ComputeShader, "FFX_LPM_ColorSpace_REC709", true);

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            switch (setting.displayMode.value)
            {
                case DisplayMode.FfxLpmDisplaymodeLDR:
                    CoreUtils.SetKeyword(data.ComputeShader, "FfxLpmDisplaymodeLDR", true);
                    cmd.SetComputeIntParam(data.ComputeShader, "displayMode", 0);
                    break;
                case DisplayMode.FfxLpmDisplaymodeHDR102084:
                    CoreUtils.SetKeyword(data.ComputeShader, "FfxLpmDisplaymodeHDR102084", true);
                    break;
                case DisplayMode.FfxLpmDisplaymodeHDR10Scrgb:
                    CoreUtils.SetKeyword(data.ComputeShader, "FfxLpmDisplaymodeHDR10Scrgb", true);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            cmd.SetComputeIntParam(data.ComputeShader, "displayMode",
                (int)data.PreservingMapperSetting.displayMode.value);

            cmd.SetComputeIntParam(data.ComputeShader, "shoulder", setting.shoulder.value ? 1 : 0);
            
            cmd.SetComputeFloatParam(data.ComputeShader, "softGap", setting.SoftGap.value);
            cmd.SetComputeFloatParam(data.ComputeShader, "lpmExposure", setting.LPMExposure.value);
            cmd.SetComputeFloatParam(data.ComputeShader, "hdrMax", setting.HdrMax.value);
            cmd.SetComputeFloatParam(data.ComputeShader, "contrast", setting.Contrast.value);
            cmd.SetComputeFloatParam(data.ComputeShader, "shoulderContrast", setting.ShoulderContrast.value);


            cmd.SetComputeVectorParam(data.ComputeShader, "saturation", setting.Saturation.value);
            cmd.SetComputeVectorParam(data.ComputeShader, "crosstalk", setting.Crosstalk.value);
            cmd.SetComputeVectorParam(data.ComputeShader, "displayMinMaxLuminance", data.displayMinMaxLuminance);

            cmd.SetComputeTextureParam(data.ComputeShader, data.KernelID, "r_input_color", data.InputTexture);
            cmd.SetComputeTextureParam(data.ComputeShader, data.KernelID, "rw_output_color", data.OutputTexture);

            cmd.DispatchCompute(data.ComputeShader, data.KernelID, (int)data.dispatchXY.x, (int)data.dispatchXY.y, 1);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var cameraColor = resourceData.activeColorTexture;
            var cameraColorDesc = renderGraph.GetTextureDesc(cameraColor);
            cameraColorDesc.enableRandomWrite = true;
            cameraColorDesc.name = "LPMColor";
            var destRT = renderGraph.CreateTexture(cameraColorDesc);
            // var buffer = renderGraph.CreateBuffer(bufferDesc);

            int dispatchX = FFX_DIVIDE_ROUNDING_UP(cameraColorDesc.width, threadGroupWorkRegionDim);
            int dispatchY = FFX_DIVIDE_ROUNDING_UP(cameraColorDesc.height, threadGroupWorkRegionDim);

            var volume = VolumeManager.instance.stack.GetComponent<LumaPreservingMapper>();
            if (volume == null || !volume.IsActive())
            {
                return;
            }

            HDROutputSettings mainDisplayHdrSettings = HDROutputSettings.main;


            var displayMinMaxLuminance = new Vector2(0,100);
            
            if (mainDisplayHdrSettings.active)
            {
                
                displayMinMaxLuminance.x = cameraData.hdrDisplayInformation.minToneMapLuminance;
                displayMinMaxLuminance.y = cameraData.hdrDisplayInformation.maxToneMapLuminance;
            }

            
            using (var builder = renderGraph.AddComputePass<PassData>("LumaPreservingMapperCSPass", out var passData))
            {
                passData.InputTexture = resourceData.activeColorTexture;
                passData.OutputTexture = destRT;
                passData.dispatchXY = new Vector2(dispatchX, dispatchY);
                passData.PreservingMapperSetting = volume;
                passData.displayMinMaxLuminance = displayMinMaxLuminance;
                passData.KernelID = kernelID;
                passData.ComputeShader = _computeShader;
                builder.UseTexture(passData.InputTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.OutputTexture, AccessFlags.ReadWrite);
                builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => ExecutePass(data, cgContext));
            }

            resourceData.cameraColor = destRT;
        }
    }
}
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public partial class MipGenerator
    {
        private ComputeShader GPUCopyColor;
        private int GPUCopyColorKernelID;

        private ComputeShader spdCompatibleCS;
        private ComputeShader spdCS;

        private int spdKernelID;

        internal class MipGeneratePassData
        {
            internal ComputeShader spdCS;
            internal int spdKernelID;

            internal ComputeShader GPUCopyColor;
            internal int GPUCopyColorKernelID;
            internal int2 CopyDimension;

            internal BufferHandle atomicBuffer;

            internal TextureHandle inputTexture;
            internal TextureHandle outputTexture;

            internal SPDConstants spdConstants;
            internal int2 dispatchThreadGroupCountXY;

            internal bool fp16;
            internal bool wavefront;
            internal bool compatible;
        }

        internal unsafe struct SPDConstants
        {
            internal int mips;
            internal int numWorkGroups;
            internal int2 workGroupOffset;
        }

        static class SPDShaderID
        {
            public static readonly int rw_spd_mip0 = Shader.PropertyToID("rw_spd_mip0");
            public static readonly int rw_spd_mip1 = Shader.PropertyToID("rw_spd_mip1");
            public static readonly int rw_spd_mip2 = Shader.PropertyToID("rw_spd_mip2");
            public static readonly int rw_spd_mip3 = Shader.PropertyToID("rw_spd_mip3");
            public static readonly int rw_spd_mip4 = Shader.PropertyToID("rw_spd_mip4");
            public static readonly int rw_spd_mip5 = Shader.PropertyToID("rw_spd_mip5");
            public static readonly int rw_spd_mip6 = Shader.PropertyToID("rw_spd_mip6");
            public static readonly int rw_spd_mip7 = Shader.PropertyToID("rw_spd_mip7");
            public static readonly int rw_spd_mip8 = Shader.PropertyToID("rw_spd_mip8");
            public static readonly int rw_spd_mip9 = Shader.PropertyToID("rw_spd_mip9");
            public static readonly int rw_spd_mip10 = Shader.PropertyToID("rw_spd_mip10");
            public static readonly int rw_spd_mip11 = Shader.PropertyToID("rw_spd_mip11");
            public static readonly int rw_spd_mip12 = Shader.PropertyToID("rw_spd_mip12");
            public static readonly int spdGlobalAtomic = Shader.PropertyToID("spdGlobalAtomic");
        }

        private static readonly int[] MipBindArray =
        {
            SPDShaderID.rw_spd_mip0,
            SPDShaderID.rw_spd_mip1,
            SPDShaderID.rw_spd_mip2,
            SPDShaderID.rw_spd_mip3,
            SPDShaderID.rw_spd_mip4,
            SPDShaderID.rw_spd_mip5,
            SPDShaderID.rw_spd_mip6,
            SPDShaderID.rw_spd_mip7,
            SPDShaderID.rw_spd_mip8,
            SPDShaderID.rw_spd_mip9,
            SPDShaderID.rw_spd_mip10,
            SPDShaderID.rw_spd_mip11,
            SPDShaderID.rw_spd_mip12,
        };

        static void ExecuteSPD(MipGeneratePassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;

            ConstantBuffer<SPDConstants>.Push(cmd, data.spdConstants, data.spdCS, Shader.PropertyToID("spdConstants"));

            {
                cmd.SetComputeTextureParam(data.GPUCopyColor, data.GPUCopyColorKernelID, "_Input", data.inputTexture);
                cmd.SetComputeTextureParam(data.GPUCopyColor, data.GPUCopyColorKernelID, "_Output", data.outputTexture);
                var dispatchX = RenderingUtilsExt.DivRoundUp(data.CopyDimension.x, 8);
                var dispatchY = RenderingUtilsExt.DivRoundUp(data.CopyDimension.y, 8);

                cmd.DispatchCompute(data.GPUCopyColor, data.GPUCopyColorKernelID, dispatchX, dispatchY, 1);
            }


            cmd.SetComputeBufferParam(data.spdCS, data.spdKernelID, SPDShaderID.spdGlobalAtomic, data.atomicBuffer);

            var maxMipLevel = math.max(SystemInfo.supportedRandomWriteTargetCount - 1, data.spdConstants.mips); //need atomic uav

            data.spdCS.shaderKeywords = null;
            CoreUtils.SetKeyword(data.spdCS, "COMPATIBLE", data.compatible);
            CoreUtils.SetKeyword(data.spdCS, "FP16", data.fp16);
            CoreUtils.SetKeyword(data.spdCS, "WAVEFRONT", data.wavefront);


            //note:
            //d3d11 only support 8 uav.....
            for (int i = 0; i <= maxMipLevel; i++)
            {
                cmd.SetComputeTextureParam(data.spdCS, data.spdKernelID, MipBindArray[i], data.outputTexture, i);
            }

            //fill up texture descriptor

            for (int i = maxMipLevel + 1; i <= 12; i++)
            {
                cmd.SetComputeTextureParam(data.spdCS, data.spdKernelID, MipBindArray[i], data.outputTexture);
            }

            cmd.DispatchCompute(data.spdCS, data.spdKernelID, data.dispatchThreadGroupCountXY.x, data.dispatchThreadGroupCountXY.y, 1);
        }

        static void SpdSetup(
            out int2 dispatchThreadGroupCountXY, // CPU side: dispatch thread group count xy
            out int2 workGroupOffset, // GPU side: pass in as constant
            out int2 numWorkGroupsAndMips, // GPU side: pass in as constant
            int4 rectInfo, // left, top, width, height
            int mips // optional: if -1, calculate based on rect width and height
        )
        {
            workGroupOffset = new int2(0, 0);
            dispatchThreadGroupCountXY = new int2(0, 0);
            numWorkGroupsAndMips = new int2(0, 0);
            workGroupOffset[0] = rectInfo[0] / 64; // rectInfo[0] = left
            workGroupOffset[1] = rectInfo[1] / 64; // rectInfo[1] = top

            int endIndexX = (rectInfo[0] + rectInfo[2] - 1) / 64; // rectInfo[0] = left, rectInfo[2] = width
            int endIndexY = (rectInfo[1] + rectInfo[3] - 1) / 64; // rectInfo[1] = top, rectInfo[3] = height

            dispatchThreadGroupCountXY[0] = endIndexX + 1 - workGroupOffset[0];
            dispatchThreadGroupCountXY[1] = endIndexY + 1 - workGroupOffset[1];

            numWorkGroupsAndMips[0] = (dispatchThreadGroupCountXY[0]) * (dispatchThreadGroupCountXY[1]);

            if (mips >= 0)
            {
                numWorkGroupsAndMips[1] = mips;
            }
            else
            {
                // calculate based on rect width and height
                numWorkGroupsAndMips[1] = math.min(RenderingUtilsExt.CalcMipCount(new Vector2Int(rectInfo[2], rectInfo[3])), 12);
            }
        }


        public TextureHandle SPDGenerateMip(RenderGraph renderGraph, TextureHandle inputTexture, int mipLevel = -1, bool fp16 = false, bool waveFront = false)
        {
            using (var builder = renderGraph.AddComputePass<MipGeneratePassData>("FidelityFX SPD Mip Generator", out var passData))
            {
                builder.AllowPassCulling(false);

                bool compatible = true;
                switch (SystemInfo.graphicsDeviceType)
                {
                    case GraphicsDeviceType.Metal:
                        compatible = false;
                        break;
                    case GraphicsDeviceType.Direct3D12:
                        compatible = false;
                        break;
                    case GraphicsDeviceType.Vulkan:
                        compatible = false;
                        break;
                    default:
                        break;
                }


                passData.spdCS = !compatible || (fp16 && waveFront) ? spdCS : spdCompatibleCS;
                passData.spdKernelID = spdKernelID;
                passData.GPUCopyColor = GPUCopyColor;
                passData.GPUCopyColorKernelID = GPUCopyColorKernelID;


                passData.inputTexture = inputTexture;


                var desc = renderGraph.GetTextureDesc(inputTexture);

                passData.outputTexture = renderGraph.CreateTexture(new TextureDesc(desc)
                {
                    name = "SPD Texture",
                    enableRandomWrite = true,
                    useMipMap = true,
                });
                SpdSetup(out var dispatchThreadGroupCountXY, out var workGroupOffset, out var numWorkGroupsAndMips,
                    new int4(0, 0, desc.width, desc.height), mipLevel);
                var spdConst = new SPDConstants
                {
                    numWorkGroups = numWorkGroupsAndMips.x,
                    mips = numWorkGroupsAndMips.y,
                    workGroupOffset = workGroupOffset
                };
                var counterDesc = new BufferDesc(6, sizeof(uint), GraphicsBuffer.Target.Structured);
                var conuterBuffer = GraphicsBufferSystem.instance.GetGraphicsBuffer(GraphicsBufferSystemBufferID.SPD, counterDesc);
                conuterBuffer.SetData(new uint[] { 0, 0, 0, 0, 0, 0 });
                var counter = renderGraph
                    .ImportBuffer(conuterBuffer); //builder.CreateTransientBuffer(new BufferDesc(6, sizeof(uint), GraphicsBuffer.Target.Structured));

                passData.CopyDimension = new int2(desc.width, desc.height);
                passData.atomicBuffer = counter;
                passData.spdConstants = spdConst;
                passData.dispatchThreadGroupCountXY = dispatchThreadGroupCountXY;

                passData.fp16 = fp16;
                passData.wavefront = waveFront;
                passData.compatible = compatible;

                builder.UseTexture(passData.inputTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.outputTexture, AccessFlags.ReadWrite);
                builder.UseBuffer(passData.atomicBuffer, AccessFlags.ReadWrite);

                builder.SetRenderFunc((MipGeneratePassData data, ComputeGraphContext context) => ExecuteSPD(data, context));
            }

            return inputTexture;
        }
    }
}
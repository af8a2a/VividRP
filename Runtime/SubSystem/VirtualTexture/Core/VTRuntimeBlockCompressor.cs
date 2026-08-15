using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal static class VTRuntimeBlockCompressor
    {
        private static readonly int s_SourcePagesId = Shader.PropertyToID("_SourcePages");
        private static readonly int s_OutputBC128Id = Shader.PropertyToID("_OutputBC128");
        private static readonly int s_OutputBC64Id = Shader.PropertyToID("_OutputBC64");
        private static readonly int s_ParamsId = Shader.PropertyToID("_VTBlockCompressParams");

        private static ComputeShader s_Shader;
        private static int s_BC7Kernel = -1;
        private static int s_BC5Kernel = -1;
        private static int s_BC4Kernel = -1;

        internal static bool IsAvailable(out string unavailableReason)
        {
            ComputeShader shader = PipelineResourceManager
                .Get<VividRPCoreResources>()
                ?.VirtualTextureBlockCompressCompute;
            if (shader == null)
            {
                unavailableReason = "VT GPU block compressor resource is missing.";
                return false;
            }

            if (!shader.HasKernel("EncodeBC7")
                || !shader.HasKernel("EncodeBC5")
                || !shader.HasKernel("EncodeBC4"))
            {
                unavailableReason = "VT GPU block compressor is missing a BC7, BC5, or BC4 kernel.";
                return false;
            }

            unavailableReason = string.Empty;
            return true;
        }

        internal static void RecordCompression(
            CommandBuffer cmd,
            Texture sourceTexture,
            int sourceSlice,
            RenderTexture outputTexture,
            int outputSlice,
            GraphicsFormat destinationFormat,
            int sourceDimension)
        {
            if (cmd == null)
                throw new ArgumentNullException(nameof(cmd));
            if (sourceTexture == null)
                throw new ArgumentNullException(nameof(sourceTexture));
            if (outputTexture == null)
                throw new ArgumentNullException(nameof(outputTexture));
            if (sourceDimension <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceDimension));

            ResolveShader(out ComputeShader shader, out int bc7Kernel, out int bc5Kernel, out int bc4Kernel);
            int kernel;
            int outputId;
            switch (destinationFormat)
            {
                case GraphicsFormat.RGBA_BC7_UNorm:
                case GraphicsFormat.RGBA_BC7_SRGB:
                    kernel = bc7Kernel;
                    outputId = s_OutputBC128Id;
                    break;
                case GraphicsFormat.RG_BC5_UNorm:
                    kernel = bc5Kernel;
                    outputId = s_OutputBC128Id;
                    break;
                case GraphicsFormat.R_BC4_UNorm:
                    kernel = bc4Kernel;
                    outputId = s_OutputBC64Id;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"[VividRP] GPU block compression does not support {destinationFormat}.");
            }

            cmd.SetComputeTextureParam(shader, kernel, s_SourcePagesId, sourceTexture);
            cmd.SetComputeTextureParam(shader, kernel, outputId, outputTexture);
            cmd.SetComputeIntParams(
                shader,
                s_ParamsId,
                new[] { sourceSlice, outputSlice, sourceDimension, sourceDimension });
            int blockDimension = Mathf.CeilToInt(sourceDimension / 4.0f);
            int groupCount = Mathf.CeilToInt(blockDimension / 8.0f);
            cmd.DispatchCompute(shader, kernel, groupCount, groupCount, 1);
        }

        private static void ResolveShader(
            out ComputeShader shader,
            out int bc7Kernel,
            out int bc5Kernel,
            out int bc4Kernel)
        {
            shader = PipelineResourceManager
                .Get<VividRPCoreResources>()
                ?.VirtualTextureBlockCompressCompute;
            if (shader == null)
                throw new InvalidOperationException("[VividRP] VT GPU block compressor resource is missing.");

            if (!ReferenceEquals(s_Shader, shader))
            {
                if (!shader.HasKernel("EncodeBC7")
                    || !shader.HasKernel("EncodeBC5")
                    || !shader.HasKernel("EncodeBC4"))
                {
                    throw new InvalidOperationException(
                        "[VividRP] VT GPU block compressor is missing a BC7, BC5, or BC4 kernel.");
                }

                s_Shader = shader;
                s_BC7Kernel = shader.FindKernel("EncodeBC7");
                s_BC5Kernel = shader.FindKernel("EncodeBC5");
                s_BC4Kernel = shader.FindKernel("EncodeBC4");
            }

            bc7Kernel = s_BC7Kernel;
            bc5Kernel = s_BC5Kernel;
            bc4Kernel = s_BC4Kernel;
        }
    }
}

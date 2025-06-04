using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class InterleavedTextureGenerator
    {
        private ComputeShader InterleavedTextureCS;
        private ComputeShader DeinterleavedTextureCS;

        private static readonly Lazy<InterleavedTextureGenerator> _instance = new();
        public static InterleavedTextureGenerator Instance => _instance.Value;

        private static int _interleavedInputTextureID = Shader.PropertyToID("Input");
        private static int _interleavedResultTextureID = Shader.PropertyToID("Result");


        private static int _DeinterleavedInputTextureID = Shader.PropertyToID("InterleavedTexture");
        private static int _DeinterleavedResultTextureID = Shader.PropertyToID("Result");

        private int PrepareinterleavedkernelID;
        private int DeinterleavedkernelID;

        public InterleavedTextureGenerator()
        {
            InterleavedTextureCS = Resources.Load<ComputeShader>("InterleavedTexture");
            DeinterleavedTextureCS = Resources.Load<ComputeShader>("DeinterleavedTexture");
            PrepareinterleavedkernelID = InterleavedTextureCS.FindKernel("PrepareInterleavedTexture");
            DeinterleavedkernelID = DeinterleavedTextureCS.FindKernel("DeinterleavedTextureSample");
        }


        public void RenderInterleavedTexture(ComputeCommandBuffer cmd, TextureHandle textureInput,
            TextureHandle resultTexture, int DispathX, int DispathY)
        {
            cmd.SetComputeTextureParam(InterleavedTextureCS, PrepareinterleavedkernelID, _interleavedInputTextureID,
                textureInput);
            cmd.SetComputeTextureParam(InterleavedTextureCS, PrepareinterleavedkernelID, _interleavedResultTextureID,
                resultTexture);

            cmd.DispatchCompute(InterleavedTextureCS, PrepareinterleavedkernelID, DispathX, DispathY, 1);
        }

        public void SampleInterleavedTexture(ComputeCommandBuffer cmd, TextureHandle textureInput,
            TextureHandle resultTexture, int DispathX, int DispathY)
        {
            cmd.SetComputeTextureParam(DeinterleavedTextureCS, DeinterleavedkernelID, _DeinterleavedInputTextureID,
                textureInput);
            cmd.SetComputeTextureParam(DeinterleavedTextureCS, DeinterleavedkernelID, _DeinterleavedResultTextureID,
                resultTexture);

            cmd.DispatchCompute(DeinterleavedTextureCS, PrepareinterleavedkernelID, DispathX, DispathY, 1);
        }
    }
}
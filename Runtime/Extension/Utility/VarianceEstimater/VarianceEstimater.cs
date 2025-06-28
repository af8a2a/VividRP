using System;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class VarianceEstimater
    {
        private static readonly Lazy<VarianceEstimater> _instance = new Lazy<VarianceEstimater>(() => new VarianceEstimater());

        public static VarianceEstimater instance => _instance.Value;

        private ComputeShader m_VarianceEstimaterCS;
        private int ComputeMeanKernelID;
        private int ComputeDistanceKernelID;
        private int ComputeDeviationKernelID;

        public VarianceEstimater()
        {
            var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<VarianceEstimaterRuntimeShader>();

            m_VarianceEstimaterCS = runtimeShader.varianceEstimaterCS;

            ComputeMeanKernelID = m_VarianceEstimaterCS.FindKernel("ComputeMean");
            ComputeDistanceKernelID = m_VarianceEstimaterCS.FindKernel("ComputeDistance");
            ComputeDeviationKernelID = m_VarianceEstimaterCS.FindKernel("ComputeDeviation");
        }


        private static int g_MeanBuffer = Shader.PropertyToID("g_MeanBuffer");
        private static int g_SquareBuffer = Shader.PropertyToID("g_SquareBuffer");
        private static int g_ResultBuffer = Shader.PropertyToID("g_ResultBuffer");
        private static int g_ColorBuffer = Shader.PropertyToID("g_ColorBuffer");
        private static int g_BufferDimensions = Shader.PropertyToID("g_BufferDimensions");


        public class VarianceEstimaterParameter
        {
            //input
            public TextureHandle colorBuffer;
            public int width, height;

            //inout`
            public BufferHandle meanBuffer;
            public BufferHandle squareBuffer;
            public BufferHandle resultBuffer;
        }


        public void Estimate(ComputeCommandBuffer cmd, VarianceEstimaterParameter varianceEstimateParameter)
        {
            var tx = RenderingUtilsExt.DivRoundUp(varianceEstimateParameter.width, 16);
            var ty = RenderingUtilsExt.DivRoundUp(varianceEstimateParameter.height, 16);
            var cs = m_VarianceEstimaterCS;
            var kernel = ComputeMeanKernelID;

            cmd.SetComputeVectorParam(cs, g_BufferDimensions, new Vector4(varianceEstimateParameter.width, varianceEstimateParameter.height, 0, 0));
            {
                cmd.SetComputeTextureParam(cs, kernel, g_ColorBuffer, varianceEstimateParameter.colorBuffer);
                cmd.SetComputeBufferParam(cs, kernel, g_MeanBuffer, varianceEstimateParameter.meanBuffer);
                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }

            kernel = ComputeDistanceKernelID;
            {
                cmd.SetComputeTextureParam(cs, kernel, g_ColorBuffer, varianceEstimateParameter.colorBuffer);
                cmd.SetComputeBufferParam(cs, kernel, g_MeanBuffer, varianceEstimateParameter.meanBuffer);
                cmd.SetComputeBufferParam(cs, kernel, g_SquareBuffer, varianceEstimateParameter.squareBuffer);

                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }
            kernel = ComputeDeviationKernelID;
            {
                cmd.SetComputeTextureParam(cs, kernel, g_ColorBuffer, varianceEstimateParameter.colorBuffer);
                cmd.SetComputeBufferParam(cs, kernel, g_MeanBuffer, varianceEstimateParameter.meanBuffer);
                cmd.SetComputeBufferParam(cs, kernel, g_SquareBuffer, varianceEstimateParameter.squareBuffer);
                cmd.SetComputeBufferParam(cs, kernel, g_ResultBuffer, varianceEstimateParameter.resultBuffer);

                cmd.DispatchCompute(cs, kernel, 1, 1, 1);
            }
        }
    }
}
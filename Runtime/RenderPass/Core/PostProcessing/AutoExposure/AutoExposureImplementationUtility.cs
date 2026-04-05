using UnityEngine;

namespace VividRP.Runtime
{
    internal static class AutoExposureImplementationUtility
    {
        private const string ClearHistogramKernelName = "ClearHistogram";
        private const string BuildHistogramKernelName = "BuildHistogram";
        private const string ResolveExposureKernelName = "ResolveExposure";
        private const string HdrpPrePassKernelName = "KPrePass";
        private const string HdrpReductionKernelName = "KReduction";
        private const string HdrpResetKernelName = "KReset";

        internal static AutoExposureImplementationPath ResolveImplementation(VividRenderPipelineAsset pipelineAsset)
        {
            return pipelineAsset?.AutoExposureImplementation ?? AutoExposureImplementationPath.Unreal;
        }

        internal static ComputeShader ResolveComputeShader(
            VividRPCoreResources resources,
            VividRenderPipelineAsset pipelineAsset)
        {
            return ResolveComputeShader(resources, ResolveImplementation(pipelineAsset));
        }

        internal static ComputeShader ResolveComputeShader(
            VividRPCoreResources resources,
            AutoExposureImplementationPath implementation)
        {
            return implementation == AutoExposureImplementationPath.HDRP
                ? resources?.AutoExposureHDRPCompute
                : resources?.AutoExposureCompute;
        }

        internal static ComputeShader ResolveHistogramDebugCompute(VividRPCoreResources resources)
        {
            return resources?.AutoExposureCompute;
        }

        internal static bool SupportsUnrealDispatch(ComputeShader computeShader)
        {
            return computeShader != null
                && computeShader.HasKernel(ClearHistogramKernelName)
                && computeShader.HasKernel(BuildHistogramKernelName)
                && computeShader.HasKernel(ResolveExposureKernelName);
        }

        internal static bool SupportsHdrpDispatch(ComputeShader computeShader)
        {
            return computeShader != null
                && computeShader.HasKernel(HdrpPrePassKernelName)
                && computeShader.HasKernel(HdrpReductionKernelName)
                && computeShader.HasKernel(HdrpResetKernelName);
        }
    }
}

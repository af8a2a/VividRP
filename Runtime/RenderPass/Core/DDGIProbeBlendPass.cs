using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class DDGIProbeBlendPass : UnsafePass, IAsyncComputeSupportedPass
    {
        private const string BlendKernelName = "DDGIProbeBlendingCS";

        private static readonly int RayDataId = Shader.PropertyToID("RayData");
        private static readonly int OutputId = Shader.PropertyToID("Output");
        private static readonly int ProbeDataId = Shader.PropertyToID("ProbeData");
        private static readonly int ProbeVariabilityId = Shader.PropertyToID("ProbeVariability");
        private static readonly int VolumeConstantsId = Shader.PropertyToID("DDGIVolumes");

        [RenderGraphResource(
            Name = "DDGIProbeRayData",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ProbeRayData;

        [RenderGraphResource(
            Name = "DDGIProbeIrradiance",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ProbeIrradiance;

        [RenderGraphResource(
            Name = "DDGIProbeDistance",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ProbeDistance;

        [RenderGraphResource(
            Name = "DDGIProbeData",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ProbeData;

        [RenderGraphResource(
            Name = "DDGIProbeVariability",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ProbeVariability;

        [RenderGraphResource(
            Name = "DDGIVolumeConstants",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_VolumeConstantsBuffer;

        private ComputeShader m_IrradianceBlendCompute;
        private ComputeShader m_DistanceBlendCompute;
        private int m_IrradianceKernel = -1;
        private int m_DistanceKernel = -1;
        private bool m_ShouldBlend;
        private int m_DispatchGroupCountX = 1;
        private int m_DispatchGroupCountY = 1;
        private int m_DispatchGroupCountZ = 1;
        private DDGIRootConstants m_RootConstants;

        public DDGIProbeBlendPass()
        {
            profilingSampler = new ProfilingSampler(nameof(DDGIProbeBlendPass));
            m_ProbeRayData = CreateImportedTexture("DDGIProbeRayData", GraphicsFormat.R32G32_SFloat, FilterMode.Point);
            m_ProbeIrradiance = CreateImportedTexture("DDGIProbeIrradiance", GraphicsFormat.A2B10G10R10_UNormPack32, FilterMode.Bilinear);
            m_ProbeDistance = CreateImportedTexture("DDGIProbeDistance", GraphicsFormat.R16G16_SFloat, FilterMode.Bilinear);
            m_ProbeData = CreateImportedTexture("DDGIProbeData", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point);
            m_ProbeVariability = CreateImportedTexture("DDGIProbeVariability", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point);
            m_VolumeConstantsBuffer = new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = 1,
                    Stride = Marshal.SizeOf<DDGIVolumeDescGPUPacked>(),
                    Target = GraphicsBuffer.Target.Structured,
                    Name = "DDGIVolumeConstants"
                }
            };
        }

        public override void Create()
        {
            VividRPCoreResources resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_IrradianceBlendCompute = resources?.DDGIProbeBlendIrradianceCompute;
            m_DistanceBlendCompute = resources?.DDGIProbeBlendDistanceCompute;

            if (m_IrradianceBlendCompute != null)
            {
                m_IrradianceKernel = m_IrradianceBlendCompute.FindKernel(BlendKernelName);
            }

            if (m_DistanceBlendCompute != null)
            {
                m_DistanceKernel = m_DistanceBlendCompute.FindKernel(BlendKernelName);
            }
        }

        public override void Prepare(ContextContainer frameData)
        {
            DDGIRuntimeData runtimeData = frameData.GetOrCreate<DDGIRuntimeData>();
            m_ShouldBlend = runtimeData.isRuntimeReady
                && m_IrradianceBlendCompute != null
                && m_DistanceBlendCompute != null
                && m_IrradianceKernel >= 0
                && m_DistanceKernel >= 0
                && DDGISystem.instance.ProbeRayDataHandle != null
                && DDGISystem.instance.ProbeIrradianceHandle != null
                && DDGISystem.instance.ProbeDistanceHandle != null
                && DDGISystem.instance.ProbeDataHandle != null
                && DDGISystem.instance.ProbeVariabilityHandle != null
                && runtimeData.volumeConstantsBuffer != null;

            if (!m_ShouldBlend)
            {
                m_VolumeConstantsBuffer.ClearImportedBuffer();
                return;
            }

            SyncImportedTexture(m_ProbeRayData, DDGISystem.instance.ProbeRayDataHandle);
            SyncImportedTexture(m_ProbeIrradiance, DDGISystem.instance.ProbeIrradianceHandle);
            SyncImportedTexture(m_ProbeDistance, DDGISystem.instance.ProbeDistanceHandle);
            SyncImportedTexture(m_ProbeData, DDGISystem.instance.ProbeDataHandle);
            SyncImportedTexture(m_ProbeVariability, DDGISystem.instance.ProbeVariabilityHandle);
            SyncImportedBuffer(m_VolumeConstantsBuffer, runtimeData.volumeConstantsBuffer, "DDGIVolumeConstants");

            Vector3Int probeCounts = runtimeData.activeVolume != null ? runtimeData.activeVolume.ProbeCounts : Vector3Int.one;
            m_DispatchGroupCountX = Mathf.Max(1, probeCounts.x);
            m_DispatchGroupCountY = Mathf.Max(1, probeCounts.z);
            m_DispatchGroupCountZ = Mathf.Max(1, probeCounts.y);
            m_RootConstants = runtimeData.rootConstants;
        }

        public override void Record(UnsafeGraphContext context)
        {
            if (!m_ShouldBlend)
            {
                return;
            }

            UnsafeCommandBuffer cmd = context.cmd;
            CommandBuffer nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(cmd);

            using (new ProfilingScope(nativeCmd, profilingSampler))
            {
                DispatchIrradianceBlend(cmd, nativeCmd);
                DispatchDistanceBlend(cmd, nativeCmd);
            }
        }

        public override void Dispose()
        {
            m_VolumeConstantsBuffer?.ClearImportedBuffer();
            m_IrradianceBlendCompute = null;
            m_DistanceBlendCompute = null;
            m_IrradianceKernel = -1;
            m_DistanceKernel = -1;
            m_ShouldBlend = false;
            m_DispatchGroupCountX = 1;
            m_DispatchGroupCountY = 1;
            m_DispatchGroupCountZ = 1;
            m_RootConstants = default;
        }

        private void DispatchIrradianceBlend(UnsafeCommandBuffer cmd, CommandBuffer nativeCmd)
        {
            BindSharedResources(cmd, nativeCmd, m_IrradianceBlendCompute, m_IrradianceKernel);
            cmd.SetComputeTextureParam(m_IrradianceBlendCompute, m_IrradianceKernel, OutputId, m_ProbeIrradiance.innerHandle);
            cmd.SetComputeTextureParam(m_IrradianceBlendCompute, m_IrradianceKernel, ProbeVariabilityId, m_ProbeVariability.innerHandle);
            cmd.DispatchCompute(
                m_IrradianceBlendCompute,
                m_IrradianceKernel,
                m_DispatchGroupCountX,
                m_DispatchGroupCountY,
                m_DispatchGroupCountZ);
        }

        private void DispatchDistanceBlend(UnsafeCommandBuffer cmd, CommandBuffer nativeCmd)
        {
            BindSharedResources(cmd, nativeCmd, m_DistanceBlendCompute, m_DistanceKernel);
            cmd.SetComputeTextureParam(m_DistanceBlendCompute, m_DistanceKernel, OutputId, m_ProbeDistance.innerHandle);
            cmd.DispatchCompute(
                m_DistanceBlendCompute,
                m_DistanceKernel,
                m_DispatchGroupCountX,
                m_DispatchGroupCountY,
                m_DispatchGroupCountZ);
        }

        private void BindSharedResources(
            UnsafeCommandBuffer cmd,
            CommandBuffer nativeCmd,
            ComputeShader computeShader,
            int kernel)
        {
            cmd.SetComputeTextureParam(computeShader, kernel, RayDataId, m_ProbeRayData.innerHandle);
            cmd.SetComputeTextureParam(computeShader, kernel, ProbeDataId, m_ProbeData.innerHandle);
            cmd.SetComputeBufferParam(computeShader, kernel, VolumeConstantsId, m_VolumeConstantsBuffer.innerHandle);
            ConstantBuffer.Push(nativeCmd, m_RootConstants, computeShader, DDGIRootConstants.ConstantBufferShaderId);
        }

        private static RenderGraphTexture CreateImportedTexture(string name, GraphicsFormat format, FilterMode filterMode)
        {
            RenderGraphTexture texture = RenderGraphTexture.CreateOutput(name, format);
            texture.desc.Dimension = TextureDimension.Tex2DArray;
            texture.desc.Slices = 1;
            texture.desc.EnableRandomWrite = true;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.FilterMode = filterMode;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        private static void SyncImportedTexture(RenderGraphTexture texture, RTHandle handle)
        {
            if (texture == null || handle == null)
            {
                return;
            }

            if (handle.rt != null)
            {
                texture.desc.Width = Mathf.Max(1, handle.rt.width);
                texture.desc.Height = Mathf.Max(1, handle.rt.height);
                texture.desc.Slices = Mathf.Max(1, handle.rt.volumeDepth);
                texture.desc.Dimension = handle.rt.dimension;
            }

            PassRecorder.ImportTexture(texture, handle);
        }

        private static void SyncImportedBuffer(RenderGraphBuffer buffer, GraphicsBuffer graphicsBuffer, string name)
        {
            if (buffer?.desc == null)
            {
                return;
            }

            if (graphicsBuffer == null)
            {
                buffer.ClearImportedBuffer();
                buffer.desc.Count = 1;
                return;
            }

            buffer.desc.Count = Mathf.Max(1, graphicsBuffer.count);
            buffer.desc.Stride = Mathf.Max(1, graphicsBuffer.stride);
            buffer.desc.Name = name;
            buffer.SetImportedBuffer(graphicsBuffer);
        }
    }
}

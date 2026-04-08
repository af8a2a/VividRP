using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class DDGIProbeTracePass : UnsafePass, IAsyncComputeSupportedPass
    {
        private const string ClearKernelName = "DDGIClearProbes";
        private const string TraceKernelName = "DDGIProbeTrace";
        private const string AccelerationStructureName = "_DDGIAccelerationStructure";
        private const int TraceThreadGroupSizeX = 8;
        private const int ClearThreadGroupSizeX = 8;
        private const int ClearThreadGroupSizeY = 8;

        private static readonly int ProbeRayDataId = Shader.PropertyToID("_DDGIProbeRayData");
        private static readonly int ProbeIrradianceId = Shader.PropertyToID("_DDGIProbeIrradianceRW");
        private static readonly int ProbeDistanceId = Shader.PropertyToID("_DDGIProbeDistanceRW");
        private static readonly int ProbeDataId = Shader.PropertyToID("_DDGIProbeDataRW");
        private static readonly int ProbeVariabilityId = Shader.PropertyToID("_DDGIProbeVariabilityRW");
        private static readonly int VolumeConstantsId = Shader.PropertyToID("_DDGIVolumes");
        private static readonly int InstanceBufferId = Shader.PropertyToID("_DDGIInstances");
        private static readonly int SubMeshBufferId = Shader.PropertyToID("_DDGISubMeshes");
        private static readonly int MaterialBufferId = Shader.PropertyToID("_DDGIMaterials");
        private static readonly int VertexBufferId = Shader.PropertyToID("_DDGIVertices");
        private static readonly int IndexBufferId = Shader.PropertyToID("_DDGIIndices");
        private static readonly int DirectionalLightBufferId = Shader.PropertyToID("_DirectionalLights");
        private static readonly int PunctualLightBufferId = Shader.PropertyToID("_PunctualLights");
        private static readonly int DirectionalLightCountId = Shader.PropertyToID("_DirectionalLightCount");
        private static readonly int PunctualLightCountId = Shader.PropertyToID("_PunctualLightCount");
        private static readonly int ClearWidthId = Shader.PropertyToID("_DDGIClearWidth");
        private static readonly int ClearHeightId = Shader.PropertyToID("_DDGIClearHeight");
        private static readonly int ClearSlicesId = Shader.PropertyToID("_DDGIClearSlices");

        [RenderGraphResource(Name = "DDGIRTAS", Access = AccessFlags.Read)]
        private RenderGraphAccelerationStructure m_DDGIAccelerationStructure;

        [RenderGraphResource(
            Name = "DDGIProbeRayData",
            Access = AccessFlags.Write,
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
            Access = AccessFlags.Write,
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

        [RenderGraphResource(
            Name = "DDGIInstances",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_InstanceBuffer;

        [RenderGraphResource(
            Name = "DDGISubMeshes",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_SubMeshBuffer;

        [RenderGraphResource(
            Name = "DDGIMaterials",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_MaterialBuffer;

        [RenderGraphResource(
            Name = "DDGIVertices",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_VertexBuffer;

        [RenderGraphResource(
            Name = "DDGIIndices",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_IndexBuffer;

        [RenderGraphResource(
            Name = "DDGIDirectionalLights",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_DirectionalLightBuffer;

        [RenderGraphResource(
            Name = "DDGIPunctualLights",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_PunctualLightBuffer;

        private ComputeShader m_ProbeTraceCompute;
        private int m_ClearKernel = -1;
        private int m_TraceKernel = -1;
        private bool m_ShouldTrace;
        private bool m_ShouldClear;
        private int m_TraceDispatchGroupCountX = 1;
        private int m_TraceDispatchGroupCountY = 1;
        private int m_TraceDispatchGroupCountZ = 1;
        private int m_ClearDispatchGroupCountX = 1;
        private int m_ClearDispatchGroupCountY = 1;
        private int m_ClearDispatchGroupCountZ = 1;
        private int m_ClearWidth = 1;
        private int m_ClearHeight = 1;
        private int m_ClearSlices = 1;
        private int m_DirectionalLightCount;
        private int m_PunctualLightCount;
        private DDGIRootConstants m_RootConstants;

        public DDGIProbeTracePass()
        {
            profilingSampler = new ProfilingSampler(nameof(DDGIProbeTracePass));
            m_DDGIAccelerationStructure = new RenderGraphAccelerationStructure
            {
                desc = RenderGraphAccelerationStructureDesc.Create("DDGIRTAS")
            };
            m_ProbeRayData = CreateImportedTexture("DDGIProbeRayData", GraphicsFormat.R32G32_SFloat, FilterMode.Point);
            m_ProbeIrradiance = CreateImportedTexture("DDGIProbeIrradiance", GraphicsFormat.A2B10G10R10_UNormPack32, FilterMode.Bilinear);
            m_ProbeDistance = CreateImportedTexture("DDGIProbeDistance", GraphicsFormat.R16G16_SFloat, FilterMode.Bilinear);
            m_ProbeData = CreateImportedTexture("DDGIProbeData", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point);
            m_ProbeVariability = CreateImportedTexture("DDGIProbeVariability", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point);
            m_VolumeConstantsBuffer = CreateImportedBuffer("DDGIVolumeConstants", Marshal.SizeOf<DDGIVolumeDescGPUPacked>());
            m_InstanceBuffer = CreateImportedBuffer("DDGIInstances", Marshal.SizeOf<DDGIInstanceData>());
            m_SubMeshBuffer = CreateImportedBuffer("DDGISubMeshes", Marshal.SizeOf<DDGISubMeshData>());
            m_MaterialBuffer = CreateImportedBuffer("DDGIMaterials", Marshal.SizeOf<DDGIMaterialData>());
            m_VertexBuffer = CreateImportedBuffer("DDGIVertices", Marshal.SizeOf<DDGIVertexData>());
            m_IndexBuffer = CreateImportedBuffer("DDGIIndices", sizeof(uint));
            m_DirectionalLightBuffer = CreateImportedBuffer("DDGIDirectionalLights", VividLightData.DirectionalLightData.Stride);
            m_PunctualLightBuffer = CreateImportedBuffer("DDGIPunctualLights", VividLightData.PunctualLightData.Stride);
        }

        public override void Create()
        {
            m_ProbeTraceCompute = PipelineResourceManager.Get<VividRPCoreResources>()?.DDGIProbeTraceCompute;
            if (m_ProbeTraceCompute == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find compute shader resource 'Shaders/Core/Private/GlobalIllumination/DDGI/Internal/ProbeTrace.compute' for {nameof(DDGIProbeTracePass)}.");
                return;
            }

            m_ClearKernel = m_ProbeTraceCompute.FindKernel(ClearKernelName);
            m_TraceKernel = m_ProbeTraceCompute.FindKernel(TraceKernelName);
        }

        public override void Prepare(ContextContainer frameData)
        {
            DDGIRuntimeData runtimeData = frameData.GetOrCreate<DDGIRuntimeData>();
            VividLightData lightData = frameData.GetOrCreate<VividLightData>();

            m_DDGIAccelerationStructure.SetAccelerationStructure(
                runtimeData.isRuntimeReady ? DDGISystem.instance.AccelerationStructure : null,
                transferOwnership: false);

            m_ShouldTrace = runtimeData.isRuntimeReady
                && m_ProbeTraceCompute != null
                && m_ClearKernel >= 0
                && m_TraceKernel >= 0
                && DDGISystem.instance.AccelerationStructure != null
                && DDGISystem.instance.ProbeRayDataHandle != null
                && DDGISystem.instance.ProbeIrradianceHandle != null
                && DDGISystem.instance.ProbeDistanceHandle != null
                && DDGISystem.instance.ProbeDataHandle != null
                && DDGISystem.instance.ProbeVariabilityHandle != null
                && runtimeData.volumeConstantsBuffer != null
                && runtimeData.instanceBuffer != null
                && runtimeData.subMeshBuffer != null
                && runtimeData.materialBuffer != null
                && runtimeData.vertexBuffer != null
                && runtimeData.indexBuffer != null;

            if (!m_ShouldTrace)
            {
                ClearImportedBuffers();
                m_ShouldClear = false;
                return;
            }

            SyncImportedTexture(m_ProbeRayData, DDGISystem.instance.ProbeRayDataHandle);
            SyncImportedTexture(m_ProbeIrradiance, DDGISystem.instance.ProbeIrradianceHandle);
            SyncImportedTexture(m_ProbeDistance, DDGISystem.instance.ProbeDistanceHandle);
            SyncImportedTexture(m_ProbeData, DDGISystem.instance.ProbeDataHandle);
            SyncImportedTexture(m_ProbeVariability, DDGISystem.instance.ProbeVariabilityHandle);

            SyncImportedBuffer(m_VolumeConstantsBuffer, runtimeData.volumeConstantsBuffer, "DDGIVolumeConstants");
            SyncImportedBuffer(m_InstanceBuffer, runtimeData.instanceBuffer, "DDGIInstances");
            SyncImportedBuffer(m_SubMeshBuffer, runtimeData.subMeshBuffer, "DDGISubMeshes");
            SyncImportedBuffer(m_MaterialBuffer, runtimeData.materialBuffer, "DDGIMaterials");
            SyncImportedBuffer(m_VertexBuffer, runtimeData.vertexBuffer, "DDGIVertices");
            SyncImportedBuffer(m_IndexBuffer, runtimeData.indexBuffer, "DDGIIndices");
            SyncImportedBuffer(m_DirectionalLightBuffer, runtimeData.directionalLightBuffer, "DDGIDirectionalLights");
            SyncImportedBuffer(m_PunctualLightBuffer, runtimeData.punctualLightBuffer, "DDGIPunctualLights");

            Vector3Int probeCounts = runtimeData.activeVolume != null ? runtimeData.activeVolume.ProbeCounts : Vector3Int.one;
            DDGIProfile profile = DDGIProfileTable.GetProfile(runtimeData.profileId);
            m_RootConstants = runtimeData.rootConstants;
            m_ShouldClear = runtimeData.clearProbeTextures;
            m_DirectionalLightCount = lightData != null ? lightData.directionalLightCount : 0;
            m_PunctualLightCount = lightData != null ? lightData.punctualLightCount : 0;

            m_TraceDispatchGroupCountX = Mathf.Max(1, CoreUtils.DivRoundUp(profile.RaysPerProbe, TraceThreadGroupSizeX));
            m_TraceDispatchGroupCountY = Mathf.Max(1, runtimeData.probesPerPlane);
            m_TraceDispatchGroupCountZ = Mathf.Max(1, probeCounts.y);

            int clearWidth = Mathf.Max(
                GetTextureWidth(DDGISystem.instance.ProbeRayDataHandle),
                Mathf.Max(
                    GetTextureWidth(DDGISystem.instance.ProbeIrradianceHandle),
                    Mathf.Max(
                        GetTextureWidth(DDGISystem.instance.ProbeDistanceHandle),
                        Mathf.Max(
                            GetTextureWidth(DDGISystem.instance.ProbeDataHandle),
                            GetTextureWidth(DDGISystem.instance.ProbeVariabilityHandle)))));
            int clearHeight = Mathf.Max(
                GetTextureHeight(DDGISystem.instance.ProbeRayDataHandle),
                Mathf.Max(
                    GetTextureHeight(DDGISystem.instance.ProbeIrradianceHandle),
                    Mathf.Max(
                        GetTextureHeight(DDGISystem.instance.ProbeDistanceHandle),
                        Mathf.Max(
                            GetTextureHeight(DDGISystem.instance.ProbeDataHandle),
                            GetTextureHeight(DDGISystem.instance.ProbeVariabilityHandle)))));
            int clearSlices = Mathf.Max(
                GetTextureSlices(DDGISystem.instance.ProbeRayDataHandle),
                Mathf.Max(
                    GetTextureSlices(DDGISystem.instance.ProbeIrradianceHandle),
                    Mathf.Max(
                        GetTextureSlices(DDGISystem.instance.ProbeDistanceHandle),
                        Mathf.Max(
                            GetTextureSlices(DDGISystem.instance.ProbeDataHandle),
                            GetTextureSlices(DDGISystem.instance.ProbeVariabilityHandle)))));
            m_ClearDispatchGroupCountX = Mathf.Max(1, CoreUtils.DivRoundUp(clearWidth, ClearThreadGroupSizeX));
            m_ClearDispatchGroupCountY = Mathf.Max(1, CoreUtils.DivRoundUp(clearHeight, ClearThreadGroupSizeY));
            m_ClearDispatchGroupCountZ = Mathf.Max(1, clearSlices);
            m_ClearWidth = Mathf.Max(1, clearWidth);
            m_ClearHeight = Mathf.Max(1, clearHeight);
            m_ClearSlices = Mathf.Max(1, clearSlices);
        }

        public override void Record(UnsafeGraphContext context)
        {
            if (!m_ShouldTrace || m_ProbeTraceCompute == null)
            {
                return;
            }

            UnsafeCommandBuffer cmd = context.cmd;
            CommandBuffer nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(cmd);

            using (new ProfilingScope(nativeCmd, profilingSampler))
            {
                if (m_ShouldClear)
                {
                    BindSharedResources(cmd, nativeCmd, m_ClearKernel);
                    cmd.SetComputeIntParam(m_ProbeTraceCompute, ClearWidthId, m_ClearWidth);
                    cmd.SetComputeIntParam(m_ProbeTraceCompute, ClearHeightId, m_ClearHeight);
                    cmd.SetComputeIntParam(m_ProbeTraceCompute, ClearSlicesId, m_ClearSlices);
                    cmd.DispatchCompute(
                        m_ProbeTraceCompute,
                        m_ClearKernel,
                        m_ClearDispatchGroupCountX,
                        m_ClearDispatchGroupCountY,
                        m_ClearDispatchGroupCountZ);
                    DDGISystem.instance.ConsumeClearRequest();
                }

                BindSharedResources(cmd, nativeCmd, m_TraceKernel);
                cmd.SetComputeIntParam(m_ProbeTraceCompute, DirectionalLightCountId, m_DirectionalLightCount);
                cmd.SetComputeIntParam(m_ProbeTraceCompute, PunctualLightCountId, m_PunctualLightCount);
                cmd.DispatchCompute(
                    m_ProbeTraceCompute,
                    m_TraceKernel,
                    m_TraceDispatchGroupCountX,
                    m_TraceDispatchGroupCountY,
                    m_TraceDispatchGroupCountZ);
            }
        }

        public override void Dispose()
        {
            ClearImportedBuffers();
            m_DDGIAccelerationStructure?.SetAccelerationStructure(null, transferOwnership: false);
            m_DDGIAccelerationStructure?.Dispose();
            m_DDGIAccelerationStructure = null;
            m_ProbeTraceCompute = null;
            m_ClearKernel = -1;
            m_TraceKernel = -1;
            m_ShouldTrace = false;
            m_ShouldClear = false;
            m_TraceDispatchGroupCountX = 1;
            m_TraceDispatchGroupCountY = 1;
            m_TraceDispatchGroupCountZ = 1;
            m_ClearDispatchGroupCountX = 1;
            m_ClearDispatchGroupCountY = 1;
            m_ClearDispatchGroupCountZ = 1;
            m_ClearWidth = 1;
            m_ClearHeight = 1;
            m_ClearSlices = 1;
            m_DirectionalLightCount = 0;
            m_PunctualLightCount = 0;
            m_RootConstants = default;
        }

        private void BindSharedResources(UnsafeCommandBuffer cmd, CommandBuffer nativeCmd, int kernel)
        {
            nativeCmd.SetRayTracingAccelerationStructure(
                m_ProbeTraceCompute,
                kernel,
                AccelerationStructureName,
                (RayTracingAccelerationStructure)m_DDGIAccelerationStructure);
            cmd.SetComputeTextureParam(m_ProbeTraceCompute, kernel, ProbeRayDataId, m_ProbeRayData.innerHandle);
            cmd.SetComputeTextureParam(m_ProbeTraceCompute, kernel, ProbeIrradianceId, m_ProbeIrradiance.innerHandle);
            cmd.SetComputeTextureParam(m_ProbeTraceCompute, kernel, ProbeDistanceId, m_ProbeDistance.innerHandle);
            cmd.SetComputeTextureParam(m_ProbeTraceCompute, kernel, ProbeDataId, m_ProbeData.innerHandle);
            cmd.SetComputeTextureParam(m_ProbeTraceCompute, kernel, ProbeVariabilityId, m_ProbeVariability.innerHandle);
            cmd.SetComputeBufferParam(m_ProbeTraceCompute, kernel, VolumeConstantsId, m_VolumeConstantsBuffer.innerHandle);
            cmd.SetComputeBufferParam(m_ProbeTraceCompute, kernel, InstanceBufferId, m_InstanceBuffer.innerHandle);
            cmd.SetComputeBufferParam(m_ProbeTraceCompute, kernel, SubMeshBufferId, m_SubMeshBuffer.innerHandle);
            cmd.SetComputeBufferParam(m_ProbeTraceCompute, kernel, MaterialBufferId, m_MaterialBuffer.innerHandle);
            cmd.SetComputeBufferParam(m_ProbeTraceCompute, kernel, VertexBufferId, m_VertexBuffer.innerHandle);
            cmd.SetComputeBufferParam(m_ProbeTraceCompute, kernel, IndexBufferId, m_IndexBuffer.innerHandle);
            if (m_DirectionalLightBuffer?.innerHandle.IsValid() == true)
            {
                cmd.SetComputeBufferParam(m_ProbeTraceCompute, kernel, DirectionalLightBufferId, m_DirectionalLightBuffer.innerHandle);
            }

            if (m_PunctualLightBuffer?.innerHandle.IsValid() == true)
            {
                cmd.SetComputeBufferParam(m_ProbeTraceCompute, kernel, PunctualLightBufferId, m_PunctualLightBuffer.innerHandle);
            }

            ConstantBuffer.Push(nativeCmd, m_RootConstants, m_ProbeTraceCompute, DDGIRootConstants.ConstantBufferShaderId);
        }

        private void ClearImportedBuffers()
        {
            m_VolumeConstantsBuffer?.ClearImportedBuffer();
            m_InstanceBuffer?.ClearImportedBuffer();
            m_SubMeshBuffer?.ClearImportedBuffer();
            m_MaterialBuffer?.ClearImportedBuffer();
            m_VertexBuffer?.ClearImportedBuffer();
            m_IndexBuffer?.ClearImportedBuffer();
            m_DirectionalLightBuffer?.ClearImportedBuffer();
            m_PunctualLightBuffer?.ClearImportedBuffer();
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

        private static RenderGraphBuffer CreateImportedBuffer(string name, int stride)
        {
            return new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = 1,
                    Stride = stride,
                    Target = GraphicsBuffer.Target.Structured,
                    Name = name
                }
            };
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

        private static int GetTextureWidth(RTHandle handle)
        {
            return handle?.rt != null ? Mathf.Max(1, handle.rt.width) : 1;
        }

        private static int GetTextureHeight(RTHandle handle)
        {
            return handle?.rt != null ? Mathf.Max(1, handle.rt.height) : 1;
        }

        private static int GetTextureSlices(RTHandle handle)
        {
            return handle?.rt != null ? Mathf.Max(1, handle.rt.volumeDepth) : 1;
        }
    }
}

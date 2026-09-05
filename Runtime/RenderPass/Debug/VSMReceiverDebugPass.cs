using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public enum VSMReceiverDebugMode
    {
        PreferredLevel,
        SampledLevel,
        FallbackLevels,
        TexelFootprint,
        SamplingWork,
        Availability,
        QualityPolicy,
    }

    // Opt-in graph node: absent from the shipping graph means no diagnostic
    // dispatch, texture, readback, or per-pixel instrumentation in normal resolve.
    public sealed class VSMReceiverDebugPass : ComputePass
    {
        private static readonly int DepthId = Shader.PropertyToID("_DepthTexture");
        private static readonly int NormalId = Shader.PropertyToID("_GBuffer1");
        private static readonly int ShadowId = Shader.PropertyToID("_VSMReceiverDebugShadow");
        private static readonly int OutputId = Shader.PropertyToID("_VSMReceiverDebugOutput");
        private static readonly int DataId = Shader.PropertyToID("_VSMReceiverDebugData");
        private static readonly int ModeId = Shader.PropertyToID("_VSMReceiverDebugMode");
        private static readonly int InvViewProjectionId = Shader.PropertyToID("_CSMInvViewProjMatrix");
        private static readonly int WidthId = Shader.PropertyToID("_CSMOutputWidth");
        private static readonly int HeightId = Shader.PropertyToID("_CSMOutputHeight");
        private static readonly int StaticId = Shader.PropertyToID("_VSMPrototypeStaticPhysicalPage");
        private static readonly int DynamicId = Shader.PropertyToID("_VSMPrototypeDynamicPhysicalPage");
        private static readonly int TableId = Shader.PropertyToID("_VSMPrototypePageTable");
        private static readonly int MetadataId = Shader.PropertyToID("_VSMPrototypePageMetadata");
        private static readonly int EnabledId = Shader.PropertyToID("_VSMPrototypeEnabled");
        private static readonly int ParametersId = Shader.PropertyToID("_VSMReceiverParameters");
        private static readonly int ResolutionId = Shader.PropertyToID("_VSMPrototypeVirtualResolution");
        private static readonly int PageSizeId = Shader.PropertyToID("_VSMPrototypePageSize");
        private static readonly int PagesPerAxisId = Shader.PropertyToID("_VSMPrototypePagesPerAxis");
        private static readonly int PagesPerRowId = Shader.PropertyToID("_VSMPrototypePhysicalPagesPerRow");

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Depth;
        [RenderGraphResource(Name = "GBuffer1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Normal;
        // Mandatory dependency on the completed resolve, not just the shadow pool.
        [RenderGraphResource(Name = "DirectionalShadowTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Shadow;
        [RenderGraphResource(Name = "OutputTexture", Access = AccessFlags.Write)]
        private RenderGraphTexture m_Output;
        [RenderGraphResource(Name = "DiagnosticData", Access = AccessFlags.Write)]
        private RenderGraphTexture m_Data;

        [SerializeField, Tooltip("Levels are zero-based. DiagnosticData contains raw values for the selected view; see Roadmap~/VSMQualityBaseline.md. Magenta means unavailable; black is sky.")]
        private VSMReceiverDebugMode m_VisualizationMode = VSMReceiverDebugMode.TexelFootprint;
        public VSMReceiverDebugMode VisualizationMode
        {
            get => m_VisualizationMode;
            set => m_VisualizationMode = NormalizeMode(value);
        }

        private ComputeShader m_Compute;
        private int m_Kernel = -1;
        private bool m_Ready;
        private ulong m_CameraId;
        private int m_FrameIndex;
        private Matrix4x4 m_ViewProjection;
        private Matrix4x4 m_InvViewProjection;
        private Vector4 m_Parameters;
        private Vector4 m_Quality;
        private TextureHandle m_Static, m_Dynamic;
        private BufferHandle m_Table, m_Metadata, m_Projections;

        public VSMReceiverDebugPass()
        {
            profilingSampler = new ProfilingSampler(nameof(VSMReceiverDebugPass));
            m_Depth = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_Normal = RenderGraphTexture.CreateInput("GBuffer1", GraphicsFormat.A2B10G10R10_UNormPack32);
            m_Shadow = RenderGraphTexture.CreateInput("DirectionalShadowTexture", GraphicsFormat.R16_SFloat);
            m_Output = CreateOutput("VSMReceiverDebugOutput", GraphicsFormat.R8G8B8A8_UNorm, Color.magenta);
            m_Data = CreateOutput("VSMReceiverDiagnosticData", GraphicsFormat.R32G32B32A32_SFloat, new Color(-1, -1, -1, -1));
        }

        private static RenderGraphTexture CreateOutput(string name, GraphicsFormat format, Color clear)
        {
            var texture = RenderGraphTexture.CreateOutput(name, format);
            texture.desc.EnableRandomWrite = true;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.ClearBuffer = true;
            texture.desc.ClearColor = clear;
            return texture;
        }

        public override void Create()
        {
            m_Compute = PipelineResourceManager.Get<VividRPCoreResources>()?.CSMShadowResolveCompute;
            m_Kernel = m_Compute != null && m_Compute.HasKernel("VSMReceiverDebug")
                ? m_Compute.FindKernel("VSMReceiverDebug") : -1;
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_Ready = false;
            m_Static = m_Dynamic = default;
            m_Table = m_Metadata = m_Projections = default;
            var camera = frameData.GetOrCreate<VividCameraData>();
            ConfigureOutputSize(camera.actualWidth, camera.actualHeight);
            if (DebugPassCameraUtility.ShouldSkipExecution(camera)) return;
            var settings = VividVolumeManagerUtility.GetCascadedShadowSettingsVolume();
            var shadow = frameData.GetOrCreate<VividShadowData>();
            if (settings == null || !settings.enableVirtualShadowMapPrototype.value || !shadow.isCSMActive
                || !VirtualShadowMapPrototypeRuntime.HasPageRequestResources
                || !VirtualShadowMapPrototypeRuntime.IsFramePrepared) return;
            m_CameraId = camera.camera != null ? EntityId.ToULong(camera.camera.GetEntityId()) : 0ul;
            m_FrameIndex = camera.frameIndex >= 0 ? camera.frameIndex : Time.frameCount;
            m_ViewProjection = camera.GetGPUViewProjectionMatrix(renderIntoTexture: true);
            m_InvViewProjection = m_ViewProjection.inverse;
            m_Quality = VirtualShadowMapReceiverQuality.BuildParameters(settings);
            m_Parameters = new Vector4(settings.virtualShadowMapPCF.value ? 1 : 0,
                shadow.depthBias, shadow.slopeScaleDepthBias, 0);
            m_Static = PassRecorder.ImportTextureForPass(this, VirtualShadowMapPrototypeRuntime.StaticPhysicalPage, AccessFlags.Read);
            m_Dynamic = PassRecorder.ImportTextureForPass(this, VirtualShadowMapPrototypeRuntime.DynamicPhysicalPage, AccessFlags.Read);
            m_Table = PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.PageTable, AccessFlags.Read);
            m_Metadata = PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.PageMetadata, AccessFlags.Read);
            m_Projections = PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.Projections.Buffer, AccessFlags.Read);
            m_Ready = m_Static.IsValid() && m_Dynamic.IsValid() && m_Table.IsValid()
                && m_Metadata.IsValid() && m_Projections.IsValid();
        }

        internal void ConfigureOutputSize(int width, int height)
        {
            m_Output.Resize(Mathf.Max(width, 1), Mathf.Max(height, 1));
            m_Data.Resize(Mathf.Max(width, 1), Mathf.Max(height, 1));
        }

        internal static VSMReceiverDebugMode NormalizeMode(VSMReceiverDebugMode mode) =>
            mode >= VSMReceiverDebugMode.PreferredLevel && mode <= VSMReceiverDebugMode.QualityPolicy
                ? mode : VSMReceiverDebugMode.TexelFootprint;

        public override void Record(ComputePassContext context)
        {
            if (!m_Ready || m_Compute == null || m_Kernel < 0
                || !VirtualShadowMapPrototypeRuntime.HasReceiverDebugSnapshot(m_CameraId, m_FrameIndex)
                || !m_Depth.innerHandle.IsValid() || !m_Normal.innerHandle.IsValid()
                || !m_Shadow.innerHandle.IsValid() || !m_Output.innerHandle.IsValid() || !m_Data.innerHandle.IsValid()) return;
            var cmd = context.cmd;
            cmd.SetComputeTextureParam(m_Compute, m_Kernel, DepthId, m_Depth.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, m_Kernel, NormalId, m_Normal.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, m_Kernel, ShadowId, m_Shadow.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, m_Kernel, OutputId, m_Output.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, m_Kernel, DataId, m_Data.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, m_Kernel, StaticId, m_Static);
            cmd.SetComputeTextureParam(m_Compute, m_Kernel, DynamicId, m_Dynamic);
            cmd.SetComputeBufferParam(m_Compute, m_Kernel, TableId, m_Table);
            cmd.SetComputeBufferParam(m_Compute, m_Kernel, MetadataId, m_Metadata);
            cmd.SetComputeBufferParam(m_Compute, m_Kernel, VirtualShadowMapProjectionSet.BufferId, m_Projections);
            cmd.SetComputeIntParam(m_Compute, VirtualShadowMapProjectionSet.CountId, VirtualShadowMapPrototypeRuntime.Projections.Count);
            cmd.SetComputeIntParam(m_Compute, ModeId, (int)NormalizeMode(m_VisualizationMode));
            cmd.SetComputeMatrixParam(m_Compute, VirtualShadowMapReceiverQuality.ViewProjectionId, m_ViewProjection);
            cmd.SetComputeVectorParam(m_Compute, VirtualShadowMapReceiverQuality.ParametersId, m_Quality);
            cmd.SetComputeMatrixParam(m_Compute, InvViewProjectionId, m_InvViewProjection);
            cmd.SetComputeVectorParam(m_Compute, ParametersId, m_Parameters);
            cmd.SetComputeIntParam(m_Compute, WidthId, m_Output.desc.Width);
            cmd.SetComputeIntParam(m_Compute, HeightId, m_Output.desc.Height);
            cmd.SetComputeIntParam(m_Compute, EnabledId, 1);
            cmd.SetComputeIntParam(m_Compute, ResolutionId, VirtualShadowMapPrototypeRuntime.VirtualResolution);
            cmd.SetComputeIntParam(m_Compute, PageSizeId, VirtualShadowMapPrototypeRuntime.PageSize);
            cmd.SetComputeIntParam(m_Compute, PagesPerAxisId, VirtualShadowMapPrototypeRuntime.PagesPerAxis);
            cmd.SetComputeIntParam(m_Compute, PagesPerRowId, VirtualShadowMapPrototypeRuntime.PhysicalPagesPerRow);
            cmd.DispatchCompute(m_Compute, m_Kernel, CoreUtils.DivRoundUp(m_Output.desc.Width, 8), CoreUtils.DivRoundUp(m_Output.desc.Height, 8), 1);
        }

        public override void Dispose()
        {
            m_Compute = null;
            m_Kernel = -1;
            m_Ready = false;
            m_Static = m_Dynamic = default;
            m_Table = m_Metadata = m_Projections = default;
        }
    }
}

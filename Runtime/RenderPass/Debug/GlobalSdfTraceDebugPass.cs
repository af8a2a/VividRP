using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Runtime.RenderPass.Core
{
    public enum GlobalSdfDebugMode
    {
        Surface,
        HorizontalSlice,
    }

    public sealed class GlobalSdfTraceDebugPass : ComputePass
    {
        private const int GroupSize = 8;
        private const string KernelName = "TraceGlobalSdfDebug";
        private static readonly int NearId = Shader.PropertyToID("_GlobalSdfNear");
        private static readonly int MiddleId = Shader.PropertyToID("_GlobalSdfMiddle");
        private static readonly int FarId = Shader.PropertyToID("_GlobalSdfFar");
        private static readonly int LevelsId = Shader.PropertyToID("_GlobalSdfLevels");
        private static readonly int ScrollId = Shader.PropertyToID("_GlobalSdfScroll");
        private static readonly int OutputId = Shader.PropertyToID("_GlobalSdfDebugOutput");
        private static readonly int ViewProjectionId = Shader.PropertyToID("_GlobalSdfInverseViewProjection");
        private static readonly int ScreenSizeId = Shader.PropertyToID("_GlobalSdfScreenSize");
        private static readonly int FarClipId = Shader.PropertyToID("_GlobalSdfFarClip");
        private static readonly int SliceHeightId = Shader.PropertyToID("_GlobalSdfSliceHeight");
        private static readonly int ModeId = Shader.PropertyToID("_GlobalSdfDebugMode");

        [RenderGraphResource(Name = "Near", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Near = RenderGraphTexture.CreateInput("Near", GraphicsFormat.R16_SFloat);

        [RenderGraphResource(Name = "Middle", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Middle = RenderGraphTexture.CreateInput("Middle", GraphicsFormat.R16_SFloat);

        [RenderGraphResource(Name = "Far", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Far = RenderGraphTexture.CreateInput("Far", GraphicsFormat.R16_SFloat);

        [RenderGraphResource(Name = "OutputTexture", Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        [SerializeField] private GlobalSdfDebugMode m_Mode;
        [SerializeField] private float m_SliceHeightOffset = -0.5f;

        private VividGlobalSdfFrameData m_Frame;
        private ComputeShader m_Shader;
        private int m_Kernel = -1;
        private Matrix4x4 m_InverseViewProjection;
        private float m_FarClip, m_SliceHeight;
        private int m_Width, m_Height;
        private bool m_Skip;

        public GlobalSdfTraceDebugPass()
        {
            profilingSampler = new ProfilingSampler(nameof(GlobalSdfTraceDebugPass));
            m_OutputTexture = RenderGraphTexture.CreateOutput("Global SDF Debug", GraphicsFormat.R8G8B8A8_UNorm);
            m_OutputTexture.desc.ClearBuffer = true;
            m_OutputTexture.desc.ClearColor = Color.black;
            m_OutputTexture.desc.EnableRandomWrite = true;
        }

        public override void Create()
        {
            m_Shader = PipelineResourceManager.Get<VividRPCoreResources>()?.GlobalSdfCompute;
            if (m_Shader != null && SystemInfo.supportsComputeShaders && m_Shader.HasKernel(KernelName))
                m_Kernel = m_Shader.FindKernel(KernelName);
            else
                Debug.LogWarning("[VividRP] Global SDF debug compute kernel is missing or unsupported.");
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            m_Width = CameraDimensionUtility.ResolveCameraDimension(cameraData.actualWidth, cameraData.pixelWidth, Screen.width);
            m_Height = CameraDimensionUtility.ResolveCameraDimension(cameraData.actualHeight, cameraData.pixelHeight, Screen.height);
            m_OutputTexture.Resize(m_Width, m_Height);
            m_Frame = frameData.GetOrCreate<VividGlobalSdfFrameData>();
            var camera = cameraData.camera;
            m_Skip = camera == null || DebugPassCameraUtility.ShouldSkipExecution(camera);
            if (m_Skip)
                return;

            m_InverseViewProjection = cameraData.GetGPUViewProjectionMatrix(renderIntoTexture: true).inverse;
            m_FarClip = camera.farClipPlane;
            m_SliceHeight = camera.transform.position.y + m_SliceHeightOffset;
        }

        public override void Record(ComputePassContext context)
        {
            if (m_Skip || m_Kernel < 0 || !m_Frame.Recorded || !m_OutputTexture.innerHandle.IsValid()
                || !ReferenceEquals(m_Near, m_Frame.Outputs[0]) || !ReferenceEquals(m_Middle, m_Frame.Outputs[1])
                || !ReferenceEquals(m_Far, m_Frame.Outputs[2]))
                return;

            var cmd = context.cmd;
            cmd.SetComputeTextureParam(m_Shader, m_Kernel, NearId, m_Near.innerHandle);
            cmd.SetComputeTextureParam(m_Shader, m_Kernel, MiddleId, m_Middle.innerHandle);
            cmd.SetComputeTextureParam(m_Shader, m_Kernel, FarId, m_Far.innerHandle);
            cmd.SetComputeTextureParam(m_Shader, m_Kernel, OutputId, m_OutputTexture.innerHandle);
            cmd.SetComputeVectorArrayParam(m_Shader, LevelsId, m_Frame.Levels);
            cmd.SetComputeVectorArrayParam(m_Shader, ScrollId, m_Frame.Scroll);
            cmd.SetComputeMatrixParam(m_Shader, ViewProjectionId, m_InverseViewProjection);
            cmd.SetComputeVectorParam(m_Shader, ScreenSizeId, new Vector4(m_Width, m_Height, 1f / m_Width, 1f / m_Height));
            cmd.SetComputeFloatParam(m_Shader, FarClipId, m_FarClip);
            cmd.SetComputeFloatParam(m_Shader, SliceHeightId, m_SliceHeight);
            cmd.SetComputeIntParam(m_Shader, ModeId, (int)m_Mode);
            cmd.DispatchCompute(m_Shader, m_Kernel, CoreUtils.DivRoundUp(m_Width, GroupSize),
                CoreUtils.DivRoundUp(m_Height, GroupSize), 1);
        }

        public override void Dispose()
        {
            m_Frame = null;
            m_Shader = null;
            m_Kernel = -1;
        }
    }
}

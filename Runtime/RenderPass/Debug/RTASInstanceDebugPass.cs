using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public enum RTASInstanceDebugVisualizationMode
    {
        InstanceIndex = 0,
        InstanceID = 1,
        PrimitiveIndex = 2,
    }

    public sealed class RTASInstanceDebugPass : UnsafePass, IAsyncComputeSupportedPass
    {
        private const int ThreadGroupSizeX = 8;
        private const int ThreadGroupSizeY = 8;
        private const string KernelName = "RTASInstanceDebug";
        private const string AccelerationStructureName = "_AccelerationStructure";

        private static readonly int OutputTextureId = Shader.PropertyToID("_OutputTexture");
        private static readonly int OutputWidthId = Shader.PropertyToID("_OutputWidth");
        private static readonly int OutputHeightId = Shader.PropertyToID("_OutputHeight");
        private static readonly int CameraPositionId = Shader.PropertyToID("_CameraPositionWS");
        private static readonly int VisualizationModeId = Shader.PropertyToID("_VisualizationMode");
        private static readonly int PixelCoordToViewDirRow0Id = Shader.PropertyToID("_PixelCoordToViewDirWS_Row0");
        private static readonly int PixelCoordToViewDirRow1Id = Shader.PropertyToID("_PixelCoordToViewDirWS_Row1");
        private static readonly int PixelCoordToViewDirRow2Id = Shader.PropertyToID("_PixelCoordToViewDirWS_Row2");
        private static readonly int PixelCoordToViewDirRow3Id = Shader.PropertyToID("_PixelCoordToViewDirWS_Row3");

        [RenderGraphResource(Name = "SceneRTAS", Access = AccessFlags.Read)]
        private RenderGraphAccelerationStructure m_SceneAccelerationStructure;

        [RenderGraphResource(
            Name = "OutputTexture",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        private ComputeShader m_RTASInstanceDebugCompute;
        private int m_Kernel = -1;
        private bool m_SupportsRayTracing;
        private int m_DispatchGroupCountX = 1;
        private int m_DispatchGroupCountY = 1;
        private Vector4 m_CameraPositionWS;
        private Matrix4x4 m_PixelCoordToViewDirWS;
        private bool m_ShouldSkipExecution;

        [SerializeField]
        private RTASInstanceDebugVisualizationMode m_VisualizationMode = RTASInstanceDebugVisualizationMode.InstanceIndex;

        public RTASInstanceDebugVisualizationMode VisualizationMode
        {
            get => m_VisualizationMode;
            set => m_VisualizationMode = value;
        }

        public RTASInstanceDebugPass()
        {
            profilingSampler = new ProfilingSampler(nameof(RTASInstanceDebugPass));
            m_SceneAccelerationStructure = CreateSceneAccelerationStructure();
            m_OutputTexture = RenderGraphTexture.CreateOutput("OutputTexture", GraphicsFormat.R16G16B16A16_SFloat);
            m_OutputTexture.desc.ClearBuffer = true;
            m_OutputTexture.desc.ClearColor = Color.black;
        }

        public override void Create()
        {
            m_SupportsRayTracing = SystemInfo.supportsRayTracing;
            m_RTASInstanceDebugCompute = PipelineResourceManager.Get<VividRPCoreResources>()?.RTASInstanceDebugCompute;

            if (m_RTASInstanceDebugCompute == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find compute shader resource 'Shaders/Core/Private/RTASInstanceDebug' for {nameof(RTASInstanceDebugPass)}.");
                return;
            }

            m_Kernel = m_RTASInstanceDebugCompute.FindKernel(KernelName);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            m_ShouldSkipExecution = DebugPassCameraUtility.ShouldSkipExecution(cameraData);
            int width = CameraDimensionUtility.ResolveCameraDimension(cameraData.actualWidth, cameraData.pixelWidth, Screen.width);
            int height = CameraDimensionUtility.ResolveCameraDimension(cameraData.actualHeight, cameraData.pixelHeight, Screen.height);

            ConfigureOutputTexture(width, height);

            m_DispatchGroupCountX = CoreUtils.DivRoundUp(width, ThreadGroupSizeX);
            m_DispatchGroupCountY = CoreUtils.DivRoundUp(height, ThreadGroupSizeY);

            var camera = cameraData.camera;
            if (camera != null)
            {
                m_PixelCoordToViewDirWS = cameraData.GetPixelCoordToViewDirWSMatrix();
                var cameraPosition = camera.transform.position;
                m_CameraPositionWS = new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 1f);
            }
            else
            {
                m_PixelCoordToViewDirWS = Matrix4x4.identity;
                m_CameraPositionWS = Vector4.zero;
            }
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_ShouldSkipExecution)
                return;

            if (!m_SupportsRayTracing
                || m_RTASInstanceDebugCompute == null
                || m_Kernel < 0
                || m_SceneAccelerationStructure == null
                || !m_OutputTexture.innerHandle.IsValid())
            {
                return;
            }

            var accelerationStructure = (RayTracingAccelerationStructure)m_SceneAccelerationStructure;
            if (accelerationStructure == null)
                return;

            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            using (new ProfilingScope(cmd, profilingSampler))
            {
                cmd.SetRayTracingAccelerationStructure(
                    m_RTASInstanceDebugCompute,
                    m_Kernel,
                    AccelerationStructureName,
                    accelerationStructure);
                cmd.SetComputeTextureParam(m_RTASInstanceDebugCompute, m_Kernel, OutputTextureId, m_OutputTexture.innerHandle);
                cmd.SetComputeIntParam(m_RTASInstanceDebugCompute, VisualizationModeId, (int)m_VisualizationMode);
                cmd.SetComputeVectorParam(m_RTASInstanceDebugCompute, CameraPositionId, m_CameraPositionWS);
                cmd.SetComputeVectorParam(
                    m_RTASInstanceDebugCompute,
                    PixelCoordToViewDirRow0Id,
                    m_PixelCoordToViewDirWS.GetRow(0));
                cmd.SetComputeVectorParam(
                    m_RTASInstanceDebugCompute,
                    PixelCoordToViewDirRow1Id,
                    m_PixelCoordToViewDirWS.GetRow(1));
                cmd.SetComputeVectorParam(
                    m_RTASInstanceDebugCompute,
                    PixelCoordToViewDirRow2Id,
                    m_PixelCoordToViewDirWS.GetRow(2));
                cmd.SetComputeVectorParam(
                    m_RTASInstanceDebugCompute,
                    PixelCoordToViewDirRow3Id,
                    m_PixelCoordToViewDirWS.GetRow(3));
                cmd.DispatchCompute(m_RTASInstanceDebugCompute, m_Kernel, m_DispatchGroupCountX, m_DispatchGroupCountY, 1);
            }
        }

        public override void Dispose()
        {
            m_RTASInstanceDebugCompute = null;
            m_Kernel = -1;
            m_DispatchGroupCountX = 1;
            m_DispatchGroupCountY = 1;
            m_CameraPositionWS = Vector4.zero;
            m_PixelCoordToViewDirWS = Matrix4x4.identity;
            m_ShouldSkipExecution = false;
        }

        private void ConfigureOutputTexture(int width, int height)
        {
            if (m_OutputTexture?.desc == null)
                return;

            m_OutputTexture.Resize(width, height);
            m_OutputTexture.desc.ColorFormat = m_OutputTexture.desc.ResolveColorFormat(
                GraphicsFormat.R16G16B16A16_SFloat);
            m_OutputTexture.desc.DepthBufferBits = DepthBits.None;
            m_OutputTexture.desc.MsaaSamples = MSAASamples.None;
            m_OutputTexture.desc.FilterMode = FilterMode.Point;
            m_OutputTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_OutputTexture.desc.ClearBuffer = true;
            m_OutputTexture.desc.ClearColor = Color.black;
            m_OutputTexture.desc.UseMipMap = false;
            m_OutputTexture.desc.AutoGenerateMips = false;
            m_OutputTexture.desc.MipCount = 1;
            m_OutputTexture.desc.EnableRandomWrite = true;
            m_OutputTexture.desc.BindTextureMS = false;
            m_OutputTexture.desc.Name = "OutputTexture";
        }
        private static RenderGraphAccelerationStructure CreateSceneAccelerationStructure()
        {
            return new RenderGraphAccelerationStructure
            {
                desc = RenderGraphAccelerationStructureDesc.Create("SceneRTAS")
            };
        }

    }
}

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class MeshSdfTraceDebugPass : ComputePass
    {
        private const int ThreadGroupSize = 8;
        private const string KernelName = "MeshSdfTraceDebug";

        private static readonly int VoxelsId = Shader.PropertyToID("_MeshSdfVoxels");
        private static readonly int FieldsId = Shader.PropertyToID("_MeshSdfFields");
        private static readonly int InstancesId = Shader.PropertyToID("_MeshSdfInstances");
        private static readonly int NodesId = Shader.PropertyToID("_MeshSdfNodes");
        private static readonly int LeafInstancesId = Shader.PropertyToID("_MeshSdfLeafInstances");
        private static readonly int NodeCountId = Shader.PropertyToID("_MeshSdfNodeCount");
        private static readonly int OutputId = Shader.PropertyToID("_MeshSdfOutput");
        private static readonly int InverseViewProjectionId = Shader.PropertyToID("_MeshSdfInverseViewProjection");
        private static readonly int WidthId = Shader.PropertyToID("_MeshSdfWidth");
        private static readonly int HeightId = Shader.PropertyToID("_MeshSdfHeight");
        private static readonly int LayerMaskId = Shader.PropertyToID("_MeshSdfLayerMask");
        private static readonly int FarClipId = Shader.PropertyToID("_MeshSdfFarClip");

        [RenderGraphResource(Name = "OutputTexture", Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        private readonly VividMeshSdfGpuScene m_Scene = new();
        private ComputeShader m_Compute;
        private int m_Kernel = -1;
        private Matrix4x4 m_InverseViewProjection;
        private int m_Width, m_Height, m_LayerMask;
        private float m_FarClip;

        public MeshSdfTraceDebugPass()
        {
            profilingSampler = new ProfilingSampler(nameof(MeshSdfTraceDebugPass));
            m_OutputTexture = RenderGraphTexture.CreateOutput("MeshSdfTraceDebug", GraphicsFormat.R8G8B8A8_UNorm);
            m_OutputTexture.desc.ClearBuffer = true;
            m_OutputTexture.desc.ClearColor = Color.black;
            m_OutputTexture.desc.EnableRandomWrite = true;
        }

        public override void Create()
        {
            m_Compute = PipelineResourceManager.Get<VividRPCoreResources>()?.MeshSdfTraceDebugCompute;
            if (m_Compute != null && SystemInfo.supportsComputeShaders && m_Compute.HasKernel(KernelName))
                m_Kernel = m_Compute.FindKernel(KernelName);
            else
                Debug.LogWarning("[VividRP] Mesh SDF trace compute kernel is missing or unsupported.");
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            m_Width = CameraDimensionUtility.ResolveCameraDimension(cameraData.actualWidth, cameraData.pixelWidth, Screen.width);
            m_Height = CameraDimensionUtility.ResolveCameraDimension(cameraData.actualHeight, cameraData.pixelHeight, Screen.height);
            m_OutputTexture.Resize(m_Width, m_Height);

            var camera = cameraData.camera;
            m_LayerMask = camera == null || DebugPassCameraUtility.ShouldSkipExecution(camera) ? 0 : camera.cullingMask;
            if (m_Kernel < 0 || m_LayerMask == 0)
                return;

            m_InverseViewProjection = cameraData.GetGPUViewProjectionMatrix(renderIntoTexture: true).inverse;
            m_FarClip = camera.farClipPlane;
            m_Scene.Update(VividMeshletRendererDatabase.instance);
        }

        public override void Record(ComputePassContext context)
        {
            if (m_Kernel < 0 || m_LayerMask == 0 || m_Scene.NodeCount == 0 || !m_OutputTexture.innerHandle.IsValid())
                return;

            var cmd = context.cmd;
            // Scene buffers are persistent and uploaded before graph recording.
            cmd.SetComputeBufferParam(m_Compute, m_Kernel, VoxelsId, m_Scene.Voxels);
            cmd.SetComputeBufferParam(m_Compute, m_Kernel, FieldsId, m_Scene.Fields);
            cmd.SetComputeBufferParam(m_Compute, m_Kernel, InstancesId, m_Scene.Instances);
            cmd.SetComputeBufferParam(m_Compute, m_Kernel, NodesId, m_Scene.Nodes);
            cmd.SetComputeBufferParam(m_Compute, m_Kernel, LeafInstancesId, m_Scene.LeafInstances);
            cmd.SetComputeIntParam(m_Compute, NodeCountId, m_Scene.NodeCount);
            cmd.SetComputeTextureParam(m_Compute, m_Kernel, OutputId, m_OutputTexture.innerHandle);
            cmd.SetComputeMatrixParam(m_Compute, InverseViewProjectionId, m_InverseViewProjection);
            cmd.SetComputeIntParam(m_Compute, WidthId, m_Width);
            cmd.SetComputeIntParam(m_Compute, HeightId, m_Height);
            cmd.SetComputeIntParam(m_Compute, LayerMaskId, m_LayerMask);
            cmd.SetComputeFloatParam(m_Compute, FarClipId, m_FarClip);
            cmd.DispatchCompute(m_Compute, m_Kernel,
                CoreUtils.DivRoundUp(m_Width, ThreadGroupSize), CoreUtils.DivRoundUp(m_Height, ThreadGroupSize), 1);
        }

        public override void Dispose()
        {
            m_Scene.Dispose();
            m_Compute = null;
            m_Kernel = -1;
        }
    }
}

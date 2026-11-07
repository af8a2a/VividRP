using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class GlobalSdfClipmapPass : ComputePass, IRenderGraphPreparePass
    {
        private const int GroupSize = 4;
        private const string KernelName = "ComposeGlobalSdf";
        private const string ThinKernelName = "ComposeGlobalSdfThinBricks";
        private static readonly int OutputId = Shader.PropertyToID("_GlobalSdfWrite");
        private static readonly int ThinBricksId = Shader.PropertyToID("_GlobalSdfThinBrickWrites");
        private static readonly int LevelId = Shader.PropertyToID("_GlobalSdfWriteLevel");
        private static readonly int RegionMinId = Shader.PropertyToID("_GlobalSdfRegionMin");
        private static readonly int RegionSizeId = Shader.PropertyToID("_GlobalSdfRegionSize");
        private static readonly int VoxelSizeId = Shader.PropertyToID("_GlobalSdfVoxelSize");
        private static readonly int LayerMaskId = Shader.PropertyToID("_GlobalSdfLayerMask");

        [RenderGraphResource(Name = "Near", Access = AccessFlags.Write)]
        private RenderGraphTexture m_Near = CreateOutput("Global SDF Near");

        [RenderGraphResource(Name = "Middle", Access = AccessFlags.Write)]
        private RenderGraphTexture m_Middle = CreateOutput("Global SDF Middle");

        [RenderGraphResource(Name = "Far", Access = AccessFlags.Write)]
        private RenderGraphTexture m_Far = CreateOutput("Global SDF Far");

        [RenderGraphResource(Name = "Thin Shell Bricks", Access = AccessFlags.Write)]
        private RenderGraphBuffer m_ThinBricks = RenderGraphBuffer.CreateStructured(
            "Global SDF Thin Shell Bricks", VividGlobalSdfClipmap.ThinBrickWordCount, sizeof(uint));

        [SerializeField, Range(0.05f, 2f)]
        private float m_BaseVoxelSize = VividGlobalSdfClipmap.DefaultVoxelSize;

        private readonly CameraRelativeSystem<VividGlobalSdfCameraState> m_Cameras = new();
        private readonly VividMeshSdfGpuScene m_Scene = new();
        private readonly RenderGraphTexture[] m_Outputs = new RenderGraphTexture[VividGlobalSdfClipmap.LevelCount];
        private readonly int[] m_RegionCounts = new int[VividGlobalSdfClipmap.LevelCount];
        private readonly VividGlobalSdfClipmap.Region[] m_Regions =
            new VividGlobalSdfClipmap.Region[VividGlobalSdfClipmap.LevelCount * VividGlobalSdfClipmap.RegionsPerLevel];
        private readonly int[] m_ThinRegionCounts = new int[VividGlobalSdfClipmap.LevelCount];
        private readonly VividGlobalSdfClipmap.Region[] m_ThinRegions =
            new VividGlobalSdfClipmap.Region[VividGlobalSdfClipmap.LevelCount * VividGlobalSdfClipmap.RegionsPerLevel];
        private readonly int[] m_RegionMin = new int[3];
        private readonly int[] m_RegionSize = new int[3];
        private VividGlobalSdfFrameData m_Frame;
        private ComputeShader m_Shader;
        private GraphicsFormat m_Format;
        private int m_Kernel = -1;
        private int m_ThinKernel = -1;

        public GlobalSdfClipmapPass() => profilingSampler = new ProfilingSampler(nameof(GlobalSdfClipmapPass));

        public override void Create()
        {
            m_Shader = PipelineResourceManager.Get<VividRPCoreResources>()?.GlobalSdfCompute;
            const GraphicsFormatUsage usage = GraphicsFormatUsage.Sample | GraphicsFormatUsage.LoadStore;
            m_Format = SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, usage)
                ? GraphicsFormat.R16_SFloat
                : GraphicsFormat.R32_SFloat;
            if (m_Shader != null && m_Shader.HasKernel(KernelName) && m_Shader.HasKernel(ThinKernelName) && SystemInfo.supportsComputeShaders
                && SystemInfo.maxTexture3DSize >= VividGlobalSdfClipmap.Resolution
                && SystemInfo.IsFormatSupported(m_Format, usage))
            {
                m_Kernel = m_Shader.FindKernel(KernelName);
                m_ThinKernel = m_Shader.FindKernel(ThinKernelName);
            }
            else
                Debug.LogWarning("[VividRP] Global SDF composition requires a supported compute kernel and writable 3D texture.");
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_Frame = frameData.GetOrCreate<VividGlobalSdfFrameData>();
            var camera = frameData.Get<VividCameraData>().camera;
            if (m_Kernel < 0 || camera == null || camera.cullingMask == 0 || DebugPassCameraUtility.ShouldSkipExecution(camera))
                return;

            m_Scene.Update(VividMeshletRendererDatabase.instance);
            if (m_Scene.NodeCount == 0)
                return;

            m_Cameras.PurgeDestroyedCameras();
            var state = m_Cameras.GetOrCreateBase(camera);
            state.EnsureResources(m_Format);
            float baseVoxelSize = float.IsFinite(m_BaseVoxelSize)
                ? Mathf.Clamp(m_BaseVoxelSize, 0.05f, 2f)
                : VividGlobalSdfClipmap.DefaultVoxelSize;
            int layerMask = camera.cullingMask;
            bool reset = !state.Valid || state.SceneRevision != m_Scene.Revision
                || state.LayerMask != layerMask || state.BaseVoxelSize != baseVoxelSize;

            m_Outputs[0] = m_Near;
            m_Outputs[1] = m_Middle;
            m_Outputs[2] = m_Far;
            m_Frame.State = state;
            m_Frame.Outputs = m_Outputs;
            m_Frame.MeshScene = m_Scene;
            m_Frame.ThinBricks = m_ThinBricks;
            m_Frame.SceneRevision = m_Scene.Revision;
            m_Frame.LayerMask = layerMask;
            m_Frame.BaseVoxelSize = baseVoxelSize;

            const int size = VividGlobalSdfClipmap.Resolution;
            float3 position = camera.transform.position;
            for (int level = 0; level < m_Outputs.Length; level++)
            {
                float voxel = baseVoxelSize * (1 << level);
                int3 origin = (int3)math.floor(position / voxel) - size / 2;
                int3 scroll = origin & (size - 1);
                m_Outputs[level].desc.ColorFormat = m_Format;
                m_Frame.Origins[level] = origin;
                m_Frame.Levels[level] = new Vector4(origin.x * voxel, origin.y * voxel, origin.z * voxel, voxel);
                m_Frame.Scroll[level] = new Vector4(scroll.x, scroll.y, scroll.z, 0f);
                int start = level * VividGlobalSdfClipmap.RegionsPerLevel;
                m_RegionCounts[level] = VividGlobalSdfClipmap.GetUpdateRegions(state.Origins[level], origin, reset, m_Regions, start);
                m_ThinRegionCounts[level] = VividGlobalSdfClipmap.GetUpdateRegions(
                    state.Origins[level] >> VividGlobalSdfClipmap.ThinBrickShift,
                    origin >> VividGlobalSdfClipmap.ThinBrickShift, reset, m_ThinRegions, start, VividGlobalSdfClipmap.ThinBrickResolution);
            }
        }

        public void PrepareRenderGraph(ContextContainer frameData)
        {
            if (m_Frame?.State == null)
            {
                m_ThinBricks.ClearImportedBuffer();
                return;
            }
            m_ThinBricks.SetImportedBuffer(m_Frame.State.ThinBricks);
            for (int level = 0; level < m_Outputs.Length; level++)
                PassRecorder.ImportTexture(m_Outputs[level], m_Frame.State.Textures[level]);
        }

        public override void Record(ComputePassContext context)
        {
            if (m_Frame?.State == null || !m_ThinBricks.innerHandle.IsValid())
                return;
            for (int level = 0; level < m_Outputs.Length; level++)
                if (!m_Outputs[level].innerHandle.IsValid())
                    return;

            var cmd = context.cmd;
            bool bound = false;
            for (int level = 0; level < m_Outputs.Length; level++)
            {
                int count = m_RegionCounts[level];
                int thinCount = m_ThinRegionCounts[level];
                if (count == 0 && thinCount == 0)
                    continue;
                if (!bound)
                {
                    // Do not reuse partially updated history if the frame fails before committing.
                    m_Frame.State.Valid = false;
                    m_Scene.Bind(cmd, m_Shader, m_Kernel);
                    m_Scene.Bind(cmd, m_Shader, m_ThinKernel);
                    cmd.SetComputeBufferParam(m_Shader, m_ThinKernel, ThinBricksId, m_ThinBricks.innerHandle);
                    cmd.SetComputeIntParam(m_Shader, LayerMaskId, m_Frame.LayerMask);
                    bound = true;
                }
                cmd.SetComputeFloatParam(m_Shader, VoxelSizeId, m_Frame.Levels[level].w);
                int start = level * VividGlobalSdfClipmap.RegionsPerLevel;
                if (count != 0)
                {
                    cmd.SetComputeTextureParam(m_Shader, m_Kernel, OutputId, m_Outputs[level].innerHandle);
                    DispatchRegions(cmd, m_Kernel, m_Regions, start, count);
                }
                if (thinCount != 0)
                {
                    cmd.SetComputeIntParam(m_Shader, LevelId, level);
                    DispatchRegions(cmd, m_ThinKernel, m_ThinRegions, start, thinCount);
                }
            }
            m_Frame.Recorded = true;
        }

        public override void Dispose()
        {
            if (m_Frame?.Outputs == m_Outputs)
                m_Frame.Reset();
            m_ThinBricks.ClearImportedBuffer();
            m_Cameras.Dispose();
            m_Scene.Dispose();
            m_Frame = null;
            m_Shader = null;
            m_Kernel = -1;
            m_ThinKernel = -1;
        }

        private void DispatchRegions(ComputeCommandBuffer cmd, int kernel, VividGlobalSdfClipmap.Region[] regions,
            int start, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var region = regions[start + i];
                SetInt3(m_RegionMin, region.Min);
                SetInt3(m_RegionSize, region.Size);
                cmd.SetComputeIntParams(m_Shader, RegionMinId, m_RegionMin);
                cmd.SetComputeIntParams(m_Shader, RegionSizeId, m_RegionSize);
                cmd.DispatchCompute(m_Shader, kernel, CoreUtils.DivRoundUp(region.Size.x, GroupSize),
                    CoreUtils.DivRoundUp(region.Size.y, GroupSize), CoreUtils.DivRoundUp(region.Size.z, GroupSize));
            }
        }

        private static RenderGraphTexture CreateOutput(string name)
        {
            const int size = VividGlobalSdfClipmap.Resolution;
            var texture = RenderGraphTexture.CreateOutput(name, GraphicsFormat.R16_SFloat);
            texture.Resize(size, size);
            texture.desc.Dimension = TextureDimension.Tex3D;
            texture.desc.Slices = size;
            texture.desc.WrapMode = TextureWrapMode.Repeat;
            texture.desc.EnableRandomWrite = true;
            return texture;
        }

        private static void SetInt3(int[] values, int3 value)
        {
            values[0] = value.x;
            values[1] = value.y;
            values[2] = value.z;
        }
    }
}

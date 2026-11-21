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
        private const string ClearKernelName = "ClearGlobalSdfPages";
        private const string ThinKernelName = "ComposeGlobalSdfThinBricks";
        private const string BorderKernelName = "CopyGlobalSdfPageBorders";
        private const string MipSeedKernelName = "SeedGlobalSdfMip";
        private const string MipPropagateKernelName = "PropagateGlobalSdfMip";
        private static readonly int OutputId = Shader.PropertyToID("_GlobalSdfWrite");
        private static readonly int PagesId = Shader.PropertyToID("_GlobalSdfPages");
        private static readonly int FirstPageId = Shader.PropertyToID("_GlobalSdfFirstPage");
        private static readonly int ThinBricksId = Shader.PropertyToID("_GlobalSdfThinBrickWrites");
        private static readonly int LevelId = Shader.PropertyToID("_GlobalSdfWriteLevel");
        private static readonly int RegionMinId = Shader.PropertyToID("_GlobalSdfRegionMin");
        private static readonly int RegionSizeId = Shader.PropertyToID("_GlobalSdfRegionSize");
        private static readonly int VoxelSizeId = Shader.PropertyToID("_GlobalSdfVoxelSize");
        private static readonly int LayerMaskId = Shader.PropertyToID("_GlobalSdfLayerMask");
        private static readonly int ScrollId = Shader.PropertyToID("_GlobalSdfScroll");
        private static readonly int MipFineAtlasId = Shader.PropertyToID("_GlobalSdfMipFineAtlas");
        private static readonly int MipSourceId = Shader.PropertyToID("_GlobalSdfMipSource");
        private static readonly int MipOutputId = Shader.PropertyToID("_GlobalSdfMipOutput");
        private static readonly int MipReadOffsetId = Shader.PropertyToID("_GlobalSdfMipReadOffset");
        private static readonly int MipWriteOffsetId = Shader.PropertyToID("_GlobalSdfMipWriteOffset");

        [RenderGraphResource(Name = "Near", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_Near = CreateOutput("Global SDF Near");

        [RenderGraphResource(Name = "Middle", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_Middle = CreateOutput("Global SDF Middle");

        [RenderGraphResource(Name = "Far", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_Far = CreateOutput("Global SDF Far");

        [RenderGraphResource(Name = "Coarse Mip", Access = AccessFlags.ReadWrite)]
        private readonly RenderGraphTexture m_Mip = CreateMipTexture("Global SDF Coarse Mip", VividGlobalSdfClipmap.LevelCount);

        [RenderGraphResource(Name = "Coarse Mip Scratch", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_MipScratch = CreateMipTexture("Global SDF Coarse Mip Scratch", 1);

        [RenderGraphResource(Name = "Page Table", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_Pages = RenderGraphBuffer.CreateStructured(
            "Global SDF Page Table", VividGlobalSdfClipmap.PageCount, sizeof(uint));

        [RenderGraphResource(Name = "Thin Shell Bricks", Access = AccessFlags.Write)]
        private RenderGraphBuffer m_ThinBricks = RenderGraphBuffer.CreateStructured(
            "Global SDF Thin Shell Bricks", VividGlobalSdfClipmap.ThinBrickWordCount, sizeof(uint));

        [SerializeField, Range(0.05f, 2f)]
        private float m_BaseVoxelSize = VividGlobalSdfClipmap.DefaultVoxelSize;

        private readonly CameraRelativeSystem<VividGlobalSdfCameraState> m_Cameras = new();
        private readonly VividMeshSdfGpuScene m_Scene = new(new VividSparseMeshSdfAtlas());
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
        private int m_SparseKernel = -1;
        private TextureHandle m_SparseBricks;
        private int m_ClearKernel = -1;
        private int m_ThinKernel = -1;
        private int m_BorderKernel = -1, m_MipSeedKernel = -1, m_MipPropagateKernel = -1;

        public GlobalSdfClipmapPass() => profilingSampler = new ProfilingSampler(nameof(GlobalSdfClipmapPass));

        public override void Create()
        {
            m_Shader = PipelineResourceManager.Get<VividRPCoreResources>()?.GlobalSdfCompute;
            const GraphicsFormatUsage usage = GraphicsFormatUsage.Sample | GraphicsFormatUsage.Linear | GraphicsFormatUsage.LoadStore;
            m_Format = SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, usage)
                ? GraphicsFormat.R16_SFloat
                : GraphicsFormat.R32_SFloat;
            if (m_Shader != null && m_Shader.HasKernel(KernelName) && m_Shader.HasKernel(ClearKernelName)
                && m_Shader.HasKernel(ThinKernelName) && m_Shader.HasKernel(BorderKernelName)
                && m_Shader.HasKernel(MipSeedKernelName) && m_Shader.HasKernel(MipPropagateKernelName)
                && SystemInfo.supportsComputeShaders && SystemInfo.maxTexture3DSize >= VividGlobalSdfClipmap.AtlasResolution
                && SystemInfo.IsFormatSupported(m_Format, usage))
            {
                m_Kernel = m_Shader.FindKernel(KernelName);
                m_SparseKernel = m_Shader.FindKernel("ComposeGlobalSdfSparse");
                m_ClearKernel = m_Shader.FindKernel(ClearKernelName);
                m_ThinKernel = m_Shader.FindKernel(ThinKernelName);
                m_BorderKernel = m_Shader.FindKernel(BorderKernelName);
                m_MipSeedKernel = m_Shader.FindKernel(MipSeedKernelName);
                m_MipPropagateKernel = m_Shader.FindKernel(MipPropagateKernelName);
                m_Mip.desc.ColorFormat = m_MipScratch.desc.ColorFormat = m_Format;
            }
            else
                Debug.LogWarning("[VividRP] Global SDF composition requires a supported compute kernel and writable 3D texture.");
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_SparseBricks = default;
            m_Frame = frameData.GetOrCreate<VividGlobalSdfFrameData>();
            var camera = frameData.Get<VividCameraData>().camera;
            if (m_Kernel < 0 || camera == null || camera.cullingMask == 0 || DebugPassCameraUtility.ShouldSkipExecution(camera))
                return;

            m_Scene.Update(VividMeshletRendererDatabase.instance);
            if (m_Scene.NodeCount == 0 || !m_Scene.PrepareBrickTexture(this))
                return;
            if (m_Scene.SparseAtlas.Active)
                m_SparseBricks = PassRecorder.ImportTextureForPass(this, m_Scene.SparseAtlas.BricksHandle);

            m_Cameras.PurgeDestroyedCameras();
            var state = m_Cameras.GetOrCreateBase(camera);
            state.EnsureBuffers();
            state.EnsureMip(m_Format);
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
            m_Frame.Mip = m_Mip;
            m_Frame.MeshScene = m_Scene;
            m_Frame.Pages = m_Pages;
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
                bool rebuild = reset || math.any(math.abs(origin - state.Origins[level]) >= size)
                    || state.Textures[level]?.rt == null || !state.Textures[level].rt.IsCreated();
                if (rebuild || math.any(origin != state.Origins[level]))
                {
                    state.Valid = false;
                    rebuild = state.UpdateResidency(level, origin, voxel, (uint)layerMask, m_Format, rebuild, m_Scene);
                }
                else
                    state.NewPageCounts[level] = 0;

                var atlas = state.Textures[level].rt;
                m_Outputs[level].Resize(atlas.width, atlas.height);
                m_Outputs[level].desc.Slices = atlas.volumeDepth;
                m_Outputs[level].desc.ColorFormat = m_Format;
                m_Frame.Origins[level] = origin;
                m_Frame.Levels[level] = new Vector4(origin.x * voxel, origin.y * voxel, origin.z * voxel, voxel);
                m_Frame.Scroll[level] = new Vector4(scroll.x, scroll.y, scroll.z, 0f);
                int start = level * VividGlobalSdfClipmap.RegionsPerLevel;
                m_RegionCounts[level] = VividGlobalSdfClipmap.GetUpdateRegions(state.Origins[level], origin, rebuild, m_Regions, start);
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
                m_Pages.ClearImportedBuffer();
                return;
            }
            m_Pages.SetImportedBuffer(m_Frame.State.Pages);
            m_ThinBricks.SetImportedBuffer(m_Frame.State.ThinBricks);
            for (int level = 0; level < m_Outputs.Length; level++)
                PassRecorder.ImportTexture(m_Outputs[level], m_Frame.State.Textures[level]);
            PassRecorder.ImportTexture(m_Mip, m_Frame.State.Mip);
        }

        public override void Record(ComputePassContext context)
        {
            if (m_Frame?.State == null || !m_ThinBricks.innerHandle.IsValid() || !m_Pages.innerHandle.IsValid()
                || !m_Mip.innerHandle.IsValid() || !m_MipScratch.innerHandle.IsValid())
                return;
            for (int level = 0; level < m_Outputs.Length; level++)
                if (!m_Outputs[level].innerHandle.IsValid())
                    return;

            var cmd = context.cmd;
            int composeKernel = m_Scene.SparseAtlas.Active ? m_SparseKernel : m_Kernel;
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
                    m_Scene.Bind(cmd, m_Shader, composeKernel);
                    if (m_Scene.SparseAtlas.Active)
                    {
                        m_Scene.SparseAtlas.Bind(cmd, m_Shader, composeKernel);
                        cmd.SetComputeTextureParam(m_Shader, composeKernel, VividSparseMeshSdfAtlas.BricksId, m_SparseBricks);
                    }
                    m_Scene.Bind(cmd, m_Shader, m_ThinKernel);
                    cmd.SetComputeBufferParam(m_Shader, composeKernel, PagesId, m_Pages.innerHandle);
                    cmd.SetComputeBufferParam(m_Shader, m_BorderKernel, PagesId, m_Pages.innerHandle);
                    cmd.SetComputeBufferParam(m_Shader, m_MipSeedKernel, PagesId, m_Pages.innerHandle);
                    cmd.SetComputeVectorArrayParam(m_Shader, ScrollId, m_Frame.Scroll);
                    cmd.SetComputeBufferParam(m_Shader, m_ThinKernel, ThinBricksId, m_ThinBricks.innerHandle);
                    cmd.SetComputeIntParam(m_Shader, LayerMaskId, m_Frame.LayerMask);
                    bound = true;
                }
                cmd.SetComputeFloatParam(m_Shader, VoxelSizeId, m_Frame.Levels[level].w);
                cmd.SetComputeIntParam(m_Shader, LevelId, level);
                int start = level * VividGlobalSdfClipmap.RegionsPerLevel;
                if (count != 0)
                {
                    ClearNewPages(cmd, level);
                    cmd.SetComputeTextureParam(m_Shader, composeKernel, OutputId, m_Outputs[level].innerHandle);
                    DispatchRegions(cmd, composeKernel, m_Regions, start, count);
                    cmd.SetComputeTextureParam(m_Shader, m_BorderKernel, OutputId, m_Outputs[level].innerHandle);
                    cmd.DispatchCompute(m_Shader, m_BorderKernel, VividGlobalSdfClipmap.PagesPerLevel, 1, 1);
                    RecordCoarseMip(cmd, level);
                }
                if (thinCount != 0)
                    DispatchRegions(cmd, m_ThinKernel, m_ThinRegions, start, thinCount);
            }
            m_Frame.Recorded = true;
        }

        public override void Dispose()
        {
            if (m_Frame?.Outputs == m_Outputs)
                m_Frame.Reset();
            m_ThinBricks.ClearImportedBuffer();
            m_Pages.ClearImportedBuffer();
            m_Cameras.Dispose();
            m_Scene.Dispose();
            m_Frame = null;
            m_Shader = null;
            m_Kernel = -1;
            m_ClearKernel = -1;
            m_ThinKernel = -1;
            m_BorderKernel = m_MipSeedKernel = m_MipPropagateKernel = -1;
        }

        private void RecordCoarseMip(ComputeCommandBuffer cmd, int level)
        {
            const int groups = VividGlobalSdfClipmap.MipResolution / GroupSize;
            int offset = level * VividGlobalSdfClipmap.MipResolution;
            var mip = m_Mip.innerHandle;
            var scratch = m_MipScratch.innerHandle;
            cmd.SetComputeTextureParam(m_Shader, m_MipSeedKernel, MipFineAtlasId, m_Outputs[level].innerHandle);
            cmd.SetComputeTextureParam(m_Shader, m_MipSeedKernel, MipOutputId, mip);
            cmd.SetComputeIntParam(m_Shader, MipWriteOffsetId, offset);
            cmd.DispatchCompute(m_Shader, m_MipSeedKernel, groups, groups, groups);
            for (int step = 1; step < VividGlobalSdfClipmap.MipPropagationSteps; step++)
            {
                bool writeMip = (step & 1) == 0;
                cmd.SetComputeTextureParam(m_Shader, m_MipPropagateKernel, MipSourceId, writeMip ? scratch : mip);
                cmd.SetComputeTextureParam(m_Shader, m_MipPropagateKernel, MipOutputId, writeMip ? mip : scratch);
                cmd.SetComputeIntParam(m_Shader, MipReadOffsetId, writeMip ? 0 : offset);
                cmd.SetComputeIntParam(m_Shader, MipWriteOffsetId, writeMip ? offset : 0);
                cmd.DispatchCompute(m_Shader, m_MipPropagateKernel, groups, groups, groups);
            }
        }

        private void ClearNewPages(ComputeCommandBuffer cmd, int level)
        {
            var state = m_Frame.State;
            int count = state.NewPageCounts[level];
            if (count == 0)
                return;

            // Retained cells in a previously missing page had the implicit band value.
            cmd.SetComputeTextureParam(m_Shader, m_ClearKernel, OutputId, m_Outputs[level].innerHandle);
            const int groupsPerAxis = VividGlobalSdfClipmap.BrickSize / GroupSize;
            int end = level * VividGlobalSdfClipmap.PagesPerLevel + count;
            for (int i = end - count; i < end;)
            {
                int first = state.NewPages[i++];
                int length = 1;
                while (i < end && state.NewPages[i] == first + length)
                {
                    i++;
                    length++;
                }
                cmd.SetComputeIntParam(m_Shader, FirstPageId, first);
                cmd.DispatchCompute(m_Shader, m_ClearKernel, length * groupsPerAxis, groupsPerAxis, groupsPerAxis);
            }
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
            const int size = VividGlobalSdfClipmap.AtlasResolution;
            var texture = RenderGraphTexture.CreateOutput(name, GraphicsFormat.R16_SFloat);
            texture.Resize(size, size);
            texture.desc.Dimension = TextureDimension.Tex3D;
            texture.desc.Slices = size;
            texture.desc.FilterMode = FilterMode.Bilinear;
            texture.desc.EnableRandomWrite = true;
            return texture;
        }

        private static RenderGraphTexture CreateMipTexture(string name, int levelCount)
        {
            const int size = VividGlobalSdfClipmap.MipResolution;
            var texture = RenderGraphTexture.CreateOutput(name, GraphicsFormat.R16_SFloat);
            texture.Resize(size, size);
            texture.desc.Dimension = TextureDimension.Tex3D;
            texture.desc.Slices = size * levelCount;
            texture.desc.FilterMode = FilterMode.Bilinear;
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

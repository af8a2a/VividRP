using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.GPUDriven.Meshlets
{
    internal sealed class VividMeshSdfGpuScene : IDisposable
    {
#if UNITY_EDITOR
        internal static bool EditorForceTwoSidedEmission;
#endif
        private static readonly int VoxelsId = Shader.PropertyToID("_MeshSdfVoxels");
        private static readonly int BrickAtlasId = Shader.PropertyToID("_MeshSdfBrickAtlas");
        private static readonly int BrickAtlasSizeId = Shader.PropertyToID("_MeshSdfBrickAtlasSize");
        private static readonly int FieldsId = Shader.PropertyToID("_MeshSdfFields");
        private static readonly int InstancesId = Shader.PropertyToID("_MeshSdfInstances");
        private static readonly int TraceInstancesId = Shader.PropertyToID("_MeshSdfTraceInstances");
        private static readonly int NodesId = Shader.PropertyToID("_MeshSdfNodes");
        private static readonly int LeafInstancesId = Shader.PropertyToID("_MeshSdfLeafInstances");
        private static readonly int NodeCountId = Shader.PropertyToID("_MeshSdfNodeCount");

        // Keep these layouts in sync with MeshSdfTrace.hlsl.
        [StructLayout(LayoutKind.Sequential)]
        internal struct Field
        {
            public Vector4 OriginAndVoxelSize;
            public uint Width, Height, Depth, WordOffset;
            public uint SignMode, PageOffset;
            public float ShellThickness;
            public uint MostlyTwoSided;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Instance
        {
            public Vector4 WorldToLocal0, WorldToLocal1, WorldToLocal2;
            public uint FieldIndex, LayerMask, EntityIdLow, EntityIdHigh;
            public float DistanceScale;
            public uint MotionFrame, Padding1, Padding2;
            public Vector4 PreviousLocalToWorld0, PreviousLocalToWorld1, PreviousLocalToWorld2;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct TraceInstance
        {
            public Vector4 WorldToLocal0, WorldToLocal1, WorldToLocal2;
            public uint FieldIndex, LayerMask, EntityIdLow, EntityIdHigh;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Node
        {
            public Vector3 Min;
            public uint FirstLeaf;
            public Vector3 Max;
            public uint EscapeIndex;
            public uint LeafCount, LayerMask, Padding0, Padding1;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DiffuseSurface : IEquatable<DiffuseSurface>
        {
            public Vector4 AlbedoAndValid;
            public Vector4 Emission; // RGB radiance; W is Unity CullMode when original card normals are available.
            public Vector4 CardMinAndFirst;
            public Vector4 CardExtent; // w is the 1-based runtime lighting instance slot.

            public readonly bool Equals(DiffuseSurface other) =>
                AlbedoAndValid.Equals(other.AlbedoAndValid) && Emission.Equals(other.Emission)
                && CardMinAndFirst.Equals(other.CardMinAndFirst) && CardExtent.Equals(other.CardExtent);

            public readonly bool HasSameTransport(DiffuseSurface other) =>
                AlbedoAndValid.Equals(other.AlbedoAndValid) && Emission.w == other.Emission.w
                && CardMinAndFirst.Equals(other.CardMinAndFirst)
                && CardExtent.Equals(other.CardExtent);
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");
        private static readonly int CullId = Shader.PropertyToID("_Cull");

        private const int MaxLeafInstances = 1;
        private const int SahBinCount = 8;

        private struct SahBin
        {
            public Bounds Bounds;
            public int Count;
        }
        private const VividMeshletRendererFlags VisibleFlags = VividMeshletRendererFlags.ActiveInHierarchy
            | VividMeshletRendererFlags.Enabled | VividMeshletRendererFlags.SourceRendererEnabled;

        private struct FieldSource : IEquatable<FieldSource>
        {
            public Texture3D Texture;
            public uint TextureVersion;
            public Field Layout;

            public readonly bool Equals(FieldSource other) =>
                ReferenceEquals(Texture, other.Texture)
                && TextureVersion == other.TextureVersion
                && Layout.Width == other.Layout.Width && Layout.Height == other.Layout.Height && Layout.Depth == other.Layout.Depth
                && Layout.OriginAndVoxelSize.Equals(other.Layout.OriginAndVoxelSize)
                && Layout.SignMode == other.Layout.SignMode
                && Layout.ShellThickness.Equals(other.Layout.ShellThickness)
                && Layout.MostlyTwoSided == other.Layout.MostlyTwoSided;
        }

        private struct InstanceSource : IEquatable<InstanceSource>
        {
            public Matrix4x4 LocalToWorld, WorldToLocal;
            public Bounds SparseBounds;
            public ulong EntityId;
            public uint FieldIndex, LayerMask;

            public readonly bool Equals(InstanceSource other) =>
                EntityId == other.EntityId && FieldIndex == other.FieldIndex && LayerMask == other.LayerMask
                && LocalToWorld.Equals(other.LocalToWorld) && WorldToLocal.Equals(other.WorldToLocal)
                && SparseBounds.Equals(other.SparseBounds);
        }

        private struct RadiosityCardIdentity : IEquatable<RadiosityCardIdentity>
        {
            public ulong Entity;
            public FieldSource Field;
            public Vector4 CardMinAndFirst, CardExtent;
            public int Slot;

            public readonly bool Equals(RadiosityCardIdentity other) => Entity == other.Entity && Slot == other.Slot
                && Field.Equals(other.Field) && CardMinAndFirst.Equals(other.CardMinAndFirst)
                && CardExtent.Equals(other.CardExtent);
        }

        private struct Leaf
        {
            public Bounds Bounds;
            public uint InstanceIndex, LayerMask;
        }

        private sealed class LeafComparer : IComparer<Leaf>
        {
            private readonly int m_Axis;

            public LeafComparer(int axis) => m_Axis = axis;

            public int Compare(Leaf a, Leaf b)
            {
                int order = a.Bounds.center[m_Axis].CompareTo(b.Bounds.center[m_Axis]);
                return order != 0 ? order : a.InstanceIndex.CompareTo(b.InstanceIndex);
            }
        }

        private static readonly LeafComparer[] s_LeafComparers = { new(0), new(1), new(2) };

        private readonly Dictionary<VividMeshSdfAsset, uint> m_FieldIndices = new();
        private List<FieldSource> m_Fields = new();
        private List<FieldSource> m_NextFields = new();
        private List<InstanceSource> m_Instances = new();
        private List<InstanceSource> m_NextInstances = new();
        private readonly List<Field> m_FieldUpload = new();
        private readonly List<uint> m_BrickPages = new();
        private readonly List<Instance> m_InstanceUpload = new();
        private readonly List<TraceInstance> m_TraceInstanceUpload = new();
        private readonly List<Leaf> m_Leaves = new();
        private readonly List<Node> m_Nodes = new();
        private readonly List<int> m_LeafSlotsByInstance = new();
        private readonly List<uint> m_LeafInstanceUpload = new();
        private readonly SahBin[] m_SahBins = new SahBin[SahBinCount];
        private readonly Bounds[] m_SahRightBounds = new Bounds[SahBinCount];
        private readonly int[] m_SahRightCounts = new int[SahBinCount];

        private GraphicsBuffer m_Voxels;
        private Texture3D m_BrickAtlas;
        private RTHandle m_BrickAtlasHandle, m_EmptyBrickAtlasHandle;
        private TextureHandle m_ImportedBrickAtlas;
        private Vector4 m_BrickAtlasSize;
        private GraphicsBuffer m_FieldBuffer;
        private GraphicsBuffer m_InstanceBuffer;
        private GraphicsBuffer m_TraceInstanceBuffer;
        private GraphicsBuffer m_NodeBuffer;
        private GraphicsBuffer m_LeafInstanceBuffer;
        private GraphicsBuffer m_DiffuseSurfaceBuffer;
        private GraphicsBuffer m_SurfaceLightingInstanceBuffer;
        private readonly List<uint> m_NextSurfaceLightingInstances;
        private readonly List<uint> m_RadiosityWork = new();
        private List<RadiosityCardIdentity> m_RadiosityIdentities = new();
        private List<RadiosityCardIdentity> m_NextRadiosityIdentities = new();
        private GraphicsBuffer m_RadiosityWorkBuffer;
        private VividSdfSurfaceCache m_RadiosityCache;
        private uint m_RadiosityRevision;
        private ulong m_RadiosityCardVersion;
        private int m_RadiosityCardLimit;
        private List<DiffuseSurface> m_DiffuseSurfaces;
        private List<DiffuseSurface> m_NextDiffuseSurfaces;
        private readonly Shader m_DiffuseShader;
        private readonly VividSdfSceneCacheSet m_SceneCaches;
        private long m_LastRejectedBytes;

        internal GraphicsBuffer Voxels => m_Voxels;
        internal GraphicsBuffer Fields => m_FieldBuffer;
        internal GraphicsBuffer Instances => m_InstanceBuffer;
        internal GraphicsBuffer Nodes => m_NodeBuffer;
        internal GraphicsBuffer LeafInstances => m_LeafInstanceBuffer;
        internal IReadOnlyList<Node> BvhNodes => m_Nodes;
        internal int NodeCount { get; private set; }
        internal uint Revision { get; private set; }
        internal GraphicsBuffer DiffuseSurfaces => m_DiffuseSurfaceBuffer;
        internal GraphicsBuffer SurfaceLightingInstances => m_SurfaceLightingInstanceBuffer;
        internal int SurfaceLightingInstanceCount { get; private set; }
        internal GraphicsBuffer RadiosityWork => m_RadiosityWorkBuffer;
        internal int RadiosityCardCount { get; private set; }
        internal uint RadiosityLayoutVersion { get; private set; }
        internal uint RadiosityDiscontinuityVersion { get; private set; }
        internal uint TransportVersion { get; private set; }
        internal uint NonMotionVersion { get; private set; }
        internal uint NonMotionTransportVersion { get; private set; }
        internal uint EmissionVersion { get; private set; }
        private Vector4 m_EmissionMaximum, m_PreviousEmissionMaximum;
        private bool m_LinearEmissionChange;
        internal bool HasEmissiveSurfaces { get; private set; }
        internal VividSdfSurfaceCache SurfaceCache => m_SceneCaches?.Cache;

        internal VividSdfSurfaceCache RefreshSurfaceCache() => m_SceneCaches?.Refresh(VividSdfSceneSurfaceCache.Active);

        internal void CopyInstanceIndices(Dictionary<ulong, uint> destination)
        {
            destination.Clear();
            for (int index = 0; index < m_Instances.Count; index++)
            {
                ulong entity = m_Instances[index].EntityId;
                if (!destination.TryAdd(entity, (uint)index))
                    destination[entity] = uint.MaxValue;
            }
        }

        internal VividMeshSdfGpuScene() { }

        internal readonly VividSparseMeshSdfAtlas SparseAtlas;
        internal VividMeshSdfGpuScene(VividSparseMeshSdfAtlas sparseAtlas) => SparseAtlas = sparseAtlas;

        internal VividMeshSdfGpuScene(Shader diffuseShader)
        {
            m_DiffuseShader = diffuseShader;
            m_SceneCaches = new VividSdfSceneCacheSet();
            m_DiffuseSurfaces = new List<DiffuseSurface>();
            m_NextDiffuseSurfaces = new List<DiffuseSurface>();
            m_NextSurfaceLightingInstances = new List<uint>();
        }

        internal bool PrepareBrickTexture(IRenderPass pass)
        {
            var handle = m_BrickAtlasHandle ?? (m_EmptyBrickAtlasHandle ??= RTHandles.Alloc(CoreUtils.blackVolumeTexture));
            m_ImportedBrickAtlas = PassRecorder.ImportTextureForPass(pass, handle);
            return m_ImportedBrickAtlas.IsValid();
        }

        internal void RegisterBrickTexture(IRenderPass pass) => PassRecorder.RegisterImportedTextureForPass(pass, m_ImportedBrickAtlas);

        internal void Bind(ComputeCommandBuffer cmd, ComputeShader shader, int kernel)
        {
            cmd.SetComputeBufferParam(shader, kernel, VoxelsId, m_Voxels);
            cmd.SetComputeTextureParam(shader, kernel, BrickAtlasId, m_ImportedBrickAtlas);
            cmd.SetComputeVectorParam(shader, BrickAtlasSizeId, m_BrickAtlasSize);
            cmd.SetComputeBufferParam(shader, kernel, FieldsId, m_FieldBuffer);
            cmd.SetComputeBufferParam(shader, kernel, InstancesId, m_InstanceBuffer);
            cmd.SetComputeBufferParam(shader, kernel, TraceInstancesId, m_TraceInstanceBuffer);
            cmd.SetComputeBufferParam(shader, kernel, LeafInstancesId, m_LeafInstanceBuffer);
            BindBvhNodes(cmd, shader, kernel);
        }

        internal static void BindFallback(
            ComputeCommandBuffer cmd,
            ComputeShader shader,
            int kernel,
            TextureHandle fallback3D,
            BufferHandle fallbackBuffer)
        {
            if (fallback3D.IsValid())
                cmd.SetComputeTextureParam(shader, kernel, BrickAtlasId, fallback3D);
            cmd.SetComputeVectorParam(shader, BrickAtlasSizeId, Vector4.zero);
            cmd.SetComputeIntParam(shader, NodeCountId, 0);
            if (fallbackBuffer.IsValid())
            {
                cmd.SetComputeBufferParam(shader, kernel, VoxelsId, fallbackBuffer);
                cmd.SetComputeBufferParam(shader, kernel, FieldsId, fallbackBuffer);
                cmd.SetComputeBufferParam(shader, kernel, InstancesId, fallbackBuffer);
                cmd.SetComputeBufferParam(shader, kernel, TraceInstancesId, fallbackBuffer);
                cmd.SetComputeBufferParam(shader, kernel, LeafInstancesId, fallbackBuffer);
                cmd.SetComputeBufferParam(shader, kernel, NodesId, fallbackBuffer);
            }
        }

        internal void BindBvhNodes(ComputeCommandBuffer cmd, ComputeShader shader, int kernel)
        {
            cmd.SetComputeBufferParam(shader, kernel, NodesId, m_NodeBuffer);
            cmd.SetComputeIntParam(shader, NodeCountId, NodeCount);
        }

        internal void Update(VividMeshletRendererDatabase database) => Update(database, Time.frameCount);

        internal void Update(VividMeshletRendererDatabase database, int frameIndex)
        {
            CollectSources(database);
            if (m_NextInstances.Count == 0)
            {
                if (m_Voxels != null)
                    ReleaseGeometry();
                return;
            }

            bool fieldsChanged = NodeCount == 0 || !SameSources(m_Fields, m_NextFields);
            bool sparseChanged = SparseAtlas?.End() == true;
            bool instancesChanged = fieldsChanged || sparseChanged || !SameSources(m_Instances, m_NextInstances);
            bool transformOnly = instancesChanged && !fieldsChanged && !sparseChanged && HasSameInstanceOrder();
            if (transformOnly)
            {
                for (int i = 0; i < m_Instances.Count; i++)
                    if (m_Instances[i].FieldIndex != m_NextInstances[i].FieldIndex || m_Instances[i].LayerMask != m_NextInstances[i].LayerMask)
                    {
                        transformOnly = false;
                        break;
                    }
            }
            bool smallTransform = transformOnly && HasSmallTransformChanges();
            if (instancesChanged && !UpdateGeometry(fieldsChanged, unchecked((uint)frameIndex)))
                return;

            bool surfacesChanged = m_NextDiffuseSurfaces != null
                && (m_DiffuseSurfaceBuffer == null || !SameSources(m_DiffuseSurfaces, m_NextDiffuseSurfaces));
            bool transportChanged = instancesChanged;
            bool nonMotionTransportChanged = instancesChanged && !transformOnly;
            if (surfacesChanged)
            {
                bool sameTransport = m_DiffuseSurfaces.Count == m_NextDiffuseSurfaces.Count;
                bool emissionChanged = !sameTransport;
                bool linearEmission = sameTransport;
                Vector4 emissionScale = Vector4.one;
                int emissionChannels = 0;
                m_PreviousEmissionMaximum = m_EmissionMaximum;
                m_EmissionMaximum = Vector4.zero;
                for (int i = 0; i < m_NextDiffuseSurfaces.Count; i++)
                {
                    var next = m_NextDiffuseSurfaces[i];
                    m_EmissionMaximum = Vector4.Max(m_EmissionMaximum,
                        new Vector4(next.Emission.x, next.Emission.y, next.Emission.z, 0f));
                    if (i >= m_DiffuseSurfaces.Count)
                        continue;
                    var old = m_DiffuseSurfaces[i];
                    sameTransport &= old.HasSameTransport(next);
                    emissionChanged |= !old.Emission.Equals(next.Emission);
                    linearEmission &= TryMergeEmissionScale(old.Emission, next.Emission, ref emissionScale, ref emissionChannels);
                }
                transportChanged |= !sameTransport;
                nonMotionTransportChanged |= !sameTransport;
                if (emissionChanged)
                {
                    EmissionVersion++;
                    m_LinearEmissionChange = linearEmission && sameTransport;
                }
                Upload(ref m_DiffuseSurfaceBuffer, m_NextDiffuseSurfaces, "Mesh SDF Diffuse Surfaces");
                SurfaceLightingInstanceCount = m_NextSurfaceLightingInstances.Count;
                if (SurfaceLightingInstanceCount != 0)
                    Upload(ref m_SurfaceLightingInstanceBuffer, m_NextSurfaceLightingInstances, "Mesh SDF Surface Lighting Instances");
                else
                {
                    m_SurfaceLightingInstanceBuffer?.Dispose();
                    m_SurfaceLightingInstanceBuffer = null;
                }
                (m_DiffuseSurfaces, m_NextDiffuseSurfaces) = (m_NextDiffuseSurfaces, m_DiffuseSurfaces);
                m_NextDiffuseSurfaces.Clear();
                HasEmissiveSurfaces = false;
                for (int i = 0; i < m_DiffuseSurfaces.Count; i++)
                {
                    Vector4 emission = m_DiffuseSurfaces[i].Emission;
                    // Non-finite/unknown emission is not a sun-only lighting source.
                    if (!(emission.x <= 0f && emission.y <= 0f && emission.z <= 0f))
                    {
                        HasEmissiveSurfaces = true;
                        break;
                    }
                }
            }
            if (instancesChanged || surfacesChanged)
            {
                Revision++;
                if (!transformOnly || surfacesChanged)
                    NonMotionVersion++;
                if (transportChanged)
                    TransportVersion++;
                if (nonMotionTransportChanged)
                    NonMotionTransportVersion++;
                if (!smallTransform || surfacesChanged)
                    RadiosityDiscontinuityVersion++;
            }
        }

        // Only emission may change. Albedo, coverage, card identity and geometry still
        // invalidate transport. A skipped emission revision cannot be reconstructed here.
        internal bool TryGetEmissionChange(uint previousVersion, out Vector4 previous, out Vector4 current)
        {
            current = m_EmissionMaximum;
            previous = previousVersion == EmissionVersion ? current : m_PreviousEmissionMaximum;
            return previousVersion == EmissionVersion
                || previousVersion + 1u == EmissionVersion && m_LinearEmissionChange;
        }

        internal static bool TryMergeEmissionScale(Vector4 previous, Vector4 current, ref Vector4 scale, ref int channels)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                float a = previous[channel], b = current[channel];
                if (!float.IsFinite(a) || !float.IsFinite(b) || a < 0f || b < 0f || a == 0f && b > 0f)
                    return false;
                if (a == 0f)
                    continue;
                float ratio = b / a;
                int bit = 1 << channel;
                if (!float.IsFinite(ratio) || ratio > 8f
                    || (channels & bit) != 0 && Mathf.Abs(ratio - scale[channel]) > 1e-5f * Mathf.Max(1f, ratio))
                    return false;
                if ((channels & bit) == 0)
                    scale[channel] = ratio;
                channels |= bit;
            }
            return true;
        }

        private bool HasSmallTransformChanges()
        {
            for (int i = 0; i < m_Instances.Count; i++)
            {
                var previous = m_Instances[i];
                var current = m_NextInstances[i];
                if (previous.LayerMask != current.LayerMask
                    || !IsSmallRadiosityTransformChange(previous.LocalToWorld, current.LocalToWorld))
                    return false;
            }
            return true;
        }

        internal static bool IsSmallRadiosityTransformChange(Matrix4x4 previous, Matrix4x4 current)
        {
            for (int column = 0; column < 3; column++)
                if (!previous.GetColumn(column).Equals(current.GetColumn(column)))
                    return false;
            return (previous.GetColumn(3) - current.GetColumn(3)).sqrMagnitude <= 0.0025f; // Five cm; unchanged basis.
        }

        private bool UpdateGeometry(bool fieldsChanged, uint motionFrame)
        {
            bool rebuildBvh = NodeCount == 0 || !HasSameInstanceOrder();
            // A failed or interrupted upload cannot leave the previous scene marked ready.
            NodeCount = 0;
            if (fieldsChanged && !UploadFields())
                return false;

            UpdateInstancesAndBvh(rebuildBvh, motionFrame);
            Upload(ref m_InstanceBuffer, m_InstanceUpload, "Mesh SDF Instances");
            Upload(ref m_TraceInstanceBuffer, m_TraceInstanceUpload, "Mesh SDF Trace Instances");
            Upload(ref m_NodeBuffer, m_Nodes, "Mesh SDF BVH");
            (m_Fields, m_NextFields) = (m_NextFields, m_Fields);
            (m_Instances, m_NextInstances) = (m_NextInstances, m_Instances);
            m_NextFields.Clear();
            m_NextInstances.Clear();
            NodeCount = m_Nodes.Count;
            return true;
        }

        internal void PrepareRadiosityCards(int limit)
        {
            var cache = SurfaceCache;
            ulong cardVersion = cache.CardVersion;
            if (ReferenceEquals(cache, m_RadiosityCache) && Revision == m_RadiosityRevision
                && cardVersion == m_RadiosityCardVersion && limit == m_RadiosityCardLimit)
                return;

            // The header maps runtime lighting instances to the first card in the
            // compact work list. Each work item stores an SDF instance and its card slot.
            int count = SurfaceLightingInstanceCount;
            m_RadiosityWork.Clear();
            m_NextRadiosityIdentities.Clear();
            m_RadiosityWork.Add((uint)(count + 1));
            for (int i = 0; i < count; i++)
                m_RadiosityWork.Add(uint.MaxValue);

            int cards = 0;
            for (int i = 0; i < count && cards < limit; i++)
            {
                uint instance = m_NextSurfaceLightingInstances[i];
                int first = (int)m_DiffuseSurfaces[(int)instance].CardMinAndFirst.w - 1;
                int slots = Mathf.Min(limit - cards, VividSdfSurfaceCache.CardsPerMesh + cache.GetLocalCardCount(first));
                m_RadiosityWork[i + 1] = (uint)cards;
                for (int slot = 0; slot < slots; slot++)
                {
                    m_RadiosityWork.Add(instance);
                    m_RadiosityWork.Add((uint)slot);
                    var source = m_Instances[(int)instance];
                    var surface = m_DiffuseSurfaces[(int)instance];
                    m_NextRadiosityIdentities.Add(new RadiosityCardIdentity
                    {
                        Entity = source.EntityId, Field = m_Fields[(int)source.FieldIndex], Slot = slot,
                        CardMinAndFirst = surface.CardMinAndFirst, CardExtent = surface.CardExtent
                    });
                }
                cards += slots;
            }

            if (cards != 0)
                Upload(ref m_RadiosityWorkBuffer, m_RadiosityWork, "Mesh SDF Radiosity Card Work");
            RadiosityCardCount = cards;
            // A transform or material edit may reuse old incident lighting temporarily.
            // A removed/reordered/rebaked card must never inherit another card's texels.
            if (!SameSources(m_RadiosityIdentities, m_NextRadiosityIdentities))
                RadiosityLayoutVersion++;
            (m_RadiosityIdentities, m_NextRadiosityIdentities) = (m_NextRadiosityIdentities, m_RadiosityIdentities);
            m_NextRadiosityIdentities.Clear();
            m_RadiosityCache = cache;
            m_RadiosityRevision = Revision;
            m_RadiosityCardVersion = cardVersion;
            m_RadiosityCardLimit = limit;
        }

        internal Bounds GetLightingCardBounds(int card)
        {
            int work = (int)m_RadiosityWork[0] + 2 * card;
            int instance = (int)m_RadiosityWork[work];
            int slot = (int)m_RadiosityWork[work + 1];
            var surface = m_DiffuseSurfaces[instance];
            Vector3 size = surface.CardExtent;
            Vector3 min = surface.CardMinAndFirst;
            Bounds local = slot < VividSdfSurfaceCache.CardsPerMesh
                ? new Bounds(min + 0.5f * size, size)
                : SurfaceCache.GetLocalCardBounds((int)surface.CardMinAndFirst.w - 1, slot);
            return VividMeshletRendererDatabase.TransformBounds(local, m_Instances[instance].LocalToWorld);
        }

        private void CollectSources(VividMeshletRendererDatabase database)
        {
            m_FieldIndices.Clear();
            m_NextFields.Clear();
            SparseAtlas?.Begin();
            m_NextInstances.Clear();
            m_NextDiffuseSurfaces?.Clear();
            m_NextSurfaceLightingInstances?.Clear();
            for (int i = 0; i < database.SdfRendererCount; i++)
            {
                database.GetSdfRenderer(i, out var data, out var renderer);
                if ((data.flags & VisibleFlags) != VisibleFlags)
                    continue;

                var asset = renderer != null ? renderer.meshSdf : null;
                if (asset == null)
                    continue;

                float determinant = data.objectToWorldMatrix.determinant;
                if (!float.IsFinite(determinant) || determinant == 0f)
                    continue;

                if (!m_FieldIndices.TryGetValue(asset, out uint fieldIndex))
                {
                    var texture = asset.DistanceField;
                    if (!texture.isReadable || texture.graphicsFormat != GraphicsFormat.R16_SFloat
                        || !float.IsFinite(asset.VoxelSize) || asset.VoxelSize <= 0f)
                        continue;

                    fieldIndex = (uint)m_NextFields.Count;
                    m_FieldIndices.Add(asset, fieldIndex);
                    var origin = asset.SampleOrigin;
                    m_NextFields.Add(new FieldSource
                    {
                        Texture = texture,
                        TextureVersion = texture.updateCount,
                        Layout = new Field
                        {
                            OriginAndVoxelSize = new Vector4(origin.x, origin.y, origin.z, asset.VoxelSize),
                            Width = (uint)texture.width,
                            Height = (uint)texture.height,
                            Depth = (uint)texture.depth,
                            SignMode = (uint)asset.SignMode,
                            ShellThickness = asset.ShellThickness,
                            MostlyTwoSided = asset.MostlyTwoSided ? 1u : 0u,
                        },
                    });
                }

                Bounds sparseBounds = default;
                if (SparseAtlas != null)
                {
                    var sparse = renderer.SparseDistanceField;
                    var bakeScale = renderer.SparseFieldBakeScale;
                    Vector3 center = sparse ? renderer.sourceMesh.bounds.center : default;
                    SparseAtlas.Add(sparse, bakeScale, center, data.objectToWorldMatrix);
                    if (sparse)
                    {
                        Vector3 undoBake = new(0.01f / bakeScale.x, 0.01f / bakeScale.y, 0.01f / bakeScale.z);
                        var offset = sparse.FieldBoundsCm.center - sparse.GeometryBoundsCm.center;
                        var size = sparse.FieldBoundsCm.size;
                        sparseBounds = new Bounds(center + Vector3.Scale(new Vector3(offset.y, offset.z, offset.x), undoBake),
                            Vector3.Scale(new Vector3(size.y, size.z, size.x), undoBake));
                    }
                }
                m_NextInstances.Add(new InstanceSource
                {
                    SparseBounds = sparseBounds,
                    LocalToWorld = data.objectToWorldMatrix,
                    WorldToLocal = data.worldToObjectMatrix,
                    FieldIndex = fieldIndex,
                    LayerMask = data.cameraLayerMask,
                    EntityId = EntityId.ToULong(data.meshletRendererEntityId),
                });
                if (m_NextDiffuseSurfaces != null)
                {
                    var surface = ReadDiffuseSurface(renderer);
                    if (surface.CardMinAndFirst.w != 0f)
                    {
                        surface.CardExtent.w = m_NextSurfaceLightingInstances.Count + 1;
                        m_NextSurfaceLightingInstances.Add((uint)(m_NextInstances.Count - 1));
                    }
                    m_NextDiffuseSurfaces.Add(surface);
                }
            }
        }

        private DiffuseSurface ReadDiffuseSurface(MeshletRenderer renderer)
        {
            var materials = renderer.sourceMaterials;
            if (m_DiffuseShader == null || materials.Count != 1)
                return default;
            var material = materials[0];
            if (material == null || material.shader != m_DiffuseShader
                || material.GetFloat(SurfaceId) != 0f || material.GetFloat(AlphaClipId) != 0f)
                return default;

            var baseColor = GPUDrivenMaterialCompiler.ConvertMaterialColorForGPU(material.GetColor(BaseColorId));
            var proxies = renderer.materialProxies;
            var proxy = proxies.Count > 0 ? proxies[0] : null;
            var emission = proxy != null && proxy.Model == GPUDrivenMaterialProxyModel.StandardLit && proxy.MaterialGraph == null
                ? GPUDrivenMaterialCompiler.ResolveStandardLitProxyEmission(proxy)
                : GPUDrivenMaterialCompiler.ConvertMaterialColorForGPU(
                    GPUDrivenMaterialCompiler.ResolveStandardLitEmissionColor(material, out _));
            float diffuse = 1f - Mathf.Clamp01(material.GetFloat(MetallicId));
            if (!math.all(math.isfinite(baseColor)) || !math.all(math.isfinite(emission)) || !float.IsFinite(diffuse))
                return default;
            Bounds bounds = default;
            int firstCard = 0;
            bool originalNormals = false;
            if (SurfaceCache != null && !m_SceneCaches.TryGetCards(renderer.gameObject.scene, renderer.sourceMesh, material,
                    out bounds, out firstCard, out originalNormals))
                return default;

            var surface = new DiffuseSurface
            {
                AlbedoAndValid = new Vector4(baseColor.x * diffuse, baseColor.y * diffuse, baseColor.z * diffuse, 1f),
                // Legacy cards discarded original facing. Retain their old two-sided
                // behavior until explicitly recaptured instead of inventing a side.
                Emission = new Vector4(emission.x, emission.y, emission.z,
                    originalNormals
                        ? (float)(proxy != null ? proxy.CullMode : material.HasProperty(CullId)
                            ? (CullMode)Mathf.RoundToInt(material.GetFloat(CullId)) : CullMode.Back)
                        : (float)CullMode.Off),
            };
#if UNITY_EDITOR
            if (EditorForceTwoSidedEmission) surface.Emission.w = (float)CullMode.Off;
#endif
            if (SurfaceCache == null)
                return surface;

            var min = bounds.min;
            var size = bounds.size;
            surface.CardMinAndFirst = new Vector4(min.x, min.y, min.z, firstCard + 1);
            surface.CardExtent = new Vector4(size.x, size.y, size.z, 0f);
            return surface;
        }

        private bool UploadFields()
        {
            if (TryUploadBrickAtlas())
                return true;
            ReleaseBrickAtlas();
            m_FieldUpload.Clear();
            m_BrickPages.Clear();
            long payloadWords = 0;
            foreach (var source in m_NextFields)
            {
                var field = source.Layout;
                var dimensions = new int3((int)field.Width, (int)field.Height, (int)field.Depth);
                field.WordOffset = (uint)payloadWords;
                payloadWords += VividMeshSdfBricks.Plan(source.Texture.GetPixelData<ushort>(0), dimensions,
                    field.OriginAndVoxelSize.w, m_BrickPages, out field.PageOffset);
                m_FieldUpload.Add(field);
            }

            long wordCount = m_BrickPages.Count + payloadWords;
            long bytes = wordCount * sizeof(uint);
            long deviceLimit = SystemInfo.maxGraphicsBufferSize;
            // The ushort view of the staging array must also fit in an int-sized NativeArray.
            if (wordCount > int.MaxValue / 2 || (deviceLimit > 0 && bytes > deviceLimit))
            {
                if (m_LastRejectedBytes != bytes)
                    Debug.LogError($"[VividRP] The Mesh SDF voxel buffer requires {bytes} bytes and exceeds the supported buffer size.");
                m_LastRejectedBytes = bytes;
                return false;
            }
            m_LastRejectedBytes = 0;

            using var words = new NativeArray<uint>((int)wordCount, Allocator.TempJob);
            var texels = words.Reinterpret<ushort>(sizeof(uint));
            var directory = words.GetSubArray(0, m_BrickPages.Count);
            for (int i = 0; i < m_BrickPages.Count; i++)
                directory[i] = m_BrickPages[i];

            for (int i = 0; i < m_NextFields.Count; i++)
            {
                var field = m_FieldUpload[i];
                field.WordOffset += (uint)m_BrickPages.Count;
                var dimensions = new int3((int)field.Width, (int)field.Height, (int)field.Depth);
                VividMeshSdfBricks.Write(m_NextFields[i].Texture.GetPixelData<ushort>(0), dimensions, m_BrickPages,
                    field.PageOffset, texels, 2 * (int)field.WordOffset);
                m_FieldUpload[i] = field;
            }

            EnsureBuffer(ref m_Voxels, words.Length, sizeof(uint), "Mesh SDF Voxels");
            m_Voxels.SetData(words, 0, 0, words.Length);
            Upload(ref m_FieldBuffer, m_FieldUpload, "Mesh SDF Fields");
            return true;
        }

        private bool TryUploadBrickAtlas()
        {
            const GraphicsFormat format = GraphicsFormat.R16_SFloat;
            if (!SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Sample | GraphicsFormatUsage.Linear))
                return false;

            m_FieldUpload.Clear();
            m_BrickPages.Clear();
            int slots = 0;
            foreach (var source in m_NextFields)
            {
                var field = source.Layout;
                var size = new int3((int)field.Width, (int)field.Height, (int)field.Depth);
                if (math.any(size < 2))
                    return false;

                field.PageOffset = VividMeshSdfBricks.PlanAtlas(source.Texture.GetPixelData<ushort>(0), size,
                    field.OriginAndVoxelSize.w, m_BrickPages, ref slots);
                m_FieldUpload.Add(field);
            }

            if (!VividMeshSdfBricks.TryGetAtlasLayout(slots, SystemInfo.maxTexture3DSize, out var layout))
                return false;
            int3 dimensions = layout * VividMeshSdfBricks.StoredSize;
            using var texels = new NativeArray<ushort>(dimensions.x * dimensions.y * dimensions.z, Allocator.TempJob);
            for (int i = 0; i < m_FieldUpload.Count; i++)
            {
                var field = m_FieldUpload[i];
                VividMeshSdfBricks.WriteAtlas(m_NextFields[i].Texture.GetPixelData<ushort>(0),
                    new int3((int)field.Width, (int)field.Height, (int)field.Depth),
                    m_BrickPages, field.PageOffset, texels, layout.x);
            }

            var atlas = new Texture3D(dimensions.x, dimensions.y, dimensions.z, format, TextureCreationFlags.None)
                { name = "Mesh SDF Brick Atlas", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            try
            {
                atlas.SetPixelData(texels, 0);
                atlas.Apply(false, true);
                if (m_BrickAtlas == null)
                {
                    m_Voxels?.Dispose();
                    m_Voxels = null;
                }
                Upload(ref m_Voxels, m_BrickPages, "Mesh SDF Brick Pages");
                Upload(ref m_FieldBuffer, m_FieldUpload, "Mesh SDF Fields");
            }
            catch
            {
                CoreUtils.Destroy(atlas);
                throw;
            }
            ReleaseBrickAtlas();
            m_BrickAtlas = atlas;
            m_BrickAtlasHandle = RTHandles.Alloc(atlas);
            m_BrickAtlasSize = new Vector4(1f / dimensions.x, 1f / dimensions.y, 1f / dimensions.z, layout.x);
            m_LastRejectedBytes = 0;
            return true;
        }

        private void ReleaseBrickAtlas()
        {
            m_BrickAtlasHandle?.Release();
            m_BrickAtlasHandle = null;
            CoreUtils.Destroy(m_BrickAtlas);
            m_BrickAtlas = null;
            m_BrickAtlasSize = default;
            m_ImportedBrickAtlas = default;
        }

        private bool HasSameInstanceOrder()
        {
            if (m_Instances.Count != m_NextInstances.Count)
                return false;
            for (int i = 0; i < m_Instances.Count; i++)
                if (m_Instances[i].EntityId != m_NextInstances[i].EntityId)
                    return false;
            return true;
        }

        private void UpdateInstancesAndBvh(bool rebuildBvh, uint motionFrame)
        {
            if (rebuildBvh)
            {
                m_Leaves.Clear();
                m_Nodes.Clear();
                m_LeafSlotsByInstance.Clear();
                m_LeafInstanceUpload.Clear();
            }

            for (int i = 0; i < m_NextInstances.Count; i++)
            {
                var source = m_NextInstances[i];
                var field = m_NextFields[(int)source.FieldIndex].Layout;
                Vector3 origin = field.OriginAndVoxelSize;
                var extent = new Vector3(field.Width - 1, field.Height - 1, field.Depth - 1) * field.OriginAndVoxelSize.w;
                var localBounds = new Bounds(origin + extent * 0.5f, extent);
                if (source.SparseBounds.size != Vector3.zero)
                    localBounds.Encapsulate(source.SparseBounds);
                var bounds = VividMeshletRendererDatabase.TransformBounds(localBounds, source.LocalToWorld);
                var leaf = new Leaf { Bounds = bounds, InstanceIndex = (uint)i, LayerMask = source.LayerMask };
                if (rebuildBvh)
                {
                    m_Leaves.Add(leaf);
                    m_LeafSlotsByInstance.Add(0);
                }
                else
                {
                    m_Leaves[m_LeafSlotsByInstance[i]] = leaf;
                }

                bool sameEntity = i < m_Instances.Count && m_Instances[i].EntityId == source.EntityId;
                Instance previous = sameEntity ? m_InstanceUpload[i] : default;
                bool moved = sameEntity && !source.LocalToWorld.Equals(m_Instances[i].LocalToWorld);
                if (moved && previous.MotionFrame != motionFrame)
                {
                    var previousTransform = m_Instances[i].LocalToWorld;
                    previous.PreviousLocalToWorld0 = previousTransform.GetRow(0);
                    previous.PreviousLocalToWorld1 = previousTransform.GetRow(1);
                    previous.PreviousLocalToWorld2 = previousTransform.GetRow(2);
                    previous.MotionFrame = motionFrame;
                }
                var instance = new Instance
                {
                    MotionFrame = sameEntity ? previous.MotionFrame : uint.MaxValue,
                    PreviousLocalToWorld0 = previous.PreviousLocalToWorld0,
                    PreviousLocalToWorld1 = previous.PreviousLocalToWorld1,
                    PreviousLocalToWorld2 = previous.PreviousLocalToWorld2,
                    WorldToLocal0 = source.WorldToLocal.GetRow(0),
                    WorldToLocal1 = source.WorldToLocal.GetRow(1),
                    WorldToLocal2 = source.WorldToLocal.GetRow(2),
                    FieldIndex = source.FieldIndex,
                    LayerMask = source.LayerMask,
                    EntityIdLow = (uint)source.EntityId,
                    EntityIdHigh = (uint)(source.EntityId >> 32),
                    DistanceScale = CalculateDistanceScale(source.WorldToLocal),
                };
                var traceInstance = new TraceInstance
                {
                    WorldToLocal0 = instance.WorldToLocal0,
                    WorldToLocal1 = instance.WorldToLocal1,
                    WorldToLocal2 = instance.WorldToLocal2,
                    FieldIndex = instance.FieldIndex,
                    LayerMask = instance.LayerMask,
                    EntityIdLow = instance.EntityIdLow,
                    EntityIdHigh = instance.EntityIdHigh,
                };
                if (i < m_InstanceUpload.Count)
                {
                    m_InstanceUpload[i] = instance;
                    m_TraceInstanceUpload[i] = traceInstance;
                }
                else
                {
                    m_InstanceUpload.Add(instance);
                    m_TraceInstanceUpload.Add(traceInstance);
                }
            }
            if (m_InstanceUpload.Count > m_NextInstances.Count)
            {
                int removeCount = m_InstanceUpload.Count - m_NextInstances.Count;
                m_InstanceUpload.RemoveRange(m_NextInstances.Count, removeCount);
                m_TraceInstanceUpload.RemoveRange(m_NextInstances.Count, removeCount);
            }

            if (rebuildBvh)
            {
                BuildNode(0, m_Leaves.Count);
                for (int i = 0; i < m_Leaves.Count; i++)
                {
                    uint instanceIndex = m_Leaves[i].InstanceIndex;
                    m_LeafSlotsByInstance[(int)instanceIndex] = i;
                    m_LeafInstanceUpload.Add(instanceIndex);
                }
                Upload(ref m_LeafInstanceBuffer, m_LeafInstanceUpload, "Mesh SDF Leaf Instances");
            }
            else
                RefitBvh();
        }

        private static float CalculateDistanceScale(Matrix4x4 worldToLocal)
        {
            var x = new Vector3(worldToLocal.m00, worldToLocal.m01, worldToLocal.m02);
            var y = new Vector3(worldToLocal.m10, worldToLocal.m11, worldToLocal.m12);
            var z = new Vector3(worldToLocal.m20, worldToLocal.m21, worldToLocal.m22);
            float xy = Mathf.Abs(Vector3.Dot(x, y));
            float xz = Mathf.Abs(Vector3.Dot(x, z));
            float yz = Mathf.Abs(Vector3.Dot(y, z));
            // The largest absolute row sum of A * transpose(A) bounds the squared stretch of A.
            float maxStretchSquared = Mathf.Max(x.sqrMagnitude + xy + xz,
                Mathf.Max(y.sqrMagnitude + xy + yz, z.sqrMagnitude + xz + yz));
            return 1f / Mathf.Sqrt(maxStretchSquared);
        }

        private static float SurfaceArea(Bounds bounds)
        {
            var size = bounds.size;
            return size.x * size.y + size.y * size.z + size.z * size.x;
        }

        private void BuildNode(int start, int count)
        {
            var bounds = m_Leaves[start].Bounds;
            var centers = new Bounds(bounds.center, Vector3.zero);
            uint layerMask = m_Leaves[start].LayerMask;
            for (int i = start + 1; i < start + count; i++)
            {
                bounds.Encapsulate(m_Leaves[i].Bounds);
                centers.Encapsulate(m_Leaves[i].Bounds.center);
                layerMask |= m_Leaves[i].LayerMask;
            }

            int index = m_Nodes.Count;
            m_Nodes.Add(default);
            bool isLeaf = count <= MaxLeafInstances;
            if (!isLeaf)
            {
                int leftCount = PartitionSah(start, count, centers);
                BuildNode(start, leftCount);
                BuildNode(start + leftCount, count - leftCount);
            }

            m_Nodes[index] = new Node
            {
                Min = bounds.min,
                Max = bounds.max,
                FirstLeaf = (uint)start,
                EscapeIndex = (uint)m_Nodes.Count,
                LeafCount = isLeaf ? (uint)count : 0u,
                LayerMask = layerMask,
            };
        }

        private int PartitionSah(int start, int count, Bounds centers)
        {
            var extent = centers.size;
            int bestAxis = -1;
            float bestSplit = 0f;
            float bestCost = float.PositiveInfinity;

            for (int axis = 0; axis < 3; axis++)
            {
                float axisExtent = extent[axis];
                if (axisExtent <= 1e-5f)
                    continue;

                for (int b = 0; b < SahBinCount; b++)
                    m_SahBins[b] = default;

                float minCenter = centers.min[axis];
                float scale = SahBinCount / axisExtent;
                for (int i = start; i < start + count; i++)
                {
                    var leafBounds = m_Leaves[i].Bounds;
                    int bin = Mathf.Clamp((int)((leafBounds.center[axis] - minCenter) * scale), 0, SahBinCount - 1);
                    if (m_SahBins[bin].Count == 0)
                        m_SahBins[bin].Bounds = leafBounds;
                    else
                        m_SahBins[bin].Bounds.Encapsulate(leafBounds);
                    m_SahBins[bin].Count++;
                }

                Bounds rightBounds = default;
                int rightCount = 0;
                for (int b = SahBinCount - 1; b > 0; b--)
                {
                    if (m_SahBins[b].Count > 0)
                    {
                        if (rightCount == 0)
                            rightBounds = m_SahBins[b].Bounds;
                        else
                            rightBounds.Encapsulate(m_SahBins[b].Bounds);
                        rightCount += m_SahBins[b].Count;
                    }
                    m_SahRightBounds[b] = rightBounds;
                    m_SahRightCounts[b] = rightCount;
                }

                Bounds leftBounds = default;
                int leftCount = 0;
                for (int b = 0; b < SahBinCount - 1; b++)
                {
                    if (m_SahBins[b].Count > 0)
                    {
                        if (leftCount == 0)
                            leftBounds = m_SahBins[b].Bounds;
                        else
                            leftBounds.Encapsulate(m_SahBins[b].Bounds);
                        leftCount += m_SahBins[b].Count;
                    }
                    int rc = m_SahRightCounts[b + 1];
                    if (leftCount == 0 || rc == 0)
                        continue;

                    float cost = leftCount * SurfaceArea(leftBounds) + rc * SurfaceArea(m_SahRightBounds[b + 1]);
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestAxis = axis;
                        bestSplit = minCenter + (b + 1) * (axisExtent / SahBinCount);
                    }
                }
            }

            if (bestAxis >= 0)
            {
                int i = start;
                int j = start + count - 1;
                while (i <= j)
                {
                    if (m_Leaves[i].Bounds.center[bestAxis] < bestSplit)
                    {
                        i++;
                    }
                    else
                    {
                        (m_Leaves[i], m_Leaves[j]) = (m_Leaves[j], m_Leaves[i]);
                        j--;
                    }
                }
                int splitCount = i - start;
                if (splitCount > 0 && splitCount < count)
                    return splitCount;
            }

            int fallbackAxis = extent.x >= extent.y && extent.x >= extent.z ? 0 : extent.y >= extent.z ? 1 : 2;
            m_Leaves.Sort(start, count, s_LeafComparers[fallbackAxis]);
            return count / 2;
        }

        private void RefitBvh()
        {
            // Children follow their parent in preorder, so they are up to date in this reverse walk.
            for (int i = m_Nodes.Count - 1; i >= 0; i--)
            {
                var node = m_Nodes[i];
                if (node.LeafCount != 0)
                {
                    int start = (int)node.FirstLeaf;
                    var bounds = m_Leaves[start].Bounds;
                    node.LayerMask = m_Leaves[start].LayerMask;
                    int end = start + (int)node.LeafCount;
                    for (int j = start + 1; j < end; j++)
                    {
                        bounds.Encapsulate(m_Leaves[j].Bounds);
                        node.LayerMask |= m_Leaves[j].LayerMask;
                    }
                    node.Min = bounds.min;
                    node.Max = bounds.max;
                }
                else
                {
                    var left = m_Nodes[i + 1];
                    var right = m_Nodes[(int)left.EscapeIndex];
                    node.Min = Vector3.Min(left.Min, right.Min);
                    node.Max = Vector3.Max(left.Max, right.Max);
                    node.LayerMask = left.LayerMask | right.LayerMask;
                }
                m_Nodes[i] = node;
            }
        }

        private static bool SameSources<T>(List<T> previous, List<T> next) where T : struct, IEquatable<T>
        {
            if (previous.Count != next.Count)
                return false;
            for (int i = 0; i < previous.Count; i++)
                if (!previous[i].Equals(next[i]))
                    return false;
            return true;
        }

        private static void Upload<T>(ref GraphicsBuffer buffer, List<T> data, string name) where T : unmanaged
        {
            EnsureBuffer(ref buffer, data.Count, UnsafeUtility.SizeOf<T>(), name);
            buffer.SetData(data, 0, 0, data.Count);
        }

        private static void EnsureBuffer(ref GraphicsBuffer buffer, int count, int stride, string name)
        {
            if (buffer != null && buffer.count >= count)
                return;
            var replacement = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, stride) { name = name };
            buffer?.Dispose();
            buffer = replacement;
        }

        public void Dispose()
        {
            ReleaseGeometry();
            m_SceneCaches?.Dispose();
        }

        private void ReleaseGeometry()
        {
            SparseAtlas?.Dispose();
            ReleaseBrickAtlas();
            m_EmptyBrickAtlasHandle?.Release();
            m_EmptyBrickAtlasHandle = null;
            m_Voxels?.Dispose();
            m_FieldBuffer?.Dispose();
            m_InstanceBuffer?.Dispose();
            m_TraceInstanceBuffer?.Dispose();
            m_NodeBuffer?.Dispose();
            m_LeafInstanceBuffer?.Dispose();
            m_DiffuseSurfaceBuffer?.Dispose();
            m_SurfaceLightingInstanceBuffer?.Dispose();
            m_RadiosityWorkBuffer?.Dispose();
            m_RadiosityWorkBuffer = null;
            m_RadiosityCache = null;
            m_RadiosityIdentities.Clear();
            m_NextRadiosityIdentities.Clear();
            RadiosityLayoutVersion++;
            RadiosityDiscontinuityVersion++;
            TransportVersion++;
            NonMotionVersion++;
            NonMotionTransportVersion++;
            EmissionVersion++;
            RadiosityCardCount = 0;
            m_SurfaceLightingInstanceBuffer = null;
            m_DiffuseSurfaceBuffer = null;
            SurfaceLightingInstanceCount = 0;
            HasEmissiveSurfaces = false;
            m_EmissionMaximum = m_PreviousEmissionMaximum = Vector4.zero;
            m_LinearEmissionChange = false;
            m_Voxels = m_FieldBuffer = m_InstanceBuffer = m_TraceInstanceBuffer = m_NodeBuffer = m_LeafInstanceBuffer = null;
            NodeCount = 0;
            m_FieldIndices.Clear();
            m_Fields.Clear();
            m_NextFields.Clear();
            m_Instances.Clear();
            m_NextInstances.Clear();
            m_DiffuseSurfaces?.Clear();
            m_NextDiffuseSurfaces?.Clear();
            m_NextSurfaceLightingInstances?.Clear();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven.Meshlets
{
    internal sealed class VividMeshSdfGpuScene : IDisposable
    {
        private static readonly int VoxelsId = Shader.PropertyToID("_MeshSdfVoxels");
        private static readonly int FieldsId = Shader.PropertyToID("_MeshSdfFields");
        private static readonly int InstancesId = Shader.PropertyToID("_MeshSdfInstances");
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
            public uint Padding;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Instance
        {
            public Vector4 WorldToLocal0, WorldToLocal1, WorldToLocal2;
            public uint FieldIndex, LayerMask, EntityIdLow, EntityIdHigh;
            public float DistanceScale;
            public uint Padding0, Padding1, Padding2;
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
            public Vector4 Emission;
            public Vector4 CardMinAndFirst;
            public Vector4 CardExtent; // w is the 1-based runtime lighting instance slot.

            public readonly bool Equals(DiffuseSurface other) =>
                AlbedoAndValid.Equals(other.AlbedoAndValid) && Emission.Equals(other.Emission)
                && CardMinAndFirst.Equals(other.CardMinAndFirst) && CardExtent.Equals(other.CardExtent);
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");

        private const int MaxLeafInstances = 4;
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
                && Layout.ShellThickness.Equals(other.Layout.ShellThickness);
        }

        private struct InstanceSource : IEquatable<InstanceSource>
        {
            public Matrix4x4 LocalToWorld, WorldToLocal;
            public ulong EntityId;
            public uint FieldIndex, LayerMask;

            public readonly bool Equals(InstanceSource other) =>
                EntityId == other.EntityId && FieldIndex == other.FieldIndex && LayerMask == other.LayerMask
                && LocalToWorld.Equals(other.LocalToWorld) && WorldToLocal.Equals(other.WorldToLocal);
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
        private readonly List<Leaf> m_Leaves = new();
        private readonly List<Node> m_Nodes = new();
        private readonly List<int> m_LeafSlotsByInstance = new();
        private readonly List<uint> m_LeafInstanceUpload = new();

        private GraphicsBuffer m_Voxels;
        private GraphicsBuffer m_FieldBuffer;
        private GraphicsBuffer m_InstanceBuffer;
        private GraphicsBuffer m_NodeBuffer;
        private GraphicsBuffer m_LeafInstanceBuffer;
        private GraphicsBuffer m_DiffuseSurfaceBuffer;
        private GraphicsBuffer m_SurfaceLightingInstanceBuffer;
        private readonly List<uint> m_NextSurfaceLightingInstances;
        private readonly List<uint> m_RadiosityWork = new();
        private GraphicsBuffer m_RadiosityWorkBuffer;
        private VividSdfSurfaceCache m_RadiosityCache;
        private uint m_RadiosityRevision;
        private ulong m_RadiosityCardVersion;
        private int m_RadiosityCardLimit;
        private List<DiffuseSurface> m_DiffuseSurfaces;
        private List<DiffuseSurface> m_NextDiffuseSurfaces;
        private readonly Shader m_DiffuseShader;
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
        internal VividSdfSurfaceCache SurfaceCache { get; set; }

        internal VividMeshSdfGpuScene() { }

        internal VividMeshSdfGpuScene(Shader diffuseShader)
        {
            m_DiffuseShader = diffuseShader;
            m_DiffuseSurfaces = new List<DiffuseSurface>();
            m_NextDiffuseSurfaces = new List<DiffuseSurface>();
            m_NextSurfaceLightingInstances = new List<uint>();
        }

        internal void Bind(ComputeCommandBuffer cmd, ComputeShader shader, int kernel)
        {
            cmd.SetComputeBufferParam(shader, kernel, VoxelsId, m_Voxels);
            cmd.SetComputeBufferParam(shader, kernel, FieldsId, m_FieldBuffer);
            cmd.SetComputeBufferParam(shader, kernel, InstancesId, m_InstanceBuffer);
            cmd.SetComputeBufferParam(shader, kernel, NodesId, m_NodeBuffer);
            cmd.SetComputeBufferParam(shader, kernel, LeafInstancesId, m_LeafInstanceBuffer);
            cmd.SetComputeIntParam(shader, NodeCountId, NodeCount);
        }

        internal void Update(VividMeshletRendererDatabase database)
        {
            CollectSources(database);
            if (m_NextInstances.Count == 0)
            {
                if (m_Voxels != null)
                    Dispose();
                return;
            }

            bool fieldsChanged = NodeCount == 0 || !SameSources(m_Fields, m_NextFields);
            bool instancesChanged = fieldsChanged || !SameSources(m_Instances, m_NextInstances);
            if (instancesChanged && !UpdateGeometry(fieldsChanged))
                return;

            bool surfacesChanged = m_NextDiffuseSurfaces != null
                && (m_DiffuseSurfaceBuffer == null || !SameSources(m_DiffuseSurfaces, m_NextDiffuseSurfaces));
            if (surfacesChanged)
            {
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
            }
            if (instancesChanged || surfacesChanged)
                Revision++;
        }

        private bool UpdateGeometry(bool fieldsChanged)
        {
            bool rebuildBvh = NodeCount == 0 || !HasSameInstanceOrder();
            // A failed or interrupted upload cannot leave the previous scene marked ready.
            NodeCount = 0;
            if (fieldsChanged && !UploadFields())
                return false;

            UpdateInstancesAndBvh(rebuildBvh);
            Upload(ref m_InstanceBuffer, m_InstanceUpload, "Mesh SDF Instances");
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
                }
                cards += slots;
            }

            if (cards != 0)
                Upload(ref m_RadiosityWorkBuffer, m_RadiosityWork, "Mesh SDF Radiosity Card Work");
            RadiosityCardCount = cards;
            m_RadiosityCache = cache;
            m_RadiosityRevision = Revision;
            m_RadiosityCardVersion = cardVersion;
            m_RadiosityCardLimit = limit;
        }

        private void CollectSources(VividMeshletRendererDatabase database)
        {
            m_FieldIndices.Clear();
            m_NextFields.Clear();
            m_NextInstances.Clear();
            m_NextDiffuseSurfaces?.Clear();
            m_NextSurfaceLightingInstances?.Clear();
            var rendererData = database.rendererData;
            var resources = database.rendererResources;
            for (int i = 0; i < rendererData.Count; i++)
            {
                var data = rendererData[i];
                if ((data.flags & VisibleFlags) != VisibleFlags)
                    continue;

                var renderer = resources[i].MeshletRenderer;
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
                        },
                    });
                }

                m_NextInstances.Add(new InstanceSource
                {
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
            var surface = new DiffuseSurface
            {
                AlbedoAndValid = new Vector4(baseColor.x * diffuse, baseColor.y * diffuse, baseColor.z * diffuse, 1f),
                Emission = new Vector4(emission.x, emission.y, emission.z, 0f),
            };
            if (SurfaceCache == null)
                return surface;
            if (!SurfaceCache.TryGetCards(renderer.sourceMesh, material, out var bounds, out int firstCard))
                return default;

            var min = bounds.min;
            var size = bounds.size;
            surface.CardMinAndFirst = new Vector4(min.x, min.y, min.z, firstCard + 1);
            surface.CardExtent = new Vector4(size.x, size.y, size.z, 0f);
            return surface;
        }

        private bool UploadFields()
        {
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

        private bool HasSameInstanceOrder()
        {
            if (m_Instances.Count != m_NextInstances.Count)
                return false;
            for (int i = 0; i < m_Instances.Count; i++)
                if (m_Instances[i].EntityId != m_NextInstances[i].EntityId)
                    return false;
            return true;
        }

        private void UpdateInstancesAndBvh(bool rebuildBvh)
        {
            m_InstanceUpload.Clear();
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

                m_InstanceUpload.Add(new Instance
                {
                    WorldToLocal0 = source.WorldToLocal.GetRow(0),
                    WorldToLocal1 = source.WorldToLocal.GetRow(1),
                    WorldToLocal2 = source.WorldToLocal.GetRow(2),
                    FieldIndex = source.FieldIndex,
                    LayerMask = source.LayerMask,
                    EntityIdLow = (uint)source.EntityId,
                    EntityIdHigh = (uint)(source.EntityId >> 32),
                    DistanceScale = CalculateDistanceScale(source.WorldToLocal),
                });
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
                var size = centers.size;
                int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                m_Leaves.Sort(start, count, s_LeafComparers[axis]);
                int leftCount = count / 2;
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
            m_Voxels?.Dispose();
            m_FieldBuffer?.Dispose();
            m_InstanceBuffer?.Dispose();
            m_NodeBuffer?.Dispose();
            m_LeafInstanceBuffer?.Dispose();
            m_DiffuseSurfaceBuffer?.Dispose();
            m_SurfaceLightingInstanceBuffer?.Dispose();
            m_RadiosityWorkBuffer?.Dispose();
            m_RadiosityWorkBuffer = null;
            m_RadiosityCache = null;
            RadiosityCardCount = 0;
            m_SurfaceLightingInstanceBuffer = null;
            m_DiffuseSurfaceBuffer = null;
            SurfaceLightingInstanceCount = 0;
            m_Voxels = m_FieldBuffer = m_InstanceBuffer = m_NodeBuffer = m_LeafInstanceBuffer = null;
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

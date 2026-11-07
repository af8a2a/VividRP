using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace VividRP.Runtime.GPUDriven.Meshlets
{
    internal sealed class VividMeshSdfGpuScene : IDisposable
    {
        // Keep these layouts in sync with MeshSdfTrace.hlsl.
        [StructLayout(LayoutKind.Sequential)]
        internal struct Field
        {
            public Vector4 OriginAndVoxelSize;
            public uint Width, Height, Depth, WordOffset;
            public uint SignMode, Padding0, Padding1, Padding2;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Instance
        {
            public Vector4 WorldToLocal0, WorldToLocal1, WorldToLocal2;
            public uint FieldIndex, LayerMask, EntityIdLow, EntityIdHigh;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Node
        {
            public Vector3 Min;
            public uint InstanceIndex;
            public Vector3 Max;
            public uint EscapeIndex;
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
                && Layout.SignMode == other.Layout.SignMode;
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
            public uint InstanceIndex;
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
        private readonly List<Instance> m_InstanceUpload = new();
        private readonly List<Leaf> m_Leaves = new();
        private readonly List<Node> m_Nodes = new();
        private readonly List<int> m_LeafNodeIndices = new();

        private GraphicsBuffer m_Voxels;
        private GraphicsBuffer m_FieldBuffer;
        private GraphicsBuffer m_InstanceBuffer;
        private GraphicsBuffer m_NodeBuffer;
        private long m_LastRejectedBytes;

        internal GraphicsBuffer Voxels => m_Voxels;
        internal GraphicsBuffer Fields => m_FieldBuffer;
        internal GraphicsBuffer Instances => m_InstanceBuffer;
        internal GraphicsBuffer Nodes => m_NodeBuffer;
        internal int NodeCount { get; private set; }

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
            if (!fieldsChanged && SameSources(m_Instances, m_NextInstances))
                return;

            bool rebuildBvh = NodeCount == 0 || !HasSameInstanceOrder();
            // A failed or interrupted upload cannot leave the previous scene marked ready.
            NodeCount = 0;
            if (fieldsChanged && !UploadFields())
                return;

            UpdateInstancesAndBvh(rebuildBvh);
            Upload(ref m_InstanceBuffer, m_InstanceUpload, "Mesh SDF Instances");
            Upload(ref m_NodeBuffer, m_Nodes, "Mesh SDF BVH");
            (m_Fields, m_NextFields) = (m_NextFields, m_Fields);
            (m_Instances, m_NextInstances) = (m_NextInstances, m_Instances);
            m_NextFields.Clear();
            m_NextInstances.Clear();
            NodeCount = m_Nodes.Count;
        }

        private void CollectSources(VividMeshletRendererDatabase database)
        {
            m_FieldIndices.Clear();
            m_NextFields.Clear();
            m_NextInstances.Clear();
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
            }
        }

        private bool UploadFields()
        {
            long wordCount = 0;
            foreach (var source in m_NextFields)
            {
                var field = source.Layout;
                long texelCount = (long)field.Width * field.Height * field.Depth;
                wordCount += (texelCount + 1) / 2;
            }

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
            m_FieldUpload.Clear();
            int wordOffset = 0;
            foreach (var source in m_NextFields)
            {
                var field = source.Layout;
                var samples = source.Texture.GetPixelData<ushort>(0);
                NativeArray<ushort>.Copy(samples, 0, texels, wordOffset * 2, samples.Length);
                field.WordOffset = (uint)wordOffset;
                m_FieldUpload.Add(field);
                wordOffset += (samples.Length + 1) / 2;
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
                m_LeafNodeIndices.Clear();
            }

            for (int i = 0; i < m_NextInstances.Count; i++)
            {
                var source = m_NextInstances[i];
                var field = m_NextFields[(int)source.FieldIndex].Layout;
                Vector3 origin = field.OriginAndVoxelSize;
                var extent = new Vector3(field.Width - 1, field.Height - 1, field.Depth - 1) * field.OriginAndVoxelSize.w;
                var localBounds = new Bounds(origin + extent * 0.5f, extent);
                var bounds = VividMeshletRendererDatabase.TransformBounds(localBounds, source.LocalToWorld);
                if (rebuildBvh)
                {
                    m_Leaves.Add(new Leaf { Bounds = bounds, InstanceIndex = (uint)i });
                    m_LeafNodeIndices.Add(0);
                }
                else
                {
                    int nodeIndex = m_LeafNodeIndices[i];
                    var node = m_Nodes[nodeIndex];
                    node.Min = bounds.min;
                    node.Max = bounds.max;
                    m_Nodes[nodeIndex] = node;
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
                });
            }

            if (rebuildBvh)
                BuildNode(0, m_Leaves.Count);
            else
                RefitBvh();
        }

        private void BuildNode(int start, int count)
        {
            var bounds = m_Leaves[start].Bounds;
            var centers = new Bounds(bounds.center, Vector3.zero);
            for (int i = start + 1; i < start + count; i++)
            {
                bounds.Encapsulate(m_Leaves[i].Bounds);
                centers.Encapsulate(m_Leaves[i].Bounds.center);
            }

            int index = m_Nodes.Count;
            m_Nodes.Add(default);
            uint instanceIndex = uint.MaxValue;
            if (count == 1)
            {
                instanceIndex = m_Leaves[start].InstanceIndex;
                m_LeafNodeIndices[(int)instanceIndex] = index;
            }
            else
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
                InstanceIndex = instanceIndex,
                EscapeIndex = (uint)m_Nodes.Count,
            };
        }

        private void RefitBvh()
        {
            // Children follow their parent in preorder, so they are up to date in this reverse walk.
            for (int i = m_Nodes.Count - 1; i >= 0; i--)
            {
                var node = m_Nodes[i];
                if (node.InstanceIndex != uint.MaxValue)
                    continue;
                var left = m_Nodes[i + 1];
                var right = m_Nodes[(int)left.EscapeIndex];
                node.Min = Vector3.Min(left.Min, right.Min);
                node.Max = Vector3.Max(left.Max, right.Max);
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
            m_Voxels = m_FieldBuffer = m_InstanceBuffer = m_NodeBuffer = null;
            NodeCount = 0;
            m_FieldIndices.Clear();
            m_Fields.Clear();
            m_NextFields.Clear();
            m_Instances.Clear();
            m_NextInstances.Clear();
        }
    }
}

using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven.Meshlets
{
    // The UE sparse payload is immutable. Only repack the atlas when its asset set or an import changes.
    internal sealed class VividSparseMeshSdfAtlas : IDisposable
    {
        private static readonly int MipsId = Shader.PropertyToID("_SparseSdfMips");
        private static readonly int TableId = Shader.PropertyToID("_SparseSdfTable");
        internal static readonly int BricksId = Shader.PropertyToID("_SparseSdfBricks");
        private static readonly int ObjectsId = Shader.PropertyToID("_SparseSdfObjects");
        private static readonly int AtlasId = Shader.PropertyToID("_SparseSdfAtlasSize");
        private readonly List<VividSparseMeshDistanceField> m_Assets = new(), m_NextAssets = new();
        private readonly List<uint> m_Revisions = new();
        private readonly List<Vector4> m_Objects = new(), m_NextObjects = new(), m_Mips = new();
        private readonly List<uint> m_Table = new();
        private GraphicsBuffer m_AssetBuffer, m_TableBuffer, m_ObjectBuffer;
        private Texture3D m_Bricks;
        internal RTHandle BricksHandle { get; private set; }
        private Vector4 m_AtlasSize;
        internal bool Active => m_Bricks != null;

        internal void Begin()
        {
            m_NextAssets.Clear();
            m_NextObjects.Clear();
        }

        internal void Add(VividSparseMeshDistanceField field, Vector3 bakeScale, Vector3 meshCenter, Matrix4x4 localToWorld)
        {
            if (!field)
            {
                m_NextObjects.Add(new Vector4(0, 0, 0, -1));
                m_NextObjects.Add(default);
                m_NextObjects.Add(default);
                m_NextObjects.Add(default);
                return;
            }
            if (!math.all(math.isfinite((float3)bakeScale)) || math.any((float3)bakeScale <= 0))
                throw new ArgumentException("Sparse distance field bake scale must be finite and positive.", nameof(bakeScale));
            int asset = m_NextAssets.IndexOf(field);
            if (asset < 0)
            {
                asset = m_NextAssets.Count;
                m_NextAssets.Add(field);
            }
            var ext = field.FieldBoundsCm.extents;
            float maxExtent = Mathf.Max(ext.x, Mathf.Max(ext.y, ext.z));
            var scale = new Vector3(bakeScale.z, bakeScale.x, bakeScale.y) * (100f / maxExtent);
            Vector3 offset = (field.GeometryBoundsCm.center - field.FieldBoundsCm.center) / maxExtent
                - Vector3.Scale(new Vector3(meshCenter.z, meshCenter.x, meshCenter.y), scale);
            Vector3 worldScale = new Vector3(localToWorld.MultiplyVector(Vector3.forward).magnitude / scale.x,
                localToWorld.MultiplyVector(Vector3.right).magnitude / scale.y, localToWorld.MultiplyVector(Vector3.up).magnitude / scale.z);
            m_NextObjects.Add(new Vector4(scale.x, scale.y, scale.z, asset));
            m_NextObjects.Add(offset);
            m_NextObjects.Add(ext / maxExtent);
            m_NextObjects.Add(worldScale);
        }

        internal bool End()
        {
            bool changed = m_Assets.Count != m_NextAssets.Count;
            for (int i = 0; !changed && i < m_Assets.Count; ++i)
                changed = m_Assets[i] != m_NextAssets[i] || m_Revisions[i] != m_NextAssets[i].Revision;
            if (m_NextAssets.Count == 0)
            {
                if (changed) Dispose();
                return changed;
            }
            if (changed)
            {
                RebuildAtlas();
                m_Assets.Clear();
                m_Revisions.Clear();
                foreach (var field in m_NextAssets)
                {
                    m_Assets.Add(field);
                    m_Revisions.Add(field.Revision);
                }
            }
            bool objectsChanged = m_Objects.Count != m_NextObjects.Count;
            for (int i = 0; !objectsChanged && i < m_Objects.Count; ++i)
                objectsChanged = !m_Objects[i].Equals(m_NextObjects[i]);
            if (objectsChanged || changed)
            {
                Upload(ref m_ObjectBuffer, m_NextObjects, 16);
                m_Objects.Clear();
                m_Objects.AddRange(m_NextObjects);
            }
            return changed || objectsChanged;
        }

        private void RebuildAtlas()
        {
            int count = 0;
            foreach (var field in m_NextAssets)
                foreach (var mip in field.Mips)
                    count = checked(count + mip.Bricks.Length / 512);
            int across = 1;
            while ((long)across * across * across < count) across *= 2;
            int layers = Math.Max(1, (int)(((long)count + across * across - 1) / (across * across)));
            int width = checked(across * 8);
            if (Math.Max(width, layers * 8) > SystemInfo.maxTexture3DSize)
                throw new InvalidOperationException("Imported distance field bricks exceed the hardware 3D texture limit.");
            var bytes = new byte[checked(width * width * layers * 8)];
            m_Table.Clear();
            m_Mips.Clear();
            int firstBrick = 0;
            foreach (var field in m_NextAssets)
            {
                for (int index = field.Mips.Length - 1; index >= 0; --index)
                {
                    ref var mip = ref field.Mips[index];
                    var dims = mip.Dimensions;
                    uint packed = (uint)(dims.x | dims.y << 10 | dims.z << 20) | ((uint)field.Mips.Length << 30);
                    m_Mips.Add(new Vector4(math.asfloat(packed), math.asfloat(m_Table.Count), 0, 0));
                    Vector3 scale = Vector3.Scale(mip.VolumeToVirtualUVScale, dims);
                    Vector3 add = Vector3.Scale(mip.VolumeToVirtualUVAdd, dims);
                    m_Mips.Add(new Vector4(scale.x, scale.y, scale.z, mip.DistanceFieldToVolumeScaleBias.x));
                    m_Mips.Add(new Vector4(add.x, add.y, add.z, mip.DistanceFieldToVolumeScaleBias.y));
                    foreach (uint brick in mip.Indirection)
                        m_Table.Add(brick == uint.MaxValue ? brick : brick + (uint)firstBrick);
                    int brickCount = mip.Bricks.Length / 512;
                    for (int i = 0; i < brickCount; ++i)
                    {
                        int slot = firstBrick + i;
                        int x = (slot % across) * 8, y = (slot / across % across) * 8, z = (slot / (across * across)) * 8;
                        for (int dz = 0; dz < 8; ++dz)
                            for (int dy = 0; dy < 8; ++dy)
                                Buffer.BlockCopy(mip.Bricks, i * 512 + dz * 64 + dy * 8, bytes,
                                    ((z + dz) * width + y + dy) * width + x, 8);
                    }
                    firstBrick += brickCount;
                }
            }
            var texture = new Texture3D(width, width, layers * 8, GraphicsFormat.R8_UNorm, TextureCreationFlags.None)
                { name = "Sparse Mesh SDF Bricks", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            try
            {
                texture.SetPixelData(bytes, 0);
                texture.Apply(false, true);
                Upload(ref m_AssetBuffer, m_Mips, 16);
                Upload(ref m_TableBuffer, m_Table, 4);
            }
            catch
            {
                CoreUtils.Destroy(texture);
                throw;
            }
            BricksHandle?.Release();
            CoreUtils.Destroy(m_Bricks);
            m_Bricks = texture;
            BricksHandle = RTHandles.Alloc(texture);
            m_AtlasSize = new Vector4(1f / width, 1f / width, 1f / (layers * 8), across);
        }

        private static void Upload<T>(ref GraphicsBuffer buffer, List<T> data, int stride) where T : unmanaged
        {
            if (buffer == null || buffer.count < data.Count)
            {
                var replacement = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.Count, stride);
                buffer?.Dispose();
                buffer = replacement;
            }
            buffer.SetData(data, 0, 0, data.Count);
        }

        internal void Bind(ComputeCommandBuffer cmd, ComputeShader shader, int kernel)
        {
            cmd.SetComputeVectorParam(shader, AtlasId, m_AtlasSize);
            cmd.SetComputeBufferParam(shader, kernel, MipsId, m_AssetBuffer);
            cmd.SetComputeBufferParam(shader, kernel, TableId, m_TableBuffer);
            cmd.SetComputeBufferParam(shader, kernel, ObjectsId, m_ObjectBuffer);
        }

        public void Dispose()
        {
            BricksHandle?.Release();
            BricksHandle = null;
            CoreUtils.Destroy(m_Bricks);
            m_Bricks = null;
            m_AssetBuffer?.Dispose();
            m_TableBuffer?.Dispose();
            m_ObjectBuffer?.Dispose();
            m_AssetBuffer = m_TableBuffer = m_ObjectBuffer = null;
            m_Assets.Clear();
            m_Revisions.Clear();
            m_Objects.Clear();
        }
    }
}

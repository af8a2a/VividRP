using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.Particle
{
    public static class VividParticleSystemManager
    {
        public const string DefaultShaderName = "Hidden/VividRP/Particles/BillboardUnlit";

        internal const uint PerInstanceMetadataMask = 0x80000000u;
        internal const int SizeOfMatrix = sizeof(float) * 4 * 4;
        internal const int SizeOfPackedMatrix = sizeof(float) * 4 * 3;
        internal const int SizeOfFloat4 = sizeof(float) * 4;
        internal const int ZeroBlockByteSize = SizeOfPackedMatrix * 2;

        private static readonly Dictionary<VividParticleSystem, ParticleRenderState> s_RenderStates = new();

        public static int registeredSystemCount => s_RenderStates.Count;

        public static bool Contains(VividParticleSystem system)
        {
            return system != null && s_RenderStates.ContainsKey(system);
        }

        public static void Register(VividParticleSystem system)
        {
            if (system == null || s_RenderStates.ContainsKey(system))
                return;

            s_RenderStates.Add(system, new ParticleRenderState(system));
        }

        public static void Unregister(VividParticleSystem system)
        {
            if (system == null || !s_RenderStates.TryGetValue(system, out ParticleRenderState state))
                return;

            state.Dispose();
            s_RenderStates.Remove(system);
        }

        public static void UpdateSystem(VividParticleSystem system)
        {
            if (system == null)
                return;

            if (!s_RenderStates.ContainsKey(system))
                Register(system);

            if (Application.isPlaying)
                system.UpdateAutomatic(Time.deltaTime);

            UpdateRendering(system);
        }

        public static void UpdateRendering(VividParticleSystem system)
        {
            if (system == null)
                return;

            if (!s_RenderStates.TryGetValue(system, out ParticleRenderState state))
            {
                Register(system);
                if (!s_RenderStates.TryGetValue(system, out state))
                    return;
            }

            state.UpdateRendering();
        }

        public static void MarkRendererDirty(VividParticleSystem system)
        {
            if (system != null && s_RenderStates.TryGetValue(system, out ParticleRenderState state))
                state.MarkResourcesDirty();
        }

        internal static void ClearForTests()
        {
            foreach (ParticleRenderState state in s_RenderStates.Values)
                state.Dispose();

            s_RenderStates.Clear();
        }

        internal static bool TryGetStats(
            VividParticleSystem system,
            out VividParticleSystemManagerStats stats)
        {
            stats = default;
            if (system == null || !s_RenderStates.TryGetValue(system, out ParticleRenderState state))
                return false;

            stats = state.stats;
            return true;
        }

        internal static MetadataValue CreatePerInstanceMetadata(int nameId, int byteAddress)
        {
            return new MetadataValue
            {
                NameID = nameId,
                Value = PerInstanceMetadataMask | (uint)byteAddress,
            };
        }

        internal static int ObjectToWorldByteAddress(int capacity)
        {
            return ZeroBlockByteSize;
        }

        internal static int WorldToObjectByteAddress(int capacity)
        {
            return ObjectToWorldByteAddress(capacity) + Mathf.Max(1, capacity) * SizeOfPackedMatrix;
        }

        internal static int BaseColorByteAddress(int capacity)
        {
            return WorldToObjectByteAddress(capacity) + Mathf.Max(1, capacity) * SizeOfPackedMatrix;
        }

        internal static int InstanceDataByteSize(int capacity)
        {
            return BaseColorByteAddress(capacity) + Mathf.Max(1, capacity) * SizeOfFloat4;
        }

        internal static bool IntersectsCullingPlanes(Bounds bounds, Plane[] planes)
        {
            if (planes == null || planes.Length == 0)
                return true;

            for (int i = 0; i < planes.Length; i++)
            {
                if (IsOutsidePlane(bounds, planes[i]))
                    return false;
            }

            return true;
        }

        private static bool IntersectsCullingPlanes(
            Bounds bounds,
            NativeArray<Plane> planes,
            int start,
            int count)
        {
            if (!planes.IsCreated || count <= 0)
                return true;

            int end = Mathf.Min(planes.Length, start + count);
            for (int i = Mathf.Max(0, start); i < end; i++)
            {
                if (IsOutsidePlane(bounds, planes[i]))
                    return false;
            }

            return true;
        }

        private static bool IsOutsidePlane(Bounds bounds, Plane plane)
        {
            Vector3 normal = plane.normal;
            Vector3 positiveVertex = bounds.center + new Vector3(
                normal.x >= 0.0f ? bounds.extents.x : -bounds.extents.x,
                normal.y >= 0.0f ? bounds.extents.y : -bounds.extents.y,
                normal.z >= 0.0f ? bounds.extents.z : -bounds.extents.z);

            return plane.GetDistanceToPoint(positiveVertex) < 0.0f;
        }

        private sealed class ParticleRenderState : IDisposable
        {
            private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
            private static readonly int s_ObjectToWorldId = Shader.PropertyToID("unity_ObjectToWorld");
            private static readonly int s_WorldToObjectId = Shader.PropertyToID("unity_WorldToObject");

            private readonly VividParticleSystem m_System;
            private BatchRendererGroup m_BRG;
            private GraphicsBuffer m_InstanceData;
            private Mesh m_QuadMesh;
            private Material m_OwnedMaterial;
            private Material m_RegisteredMaterial;
            private Material m_SourceMaterial;
            private BatchID m_BatchID;
            private BatchMeshID m_MeshID;
            private BatchMaterialID m_MaterialID;
            private PackedMatrix[] m_ObjectToWorldData = Array.Empty<PackedMatrix>();
            private PackedMatrix[] m_WorldToObjectData = Array.Empty<PackedMatrix>();
            private Vector4[] m_BaseColorData = Array.Empty<Vector4>();
            private readonly PackedMatrix[] m_ZeroBlockData =
            {
                new PackedMatrix(Matrix4x4.zero),
                new PackedMatrix(Matrix4x4.zero),
            };
            private int m_Capacity;
            private int m_LastUploadedCount;
            private int m_RenderQueueOffset;
            private bool m_BatchCreated;
            private bool m_ResourcesDirty = true;
            private bool m_MissingShaderWarningLogged;

            public ParticleRenderState(VividParticleSystem system)
            {
                m_System = system;
            }

            public VividParticleSystemManagerStats stats => new(
                IsInitialized,
                m_Capacity,
                m_LastUploadedCount,
                CullingCallCount,
                VisibleCullingCallCount,
                LastVisible,
                LastViewType,
                LastDrawCommandCount);

            private bool IsInitialized => m_BRG != null
                && m_InstanceData != null
                && m_BatchCreated
                && m_Capacity > 0;

            private int CullingCallCount { get; set; }

            private int VisibleCullingCallCount { get; set; }

            private bool LastVisible { get; set; }

            private BatchCullingViewType LastViewType { get; set; }

            private int LastDrawCommandCount { get; set; }

            public void MarkResourcesDirty()
            {
                m_ResourcesDirty = true;
            }

            public void UpdateRendering()
            {
                if (m_System == null)
                    return;

                if (!m_System.rendererModule.enabled)
                {
                    m_LastUploadedCount = 0;
                    return;
                }

                if (!EnsureResources())
                    return;

                UploadInstanceData();
            }

            public void Dispose()
            {
                ReleaseResources();
            }

            private bool EnsureResources()
            {
                int capacity = Mathf.Max(1, m_System.maxParticles);
                Material sourceMaterial = m_System.rendererModule.material;
                int renderQueueOffset = m_System.rendererModule.renderQueueOffset;
                bool materialChanged = m_SourceMaterial != sourceMaterial
                    || m_RenderQueueOffset != renderQueueOffset
                    || m_RegisteredMaterial == null;

                if (IsInitialized && !m_ResourcesDirty && m_Capacity == capacity && !materialChanged)
                    return true;

                ReleaseResources();
                m_Capacity = capacity;
                EnsureUploadArrays(capacity);

                Shader shader = null;
                Material material = sourceMaterial;
                bool ownsMaterial = false;
                if (material == null)
                {
                    shader = Shader.Find(DefaultShaderName);
                    if (shader == null)
                    {
                        if (!m_MissingShaderWarningLogged)
                        {
                            UnityEngine.Debug.LogWarning(
                                $"[VividRP] Could not find shader '{DefaultShaderName}' for {nameof(VividParticleSystem)}.");
                            m_MissingShaderWarningLogged = true;
                        }

                        return false;
                    }

                    material = new Material(shader)
                    {
                        name = "Vivid Particle System Billboard Material",
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    ownsMaterial = true;
                }
                else if (renderQueueOffset != 0)
                {
                    material = new Material(sourceMaterial)
                    {
                        name = $"{sourceMaterial.name} (Vivid Particle Instance)",
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    ownsMaterial = true;
                }

                if (ownsMaterial)
                {
                    material.SetColor(s_BaseColorId, Color.white);
                    if (material.shader != null && material.shader.renderQueue >= 0)
                        material.renderQueue = material.shader.renderQueue + renderQueueOffset;
                    m_OwnedMaterial = material;
                }

                m_MissingShaderWarningLogged = false;
                m_SourceMaterial = sourceMaterial;
                m_RenderQueueOffset = renderQueueOffset;
                m_RegisteredMaterial = material;
                m_QuadMesh = CreateQuadMesh();
                m_InstanceData = CreateInstanceBuffer(capacity);
                m_InstanceData.SetData(m_ZeroBlockData, 0, 0, m_ZeroBlockData.Length);

                m_BRG = new BatchRendererGroup(new BatchRendererGroupCreateInfo
                {
                    cullingCallback = OnPerformCulling,
                    userContext = IntPtr.Zero,
                });
#if UNITY_EDITOR
                m_BRG.SetEnabledViewTypes(new[]
                {
                    BatchCullingViewType.Camera,
                    BatchCullingViewType.Picking,
                    BatchCullingViewType.SelectionOutline,
                    BatchCullingViewType.Filtering,
                });
#endif
                m_MeshID = m_BRG.RegisterMesh(m_QuadMesh);
                m_MaterialID = m_BRG.RegisterMaterial(m_RegisteredMaterial);

                var metadata = new NativeArray<MetadataValue>(3, Allocator.Temp);
                try
                {
                    metadata[0] = CreatePerInstanceMetadata(s_ObjectToWorldId, ObjectToWorldByteAddress(capacity));
                    metadata[1] = CreatePerInstanceMetadata(s_WorldToObjectId, WorldToObjectByteAddress(capacity));
                    metadata[2] = CreatePerInstanceMetadata(s_BaseColorId, BaseColorByteAddress(capacity));

                    m_BatchID = m_BRG.AddBatch(
                        metadata,
                        m_InstanceData.bufferHandle,
                        0u,
                        ResolveBufferWindowSize());
                }
                finally
                {
                    metadata.Dispose();
                }

                m_BatchCreated = true;
                m_ResourcesDirty = false;
                CullingCallCount = 0;
                VisibleCullingCallCount = 0;
                LastVisible = false;
                LastViewType = default;
                LastDrawCommandCount = 0;
                return true;
            }

            private void ReleaseResources()
            {
                m_BatchCreated = false;
                m_BRG?.Dispose();
                m_BRG = null;

                m_InstanceData?.Dispose();
                m_InstanceData = null;

                if (m_OwnedMaterial != null)
                {
                    CoreUtils.Destroy(m_OwnedMaterial);
                    m_OwnedMaterial = null;
                }

                if (m_QuadMesh != null)
                {
                    CoreUtils.Destroy(m_QuadMesh);
                    m_QuadMesh = null;
                }

                m_RegisteredMaterial = null;
                m_LastUploadedCount = 0;
            }

            private void EnsureUploadArrays(int capacity)
            {
                if (m_ObjectToWorldData.Length != capacity)
                    m_ObjectToWorldData = new PackedMatrix[capacity];
                if (m_WorldToObjectData.Length != capacity)
                    m_WorldToObjectData = new PackedMatrix[capacity];
                if (m_BaseColorData.Length != capacity)
                    m_BaseColorData = new Vector4[capacity];
            }

            private void UploadInstanceData()
            {
                if (m_InstanceData == null || m_System == null)
                    return;

                int count = Mathf.Min(m_System.aliveParticleCount, m_Capacity);
                m_LastUploadedCount = count;
                if (count <= 0)
                    return;

                for (int index = 0; index < count; index++)
                {
                    Matrix4x4 objectToWorld = m_System.GetParticleObjectToWorldMatrix(index);
                    m_ObjectToWorldData[index] = new PackedMatrix(objectToWorld);
                    m_WorldToObjectData[index] = new PackedMatrix(objectToWorld.inverse);
                    m_BaseColorData[index] = (Vector4)m_System.GetParticleRenderColor(index);
                }

                m_InstanceData.SetData(m_ZeroBlockData, 0, 0, m_ZeroBlockData.Length);
                m_InstanceData.SetData(
                    m_ObjectToWorldData,
                    0,
                    ObjectToWorldByteAddress(m_Capacity) / SizeOfPackedMatrix,
                    count);
                m_InstanceData.SetData(
                    m_WorldToObjectData,
                    0,
                    WorldToObjectByteAddress(m_Capacity) / SizeOfPackedMatrix,
                    count);
                m_InstanceData.SetData(
                    m_BaseColorData,
                    0,
                    BaseColorByteAddress(m_Capacity) / SizeOfFloat4,
                    count);
            }

            private unsafe JobHandle OnPerformCulling(
                BatchRendererGroup rendererGroup,
                BatchCullingContext cullingContext,
                BatchCullingOutput cullingOutput,
                IntPtr userContext)
            {
                CullingCallCount++;
                LastViewType = cullingContext.viewType;

                bool visible = m_System != null
                    && m_System.shouldRender
                    && IsVisibleInCullingContext(m_System.worldBounds, cullingContext);
                LastVisible = visible;

                int visibleCount = visible ? Mathf.Min(m_System.aliveParticleCount, m_LastUploadedCount) : 0;
                if (!visible || !m_BatchCreated || visibleCount <= 0)
                {
                    LastDrawCommandCount = 0;
                    WriteEmptyDrawCommands(cullingOutput);
                    return default;
                }

                VisibleCullingCallCount++;
                LastDrawCommandCount = 1;
                WriteVisibleDrawCommands(cullingOutput, visibleCount);
                return default;
            }

            private static bool IsVisibleInCullingContext(Bounds bounds, BatchCullingContext cullingContext)
            {
                NativeArray<Plane> planes = cullingContext.cullingPlanes;
                if (!planes.IsCreated || planes.Length == 0)
                    return true;

                var splits = cullingContext.cullingSplits;
                if (!splits.IsCreated || splits.Length == 0)
                    return IntersectsCullingPlanes(bounds, planes, 0, planes.Length);

                for (int splitIndex = 0; splitIndex < splits.Length; splitIndex++)
                {
                    var split = splits[splitIndex];
                    if (split.cullingPlaneCount <= 0)
                        return true;

                    if (IntersectsCullingPlanes(bounds, planes, split.cullingPlaneOffset, split.cullingPlaneCount))
                        return true;
                }

                return false;
            }

            private unsafe void WriteVisibleDrawCommands(BatchCullingOutput cullingOutput, int visibleCount)
            {
                var draws = new BatchCullingOutputDrawCommands
                {
                    drawCommandCount = 1,
                    drawRangeCount = 1,
                    visibleInstanceCount = visibleCount,
                    drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(
                        UnsafeUtility.SizeOf<BatchDrawCommand>(),
                        UnsafeUtility.AlignOf<long>(),
                        Allocator.TempJob),
                    drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(
                        UnsafeUtility.SizeOf<BatchDrawRange>(),
                        UnsafeUtility.AlignOf<long>(),
                        Allocator.TempJob),
                    visibleInstances = (int*)UnsafeUtility.Malloc(
                        sizeof(int) * visibleCount,
                        UnsafeUtility.AlignOf<long>(),
                        Allocator.TempJob),
                    drawCommandPickingEntityIds = null,
                    instanceSortingPositions = null,
                    instanceSortingPositionFloatCount = 0,
                };

                draws.drawCommands[0] = new BatchDrawCommand
                {
                    visibleOffset = 0,
                    visibleCount = (uint)visibleCount,
                    batchID = m_BatchID,
                    materialID = m_MaterialID,
                    meshID = m_MeshID,
                    submeshIndex = 0,
                    splitVisibilityMask = 0xff,
                    flags = BatchDrawCommandFlags.None,
                    sortingPosition = 0,
                };

                draws.drawRanges[0] = new BatchDrawRange
                {
                    drawCommandsBegin = 0,
                    drawCommandsCount = 1,
                    drawCommandsType = BatchDrawCommandType.Direct,
                    filterSettings = new BatchFilterSettings
                    {
                        renderingLayerMask = uint.MaxValue,
                        layer = m_System != null ? (byte)m_System.gameObject.layer : (byte)0,
                        shadowCastingMode = ShadowCastingMode.Off,
                        receiveShadows = false,
                    },
                };

                for (int index = 0; index < visibleCount; index++)
                    draws.visibleInstances[index] = index;

                cullingOutput.drawCommands[0] = draws;
            }

            private static void WriteEmptyDrawCommands(BatchCullingOutput cullingOutput)
            {
                cullingOutput.drawCommands[0] = new BatchCullingOutputDrawCommands();
            }

            private static Mesh CreateQuadMesh()
            {
                var mesh = new Mesh
                {
                    name = "Vivid Particle Billboard Quad",
                    hideFlags = HideFlags.HideAndDontSave,
                };

                mesh.SetVertices(new[]
                {
                    new Vector3(-0.5f, -0.5f, 0.0f),
                    new Vector3(-0.5f, 0.5f, 0.0f),
                    new Vector3(0.5f, 0.5f, 0.0f),
                    new Vector3(0.5f, -0.5f, 0.0f),
                });
                mesh.SetUVs(0, new[]
                {
                    new Vector2(0.0f, 0.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(1.0f, 1.0f),
                    new Vector2(1.0f, 0.0f),
                });
                mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
                mesh.RecalculateBounds();
                return mesh;
            }

            private static GraphicsBuffer CreateInstanceBuffer(int capacity)
            {
                return new GraphicsBuffer(
                    ResolveBufferTarget(),
                    BufferCountForBytes(InstanceDataByteSize(capacity)),
                    sizeof(int));
            }

            private static GraphicsBuffer.Target ResolveBufferTarget()
            {
                GraphicsBuffer.Target target = GraphicsBuffer.Target.Raw;
                if (BatchRendererGroup.BufferTarget == BatchBufferTarget.ConstantBuffer
                    || SystemInfo.graphicsDeviceType is GraphicsDeviceType.OpenGLCore or GraphicsDeviceType.OpenGLES3)
                {
                    target |= GraphicsBuffer.Target.Constant;
                }

                return target;
            }

            private static uint ResolveBufferWindowSize()
            {
                return BatchRendererGroup.BufferTarget == BatchBufferTarget.ConstantBuffer
                    ? (uint)BatchRendererGroup.GetConstantBufferMaxWindowSize()
                    : 0u;
            }

            private static int BufferCountForBytes(int byteCount)
            {
                return (byteCount + sizeof(int) - 1) / sizeof(int);
            }
        }

        internal readonly struct VividParticleSystemManagerStats
        {
            public readonly bool IsInitialized;
            public readonly int Capacity;
            public readonly int LastUploadedCount;
            public readonly int CullingCallCount;
            public readonly int VisibleCullingCallCount;
            public readonly bool LastVisible;
            public readonly BatchCullingViewType LastViewType;
            public readonly int LastDrawCommandCount;

            public VividParticleSystemManagerStats(
                bool isInitialized,
                int capacity,
                int lastUploadedCount,
                int cullingCallCount,
                int visibleCullingCallCount,
                bool lastVisible,
                BatchCullingViewType lastViewType,
                int lastDrawCommandCount)
            {
                IsInitialized = isInitialized;
                Capacity = capacity;
                LastUploadedCount = lastUploadedCount;
                CullingCallCount = cullingCallCount;
                VisibleCullingCallCount = visibleCullingCallCount;
                LastVisible = lastVisible;
                LastViewType = lastViewType;
                LastDrawCommandCount = lastDrawCommandCount;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PackedMatrix
        {
            public float c0x;
            public float c0y;
            public float c0z;
            public float c1x;
            public float c1y;
            public float c1z;
            public float c2x;
            public float c2y;
            public float c2z;
            public float c3x;
            public float c3y;
            public float c3z;

            public PackedMatrix(Matrix4x4 matrix)
            {
                c0x = matrix.m00;
                c0y = matrix.m10;
                c0z = matrix.m20;
                c1x = matrix.m01;
                c1y = matrix.m11;
                c1z = matrix.m21;
                c2x = matrix.m02;
                c2y = matrix.m12;
                c2z = matrix.m22;
                c3x = matrix.m03;
                c3y = matrix.m13;
                c3z = matrix.m23;
            }
        }
    }
}

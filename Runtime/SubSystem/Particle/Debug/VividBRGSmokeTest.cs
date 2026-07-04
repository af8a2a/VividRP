using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.Particle.Debug
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Vivid BRG Smoke Test")]
    public sealed class VividBRGSmokeTest : MonoBehaviour
    {
        internal const string ShaderName = "Hidden/VividRP/Particles/BRGSmokeTest";
        internal const uint PerInstanceMetadataMask = 0x80000000u;
        internal const int SizeOfMatrix = sizeof(float) * 4 * 4;
        internal const int SizeOfPackedMatrix = sizeof(float) * 4 * 3;
        internal const int SizeOfFloat4 = sizeof(float) * 4;
        internal const int ObjectToWorldByteAddress = SizeOfPackedMatrix * 2;
        internal const int WorldToObjectByteAddress = ObjectToWorldByteAddress + SizeOfPackedMatrix;
        internal const int BaseColorByteAddress = WorldToObjectByteAddress + SizeOfPackedMatrix;
        internal const int InstanceDataByteSize = BaseColorByteAddress + SizeOfFloat4;

        private const float MinimumSize = 0.001f;
        private const float LocalBoundsHalfThickness = 0.01f;

        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_ObjectToWorldId = Shader.PropertyToID("unity_ObjectToWorld");
        private static readonly int s_WorldToObjectId = Shader.PropertyToID("unity_WorldToObject");

        [SerializeField]
        private float m_Size = 1.0f;

        [SerializeField]
        private Color m_Color = Color.magenta;

        private BatchRendererGroup m_BRG;
        private GraphicsBuffer m_InstanceData;
        private Mesh m_QuadMesh;
        private Material m_Material;
        private BatchID m_BatchID;
        private BatchMeshID m_MeshID;
        private BatchMaterialID m_MaterialID;
        private bool m_BatchCreated;
        private bool m_MissingShaderWarningLogged;

        public bool IsInitialized => m_BRG != null && m_InstanceData != null && m_BatchCreated;

        public int CullingCallCount { get; private set; }

        public int VisibleCullingCallCount { get; private set; }

        public bool LastVisible { get; private set; }

        public BatchCullingViewType LastViewType { get; private set; }

        public int LastDrawCommandCount { get; private set; }

        public Bounds WorldBounds => CalculateWorldBounds(transform.localToWorldMatrix, ResolveSize(m_Size));

        private void OnEnable()
        {
            TryInitialize(Shader.Find(ShaderName));
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                TryInitialize(Shader.Find(ShaderName));
                if (!IsInitialized)
                    return;
            }

            UploadInstanceData();
        }

        private void OnValidate()
        {
            m_Size = ResolveSize(m_Size);

            if (IsInitialized)
                UploadInstanceData();
        }

        private void OnDisable()
        {
            ReleaseResources();
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        internal bool InitializeForTests(Shader shader)
        {
            ReleaseResources();
            return TryInitialize(shader);
        }

        internal static MetadataValue CreatePerInstanceMetadata(int nameId, int byteAddress)
        {
            return new MetadataValue
            {
                NameID = nameId,
                Value = PerInstanceMetadataMask | (uint)byteAddress,
            };
        }

        internal static Matrix4x4 CreateObjectToWorldMatrix(Matrix4x4 localToWorld, float size)
        {
            return localToWorld * Matrix4x4.Scale(Vector3.one * ResolveSize(size));
        }

        internal static Bounds CalculateWorldBounds(Matrix4x4 localToWorld, float size)
        {
            Matrix4x4 objectToWorld = CreateObjectToWorldMatrix(localToWorld, size);
            Vector3 center = objectToWorld.MultiplyPoint3x4(Vector3.zero);
            Vector3 right = objectToWorld.MultiplyVector(Vector3.right * 0.5f);
            Vector3 up = objectToWorld.MultiplyVector(Vector3.up * 0.5f);
            Vector3 forward = objectToWorld.MultiplyVector(Vector3.forward * LocalBoundsHalfThickness);

            Vector3 extents = Abs(right) + Abs(up) + Abs(forward);
            return new Bounds(center, extents * 2.0f);
        }

        internal static bool IntersectsCullingPlanes(Bounds bounds, Plane[] planes)
        {
            if (planes == null || planes.Length == 0)
                return true;

            for (var i = 0; i < planes.Length; i++)
            {
                if (IsOutsidePlane(bounds, planes[i]))
                    return false;
            }

            return true;
        }

        internal static PackedMatrix PackMatrix(Matrix4x4 matrix)
        {
            return new PackedMatrix(matrix);
        }

        private bool TryInitialize(Shader shader)
        {
            if (IsInitialized)
                return true;

            if (shader == null)
            {
                if (!m_MissingShaderWarningLogged)
                {
                    UnityEngine.Debug.LogWarning($"[VividRP] Could not find shader '{ShaderName}' for {nameof(VividBRGSmokeTest)}.");
                    m_MissingShaderWarningLogged = true;
                }

                return false;
            }

            m_MissingShaderWarningLogged = false;
            m_QuadMesh = CreateQuadMesh();
            m_Material = CreateMaterial(shader);
            m_InstanceData = CreateInstanceBuffer();

            UploadInstanceData();

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
            m_MaterialID = m_BRG.RegisterMaterial(m_Material);

            var metadata = new NativeArray<MetadataValue>(3, Allocator.Temp);
            try
            {
                metadata[0] = CreatePerInstanceMetadata(s_ObjectToWorldId, ObjectToWorldByteAddress);
                metadata[1] = CreatePerInstanceMetadata(s_WorldToObjectId, WorldToObjectByteAddress);
                metadata[2] = CreatePerInstanceMetadata(s_BaseColorId, BaseColorByteAddress);

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

            CullingCallCount = 0;
            VisibleCullingCallCount = 0;
            LastVisible = false;
            LastDrawCommandCount = 0;
            LastViewType = default;
            return true;
        }

        private void ReleaseResources()
        {
            m_BatchCreated = false;

            if (m_BRG != null)
            {
                m_BRG.Dispose();
                m_BRG = null;
            }

            if (m_InstanceData != null)
            {
                m_InstanceData.Dispose();
                m_InstanceData = null;
            }

            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }

            if (m_QuadMesh != null)
            {
                CoreUtils.Destroy(m_QuadMesh);
                m_QuadMesh = null;
            }
        }

        private void UploadInstanceData()
        {
            if (m_InstanceData == null)
                return;

            Matrix4x4 objectToWorld = CreateObjectToWorldMatrix(transform.localToWorldMatrix, m_Size);
            Matrix4x4 worldToObject = objectToWorld.inverse;

            var zero = new[] { Matrix4x4.zero };
            var objectToWorldData = new[] { new PackedMatrix(objectToWorld) };
            var worldToObjectData = new[] { new PackedMatrix(worldToObject) };
            var colorData = new[] { (Vector4)m_Color };

            m_InstanceData.SetData(zero, 0, 0, 1);
            m_InstanceData.SetData(objectToWorldData, 0, ObjectToWorldByteAddress / SizeOfPackedMatrix, 1);
            m_InstanceData.SetData(worldToObjectData, 0, WorldToObjectByteAddress / SizeOfPackedMatrix, 1);
            m_InstanceData.SetData(colorData, 0, BaseColorByteAddress / SizeOfFloat4, 1);
        }

        private unsafe JobHandle OnPerformCulling(
            BatchRendererGroup rendererGroup,
            BatchCullingContext cullingContext,
            BatchCullingOutput cullingOutput,
            IntPtr userContext)
        {
            CullingCallCount++;
            LastViewType = cullingContext.viewType;

            Bounds bounds = WorldBounds;
            bool visible = IsVisibleInCullingContext(bounds, cullingContext);
            LastVisible = visible;

            if (!visible || !m_BatchCreated)
            {
                LastDrawCommandCount = 0;
                WriteEmptyDrawCommands(cullingOutput);
                return default;
            }

            VisibleCullingCallCount++;
            LastDrawCommandCount = 1;
            WriteVisibleDrawCommands(cullingOutput);
            return default;
        }

        private bool IsVisibleInCullingContext(Bounds bounds, BatchCullingContext cullingContext)
        {
            NativeArray<Plane> planes = cullingContext.cullingPlanes;
            if (!planes.IsCreated || planes.Length == 0)
                return true;

            var splits = cullingContext.cullingSplits;
            if (!splits.IsCreated || splits.Length == 0)
                return IntersectsCullingPlanes(bounds, planes, 0, planes.Length);

            for (var splitIndex = 0; splitIndex < splits.Length; splitIndex++)
            {
                var split = splits[splitIndex];
                if (split.cullingPlaneCount <= 0)
                    return true;

                if (IntersectsCullingPlanes(bounds, planes, split.cullingPlaneOffset, split.cullingPlaneCount))
                    return true;
            }

            return false;
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
            for (var i = Mathf.Max(0, start); i < end; i++)
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

        private unsafe void WriteVisibleDrawCommands(BatchCullingOutput cullingOutput)
        {
            var draws = new BatchCullingOutputDrawCommands
            {
                drawCommandCount = 1,
                drawRangeCount = 1,
                visibleInstanceCount = 1,
                drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(
                    UnsafeUtility.SizeOf<BatchDrawCommand>(),
                    UnsafeUtility.AlignOf<long>(),
                    Allocator.TempJob),
                drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(
                    UnsafeUtility.SizeOf<BatchDrawRange>(),
                    UnsafeUtility.AlignOf<long>(),
                    Allocator.TempJob),
                visibleInstances = (int*)UnsafeUtility.Malloc(
                    sizeof(int),
                    UnsafeUtility.AlignOf<long>(),
                    Allocator.TempJob),
                drawCommandPickingEntityIds = null,
                instanceSortingPositions = null,
                instanceSortingPositionFloatCount = 0,
            };

            draws.drawCommands[0] = new BatchDrawCommand
            {
                visibleOffset = 0,
                visibleCount = 1,
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
                    layer = (byte)gameObject.layer,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                },
            };

            draws.visibleInstances[0] = 0;
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
                name = "Vivid BRG Smoke Test Quad",
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

        private static Material CreateMaterial(Shader shader)
        {
            var material = new Material(shader)
            {
                name = "Vivid BRG Smoke Test Material",
                hideFlags = HideFlags.HideAndDontSave,
            };
            material.SetColor(s_BaseColorId, Color.magenta);
            return material;
        }

        private static GraphicsBuffer CreateInstanceBuffer()
        {
            return new GraphicsBuffer(
                ResolveBufferTarget(),
                BufferCountForBytes(InstanceDataByteSize),
                sizeof(int));
        }

        private static GraphicsBuffer.Target ResolveBufferTarget()
        {
            var target = GraphicsBuffer.Target.Raw;
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

        private static float ResolveSize(float size)
        {
            return Mathf.Max(MinimumSize, size);
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
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

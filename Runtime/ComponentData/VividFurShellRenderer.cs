using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    /// <summary>
    /// Submits stacked fur shells for a single mesh using GPU instancing (<see cref="Graphics.RenderMeshInstanced"/>)
    /// and a per-instance <c>_ShellHeight</c> array (<c>VividRP/Material/FurSlab</c>).
    /// Shells are sorted outside-in (h: 1 -> 0) by default so hardware Early-Z and PreDepth Early-Z
    /// reject occluded inner shells and minimize overdraw.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("VividRP/Vivid Fur Shell Renderer")]
    public sealed class VividFurShellRenderer : MonoBehaviour
    {
        public const int MinShellCount = 1;
        public const int MaxShellCount = 64;
        internal const int DefaultShellCount = 24;

        private const float k_MaxWindGustAmplitude = 1.45f; // sin(x) + 0.45 * sin(...) in VividFurSurface.hlsl

        private static readonly int s_ShellHeightId = Shader.PropertyToID("_ShellHeight");
        private static readonly int s_FurLengthId = Shader.PropertyToID("_FurLength");
        private static readonly int s_GravityWSId = Shader.PropertyToID("_GravityWS");
        private static readonly int s_GravityStrengthId = Shader.PropertyToID("_GravityStrength");
        private static readonly int s_WindStrengthId = Shader.PropertyToID("_WindStrength");

        [StructLayout(LayoutKind.Sequential)]
        internal struct ShellInstanceData
        {
            public Matrix4x4 objectToWorld;
            public Matrix4x4 prevObjectToWorld;
        }

        [Header("Source Geometry & Material")]
        [Tooltip("Source mesh to extrude into fur shells. Leave empty to reuse the sibling MeshFilter.sharedMesh.")]
        [SerializeField] private Mesh m_Mesh;

        [Tooltip("Sub-mesh index within the source mesh.")]
        [Min(0)]
        [SerializeField] private int m_SubMeshIndex;

        [Tooltip("FurSlab material with GPU Instancing enabled (VividRP/Material/FurSlab).")]
        [SerializeField] private Material m_Material;

        [Tooltip("Automatically disable a sibling MeshRenderer on the same GameObject to prevent Z-fighting with shell h = 0.")]
        [SerializeField] private bool m_DisableSiblingMeshRenderer = true;

        [Header("Shell Stack")]
        [Tooltip("Number of stacked fur shells at near distance (e.g. 8, 16, 24, 32).")]
        [Range(MinShellCount, MaxShellCount)]
        [SerializeField] private int m_ShellCount = DefaultShellCount;

        [Tooltip("Shell height distribution curve. 1.0 = uniform spacing; < 1 clusters shells near tips; > 1 clusters near roots.")]
        [Range(0.5f, 2.0f)]
        [SerializeField] private float m_HeightDistributionPower = 1.0f;

        [Tooltip("Submit shells outside-in (h: 1 -> 0) so Early-Z + PreDepth rejects occluded inner shells.")]
        [SerializeField] private bool m_OutsideInOrder = true;

        [Header("Camera Distance LOD")]
        [Tooltip("Scale shell count down smoothly with camera distance.")]
        [SerializeField] private bool m_EnableDistanceLod = true;

        [Tooltip("Camera distance at or below which full Shell Count is rendered.")]
        [Min(0.0f)]
        [SerializeField] private float m_LodNearDistance = 4.0f;

        [Tooltip("Camera distance at or beyond which shell count drops to Min LOD Shell Count.")]
        [Min(0.01f)]
        [SerializeField] private float m_LodFarDistance = 25.0f;

        [Tooltip("Minimum shell count rendered at far camera distance (1 = base skin only).")]
        [Range(MinShellCount, 16)]
        [SerializeField] private int m_MinLodShellCount = 2;

        [Header("Shadows (VSM / CSM)")]
        [Tooltip("Shadow casting mode for fur shells.")]
        [SerializeField] private ShadowCastingMode m_ShadowCastingMode = ShadowCastingMode.On;

        [Tooltip("Cast shadows only from a sparse core subset of shells instead of the full visual stack.")]
        [SerializeField] private bool m_SparseShadowShells = true;

        [Tooltip("Number of core shells submitted to shadow passes when Sparse Shadow Shells is enabled.")]
        [Range(MinShellCount, 8)]
        [SerializeField] private int m_ShadowShellCount = 2;

        [Tooltip("Maximum shell height h in [0, 1] used for sparse shadow shells (0 = solid base skin only).")]
        [Range(0.0f, 1.0f)]
        [SerializeField] private float m_ShadowMaxHeight = 0.35f;

        [Tooltip("Whether the fur shells receive shadows.")]
        [SerializeField] private bool m_ReceiveShadows = true;

        [Header("Culling & Motion")]
        [Tooltip("Additional world-space bounds margin added on top of material _FurLength + gravity + wind extrusion.")]
        [Min(0.0f)]
        [SerializeField] private float m_ExtraBoundsPadding = 0.02f;

        [Tooltip("Motion vector generation mode for instanced fur shells.")]
        [SerializeField] private MotionVectorGenerationMode m_MotionVectorMode = MotionVectorGenerationMode.Object;

        [SerializeField] private uint m_RenderingLayerMask = 1u;

        // Pre-allocated, zero-GC runtime buffers
        private ShellInstanceData[] m_InstanceData;
        private float[] m_VisualShellHeights;
        private float[] m_ShadowShellHeights;
        private MaterialPropertyBlock m_VisualPropertyBlock;
        private MaterialPropertyBlock m_ShadowPropertyBlock;
        private Action<ScriptableRenderContext, Camera> m_BeginCameraRenderingCallback;

        // Cached sibling components & material property capability flags
        private MeshFilter m_CachedMeshFilter;
        private MeshRenderer m_CachedSiblingRenderer;
        private Material m_CachedBoundsMaterial;
        private bool m_HasFurLengthProp;
        private bool m_HasGravityWSProp;
        private bool m_HasGravityStrengthProp;
        private bool m_HasWindStrengthProp;
        private EntityId m_WarnedNonInstancedMaterialId;

        // Dirty-tracking for MaterialPropertyBlock uploads
        private int m_UploadedVisualShellCount = -1;
        private float m_UploadedVisualHeightPower = -1.0f;
        private bool m_UploadedVisualOutsideIn;

        private int m_UploadedShadowShellCount = -1;
        private float m_UploadedShadowMaxHeight = -1.0f;
        private float m_UploadedShadowHeightPower = -1.0f;
        private bool m_UploadedShadowOutsideIn;

        // Per-frame transform history for accurate multi-camera Object motion vectors
        private Matrix4x4 m_CurrentLocalToWorld = Matrix4x4.identity;
        private Matrix4x4 m_PreviousLocalToWorld = Matrix4x4.identity;
        private int m_LastTransformFrameCount = -1;
        private bool m_HasTransformHistory;

        // Live diagnostics
        public int LastRenderedShellCount { get; private set; }
        public int LastShadowShellCount { get; private set; }
        public int LastDrawCallCount { get; private set; }
        public Bounds LastWorldBounds { get; private set; }

        public Mesh Mesh
        {
            get => m_Mesh;
            set => m_Mesh = value;
        }

        public int SubMeshIndex
        {
            get => m_SubMeshIndex;
            set => m_SubMeshIndex = Mathf.Max(0, value);
        }

        public Material Material
        {
            get => m_Material;
            set
            {
                if (m_Material != value)
                {
                    m_Material = value;
                    m_CachedBoundsMaterial = null;
                    m_WarnedNonInstancedMaterialId = EntityId.None;
                }
            }
        }

        public bool DisableSiblingMeshRenderer
        {
            get => m_DisableSiblingMeshRenderer;
            set
            {
                m_DisableSiblingMeshRenderer = value;
                SyncSiblingMeshRendererState();
            }
        }

        public int ShellCount
        {
            get => m_ShellCount;
            set
            {
                m_ShellCount = Mathf.Clamp(value, MinShellCount, MaxShellCount);
                m_MinLodShellCount = Mathf.Clamp(m_MinLodShellCount, MinShellCount, m_ShellCount);
                InvalidateShellHeightUploads();
            }
        }

        public float HeightDistributionPower
        {
            get => m_HeightDistributionPower;
            set
            {
                m_HeightDistributionPower = Mathf.Clamp(value, 0.5f, 2.0f);
                InvalidateShellHeightUploads();
            }
        }

        public bool OutsideInOrder
        {
            get => m_OutsideInOrder;
            set
            {
                if (m_OutsideInOrder != value)
                {
                    m_OutsideInOrder = value;
                    InvalidateShellHeightUploads();
                }
            }
        }

        public bool EnableDistanceLod
        {
            get => m_EnableDistanceLod;
            set => m_EnableDistanceLod = value;
        }

        public float LodNearDistance
        {
            get => m_LodNearDistance;
            set
            {
                m_LodNearDistance = Mathf.Max(0.0f, value);
                if (m_LodFarDistance < m_LodNearDistance + 0.01f)
                    m_LodFarDistance = m_LodNearDistance + 0.01f;
            }
        }

        public float LodFarDistance
        {
            get => m_LodFarDistance;
            set => m_LodFarDistance = Mathf.Max(m_LodNearDistance + 0.01f, value);
        }

        public int MinLodShellCount
        {
            get => m_MinLodShellCount;
            set => m_MinLodShellCount = Mathf.Clamp(value, MinShellCount, m_ShellCount);
        }

        public ShadowCastingMode ShadowCastingMode
        {
            get => m_ShadowCastingMode;
            set => m_ShadowCastingMode = value;
        }

        public bool SparseShadowShells
        {
            get => m_SparseShadowShells;
            set => m_SparseShadowShells = value;
        }

        public int ShadowShellCount
        {
            get => m_ShadowShellCount;
            set
            {
                m_ShadowShellCount = Mathf.Clamp(value, MinShellCount, MaxShellCount);
                InvalidateShellHeightUploads();
            }
        }

        public float ShadowMaxHeight
        {
            get => m_ShadowMaxHeight;
            set
            {
                m_ShadowMaxHeight = Mathf.Clamp01(value);
                InvalidateShellHeightUploads();
            }
        }

        public bool ReceiveShadows
        {
            get => m_ReceiveShadows;
            set => m_ReceiveShadows = value;
        }

        public float ExtraBoundsPadding
        {
            get => m_ExtraBoundsPadding;
            set => m_ExtraBoundsPadding = Mathf.Max(0.0f, value);
        }

        public MotionVectorGenerationMode MotionVectorMode
        {
            get => m_MotionVectorMode;
            set => m_MotionVectorMode = value;
        }

        public uint RenderingLayerMask
        {
            get => m_RenderingLayerMask;
            set => m_RenderingLayerMask = value;
        }

        private void Reset()
        {
            if (TryGetComponent(out MeshFilter meshFilter) && m_Mesh == null)
                m_Mesh = meshFilter.sharedMesh;

            if (TryGetComponent(out MeshRenderer meshRenderer))
            {
                if (m_Material == null)
                    m_Material = meshRenderer.sharedMaterial;

                if (m_DisableSiblingMeshRenderer)
                    meshRenderer.enabled = false;
            }
        }

        private void OnEnable()
        {
            EnsureRuntimeStorage();
            CacheSiblingComponents();
            SyncSiblingMeshRendererState();
            InvalidateShellHeightUploads();

            Matrix4x4 localToWorld = transform.localToWorldMatrix;
            m_CurrentLocalToWorld = localToWorld;
            m_PreviousLocalToWorld = localToWorld;
            m_LastTransformFrameCount = Time.frameCount;
            m_HasTransformHistory = true;
            m_WarnedNonInstancedMaterialId = EntityId.None;

            RenderPipelineManager.beginCameraRendering -= m_BeginCameraRenderingCallback;
            RenderPipelineManager.beginCameraRendering += m_BeginCameraRenderingCallback;
        }

        private void OnDisable()
        {
            if (m_BeginCameraRenderingCallback != null)
                RenderPipelineManager.beginCameraRendering -= m_BeginCameraRenderingCallback;

            LastRenderedShellCount = 0;
            LastShadowShellCount = 0;
            LastDrawCallCount = 0;
            m_HasTransformHistory = false;
        }

        private void OnValidate()
        {
            m_SubMeshIndex = Mathf.Max(0, m_SubMeshIndex);
            m_ShellCount = Mathf.Clamp(m_ShellCount, MinShellCount, MaxShellCount);
            m_HeightDistributionPower = Mathf.Clamp(m_HeightDistributionPower, 0.5f, 2.0f);
            m_LodNearDistance = Mathf.Max(0.0f, m_LodNearDistance);
            m_LodFarDistance = Mathf.Max(m_LodNearDistance + 0.01f, m_LodFarDistance);
            m_MinLodShellCount = Mathf.Clamp(m_MinLodShellCount, MinShellCount, m_ShellCount);
            m_ShadowShellCount = Mathf.Clamp(m_ShadowShellCount, MinShellCount, MaxShellCount);
            m_ShadowMaxHeight = Mathf.Clamp01(m_ShadowMaxHeight);
            m_ExtraBoundsPadding = Mathf.Max(0.0f, m_ExtraBoundsPadding);
            m_CachedBoundsMaterial = null;
            m_WarnedNonInstancedMaterialId = EntityId.None;
            InvalidateShellHeightUploads();

            if (isActiveAndEnabled)
                SyncSiblingMeshRendererState();
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || !isActiveAndEnabled)
                return;

            // Do not inject scene fur shells into Inspector material preview cameras.
            if (camera.cameraType == CameraType.Preview)
                return;

            int objectLayer = gameObject.layer;
            if ((camera.cullingMask & (1 << objectLayer)) == 0)
                return;

            Mesh mesh = ResolveMesh();
            Material material = m_Material;
            if (mesh == null || material == null)
                return;

            int subMeshCount = mesh.subMeshCount;
            if (subMeshCount <= 0)
                return;

            int subMeshIndex = m_SubMeshIndex < subMeshCount ? m_SubMeshIndex : 0;

            if (!material.enableInstancing)
            {
                EntityId matId = material.GetEntityId();
                if (m_WarnedNonInstancedMaterialId != matId)
                {
                    m_WarnedNonInstancedMaterialId = matId;
                    Debug.LogWarning(
                        $"[VividFurShellRenderer] Material '{material.name}' on '{name}' does not have GPU Instancing enabled. Enable Instancing on the material to render fur shells.",
                        this);
                }

                return;
            }

            EnsureRuntimeStorage();

            Matrix4x4 localToWorld = transform.localToWorldMatrix;
            UpdateTransformHistory(localToWorld, Time.frameCount);

            float maxExtrusion = EvaluateMaterialWorldExtrusion(material);
            Bounds worldBounds = ComputeExpandedWorldBounds(mesh.bounds, localToWorld, maxExtrusion);
            LastWorldBounds = worldBounds;

            Vector3 cameraPosWS = camera.transform.position;
            float distanceToCamera = Vector3.Distance(cameraPosWS, worldBounds.center);
            int visualShellCount = ComputeLodShellCount(
                m_ShellCount,
                m_EnableDistanceLod,
                distanceToCamera,
                m_LodNearDistance,
                m_LodFarDistance,
                m_MinLodShellCount);

            bool shouldCastShadows = m_ShadowCastingMode != ShadowCastingMode.Off;
            bool shadowsOnly = m_ShadowCastingMode == ShadowCastingMode.ShadowsOnly;
            bool useSparseShadows = shouldCastShadows && m_SparseShadowShells;
            int shadowShellCount = shouldCastShadows
                ? (useSparseShadows ? Mathf.Min(Mathf.Clamp(m_ShadowShellCount, MinShellCount, MaxShellCount), visualShellCount) : visualShellCount)
                : 0;

            int maxInstanceCount = shadowsOnly ? shadowShellCount : visualShellCount;
            if (maxInstanceCount <= 0)
                return;

            PopulateInstanceData(maxInstanceCount, m_CurrentLocalToWorld, m_PreviousLocalToWorld);

            RenderParams renderParams = new RenderParams(material)
            {
                layer = objectLayer,
                renderingLayerMask = m_RenderingLayerMask,
                worldBounds = worldBounds,
                camera = camera,
                receiveShadows = m_ReceiveShadows,
                reflectionProbeUsage = ReflectionProbeUsage.BlendProbes,
                lightProbeUsage = LightProbeUsage.BlendProbes,
                motionVectorMode = m_MotionVectorMode,
            };

            if (shadowsOnly)
            {
                if (useSparseShadows)
                {
                    EnsureShadowShellHeightsUploaded(shadowShellCount, m_ShadowMaxHeight, m_HeightDistributionPower, m_OutsideInOrder);
                    renderParams.matProps = m_ShadowPropertyBlock;
                }
                else
                {
                    EnsureVisualShellHeightsUploaded(visualShellCount, m_HeightDistributionPower, m_OutsideInOrder);
                    renderParams.matProps = m_VisualPropertyBlock;
                }

                renderParams.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                Graphics.RenderMeshInstanced(renderParams, mesh, subMeshIndex, m_InstanceData, shadowShellCount);

                LastRenderedShellCount = 0;
                LastShadowShellCount = shadowShellCount;
                LastDrawCallCount = 1;
                return;
            }

            EnsureVisualShellHeightsUploaded(visualShellCount, m_HeightDistributionPower, m_OutsideInOrder);

            if (useSparseShadows)
            {
                // Pass 1: Visual shells (PreDepth, GBuffer, MotionVectors) without shadow overdraw.
                renderParams.shadowCastingMode = ShadowCastingMode.Off;
                renderParams.matProps = m_VisualPropertyBlock;
                Graphics.RenderMeshInstanced(renderParams, mesh, subMeshIndex, m_InstanceData, visualShellCount);

                // Pass 2: Sparse core shells for VSM / CSM shadow passes only.
                EnsureShadowShellHeightsUploaded(shadowShellCount, m_ShadowMaxHeight, m_HeightDistributionPower, m_OutsideInOrder);
                renderParams.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                renderParams.matProps = m_ShadowPropertyBlock;
                Graphics.RenderMeshInstanced(renderParams, mesh, subMeshIndex, m_InstanceData, shadowShellCount);

                LastRenderedShellCount = visualShellCount;
                LastShadowShellCount = shadowShellCount;
                LastDrawCallCount = 2;
            }
            else
            {
                // Single instanced draw call for both visual and shadow passes (or shadows disabled).
                renderParams.shadowCastingMode = m_ShadowCastingMode;
                renderParams.matProps = m_VisualPropertyBlock;
                Graphics.RenderMeshInstanced(renderParams, mesh, subMeshIndex, m_InstanceData, visualShellCount);

                LastRenderedShellCount = visualShellCount;
                LastShadowShellCount = shouldCastShadows ? visualShellCount : 0;
                LastDrawCallCount = 1;
            }
        }

        private void EnsureRuntimeStorage()
        {
            m_InstanceData ??= new ShellInstanceData[MaxShellCount];
            m_VisualShellHeights ??= new float[MaxShellCount];
            m_ShadowShellHeights ??= new float[MaxShellCount];
            m_VisualPropertyBlock ??= new MaterialPropertyBlock();
            m_ShadowPropertyBlock ??= new MaterialPropertyBlock();
            m_BeginCameraRenderingCallback ??= OnBeginCameraRendering;
        }

        private void CacheSiblingComponents()
        {
            TryGetComponent(out m_CachedMeshFilter);
            TryGetComponent(out m_CachedSiblingRenderer);
        }

        private void SyncSiblingMeshRendererState()
        {
            if (m_CachedSiblingRenderer == null)
                TryGetComponent(out m_CachedSiblingRenderer);

            if (m_DisableSiblingMeshRenderer && m_CachedSiblingRenderer != null && m_CachedSiblingRenderer.enabled)
                m_CachedSiblingRenderer.enabled = false;
        }

        private Mesh ResolveMesh()
        {
            if (m_Mesh != null)
                return m_Mesh;

            if (m_CachedMeshFilter == null)
                TryGetComponent(out m_CachedMeshFilter);

            return m_CachedMeshFilter != null ? m_CachedMeshFilter.sharedMesh : null;
        }

        private void InvalidateShellHeightUploads()
        {
            m_UploadedVisualShellCount = -1;
            m_UploadedShadowShellCount = -1;
        }

        private void UpdateTransformHistory(Matrix4x4 localToWorld, int frameCount)
        {
            if (!m_HasTransformHistory)
            {
                m_CurrentLocalToWorld = localToWorld;
                m_PreviousLocalToWorld = localToWorld;
                m_LastTransformFrameCount = frameCount;
                m_HasTransformHistory = true;
                return;
            }

            if (frameCount != m_LastTransformFrameCount)
            {
                m_PreviousLocalToWorld = m_CurrentLocalToWorld;
                m_CurrentLocalToWorld = localToWorld;
                m_LastTransformFrameCount = frameCount;
            }
            else
            {
                m_CurrentLocalToWorld = localToWorld;
            }
        }

        private void PopulateInstanceData(int instanceCount, Matrix4x4 currentLocalToWorld, Matrix4x4 previousLocalToWorld)
        {
            ShellInstanceData entry = new ShellInstanceData
            {
                objectToWorld = currentLocalToWorld,
                prevObjectToWorld = previousLocalToWorld,
            };

            for (int i = 0; i < instanceCount; i++)
                m_InstanceData[i] = entry;
        }

        private void EnsureVisualShellHeightsUploaded(int shellCount, float heightPower, bool outsideInOrder)
        {
            if (m_UploadedVisualShellCount == shellCount
                && m_UploadedVisualHeightPower == heightPower
                && m_UploadedVisualOutsideIn == outsideInOrder)
            {
                return;
            }

            FillShellHeights(m_VisualShellHeights, shellCount, heightPower, outsideInOrder, 1.0f);
            m_VisualPropertyBlock.SetFloatArray(s_ShellHeightId, m_VisualShellHeights);
            m_UploadedVisualShellCount = shellCount;
            m_UploadedVisualHeightPower = heightPower;
            m_UploadedVisualOutsideIn = outsideInOrder;
        }

        private void EnsureShadowShellHeightsUploaded(int shellCount, float maxHeight, float heightPower, bool outsideInOrder)
        {
            if (m_UploadedShadowShellCount == shellCount
                && m_UploadedShadowMaxHeight == maxHeight
                && m_UploadedShadowHeightPower == heightPower
                && m_UploadedShadowOutsideIn == outsideInOrder)
            {
                return;
            }

            FillShellHeights(m_ShadowShellHeights, shellCount, heightPower, outsideInOrder, maxHeight);
            m_ShadowPropertyBlock.SetFloatArray(s_ShellHeightId, m_ShadowShellHeights);
            m_UploadedShadowShellCount = shellCount;
            m_UploadedShadowMaxHeight = maxHeight;
            m_UploadedShadowHeightPower = heightPower;
            m_UploadedShadowOutsideIn = outsideInOrder;
        }

        private float EvaluateMaterialWorldExtrusion(Material material)
        {
            if (!ReferenceEquals(m_CachedBoundsMaterial, material))
            {
                m_CachedBoundsMaterial = material;
                m_HasFurLengthProp = material.HasProperty(s_FurLengthId);
                m_HasGravityWSProp = material.HasProperty(s_GravityWSId);
                m_HasGravityStrengthProp = material.HasProperty(s_GravityStrengthId);
                m_HasWindStrengthProp = material.HasProperty(s_WindStrengthId);
            }

            float furLength = m_HasFurLengthProp ? material.GetFloat(s_FurLengthId) : 0.0f;
            Vector3 gravityWS = m_HasGravityWSProp ? (Vector3)material.GetVector(s_GravityWSId) : Vector3.down;
            float gravityStrength = m_HasGravityStrengthProp ? material.GetFloat(s_GravityStrengthId) : 0.0f;
            float windStrength = m_HasWindStrengthProp ? material.GetFloat(s_WindStrengthId) : 0.0f;

            return ComputeMaxFurWorldExtrusion(furLength, gravityWS, gravityStrength, windStrength, m_ExtraBoundsPadding);
        }

        /// <summary>
        /// Computes camera-distance-scaled shell count in [minLodShellCount, baseShellCount].
        /// </summary>
        internal static int ComputeLodShellCount(
            int baseShellCount,
            bool enableDistanceLod,
            float distanceToCamera,
            float nearDistance,
            float farDistance,
            int minLodShellCount)
        {
            int clampedBase = Mathf.Clamp(baseShellCount, MinShellCount, MaxShellCount);
            if (!enableDistanceLod)
                return clampedBase;

            int clampedMin = Mathf.Clamp(minLodShellCount, MinShellCount, clampedBase);
            if (clampedMin >= clampedBase)
                return clampedBase;

            float near = Mathf.Max(0.0f, nearDistance);
            float far = Mathf.Max(near + 0.001f, farDistance);
            if (distanceToCamera <= near)
                return clampedBase;
            if (distanceToCamera >= far)
                return clampedMin;

            float t = (distanceToCamera - near) / (far - near);
            int lodCount = Mathf.RoundToInt(Mathf.Lerp(clampedBase, clampedMin, t));
            return Mathf.Clamp(lodCount, clampedMin, clampedBase);
        }

        /// <summary>
        /// Fills <paramref name="destination"/>[0..shellCount-1] with shell heights in [0, maxHeight].
        /// When <paramref name="shellCount"/> == 1, writes 0 (solid base skin layer).
        /// When <paramref name="outsideInOrder"/> is true, writes h from maxHeight down to 0 so Early-Z
        /// rejects occluded inner shells first.
        /// </summary>
        internal static void FillShellHeights(
            float[] destination,
            int shellCount,
            float heightDistributionPower,
            bool outsideInOrder,
            float maxHeight = 1.0f)
        {
            int count = Mathf.Clamp(shellCount, MinShellCount, Mathf.Min(MaxShellCount, destination.Length));
            float clampedMax = Mathf.Clamp01(maxHeight);
            float power = Mathf.Clamp(heightDistributionPower, 0.5f, 2.0f);

            if (count == 1)
            {
                destination[0] = 0.0f;
                return;
            }

            float invDenom = 1.0f / (count - 1);
            for (int i = 0; i < count; i++)
            {
                float t = i * invDenom;
                float u = outsideInOrder ? (1.0f - t) : t;
                float h;
                if (u <= 0.0f)
                    h = 0.0f;
                else if (u >= 1.0f)
                    h = clampedMax;
                else
                    h = Mathf.Pow(u, power) * clampedMax;

                destination[i] = h;
            }
        }

        /// <summary>
        /// Upper bound on world-space vertex extrusion produced by <c>VividEvaluateFurVertexOffset</c>.
        /// </summary>
        internal static float ComputeMaxFurWorldExtrusion(
            float furLength,
            Vector3 gravityWS,
            float gravityStrength,
            float windStrength,
            float extraPadding)
        {
            float length = Mathf.Max(0.0f, furLength);
            float gravityMag = gravityWS.magnitude;
            float gravityOffset = Mathf.Max(0.0f, gravityStrength) * gravityMag;
            float windOffset = Mathf.Max(0.0f, windStrength) * k_MaxWindGustAmplitude;
            return length * (1.0f + gravityOffset + windOffset) + Mathf.Max(0.0f, extraPadding);
        }

        /// <summary>
        /// Transforms <paramref name="localBounds"/> into world space and expands extents symmetrically by
        /// <paramref name="maxWorldExtrusion"/> so frustum and shadow culling never clip extruded fur tips.
        /// </summary>
        internal static Bounds ComputeExpandedWorldBounds(
            Bounds localBounds,
            Matrix4x4 localToWorld,
            float maxWorldExtrusion)
        {
            Vector3 centerWS = localToWorld.MultiplyPoint3x4(localBounds.center);
            Vector3 ext = localBounds.extents;
            float pad = Mathf.Max(0.0f, maxWorldExtrusion);

            float ex = Mathf.Abs(localToWorld.m00) * ext.x
                     + Mathf.Abs(localToWorld.m01) * ext.y
                     + Mathf.Abs(localToWorld.m02) * ext.z
                     + pad;
            float ey = Mathf.Abs(localToWorld.m10) * ext.x
                     + Mathf.Abs(localToWorld.m11) * ext.y
                     + Mathf.Abs(localToWorld.m12) * ext.z
                     + pad;
            float ez = Mathf.Abs(localToWorld.m20) * ext.x
                     + Mathf.Abs(localToWorld.m21) * ext.y
                     + Mathf.Abs(localToWorld.m22) * ext.z
                     + pad;

            return new Bounds(centerWS, new Vector3(ex * 2.0f, ey * 2.0f, ez * 2.0f));
        }

        /// <summary>
        /// Runs the complete steady-state per-camera preparation path (bounds, distance LOD, shell height upload,
        /// instance buffer fill) without submitting a GPU draw call. Used by allocation regression tests.
        /// </summary>
        internal void PrepareFrameDataForTest(
            Vector3 cameraPositionWS,
            Matrix4x4 localToWorld,
            Bounds localBounds,
            float maxWorldExtrusion,
            int frameCount,
            out int visualShellCount,
            out int shadowShellCount,
            out Bounds worldBounds)
        {
            EnsureRuntimeStorage();
            UpdateTransformHistory(localToWorld, frameCount);

            worldBounds = ComputeExpandedWorldBounds(localBounds, localToWorld, maxWorldExtrusion);
            float distance = Vector3.Distance(cameraPositionWS, worldBounds.center);
            visualShellCount = ComputeLodShellCount(
                m_ShellCount,
                m_EnableDistanceLod,
                distance,
                m_LodNearDistance,
                m_LodFarDistance,
                m_MinLodShellCount);

            bool shouldCastShadows = m_ShadowCastingMode != ShadowCastingMode.Off;
            bool useSparseShadows = shouldCastShadows && m_SparseShadowShells;
            shadowShellCount = shouldCastShadows
                ? (useSparseShadows ? Mathf.Min(Mathf.Clamp(m_ShadowShellCount, MinShellCount, MaxShellCount), visualShellCount) : visualShellCount)
                : 0;

            PopulateInstanceData(visualShellCount, m_CurrentLocalToWorld, m_PreviousLocalToWorld);
            EnsureVisualShellHeightsUploaded(visualShellCount, m_HeightDistributionPower, m_OutsideInOrder);
            if (useSparseShadows && shadowShellCount > 0)
                EnsureShadowShellHeightsUploaded(shadowShellCount, m_ShadowMaxHeight, m_HeightDistributionPower, m_OutsideInOrder);
        }

        internal ReadOnlySpan<float> GetVisualShellHeightsForTest(int count)
        {
            EnsureRuntimeStorage();
            return new ReadOnlySpan<float>(m_VisualShellHeights, 0, Mathf.Clamp(count, 0, MaxShellCount));
        }

        internal ReadOnlySpan<float> GetShadowShellHeightsForTest(int count)
        {
            EnsureRuntimeStorage();
            return new ReadOnlySpan<float>(m_ShadowShellHeights, 0, Mathf.Clamp(count, 0, MaxShellCount));
        }

        internal ShellInstanceData GetInstanceDataForTest(int index)
        {
            EnsureRuntimeStorage();
            return m_InstanceData[index];
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Mesh mesh = ResolveMesh();
            if (mesh == null)
                return;

            float extrusion = m_Material != null
                ? EvaluateMaterialWorldExtrusion(m_Material)
                : m_ExtraBoundsPadding;
            Bounds bounds = ComputeExpandedWorldBounds(mesh.bounds, transform.localToWorldMatrix, extrusion);

            Color prevColor = Gizmos.color;
            Gizmos.color = new Color(0.95f, 0.72f, 0.30f, 0.45f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
            Gizmos.color = prevColor;
        }
#endif
    }
}

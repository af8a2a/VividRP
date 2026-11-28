using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    /// <summary>
    /// Scene component binding a HairGroomAsset to both:
    /// 1) Realtime Strand Visibility Buffer rendering (via HairGroomSystem), and
    /// 2) Optional runtime-only DXR DOTS proxy mesh for Reference Path Tracing ground truth.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class HairGroomRenderer : MonoBehaviour
    {
        private const string DotsProxyName = "__HairDotsPathTracingProxy";

        private static readonly int BaseColorId = Shader.PropertyToID("_HairBaseColor");
        private static readonly int AbsorptionModelId = Shader.PropertyToID("_HairAbsorptionModel");
        private static readonly int MelaninId = Shader.PropertyToID("_HairMelanin");
        private static readonly int MelaninRednessId = Shader.PropertyToID("_HairMelaninRedness");
        private static readonly int LongitudinalRoughnessId = Shader.PropertyToID("_HairLongitudinalRoughness");
        private static readonly int AzimuthalRoughnessId = Shader.PropertyToID("_HairAzimuthalRoughness");
        private static readonly int IorId = Shader.PropertyToID("_HairIor");
        private static readonly int CuticleAngleDegreesId = Shader.PropertyToID("_HairCuticleAngleDegrees");
        private static readonly int FresnelApproximationId = Shader.PropertyToID("_HairFresnelApproximation");

        [Header("Groom Asset")]
        [SerializeField] private HairGroomAsset m_GroomAsset;
        [SerializeField, Min(0.05f)] private float m_RadiusScale = 1.0f;
        [SerializeField, Min(0)] private int m_MaxActiveStrands;

        [Header("Unified Hair Shading")]
        [SerializeField] private HairGroomShadingParameters m_Shading = HairGroomShadingParameters.CreateDefault();

        [Header("Reference Path Tracing (DXR DOTS Proxy)")]
        [SerializeField] private bool m_EnablePathTracingDotsProxy = true;
        [SerializeField] private Material m_PathTracingMaterial;
        [SerializeField, Min(0)] private int m_MaxPathTracingSegments;

        [NonSerialized] private GameObject m_DotsProxyObject;
        [NonSerialized] private MeshFilter m_DotsMeshFilter;
        [NonSerialized] private MeshRenderer m_DotsMeshRenderer;
        [NonSerialized] private HairDotsGpuStream m_DotsGpuStream;
        [NonSerialized] private GraphicsBuffer m_DotsSegmentBuffer;
        [NonSerialized] private HairGroomAsset m_BuiltAsset;
        [NonSerialized] private int m_BuiltAssetVersion = -1;
        [NonSerialized] private int m_BuiltSegmentCount;
        [NonSerialized] private float m_BuiltRadiusScale = -1.0f;
        [NonSerialized] private bool m_DotsDirty = true;
        [NonSerialized] private int m_UpdateFrameCounter;

        public HairGroomAsset GroomAsset
        {
            get => m_GroomAsset;
            set
            {
                if (ReferenceEquals(m_GroomAsset, value))
                    return;
                m_GroomAsset = value;
                m_DotsDirty = true;
            }
        }

        public float RadiusScale
        {
            get => Mathf.Max(0.05f, m_RadiusScale);
            set
            {
                float clamped = Mathf.Max(0.05f, value);
                if (Mathf.Approximately(m_RadiusScale, clamped))
                    return;
                m_RadiusScale = clamped;
                m_DotsDirty = true;
            }
        }

        public int ActiveStrandLimit => m_MaxActiveStrands;

        public HairGroomShadingParameters Shading
        {
            get => m_Shading;
            set
            {
                m_Shading = value;
                SyncPathTracingMaterial();
            }
        }

        public bool EnablePathTracingDotsProxy
        {
            get => m_EnablePathTracingDotsProxy;
            set
            {
                if (m_EnablePathTracingDotsProxy == value)
                    return;
                m_EnablePathTracingDotsProxy = value;
                m_DotsDirty = true;
                UpdateDotsProxyState();
            }
        }

        public Material PathTracingMaterial
        {
            get => m_PathTracingMaterial;
            set
            {
                m_PathTracingMaterial = value;
                if (m_DotsMeshRenderer != null)
                    m_DotsMeshRenderer.sharedMaterial = value;
                SyncPathTracingMaterial();
            }
        }

        public Mesh RuntimeDotsProxyMesh => m_DotsGpuStream?.Mesh;
        public int RuntimeDotsSegmentCount => m_BuiltSegmentCount;

        private void OnEnable()
        {
            HairGroomSystem.Register(this);
            m_DotsDirty = true;
            UpdateDotsProxyState();
        }

        private void OnDisable()
        {
            HairGroomSystem.Unregister(this);
            SetDotsProxyVisible(false);
        }

        private void OnDestroy()
        {
            HairGroomSystem.Unregister(this);
            ReleaseDotsProxyResources();
        }

        private void OnValidate()
        {
            m_RadiusScale = Mathf.Max(0.05f, m_RadiusScale);
            m_MaxActiveStrands = Mathf.Max(0, m_MaxActiveStrands);
            m_MaxPathTracingSegments = Mathf.Max(0, m_MaxPathTracingSegments);
            m_Shading.LongitudinalRoughness = Mathf.Clamp(m_Shading.LongitudinalRoughness, 0.05f, 1.0f);
            m_Shading.AzimuthalRoughness = Mathf.Clamp(m_Shading.AzimuthalRoughness, 0.05f, 1.0f);
            m_Shading.Ior = Mathf.Clamp(m_Shading.Ior, 1.2f, 2.0f);
            m_Shading.CuticleAngleDegrees = Mathf.Clamp(m_Shading.CuticleAngleDegrees, 0.0f, 10.0f);
            m_DotsDirty = true;
        }

        private void LateUpdate()
        {
            if (m_DotsDirty)
                UpdateDotsProxyState();
        }

        /// <summary>
        /// Forces immediate GPU DOTS proxy rebuild (used by LookDev harnesses and tests).
        /// </summary>
        public void RebuildDotsProxyNow()
        {
            m_DotsDirty = true;
            UpdateDotsProxyState();
        }

        public void SyncPathTracingMaterial()
        {
            if (m_PathTracingMaterial == null)
                return;

            m_PathTracingMaterial.SetColor(BaseColorId, m_Shading.BaseColor);
            m_PathTracingMaterial.SetFloat(AbsorptionModelId, (float)m_Shading.AbsorptionMode);
            m_PathTracingMaterial.SetFloat(MelaninId, m_Shading.Melanin);
            m_PathTracingMaterial.SetFloat(MelaninRednessId, m_Shading.MelaninRedness);
            m_PathTracingMaterial.SetFloat(LongitudinalRoughnessId, m_Shading.LongitudinalRoughness);
            m_PathTracingMaterial.SetFloat(AzimuthalRoughnessId, m_Shading.AzimuthalRoughness);
            m_PathTracingMaterial.SetFloat(IorId, m_Shading.Ior);
            m_PathTracingMaterial.SetFloat(CuticleAngleDegreesId, m_Shading.CuticleAngleDegrees);
            m_PathTracingMaterial.SetFloat(FresnelApproximationId, 1.0f);
        }

        private void UpdateDotsProxyState()
        {
            m_DotsDirty = false;
            if (!isActiveAndEnabled
                || !m_EnablePathTracingDotsProxy
                || m_GroomAsset == null
                || !m_GroomAsset.HasBinaryData
                || m_GroomAsset.SegmentCount <= 0)
            {
                SetDotsProxyVisible(false);
                return;
            }

            int targetSegmentCount = m_GroomAsset.SegmentCount;
            if (m_MaxPathTracingSegments > 0 && m_MaxPathTracingSegments < targetSegmentCount)
                targetSegmentCount = m_MaxPathTracingSegments;

            bool needsRebuild =
                m_DotsGpuStream == null
                || m_DotsSegmentBuffer == null
                || !m_DotsSegmentBuffer.IsValid()
                || !ReferenceEquals(m_BuiltAsset, m_GroomAsset)
                || m_BuiltAssetVersion != m_GroomAsset.ContentVersion
                || m_BuiltSegmentCount != targetSegmentCount
                || !Mathf.Approximately(m_BuiltRadiusScale, RadiusScale);

            EnsureDotsProxyGameObject();
            if (m_DotsProxyObject == null)
                return;

            if (needsRebuild)
                BuildDotsProxyMesh(targetSegmentCount);

            if (m_DotsMeshRenderer != null)
            {
                if (m_PathTracingMaterial != null)
                    m_DotsMeshRenderer.sharedMaterial = m_PathTracingMaterial;
                SyncPathTracingMaterial();
                m_DotsMeshRenderer.enabled = true;
            }
        }

        private void BuildDotsProxyMesh(int targetSegmentCount)
        {
            var segments = new HairGpuStrandSegment[targetSegmentCount];
            int written = m_GroomAsset.BuildGpuStrandSegments(segments, targetSegmentCount, 1.0f);
            if (written <= 0)
            {
                SetDotsProxyVisible(false);
                return;
            }

            if (m_DotsSegmentBuffer == null
                || !m_DotsSegmentBuffer.IsValid()
                || m_DotsSegmentBuffer.count < written)
            {
                m_DotsSegmentBuffer?.Dispose();
                m_DotsSegmentBuffer = HairDotsGpuStream.CreateSimulationBuffer(
                    written,
                    $"{m_GroomAsset.name}_DotsSegments");
            }

            m_DotsSegmentBuffer.SetData(segments, 0, 0, written);

            if (m_DotsGpuStream == null)
            {
                var runtimeMesh = new Mesh
                {
                    name = $"{m_GroomAsset.name}_RuntimeDOTS",
                    hideFlags = HideFlags.DontSave,
                };
                m_DotsGpuStream = new HairDotsGpuStream(written, runtimeMesh);
            }

            Bounds conservativeBounds = m_GroomAsset.Bounds;
            float expand = Mathf.Max(m_GroomAsset.DefaultRadius * RadiusScale * 4.0f, 0.01f);
            conservativeBounds.Expand(expand);

            var cmd = CommandBufferPool.Get("HairGroomRenderer.ExpandDotsProxy");
            try
            {
                m_DotsGpuStream.RecordGpuUpdate(
                    cmd,
                    m_DotsSegmentBuffer,
                    written,
                    conservativeBounds,
                    ++m_UpdateFrameCounter,
                    topologyVersion: m_GroomAsset.ContentVersion,
                    forceHistoryReset: true,
                    radiusScale: RadiusScale);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            finally
            {
                CommandBufferPool.Release(cmd);
            }

            m_DotsGpuStream.Mesh.hideFlags = HideFlags.DontSave;
            m_DotsMeshFilter.sharedMesh = m_DotsGpuStream.Mesh;
            m_BuiltAsset = m_GroomAsset;
            m_BuiltAssetVersion = m_GroomAsset.ContentVersion;
            m_BuiltSegmentCount = written;
            m_BuiltRadiusScale = RadiusScale;
        }

        private void EnsureDotsProxyGameObject()
        {
            if (m_DotsProxyObject == null)
            {
                Transform existing = transform.Find(DotsProxyName);
                if (existing != null)
                {
                    m_DotsProxyObject = existing.gameObject;
                }
                else
                {
                    m_DotsProxyObject = new GameObject(DotsProxyName);
                    m_DotsProxyObject.transform.SetParent(transform, false);
                    m_DotsProxyObject.transform.localPosition = Vector3.zero;
                    m_DotsProxyObject.transform.localRotation = Quaternion.identity;
                    m_DotsProxyObject.transform.localScale = Vector3.one;
                }

                m_DotsProxyObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                m_DotsProxyObject.layer = gameObject.layer;
            }

            if (m_DotsMeshFilter == null && !m_DotsProxyObject.TryGetComponent(out m_DotsMeshFilter))
                m_DotsMeshFilter = m_DotsProxyObject.AddComponent<MeshFilter>();

            if (m_DotsMeshRenderer == null && !m_DotsProxyObject.TryGetComponent(out m_DotsMeshRenderer))
                m_DotsMeshRenderer = m_DotsProxyObject.AddComponent<MeshRenderer>();

            m_DotsMeshRenderer.shadowCastingMode = ShadowCastingMode.On;
            m_DotsMeshRenderer.receiveShadows = true;
            m_DotsMeshRenderer.rayTracingMode = RayTracingMode.DynamicGeometry;
            m_DotsMeshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
        }

        private void SetDotsProxyVisible(bool visible)
        {
            if (m_DotsMeshRenderer != null)
                m_DotsMeshRenderer.enabled = visible;
        }

        private void ReleaseDotsProxyResources()
        {
            m_DotsGpuStream?.Dispose();
            m_DotsGpuStream = null;
            m_DotsSegmentBuffer?.Dispose();
            m_DotsSegmentBuffer = null;

            if (m_DotsProxyObject != null)
            {
                if (Application.isPlaying)
                    Destroy(m_DotsProxyObject);
                else
                    DestroyImmediate(m_DotsProxyObject);
                m_DotsProxyObject = null;
                m_DotsMeshFilter = null;
                m_DotsMeshRenderer = null;
            }

            m_BuiltAsset = null;
            m_BuiltAssetVersion = -1;
            m_BuiltSegmentCount = 0;
            m_BuiltRadiusScale = -1.0f;
        }
    }
}

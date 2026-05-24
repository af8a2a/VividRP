using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class VirtualTextureDemoController : MonoBehaviour
    {
        private const string DemoSurfaceName = "VT Demo Surface";
        private const string DemoShaderName = "VividRP/Material/VirtualTextureDemo";
        private const string DefaultSourceTextureAssetPath = "Assets/vt/UVTest.jpg";
        private static readonly int s_BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int s_MainTexId = Shader.PropertyToID("_MainTex");

        private enum DemoProducerMode
        {
            CheckerSource = 0,
            ProceduralPageDebug = 1,
            SourceTexture = 2,
        }

        [SerializeField]
        private string m_SpaceName = "VT Demo Space";

        [SerializeField, Min(16)]
        private int m_PageSize = 128;

        [SerializeField, Min(0)]
        private int m_BorderSize = 4;

        [SerializeField, Min(1)]
        private int m_VirtualPageCountX = 16;

        [SerializeField, Min(1)]
        private int m_VirtualPageCountY = 16;

        [SerializeField, Min(1)]
        private int m_MipCount = 5;

        [SerializeField, Min(2)]
        private int m_CachePageCount = 24;

        [SerializeField, Min(1)]
        private int m_MaxUploadsPerFrame = 4;

        [SerializeField, Min(16)]
        private int m_FeedbackCapacity = 512;

        [SerializeField]
        private DemoProducerMode m_ProducerMode = DemoProducerMode.SourceTexture;

        [SerializeField]
        private Texture2D m_SourceTexture;

        [SerializeField]
        private string m_SourceTextureAssetPath = DefaultSourceTextureAssetPath;

        [SerializeField]
        private bool m_AutoSizeFromSourceTexture = true;

        [SerializeField]
        private bool m_CreateDemoSurface = true;

        [SerializeField]
        private Vector2 m_SurfaceSize = new(10f, 10f);

        [SerializeField]
        private MeshRenderer m_DemoRenderer;

        private Material m_RuntimeMaterial;
        private VTTexture2DPageProducer m_TextureProducer;
        private int m_SpaceId;

        public int SpaceId => m_SpaceId;

        private void OnEnable()
        {
            EnsureRegisteredSpace();
            EnsureDemoSurface();
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled)
                return;

            EnsureRegisteredSpace();
            EnsureDemoSurface();
        }

        private void OnDisable()
        {
            if (m_SpaceId > 0)
            {
                VirtualTextureSystem.UnregisterAddressSpace(m_SpaceId);
                m_SpaceId = 0;
            }

            m_TextureProducer = null;

            if (m_RuntimeMaterial != null)
            {
                CoreUtils.Destroy(m_RuntimeMaterial);
                m_RuntimeMaterial = null;
            }
        }

        private void EnsureRegisteredSpace()
        {
            try
            {
                m_SpaceId = VirtualTextureSystem.RegisterOrReconfigureAddressSpace(
                    CreateDescriptor(),
                    ResolveProducer());
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[VividRP] Failed to register VT demo space '{m_SpaceName}': {exception.Message}", this);
            }
        }

        private void EnsureDemoSurface()
        {
            if (!m_CreateDemoSurface)
                return;

            if (m_DemoRenderer == null)
            {
                Transform existingSurface = transform.Find(DemoSurfaceName);
                GameObject surfaceObject = existingSurface != null
                    ? existingSurface.gameObject
                    : GameObject.CreatePrimitive(PrimitiveType.Quad);
                surfaceObject.name = DemoSurfaceName;
                surfaceObject.transform.SetParent(transform, false);
                surfaceObject.transform.localPosition = Vector3.zero;
                surfaceObject.transform.localRotation = Quaternion.identity;

                if (surfaceObject.TryGetComponent(out Collider collider))
                {
                    if (Application.isPlaying)
                        Destroy(collider);
                    else
                        DestroyImmediate(collider);
                }

                if (!surfaceObject.TryGetComponent(out MeshRenderer renderer))
                    renderer = surfaceObject.AddComponent<MeshRenderer>();

                if (!surfaceObject.TryGetComponent(out MeshFilter _))
                    surfaceObject.AddComponent<MeshFilter>();

                m_DemoRenderer = renderer;
            }

            Transform surfaceTransform = m_DemoRenderer.transform;
            surfaceTransform.localScale = new Vector3(m_SurfaceSize.x, m_SurfaceSize.y, 1f);

            Shader shader = Shader.Find(DemoShaderName);
            if (shader == null)
                return;

            if (m_RuntimeMaterial == null || m_RuntimeMaterial.shader != shader)
            {
                if (m_RuntimeMaterial != null)
                    CoreUtils.Destroy(m_RuntimeMaterial);

                m_RuntimeMaterial = CoreUtils.CreateEngineMaterial(shader);
                m_RuntimeMaterial.name = $"{nameof(VirtualTextureDemoController)}_Material";
            }

            if (m_DemoRenderer.sharedMaterial != m_RuntimeMaterial)
                m_DemoRenderer.sharedMaterial = m_RuntimeMaterial;

            SyncSourceTextureToMaterial();
        }

        private VirtualTextureSpaceDesc CreateDescriptor()
        {
            int pageSize = Mathf.Max(16, m_PageSize);
            int virtualPageCountX = Mathf.Max(1, m_VirtualPageCountX);
            int virtualPageCountY = Mathf.Max(1, m_VirtualPageCountY);
            int mipCount = Mathf.Max(1, m_MipCount);
            Texture2D sourceTexture = ResolveSourceTexture();

            if (m_ProducerMode == DemoProducerMode.SourceTexture
                && m_AutoSizeFromSourceTexture
                && sourceTexture != null)
            {
                virtualPageCountX = Mathf.Max(1, Mathf.CeilToInt(sourceTexture.width / (float)pageSize));
                virtualPageCountY = Mathf.Max(1, Mathf.CeilToInt(sourceTexture.height / (float)pageSize));
                mipCount = ComputeMipCount(virtualPageCountX, virtualPageCountY);
            }

            return new VirtualTextureSpaceDesc(
                string.IsNullOrWhiteSpace(m_SpaceName) ? "VT Demo Space" : m_SpaceName,
                pageSize: pageSize,
                borderSize: Mathf.Max(0, m_BorderSize),
                virtualPageCountX: virtualPageCountX,
                virtualPageCountY: virtualPageCountY,
                mipCount: mipCount,
                cachePageCount: Mathf.Max(2, m_CachePageCount),
                graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm,
                maxUploadsPerFrame: Mathf.Max(1, m_MaxUploadsPerFrame),
                feedbackCapacity: Mathf.Max(16, m_FeedbackCapacity));
        }

        private VTProducer ResolveProducer()
        {
            if (m_ProducerMode == DemoProducerMode.ProceduralPageDebug)
                return VTProceduralPageProducer.Instance;

            if (m_ProducerMode == DemoProducerMode.CheckerSource)
                return VTCheckerSourcePageProducer.Instance;

            Texture2D sourceTexture = ResolveSourceTexture();
            if (sourceTexture == null)
                return VTCheckerSourcePageProducer.Instance;

            if (m_TextureProducer == null || !ReferenceEquals(m_TextureProducer.SourceTexture, sourceTexture))
                m_TextureProducer = new VTTexture2DPageProducer(sourceTexture);

            return m_TextureProducer;
        }

        private Texture2D ResolveSourceTexture()
        {
            if (m_SourceTexture != null)
                return m_SourceTexture;

#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(m_SourceTextureAssetPath))
                return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(m_SourceTextureAssetPath);
#endif

            return null;
        }

        private void SyncSourceTextureToMaterial()
        {
            if (m_RuntimeMaterial == null)
                return;

            Texture2D sourceTexture = m_ProducerMode == DemoProducerMode.SourceTexture
                ? ResolveSourceTexture()
                : null;

            if (m_RuntimeMaterial.HasProperty(s_BaseMapId))
                m_RuntimeMaterial.SetTexture(s_BaseMapId, sourceTexture);

            if (m_RuntimeMaterial.HasProperty(s_MainTexId))
                m_RuntimeMaterial.SetTexture(s_MainTexId, sourceTexture);
        }

        private static int ComputeMipCount(int virtualPageCountX, int virtualPageCountY)
        {
            int maxPageCount = Mathf.Max(1, Mathf.Max(virtualPageCountX, virtualPageCountY));
            int mipCount = 1;
            while ((maxPageCount >>= 1) > 0 && mipCount < VirtualTextureFeedbackProcessor.MaxMipCount)
                mipCount += 1;

            return mipCount;
        }
    }
}

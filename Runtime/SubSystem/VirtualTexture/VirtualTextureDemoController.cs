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
        private bool m_CreateDemoSurface = true;

        [SerializeField]
        private Vector2 m_SurfaceSize = new(10f, 10f);

        [SerializeField]
        private MeshRenderer m_DemoRenderer;

        private Material m_RuntimeMaterial;
        private int m_SpaceId;

        public int SpaceId => m_SpaceId;

        private void OnEnable()
        {
            EnsureRegisteredSpace();
            EnsureDemoSurface();
        }

        private void OnValidate()
        {
            EnsureRegisteredSpace();
            EnsureDemoSurface();
        }

        private void OnDisable()
        {
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
                m_SpaceId = VirtualTextureSystem.RegisterAddressSpace(CreateDescriptor(), VTProceduralPageProducer.Instance);
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
        }

        private VirtualTextureSpaceDesc CreateDescriptor()
        {
            return new VirtualTextureSpaceDesc(
                string.IsNullOrWhiteSpace(m_SpaceName) ? "VT Demo Space" : m_SpaceName,
                pageSize: Mathf.Max(16, m_PageSize),
                borderSize: Mathf.Max(0, m_BorderSize),
                virtualPageCountX: Mathf.Max(1, m_VirtualPageCountX),
                virtualPageCountY: Mathf.Max(1, m_VirtualPageCountY),
                mipCount: Mathf.Max(1, m_MipCount),
                cachePageCount: Mathf.Max(2, m_CachePageCount),
                graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm,
                maxUploadsPerFrame: Mathf.Max(1, m_MaxUploadsPerFrame),
                feedbackCapacity: Mathf.Max(16, m_FeedbackCapacity));
        }
    }
}

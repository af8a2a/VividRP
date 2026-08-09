using UnityEngine;

namespace VividRP.Runtime.SubSystem.Decal
{
    [ExecuteAlways]
    public class DecalProjector : MonoBehaviour, IBoundProxyProvider, IBoundProxyWorldDataProvider
    {
        [SerializeField] private BoundProxyShape m_BoundProxy = CreateDefaultBoundProxy();

        [SerializeField] [Min(0.0f)] private float m_BlendDistance;

        [Header("Decal Material")]
        [SerializeField] private Texture2D m_BaseColorTexture;
        [SerializeField] private Texture2D m_NormalTexture;
        [SerializeField] private Texture2D m_MetallicTexture;
        [SerializeField] private Texture2D m_RoughnessTexture;
        [SerializeField] private Color m_BaseColor = Color.white;
        [SerializeField] [Range(0.0f, 1.0f)] private float m_Metallic;
        [SerializeField] [Range(0.0f, 1.0f)] private float m_Roughness = 0.5f;

        public Texture2D BaseColorTexture
        {
            get => m_BaseColorTexture;
            set => m_BaseColorTexture = value;
        }

        public Texture2D NormalTexture
        {
            get => m_NormalTexture;
            set => m_NormalTexture = value;
        }

        public Texture2D MetallicTexture
        {
            get => m_MetallicTexture;
            set => m_MetallicTexture = value;
        }

        public Texture2D RoughnessTexture
        {
            get => m_RoughnessTexture;
            set => m_RoughnessTexture = value;
        }

        public Color BaseColor
        {
            get => m_BaseColor;
            set => m_BaseColor = value;
        }

        public float Metallic
        {
            get => m_Metallic;
            set => m_Metallic = Mathf.Clamp01(value);
        }

        public float Roughness
        {
            get => m_Roughness;
            set => m_Roughness = Mathf.Clamp01(value);
        }

        public float BlendDistance => m_BlendDistance;

        private void OnEnable()
        {
            DecalSystem.Register(this);
        }

        private void OnDisable()
        {
            DecalSystem.Unregister(this);
        }

        public BoundProxyFeature BoundProxyFeature => BoundProxyFeature.Decal;

        public bool IsBoundProxyActive => isActiveAndEnabled;

        public Transform BoundProxyTransform => transform;

        public BoundProxyShape BoundProxyShape
        {
            get
            {
                BoundProxyShape shape = m_BoundProxy;
                shape.Sanitize();
                shape.center = Vector3.zero;
                return shape;
            }
        }

        private static BoundProxyShape CreateDefaultBoundProxy()
        {
            return new BoundProxyShape
            {
                shape = BoundProxyShapeType.Box,
                size = new Vector3(1.0f, 1.0f, 1.0f),
            };
        }

        public bool TryCreateBoundProxyWorldData(out BoundProxyWorldData worldData)
        {
            if (!IsBoundProxyActive)
            {
                worldData = default;
                return false;
            }

            worldData = transform.CreateWorldData(
                BoundProxyFeature,
                BoundProxyShape,
                transform.GetEntityId());
            return true;
        }
    }
}

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
        [SerializeField] private Color m_BaseColor = Color.white;

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

        public Color BaseColor
        {
            get => m_BaseColor;
            set => m_BaseColor = value;
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
                size = new Vector3(10.0f, 5.0f, 10.0f),
            };
        }

        public bool TryCreateBoundProxyWorldData(out BoundProxyWorldData worldData)
        {
            if (!IsBoundProxyActive)
            {
                worldData = default;
                return false;
            }

            BoundProxyShape shape = BoundProxyShape;
            Vector3 boxSize = shape.GetSanitizedSize();
            float sphereRadius = shape.GetSanitizedRadius();
            Bounds worldAabb = shape.shape == BoundProxyShapeType.Sphere
                ? new Bounds(transform.position, Vector3.one * (sphereRadius * 2.0f))
                : new Bounds(transform.position, boxSize);

            worldData = new BoundProxyWorldData
            {
                entityId = transform.GetEntityId(),
                feature = BoundProxyFeature,
                shape = shape.shape,
                worldCenter = transform.position,
                worldRotation = Quaternion.identity,
                boxSize = boxSize,
                sphereRadius = sphereRadius,
                worldAabb = worldAabb,
            };
            return true;
        }
    }
}
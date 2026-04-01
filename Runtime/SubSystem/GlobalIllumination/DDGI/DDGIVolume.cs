using UnityEngine;

namespace VividRP.Runtime
{
    [DisallowMultipleComponent]
    public class DDGIVolume : MonoBehaviour, IBoundProxyProvider, IBoundProxyWorldDataProvider
    {
        [SerializeField]
        private BoundProxyShape m_BoundProxy = CreateDefaultBoundProxy();

        [SerializeField]
        [Min(0.0f)]
        private float m_BlendDistance;

        public BoundProxyFeature BoundProxyFeature => BoundProxyFeature.DDGIVolume;

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

        public BoundProxyShape BlendInnerBoundProxyShape => ShrinkShape(BoundProxyShape, BlendDistance);

        public float BlendDistance => Mathf.Max(m_BlendDistance, 0.0f);

        public Bounds LocalBounds => BoundProxyShape.GetLocalBounds();

        public Bounds BlendInnerLocalBounds => BlendInnerBoundProxyShape.GetLocalBounds();

        public Bounds WorldBounds
        {
            get
            {
                TryCreateBoundProxyWorldData(out BoundProxyWorldData worldData);
                return worldData.worldAabb;
            }
        }

        private void Reset()
        {
            m_BoundProxy = CreateDefaultBoundProxy();
        }

        internal void SetBoundProxyShape(BoundProxyShape shape)
        {
            shape.Sanitize();
            shape.center = Vector3.zero;
            m_BoundProxy = shape;
        }

        private void OnValidate()
        {
            m_BoundProxy.Sanitize();
            m_BoundProxy.center = Vector3.zero;
            m_BlendDistance = BlendDistance;
        }

        public float EvaluateBlendFactor(Vector3 worldPosition)
        {
            if (!IsBoundProxyActive)
            {
                return 0.0f;
            }

            float signedDistance = CalculateSignedDistanceToBoundary(worldPosition, BoundProxyShape);
            if (signedDistance < 0.0f)
            {
                return 0.0f;
            }

            float blendDistance = BlendDistance;
            if (blendDistance <= 0.0f)
            {
                return 1.0f;
            }

            return Mathf.Clamp01(signedDistance / blendDistance);
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

        private static BoundProxyShape ShrinkShape(BoundProxyShape shape, float blendDistance)
        {
            shape.Sanitize();
            if (blendDistance <= 0.0f)
            {
                return shape;
            }

            if (shape.shape == BoundProxyShapeType.Sphere)
            {
                shape.radius = Mathf.Max(shape.radius - blendDistance, 0.0f);
                return shape;
            }

            Vector3 size = shape.GetSanitizedSize();
            float shrinkAmount = blendDistance * 2.0f;
            shape.size = new Vector3(
                Mathf.Max(size.x - shrinkAmount, 0.0f),
                Mathf.Max(size.y - shrinkAmount, 0.0f),
                Mathf.Max(size.z - shrinkAmount, 0.0f));
            return shape;
        }

        private float CalculateSignedDistanceToBoundary(Vector3 worldPosition, BoundProxyShape shape)
        {
            shape.Sanitize();
            Vector3 localPosition = worldPosition - transform.position;
            if (shape.shape == BoundProxyShapeType.Sphere)
            {
                return shape.GetSanitizedRadius() - localPosition.magnitude;
            }

            Vector3 halfExtents = shape.GetSanitizedSize() * 0.5f;
            Vector3 absLocalPosition = Abs(localPosition);
            Vector3 outsideDelta = absLocalPosition - halfExtents;
            if (outsideDelta.x > 0.0f || outsideDelta.y > 0.0f || outsideDelta.z > 0.0f)
            {
                Vector3 outside = new Vector3(
                    Mathf.Max(outsideDelta.x, 0.0f),
                    Mathf.Max(outsideDelta.y, 0.0f),
                    Mathf.Max(outsideDelta.z, 0.0f));
                return -outside.magnitude;
            }

            return Mathf.Min(
                halfExtents.x - absLocalPosition.x,
                Mathf.Min(
                    halfExtents.y - absLocalPosition.y,
                    halfExtents.z - absLocalPosition.z));
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        private static BoundProxyShape CreateDefaultBoundProxy()
        {
            return new BoundProxyShape
            {
                shape = BoundProxyShapeType.Box,
                size = new Vector3(10.0f, 5.0f, 10.0f),
            };
        }
    }
}

using UnityEngine;

namespace VividRP.Runtime
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class DDGIVolume : MonoBehaviour, IBoundProxyProvider, IBoundProxyWorldDataProvider
    {
        [SerializeField]
        private BoundProxyShape m_BoundProxy = CreateDefaultBoundProxy();

        [SerializeField]
        [Min(0.0f)]
        private float m_BlendDistance;

        [SerializeField]
        private DDGIProfileId m_Profile = DDGIProfileId.Balanced;

        [SerializeField]
        private Vector3 m_ProbeSpacing = new Vector3(2.0f, 2.0f, 2.0f);

        [SerializeField]
        [Min(0.0f)]
        private float m_ProbeNormalBias = 0.2f;

        [SerializeField]
        [Min(0.0f)]
        private float m_ProbeViewBias = 0.2f;

        [SerializeField]
        [Min(0.0f)]
        private float m_ProbeMaxRayDistance = 30.0f;

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

        public DDGIProfileId Profile => m_Profile;

        public Vector3 ProbeSpacing => DDGIProfileTable.SanitizeProbeSpacing(m_ProbeSpacing);

        public float ProbeNormalBias => Mathf.Max(m_ProbeNormalBias, 0.0f);

        public float ProbeViewBias => Mathf.Max(m_ProbeViewBias, 0.0f);

        public float ProbeMaxRayDistance => Mathf.Max(m_ProbeMaxRayDistance, 0.0f);

        public Vector3Int ProbeCounts => CalculateProbeCounts(BoundProxyShape.GetLocalBounds().size, ProbeSpacing);

        public Vector3 ProbeGridOriginLocalPosition => CalculateProbeGridOriginLocalPosition(ProbeCounts, ProbeSpacing);

        public bool IsRuntimeSupported => IsBoundProxyActive && BoundProxyShape.shape == BoundProxyShapeType.Box;

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

        public Bounds ExpandedWorldBounds
        {
            get
            {
                Bounds bounds = WorldBounds;
                bounds.Expand(ProbeMaxRayDistance * 2.0f);
                return bounds;
            }
        }

        public Vector3 GetProbeLocalPosition(Vector3Int probeCoordinate)
        {
            Vector3Int probeCounts = ProbeCounts;
            Vector3Int clampedCoordinate = ClampProbeCoordinate(probeCoordinate, probeCounts);
            Vector3 origin = ProbeGridOriginLocalPosition;
            Vector3 spacing = ProbeSpacing;

            return new Vector3(
                origin.x + clampedCoordinate.x * spacing.x,
                origin.y + clampedCoordinate.y * spacing.y,
                origin.z + clampedCoordinate.z * spacing.z);
        }

        public Vector3 GetProbeWorldPosition(Vector3Int probeCoordinate)
        {
            return transform.position + GetProbeLocalPosition(probeCoordinate);
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
            m_ProbeSpacing = ProbeSpacing;
            m_ProbeNormalBias = ProbeNormalBias;
            m_ProbeViewBias = ProbeViewBias;
            m_ProbeMaxRayDistance = ProbeMaxRayDistance;
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

        private static Vector3 CalculateProbeGridOriginLocalPosition(Vector3Int probeCounts, Vector3 spacing)
        {
            return new Vector3(
                -(probeCounts.x - 1) * spacing.x * 0.5f,
                -(probeCounts.y - 1) * spacing.y * 0.5f,
                -(probeCounts.z - 1) * spacing.z * 0.5f);
        }

        private static Vector3Int ClampProbeCoordinate(Vector3Int probeCoordinate, Vector3Int probeCounts)
        {
            return new Vector3Int(
                Mathf.Clamp(probeCoordinate.x, 0, Mathf.Max(probeCounts.x - 1, 0)),
                Mathf.Clamp(probeCoordinate.y, 0, Mathf.Max(probeCounts.y - 1, 0)),
                Mathf.Clamp(probeCoordinate.z, 0, Mathf.Max(probeCounts.z - 1, 0)));
        }

        private static Vector3Int CalculateProbeCounts(Vector3 boundsSize, Vector3 spacing)
        {
            return new Vector3Int(
                Mathf.Max(1, Mathf.FloorToInt(boundsSize.x / spacing.x) + 1),
                Mathf.Max(1, Mathf.FloorToInt(boundsSize.y / spacing.y) + 1),
                Mathf.Max(1, Mathf.FloorToInt(boundsSize.z / spacing.z) + 1));
        }
    }
}

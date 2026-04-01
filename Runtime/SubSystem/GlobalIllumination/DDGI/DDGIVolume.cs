using UnityEngine;

namespace VividRP.Runtime
{
    [DisallowMultipleComponent]
    public class DDGIVolume : MonoBehaviour, IBoundProxyProvider
    {
        [SerializeField]
        private BoundProxyShape m_BoundProxy = CreateDefaultBoundProxy();

        public BoundProxyFeature BoundProxyFeature => BoundProxyFeature.DDGIVolume;

        public bool IsBoundProxyActive => isActiveAndEnabled;

        public Transform BoundProxyTransform => transform;

        public BoundProxyShape BoundProxyShape
        {
            get
            {
                BoundProxyShape shape = m_BoundProxy;
                shape.Sanitize();
                return shape;
            }
        }

        public Bounds LocalBounds => BoundProxyShape.GetLocalBounds();

        public Bounds WorldBounds => BoundProxyUtility.CalculateWorldAabb(transform, BoundProxyShape);

        private void Reset()
        {
            m_BoundProxy = CreateDefaultBoundProxy();
        }

        private void OnValidate()
        {
            m_BoundProxy.Sanitize();
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

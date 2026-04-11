using System;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    internal sealed class TestBoundProxyProvider : MonoBehaviour, IBoundProxyProvider
    {
        [SerializeField]
        private BoundProxyFeature m_Feature = BoundProxyFeature.Decal;

        [SerializeField]
        private bool m_IsBoundProxyActive = true;

        [SerializeField]
        private BoundProxyShape m_BoundProxy;

        public BoundProxyFeature BoundProxyFeature => m_Feature;

        public bool IsBoundProxyActive => m_IsBoundProxyActive && enabled && gameObject.activeInHierarchy;

        public Transform BoundProxyTransform => transform;

        public BoundProxyShape BoundProxyShape => m_BoundProxy;

        internal void SetFeature(BoundProxyFeature feature)
        {
            m_Feature = feature;
        }

        internal void SetProviderActive(bool isActive)
        {
            m_IsBoundProxyActive = isActive;
        }

        internal void SetShape(BoundProxyShape shape)
        {
            m_BoundProxy = shape;
        }
    }

    internal sealed class BoundProxyShapeHost : MonoBehaviour
    {
        [SerializeField]
        private BoundProxyShape m_BoundProxy;

        internal BoundProxyShape BoundProxy
        {
            get => m_BoundProxy;
            set => m_BoundProxy = value;
        }
    }

    [Serializable]
    internal sealed class BoundProxyShapeSerializationContainer
    {
        public BoundProxyShape shape;
    }
}

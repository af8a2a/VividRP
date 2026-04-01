using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public struct BoundProxyWorldData
    {
        public EntityId entityId;
        public BoundProxyFeature feature;
        public BoundProxyShapeType shape;
        public Vector3 worldCenter;
        public Quaternion worldRotation;
        public Vector3 boxSize;
        public float sphereRadius;
        public Bounds worldAabb;
    }
}

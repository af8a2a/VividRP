using System;
using UnityEngine;

namespace VividRP.Runtime
{
    [Serializable]
    public struct BoundProxyShape
    {
        public BoundProxyShapeType shape;
        public Vector3 center;

        [Min(0.0f)]
        public Vector3 size;

        [Min(0.0f)]
        public float radius;

        public readonly Vector3 GetSanitizedSize()
        {
            return new Vector3(
                Mathf.Max(size.x, 0.0f),
                Mathf.Max(size.y, 0.0f),
                Mathf.Max(size.z, 0.0f));
        }

        public readonly float GetSanitizedRadius()
        {
            return Mathf.Max(radius, 0.0f);
        }

        public readonly Bounds GetLocalBounds()
        {
            if (shape == BoundProxyShapeType.Sphere)
            {
                float sanitizedRadius = GetSanitizedRadius();
                return new Bounds(center, Vector3.one * (sanitizedRadius * 2.0f));
            }

            return new Bounds(center, GetSanitizedSize());
        }

        public void Sanitize()
        {
            size = GetSanitizedSize();
            radius = GetSanitizedRadius();
        }
    }
}

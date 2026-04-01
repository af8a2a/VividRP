using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public static class BoundProxyUtility
    {
        private const float IntersectionEpsilon = 1e-5f;

        public static bool TryCreateWorldData(IBoundProxyProvider provider, out BoundProxyWorldData worldData)
        {
            worldData = default;
            if (provider == null || !provider.IsBoundProxyActive)
            {
                return false;
            }

            Transform boundTransform = provider.BoundProxyTransform;
            if (boundTransform == null)
            {
                return false;
            }

            worldData = CreateWorldData(
                boundTransform,
                provider.BoundProxyFeature,
                provider.BoundProxyShape,
                boundTransform.GetEntityId());
            return true;
        }

        public static BoundProxyWorldData CreateWorldData(
            Transform boundTransform,
            BoundProxyFeature feature,
            BoundProxyShape shape,
            EntityId entityId = default)
        {
            shape.Sanitize();
            Vector3 worldCenter = CalculateWorldCenter(boundTransform, shape.center);
            Quaternion worldRotation = boundTransform != null ? boundTransform.rotation : Quaternion.identity;
            Vector3 boxSize = shape.GetSanitizedSize();
            float sphereRadius = shape.GetSanitizedRadius();

            return new BoundProxyWorldData
            {
                entityId = entityId,
                feature = feature,
                shape = shape.shape,
                worldCenter = worldCenter,
                worldRotation = worldRotation,
                boxSize = boxSize,
                sphereRadius = sphereRadius,
                worldAabb = CalculateWorldAabb(worldCenter, worldRotation, shape.shape, boxSize, sphereRadius),
            };
        }

        public static Bounds CalculateWorldAabb(Transform boundTransform, BoundProxyShape shape)
        {
            shape.Sanitize();
            Vector3 worldCenter = CalculateWorldCenter(boundTransform, shape.center);
            Quaternion worldRotation = boundTransform != null ? boundTransform.rotation : Quaternion.identity;
            return CalculateWorldAabb(
                worldCenter,
                worldRotation,
                shape.shape,
                shape.GetSanitizedSize(),
                shape.GetSanitizedRadius());
        }

        public static Bounds CalculateWorldAabb(in BoundProxyWorldData worldData)
        {
            return CalculateWorldAabb(
                worldData.worldCenter,
                worldData.worldRotation,
                worldData.shape,
                worldData.boxSize,
                worldData.sphereRadius);
        }

        public static bool Contains(in BoundProxyWorldData worldData, Vector3 worldPosition)
        {
            if (worldData.shape == BoundProxyShapeType.Sphere)
            {
                return (worldPosition - worldData.worldCenter).sqrMagnitude
                       <= worldData.sphereRadius * worldData.sphereRadius;
            }

            Vector3 halfExtents = GetHalfExtents(worldData.boxSize);
            Vector3 localPosition = Quaternion.Inverse(worldData.worldRotation) * (worldPosition - worldData.worldCenter);
            return Mathf.Abs(localPosition.x) <= halfExtents.x + IntersectionEpsilon
                   && Mathf.Abs(localPosition.y) <= halfExtents.y + IntersectionEpsilon
                   && Mathf.Abs(localPosition.z) <= halfExtents.z + IntersectionEpsilon;
        }

        public static bool IntersectsAabb(in BoundProxyWorldData worldData, Bounds bounds)
        {
            if (worldData.shape == BoundProxyShapeType.Sphere)
            {
                return bounds.SqrDistance(worldData.worldCenter) <= worldData.sphereRadius * worldData.sphereRadius;
            }

            return IntersectsBoxAabb(worldData, bounds);
        }

        private static Vector3 CalculateWorldCenter(Transform boundTransform, Vector3 localCenter)
        {
            if (boundTransform == null)
            {
                return localCenter;
            }

            return boundTransform.position + boundTransform.rotation * localCenter;
        }

        private static Bounds CalculateWorldAabb(
            Vector3 worldCenter,
            Quaternion worldRotation,
            BoundProxyShapeType shapeType,
            Vector3 boxSize,
            float sphereRadius)
        {
            if (shapeType == BoundProxyShapeType.Sphere)
            {
                return new Bounds(worldCenter, Vector3.one * (sphereRadius * 2.0f));
            }

            Vector3 halfExtents = GetHalfExtents(boxSize);
            Vector3 axisX = worldRotation * Vector3.right;
            Vector3 axisY = worldRotation * Vector3.up;
            Vector3 axisZ = worldRotation * Vector3.forward;
            Vector3 aabbExtents =
                Abs(axisX) * halfExtents.x
                + Abs(axisY) * halfExtents.y
                + Abs(axisZ) * halfExtents.z;
            return new Bounds(worldCenter, aabbExtents * 2.0f);
        }

        private static bool IntersectsBoxAabb(in BoundProxyWorldData worldData, Bounds aabb)
        {
            Vector3 boxHalfExtents = GetHalfExtents(worldData.boxSize);
            Vector3 aabbHalfExtents = aabb.extents;
            Vector3 axisX = worldData.worldRotation * Vector3.right;
            Vector3 axisY = worldData.worldRotation * Vector3.up;
            Vector3 axisZ = worldData.worldRotation * Vector3.forward;
            Vector3 translationWorld = aabb.center - worldData.worldCenter;
            float tx = Vector3.Dot(translationWorld, axisX);
            float ty = Vector3.Dot(translationWorld, axisY);
            float tz = Vector3.Dot(translationWorld, axisZ);

            float r00 = axisX.x;
            float r01 = axisX.y;
            float r02 = axisX.z;
            float r10 = axisY.x;
            float r11 = axisY.y;
            float r12 = axisY.z;
            float r20 = axisZ.x;
            float r21 = axisZ.y;
            float r22 = axisZ.z;

            float ar00 = Mathf.Abs(r00) + IntersectionEpsilon;
            float ar01 = Mathf.Abs(r01) + IntersectionEpsilon;
            float ar02 = Mathf.Abs(r02) + IntersectionEpsilon;
            float ar10 = Mathf.Abs(r10) + IntersectionEpsilon;
            float ar11 = Mathf.Abs(r11) + IntersectionEpsilon;
            float ar12 = Mathf.Abs(r12) + IntersectionEpsilon;
            float ar20 = Mathf.Abs(r20) + IntersectionEpsilon;
            float ar21 = Mathf.Abs(r21) + IntersectionEpsilon;
            float ar22 = Mathf.Abs(r22) + IntersectionEpsilon;

            if (Mathf.Abs(tx) > boxHalfExtents.x + aabbHalfExtents.x * ar00 + aabbHalfExtents.y * ar01 + aabbHalfExtents.z * ar02)
                return false;

            if (Mathf.Abs(ty) > boxHalfExtents.y + aabbHalfExtents.x * ar10 + aabbHalfExtents.y * ar11 + aabbHalfExtents.z * ar12)
                return false;

            if (Mathf.Abs(tz) > boxHalfExtents.z + aabbHalfExtents.x * ar20 + aabbHalfExtents.y * ar21 + aabbHalfExtents.z * ar22)
                return false;

            if (Mathf.Abs(translationWorld.x) > aabbHalfExtents.x + boxHalfExtents.x * ar00 + boxHalfExtents.y * ar10 + boxHalfExtents.z * ar20)
                return false;

            if (Mathf.Abs(translationWorld.y) > aabbHalfExtents.y + boxHalfExtents.x * ar01 + boxHalfExtents.y * ar11 + boxHalfExtents.z * ar21)
                return false;

            if (Mathf.Abs(translationWorld.z) > aabbHalfExtents.z + boxHalfExtents.x * ar02 + boxHalfExtents.y * ar12 + boxHalfExtents.z * ar22)
                return false;

            float ra;
            float rb;

            ra = boxHalfExtents.y * ar20 + boxHalfExtents.z * ar10;
            rb = aabbHalfExtents.y * ar02 + aabbHalfExtents.z * ar01;
            if (Mathf.Abs(tz * r10 - ty * r20) > ra + rb)
                return false;

            ra = boxHalfExtents.y * ar21 + boxHalfExtents.z * ar11;
            rb = aabbHalfExtents.x * ar02 + aabbHalfExtents.z * ar00;
            if (Mathf.Abs(tz * r11 - ty * r21) > ra + rb)
                return false;

            ra = boxHalfExtents.y * ar22 + boxHalfExtents.z * ar12;
            rb = aabbHalfExtents.x * ar01 + aabbHalfExtents.y * ar00;
            if (Mathf.Abs(tz * r12 - ty * r22) > ra + rb)
                return false;

            ra = boxHalfExtents.x * ar20 + boxHalfExtents.z * ar00;
            rb = aabbHalfExtents.y * ar12 + aabbHalfExtents.z * ar11;
            if (Mathf.Abs(tx * r20 - tz * r00) > ra + rb)
                return false;

            ra = boxHalfExtents.x * ar21 + boxHalfExtents.z * ar01;
            rb = aabbHalfExtents.x * ar12 + aabbHalfExtents.z * ar10;
            if (Mathf.Abs(tx * r21 - tz * r01) > ra + rb)
                return false;

            ra = boxHalfExtents.x * ar22 + boxHalfExtents.z * ar02;
            rb = aabbHalfExtents.x * ar11 + aabbHalfExtents.y * ar10;
            if (Mathf.Abs(tx * r22 - tz * r02) > ra + rb)
                return false;

            ra = boxHalfExtents.x * ar10 + boxHalfExtents.y * ar00;
            rb = aabbHalfExtents.y * ar22 + aabbHalfExtents.z * ar21;
            if (Mathf.Abs(ty * r00 - tx * r10) > ra + rb)
                return false;

            ra = boxHalfExtents.x * ar11 + boxHalfExtents.y * ar01;
            rb = aabbHalfExtents.x * ar22 + aabbHalfExtents.z * ar20;
            if (Mathf.Abs(ty * r01 - tx * r11) > ra + rb)
                return false;

            ra = boxHalfExtents.x * ar12 + boxHalfExtents.y * ar02;
            rb = aabbHalfExtents.x * ar21 + aabbHalfExtents.y * ar20;
            if (Mathf.Abs(ty * r02 - tx * r12) > ra + rb)
                return false;

            return true;
        }

        private static Vector3 GetHalfExtents(Vector3 size)
        {
            return new Vector3(
                Mathf.Max(size.x, 0.0f) * 0.5f,
                Mathf.Max(size.y, 0.0f) * 0.5f,
                Mathf.Max(size.z, 0.0f) * 0.5f);
        }

        private static Vector3 Abs(Vector3 vector)
        {
            return new Vector3(Mathf.Abs(vector.x), Mathf.Abs(vector.y), Mathf.Abs(vector.z));
        }
    }
}

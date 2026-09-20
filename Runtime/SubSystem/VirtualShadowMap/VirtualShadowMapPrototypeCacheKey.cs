using System;
using Unity.Mathematics;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Runtime.VirtualShadowMap
{
    internal readonly struct VirtualShadowMapPrototypeCacheKey
        : IEquatable<VirtualShadowMapPrototypeCacheKey>
    {
        private readonly ulong m_CameraEntityId;
        private readonly ulong m_ClipmapGeneration;
        private readonly uint m_PrimitiveSceneToken;
        private readonly uint m_StaticShadowRevision;
        private readonly uint m_TextureBindingRevision;
        private readonly int m_CascadeCount;
        private readonly int m_VirtualResolution;
        private readonly int m_ForcedMeshLODNodeDepth;
        private readonly float m_MeshLODErrorThreshold;
        private readonly float m_SlopeScaleDepthBias;
        private readonly Vector4 m_ShadowCasterState;
        private readonly int m_CameraCullingMask;
        private readonly VividGPULODSelectionContext m_LODSelectionContext;
        private readonly VividGPUCullingContext m_CullingContext0;
        private readonly VividGPUCullingContext m_CullingContext1;
        private readonly VividGPUCullingContext m_CullingContext2;
        private readonly VividGPUCullingContext m_CullingContext3;
        private readonly Matrix4x4 m_Cascade0;
        private readonly Matrix4x4 m_Cascade1;
        private readonly Matrix4x4 m_Cascade2;
        private readonly Matrix4x4 m_Cascade3;

        internal VirtualShadowMapPrototypeCacheKey(
            ulong cameraEntityId,
            uint primitiveSceneToken,
            uint staticShadowRevision,
            uint textureBindingRevision,
            int cascadeCount,
            int virtualResolution,
            int forcedMeshLODNodeDepth,
            float meshLODErrorThreshold,
            float slopeScaleDepthBias,
            Vector4 shadowCasterState,
            int cameraCullingMask,
            in VividGPULODSelectionContext lodSelectionContext,
            VividGPUCullingContext[] cullingContexts,
            Matrix4x4 cascade0,
            Matrix4x4 cascade1,
            Matrix4x4 cascade2,
            Matrix4x4 cascade3)
        {
            m_CameraEntityId = cameraEntityId;
            m_ClipmapGeneration = 0;
            m_PrimitiveSceneToken = primitiveSceneToken;
            m_StaticShadowRevision = staticShadowRevision;
            m_TextureBindingRevision = textureBindingRevision;
            m_CascadeCount = cascadeCount;
            m_VirtualResolution = virtualResolution;
            m_ForcedMeshLODNodeDepth = forcedMeshLODNodeDepth;
            m_MeshLODErrorThreshold = meshLODErrorThreshold;
            m_SlopeScaleDepthBias = slopeScaleDepthBias;
            m_ShadowCasterState = shadowCasterState;
            m_CameraCullingMask = cameraCullingMask;
            m_LODSelectionContext = lodSelectionContext;
            // Copy values, not the mutable pass-owned array. Ignore inactive cascades.
            m_CullingContext0 = cullingContexts != null && cascadeCount > 0 ? cullingContexts[0] : default;
            m_CullingContext1 = cullingContexts != null && cascadeCount > 1 ? cullingContexts[1] : default;
            m_CullingContext2 = cullingContexts != null && cascadeCount > 2 ? cullingContexts[2] : default;
            m_CullingContext3 = cullingContexts != null && cascadeCount > 3 ? cullingContexts[3] : default;
            m_Cascade0 = cascade0;
            m_Cascade1 = cascade1;
            m_Cascade2 = cascade2;
            m_Cascade3 = cascade3;
        }

        internal bool IsValid => m_CameraEntityId != 0ul
            && m_CascadeCount > 0
            && (m_ClipmapGeneration != 0 || m_CascadeCount <= VividShadowData.MaxCascadeCount)
            && m_VirtualResolution > 0
            && float.IsFinite(m_MeshLODErrorThreshold)
            && float.IsFinite(m_SlopeScaleDepthBias)
            && IsFinite(m_ShadowCasterState)
            && IsFinite(m_LODSelectionContext.ViewProjectionMatrix)
            && math.all(math.isfinite(m_LODSelectionContext.CameraPosition))
            && math.all(math.isfinite(m_LODSelectionContext.CameraUp))
            && math.all(math.isfinite(m_LODSelectionContext.CameraRight))
            && math.all(math.isfinite(m_LODSelectionContext.ScreenSizePixels))
            && IsFinite(m_CullingContext0)
            && IsFinite(m_CullingContext1)
            && IsFinite(m_CullingContext2)
            && IsFinite(m_CullingContext3)
            && IsFinite(m_Cascade0)
            && IsFinite(m_Cascade1)
            && IsFinite(m_Cascade2)
            && IsFinite(m_Cascade3);

        // Clipmap generation covers light identity/basis, level sizes, resolution
        // and stable Z. Integer XY movement is handled by page remapping, not key
        // equality. The actual VSM LOD/culling inputs are derived from that basis.
        internal VirtualShadowMapPrototypeCacheKey(ulong cameraEntityId, uint primitiveSceneToken,
            uint staticShadowRevision, uint textureBindingRevision, int levelCount, int virtualResolution,
            int forcedMeshLODNodeDepth, float meshLODErrorThreshold, float slopeScaleDepthBias,
            Vector4 shadowCasterState, int cameraCullingMask, ulong clipmapGeneration)
            : this(cameraEntityId, primitiveSceneToken, staticShadowRevision, textureBindingRevision,
                levelCount, virtualResolution, forcedMeshLODNodeDepth, meshLODErrorThreshold,
                slopeScaleDepthBias, shadowCasterState, cameraCullingMask, default, null,
                Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.identity)
        {
            m_ClipmapGeneration = clipmapGeneration;
        }

        internal uint StaticShadowRevision => m_StaticShadowRevision;

        public bool Equals(VirtualShadowMapPrototypeCacheKey other)
        {
            return m_CameraEntityId == other.m_CameraEntityId
                && m_ClipmapGeneration == other.m_ClipmapGeneration
                && m_PrimitiveSceneToken == other.m_PrimitiveSceneToken
                && m_TextureBindingRevision == other.m_TextureBindingRevision
                && m_CascadeCount == other.m_CascadeCount
                && m_VirtualResolution == other.m_VirtualResolution
                && m_ForcedMeshLODNodeDepth == other.m_ForcedMeshLODNodeDepth
                && m_MeshLODErrorThreshold.Equals(other.m_MeshLODErrorThreshold)
                && m_SlopeScaleDepthBias.Equals(other.m_SlopeScaleDepthBias)
                && m_ShadowCasterState.Equals(other.m_ShadowCasterState)
                && m_CameraCullingMask == other.m_CameraCullingMask
                && m_LODSelectionContext.ViewProjectionMatrix.Equals(other.m_LODSelectionContext.ViewProjectionMatrix)
                && m_LODSelectionContext.CameraPosition.Equals(other.m_LODSelectionContext.CameraPosition)
                && m_LODSelectionContext.CameraUp.Equals(other.m_LODSelectionContext.CameraUp)
                && m_LODSelectionContext.CameraRight.Equals(other.m_LODSelectionContext.CameraRight)
                && m_LODSelectionContext.ScreenSizePixels.Equals(other.m_LODSelectionContext.ScreenSizePixels)
                && CullingContextsEqual(m_CullingContext0, other.m_CullingContext0)
                && CullingContextsEqual(m_CullingContext1, other.m_CullingContext1)
                && CullingContextsEqual(m_CullingContext2, other.m_CullingContext2)
                && CullingContextsEqual(m_CullingContext3, other.m_CullingContext3)
                && m_Cascade0.Equals(other.m_Cascade0)
                && m_Cascade1.Equals(other.m_Cascade1)
                && m_Cascade2.Equals(other.m_Cascade2)
                && m_Cascade3.Equals(other.m_Cascade3);
        }

        public override bool Equals(object obj)
        {
            return obj is VirtualShadowMapPrototypeCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(m_CameraEntityId);
            hash.Add(m_ClipmapGeneration);
            hash.Add(m_PrimitiveSceneToken);
            hash.Add(m_TextureBindingRevision);
            hash.Add(m_CascadeCount);
            hash.Add(m_VirtualResolution);
            hash.Add(m_ForcedMeshLODNodeDepth);
            hash.Add(m_MeshLODErrorThreshold);
            hash.Add(m_SlopeScaleDepthBias);
            hash.Add(m_ShadowCasterState);
            hash.Add(m_CameraCullingMask);
            hash.Add(m_LODSelectionContext.ViewProjectionMatrix);
            hash.Add(m_LODSelectionContext.CameraPosition);
            hash.Add(m_LODSelectionContext.CameraUp);
            hash.Add(m_LODSelectionContext.CameraRight);
            hash.Add(m_LODSelectionContext.ScreenSizePixels);
            AddCullingContextHash(ref hash, m_CullingContext0);
            AddCullingContextHash(ref hash, m_CullingContext1);
            AddCullingContextHash(ref hash, m_CullingContext2);
            AddCullingContextHash(ref hash, m_CullingContext3);
            hash.Add(m_Cascade0);
            hash.Add(m_Cascade1);
            hash.Add(m_Cascade2);
            hash.Add(m_Cascade3);
            return hash.ToHashCode();
        }

        // Dispatch offsets and padding only address scratch buffers; they do not
        // change visibility. Compare consumed fields explicitly to avoid boxing.
        private static bool CullingContextsEqual(
            in VividGPUCullingContext left, in VividGPUCullingContext right)
        {
            if (!left.ViewProjectionMatrix.Equals(right.ViewProjectionMatrix)
                || !left.ViewMatrix.Equals(right.ViewMatrix)
                || !left.CameraPosition.Equals(right.CameraPosition)
                || !left.CullingSphereLS.Equals(right.CullingSphereLS)
                || left.PassMask != right.PassMask
                || left.CameraIsPerspective != right.CameraIsPerspective)
                return false;

            for (int planeIndex = 0; planeIndex < 6; planeIndex++)
            {
                if (!left.GetFrustumPlane(planeIndex).Equals(right.GetFrustumPlane(planeIndex)))
                    return false;
            }
            return true;
        }

        private static void AddCullingContextHash(
            ref HashCode hash, in VividGPUCullingContext context)
        {
            hash.Add(context.ViewProjectionMatrix);
            hash.Add(context.ViewMatrix);
            hash.Add(context.CameraPosition);
            hash.Add(context.CullingSphereLS);
            hash.Add(context.PassMask);
            hash.Add(context.CameraIsPerspective);
            for (int planeIndex = 0; planeIndex < 6; planeIndex++)
                hash.Add(context.GetFrustumPlane(planeIndex));
        }

        private static bool IsFinite(in VividGPUCullingContext context)
        {
            if (!IsFinite(context.ViewProjectionMatrix)
                || !IsFinite(context.ViewMatrix)
                || !math.all(math.isfinite(context.CameraPosition))
                || !math.all(math.isfinite(context.CullingSphereLS)))
                return false;

            for (int planeIndex = 0; planeIndex < 6; planeIndex++)
            {
                if (!IsFinite(context.GetFrustumPlane(planeIndex)))
                    return false;
            }
            return true;
        }

        private static bool IsFinite(float4x4 matrix)
        {
            return math.all(math.isfinite(matrix.c0))
                && math.all(math.isfinite(matrix.c1))
                && math.all(math.isfinite(matrix.c2))
                && math.all(math.isfinite(matrix.c3));
        }

        private static bool IsFinite(Vector4 value)
        {
            return float.IsFinite(value.x)
                && float.IsFinite(value.y)
                && float.IsFinite(value.z)
                && float.IsFinite(value.w);
        }

        private static bool IsFinite(Matrix4x4 matrix)
        {
            for (int elementIndex = 0; elementIndex < 16; elementIndex++)
            {
                if (!float.IsFinite(matrix[elementIndex]))
                    return false;
            }

            return true;
        }
    }
}

using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.VirtualShadowMap
{
    internal enum VividVSMLightType : uint
    {
        Directional = 0u,
        Point = 1u,
        Spot = 2u,
        Rect = 3u,
        Tube = 4u,
    }

    [Flags]
    internal enum VividVSMProjectionFlags : uint
    {
        None = 0u,
        SinglePage = 1u << 0,
        Unreferenced = 1u << 1,
        UseReceiverMask = 1u << 2,
        IsCoarseClipLevel = 1u << 3,
        Uncached = 1u << 4,
        SkipDynamicInvalid = 1u << 5,
    }

    [Flags]
    internal enum VividVSMNextMapFlags : uint
    {
        None = 0u,
        Valid = 1u << 0,
        InvalidateAll = 1u << 1,
    }

    [Flags]
    internal enum VividVSMOwnerFlags : uint
    {
        None = 0u,
        Valid = 1u << 0,
        SinglePage = 1u << 1,
        Unreferenced = 1u << 2,
        DetailGeometry = 1u << 3,
        Remapped = 1u << 4,
    }

    // Unified 256-byte GPU ABI (16 * float4) matching VividVirtualShadowMapProjection.hlsl.
    // Offsets 0..144 preserve existing Directional Clipmap fields and offsets.
    [StructLayout(LayoutKind.Sequential)]
    internal struct VirtualShadowMapProjection
    {
        internal const int Stride = 256;

        internal Matrix4x4 WorldToClip;
        internal Matrix4x4 WorldToShadow;
        // Directional: (unsnappedCamPosWS.xyz, -levelRadius). Local: (lightPosWS.xyz, lightRange).
        internal Vector4 SelectionSphere;
        // Directional: (worldTexelSize, normalBias, perfLodBias, maxDistance).
        // Local: (sourceRadius, normalBias, resolutionLodBias, nearPlane).
        internal Vector4 Parameters;
        // Directional: (toLightDirWS, sinHalfAngularDiam).
        // Local: (lightForwardWS, cosOuterSpotAngle; Point=-2.0).
        internal Vector4 LightDirectionAndSpot;
        // (proj.m00, proj.m11, proj.m22, proj.m23) for perspective/ortho slope & footprint math.
        internal Vector4 ProjectionScales;
        // (pageTableBaseOffset, hierarchyBaseOffset, pagesPerAxis, maxMipLevels)
        internal uint4 Addressing;
        // (lightType, flags, minMipLevel, clipmapRemainingOrFace)
        internal uint4 Meta;
        // (baseVsmId, prevVsmId, clipmapOriginRelX, clipmapOriginRelY)
        internal uint4 IdentityAndOrigin;
        // (asuint(texelDitherScale), boundRectBaseOffset, lightIndex, reserved)
        internal uint4 Extended;
    }

    // Cross-frame VSM remap entry (16 bytes), indexed by previous frame's VSMId.
    [StructLayout(LayoutKind.Sequential)]
    internal struct VividVSMNextMapEntry
    {
        internal const int Stride = 16;

        internal uint NextVSMId;
        internal int PageOffsetX;
        internal int PageOffsetY;
        internal uint Flags;
    }

    // Compacted active (VSMId, MipLevel) work item (16 bytes).
    [StructLayout(LayoutKind.Sequential)]
    internal struct VividVSMActiveView
    {
        internal const int Stride = 16;

        internal uint VSMId;
        internal uint MipLevel;
        internal uint VirtualResolution;
        internal uint BoundRectIndex;
    }

    internal static class VirtualShadowMapAddressing
    {
        internal const uint MaxSinglePageShadowMaps = 512u;
        internal const uint InvalidVSMId = 0xFFFFFFFFu;
        internal const uint MaxPackedLocalLights = 32u;

        internal static bool IsSinglePageId(uint vsmId) => vsmId < MaxSinglePageShadowMaps;

        internal static int CalculateMipCount(int pagesPerAxis)
        {
            int axis = Mathf.NextPowerOfTwo(Mathf.Max(pagesPerAxis, 1));
            int mips = 1;
            while (axis > 1)
            {
                axis >>= 1;
                mips++;
            }
            return mips;
        }

        internal static int CalculateMipEntryOffset(int pagesPerAxis, int mipLevel)
        {
            int axis = Mathf.NextPowerOfTwo(Mathf.Max(pagesPerAxis, 1));
            int mipAxis = axis >> Mathf.Clamp(mipLevel, 0, 15);
            return (4 * (axis * axis - mipAxis * mipAxis)) / 3;
        }

        internal static int CalculateTotalMipChainEntries(int pagesPerAxis)
        {
            int axis = Mathf.NextPowerOfTwo(Mathf.Max(pagesPerAxis, 1));
            return (4 * axis * axis - 1) / 3;
        }

        internal static uint CalculatePageIndex(uint baseOffset, uint pagesPerAxis, bool isSinglePage, uint mipLevel, uint2 pageXY)
        {
            if (isSinglePage)
                return baseOffset;
            uint mipAxis = pagesPerAxis >> (int)mipLevel;
            uint mipOffset = (4u * (pagesPerAxis * pagesPerAxis - mipAxis * mipAxis)) / 3u;
            return baseOffset + mipOffset + pageXY.y * mipAxis + pageXY.x;
        }

        internal static uint2 EncodePhysicalOwnerDesc(uint vsmId, uint mipLevel, uint2 pageXY, VividVSMOwnerFlags flags)
        {
            uint word0 = (vsmId & 0xFFFFu) | ((mipLevel & 0xFu) << 16) | (((uint)flags & 0xFFu) << 20);
            uint word1 = (pageXY.x & 0xFFFFu) | ((pageXY.y & 0xFFFFu) << 16);
            return new uint2(word0, word1);
        }

        internal static void DecodePhysicalOwnerDesc(uint2 desc, out uint vsmId, out uint mipLevel, out uint2 pageXY, out VividVSMOwnerFlags flags)
        {
            vsmId = desc.x & 0xFFFFu;
            mipLevel = (desc.x >> 16) & 0xFu;
            flags = (VividVSMOwnerFlags)((desc.x >> 20) & 0xFFu);
            pageXY = new uint2(desc.y & 0xFFFFu, (desc.y >> 16) & 0xFFFFu);
        }

        // Matches HLSL VividVSMGetCubeFace: 0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z.
        internal static int GetCubeFace(Vector3 dirFromLight)
        {
            float ax = Mathf.Abs(dirFromLight.x);
            float ay = Mathf.Abs(dirFromLight.y);
            float az = Mathf.Abs(dirFromLight.z);
            if (ax >= ay && ax >= az) return dirFromLight.x > 0.0f ? 0 : 1;
            if (ay > az) return dirFromLight.y > 0.0f ? 2 : 3;
            return dirFromLight.z > 0.0f ? 4 : 5;
        }
    }

    internal sealed class VirtualShadowMapProjectionSet : IDisposable
    {
        internal static readonly int BufferId = Shader.PropertyToID("_VSMProjections");
        internal static readonly int CountId = Shader.PropertyToID("_VSMProjectionCount");
        internal static readonly int RemapId = Shader.PropertyToID("_VSMProjectionRemap");
        internal static readonly int NextMapId = Shader.PropertyToID("_VSMNextMap");
        internal static readonly int NextMapCountId = Shader.PropertyToID("_VSMNextMapCount");
        internal static readonly int PageOffsetsId = Shader.PropertyToID("_VSMClipmapPageOffsets");
        internal const int MaxVirtualResolution = 16384;
        internal const int UnityRasterTileSize = 2048;

        private VirtualShadowMapProjection[] m_Upload;
        private int4[] m_Remap;
        private VividVSMNextMapEntry[] m_NextMap;
        private int2[] m_PageOffsets;
        private readonly long[] m_RecordedOriginX = new long[VirtualShadowMapClipmapLayout.MaxLevels];
        private readonly long[] m_RecordedOriginY = new long[VirtualShadowMapClipmapLayout.MaxLevels];
        private VirtualShadowMapClipmapLayout m_Layout;
        private bool m_HasRecordedLayout;
        private ulong m_RecordedCamera, m_RecordedLight, m_RecordedGeneration;
        private Quaternion m_RecordedRotation;
        private int m_RecordedResolution, m_RecordedFirstLevel, m_RecordedCount;
        private readonly float[] m_RecordedDepthMin = new float[VirtualShadowMapClipmapLayout.MaxLevels];
        private readonly float[] m_RecordedDepthMax = new float[VirtualShadowMapClipmapLayout.MaxLevels];
        private int m_NextMapCount;

        internal GraphicsBuffer Buffer { get; private set; }
        internal GraphicsBuffer RemapBuffer { get; private set; }
        internal GraphicsBuffer NextMapBuffer { get; private set; }
        internal GraphicsBuffer PageOffsetsBuffer { get; private set; }
        internal int Count { get; private set; }
        internal int NextMapCount => m_NextMapCount;
        internal int DirectionalCount { get; private set; }
        internal int LocalCount => Mathf.Max(0, Count - DirectionalCount);
        internal ulong Generation { get; private set; }
        internal bool RequiresRemap { get; private set; }
        internal bool RequiresFeedbackReset { get; private set; }
        internal bool LayoutRecorded { get; private set; }

        internal void EnsureCapacity(int count, int pageOffsetSide = 0)
        {
            count = Mathf.Max(1, count);
            int offsetSide = Mathf.Max(1, pageOffsetSide > 0 ? pageOffsetSide : count);
            int offsetCount = offsetSide * offsetSide;
            if (Buffer != null && Buffer.IsValid() && Buffer.count >= count
                && NextMapBuffer != null && NextMapBuffer.IsValid() && NextMapBuffer.count >= count
                && PageOffsetsBuffer != null && PageOffsetsBuffer.IsValid() && PageOffsetsBuffer.count >= offsetCount)
                return;

            Buffer?.Dispose();
            RemapBuffer?.Dispose();
            NextMapBuffer?.Dispose();
            PageOffsetsBuffer?.Dispose();

            m_Upload = new VirtualShadowMapProjection[count];
            m_Remap = new int4[count];
            m_NextMap = new VividVSMNextMapEntry[count];
            m_PageOffsets = new int2[offsetCount];

            PageOffsetsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, offsetCount, 8)
            { name = "VSMClipmapPageOffsets" };
            Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, VirtualShadowMapProjection.Stride)
            { name = "VSMProjections" };
            RemapBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16)
            { name = "VSMProjectionRemap" };
            NextMapBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, VividVSMNextMapEntry.Stride)
            { name = "VSMNextMap" };
        }

        internal void Upload(CommandBuffer cmd)
        {
            if (Count > 0)
            {
                cmd.SetBufferData(Buffer, m_Upload, 0, 0, Count);
                if (m_Layout != null)
                {
                    cmd.SetBufferData(RemapBuffer, m_Remap, 0, 0, DirectionalCount);
                    cmd.SetBufferData(PageOffsetsBuffer, m_PageOffsets, 0, 0, DirectionalCount * DirectionalCount);
                }
                if (m_NextMapCount > 0)
                    cmd.SetBufferData(NextMapBuffer, m_NextMap, 0, 0, m_NextMapCount);
            }
        }

        internal void PrepareClipmaps(VirtualShadowMapClipmapLayout layout)
        {
            EnsureCapacity(layout.Count, layout.Count);
            m_Layout = layout;
            Count = layout.Count;
            DirectionalCount = layout.Count;
            m_NextMapCount = layout.Count;

            bool sameBasis = m_HasRecordedLayout && m_RecordedCamera == layout.CameraId
                && m_RecordedLight == layout.LightId && m_RecordedRotation.Equals(layout.Rotation)
                && m_RecordedResolution == layout.Resolution && m_RecordedCount == layout.Count
                && m_RecordedFirstLevel == layout.FirstLevel;
            Generation = sameBasis ? m_RecordedGeneration : m_RecordedGeneration + 1;
            RequiresRemap = !sameBasis;
            RequiresFeedbackReset = !sameBasis;
            LayoutRecorded = false;

            int pages = layout.Resolution / VirtualShadowMapPrototypeRuntime.PageSize;
            uint pagesPerLevel = (uint)(pages * pages);
            uint hierarchyNodesPerLevel = (uint)VirtualShadowMapPrototypeRuntime.CalculateHierarchyNodesPerLevel(pages);
            Vector3 toLight = -(layout.Rotation * Vector3.forward);
            uint ditherOne = math.asuint(1.0f);

            for (int i = 0; i < Count; i++)
            {
                bool sameDepth = sameBasis && m_RecordedDepthMin[i] == layout.DepthMins[i]
                    && m_RecordedDepthMax[i] == layout.DepthMaxs[i];
                int dx = sameDepth ? ClampPageDelta(layout.OriginX[i] - m_RecordedOriginX[i], pages) : 0;
                int dy = sameDepth ? ClampPageDelta(layout.OriginY[i] - m_RecordedOriginY[i], pages) : 0;
                m_Remap[i] = new int4(dx, dy, sameDepth ? 0 : 1, 0);
                m_NextMap[i] = new VividVSMNextMapEntry
                {
                    NextVSMId = sameDepth ? (uint)i : VirtualShadowMapAddressing.InvalidVSMId,
                    PageOffsetX = dx,
                    PageOffsetY = dy,
                    Flags = sameDepth
                        ? (uint)VividVSMNextMapFlags.Valid
                        : (uint)VividVSMNextMapFlags.InvalidateAll,
                };
                // Integer relative origins avoid cancellation of large world-space floats.
                // Both axes of WorldToShadow use the layout's positive light-space axes.
                for (int target = i; target < Count; target++)
                {
                    long scale = 1L << (target - i);
                    m_PageOffsets[i * Count + target] = new int2(
                        checked((int)(layout.OriginX[i] - layout.OriginX[target] * scale)),
                        checked((int)(layout.OriginY[i] - layout.OriginY[target] * scale)));
                }
                RequiresRemap |= !sameDepth || dx != 0 || dy != 0;
                RequiresFeedbackReset |= Math.Abs(dx) >= pages || Math.Abs(dy) >= pages;

                Vector3 center = layout.CameraPosition;
                Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(layout.Projections[i], true);
                VividVSMProjectionFlags flags = VividVSMProjectionFlags.UseReceiverMask
                    | (i == Count - 1 ? VividVSMProjectionFlags.IsCoarseClipLevel : VividVSMProjectionFlags.None);
                m_Upload[i] = new VirtualShadowMapProjection
                {
                    WorldToClip = gpuProj * layout.Views[i],
                    WorldToShadow = VividShadowData.BuildWorldToShadowMatrix(layout.Projections[i], layout.Views[i]),
                    SelectionSphere = new Vector4(center.x, center.y, center.z, -layout.Radii[i]),
                    Parameters = new Vector4(2 * layout.Radii[i] / layout.Resolution,
                        layout.NormalBias, 0, layout.MaxDistance),
                    LightDirectionAndSpot = new Vector4(toLight.x, toLight.y, toLight.z, 0.0f),
                    ProjectionScales = new Vector4(gpuProj.m00, gpuProj.m11, gpuProj.m22, gpuProj.m23),
                    Addressing = new uint4((uint)i * pagesPerLevel, (uint)i * hierarchyNodesPerLevel, (uint)pages, 1u),
                    Meta = new uint4((uint)VividVSMLightType.Directional, (uint)flags, 0u, (uint)(Count - i)),
                    IdentityAndOrigin = new uint4(
                        0u,
                        sameDepth ? (uint)i : VirtualShadowMapAddressing.InvalidVSMId,
                        unchecked((uint)(int)layout.OriginX[i]),
                        unchecked((uint)(int)layout.OriginY[i])),
                    Extended = new uint4(ditherOne, (uint)i, 0u, 0u),
                };
            }
        }

        // Unified upload path used by VirtualShadowMapRegistry for Directional + Point + Spot.
        internal void PrepareUnified(
            VirtualShadowMapClipmapLayout directionalLayout,
            VirtualShadowMapProjection[] projections,
            int projectionCount,
            int directionalCount,
            VividVSMNextMapEntry[] nextMap,
            int nextMapCount,
            bool requiresRemap,
            bool requiresFeedbackReset)
        {
            int dirCount = Mathf.Max(0, directionalCount);
            EnsureCapacity(Mathf.Max(projectionCount, nextMapCount), Mathf.Max(1, dirCount));
            m_Layout = directionalLayout;
            Count = projectionCount;
            DirectionalCount = dirCount;
            m_NextMapCount = nextMapCount;

            if (projectionCount > 0)
                Array.Copy(projections, 0, m_Upload, 0, projectionCount);
            if (nextMapCount > 0)
                Array.Copy(nextMap, 0, m_NextMap, 0, nextMapCount);

            if (directionalLayout != null && dirCount > 0)
            {
                bool sameBasis = m_HasRecordedLayout && m_RecordedCamera == directionalLayout.CameraId
                    && m_RecordedLight == directionalLayout.LightId && m_RecordedRotation.Equals(directionalLayout.Rotation)
                    && m_RecordedResolution == directionalLayout.Resolution && m_RecordedCount == dirCount
                    && m_RecordedFirstLevel == directionalLayout.FirstLevel;
                Generation = sameBasis ? m_RecordedGeneration : m_RecordedGeneration + 1;
                int pages = directionalLayout.Resolution / VirtualShadowMapPrototypeRuntime.PageSize;
                for (int i = 0; i < dirCount; i++)
                {
                    bool sameDepth = sameBasis && m_RecordedDepthMin[i] == directionalLayout.DepthMins[i]
                        && m_RecordedDepthMax[i] == directionalLayout.DepthMaxs[i];
                    int dx = sameDepth ? ClampPageDelta(directionalLayout.OriginX[i] - m_RecordedOriginX[i], pages) : 0;
                    int dy = sameDepth ? ClampPageDelta(directionalLayout.OriginY[i] - m_RecordedOriginY[i], pages) : 0;
                    m_Remap[i] = new int4(dx, dy, sameDepth ? 0 : 1, 0);
                    for (int target = i; target < dirCount; target++)
                    {
                        long scale = 1L << (target - i);
                        m_PageOffsets[i * dirCount + target] = new int2(
                            checked((int)(directionalLayout.OriginX[i] - directionalLayout.OriginX[target] * scale)),
                            checked((int)(directionalLayout.OriginY[i] - directionalLayout.OriginY[target] * scale)));
                    }
                    requiresRemap |= !sameDepth || dx != 0 || dy != 0;
                    requiresFeedbackReset |= Math.Abs(dx) >= pages || Math.Abs(dy) >= pages;
                }
            }
            else
            {
                Generation = m_RecordedGeneration + 1;
            }

            RequiresRemap = requiresRemap;
            RequiresFeedbackReset = requiresFeedbackReset;
            LayoutRecorded = false;
        }

        internal static int ClampPageDelta(long delta, int pages)
            => (int)Math.Max(-pages, Math.Min(pages, delta));

        // Called only after the commands moving both residency and feedback have
        // been recorded. Merely preparing a camera must not advance this snapshot.
        internal void CommitRecordedLayout()
        {
            if (m_Layout == null)
                return;
            m_RecordedCamera = m_Layout.CameraId;
            m_RecordedLight = m_Layout.LightId;
            m_RecordedRotation = m_Layout.Rotation;
            m_RecordedResolution = m_Layout.Resolution;
            m_RecordedFirstLevel = m_Layout.FirstLevel;
            m_RecordedCount = DirectionalCount;
            Array.Copy(m_Layout.DepthMins, m_RecordedDepthMin, DirectionalCount);
            Array.Copy(m_Layout.DepthMaxs, m_RecordedDepthMax, DirectionalCount);
            m_RecordedGeneration = Generation;
            Array.Copy(m_Layout.OriginX, m_RecordedOriginX, DirectionalCount);
            Array.Copy(m_Layout.OriginY, m_RecordedOriginY, DirectionalCount);
            m_HasRecordedLayout = true;
            LayoutRecorded = true;
        }

        internal void InvalidateLayout() => m_HasRecordedLayout = false;

        internal Matrix4x4 GetRasterMatrix(int projectionIndex, int resolution,
            int tileSize, int originX, int originY, bool uvStartsAtTop)
        {
            return RasterTransform(resolution, tileSize, originX, originY, uvStartsAtTop)
                * m_Upload[projectionIndex].WorldToClip;
        }

        internal static Matrix4x4 RasterTransform(int resolution, int tileSize,
            int originX, int originY, bool uvStartsAtTop)
        {
            Matrix4x4 transform = Matrix4x4.identity;
            transform.m00 = transform.m11 = (float)resolution / tileSize;
            transform.m03 = (float)(resolution - 2 * originX - tileSize) / tileSize;
            transform.m13 = (float)(resolution - 2 * originY - tileSize) / tileSize
                * (uvStartsAtTop ? -1.0f : 1.0f);
            return transform;
        }

        internal static int ResolveResolution(int requested, int csmResolution)
        {
            int resolution = Mathf.Clamp(requested > 0 ? requested : csmResolution,
                VirtualShadowMapClipmapLayout.MinResolution, MaxVirtualResolution);
            return Mathf.NextPowerOfTwo(resolution);
        }

        internal void Reset()
        {
            Count = 0;
            DirectionalCount = 0;
            m_NextMapCount = 0;
            m_Layout = null;
            LayoutRecorded = false;
        }

        public void Dispose()
        {
            Buffer?.Dispose();
            Buffer = null;
            RemapBuffer?.Dispose();
            RemapBuffer = null;
            NextMapBuffer?.Dispose();
            NextMapBuffer = null;
            PageOffsetsBuffer?.Dispose();
            PageOffsetsBuffer = null;
            m_PageOffsets = null;
            m_Upload = null;
            m_Remap = null;
            m_NextMap = null;
            m_HasRecordedLayout = false;
            m_Layout = null;
            LayoutRecorded = false;
            Count = 0;
            DirectionalCount = 0;
            m_NextMapCount = 0;
        }
    }
}

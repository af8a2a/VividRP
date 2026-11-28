using System;
using Unity.Mathematics;
using UnityEngine;

namespace VividRP.Runtime.VirtualShadowMap
{
    internal struct VividVSMLocalLightDescriptor
    {
        internal ulong LightEntityId;
        internal VividVSMLightType LightType;
        internal Vector3 PositionWS;
        internal Quaternion Rotation;
        internal float Range;
        internal float SpotAngle;
        internal float SourceRadius;
        internal float NormalBias;
        internal float ResolutionLodBias;
        internal float TexelDitherScale;
        internal int MaxResolution;
        internal uint MinMipLevel;
        internal bool IsSinglePage;
        internal bool UseReceiverMask;
        internal bool ForceUncached;
        internal bool IsAreaLight;
        internal uint PunctualLightIndex;
    }

    // Per-camera persistent registry managing unified Directional + Point + Spot VSM IDs,
    // cross-frame NextVSMMap remapping, and per-light cache invalidation with zero steady-state GC.
    internal sealed class VirtualShadowMapRegistry
    {
        internal const int DefaultLocalMaxResolution = 4096;
        internal const int PointFaceCount = 6;
        internal const int DefaultMaxDistantPagesPerFrame = 8;

        private static readonly Vector3[] s_CubeFaceForwards =
        {
            Vector3.right,   // 0: +X
            Vector3.left,    // 1: -X
            Vector3.up,      // 2: +Y
            Vector3.down,    // 3: -Y
            Vector3.forward, // 4: +Z
            Vector3.back,    // 5: -Z
        };

        private static readonly Vector3[] s_CubeFaceUps =
        {
            Vector3.up,      // 0: +X
            Vector3.up,      // 1: -X
            Vector3.back,    // 2: +Y
            Vector3.forward, // 3: -Y
            Vector3.up,      // 4: +Z
            Vector3.up,      // 5: -Z
        };

        private struct TrackedLightRecord
        {
            internal ulong LightEntityId;
            internal VividVSMLightType LightType;
            internal int BaseVSMId;
            internal int VSMCount;
            internal int Resolution;
            internal bool IsSinglePage;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal float Range;
            internal float SpotAngle;
            // Directional-only snapshot fields.
            internal int FirstLevel;
        }

        private VirtualShadowMapProjection[] m_Projections = new VirtualShadowMapProjection[32];
        private VividVSMNextMapEntry[] m_NextMap = new VividVSMNextMapEntry[32];
        private TrackedLightRecord[] m_PreviousRecords = new TrackedLightRecord[16];
        private TrackedLightRecord[] m_CurrentRecords = new TrackedLightRecord[16];
        private readonly long[] m_PrevDirOriginX = new long[VirtualShadowMapClipmapLayout.MaxLevels];
        private readonly long[] m_PrevDirOriginY = new long[VirtualShadowMapClipmapLayout.MaxLevels];
        private readonly float[] m_PrevDirDepthMin = new float[VirtualShadowMapClipmapLayout.MaxLevels];
        private readonly float[] m_PrevDirDepthMax = new float[VirtualShadowMapClipmapLayout.MaxLevels];
        private readonly long[] m_CurrDirOriginX = new long[VirtualShadowMapClipmapLayout.MaxLevels];
        private readonly long[] m_CurrDirOriginY = new long[VirtualShadowMapClipmapLayout.MaxLevels];
        private readonly float[] m_CurrDirDepthMin = new float[VirtualShadowMapClipmapLayout.MaxLevels];
        private readonly float[] m_CurrDirDepthMax = new float[VirtualShadowMapClipmapLayout.MaxLevels];

        private ulong m_RecordedCameraId;
        private int m_PreviousRecordCount;
        private int m_PreviousProjectionCount;
        private int m_PreviousDirCount;
        private bool m_HasRecordedFrame;

        private ulong m_PreparedCameraId;
        private int m_CurrentRecordCount;
        private int m_CurrentDirCount;
        private int m_DistantRefreshCursor;

        internal VirtualShadowMapProjection[] Projections => m_Projections;
        internal VividVSMNextMapEntry[] NextMap => m_NextMap;
        internal int ProjectionCount { get; private set; }
        internal int NextMapCount { get; private set; }
        internal int DirectionalCount => m_CurrentDirCount;
        internal int FullLocalCount { get; private set; }
        internal int SinglePageCount { get; private set; }
        internal int DistantRefreshedThisFrame { get; private set; }
        internal int TotalPageTableEntries { get; private set; }
        internal int TotalHierarchyNodes { get; private set; }
        internal int TotalBoundRectCount { get; private set; }
        internal int MaxMip0Pages { get; private set; }
        internal bool RequiresRemap { get; private set; }
        internal bool RequiresFeedbackReset { get; private set; }

        // Builds Unity left-handed Y-up view & perspective matrices for Point Light cube face [0..5].
        internal static void BuildPointFaceMatrices(
            Vector3 lightPosWS,
            float range,
            int faceIndex,
            out Matrix4x4 view,
            out Matrix4x4 projection,
            out float nearPlane)
        {
            int clampedFace = Mathf.Clamp(faceIndex, 0, PointFaceCount - 1);
            nearPlane = CalculateLocalNearPlane(range);
            float farPlane = Mathf.Max(range, nearPlane + 0.01f);
            Quaternion faceRotation = Quaternion.LookRotation(s_CubeFaceForwards[clampedFace], s_CubeFaceUps[clampedFace]);
            view = Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f))
                * Matrix4x4.TRS(lightPosWS, faceRotation, Vector3.one).inverse;
            projection = Matrix4x4.Perspective(90.0f, 1.0f, nearPlane, farPlane);
        }

        internal static void BuildSpotMatrices(
            Vector3 lightPosWS,
            Quaternion lightRotation,
            float range,
            float spotAngle,
            out Matrix4x4 view,
            out Matrix4x4 projection,
            out float nearPlane)
        {
            nearPlane = CalculateLocalNearPlane(range);
            float farPlane = Mathf.Max(range, nearPlane + 0.01f);
            float clampedFov = Mathf.Clamp(spotAngle, 1.0f, 179.0f);
            view = Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f))
                * Matrix4x4.TRS(lightPosWS, lightRotation, Vector3.one).inverse;
            projection = Matrix4x4.Perspective(clampedFov, 1.0f, nearPlane, farPlane);
        }

        internal static bool UsesCubeFaces(VividVSMLightType lightType)
        {
            return lightType == VividVSMLightType.Point
                || lightType == VividVSMLightType.Rect
                || lightType == VividVSMLightType.Tube;
        }

        internal static bool IsLocalLightType(VividVSMLightType lightType)
        {
            return lightType == VividVSMLightType.Point
                || lightType == VividVSMLightType.Spot
                || lightType == VividVSMLightType.Rect
                || lightType == VividVSMLightType.Tube;
        }

        internal static float CalculateAreaEffectiveRange(float range, float width, float height, bool isTube)
        {
            float w = Mathf.Max(width, 0.0f);
            float h = isTube ? 0.0f : Mathf.Max(height, 0.0f);
            float halfExtent = isTube ? 0.5f * w : 0.5f * Mathf.Sqrt(w * w + h * h);
            return Mathf.Max(range, 0.001f) + halfExtent;
        }

        internal static float CalculateLocalNearPlane(float range)
        {
            return Mathf.Clamp(range * 0.002f, 0.02f, 0.2f);
        }

        internal static float CalculateProjectedScreenDiameter(
            Vector3 lightPosWS,
            float range,
            Vector3 cameraPosWS,
            float cameraVerticalFovDegrees,
            int viewportHeight)
        {
            if (viewportHeight <= 0 || range <= 0.0f)
                return float.PositiveInfinity;
            float dist = Vector3.Distance(lightPosWS, cameraPosWS);
            if (dist <= range * 1.05f)
                return float.PositiveInfinity;
            float tanHalfFov = Mathf.Tan(Mathf.Clamp(cameraVerticalFovDegrees, 1.0f, 179.0f) * 0.5f * Mathf.Deg2Rad);
            return (range * viewportHeight) / Mathf.Max(dist * tanHalfFov, 1e-4f);
        }

        internal static bool ShouldUseSinglePage(
            Vector3 lightPosWS,
            float range,
            Vector3 cameraPosWS,
            float cameraVerticalFovDegrees,
            int viewportHeight,
            float thresholdPixels = 128.0f)
        {
            if (viewportHeight <= 0 || thresholdPixels <= 0.0f)
                return false;
            float projectedDiameterPx = CalculateProjectedScreenDiameter(
                lightPosWS, range, cameraPosWS, cameraVerticalFovDegrees, viewportHeight);
            return projectedDiameterPx <= thresholdPixels;
        }

        internal void PrepareFrame(
            ulong cameraId,
            VirtualShadowMapClipmapLayout directionalLayout,
            VividVSMLocalLightDescriptor[] localLights = null,
            int localLightCount = 0,
            int maxDistantPagesPerFrame = DefaultMaxDistantPagesPerFrame)
        {
            m_PreparedCameraId = cameraId;
            ProjectionCount = 0;
            m_CurrentDirCount = 0;
            FullLocalCount = 0;
            SinglePageCount = 0;
            DistantRefreshedThisFrame = 0;
            m_CurrentRecordCount = 0;
            TotalPageTableEntries = 0;
            TotalHierarchyNodes = 0;
            TotalBoundRectCount = 0;
            MaxMip0Pages = 1;

            bool sameCamera = m_HasRecordedFrame && m_RecordedCameraId == cameraId;
            NextMapCount = sameCamera ? m_PreviousProjectionCount : 0;
            EnsureNextMapCapacity(Mathf.Max(NextMapCount, 1));
            for (int i = 0; i < NextMapCount; i++)
            {
                m_NextMap[i] = new VividVSMNextMapEntry
                {
                    NextVSMId = VirtualShadowMapAddressing.InvalidVSMId,
                    PageOffsetX = 0,
                    PageOffsetY = 0,
                    Flags = (uint)VividVSMNextMapFlags.InvalidateAll,
                };
            }

            RequiresRemap = !sameCamera;
            RequiresFeedbackReset = !sameCamera;

            uint pageCursor = 0u;
            uint hierarchyCursor = 0u;
            uint boundRectCursor = 0u;

            // 1. Directional Clipmaps always occupy [0 .. dirCount - 1] for zero-shift compatibility.
            if (directionalLayout != null && directionalLayout.Count > 0)
            {
                int dirCount = directionalLayout.Count;
                EnsureProjectionCapacity(ProjectionCount + dirCount);
                EnsureCurrentRecordCapacity(m_CurrentRecordCount + 1);

                int prevIdx = sameCamera ? FindPreviousRecord(directionalLayout.LightId, VividVSMLightType.Directional) : -1;
                bool sameDirBasis = prevIdx >= 0
                    && m_PreviousRecords[prevIdx].VSMCount == dirCount
                    && m_PreviousRecords[prevIdx].Resolution == directionalLayout.Resolution
                    && m_PreviousRecords[prevIdx].FirstLevel == directionalLayout.FirstLevel
                    && m_PreviousRecords[prevIdx].Rotation.Equals(directionalLayout.Rotation);

                int pages = directionalLayout.Resolution / VirtualShadowMapPrototypeRuntime.PageSize;
                uint pagesPerLevel = (uint)(pages * pages);
                uint hierarchyPerLevel = (uint)VirtualShadowMapPrototypeRuntime.CalculateHierarchyNodesPerLevel(pages);
                MaxMip0Pages = Mathf.Max(MaxMip0Pages, (int)pagesPerLevel);
                Vector3 toLight = -(directionalLayout.Rotation * Vector3.forward);
                uint ditherOne = math.asuint(1.0f);

                for (int i = 0; i < dirCount; i++)
                {
                    int vsmId = ProjectionCount + i;
                    int prevVsmId = sameDirBasis ? m_PreviousRecords[prevIdx].BaseVSMId + i : -1;
                    bool sameDepth = sameDirBasis && i < m_PreviousDirCount
                        && m_PrevDirDepthMin[i] == directionalLayout.DepthMins[i]
                        && m_PrevDirDepthMax[i] == directionalLayout.DepthMaxs[i];
                    int dx = sameDepth ? VirtualShadowMapProjectionSet.ClampPageDelta(directionalLayout.OriginX[i] - m_PrevDirOriginX[i], pages) : 0;
                    int dy = sameDepth ? VirtualShadowMapProjectionSet.ClampPageDelta(directionalLayout.OriginY[i] - m_PrevDirOriginY[i], pages) : 0;

                    if (prevVsmId >= 0 && prevVsmId < NextMapCount)
                    {
                        m_NextMap[prevVsmId] = new VividVSMNextMapEntry
                        {
                            NextVSMId = sameDepth ? (uint)vsmId : VirtualShadowMapAddressing.InvalidVSMId,
                            PageOffsetX = dx,
                            PageOffsetY = dy,
                            Flags = sameDepth
                                ? (uint)VividVSMNextMapFlags.Valid
                                : (uint)VividVSMNextMapFlags.InvalidateAll,
                        };
                    }

                    RequiresRemap |= !sameDepth || dx != 0 || dy != 0 || (sameDepth && prevVsmId != vsmId);
                    RequiresFeedbackReset |= Math.Abs(dx) >= pages || Math.Abs(dy) >= pages;

                    m_CurrDirOriginX[i] = directionalLayout.OriginX[i];
                    m_CurrDirOriginY[i] = directionalLayout.OriginY[i];
                    m_CurrDirDepthMin[i] = directionalLayout.DepthMins[i];
                    m_CurrDirDepthMax[i] = directionalLayout.DepthMaxs[i];

                    Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(directionalLayout.Projections[i], true);
                    Vector3 camCenter = directionalLayout.CameraPosition;
                    VividVSMProjectionFlags flags = VividVSMProjectionFlags.UseReceiverMask
                        | (i == dirCount - 1 ? VividVSMProjectionFlags.IsCoarseClipLevel : VividVSMProjectionFlags.None);

                    m_Projections[vsmId] = new VirtualShadowMapProjection
                    {
                        WorldToClip = gpuProj * directionalLayout.Views[i],
                        WorldToShadow = VividShadowData.BuildWorldToShadowMatrix(directionalLayout.Projections[i], directionalLayout.Views[i]),
                        SelectionSphere = new Vector4(camCenter.x, camCenter.y, camCenter.z, -directionalLayout.Radii[i]),
                        Parameters = new Vector4(2.0f * directionalLayout.Radii[i] / directionalLayout.Resolution,
                            directionalLayout.NormalBias, 0.0f, directionalLayout.MaxDistance),
                        LightDirectionAndSpot = new Vector4(toLight.x, toLight.y, toLight.z, 0.0f),
                        ProjectionScales = new Vector4(gpuProj.m00, gpuProj.m11, gpuProj.m22, gpuProj.m23),
                        Addressing = new uint4(pageCursor, hierarchyCursor, (uint)pages, 1u),
                        Meta = new uint4((uint)VividVSMLightType.Directional, (uint)flags, 0u, (uint)(dirCount - i)),
                        IdentityAndOrigin = new uint4(
                            0u,
                            sameDepth && prevVsmId >= 0 ? (uint)prevVsmId : VirtualShadowMapAddressing.InvalidVSMId,
                            unchecked((uint)(int)directionalLayout.OriginX[i]),
                            unchecked((uint)(int)directionalLayout.OriginY[i])),
                        Extended = new uint4(ditherOne, boundRectCursor, 0u, 0u),
                    };

                    pageCursor += pagesPerLevel;
                    hierarchyCursor += hierarchyPerLevel;
                    boundRectCursor += 1u;
                }

                m_CurrentRecords[m_CurrentRecordCount++] = new TrackedLightRecord
                {
                    LightEntityId = directionalLayout.LightId,
                    LightType = VividVSMLightType.Directional,
                    BaseVSMId = 0,
                    VSMCount = dirCount,
                    Resolution = directionalLayout.Resolution,
                    IsSinglePage = false,
                    Rotation = directionalLayout.Rotation,
                    FirstLevel = directionalLayout.FirstLevel,
                };
                m_CurrentDirCount = dirCount;
                ProjectionCount += dirCount;
            }

            // 2. Full Local Lights (Point 6 faces, Spot 1 face), followed by Single-Page Local Lights.
            // Pre-count single-page faces so Round-Robin refresh wraps cleanly across the active distant set.
            int totalSinglePageFaces = 0;
            if (localLights != null && localLightCount > 0)
            {
                for (int i = 0; i < localLightCount; i++)
                {
                    ref readonly VividVSMLocalLightDescriptor d = ref localLights[i];
                    if (d.IsSinglePage && IsLocalLightType(d.LightType))
                        totalSinglePageFaces += UsesCubeFaces(d.LightType) ? PointFaceCount : 1;
                }
            }
            int distantRefreshBudget = maxDistantPagesPerFrame > 0 && totalSinglePageFaces > 0
                ? Mathf.Min(maxDistantPagesPerFrame, totalSinglePageFaces) : 0;
            int distantStartCursor = totalSinglePageFaces > 0
                ? ((m_DistantRefreshCursor % totalSinglePageFaces) + totalSinglePageFaces) % totalSinglePageFaces : 0;
            int singlePageFaceIndex = 0;

            if (localLights != null && localLightCount > 0)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    bool singlePagePass = pass == 1;
                    for (int i = 0; i < localLightCount; i++)
                    {
                        ref readonly VividVSMLocalLightDescriptor desc = ref localLights[i];
                        if (desc.IsSinglePage != singlePagePass)
                            continue;
                        if (!IsLocalLightType(desc.LightType))
                            continue;

                        bool usesCubeFaces = UsesCubeFaces(desc.LightType);
                        int faceCount = usesCubeFaces ? PointFaceCount : 1;
                        int resolution = desc.IsSinglePage
                            ? VirtualShadowMapPrototypeRuntime.PageSize
                            : VirtualShadowMapProjectionSet.ResolveResolution(
                                desc.MaxResolution > 0 ? desc.MaxResolution : DefaultLocalMaxResolution,
                                DefaultLocalMaxResolution);
                        int pagesPerAxis = desc.IsSinglePage ? 1 : resolution / VirtualShadowMapPrototypeRuntime.PageSize;
                        int mipCount = desc.IsSinglePage ? 1 : VirtualShadowMapAddressing.CalculateMipCount(pagesPerAxis);
                        uint entriesPerFace = desc.IsSinglePage
                            ? 1u
                            : (uint)VirtualShadowMapAddressing.CalculateTotalMipChainEntries(pagesPerAxis);
                        uint hierarchyPerFace = desc.IsSinglePage
                            ? 0u
                            : (uint)VirtualShadowMapPrototypeRuntime.CalculateHierarchyNodesPerLevel(pagesPerAxis);
                        MaxMip0Pages = Mathf.Max(MaxMip0Pages, pagesPerAxis * pagesPerAxis);

                        EnsureProjectionCapacity(ProjectionCount + faceCount);
                        EnsureCurrentRecordCapacity(m_CurrentRecordCount + 1);

                        int baseVsmId = ProjectionCount;
                        int prevIdx = sameCamera ? FindPreviousRecord(desc.LightEntityId, desc.LightType) : -1;
                        bool cacheReusable = prevIdx >= 0
                            && !desc.ForceUncached
                            && m_PreviousRecords[prevIdx].VSMCount == faceCount
                            && m_PreviousRecords[prevIdx].Resolution == resolution
                            && m_PreviousRecords[prevIdx].IsSinglePage == desc.IsSinglePage
                            && m_PreviousRecords[prevIdx].Position == desc.PositionWS
                            && Mathf.Approximately(m_PreviousRecords[prevIdx].Range, desc.Range)
                            && (usesCubeFaces
                                || (m_PreviousRecords[prevIdx].Rotation.Equals(desc.Rotation)
                                    && Mathf.Approximately(m_PreviousRecords[prevIdx].SpotAngle, desc.SpotAngle)));

                        Vector3 lightForward = desc.Rotation * Vector3.forward;
                        float cosOuterSpot = desc.LightType == VividVSMLightType.Spot
                            ? Mathf.Cos(Mathf.Clamp(desc.SpotAngle, 1.0f, 179.0f) * 0.5f * Mathf.Deg2Rad)
                            : -2.0f;
                        uint ditherBits = math.asuint(desc.TexelDitherScale > 0.0f ? desc.TexelDitherScale : 1.0f);
                        VividVSMProjectionFlags flags = VividVSMProjectionFlags.None;
                        if (desc.IsSinglePage)
                            flags |= VividVSMProjectionFlags.SinglePage | VividVSMProjectionFlags.SkipDynamicInvalid;
                        else if (desc.UseReceiverMask)
                            flags |= VividVSMProjectionFlags.UseReceiverMask;
                        if (desc.ForceUncached)
                            flags |= VividVSMProjectionFlags.Uncached;

                        for (int face = 0; face < faceCount; face++)
                        {
                            int vsmId = baseVsmId + face;
                            bool roundRobinRefresh = false;
                            if (desc.IsSinglePage)
                            {
                                if (distantRefreshBudget > 0 && cacheReusable)
                                {
                                    int offsetFromCursor = (singlePageFaceIndex - distantStartCursor + totalSinglePageFaces) % totalSinglePageFaces;
                                    if (offsetFromCursor < distantRefreshBudget)
                                    {
                                        roundRobinRefresh = true;
                                        DistantRefreshedThisFrame++;
                                    }
                                }
                                singlePageFaceIndex++;
                            }

                            bool faceCacheReusable = cacheReusable && !roundRobinRefresh;
                            int prevVsmId = faceCacheReusable ? m_PreviousRecords[prevIdx].BaseVSMId + face : -1;
                            if (prevVsmId >= 0 && prevVsmId < NextMapCount)
                            {
                                m_NextMap[prevVsmId] = new VividVSMNextMapEntry
                                {
                                    NextVSMId = (uint)vsmId,
                                    PageOffsetX = 0,
                                    PageOffsetY = 0,
                                    Flags = (uint)VividVSMNextMapFlags.Valid,
                                };
                                if (prevVsmId != vsmId)
                                    RequiresRemap = true;
                            }
                            else if (prevIdx >= 0)
                            {
                                RequiresRemap = true;
                            }

                            Matrix4x4 view, proj;
                            float nearPlane;
                            if (usesCubeFaces)
                                BuildPointFaceMatrices(desc.PositionWS, desc.Range, face, out view, out proj, out nearPlane);
                            else
                                BuildSpotMatrices(desc.PositionWS, desc.Rotation, desc.Range, desc.SpotAngle, out view, out proj, out nearPlane);

                            Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(proj, true);
                            m_Projections[vsmId] = new VirtualShadowMapProjection
                            {
                                WorldToClip = gpuProj * view,
                                WorldToShadow = VividShadowData.BuildWorldToShadowMatrix(proj, view),
                                SelectionSphere = new Vector4(desc.PositionWS.x, desc.PositionWS.y, desc.PositionWS.z, desc.Range),
                                Parameters = new Vector4(desc.SourceRadius, desc.NormalBias, desc.ResolutionLodBias, nearPlane),
                                LightDirectionAndSpot = new Vector4(lightForward.x, lightForward.y, lightForward.z, cosOuterSpot),
                                ProjectionScales = new Vector4(gpuProj.m00, gpuProj.m11, gpuProj.m22, gpuProj.m23),
                                Addressing = new uint4(pageCursor, hierarchyCursor, (uint)pagesPerAxis, (uint)mipCount),
                                Meta = new uint4((uint)desc.LightType, (uint)flags, Math.Min(desc.MinMipLevel, (uint)(mipCount - 1)), (uint)face),
                                IdentityAndOrigin = new uint4(
                                    (uint)baseVsmId,
                                    faceCacheReusable && prevVsmId >= 0 ? (uint)prevVsmId : VirtualShadowMapAddressing.InvalidVSMId,
                                    0u,
                                    0u),
                                Extended = new uint4(ditherBits, boundRectCursor, desc.PunctualLightIndex, desc.IsAreaLight ? 1u : 0u),
                            };

                            pageCursor += entriesPerFace;
                            hierarchyCursor += hierarchyPerFace;
                            boundRectCursor += (uint)mipCount;
                        }

                        m_CurrentRecords[m_CurrentRecordCount++] = new TrackedLightRecord
                        {
                            LightEntityId = desc.LightEntityId,
                            LightType = desc.LightType,
                            BaseVSMId = baseVsmId,
                            VSMCount = faceCount,
                            Resolution = resolution,
                            IsSinglePage = desc.IsSinglePage,
                            Position = desc.PositionWS,
                            Rotation = desc.Rotation,
                            Range = desc.Range,
                            SpotAngle = desc.SpotAngle,
                        };

                        ProjectionCount += faceCount;
                        if (desc.IsSinglePage) SinglePageCount += faceCount;
                        else FullLocalCount += faceCount;
                    }
                }
            }

            // Detect removed lights from the previous frame so their physical slots are cleared.
            if (sameCamera)
            {
                for (int i = 0; i < NextMapCount; i++)
                {
                    if ((m_NextMap[i].Flags & (uint)VividVSMNextMapFlags.Valid) == 0u)
                    {
                        RequiresRemap = true;
                        break;
                    }
                }
            }

            TotalPageTableEntries = (int)pageCursor;
            TotalHierarchyNodes = (int)hierarchyCursor;
            TotalBoundRectCount = (int)boundRectCursor;
        }

        internal void CommitRecordedFrame()
        {
            if (m_PreviousRecords.Length < m_CurrentRecordCount)
                m_PreviousRecords = new TrackedLightRecord[m_CurrentRecords.Length];
            if (m_CurrentRecordCount > 0)
                Array.Copy(m_CurrentRecords, m_PreviousRecords, m_CurrentRecordCount);
            if (m_CurrentDirCount > 0)
            {
                Array.Copy(m_CurrDirOriginX, m_PrevDirOriginX, m_CurrentDirCount);
                Array.Copy(m_CurrDirOriginY, m_PrevDirOriginY, m_CurrentDirCount);
                Array.Copy(m_CurrDirDepthMin, m_PrevDirDepthMin, m_CurrentDirCount);
                Array.Copy(m_CurrDirDepthMax, m_PrevDirDepthMax, m_CurrentDirCount);
            }
            if (SinglePageCount > 0 && DistantRefreshedThisFrame > 0)
                m_DistantRefreshCursor = (m_DistantRefreshCursor + DistantRefreshedThisFrame) % SinglePageCount;
            else if (SinglePageCount == 0)
                m_DistantRefreshCursor = 0;
            m_RecordedCameraId = m_PreparedCameraId;
            m_PreviousRecordCount = m_CurrentRecordCount;
            m_PreviousProjectionCount = ProjectionCount;
            m_PreviousDirCount = m_CurrentDirCount;
            m_HasRecordedFrame = true;
        }

        internal void InvalidateHistory()
        {
            m_HasRecordedFrame = false;
            m_PreviousRecordCount = 0;
            m_PreviousProjectionCount = 0;
            m_PreviousDirCount = 0;
            m_DistantRefreshCursor = 0;
            DistantRefreshedThisFrame = 0;
        }

        private int FindPreviousRecord(ulong lightEntityId, VividVSMLightType lightType)
        {
            for (int i = 0; i < m_PreviousRecordCount; i++)
            {
                if (m_PreviousRecords[i].LightEntityId == lightEntityId
                    && m_PreviousRecords[i].LightType == lightType)
                    return i;
            }
            return -1;
        }

        private void EnsureProjectionCapacity(int required)
        {
            if (m_Projections.Length >= required) return;
            int next = Mathf.NextPowerOfTwo(required);
            Array.Resize(ref m_Projections, next);
        }

        private void EnsureNextMapCapacity(int required)
        {
            if (m_NextMap.Length >= required) return;
            int next = Mathf.NextPowerOfTwo(required);
            Array.Resize(ref m_NextMap, next);
        }

        private void EnsureCurrentRecordCapacity(int required)
        {
            if (m_CurrentRecords.Length >= required) return;
            int next = Mathf.NextPowerOfTwo(required);
            Array.Resize(ref m_CurrentRecords, next);
        }
    }
}

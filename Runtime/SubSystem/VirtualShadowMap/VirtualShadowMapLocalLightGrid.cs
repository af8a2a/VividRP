using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.VirtualShadowMap
{
    // Phase 1: Zero-GC punctual shadow-light collector, pruned light grid resource manager,
    // and GPU dispatcher for VSMPruneLightGridCS / VSMMarkCoarsePagesUnified / VSMMarkLocalReceiverPages.
    internal sealed class VirtualShadowMapLocalLightGrid : IDisposable
    {
        internal const int MaxFullLocalVSMs = 64;
        private const uint PunctualTypePoint = 0u;
        private const uint PunctualTypeSpot = 1u;
        private const uint AreaTypeTube = 0u;
        private const uint AreaTypeRectangle = 1u;

        internal static readonly int LayeredOffsetId = Shader.PropertyToID("_VSMLayeredOffset");
        internal static readonly int LayeredLightListId = Shader.PropertyToID("_VSMLayeredLightList");
        internal static readonly int LogBaseBufferId = Shader.PropertyToID("_VSMLogBaseBuffer");
        internal static readonly int PunctualLightsId = Shader.PropertyToID("_VSMPunctualLights");
        internal static readonly int PunctualLightVSMInfoId = Shader.PropertyToID("_VSMPunctualLightVSMInfo");
        internal static readonly int AreaLightsId = Shader.PropertyToID("_VSMAreaLights");
        internal static readonly int AreaLightVSMInfoId = Shader.PropertyToID("_VSMAreaLightVSMInfo");
        internal static readonly int PrunedLightOffsetCountRWId = Shader.PropertyToID("_VSMPrunedLightOffsetCountRW");
        internal static readonly int PrunedLightIndicesRWId = Shader.PropertyToID("_VSMPrunedLightIndicesRW");
        internal static readonly int PrunedLightOffsetCountId = Shader.PropertyToID("_VSMPrunedLightOffsetCount");
        internal static readonly int PrunedLightIndicesId = Shader.PropertyToID("_VSMPrunedLightIndices");
        internal static readonly int ClusterCountId = Shader.PropertyToID("_VSMClusterCount");
        internal static readonly int PunctualLightCountId = Shader.PropertyToID("_VSMPunctualLightCount");
        internal static readonly int AreaLightCountId = Shader.PropertyToID("_VSMAreaLightCount");
        internal static readonly int PruneAreaCategoryEnabledId = Shader.PropertyToID("_VSMPruneAreaCategoryEnabled");
        internal static readonly int MarkCoarsePagesLocalId = Shader.PropertyToID("_VSMMarkCoarsePagesLocal");
        internal static readonly int LocalPagePressureEnabledId = Shader.PropertyToID("_VSMLocalPagePressureEnabled");
        internal static readonly int CameraViewParamsId = Shader.PropertyToID("_VSMCameraViewParams");
        internal static readonly int ClusterParamsId = Shader.PropertyToID("_VSMClusterParams");
        internal static readonly int ClusterDepthParamsId = Shader.PropertyToID("_VSMClusterDepthParams");
        internal static readonly int CameraViewMatrixId = Shader.PropertyToID("_VSMCameraViewMatrix");

        private static readonly int PageRequestFlagsId = Shader.PropertyToID("_VSMPageRequestFlags");
        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");
        private static readonly int PageTableEntryCountId = Shader.PropertyToID("_VSMPrototypePageTableEntryCount");
        private static readonly int PageSizeId = Shader.PropertyToID("_VSMPrototypePageSize");
        private static readonly int RequestEnabledId = Shader.PropertyToID("_VSMPrototypeRequestEnabled");
        private static readonly int OutputWidthId = Shader.PropertyToID("_CSMOutputWidth");
        private static readonly int OutputHeightId = Shader.PropertyToID("_CSMOutputHeight");
        private static readonly int InvViewProjMatrixId = Shader.PropertyToID("_CSMInvViewProjMatrix");

        private static readonly ProfilingSampler PruneSampler = new("VSM.PruneLightGrid");
        private static readonly ProfilingSampler MarkLocalSampler = new("VSM.MarkLocalReceivers");

        private VividVSMLocalLightDescriptor[] m_CollectedDescriptors = new VividVSMLocalLightDescriptor[16];
        private int[] m_CandidateLightIndices = new int[32];
        private bool[] m_CandidateIsAreaLight = new bool[32];
        private float[] m_CandidateScreenDiameters = new float[32];
        private int2[] m_PunctualVSMInfoUpload = new int2[16];
        private int4[] m_AreaVSMInfoUpload = new int4[16];
        private GraphicsBuffer m_PunctualLightVSMInfo;
        private GraphicsBuffer m_AreaLightVSMInfo;
        private GraphicsBuffer m_FallbackAreaLights;
        private GraphicsBuffer m_PrunedLightOffsetCount;
        private GraphicsBuffer m_PrunedLightIndices;
        private GraphicsBuffer m_UnifiedProjections;
        private GraphicsBuffer m_LocalPageRequestFlags;
        private GraphicsBuffer m_LocalPageReceiverMasks;

        private GraphicsBuffer m_SourcePunctualLights;
        private GraphicsBuffer m_SourceAreaLights;
        private GraphicsBuffer m_SourceLayeredOffset;
        private GraphicsBuffer m_SourceLayeredLightList;
        private GraphicsBuffer m_SourceLogBaseBuffer;
        private VirtualShadowMapRegistry m_SourceRegistry;

        private int m_CollectedCount;
        private int m_PunctualLightCount;
        private int m_AreaLightCount;
        private int m_ClusterCount;
        private int m_UnifiedProjectionCount;
        private int m_UnifiedPageTableEntryCount;
        private Vector4 m_CameraViewParams;
        private Vector4 m_ClusterParams;
        private Vector4 m_ClusterDepthParams;
        private Matrix4x4 m_CameraViewMatrix;
        private bool m_MarkCoarsePagesLocal;

        internal bool Active { get; private set; }
        internal int CollectedLightCount => m_CollectedCount;
        internal int PunctualLightCount => m_PunctualLightCount;
        internal int AreaLightCount => m_AreaLightCount;
        internal int ClusterCount => m_ClusterCount;
        internal int UnifiedProjectionCount => m_UnifiedProjectionCount;
        internal int UnifiedPageTableEntryCount => m_UnifiedPageTableEntryCount;
        internal GraphicsBuffer SourcePunctualLights => m_SourcePunctualLights;
        internal GraphicsBuffer SourceAreaLights => m_SourceAreaLights ?? m_FallbackAreaLights;
        internal GraphicsBuffer SourceLogBaseBuffer => m_SourceLogBaseBuffer;
        internal GraphicsBuffer PunctualLightVSMInfo => m_PunctualLightVSMInfo;
        internal GraphicsBuffer AreaLightVSMInfo => m_AreaLightVSMInfo;
        internal GraphicsBuffer PrunedLightOffsetCount => m_PrunedLightOffsetCount;
        internal GraphicsBuffer PrunedLightIndices => m_PrunedLightIndices;
        internal GraphicsBuffer UnifiedProjections => m_UnifiedProjections;
        internal GraphicsBuffer LocalPageRequestFlags => m_LocalPageRequestFlags;
        internal GraphicsBuffer LocalPageReceiverMasks => m_LocalPageReceiverMasks;
        internal Vector4 CameraViewParams => m_CameraViewParams;
        internal Vector4 ClusterParams => m_ClusterParams;
        internal Vector4 ClusterDepthParams => m_ClusterDepthParams;
        internal Matrix4x4 CameraViewMatrix => m_CameraViewMatrix;
        internal VividVSMLocalLightDescriptor[] CollectedDescriptors => m_CollectedDescriptors;
        internal int2[] PunctualVSMInfoUpload => m_PunctualVSMInfoUpload;
        internal int4[] AreaVSMInfoUpload => m_AreaVSMInfoUpload;

        internal void Disable()
        {
            Active = false;
            m_CollectedCount = 0;
            m_PunctualLightCount = 0;
            m_AreaLightCount = 0;
            m_ClusterCount = 0;
            m_UnifiedProjectionCount = 0;
            m_UnifiedPageTableEntryCount = 0;
            m_SourcePunctualLights = null;
            m_SourceAreaLights = null;
            m_SourceLayeredOffset = null;
            m_SourceLayeredLightList = null;
            m_SourceLogBaseBuffer = null;
            m_SourceRegistry = null;
        }

        // Collects shadow-casting Point/Spot lights from VividLightData into reusable scratch arrays (0B GC steady state).
        internal int CollectLocalLights(
            VividLightData lightData,
            VividCameraData cameraData,
            int localMaxResolution,
            float localLodBias,
            float distantThresholdPx,
            bool useReceiverMask)
        {
            if (lightData == null || cameraData?.camera == null)
            {
                m_CollectedCount = 0;
                m_PunctualLightCount = 0;
                m_AreaLightCount = 0;
                return 0;
            }

            Camera camera = cameraData.camera;
            int viewportHeight = cameraData.actualHeight > 0 ? cameraData.actualHeight : camera.pixelHeight;
            return CollectLocalLights(
                lightData,
                camera.transform.position,
                camera.fieldOfView,
                viewportHeight,
                localMaxResolution,
                localLodBias,
                distantThresholdPx,
                useReceiverMask);
        }

        internal int CollectLocalLights(
            VividLightData lightData,
            Vector3 cameraPosWS,
            float cameraFovDeg,
            int viewportHeight,
            int localMaxResolution,
            float localLodBias,
            float distantThresholdPx,
            bool useReceiverMask)
        {
            m_CollectedCount = 0;
            m_PunctualLightCount = 0;
            m_AreaLightCount = 0;
            if (lightData == null)
                return 0;

            lightData.CompleteLightGridPrepare();
            int punctualCount = lightData.punctualLightCount;
            int areaCount = lightData.areaLightCount;
            m_PunctualLightCount = punctualCount;
            m_AreaLightCount = areaCount;
            EnsurePunctualUploadCapacity(Mathf.Max(punctualCount, 1));
            for (int i = 0; i < punctualCount; i++)
                m_PunctualVSMInfoUpload[i] = new int2(-1, 0);
            EnsureAreaUploadCapacity(Mathf.Max(areaCount, 1));
            for (int i = 0; i < areaCount; i++)
                m_AreaVSMInfoUpload[i] = new int4(-1, 0, 0, 0);

            int punctualIterCount = (punctualCount > 0 && lightData.punctualLights != null && lightData.punctualLightShadowMeta != null)
                ? Mathf.Min(punctualCount, Mathf.Min(lightData.punctualLightShadowMeta.Length, lightData.punctualLights.Length)) : 0;
            int areaIterCount = (areaCount > 0 && lightData.areaLights != null && lightData.areaLightShadowMeta != null)
                ? Mathf.Min(areaCount, Mathf.Min(lightData.areaLightShadowMeta.Length, lightData.areaLights.Length)) : 0;
            if (punctualIterCount == 0 && areaIterCount == 0)
                return 0;

            int resolvedLocalRes = VirtualShadowMapProjectionSet.ResolveResolution(
                localMaxResolution > 0 ? localMaxResolution : VirtualShadowMapRegistry.DefaultLocalMaxResolution,
                VirtualShadowMapRegistry.DefaultLocalMaxResolution);

            // 1. Gather shadow-casting Point/Spot & LTC AreaLight (Rectangle/Tube) candidates scored by screen diameter.
            int candidateCount = 0;
            EnsureCandidateCapacity(Mathf.Max(punctualIterCount + areaIterCount, 1));
            for (int i = 0; i < punctualIterCount; i++)
            {
                ref readonly VividLightData.PunctualLightShadowMeta meta = ref lightData.punctualLightShadowMeta[i];
                if ((meta.flags & VividLightRenderDataFlags.CastShadows) == 0)
                    continue;

                ref readonly VividLightData.PunctualLightData light = ref lightData.punctualLights[i];
                if (light.shadowStrength <= 0.0f || light.range <= 0.0f)
                    continue;
                if (light.lightType != PunctualTypePoint && light.lightType != PunctualTypeSpot)
                    continue;

                m_CandidateLightIndices[candidateCount] = i;
                m_CandidateIsAreaLight[candidateCount] = false;
                m_CandidateScreenDiameters[candidateCount] = VirtualShadowMapRegistry.CalculateProjectedScreenDiameter(
                    light.positionWS, light.range, cameraPosWS, cameraFovDeg, viewportHeight);
                candidateCount++;
            }

            for (int i = 0; i < areaIterCount; i++)
            {
                ref readonly VividLightData.AreaLightShadowMeta meta = ref lightData.areaLightShadowMeta[i];
                if ((meta.flags & VividLightRenderDataFlags.CastShadows) == 0 || meta.shadowStrength <= 0.0f)
                    continue;

                ref readonly VividLightData.AreaLightData light = ref lightData.areaLights[i];
                if (light.range <= 0.0f)
                    continue;
                bool isTube = light.lightType == AreaTypeTube;
                bool isRect = light.lightType == AreaTypeRectangle;
                if (!isTube && !isRect)
                    continue;
                if (light.width <= 1e-4f || (isRect && light.height <= 1e-4f))
                    continue;

                float effectiveRange = VirtualShadowMapRegistry.CalculateAreaEffectiveRange(
                    light.range, light.width, light.height, isTube);
                m_AreaVSMInfoUpload[i] = new int4(-1, 0, math.asint(Mathf.Clamp01(meta.shadowStrength)), 0);
                m_CandidateLightIndices[candidateCount] = i;
                m_CandidateIsAreaLight[candidateCount] = true;
                m_CandidateScreenDiameters[candidateCount] = VirtualShadowMapRegistry.CalculateProjectedScreenDiameter(
                    light.positionWS, effectiveRange, cameraPosWS, cameraFovDeg, viewportHeight);
                candidateCount++;
            }

            // 2. Stable descending sort (zero-GC insertion sort) so closest/largest lights receive Full VSM slots first.
            for (int i = 1; i < candidateCount; i++)
            {
                int idx = m_CandidateLightIndices[i];
                bool isArea = m_CandidateIsAreaLight[i];
                float score = m_CandidateScreenDiameters[i];
                int j = i - 1;
                while (j >= 0 && m_CandidateScreenDiameters[j] < score)
                {
                    m_CandidateLightIndices[j + 1] = m_CandidateLightIndices[j];
                    m_CandidateIsAreaLight[j + 1] = m_CandidateIsAreaLight[j];
                    m_CandidateScreenDiameters[j + 1] = m_CandidateScreenDiameters[j];
                    j--;
                }
                m_CandidateLightIndices[j + 1] = idx;
                m_CandidateIsAreaLight[j + 1] = isArea;
                m_CandidateScreenDiameters[j + 1] = score;
            }

            // 3. Assign Full VSM slots up to MaxFullLocalVSMs, automatically downgrading distant/overflow lights to Single-Page.
            int fullVsmUsed = 0;
            int singlePageVsmUsed = 0;
            int maxSinglePageVSMs = (int)VirtualShadowMapAddressing.MaxSinglePageShadowMaps;

            for (int c = 0; c < candidateCount; c++)
            {
                int i = m_CandidateLightIndices[c];
                bool isArea = m_CandidateIsAreaLight[c];
                float projectedDiameterPx = m_CandidateScreenDiameters[c];

                VividVSMLightType vsmLightType;
                int faceCost;
                if (isArea)
                {
                    ref readonly VividLightData.AreaLightData area = ref lightData.areaLights[i];
                    vsmLightType = area.lightType == AreaTypeTube ? VividVSMLightType.Tube : VividVSMLightType.Rect;
                    faceCost = VirtualShadowMapRegistry.PointFaceCount;
                }
                else
                {
                    ref readonly VividLightData.PunctualLightData p = ref lightData.punctualLights[i];
                    vsmLightType = p.lightType == PunctualTypePoint ? VividVSMLightType.Point : VividVSMLightType.Spot;
                    faceCost = vsmLightType == VividVSMLightType.Point ? VirtualShadowMapRegistry.PointFaceCount : 1;
                }

                bool isSinglePage = distantThresholdPx > 0.0f && viewportHeight > 0 && projectedDiameterPx <= distantThresholdPx;
                if (!isSinglePage && fullVsmUsed + faceCost > MaxFullLocalVSMs)
                {
                    if (distantThresholdPx > 0.0f)
                        isSinglePage = true;
                    else
                        continue;
                }

                if (isSinglePage)
                {
                    if (singlePageVsmUsed + faceCost > maxSinglePageVSMs)
                        continue;
                    singlePageVsmUsed += faceCost;
                }
                else
                {
                    fullVsmUsed += faceCost;
                }

                EnsureDescriptorCapacity(m_CollectedCount + 1);
                if (isArea)
                {
                    ref readonly VividLightData.AreaLightShadowMeta meta = ref lightData.areaLightShadowMeta[i];
                    ref readonly VividLightData.AreaLightData area = ref lightData.areaLights[i];
                    bool isTube = vsmLightType == VividVSMLightType.Tube;
                    float effectiveRange = VirtualShadowMapRegistry.CalculateAreaEffectiveRange(
                        area.range, area.width, area.height, isTube);
                    float sourceRadius = isTube
                        ? 0.5f * area.width
                        : 0.5f * Mathf.Sqrt(area.width * area.width + area.height * area.height);
                    Vector3 forward = area.forwardWS.sqrMagnitude > 1e-8f ? area.forwardWS.normalized : Vector3.forward;
                    Vector3 up = area.upWS.sqrMagnitude > 1e-8f ? area.upWS.normalized : Vector3.up;
                    if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.999f)
                        up = Mathf.Abs(forward.y) < 0.99f ? Vector3.up : Vector3.right;

                    m_CollectedDescriptors[m_CollectedCount++] = new VividVSMLocalLightDescriptor
                    {
                        LightEntityId = EntityId.ToULong(meta.lightEntityId),
                        LightType = vsmLightType,
                        PositionWS = area.positionWS,
                        Rotation = Quaternion.LookRotation(forward, up),
                        Range = effectiveRange,
                        SpotAngle = 90.0f,
                        SourceRadius = sourceRadius,
                        NormalBias = 1.0f,
                        ResolutionLodBias = localLodBias,
                        TexelDitherScale = 1.0f,
                        MaxResolution = resolvedLocalRes,
                        MinMipLevel = 0u,
                        IsSinglePage = isSinglePage,
                        UseReceiverMask = useReceiverMask && !isSinglePage,
                        ForceUncached = false,
                        IsAreaLight = true,
                        PunctualLightIndex = (uint)i,
                    };
                }
                else
                {
                    ref readonly VividLightData.PunctualLightShadowMeta meta = ref lightData.punctualLightShadowMeta[i];
                    ref readonly VividLightData.PunctualLightData light = ref lightData.punctualLights[i];
                    Vector3 forward = light.directionWS.sqrMagnitude > 1e-8f ? light.directionWS.normalized : Vector3.forward;
                    Vector3 up = light.upWS.sqrMagnitude > 1e-8f ? light.upWS.normalized : Vector3.up;
                    if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.999f)
                        up = Mathf.Abs(forward.y) < 0.99f ? Vector3.up : Vector3.right;

                    m_CollectedDescriptors[m_CollectedCount++] = new VividVSMLocalLightDescriptor
                    {
                        LightEntityId = EntityId.ToULong(meta.lightEntityId),
                        LightType = vsmLightType,
                        PositionWS = light.positionWS,
                        Rotation = Quaternion.LookRotation(forward, up),
                        Range = light.range,
                        SpotAngle = meta.spotAngle,
                        SourceRadius = meta.shapeRadius,
                        NormalBias = 1.0f,
                        ResolutionLodBias = localLodBias,
                        TexelDitherScale = 1.0f,
                        MaxResolution = resolvedLocalRes,
                        MinMipLevel = 0u,
                        IsSinglePage = isSinglePage,
                        UseReceiverMask = useReceiverMask && !isSinglePage,
                        ForceUncached = false,
                        IsAreaLight = false,
                        PunctualLightIndex = (uint)i,
                    };
                }
            }

            return m_CollectedCount;
        }

        internal void PopulatePunctualVSMInfo(VirtualShadowMapRegistry registry)
        {
            if (registry == null || (m_PunctualLightCount <= 0 && m_AreaLightCount <= 0))
                return;

            for (int i = 0; i < m_PunctualLightCount; i++)
                m_PunctualVSMInfoUpload[i] = new int2(-1, 0);
            for (int i = 0; i < m_AreaLightCount; i++)
                m_AreaVSMInfoUpload[i] = new int4(-1, 0, m_AreaVSMInfoUpload[i].z, 0);

            for (int vsmId = registry.DirectionalCount; vsmId < registry.ProjectionCount; vsmId++)
            {
                ref readonly VirtualShadowMapProjection proj = ref registry.Projections[vsmId];
                uint baseVsmId = proj.IdentityAndOrigin.x;
                if ((uint)vsmId != baseVsmId)
                    continue; // Cube faces 1..5 share Face0's BaseVSMId.
                uint lightIdx = proj.Extended.z;
                bool isArea = (proj.Extended.w & 1u) != 0u;
                if (isArea)
                {
                    if (lightIdx < (uint)m_AreaLightCount)
                        m_AreaVSMInfoUpload[lightIdx] = new int4((int)baseVsmId, (int)proj.Meta.y, m_AreaVSMInfoUpload[lightIdx].z, 0);
                }
                else
                {
                    if (lightIdx < (uint)m_PunctualLightCount)
                        m_PunctualVSMInfoUpload[lightIdx] = new int2((int)baseVsmId, (int)proj.Meta.y);
                }
            }
        }

        // Populates per-punctual-light & per-area-light VSM info from Registry projections and prepares GPU buffers + RenderGraph reads.
        internal bool PrepareForPass(
            IRenderPass pass,
            VirtualShadowMapRegistry registry,
            VividClusteredLightingData clustered,
            VividCameraData cameraData,
            float globalLocalLodBias,
            bool markCoarsePagesLocal = true)
        {
            Active = false;
            if (registry == null || clustered == null || cameraData?.camera == null
                || !clustered.supportsClusteredPunctualLights
                || m_CollectedCount <= 0
                || (m_PunctualLightCount <= 0 && m_AreaLightCount <= 0)
                || registry.ProjectionCount <= 0
                || registry.TotalPageTableEntries <= 0)
            {
                return false;
            }

            GraphicsBuffer punctualBuffer = clustered.punctualLights?.ImportedBuffer;
            GraphicsBuffer areaBuffer = clustered.areaLights?.ImportedBuffer;
            GraphicsBuffer offsetBuffer = clustered.layeredOffset?.ImportedBuffer;
            GraphicsBuffer listBuffer = clustered.layeredLightList?.ImportedBuffer;
            GraphicsBuffer logBaseBuffer = clustered.logBaseBuffer?.ImportedBuffer;
            if (punctualBuffer == null || offsetBuffer == null || listBuffer == null || logBaseBuffer == null
                || !punctualBuffer.IsValid() || !offsetBuffer.IsValid() || !listBuffer.IsValid() || !logBaseBuffer.IsValid())
            {
                return false;
            }

            int clusterCount = Mathf.Max(1, clustered.clusterTileCountX * clustered.clusterTileCountY * clustered.clusterSliceCount);
            int listCapacity = Mathf.Max(1, listBuffer.count);
            EnsureGpuBuffers(
                Mathf.Max(m_PunctualLightCount, 1),
                Mathf.Max(m_AreaLightCount, 1),
                clusterCount,
                listCapacity,
                registry.ProjectionCount,
                registry.TotalPageTableEntries);

            PopulatePunctualVSMInfo(registry);

            m_SourcePunctualLights = punctualBuffer;
            m_SourceAreaLights = areaBuffer != null && areaBuffer.IsValid() ? areaBuffer : m_FallbackAreaLights;
            m_SourceLayeredOffset = offsetBuffer;
            m_SourceLayeredLightList = listBuffer;
            m_SourceLogBaseBuffer = logBaseBuffer;
            m_SourceRegistry = registry;
            m_ClusterCount = clusterCount;
            m_UnifiedProjectionCount = registry.ProjectionCount;
            m_UnifiedPageTableEntryCount = registry.TotalPageTableEntries;
            m_MarkCoarsePagesLocal = markCoarsePagesLocal;

            Camera camera = cameraData.camera;
            Matrix4x4 projMatrix = cameraData.nonJitteredProjectionMatrix;
            int width = Mathf.Max(1, cameraData.actualWidth > 0 ? cameraData.actualWidth : camera.pixelWidth);
            int height = Mathf.Max(1, cameraData.actualHeight > 0 ? cameraData.actualHeight : camera.pixelHeight);
            bool isPerspective = !camera.orthographic;
            float radiusX = 1.0f / Mathf.Max(width * Mathf.Abs(projMatrix.m00), 1e-6f);
            float radiusY = 1.0f / Mathf.Max(height * Mathf.Abs(projMatrix.m11), 1e-6f);
            float radiusScreen = Mathf.Min(radiusX, radiusY);
            float orthoRadiusWorld = isPerspective ? 0.0f : radiusScreen;
            m_CameraViewParams = new Vector4(radiusScreen, isPerspective ? 1.0f : 0.0f, orthoRadiusWorld, globalLocalLodBias);
            m_ClusterParams = new Vector4(
                clustered.clusterTileSize,
                clustered.clusterTileCountX,
                clustered.clusterTileCountY,
                clustered.clusterSliceCount);
            m_ClusterDepthParams = new Vector4(
                clustered.clusterNearClip,
                clustered.clusterFarClip,
                clustered.clusterBase,
                clustered.isLogBaseBufferEnabled ? 1.0f : 0.0f);
            m_CameraViewMatrix = cameraData.viewMatrix;

            if (pass != null)
            {
                PassRecorder.ImportBufferForPass(pass, punctualBuffer, AccessFlags.Read);
                if (m_SourceAreaLights != null && m_SourceAreaLights.IsValid())
                    PassRecorder.ImportBufferForPass(pass, m_SourceAreaLights, AccessFlags.Read);
                PassRecorder.ImportBufferForPass(pass, offsetBuffer, AccessFlags.Read);
                PassRecorder.ImportBufferForPass(pass, listBuffer, AccessFlags.Read);
                PassRecorder.ImportBufferForPass(pass, logBaseBuffer, AccessFlags.Read);
                PassRecorder.ImportBufferForPass(pass, m_PunctualLightVSMInfo, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_AreaLightVSMInfo, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_PrunedLightOffsetCount, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_PrunedLightIndices, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_UnifiedProjections, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_LocalPageRequestFlags, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_LocalPageReceiverMasks, AccessFlags.ReadWrite);
            }

            Active = true;
            return true;
        }

        internal void RecordPruneAndMark(
            CommandBuffer cmd,
            ComputeShader shader,
            int clearLocalKernel,
            int markCoarseUnifiedKernel,
            int pruneKernel,
            int markLocalKernel,
            int receiverWidth,
            int receiverHeight,
            Matrix4x4 invViewProjection,
            bool cullBackfacingPixels,
            RTHandle depthTexture,
            RTHandle gbuffer1,
            int directionalProjectionCount,
            int directionalPageTableEntryCount,
            GraphicsBuffer localPagePressure = null,
            bool localPagePressureEnabled = false,
            int frameIndex = 0)
        {
            if (!Active || pruneKernel < 0 || markLocalKernel < 0 || m_SourceRegistry == null)
                return;

            if (m_PunctualLightCount > 0)
                cmd.SetBufferData(m_PunctualLightVSMInfo, m_PunctualVSMInfoUpload, 0, 0, m_PunctualLightCount);
            if (m_AreaLightCount > 0)
                cmd.SetBufferData(m_AreaLightVSMInfo, m_AreaVSMInfoUpload, 0, 0, m_AreaLightCount);
            cmd.SetBufferData(m_UnifiedProjections, m_SourceRegistry.Projections, 0, 0, m_UnifiedProjectionCount);

            if (localPagePressure != null && localPagePressure.IsValid())
            {
                VirtualShadowMapPerformanceThrottle.BeginLocalFrame(
                    cmd,
                    frameIndex,
                    m_SourceRegistry.DirectionalCount,
                    m_UnifiedProjectionCount,
                    localPagePressureEnabled,
                    m_UnifiedProjections,
                    localPagePressure);
            }

            cmd.SetComputeIntParam(shader, PageTableEntryCountId, m_UnifiedPageTableEntryCount);
            cmd.SetComputeIntParam(shader, PageSizeId, VirtualShadowMapPrototypeRuntime.PageSize);
            cmd.SetComputeIntParam(shader, RequestEnabledId, 1);
            cmd.SetComputeIntParam(shader, VirtualShadowMapPrototypeRuntime.ReceiverMaskEnabledId, 1);
            cmd.SetComputeIntParam(shader, VirtualShadowMapProjectionSet.CountId, m_UnifiedProjectionCount);

            if (clearLocalKernel >= 0)
            {
                cmd.SetComputeBufferParam(shader, clearLocalKernel, PageRequestFlagsId, m_LocalPageRequestFlags);
                cmd.SetComputeBufferParam(shader, clearLocalKernel, VirtualShadowMapPrototypeRuntime.PageReceiverMasksId, m_LocalPageReceiverMasks);
                cmd.DispatchCompute(shader, clearLocalKernel, CoreUtils.DivRoundUp(m_UnifiedPageTableEntryCount, 64), 1, 1);
            }

            if (markCoarseUnifiedKernel >= 0)
            {
                cmd.SetComputeIntParam(shader, MarkCoarsePagesLocalId, m_MarkCoarsePagesLocal ? 1 : 0);
                cmd.SetComputeBufferParam(shader, markCoarseUnifiedKernel, VirtualShadowMapProjectionSet.BufferId, m_UnifiedProjections);
                cmd.SetComputeBufferParam(shader, markCoarseUnifiedKernel, PageRequestFlagsId, m_LocalPageRequestFlags);
                cmd.SetComputeBufferParam(shader, markCoarseUnifiedKernel, VirtualShadowMapPrototypeRuntime.PageReceiverMasksId, m_LocalPageReceiverMasks);
                cmd.DispatchCompute(shader, markCoarseUnifiedKernel, CoreUtils.DivRoundUp(m_UnifiedProjectionCount, 64), 1, 1);
            }

            using (new ProfilingScope(cmd, PruneSampler))
            {
                cmd.SetComputeIntParam(shader, ClusterCountId, m_ClusterCount);
                cmd.SetComputeIntParam(shader, PunctualLightCountId, m_PunctualLightCount);
                cmd.SetComputeIntParam(shader, AreaLightCountId, m_AreaLightCount);
                cmd.SetComputeIntParam(shader, PruneAreaCategoryEnabledId, 1);
                cmd.SetComputeBufferParam(shader, pruneKernel, LayeredOffsetId, m_SourceLayeredOffset);
                cmd.SetComputeBufferParam(shader, pruneKernel, LayeredLightListId, m_SourceLayeredLightList);
                cmd.SetComputeBufferParam(shader, pruneKernel, PunctualLightVSMInfoId, m_PunctualLightVSMInfo);
                cmd.SetComputeBufferParam(shader, pruneKernel, AreaLightVSMInfoId, m_AreaLightVSMInfo);
                cmd.SetComputeBufferParam(shader, pruneKernel, PrunedLightOffsetCountRWId, m_PrunedLightOffsetCount);
                cmd.SetComputeBufferParam(shader, pruneKernel, PrunedLightIndicesRWId, m_PrunedLightIndices);
                cmd.DispatchCompute(shader, pruneKernel, CoreUtils.DivRoundUp(m_ClusterCount, 64), 1, 1);
            }

            using (new ProfilingScope(cmd, MarkLocalSampler))
            {
                bool bindPressure = localPagePressureEnabled && localPagePressure != null && localPagePressure.IsValid();
                cmd.SetComputeIntParam(shader, ClusterCountId, m_ClusterCount);
                cmd.SetComputeIntParam(shader, PunctualLightCountId, m_PunctualLightCount);
                cmd.SetComputeIntParam(shader, AreaLightCountId, m_AreaLightCount);
                cmd.SetComputeIntParam(shader, OutputWidthId, receiverWidth);
                cmd.SetComputeIntParam(shader, OutputHeightId, receiverHeight);
                cmd.SetComputeIntParam(shader, VirtualShadowMapReceiverQuality.CullBackfacingPixelsId, cullBackfacingPixels ? 1 : 0);
                cmd.SetComputeIntParam(shader, LocalPagePressureEnabledId, bindPressure ? 1 : 0);
                cmd.SetComputeMatrixParam(shader, InvViewProjMatrixId, invViewProjection);
                cmd.SetComputeVectorParam(shader, CameraViewParamsId, m_CameraViewParams);
                cmd.SetComputeVectorParam(shader, ClusterParamsId, m_ClusterParams);
                cmd.SetComputeVectorParam(shader, ClusterDepthParamsId, m_ClusterDepthParams);
                cmd.SetComputeMatrixParam(shader, CameraViewMatrixId, m_CameraViewMatrix);

                cmd.SetComputeBufferParam(shader, markLocalKernel, VirtualShadowMapProjectionSet.BufferId, m_UnifiedProjections);
                cmd.SetComputeBufferParam(shader, markLocalKernel, PageRequestFlagsId, m_LocalPageRequestFlags);
                cmd.SetComputeBufferParam(shader, markLocalKernel, VirtualShadowMapPrototypeRuntime.PageReceiverMasksId, m_LocalPageReceiverMasks);
                cmd.SetComputeBufferParam(shader, markLocalKernel, PunctualLightsId, m_SourcePunctualLights);
                cmd.SetComputeBufferParam(shader, markLocalKernel, PunctualLightVSMInfoId, m_PunctualLightVSMInfo);
                cmd.SetComputeBufferParam(shader, markLocalKernel, AreaLightsId, SourceAreaLights);
                cmd.SetComputeBufferParam(shader, markLocalKernel, AreaLightVSMInfoId, m_AreaLightVSMInfo);
                cmd.SetComputeBufferParam(shader, markLocalKernel, PrunedLightOffsetCountId, m_PrunedLightOffsetCount);
                cmd.SetComputeBufferParam(shader, markLocalKernel, PrunedLightIndicesId, m_PrunedLightIndices);
                cmd.SetComputeBufferParam(shader, markLocalKernel, LogBaseBufferId, m_SourceLogBaseBuffer);
                if (localPagePressure != null && localPagePressure.IsValid())
                    cmd.SetComputeBufferParam(shader, markLocalKernel, VirtualShadowMapReceiverQuality.PressureId, localPagePressure);
                else if (VirtualShadowMapPrototypeRuntime.PagePressure != null && VirtualShadowMapPrototypeRuntime.PagePressure.IsValid())
                    cmd.SetComputeBufferParam(shader, markLocalKernel, VirtualShadowMapReceiverQuality.PressureId, VirtualShadowMapPrototypeRuntime.PagePressure);
                cmd.SetComputeTextureParam(shader, markLocalKernel, DepthTextureId, depthTexture);
                cmd.SetComputeTextureParam(shader, markLocalKernel, GBuffer1Id, gbuffer1);

                const int stride = 2;
                cmd.DispatchCompute(shader, markLocalKernel,
                    CoreUtils.DivRoundUp(receiverWidth, 8 * stride),
                    CoreUtils.DivRoundUp(receiverHeight, 8 * stride), 1);
            }

            // Restore directional uniform values so Phase 1 never perturbs downstream directional passes.
            cmd.SetComputeIntParam(shader, LocalPagePressureEnabledId, 0);
            cmd.SetComputeIntParam(shader, VirtualShadowMapProjectionSet.CountId, directionalProjectionCount);
            cmd.SetComputeIntParam(shader, PageTableEntryCountId, directionalPageTableEntryCount);
            m_SourceRegistry.CommitRecordedFrame();
        }

        private void EnsureDescriptorCapacity(int required)
        {
            if (m_CollectedDescriptors.Length >= required) return;
            Array.Resize(ref m_CollectedDescriptors, Mathf.NextPowerOfTwo(required));
        }

        private void EnsureCandidateCapacity(int required)
        {
            if (m_CandidateLightIndices.Length >= required) return;
            int next = Mathf.NextPowerOfTwo(required);
            Array.Resize(ref m_CandidateLightIndices, next);
            Array.Resize(ref m_CandidateIsAreaLight, next);
            Array.Resize(ref m_CandidateScreenDiameters, next);
        }

        private void EnsurePunctualUploadCapacity(int required)
        {
            if (m_PunctualVSMInfoUpload.Length >= required) return;
            Array.Resize(ref m_PunctualVSMInfoUpload, Mathf.NextPowerOfTwo(required));
        }

        private void EnsureAreaUploadCapacity(int required)
        {
            if (m_AreaVSMInfoUpload.Length >= required) return;
            Array.Resize(ref m_AreaVSMInfoUpload, Mathf.NextPowerOfTwo(required));
        }

        private void EnsureGpuBuffers(int punctualCapacity, int areaCapacity, int clusterCount, int listCapacity, int projectionCapacity, int pageTableCapacity)
        {
            if (m_PunctualLightVSMInfo == null || !m_PunctualLightVSMInfo.IsValid() || m_PunctualLightVSMInfo.count < punctualCapacity)
            {
                m_PunctualLightVSMInfo?.Dispose();
                m_PunctualLightVSMInfo = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(punctualCapacity), sizeof(int) * 2)
                { name = "VSMPunctualLightVSMInfo" };
            }
            if (m_AreaLightVSMInfo == null || !m_AreaLightVSMInfo.IsValid() || m_AreaLightVSMInfo.count < areaCapacity)
            {
                m_AreaLightVSMInfo?.Dispose();
                m_AreaLightVSMInfo = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(areaCapacity), sizeof(int) * 4)
                { name = "VSMAreaLightVSMInfo" };
            }
            if (m_FallbackAreaLights == null || !m_FallbackAreaLights.IsValid())
            {
                m_FallbackAreaLights?.Dispose();
                m_FallbackAreaLights = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, VividLightData.AreaLightData.Stride)
                { name = "VSMAreaLightsFallback" };
                m_FallbackAreaLights.SetData(new VividLightData.AreaLightData[1]);
            }
            int offsetCountCapacity = clusterCount * 2;
            if (m_PrunedLightOffsetCount == null || !m_PrunedLightOffsetCount.IsValid() || m_PrunedLightOffsetCount.count < offsetCountCapacity)
            {
                m_PrunedLightOffsetCount?.Dispose();
                m_PrunedLightOffsetCount = new GraphicsBuffer(GraphicsBuffer.Target.Structured, offsetCountCapacity, sizeof(uint))
                { name = "VSMPrunedLightOffsetCount" };
            }
            if (m_PrunedLightIndices == null || !m_PrunedLightIndices.IsValid() || m_PrunedLightIndices.count < listCapacity)
            {
                m_PrunedLightIndices?.Dispose();
                m_PrunedLightIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, listCapacity, sizeof(uint))
                { name = "VSMPrunedLightIndices" };
            }
            if (m_UnifiedProjections == null || !m_UnifiedProjections.IsValid() || m_UnifiedProjections.count < projectionCapacity)
            {
                m_UnifiedProjections?.Dispose();
                m_UnifiedProjections = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(projectionCapacity), VirtualShadowMapProjection.Stride)
                { name = "VSMUnifiedProjectionsLocal" };
            }
            if (m_LocalPageRequestFlags == null || !m_LocalPageRequestFlags.IsValid() || m_LocalPageRequestFlags.count < pageTableCapacity)
            {
                m_LocalPageRequestFlags?.Dispose();
                m_LocalPageRequestFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(pageTableCapacity), sizeof(uint))
                { name = "VSMLocalPageRequestFlags" };
            }
            if (m_LocalPageReceiverMasks == null || !m_LocalPageReceiverMasks.IsValid() || m_LocalPageReceiverMasks.count < pageTableCapacity)
            {
                m_LocalPageReceiverMasks?.Dispose();
                m_LocalPageReceiverMasks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(pageTableCapacity), sizeof(uint) * 2)
                { name = "VSMLocalPageReceiverMasks" };
            }
        }

        public void Dispose()
        {
            Disable();
            m_PunctualLightVSMInfo?.Dispose(); m_PunctualLightVSMInfo = null;
            m_AreaLightVSMInfo?.Dispose(); m_AreaLightVSMInfo = null;
            m_FallbackAreaLights?.Dispose(); m_FallbackAreaLights = null;
            m_PrunedLightOffsetCount?.Dispose(); m_PrunedLightOffsetCount = null;
            m_PrunedLightIndices?.Dispose(); m_PrunedLightIndices = null;
            m_UnifiedProjections?.Dispose(); m_UnifiedProjections = null;
            m_LocalPageRequestFlags?.Dispose(); m_LocalPageRequestFlags = null;
            m_LocalPageReceiverMasks?.Dispose(); m_LocalPageReceiverMasks = null;
        }
    }
}

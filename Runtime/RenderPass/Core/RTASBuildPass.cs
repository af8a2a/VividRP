using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class RTASBuildPass : ComputePass, IAsyncComputeSupportedPass
    {
        internal const float DefaultRayBias = 0.001f;
        internal const float DefaultDistantRayBias = 0.001f;
        internal const float DefaultSphereCullingDistance = 1000f;
        internal const float DefaultMinSolidAngle = 4f;

        private const uint DefaultInstanceMask = 0xFFu;
        private const string RenderPipelineShaderTagName = "RenderPipeline";
        private const string VividRenderPipelineShaderTagValue = "VividRenderPipeline";

        private static readonly string[] s_DoubleSidedShaderKeywords = { "_DOUBLESIDED_ON" };
        private static readonly string[] s_AlphaTestShaderKeywords = { "_ALPHATEST_ON" };
        private static readonly string[] s_TransparentShaderKeywords = { "_SURFACE_TYPE_TRANSPARENT" };

        [RenderGraphResource(
            Name = "SceneRTAS",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphAccelerationStructure m_SceneAccelerationStructure;

        private bool m_SupportsRayTracing;
        private VividRayTracingAccelerationStructureStats m_LastStats;

        private readonly struct SceneAccelerationStructureBuildStats
        {
            public SceneAccelerationStructureBuildStats(
                int candidateRendererCount,
                uint instanceCount,
                ulong memoryBytes,
                bool usedShaderTagFallback)
            {
                CandidateRendererCount = candidateRendererCount;
                InstanceCount = instanceCount;
                MemoryBytes = memoryBytes;
                UsedShaderTagFallback = usedShaderTagFallback;
            }

            public int CandidateRendererCount { get; }

            public uint InstanceCount { get; }

            public ulong MemoryBytes { get; }

            public bool UsedShaderTagFallback { get; }
        }

        internal readonly struct ResolvedRayTracingSettings
        {
            public ResolvedRayTracingSettings(
                VividRTASBuildMode buildMode,
                VividRTASCullingMode cullingMode,
                float cullingDistance,
                float minSolidAngle,
                bool extendShadowCulling,
                bool extendCameraCulling,
                float rayBias,
                float distantRayBias,
                LayerMask layerMask,
                RayTracingAccelerationStructure.RayTracingModeMask rayTracingModeMask,
                RayTracingAccelerationStructureBuildFlags buildFlagsStaticGeometries,
                RayTracingAccelerationStructureBuildFlags buildFlagsDynamicGeometries,
                bool enableCompaction)
            {
                BuildMode = buildMode;
                CullingMode = cullingMode;
                CullingDistance = cullingDistance;
                MinSolidAngle = minSolidAngle;
                ExtendShadowCulling = extendShadowCulling;
                ExtendCameraCulling = extendCameraCulling;
                RayBias = rayBias;
                DistantRayBias = distantRayBias;
                LayerMask = layerMask;
                RayTracingModeMask = rayTracingModeMask;
                BuildFlagsStaticGeometries = buildFlagsStaticGeometries;
                BuildFlagsDynamicGeometries = buildFlagsDynamicGeometries;
                EnableCompaction = enableCompaction;
            }

            public VividRTASBuildMode BuildMode { get; }

            public VividRTASCullingMode CullingMode { get; }

            public float CullingDistance { get; }

            public float MinSolidAngle { get; }

            public bool ExtendShadowCulling { get; }

            public bool ExtendCameraCulling { get; }

            public float RayBias { get; }

            public float DistantRayBias { get; }

            public LayerMask LayerMask { get; }

            public RayTracingAccelerationStructure.RayTracingModeMask RayTracingModeMask { get; }

            public RayTracingAccelerationStructureBuildFlags BuildFlagsStaticGeometries { get; }

            public RayTracingAccelerationStructureBuildFlags BuildFlagsDynamicGeometries { get; }

            public bool EnableCompaction { get; }
        }

        public RTASBuildPass()
        {
            m_SceneAccelerationStructure = CreateSceneAccelerationStructure();
        }

        public override void Create()
        {
            m_SupportsRayTracing = SystemInfo.supportsRayTracing;
        }

        public override void Prepare(ContextContainer frameData)
        {
            var resolvedSettings = ResolveSettings(
                VividVolumeManagerUtility.GetRayTracingSettingsVolume(),
                m_SceneAccelerationStructure?.desc);
            var camera = frameData.GetOrCreate<VividCameraData>().camera;

            WriteResolvedSettings(frameData, resolvedSettings);

            if (m_SceneAccelerationStructure == null)
            {
                ReportUnavailableStats(camera, in resolvedSettings, "RTAS resource is not initialized.");
                return;
            }

            var descriptor = m_SceneAccelerationStructure.desc ?? RenderGraphAccelerationStructureDesc.Create("SceneRTAS");
            if (string.IsNullOrEmpty(descriptor.Name))
                descriptor.Name = "SceneRTAS";

            ApplyResolvedSettings(descriptor, in resolvedSettings);
            m_SceneAccelerationStructure.desc = descriptor;

            if (!m_SupportsRayTracing)
            {
                ReportUnavailableStats(camera, in resolvedSettings, "Ray tracing is not supported on the current device.");
                return;
            }

            m_SceneAccelerationStructure.EnsureCreated();

            var nativeAccelerationStructure = (RayTracingAccelerationStructure)m_SceneAccelerationStructure;
            if (nativeAccelerationStructure == null)
            {
                ReportUnavailableStats(camera, in resolvedSettings, "Failed to create the native RTAS.");
                return;
            }

            if (!ShouldBuildForCamera(camera))
            {
                nativeAccelerationStructure.ClearInstances();
                ReportUnavailableStats(camera, in resolvedSettings, "RTAS stats are available for Game and SceneView cameras only.");
                return;
            }

            var buildStats = PopulateSceneAccelerationStructure(nativeAccelerationStructure, camera, in resolvedSettings);
            m_LastStats = CreateStats(camera, in resolvedSettings, buildStats, null);
            VividRayTracingAccelerationStructureStatsRegistry.Report(m_LastStats);
        }

        public override void Record(ComputeGraphContext context)
        {
            if (!m_SupportsRayTracing || m_SceneAccelerationStructure == null)
                return;

            context.cmd.BuildRayTracingAccelerationStructure(m_SceneAccelerationStructure);

            var nativeAccelerationStructure = (RayTracingAccelerationStructure)m_SceneAccelerationStructure;
            if (nativeAccelerationStructure == null || !m_LastStats.IsAvailable)
                return;

            m_LastStats = new VividRayTracingAccelerationStructureStats(
                true,
                null,
                m_LastStats.CameraName,
                m_LastStats.CameraType,
                Time.frameCount,
                Time.realtimeSinceStartupAsDouble,
                m_LastStats.BuildMode,
                m_LastStats.CullingMode,
                m_LastStats.CandidateRendererCount,
                nativeAccelerationStructure.GetInstanceCount(),
                nativeAccelerationStructure.GetSize(),
                m_LastStats.UsedShaderTagFallback);
            VividRayTracingAccelerationStructureStatsRegistry.Report(m_LastStats);
        }

        public override void Dispose()
        {
            m_SceneAccelerationStructure?.Dispose();
            m_LastStats = default;
            VividRayTracingAccelerationStructureStatsRegistry.Clear();
        }

        internal static ResolvedRayTracingSettings ResolveSettings(
            RayTracingSettingsVolume volume,
            RenderGraphAccelerationStructureDesc descriptor = null)
        {
            var layerMask = descriptor != null ? descriptor.LayerMask : (LayerMask)(~0);
            var rayTracingModeMask = descriptor != null
                ? descriptor.RayTracingModeMask
                : RayTracingAccelerationStructure.RayTracingModeMask.Everything;
            var buildFlagsStaticGeometries = descriptor != null
                ? descriptor.BuildFlagsStaticGeometries
                : RayTracingAccelerationStructureBuildFlags.None;
            var buildFlagsDynamicGeometries = descriptor != null
                ? descriptor.BuildFlagsDynamicGeometries
                : RayTracingAccelerationStructureBuildFlags.None;
            var enableCompaction = descriptor != null && descriptor.EnableCompaction;
            var buildMode = VividRTASBuildMode.Automatic;
            var cullingMode = VividRTASCullingMode.ExtendedFrustum;
            var cullingDistance = DefaultSphereCullingDistance;
            var minSolidAngle = DefaultMinSolidAngle;
            var extendShadowCulling = false;
            var extendCameraCulling = false;
            var rayBias = DefaultRayBias;
            var distantRayBias = DefaultDistantRayBias;

            if (volume != null && volume.active)
            {
                if (volume.buildMode != null && volume.buildMode.overrideState)
                    buildMode = volume.buildMode.value;

                if (volume.cullingMode != null && volume.cullingMode.overrideState)
                    cullingMode = volume.cullingMode.value;

                if (volume.cullingDistance != null && volume.cullingDistance.overrideState)
                    cullingDistance = volume.cullingDistance.value;

                if (volume.minSolidAngle != null && volume.minSolidAngle.overrideState)
                    minSolidAngle = volume.minSolidAngle.value;

                if (volume.extendShadowCulling != null && volume.extendShadowCulling.overrideState)
                    extendShadowCulling = volume.extendShadowCulling.value;

                if (volume.extendCameraCulling != null && volume.extendCameraCulling.overrideState)
                    extendCameraCulling = volume.extendCameraCulling.value;

                if (volume.rayBias != null && volume.rayBias.overrideState)
                    rayBias = volume.rayBias.value;

                if (volume.distantRayBias != null && volume.distantRayBias.overrideState)
                    distantRayBias = volume.distantRayBias.value;

                if (volume.layerMask != null && volume.layerMask.overrideState)
                    layerMask = volume.layerMask.value;

                if (volume.rayTracingModeMask != null && volume.rayTracingModeMask.overrideState)
                    rayTracingModeMask = volume.rayTracingModeMask.value;

                if (volume.buildFlagsStaticGeometries != null && volume.buildFlagsStaticGeometries.overrideState)
                    buildFlagsStaticGeometries = volume.buildFlagsStaticGeometries.value;

                if (volume.buildFlagsDynamicGeometries != null && volume.buildFlagsDynamicGeometries.overrideState)
                    buildFlagsDynamicGeometries = volume.buildFlagsDynamicGeometries.value;

                if (volume.enableCompaction != null && volume.enableCompaction.overrideState)
                    enableCompaction = volume.enableCompaction.value;
            }

            return new ResolvedRayTracingSettings(
                buildMode,
                cullingMode,
                cullingDistance,
                minSolidAngle,
                extendShadowCulling,
                extendCameraCulling,
                rayBias,
                distantRayBias,
                layerMask,
                rayTracingModeMask,
                buildFlagsStaticGeometries,
                buildFlagsDynamicGeometries,
                enableCompaction);
        }

        internal static void ApplyResolvedSettings(
            RenderGraphAccelerationStructureDesc descriptor,
            in ResolvedRayTracingSettings settings)
        {
            if (descriptor == null)
                return;

            descriptor.ManagementMode = RayTracingAccelerationStructure.ManagementMode.Manual;
            descriptor.LayerMask = settings.LayerMask;
            descriptor.RayTracingModeMask = settings.RayTracingModeMask;
            descriptor.BuildFlagsStaticGeometries = settings.BuildFlagsStaticGeometries;
            descriptor.BuildFlagsDynamicGeometries = settings.BuildFlagsDynamicGeometries;
            descriptor.EnableCompaction = settings.EnableCompaction;
        }

        internal static RayTracingInstanceCullingConfig CreateCullingConfig(
            Camera camera,
            in ResolvedRayTracingSettings settings,
            bool useRenderPipelineTagFilter = true)
        {
            var cullingConfig = new RayTracingInstanceCullingConfig
            {
                flags = RayTracingInstanceCullingFlags.EnableLODCulling
                    | RayTracingInstanceCullingFlags.IgnoreReflectionProbes
                    | RayTracingInstanceCullingFlags.EnableMeshLOD,
                instanceTests = new[] { CreateInstanceCullingTest(settings.LayerMask) }
            };

            cullingConfig.lodParameters.fieldOfView = camera != null ? camera.fieldOfView : 60f;
            cullingConfig.lodParameters.cameraPosition = camera != null ? camera.transform.position : Vector3.zero;
            cullingConfig.lodParameters.cameraPixelHeight = camera != null ? Mathf.Max(1, camera.pixelHeight) : 1;
            cullingConfig.lodParameters.orthoSize = camera != null ? camera.orthographicSize : 0f;
            cullingConfig.lodParameters.isOrthographic = camera != null && camera.orthographic;

            cullingConfig.subMeshFlagsConfig.opaqueMaterials =
                RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;
            cullingConfig.subMeshFlagsConfig.alphaTestedMaterials = RayTracingSubMeshFlags.Enabled;
            cullingConfig.subMeshFlagsConfig.transparentMaterials =
                RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.UniqueAnyHitCalls;

            cullingConfig.triangleCullingConfig.checkDoubleSidedGIMaterial = true;
            cullingConfig.triangleCullingConfig.frontTriangleCounterClockwise = false;
            cullingConfig.triangleCullingConfig.optionalDoubleSidedShaderKeywords = s_DoubleSidedShaderKeywords;

            cullingConfig.alphaTestedMaterialConfig.optionalShaderKeywords = s_AlphaTestShaderKeywords;
            cullingConfig.transparentMaterialConfig.optionalShaderKeywords = s_TransparentShaderKeywords;

            if (useRenderPipelineTagFilter)
            {
                cullingConfig.materialTest.requiredShaderTags = new[]
                {
                    new RayTracingInstanceCullingShaderTagConfig
                    {
                        tagId = new ShaderTagId(RenderPipelineShaderTagName),
                        tagValueId = new ShaderTagId(VividRenderPipelineShaderTagValue),
                    }
                };
            }

            if (camera == null)
                return cullingConfig;

            switch (settings.CullingMode)
            {
                case VividRTASCullingMode.Sphere:
                    cullingConfig.flags |= RayTracingInstanceCullingFlags.EnableSphereCulling;
                    cullingConfig.sphereCenter = camera.transform.position;
                    cullingConfig.sphereRadius = Mathf.Max(0f, settings.CullingDistance);
                    break;
                case VividRTASCullingMode.SolidAngle:
                    cullingConfig.flags |= RayTracingInstanceCullingFlags.EnableSolidAngleCulling;
                    cullingConfig.minSolidAngle = Mathf.Max(0.01f, settings.MinSolidAngle);
                    break;
                default:
                    cullingConfig.flags |= RayTracingInstanceCullingFlags.EnablePlaneCulling;
                    cullingConfig.planes = BuildExtendedFrustumPlanes(camera);
                    break;
            }

            return cullingConfig;
        }

        private static RenderGraphAccelerationStructure CreateSceneAccelerationStructure()
        {
            return new RenderGraphAccelerationStructure
            {
                desc = new RenderGraphAccelerationStructureDesc
                {
                    Name = "SceneRTAS",
                    ManagementMode = RayTracingAccelerationStructure.ManagementMode.Manual,
                    RayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                    LayerMask = ~0,
                }
            };
        }

        private static void WriteResolvedSettings(ContextContainer frameData, in ResolvedRayTracingSettings settings)
        {
            var rayTracingData = frameData.GetOrCreate<VividRayTracingSettingsData>();
            rayTracingData.supportsRayTracing = SystemInfo.supportsRayTracing;
            rayTracingData.buildMode = settings.BuildMode;
            rayTracingData.cullingMode = settings.CullingMode;
            rayTracingData.cullingDistance = settings.CullingDistance;
            rayTracingData.minSolidAngle = settings.MinSolidAngle;
            rayTracingData.extendShadowCulling = settings.ExtendShadowCulling;
            rayTracingData.extendCameraCulling = settings.ExtendCameraCulling;
            rayTracingData.rayBias = settings.RayBias;
            rayTracingData.distantRayBias = settings.DistantRayBias;
            rayTracingData.layerMask = settings.LayerMask;
            rayTracingData.rayTracingModeMask = settings.RayTracingModeMask;
            rayTracingData.buildFlagsStaticGeometries = settings.BuildFlagsStaticGeometries;
            rayTracingData.buildFlagsDynamicGeometries = settings.BuildFlagsDynamicGeometries;
            rayTracingData.enableCompaction = settings.EnableCompaction;
        }

        private static bool ShouldBuildForCamera(Camera camera)
        {
            return camera != null
                && (camera.cameraType == CameraType.Game || camera.cameraType == CameraType.SceneView);
        }

        private void ReportUnavailableStats(
            Camera camera,
            in ResolvedRayTracingSettings settings,
            string statusMessage)
        {
            m_LastStats = new VividRayTracingAccelerationStructureStats(
                false,
                statusMessage,
                camera != null ? camera.name : null,
                camera != null ? camera.cameraType : default,
                Time.frameCount,
                Time.realtimeSinceStartupAsDouble,
                settings.BuildMode,
                settings.CullingMode,
                0,
                0,
                0,
                false);
            VividRayTracingAccelerationStructureStatsRegistry.Report(m_LastStats);
        }

        private static VividRayTracingAccelerationStructureStats CreateStats(
            Camera camera,
            in ResolvedRayTracingSettings settings,
            in SceneAccelerationStructureBuildStats buildStats,
            string statusMessage)
        {
            return new VividRayTracingAccelerationStructureStats(
                true,
                statusMessage,
                camera != null ? camera.name : null,
                camera != null ? camera.cameraType : default,
                Time.frameCount,
                Time.realtimeSinceStartupAsDouble,
                settings.BuildMode,
                settings.CullingMode,
                buildStats.CandidateRendererCount,
                buildStats.InstanceCount,
                buildStats.MemoryBytes,
                buildStats.UsedShaderTagFallback);
        }

        private static SceneAccelerationStructureBuildStats PopulateSceneAccelerationStructure(
            RayTracingAccelerationStructure accelerationStructure,
            Camera camera,
            in ResolvedRayTracingSettings settings)
        {
            var candidateRendererCount = EstimateCandidateRendererCount(settings.LayerMask, settings.RayTracingModeMask, true);
            var usedShaderTagFallback = false;

            accelerationStructure.ClearInstances();

            var cullingConfig = CreateCullingConfig(camera, in settings);
            accelerationStructure.CullInstances(ref cullingConfig);

            var instanceCount = accelerationStructure.GetInstanceCount();
            if (instanceCount == 0)
            {
                usedShaderTagFallback = true;
                candidateRendererCount = EstimateCandidateRendererCount(settings.LayerMask, settings.RayTracingModeMask, false);
                accelerationStructure.ClearInstances();
                cullingConfig = CreateCullingConfig(camera, in settings, useRenderPipelineTagFilter: false);
                accelerationStructure.CullInstances(ref cullingConfig);
                instanceCount = accelerationStructure.GetInstanceCount();
            }

            return new SceneAccelerationStructureBuildStats(
                candidateRendererCount,
                instanceCount,
                accelerationStructure.GetSize(),
                usedShaderTagFallback);
        }

        private static RayTracingInstanceCullingTest CreateInstanceCullingTest(LayerMask layerMask)
        {
            return new RayTracingInstanceCullingTest
            {
                allowOpaqueMaterials = true,
                allowAlphaTestedMaterials = true,
                allowTransparentMaterials = true,
                layerMask = layerMask,
                shadowCastingModeMask = (1 << (int)ShadowCastingMode.Off)
                    | (1 << (int)ShadowCastingMode.On)
                    | (1 << (int)ShadowCastingMode.TwoSided)
                    | (1 << (int)ShadowCastingMode.ShadowsOnly),
                instanceMask = DefaultInstanceMask,
            };
        }

        private static Plane[] BuildExtendedFrustumPlanes(Camera camera)
        {
            if (camera == null)
                return null;

            if (camera.orthographic)
                return GeometryUtility.CalculateFrustumPlanes(camera);

            var cameraTransform = camera.transform;
            var cameraPosition = cameraTransform.position;
            var forward = cameraTransform.forward;
            var right = cameraTransform.right;
            var up = cameraTransform.up;

            var far = Mathf.Max(camera.nearClipPlane, camera.farClipPlane);
            var halfHeight = Mathf.Tan(Mathf.Deg2Rad * camera.fieldOfView * 0.5f) * far;
            var horizontalFov = Camera.VerticalToHorizontalFieldOfView(camera.fieldOfView, camera.aspect);
            var halfWidth = Mathf.Tan(Mathf.Deg2Rad * horizontalFov * 0.5f) * far;

            var planes = new Plane[6];

            planes[0].normal = -forward;
            planes[0].distance = -Vector3.Dot(cameraPosition + forward * far, planes[0].normal);

            planes[1].normal = forward;
            planes[1].distance = -Vector3.Dot(cameraPosition - forward * far, planes[1].normal);

            planes[2].normal = -right;
            planes[2].distance = -Vector3.Dot(cameraPosition + right * halfWidth, planes[2].normal);

            planes[3].normal = right;
            planes[3].distance = -Vector3.Dot(cameraPosition - right * halfWidth, planes[3].normal);

            planes[4].normal = -up;
            planes[4].distance = -Vector3.Dot(cameraPosition + up * halfHeight, planes[4].normal);

            planes[5].normal = up;
            planes[5].distance = -Vector3.Dot(cameraPosition - up * halfHeight, planes[5].normal);

            return planes;
        }

        private static int EstimateCandidateRendererCount(
            LayerMask layerMask,
            RayTracingAccelerationStructure.RayTracingModeMask rayTracingModeMask,
            bool requireVividRenderPipelineTag)
        {
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
            var count = 0;

            for (var i = 0; i < renderers.Length; i++)
            {
                if (IsCandidateRenderer(renderers[i], layerMask, rayTracingModeMask, requireVividRenderPipelineTag))
                    count++;
            }

            return count;
        }

        private static bool IsCandidateRenderer(
            Renderer renderer,
            LayerMask layerMask,
            RayTracingAccelerationStructure.RayTracingModeMask rayTracingModeMask,
            bool requireVividRenderPipelineTag)
        {
            if (renderer == null
                || !renderer.enabled
                || renderer.gameObject == null
                || !renderer.gameObject.activeInHierarchy
                || !renderer.gameObject.scene.IsValid()
                || !renderer.gameObject.scene.isLoaded
                || !SupportsRayTracingRendererType(renderer)
                || !IsLayerIncluded(renderer.gameObject.layer, layerMask)
                || !MatchesRayTracingModeMask(renderer.rayTracingMode, rayTracingModeMask))
            {
                return false;
            }

            return !requireVividRenderPipelineTag || HasVividRenderPipelineMaterial(renderer);
        }

        private static bool SupportsRayTracingRendererType(Renderer renderer)
        {
            return renderer is SkinnedMeshRenderer
                || (renderer is MeshRenderer && renderer.GetComponent<MeshFilter>() != null);
        }

        private static bool IsLayerIncluded(int layer, LayerMask layerMask)
        {
            return (layerMask.value & (1 << layer)) != 0;
        }

        private static bool MatchesRayTracingModeMask(
            RayTracingMode rayTracingMode,
            RayTracingAccelerationStructure.RayTracingModeMask rayTracingModeMask)
        {
            if ((rayTracingModeMask & RayTracingAccelerationStructure.RayTracingModeMask.Everything)
                == RayTracingAccelerationStructure.RayTracingModeMask.Everything)
            {
                return rayTracingMode != RayTracingMode.Off;
            }

            return rayTracingMode switch
            {
                RayTracingMode.Static => (rayTracingModeMask & RayTracingAccelerationStructure.RayTracingModeMask.Static) != 0,
                RayTracingMode.DynamicTransform => (rayTracingModeMask & RayTracingAccelerationStructure.RayTracingModeMask.DynamicTransform) != 0,
                RayTracingMode.DynamicGeometry => (rayTracingModeMask & RayTracingAccelerationStructure.RayTracingModeMask.DynamicGeometry) != 0,
                RayTracingMode.DynamicGeometryManualUpdate => (rayTracingModeMask & RayTracingAccelerationStructure.RayTracingModeMask.DynamicGeometryManualUpdate) != 0,
                _ => false,
            };
        }

        private static bool HasVividRenderPipelineMaterial(Renderer renderer)
        {
            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
                return false;

            for (var i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null)
                    continue;

                if (material.GetTag(RenderPipelineShaderTagName, false) == VividRenderPipelineShaderTagValue)
                    return true;
            }

            return false;
        }
    }
}

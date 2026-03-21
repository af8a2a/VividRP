using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class RTASBuildPass : ComputePass, IAsyncComputeSupportedPass
    {
        internal const float DefaultRayBias = 0.001f;
        internal const float DefaultDistantRayBias = 0.001f;
        internal const float DefaultSphereCullingDistance = 1000f;

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

        internal readonly struct ResolvedRayTracingSettings
        {
            public ResolvedRayTracingSettings(
                VividRTASBuildMode buildMode,
                VividRTASCullingMode cullingMode,
                float cullingDistance,
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

            WriteResolvedSettings(frameData, resolvedSettings);

            if (m_SceneAccelerationStructure == null)
                return;

            var descriptor = m_SceneAccelerationStructure.desc ?? RenderGraphAccelerationStructureDesc.Create("SceneRTAS");
            if (string.IsNullOrEmpty(descriptor.Name))
                descriptor.Name = "SceneRTAS";

            ApplyResolvedSettings(descriptor, in resolvedSettings);
            m_SceneAccelerationStructure.desc = descriptor;

            if (!m_SupportsRayTracing)
                return;

            m_SceneAccelerationStructure.EnsureCreated();

            var nativeAccelerationStructure = (RayTracingAccelerationStructure)m_SceneAccelerationStructure;
            if (nativeAccelerationStructure == null)
                return;

            var camera = frameData.GetOrCreate<VividCameraData>().camera;
            if (!ShouldBuildForCamera(camera))
            {
                nativeAccelerationStructure.ClearInstances();
                return;
            }

            PopulateSceneAccelerationStructure(nativeAccelerationStructure, camera, in resolvedSettings);
        }

        public override void Record(ComputeGraphContext context)
        {
            if (!m_SupportsRayTracing || m_SceneAccelerationStructure == null)
                return;

            context.cmd.BuildRayTracingAccelerationStructure(m_SceneAccelerationStructure);
        }

        public override void Dispose()
        {
            m_SceneAccelerationStructure?.Dispose();
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

        private static void PopulateSceneAccelerationStructure(
            RayTracingAccelerationStructure accelerationStructure,
            Camera camera,
            in ResolvedRayTracingSettings settings)
        {
            accelerationStructure.ClearInstances();

            var cullingConfig = CreateCullingConfig(camera, in settings);
            accelerationStructure.CullInstances(ref cullingConfig);

            if (accelerationStructure.GetInstanceCount() > 0)
                return;

            accelerationStructure.ClearInstances();
            cullingConfig = CreateCullingConfig(camera, in settings, useRenderPipelineTagFilter: false);
            accelerationStructure.CullInstances(ref cullingConfig);
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
    }
}

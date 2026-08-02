using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// Traces a stable primary visibility ray and emits the material/geometry guides shared by
    /// NRD REBLUR and DLSS Ray Reconstruction. Material closest-hit shaders can provide a
    /// previous world-space surface position for deforming geometry; materials without one
    /// retain the camera-motion-only fallback.
    /// </summary>
    public sealed class RaytracingGBufferPass : UnsafePass
    {
        internal const string MaterialShaderPassName = "RaytracingGBufferDXR";
        internal const string RayGenerationShaderName = "RayGenRaytracingGBuffer";

        private const string AccelerationStructureName = "_AccelerationStructure";

        private static readonly int ViewZId = Shader.PropertyToID("_RaytracingGBufferViewZ");
        private static readonly int MotionVectorsId = Shader.PropertyToID("_RaytracingGBufferMotionVectors");
        private static readonly int DlssMotionVectorsId =
            Shader.PropertyToID("_RaytracingGBufferDlssMotionVectors");
        private static readonly int NrdNormalRoughnessId =
            Shader.PropertyToID("_RaytracingGBufferNrdNormalRoughness");
        private static readonly int BaseColorMetalnessId =
            Shader.PropertyToID("_RaytracingGBufferBaseColorMetalness");
        private static readonly int DlssNormalRoughnessId =
            Shader.PropertyToID("_RaytracingGBufferDlssNormalRoughness");
        private static readonly int DiffuseAlbedoId = Shader.PropertyToID("_RaytracingGBufferDiffuseAlbedo");
        private static readonly int SpecularAlbedoId = Shader.PropertyToID("_RaytracingGBufferSpecularAlbedo");
        private static readonly int NrdDiffuseMaterialFactorId =
            Shader.PropertyToID("_RaytracingGBufferNrdDiffuseMaterialFactor");
        private static readonly int NrdSpecularMaterialFactorId =
            Shader.PropertyToID("_RaytracingGBufferNrdSpecularMaterialFactor");
        private static readonly int DlssDepthId = Shader.PropertyToID("_RaytracingGBufferDlssDepth");
        private static readonly int CameraPositionWSId =
            Shader.PropertyToID("_RaytracingGBufferCameraPositionWS");
        private static readonly int PixelCoordToViewDirWSId =
            Shader.PropertyToID("_RaytracingGBufferPixelCoordToViewDirWS");
        private static readonly int WorldToViewId = Shader.PropertyToID("_RaytracingGBufferWorldToView");
        private static readonly int WorldToClipId = Shader.PropertyToID("_RaytracingGBufferWorldToClip");
        private static readonly int WorldToViewPreviousId =
            Shader.PropertyToID("_RaytracingGBufferWorldToViewPrevious");
        private static readonly int WorldToClipPreviousId =
            Shader.PropertyToID("_RaytracingGBufferWorldToClipPrevious");
        private static readonly int ScreenSizeId = Shader.PropertyToID("_RaytracingGBufferScreenSize");
        private static readonly int RayMinDistanceId =
            Shader.PropertyToID("_RaytracingGBufferRayMinDistance");
        private static readonly int RayMaxDistanceId =
            Shader.PropertyToID("_RaytracingGBufferRayMaxDistance");

        [RenderGraphResource(Name = "SceneRTAS", Access = AccessFlags.Read)]
        private RenderGraphAccelerationStructure m_SceneAccelerationStructure;

        [RenderGraphResource(
            Name = "NrdViewZ",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ViewZ;

        [RenderGraphResource(
            Name = "MotionVectors",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_MotionVectors;

        [RenderGraphResource(
            Name = "DlssMotionVectors",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_DlssMotionVectors;

        [RenderGraphResource(
            Name = "NrdNormalRoughness",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_NrdNormalRoughness;

        [RenderGraphResource(
            Name = "BaseColorMetalness",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_BaseColorMetalness;

        [RenderGraphResource(
            Name = "DlssNormalRoughness",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_DlssNormalRoughness;

        [RenderGraphResource(
            Name = "DiffuseAlbedo",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_DiffuseAlbedo;

        [RenderGraphResource(
            Name = "SpecularAlbedo",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_SpecularAlbedo;

        [RenderGraphResource(
            Name = "NrdDiffuseMaterialFactor",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_NrdDiffuseMaterialFactor;

        [RenderGraphResource(
            Name = "NrdSpecularMaterialFactor",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_NrdSpecularMaterialFactor;

        [RenderGraphResource(
            Name = "DlssDepth",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_DlssDepth;

        private RayTracingShader m_RayTracingShader;
        private bool m_SupportsRayTracing;
        private bool m_ShouldSkipExecution;
        private int m_Width = 1;
        private int m_Height = 1;
        private Vector4 m_CameraPositionWS;
        private Matrix4x4 m_PixelCoordToViewDirWS = Matrix4x4.identity;
        private Matrix4x4 m_WorldToView = Matrix4x4.identity;
        private Matrix4x4 m_WorldToClip = Matrix4x4.identity;
        private Matrix4x4 m_WorldToViewPrevious = Matrix4x4.identity;
        private Matrix4x4 m_WorldToClipPrevious = Matrix4x4.identity;
        private float m_RayMinDistance = 0.01f;
        private float m_RayMaxDistance = 1000.0f;

        public RaytracingGBufferPass()
        {
            profilingSampler = new ProfilingSampler(nameof(RaytracingGBufferPass));
            m_SceneAccelerationStructure = new RenderGraphAccelerationStructure
            {
                desc = RenderGraphAccelerationStructureDesc.Create("SceneRTAS")
            };
            m_ViewZ = CreateOutput("NrdViewZ", GraphicsFormat.R32_SFloat);
            m_MotionVectors = CreateOutput("MotionVectors", GraphicsFormat.R16G16B16A16_SFloat);
            m_DlssMotionVectors = CreateOutput("DlssMotionVectors", GraphicsFormat.R16G16_SFloat);
            m_NrdNormalRoughness = CreateOutput(
                "NrdNormalRoughness",
                GraphicsFormat.A2B10G10R10_UNormPack32);
            m_BaseColorMetalness = CreateOutput("BaseColorMetalness", GraphicsFormat.R8G8B8A8_UNorm);
            m_DlssNormalRoughness = CreateOutput(
                "DlssNormalRoughness",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_DiffuseAlbedo = CreateOutput("DiffuseAlbedo", GraphicsFormat.A2B10G10R10_UNormPack32);
            m_SpecularAlbedo = CreateOutput("SpecularAlbedo", GraphicsFormat.A2B10G10R10_UNormPack32);
            m_NrdDiffuseMaterialFactor = CreateOutput(
                "NrdDiffuseMaterialFactor",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_NrdSpecularMaterialFactor = CreateOutput(
                "NrdSpecularMaterialFactor",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_DlssDepth = CreateOutput("DlssDepth", GraphicsFormat.R32_SFloat);
        }

        public override void Create()
        {
            m_SupportsRayTracing = SystemInfo.supportsRayTracing;
            m_RayTracingShader = PipelineResourceManager
                .Get<VividRPCoreResources>()
                ?.RaytracingGBufferRayTracing;

            if (m_RayTracingShader == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find the ray-tracing shader resource for {nameof(RaytracingGBufferPass)}.");
            }
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var temporalData = frameData.GetOrCreate<VividTemporalData>();
            m_Width = CameraDimensionUtility.ResolveCameraDimension(
                cameraData?.actualWidth ?? 0,
                cameraData?.pixelWidth ?? 0,
                Screen.width);
            m_Height = CameraDimensionUtility.ResolveCameraDimension(
                cameraData?.actualHeight ?? 0,
                cameraData?.pixelHeight ?? 0,
                Screen.height);
            ResizeOutputs(m_Width, m_Height);

            var camera = cameraData?.camera;
            m_ShouldSkipExecution = camera == null || camera.orthographic;
            if (m_ShouldSkipExecution)
            {
                ResetCameraParameters();
                return;
            }

            var cameraPosition = camera.transform.position;
            m_CameraPositionWS = new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 1.0f);
            m_PixelCoordToViewDirWS = cameraData.GetPixelCoordToViewDirWSMatrix();
            m_WorldToView = cameraData.GetViewMatrix();
            var viewToClip = cameraData.GetGPUProjectionMatrixNoJitter(renderIntoTexture: true);
            m_WorldToClip = viewToClip * m_WorldToView;

            bool hasPrevious = temporalData != null && !temporalData.isFirstFrame;
            m_WorldToViewPrevious = hasPrevious
                ? temporalData.previousViewMatrix
                : m_WorldToView;
            var viewToClipPrevious = hasPrevious
                ? temporalData.previousProjectionMatrix
                : viewToClip;
            m_WorldToClipPrevious = viewToClipPrevious * m_WorldToViewPrevious;
            m_RayMinDistance = Mathf.Max(camera.nearClipPlane, 0.0001f);
            m_RayMaxDistance = Mathf.Max(camera.farClipPlane, m_RayMinDistance + 0.0001f);
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_ShouldSkipExecution
                || !m_SupportsRayTracing
                || m_RayTracingShader == null
                || m_SceneAccelerationStructure == null
                || !HaveValidOutputs())
            {
                return;
            }

            var accelerationStructure = (RayTracingAccelerationStructure)m_SceneAccelerationStructure;
            if (accelerationStructure == null)
                return;

            var cmd = context.GetNativeCommandBuffer();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                cmd.SetRayTracingShaderPass(m_RayTracingShader, MaterialShaderPassName);
                cmd.SetRayTracingAccelerationStructure(
                    m_RayTracingShader,
                    AccelerationStructureName,
                    accelerationStructure);
                BindOutput(cmd, ViewZId, m_ViewZ);
                BindOutput(cmd, MotionVectorsId, m_MotionVectors);
                BindOutput(cmd, DlssMotionVectorsId, m_DlssMotionVectors);
                BindOutput(cmd, NrdNormalRoughnessId, m_NrdNormalRoughness);
                BindOutput(cmd, BaseColorMetalnessId, m_BaseColorMetalness);
                BindOutput(cmd, DlssNormalRoughnessId, m_DlssNormalRoughness);
                BindOutput(cmd, DiffuseAlbedoId, m_DiffuseAlbedo);
                BindOutput(cmd, SpecularAlbedoId, m_SpecularAlbedo);
                BindOutput(cmd, NrdDiffuseMaterialFactorId, m_NrdDiffuseMaterialFactor);
                BindOutput(cmd, NrdSpecularMaterialFactorId, m_NrdSpecularMaterialFactor);
                BindOutput(cmd, DlssDepthId, m_DlssDepth);

                cmd.SetRayTracingVectorParam(m_RayTracingShader, CameraPositionWSId, m_CameraPositionWS);
                cmd.SetRayTracingMatrixParam(
                    m_RayTracingShader,
                    PixelCoordToViewDirWSId,
                    m_PixelCoordToViewDirWS);
                cmd.SetRayTracingMatrixParam(m_RayTracingShader, WorldToViewId, m_WorldToView);
                cmd.SetRayTracingMatrixParam(m_RayTracingShader, WorldToClipId, m_WorldToClip);
                cmd.SetRayTracingMatrixParam(
                    m_RayTracingShader,
                    WorldToViewPreviousId,
                    m_WorldToViewPrevious);
                cmd.SetRayTracingMatrixParam(
                    m_RayTracingShader,
                    WorldToClipPreviousId,
                    m_WorldToClipPrevious);
                cmd.SetRayTracingVectorParam(
                    m_RayTracingShader,
                    ScreenSizeId,
                    new Vector4(m_Width, m_Height, 1.0f / m_Width, 1.0f / m_Height));
                cmd.SetRayTracingFloatParam(m_RayTracingShader, RayMinDistanceId, m_RayMinDistance);
                cmd.SetRayTracingFloatParam(m_RayTracingShader, RayMaxDistanceId, m_RayMaxDistance);
                cmd.DispatchRays(
                    m_RayTracingShader,
                    RayGenerationShaderName,
                    (uint)m_Width,
                    (uint)m_Height,
                    1,
                    null);
            }
        }

        public override void Dispose()
        {
            m_RayTracingShader = null;
            m_SupportsRayTracing = false;
            m_ShouldSkipExecution = false;
            m_Width = 1;
            m_Height = 1;
            ResetCameraParameters();
        }

        private static RenderGraphTexture CreateOutput(string name, GraphicsFormat format)
        {
            var texture = RenderGraphTexture.CreateOutput(name, format);
            ConfigureTexture(texture, 1, 1, format, name);
            return texture;
        }

        private void ResizeOutputs(int width, int height)
        {
            ConfigureTexture(m_ViewZ, width, height, GraphicsFormat.R32_SFloat, "NrdViewZ");
            ConfigureTexture(
                m_MotionVectors,
                width,
                height,
                GraphicsFormat.R16G16B16A16_SFloat,
                "MotionVectors");
            ConfigureTexture(
                m_DlssMotionVectors,
                width,
                height,
                GraphicsFormat.R16G16_SFloat,
                "DlssMotionVectors");
            ConfigureTexture(
                m_NrdNormalRoughness,
                width,
                height,
                GraphicsFormat.A2B10G10R10_UNormPack32,
                "NrdNormalRoughness");
            ConfigureTexture(
                m_BaseColorMetalness,
                width,
                height,
                GraphicsFormat.R8G8B8A8_UNorm,
                "BaseColorMetalness");
            ConfigureTexture(
                m_DlssNormalRoughness,
                width,
                height,
                GraphicsFormat.R16G16B16A16_SFloat,
                "DlssNormalRoughness");
            ConfigureTexture(
                m_DiffuseAlbedo,
                width,
                height,
                GraphicsFormat.A2B10G10R10_UNormPack32,
                "DiffuseAlbedo");
            ConfigureTexture(
                m_SpecularAlbedo,
                width,
                height,
                GraphicsFormat.A2B10G10R10_UNormPack32,
                "SpecularAlbedo");
            ConfigureTexture(
                m_NrdDiffuseMaterialFactor,
                width,
                height,
                GraphicsFormat.R16G16B16A16_SFloat,
                "NrdDiffuseMaterialFactor");
            ConfigureTexture(
                m_NrdSpecularMaterialFactor,
                width,
                height,
                GraphicsFormat.R16G16B16A16_SFloat,
                "NrdSpecularMaterialFactor");
            ConfigureTexture(m_DlssDepth, width, height, GraphicsFormat.R32_SFloat, "DlssDepth");
        }

        private static void ConfigureTexture(
            RenderGraphTexture texture,
            int width,
            int height,
            GraphicsFormat format,
            string name)
        {
            if (texture?.desc == null)
                return;

            texture.Resize(width, height);
            texture.desc.ColorFormat = format;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            // Every ray-generation invocation writes every guide. Avoid RTV clears for packed/UAV-only
            // guide formats such as R10G10B10A2; some D3D12 drivers expose them only as typed UAVs.
            texture.desc.ClearBuffer = false;
            texture.desc.ClearColor = Color.clear;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.EnableRandomWrite = true;
            texture.desc.BindTextureMS = false;
            texture.desc.Name = name;
        }

        private bool HaveValidOutputs()
        {
            return IsValid(m_ViewZ)
                && IsValid(m_MotionVectors)
                && IsValid(m_DlssMotionVectors)
                && IsValid(m_NrdNormalRoughness)
                && IsValid(m_BaseColorMetalness)
                && IsValid(m_DlssNormalRoughness)
                && IsValid(m_DiffuseAlbedo)
                && IsValid(m_SpecularAlbedo)
                && IsValid(m_NrdDiffuseMaterialFactor)
                && IsValid(m_NrdSpecularMaterialFactor)
                && IsValid(m_DlssDepth);
        }

        private static bool IsValid(RenderGraphTexture texture)
        {
            return texture?.innerHandle.IsValid() == true;
        }

        private void BindOutput(CommandBuffer cmd, int propertyId, RenderGraphTexture texture)
        {
            cmd.SetRayTracingTextureParam(m_RayTracingShader, propertyId, texture.innerHandle);
        }

        private void ResetCameraParameters()
        {
            m_CameraPositionWS = Vector4.zero;
            m_PixelCoordToViewDirWS = Matrix4x4.identity;
            m_WorldToView = Matrix4x4.identity;
            m_WorldToClip = Matrix4x4.identity;
            m_WorldToViewPrevious = Matrix4x4.identity;
            m_WorldToClipPrevious = Matrix4x4.identity;
            m_RayMinDistance = 0.01f;
            m_RayMaxDistance = 1000.0f;
        }
    }
}

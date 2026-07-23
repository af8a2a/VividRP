using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// OpenPBR reference path-tracing prototype for StandardLit. It traces an iterative multi-bounce
    /// path and performs next-event estimation against the main directional light at every hit.
    /// RGB stores sample radiance and A is one for a primary hit or zero for a miss.
    /// </summary>
    public sealed class ReferencedPathTracingPass : UnsafePass, IAllowGlobalStateModificationPass
    {
        internal const string MaterialShaderPassName = "ReferencedPathtracingDXR";
        internal const string RayGenerationShaderName = "RayGenReferencedPathtracing";

        private const string AccelerationStructureName = "_AccelerationStructure";
        private const int MaxBounceCount = 4;
        private const int RussianRouletteStartBounce = 3;

        private static readonly int WorldPositionTextureId = Shader.PropertyToID("_WorldPositionTexture");
        private static readonly int DiffuseRadianceHitDistanceId =
            Shader.PropertyToID("_ReferencedDiffuseRadianceHitDistance");
        private static readonly int SpecularRadianceHitDistanceId =
            Shader.PropertyToID("_ReferencedSpecularRadianceHitDistance");
        private static readonly int DirectLightingId =
            Shader.PropertyToID("_ReferencedPathTracingDirectLighting");
        private static readonly int EmissionId = Shader.PropertyToID("_ReferencedPathTracingEmission");
        private static readonly int DiffuseRayDirectionHitDistanceId =
            Shader.PropertyToID("_ReferencedDiffuseRayDirectionHitDistance");
        private static readonly int SpecularRayDirectionHitDistanceId =
            Shader.PropertyToID("_ReferencedSpecularRayDirectionHitDistance");
        private static readonly int CameraPositionId = Shader.PropertyToID("_CameraPositionWS");
        private static readonly int PixelCoordToViewDirWSId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private static readonly int WorldToViewId = Shader.PropertyToID("_ReferencedWorldToView");
        private static readonly int RayMinDistanceId = Shader.PropertyToID("_RayMinDistance");
        private static readonly int RayMaxDistanceId = Shader.PropertyToID("_RayMaxDistance");
        private static readonly int MainLightDirectionWSId = Shader.PropertyToID("_ReferencedMainLightDirectionWS");
        private static readonly int MainLightColorId = Shader.PropertyToID("_ReferencedMainLightColor");
        private static readonly int MaxBounceCountId = Shader.PropertyToID("_ReferencedMaxBounceCount");
        private static readonly int RussianRouletteStartBounceId =
            Shader.PropertyToID("_ReferencedRussianRouletteStartBounce");
        private static readonly int FrameIndexId = Shader.PropertyToID("_ReferencedFrameIndex");
        private static readonly int ReblurHitDistanceParametersId =
            Shader.PropertyToID("_ReferencedReblurHitDistanceParameters");
        private static readonly int ReGIRLightsId = Shader.PropertyToID("_ReGIRLights");
        private static readonly int ReGIRParametersId = Shader.PropertyToID("_ReGIRParameters");
        private static readonly int ReGIRReservoirsId = Shader.PropertyToID("_ReGIRReservoirs");
        private static readonly int ReGIRLightPdfTextureId = Shader.PropertyToID("_ReGIRLightPdfTexture");

        [RenderGraphResource(Name = "SceneRTAS", Access = AccessFlags.Read)]
        private RenderGraphAccelerationStructure m_SceneAccelerationStructure;

        [RenderGraphResource(Name = "ReGIRLights", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_ReGIRLightBuffer;

        [RenderGraphResource(Name = "ReGIRParameters", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_ReGIRParameterBuffer;

        [RenderGraphResource(Name = "ReGIRReservoirs", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_ReGIRReservoirBuffer;

        [RenderGraphResource(Name = "ReGIRLightPdfTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_ReGIRLightPdfTexture;

        [RenderGraphResource(
            Name = "WorldPosition",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_WorldPositionTexture;

        [RenderGraphResource(
            Name = "DiffuseRadianceHitDistance",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_DiffuseRadianceHitDistance;

        [RenderGraphResource(
            Name = "SpecularRadianceHitDistance",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_SpecularRadianceHitDistance;

        [RenderGraphResource(
            Name = "PathTracingDirectLighting",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_DirectLighting;

        [RenderGraphResource(
            Name = "PathTracingEmission",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_Emission;

        [RenderGraphResource(
            Name = "DiffuseRayDirectionHitDistance",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_DiffuseRayDirectionHitDistance;

        [RenderGraphResource(
            Name = "SpecularRayDirectionHitDistance",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_SpecularRayDirectionHitDistance;

        private RayTracingShader m_RayTracingShader;
        private bool m_SupportsRayTracing;
        private bool m_ShouldSkipExecution;
        private int m_Width = 1;
        private int m_Height = 1;
        private Vector4 m_CameraPositionWS;
        private Matrix4x4 m_PixelCoordToViewDirWS = Matrix4x4.identity;
        private Matrix4x4 m_WorldToView = Matrix4x4.identity;
        private float m_RayMinDistance = 0.01f;
        private float m_RayMaxDistance = 1000.0f;
        private Vector4 m_MainLightDirectionWS = new Vector4(0.0f, 1.0f, 0.0f, 0.0f);
        private Vector4 m_MainLightColor = Vector4.zero;
        private Vector4 m_ReblurHitDistanceParameters =
            ReferencedPathTracingReblurSettings.CreateDefault().hitDistanceParameters;
        private int m_FrameIndex;

        public ReferencedPathTracingPass()
        {
            profilingSampler = new ProfilingSampler(nameof(ReferencedPathTracingPass));
            m_SceneAccelerationStructure = CreateSceneAccelerationStructure();
            m_ReGIRLightBuffer = RenderGraphBuffer.CreateStructured(
                "ReGIRLights",
                VividReGIRLightData.Stride);
            m_ReGIRParameterBuffer = RenderGraphBuffer.CreateStructured(
                "ReGIRParameters",
                VividReGIRParameters.Stride);
            m_ReGIRReservoirBuffer = RenderGraphBuffer.CreateStructured(
                "ReGIRReservoirs",
                VividReGIRReservoir.Stride);
            m_ReGIRLightPdfTexture = RenderGraphTexture.CreateInput(
                "ReGIRLightPdfTexture",
                GraphicsFormat.R32_SFloat);
            m_WorldPositionTexture = RenderGraphTexture.CreateOutput(
                "WorldPosition",
                GraphicsFormat.R32G32B32A32_SFloat);
            m_DiffuseRadianceHitDistance = RenderGraphTexture.CreateOutput(
                "DiffuseRadianceHitDistance",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_SpecularRadianceHitDistance = RenderGraphTexture.CreateOutput(
                "SpecularRadianceHitDistance",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_DirectLighting = RenderGraphTexture.CreateOutput(
                "PathTracingDirectLighting",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_Emission = RenderGraphTexture.CreateOutput(
                "PathTracingEmission",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_DiffuseRayDirectionHitDistance = RenderGraphTexture.CreateOutput(
                "DiffuseRayDirectionHitDistance",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_SpecularRayDirectionHitDistance = RenderGraphTexture.CreateOutput(
                "SpecularRayDirectionHitDistance",
                GraphicsFormat.R16G16B16A16_SFloat);
            ConfigureOutputs(1, 1);
        }

        public override void Create()
        {
            m_SupportsRayTracing = SystemInfo.supportsRayTracing;
            m_RayTracingShader = PipelineResourceManager
                .Get<VividRPCoreResources>()
                ?.ReferencedPathtracingRayTracing;

            if (m_RayTracingShader == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find the ray-tracing shader resource for {nameof(ReferencedPathTracingPass)}.");
            }
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            m_Width = CameraDimensionUtility.ResolveCameraDimension(
                cameraData?.actualWidth ?? 0,
                cameraData?.pixelWidth ?? 0,
                Screen.width);
            m_Height = CameraDimensionUtility.ResolveCameraDimension(
                cameraData?.actualHeight ?? 0,
                cameraData?.pixelHeight ?? 0,
                Screen.height);

            ConfigureOutputs(m_Width, m_Height);
            PrepareMainDirectionalLight(frameData.GetOrCreate<VividLightData>());
            m_ReblurHitDistanceParameters =
                ReferencedPathTracingReblurSettingsResolver.Resolve().hitDistanceParameters;
            m_FrameIndex = cameraData != null && cameraData.frameIndex >= 0
                ? cameraData.frameIndex
                : Time.frameCount;

            var camera = cameraData?.camera;
            m_ShouldSkipExecution = camera == null || camera.orthographic;
            if (m_ShouldSkipExecution)
            {
                m_CameraPositionWS = Vector4.zero;
                m_PixelCoordToViewDirWS = Matrix4x4.identity;
                m_WorldToView = Matrix4x4.identity;
                m_RayMinDistance = 0.01f;
                m_RayMaxDistance = 1000.0f;
                return;
            }

            var cameraPosition = camera.transform.position;
            m_CameraPositionWS = new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 1.0f);
            m_PixelCoordToViewDirWS = cameraData.GetPixelCoordToViewDirWSMatrix();
            m_WorldToView = cameraData.GetViewMatrix();
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
                cmd.SetRayTracingTextureParam(
                    m_RayTracingShader,
                    WorldPositionTextureId,
                    m_WorldPositionTexture.innerHandle);
                BindOutput(cmd, DiffuseRadianceHitDistanceId, m_DiffuseRadianceHitDistance);
                BindOutput(cmd, SpecularRadianceHitDistanceId, m_SpecularRadianceHitDistance);
                BindOutput(cmd, DirectLightingId, m_DirectLighting);
                BindOutput(cmd, EmissionId, m_Emission);
                BindOutput(cmd, DiffuseRayDirectionHitDistanceId, m_DiffuseRayDirectionHitDistance);
                BindOutput(cmd, SpecularRayDirectionHitDistanceId, m_SpecularRayDirectionHitDistance);
                cmd.SetRayTracingVectorParam(m_RayTracingShader, CameraPositionId, m_CameraPositionWS);
                cmd.SetRayTracingMatrixParam(
                    m_RayTracingShader,
                    PixelCoordToViewDirWSId,
                    m_PixelCoordToViewDirWS);
                cmd.SetRayTracingMatrixParam(m_RayTracingShader, WorldToViewId, m_WorldToView);
                cmd.SetRayTracingFloatParam(m_RayTracingShader, RayMinDistanceId, m_RayMinDistance);
                cmd.SetRayTracingFloatParam(m_RayTracingShader, RayMaxDistanceId, m_RayMaxDistance);
                cmd.SetGlobalVector(MainLightDirectionWSId, m_MainLightDirectionWS);
                cmd.SetGlobalVector(MainLightColorId, m_MainLightColor);
                cmd.SetRayTracingVectorParam(
                    m_RayTracingShader,
                    MainLightDirectionWSId,
                    m_MainLightDirectionWS);
                cmd.SetRayTracingVectorParam(m_RayTracingShader, MainLightColorId, m_MainLightColor);
                cmd.SetRayTracingIntParam(m_RayTracingShader, MaxBounceCountId, MaxBounceCount);
                cmd.SetRayTracingIntParam(
                    m_RayTracingShader,
                    RussianRouletteStartBounceId,
                    RussianRouletteStartBounce);
                cmd.SetRayTracingIntParam(m_RayTracingShader, FrameIndexId, m_FrameIndex);
                cmd.SetRayTracingVectorParam(
                    m_RayTracingShader,
                    ReblurHitDistanceParametersId,
                    m_ReblurHitDistanceParameters);
                if (HasValidReGIRResources())
                    BindReGIRGlobals(cmd);
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
            m_CameraPositionWS = Vector4.zero;
            m_PixelCoordToViewDirWS = Matrix4x4.identity;
            m_WorldToView = Matrix4x4.identity;
            m_RayMinDistance = 0.01f;
            m_RayMaxDistance = 1000.0f;
            m_MainLightDirectionWS = new Vector4(0.0f, 1.0f, 0.0f, 0.0f);
            m_MainLightColor = Vector4.zero;
            m_ReblurHitDistanceParameters =
                ReferencedPathTracingReblurSettings.CreateDefault().hitDistanceParameters;
            m_FrameIndex = 0;
        }

        private void ConfigureOutputs(int width, int height)
        {
            ConfigureOutput(
                m_WorldPositionTexture,
                width,
                height,
                GraphicsFormat.R32G32B32A32_SFloat,
                "WorldPosition");
            ConfigureOutput(
                m_DiffuseRadianceHitDistance,
                width,
                height,
                GraphicsFormat.R16G16B16A16_SFloat,
                "DiffuseRadianceHitDistance");
            ConfigureOutput(
                m_SpecularRadianceHitDistance,
                width,
                height,
                GraphicsFormat.R16G16B16A16_SFloat,
                "SpecularRadianceHitDistance");
            ConfigureOutput(
                m_DirectLighting,
                width,
                height,
                GraphicsFormat.R16G16B16A16_SFloat,
                "PathTracingDirectLighting");
            ConfigureOutput(
                m_Emission,
                width,
                height,
                GraphicsFormat.R16G16B16A16_SFloat,
                "PathTracingEmission");
            ConfigureOutput(
                m_DiffuseRayDirectionHitDistance,
                width,
                height,
                GraphicsFormat.R16G16B16A16_SFloat,
                "DiffuseRayDirectionHitDistance");
            ConfigureOutput(
                m_SpecularRayDirectionHitDistance,
                width,
                height,
                GraphicsFormat.R16G16B16A16_SFloat,
                "SpecularRayDirectionHitDistance");
        }

        private static void ConfigureOutput(
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
            texture.desc.ClearBuffer = true;
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
            return IsValid(m_WorldPositionTexture)
                && IsValid(m_DiffuseRadianceHitDistance)
                && IsValid(m_SpecularRadianceHitDistance)
                && IsValid(m_DirectLighting)
                && IsValid(m_Emission)
                && IsValid(m_DiffuseRayDirectionHitDistance)
                && IsValid(m_SpecularRayDirectionHitDistance);
        }

        private static bool IsValid(RenderGraphTexture texture)
        {
            return texture?.innerHandle.IsValid() == true;
        }

        private void BindOutput(CommandBuffer cmd, int propertyId, RenderGraphTexture texture)
        {
            cmd.SetRayTracingTextureParam(m_RayTracingShader, propertyId, texture.innerHandle);
        }

        private static RenderGraphAccelerationStructure CreateSceneAccelerationStructure()
        {
            return new RenderGraphAccelerationStructure
            {
                desc = RenderGraphAccelerationStructureDesc.Create("SceneRTAS")
            };
        }

        private void PrepareMainDirectionalLight(VividLightData lightData)
        {
            m_MainLightDirectionWS = new Vector4(0.0f, 1.0f, 0.0f, 0.0f);
            m_MainLightColor = Vector4.zero;

            if (lightData == null)
                return;

            lightData.CompleteLightGridPrepare();
            if (!lightData.hasMainDirectionalLight)
                return;

            var mainLight = lightData.mainDirectionalLight;
            var directionWS = mainLight.directionWS;
            if (directionWS.sqrMagnitude <= 1e-8f)
                return;

            directionWS.Normalize();
            m_MainLightDirectionWS = new Vector4(directionWS.x, directionWS.y, directionWS.z, 0.0f);
            m_MainLightColor = new Vector4(
                Mathf.Max(mainLight.color.x, 0.0f),
                Mathf.Max(mainLight.color.y, 0.0f),
                Mathf.Max(mainLight.color.z, 0.0f),
                1.0f);
        }

        private bool HasValidReGIRResources()
        {
            return m_ReGIRLightBuffer?.innerHandle.IsValid() == true
                && m_ReGIRParameterBuffer?.innerHandle.IsValid() == true
                && m_ReGIRReservoirBuffer?.innerHandle.IsValid() == true
                && m_ReGIRLightPdfTexture?.innerHandle.IsValid() == true;
        }

        private void BindReGIRGlobals(CommandBuffer cmd)
        {
            cmd.SetGlobalBuffer(ReGIRLightsId, m_ReGIRLightBuffer.innerHandle);
            cmd.SetGlobalBuffer(ReGIRParametersId, m_ReGIRParameterBuffer.innerHandle);
            cmd.SetGlobalBuffer(ReGIRReservoirsId, m_ReGIRReservoirBuffer.innerHandle);
            cmd.SetGlobalTexture(ReGIRLightPdfTextureId, m_ReGIRLightPdfTexture.innerHandle);
        }
    }
}

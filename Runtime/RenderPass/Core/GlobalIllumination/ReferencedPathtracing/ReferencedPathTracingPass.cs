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
        private static readonly int CameraPositionId = Shader.PropertyToID("_CameraPositionWS");
        private static readonly int PixelCoordToViewDirWSId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private static readonly int RayMinDistanceId = Shader.PropertyToID("_RayMinDistance");
        private static readonly int RayMaxDistanceId = Shader.PropertyToID("_RayMaxDistance");
        private static readonly int MainLightDirectionWSId = Shader.PropertyToID("_ReferencedMainLightDirectionWS");
        private static readonly int MainLightColorId = Shader.PropertyToID("_ReferencedMainLightColor");
        private static readonly int MaxBounceCountId = Shader.PropertyToID("_ReferencedMaxBounceCount");
        private static readonly int RussianRouletteStartBounceId =
            Shader.PropertyToID("_ReferencedRussianRouletteStartBounce");
        private static readonly int FrameIndexId = Shader.PropertyToID("_ReferencedFrameIndex");
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

        private RayTracingShader m_RayTracingShader;
        private bool m_SupportsRayTracing;
        private bool m_ShouldSkipExecution;
        private int m_Width = 1;
        private int m_Height = 1;
        private Vector4 m_CameraPositionWS;
        private Matrix4x4 m_PixelCoordToViewDirWS = Matrix4x4.identity;
        private float m_RayMinDistance = 0.01f;
        private float m_RayMaxDistance = 1000.0f;
        private Vector4 m_MainLightDirectionWS = new Vector4(0.0f, 1.0f, 0.0f, 0.0f);
        private Vector4 m_MainLightColor = Vector4.zero;
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
            ConfigureWorldPositionTexture(1, 1);
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

            ConfigureWorldPositionTexture(m_Width, m_Height);
            PrepareMainDirectionalLight(frameData.GetOrCreate<VividLightData>());
            m_FrameIndex = cameraData != null && cameraData.frameIndex >= 0
                ? cameraData.frameIndex
                : Time.frameCount;

            var camera = cameraData?.camera;
            m_ShouldSkipExecution = camera == null || camera.orthographic;
            if (m_ShouldSkipExecution)
            {
                m_CameraPositionWS = Vector4.zero;
                m_PixelCoordToViewDirWS = Matrix4x4.identity;
                m_RayMinDistance = 0.01f;
                m_RayMaxDistance = 1000.0f;
                return;
            }

            var cameraPosition = camera.transform.position;
            m_CameraPositionWS = new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 1.0f);
            m_PixelCoordToViewDirWS = cameraData.GetPixelCoordToViewDirWSMatrix();
            m_RayMinDistance = Mathf.Max(camera.nearClipPlane, 0.0001f);
            m_RayMaxDistance = Mathf.Max(camera.farClipPlane, m_RayMinDistance + 0.0001f);
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_ShouldSkipExecution
                || !m_SupportsRayTracing
                || m_RayTracingShader == null
                || m_SceneAccelerationStructure == null
                || m_WorldPositionTexture?.innerHandle.IsValid() != true)
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
                cmd.SetRayTracingVectorParam(m_RayTracingShader, CameraPositionId, m_CameraPositionWS);
                cmd.SetRayTracingMatrixParam(
                    m_RayTracingShader,
                    PixelCoordToViewDirWSId,
                    m_PixelCoordToViewDirWS);
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
            m_RayMinDistance = 0.01f;
            m_RayMaxDistance = 1000.0f;
            m_MainLightDirectionWS = new Vector4(0.0f, 1.0f, 0.0f, 0.0f);
            m_MainLightColor = Vector4.zero;
            m_FrameIndex = 0;
        }

        private void ConfigureWorldPositionTexture(int width, int height)
        {
            if (m_WorldPositionTexture?.desc == null)
                return;

            m_WorldPositionTexture.Resize(width, height);
            m_WorldPositionTexture.desc.ColorFormat = GraphicsFormat.R32G32B32A32_SFloat;
            m_WorldPositionTexture.desc.DepthBufferBits = DepthBits.None;
            m_WorldPositionTexture.desc.MsaaSamples = MSAASamples.None;
            m_WorldPositionTexture.desc.FilterMode = FilterMode.Point;
            m_WorldPositionTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_WorldPositionTexture.desc.ClearBuffer = true;
            m_WorldPositionTexture.desc.ClearColor = Color.clear;
            m_WorldPositionTexture.desc.UseMipMap = false;
            m_WorldPositionTexture.desc.AutoGenerateMips = false;
            m_WorldPositionTexture.desc.MipCount = 1;
            m_WorldPositionTexture.desc.EnableRandomWrite = true;
            m_WorldPositionTexture.desc.BindTextureMS = false;
            m_WorldPositionTexture.desc.Name = "WorldPosition";
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

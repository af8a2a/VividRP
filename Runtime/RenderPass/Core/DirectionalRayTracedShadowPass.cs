using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class DirectionalRayTracedShadowPass : UnsafePass
    {
        private const int ThreadGroupSizeX = 8;
        private const int ThreadGroupSizeY = 8;
        private const string KernelName = "DirectionalRayTracedShadow";
        private const string AccelerationStructureName = "_AccelerationStructure";

        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");
        private static readonly int DirectionalShadowTextureId = Shader.PropertyToID("_DirectionalShadowTexture");
        private static readonly int OutputWidthId = Shader.PropertyToID("_OutputWidth");
        private static readonly int OutputHeightId = Shader.PropertyToID("_OutputHeight");
        private static readonly int LightDirectionWSId = Shader.PropertyToID("_LightDirectionWS");
        private static readonly int RayLengthId = Shader.PropertyToID("_RayLength");
        private static readonly int InvViewProjectionMatrixId = Shader.PropertyToID("_InvViewProjectionMatrix");
        private static readonly int SunBasisXId = Shader.PropertyToID("_SunBasisX");
        private static readonly int SunBasisYId = Shader.PropertyToID("_SunBasisY");
        private static readonly int TanSunAngularRadiusId = Shader.PropertyToID("_TanSunAngularRadius");
        private static readonly int FrameIndexId = Shader.PropertyToID("_FrameIndex");
        private static readonly int ShadowClassifyMaskId = Shader.PropertyToID("_ShadowClassifyMask");

        /// <summary>
        /// Clear value for the raw shadow texture. HALF_MAX (65504) encodes "fully lit / no occluder".
        /// </summary>
        private static readonly Color RawShadowClearColor = new Color(65504f, 0f, 0f, 0f);

        [RenderGraphResource(Name = "SceneRTAS", Access = AccessFlags.Read)]
        private RenderGraphAccelerationStructure m_SceneAccelerationStructure;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "GBuffer1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(Name = "DirectionalShadowTexture", Access = AccessFlags.Write)]
        private RenderGraphTexture m_DirectionalShadowTexture;

        [RenderGraphResource(Name = "ShadowClassifyMask", Access = AccessFlags.Read)]
        private RenderGraphTexture m_ShadowClassifyMask;

        private ComputeShader m_DirectionalRayTracedShadowCompute;
        private int m_Kernel = -1;
        private const string ClassifyKeyword = "SHADOW_CLASSIFY_ENABLED";
        private bool m_SupportsRayTracing;
        private bool m_ShouldTrace;
        private int m_DispatchGroupCountX = 1;
        private int m_DispatchGroupCountY = 1;
        private Vector4 m_LightDirectionWS = new Vector4(0f, 1f, 0f, 0f);
        private float m_RayLength = VividAdditionalLightData.DefaultRayTracedShadowRayLength;
        private ShaderVariablesRayTracing m_ShaderVariablesRayTracing;
        private Matrix4x4 m_InvViewProjectionMatrix = Matrix4x4.identity;
        private Vector4 m_SunBasisX;
        private Vector4 m_SunBasisY;
        private float m_TanSunAngularRadius;
        private int m_FrameIndex;
        
        // private RTHandle debugTexture;


        internal readonly struct ResolvedDirectionalShadowRequest
        {
            public ResolvedDirectionalShadowRequest(
                bool shouldTrace,
                EntityId lightEntityId,
                Vector3 lightDirectionWS,
                float rayLength,
                bool usePipelineSettings,
                float rayBias,
                float distantRayBias,
                float sunAngularDiameter)
            {
                ShouldTrace = shouldTrace;
                LightEntityId = lightEntityId;
                LightDirectionWS = lightDirectionWS;
                RayLength = rayLength;
                UsePipelineSettings = usePipelineSettings;
                RayBias = rayBias;
                DistantRayBias = distantRayBias;
                SunAngularDiameter = sunAngularDiameter;
            }

            public bool ShouldTrace { get; }

            public EntityId LightEntityId { get; }

            public Vector3 LightDirectionWS { get; }

            public float RayLength { get; }

            public bool UsePipelineSettings { get; }

            public float RayBias { get; }

            public float DistantRayBias { get; }

            public float SunAngularDiameter { get; }
        }

        public DirectionalRayTracedShadowPass()
        {
            profilingSampler = new ProfilingSampler(nameof(DirectionalRayTracedShadowPass));
            m_SceneAccelerationStructure = CreateSceneAccelerationStructure();
            m_DepthTexture = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_GBuffer1 = RenderGraphTexture.CreateInput("GBuffer1", GraphicsFormat.R16G16_SFloat);
            m_DirectionalShadowTexture = RenderGraphTexture.CreateOutput("DirectionalShadowTexture", GraphicsFormat.R16_SFloat);
            m_DirectionalShadowTexture.desc.ClearBuffer = true;
            m_DirectionalShadowTexture.desc.ClearColor = RawShadowClearColor;
            m_DirectionalShadowTexture.desc.FilterMode = FilterMode.Point;
            m_DirectionalShadowTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_ShadowClassifyMask = RenderGraphTexture.CreateInput("ShadowClassifyMask", GraphicsFormat.R8_UNorm);
        }

        public override void Create()
        {
            m_SupportsRayTracing = SystemInfo.supportsRayTracing;
            m_DirectionalRayTracedShadowCompute =
                PipelineResourceManager.Get<VividRPCoreResources>()?.DirectionalRayTracedShadowCompute;

            if (m_DirectionalRayTracedShadowCompute == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find compute shader resource 'Shaders/Core/Private/DirectionalRayTracedShadow' for {nameof(DirectionalRayTracedShadowPass)}.");
                return;
            }

            m_Kernel = m_DirectionalRayTracedShadowCompute.FindKernel(KernelName);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            ConfigureOutputTexture(cameraData.actualWidth, cameraData.actualHeight);
            m_InvViewProjectionMatrix = ResolveInvViewProjectionMatrix(cameraData);

            
            
            // var desc = new RenderTextureDescriptor(cameraData.actualWidth, cameraData.actualHeight)
            // {
            //     graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
            //     enableRandomWrite = true
            // };
            // RenderingUtils.ReAllocateHandleIfNeeded(ref debugTexture, desc, name: "NRD-SIGMA TileTexture");

            m_DispatchGroupCountX = CoreUtils.DivRoundUp(cameraData.actualWidth, ThreadGroupSizeX);
            m_DispatchGroupCountY = CoreUtils.DivRoundUp(cameraData.actualHeight, ThreadGroupSizeY);

            var hasSceneAccelerationStructure = m_SceneAccelerationStructure != null
                && (m_SceneAccelerationStructure.innerHandle.IsValid()
                    || m_SceneAccelerationStructure.HasAccelerationStructure);

            var request = ResolveShadowRequest(
                frameData.GetOrCreate<VividLightData>(),
                m_SupportsRayTracing,
                hasSceneAccelerationStructure);
            m_ShaderVariablesRayTracing =
                ShaderVariablesRayTracingUtility.Create(frameData.GetOrCreate<VividRayTracingSettingsData>());

            m_ShouldTrace = request.ShouldTrace
                && m_DirectionalRayTracedShadowCompute != null
                && m_Kernel >= 0;
            m_LightDirectionWS = request.ShouldTrace
                ? new Vector4(request.LightDirectionWS.x, request.LightDirectionWS.y, request.LightDirectionWS.z, 0f)
                : new Vector4(0f, 1f, 0f, 0f);
            m_RayLength = request.ShouldTrace
                ? request.RayLength
                : VividAdditionalLightData.DefaultRayTracedShadowRayLength;

            if (request.ShouldTrace && !request.UsePipelineSettings)
            {
                ShaderVariablesRayTracingUtility.OverrideBiases(
                    ref m_ShaderVariablesRayTracing,
                    request.RayBias,
                    request.DistantRayBias);
            }

            if (request.ShouldTrace)
            {
                var dir = request.LightDirectionWS;
                ComputeSunBasis(dir, out var basisX, out var basisY);
                m_SunBasisX = new Vector4(basisX.x, basisX.y, basisX.z, 0f);
                m_SunBasisY = new Vector4(basisY.x, basisY.y, basisY.z, 0f);
                m_TanSunAngularRadius = Mathf.Tan(Mathf.Deg2Rad * request.SunAngularDiameter * 0.5f);
                m_FrameIndex = Time.frameCount;
            }
            else
            {
                m_SunBasisX = Vector4.zero;
                m_SunBasisY = Vector4.zero;
                m_TanSunAngularRadius = 0f;
                m_FrameIndex = 0;
            }
        }

        public override void Record(UnsafeGraphContext context)
        {
            var nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            using (new ProfilingScope(nativeCmd, profilingSampler))
            {
                ClearOutput(nativeCmd);

                if (!m_ShouldTrace
                    || m_DirectionalRayTracedShadowCompute == null
                    || m_Kernel < 0
                    || m_SceneAccelerationStructure == null
                    || !m_DepthTexture.innerHandle.IsValid()
                    || !m_GBuffer1.innerHandle.IsValid()
                    || !m_DirectionalShadowTexture.innerHandle.IsValid())
                {
                    return;
                }

                var accelerationStructure = (RayTracingAccelerationStructure)m_SceneAccelerationStructure;
                if (accelerationStructure == null)
                    return;

                nativeCmd.SetRayTracingAccelerationStructure(
                    m_DirectionalRayTracedShadowCompute,
                    m_Kernel,
                    AccelerationStructureName,
                    accelerationStructure);
                nativeCmd.SetComputeTextureParam(
                    m_DirectionalRayTracedShadowCompute,
                    m_Kernel,
                    DepthTextureId,
                    m_DepthTexture.innerHandle);
                nativeCmd.SetComputeTextureParam(
                    m_DirectionalRayTracedShadowCompute,
                    m_Kernel,
                    GBuffer1Id,
                    m_GBuffer1.innerHandle);
                nativeCmd.SetComputeTextureParam(
                    m_DirectionalRayTracedShadowCompute,
                    m_Kernel,
                    DirectionalShadowTextureId,
                    m_DirectionalShadowTexture);

                BlueNoise.Instance?.Bind(nativeCmd);

                var useClassify = false;// m_ShadowClassifyMask != null && m_ShadowClassifyMask.innerHandle.IsValid();
                if (useClassify)
                {
                    nativeCmd.EnableKeyword(m_DirectionalRayTracedShadowCompute, new LocalKeyword(m_DirectionalRayTracedShadowCompute, ClassifyKeyword));
                    nativeCmd.SetComputeTextureParam(
                        m_DirectionalRayTracedShadowCompute,
                        m_Kernel,
                        ShadowClassifyMaskId,
                        m_ShadowClassifyMask.innerHandle);
                }
                else
                {
                    nativeCmd.DisableKeyword(m_DirectionalRayTracedShadowCompute, new LocalKeyword(m_DirectionalRayTracedShadowCompute, ClassifyKeyword));
                }

                nativeCmd.SetComputeVectorParam(m_DirectionalRayTracedShadowCompute, LightDirectionWSId, m_LightDirectionWS);
                nativeCmd.SetComputeFloatParam(m_DirectionalRayTracedShadowCompute, RayLengthId, m_RayLength);
                nativeCmd.SetComputeVectorParam(m_DirectionalRayTracedShadowCompute, SunBasisXId, m_SunBasisX);
                nativeCmd.SetComputeVectorParam(m_DirectionalRayTracedShadowCompute, SunBasisYId, m_SunBasisY);
                nativeCmd.SetComputeFloatParam(m_DirectionalRayTracedShadowCompute, TanSunAngularRadiusId, m_TanSunAngularRadius);
                nativeCmd.SetComputeIntParam(m_DirectionalRayTracedShadowCompute, FrameIndexId, m_FrameIndex);
                nativeCmd.SetComputeIntParam(m_DirectionalRayTracedShadowCompute, OutputWidthId, m_DirectionalShadowTexture.desc.Width);
                nativeCmd.SetComputeIntParam(m_DirectionalRayTracedShadowCompute, OutputHeightId, m_DirectionalShadowTexture.desc.Height);
                nativeCmd.SetComputeMatrixParam(
                    m_DirectionalRayTracedShadowCompute,
                    InvViewProjectionMatrixId,
                    m_InvViewProjectionMatrix);

                ConstantBuffer.Push(
                    nativeCmd,
                    m_ShaderVariablesRayTracing,
                    m_DirectionalRayTracedShadowCompute,
                    ShaderVariablesRayTracingUtility.ConstantBufferShaderId);
                nativeCmd.DispatchCompute(
                    m_DirectionalRayTracedShadowCompute,
                    m_Kernel,
                    m_DispatchGroupCountX,
                    m_DispatchGroupCountY,
                    1);
            }
        }

        public override void Dispose()
        {
            m_DirectionalRayTracedShadowCompute = null;
            m_Kernel = -1;
            m_ShouldTrace = false;
            m_DispatchGroupCountX = 1;
            m_DispatchGroupCountY = 1;
            m_LightDirectionWS = new Vector4(0f, 1f, 0f, 0f);
            m_RayLength = VividAdditionalLightData.DefaultRayTracedShadowRayLength;
            m_ShaderVariablesRayTracing = default;
            m_InvViewProjectionMatrix = Matrix4x4.identity;
            m_SunBasisX = Vector4.zero;
            m_SunBasisY = Vector4.zero;
            m_TanSunAngularRadius = 0f;
            m_FrameIndex = 0;
        }

        internal static ResolvedDirectionalShadowRequest ResolveShadowRequest(
            VividLightData lightData,
            bool supportsRayTracing,
            bool hasSceneAccelerationStructure)
        {
            if (!supportsRayTracing
                || !hasSceneAccelerationStructure
                || lightData == null
                || !lightData.hasMainDirectionalLight
                || !TryResolveMainDirectionalLight(lightData, out var light, out var additionalLightData)
                || light == null
                || additionalLightData == null
                || !additionalLightData.isRayTracedShadowActive
                || !light.enabled
                || !light.gameObject.activeInHierarchy
                || light.shadows == LightShadows.None)
            {
                return default;
            }

            var lightDirectionWS = lightData.mainDirectionalLight.directionWS;
            if (lightDirectionWS.sqrMagnitude <= 1e-6f)
                lightDirectionWS = -light.transform.forward;

            if (lightDirectionWS.sqrMagnitude <= 1e-6f)
                return default;

            return new ResolvedDirectionalShadowRequest(
                true,
                light.GetEntityId(),
                lightDirectionWS.normalized,
                additionalLightData.rayTracedShadowRayLength,
                additionalLightData.usePipelineSettings,
                additionalLightData.rayTracedShadowRayBias,
                additionalLightData.rayTracedShadowDistantRayBias,
                additionalLightData.rayTracedShadowSunAngularDiameter);
        }

        internal static bool TryResolveMainDirectionalLight(
            VividLightData lightData,
            out Light light,
            out VividAdditionalLightData additionalLightData)
        {
            light = null;
            additionalLightData = null;

            if (lightData == null || !lightData.hasMainDirectionalLight)
                return false;

            var mainDirectionalLightEntityId = lightData.mainDirectionalLightEntityId;
            if (mainDirectionalLightEntityId.Equals(EntityId.None))
                return false;

            if (lightData.mainLight != null
                && lightData.mainLight.type == LightType.Directional
                && lightData.mainLight.GetEntityId().Equals(mainDirectionalLightEntityId))
            {
                light = lightData.mainLight;
            }

            if (light == null && lightData.hasVisibleLights)
            {
                var visibleLights = lightData.visibleLights;
                for (var lightIndex = 0; lightIndex < visibleLights.Length; lightIndex++)
                {
                    var candidate = visibleLights[lightIndex].light;
                    if (candidate == null
                        || candidate.type != LightType.Directional
                        || !candidate.GetEntityId().Equals(mainDirectionalLightEntityId))
                    {
                        continue;
                    }

                    light = candidate;
                    break;
                }
            }

            if (light == null)
            {
                var sceneLights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (var lightIndex = 0; lightIndex < sceneLights.Length; lightIndex++)
                {
                    var candidate = sceneLights[lightIndex];
                    if (candidate == null
                        || candidate.type != LightType.Directional
                        || !candidate.GetEntityId().Equals(mainDirectionalLightEntityId))
                    {
                        continue;
                    }

                    light = candidate;
                    break;
                }
            }

            return light != null && light.TryGetComponent(out additionalLightData);
        }

        private void ClearOutput(CommandBuffer cmd)
        {
            if (cmd == null || !m_DirectionalShadowTexture.innerHandle.IsValid())
                return;

            cmd.SetRenderTarget(m_DirectionalShadowTexture);
            cmd.ClearRenderTarget(false, true, RawShadowClearColor);
        }

        private void ConfigureOutputTexture(int width, int height)
        {
            if (m_DirectionalShadowTexture?.desc == null)
                return;

            m_DirectionalShadowTexture.Resize(width, height);
            m_DirectionalShadowTexture.desc.ColorFormat = GraphicsFormat.R16_SFloat;
            m_DirectionalShadowTexture.desc.DepthBufferBits = DepthBits.None;
            m_DirectionalShadowTexture.desc.MsaaSamples = MSAASamples.None;
            m_DirectionalShadowTexture.desc.FilterMode = FilterMode.Point;
            m_DirectionalShadowTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_DirectionalShadowTexture.desc.ClearBuffer = true;
            m_DirectionalShadowTexture.desc.ClearColor = RawShadowClearColor;
            m_DirectionalShadowTexture.desc.UseMipMap = false;
            m_DirectionalShadowTexture.desc.AutoGenerateMips = false;
            m_DirectionalShadowTexture.desc.MipCount = 1;
            m_DirectionalShadowTexture.desc.EnableRandomWrite = true;
            m_DirectionalShadowTexture.desc.BindTextureMS = false;
            m_DirectionalShadowTexture.desc.Name = "DirectionalShadowTexture";
        }


        private static RenderGraphAccelerationStructure CreateSceneAccelerationStructure()
        {
            return new RenderGraphAccelerationStructure
            {
                desc = RenderGraphAccelerationStructureDesc.Create("SceneRTAS")
            };
        }

        internal static Matrix4x4 ResolveInvViewProjectionMatrix(VividCameraData cameraData)
        {
            if (cameraData == null)
                return Matrix4x4.identity;

            return cameraData.GetGPUViewProjectionMatrix(renderIntoTexture: true).inverse;
        }

        internal static void ComputeSunBasis(Vector3 sunDirection, out Vector3 basisX, out Vector3 basisY)
        {
            var sign = sunDirection.z >= 0f ? 1f : -1f;
            var a = -1f / (sign + sunDirection.z);
            var b = sunDirection.x * sunDirection.y * a;
            basisX = new Vector3(1f + sign * sunDirection.x * sunDirection.x * a, sign * b, -sign * sunDirection.x);
            basisY = new Vector3(b, sign + sunDirection.y * sunDirection.y * a, -sunDirection.y);
        }

    }
}

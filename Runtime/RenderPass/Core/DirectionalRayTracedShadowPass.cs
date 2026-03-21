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

        [RenderGraphResource(Name = "SceneRTAS", Access = AccessFlags.Read)]
        private RenderGraphAccelerationStructure m_SceneAccelerationStructure;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "GBuffer1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(Name = "DirectionalShadowTexture", Access = AccessFlags.Write)]
        private RenderGraphTexture m_DirectionalShadowTexture;

        
        [RenderGraphResource(Name = "DebugTexture", Access = AccessFlags.Write)]
        private RenderGraphTexture m_debugTexture;

        private ComputeShader m_DirectionalRayTracedShadowCompute;
        private int m_Kernel = -1;
        private bool m_SupportsRayTracing;
        private bool m_ShouldTrace;
        private int m_DispatchGroupCountX = 1;
        private int m_DispatchGroupCountY = 1;
        private Vector4 m_LightDirectionWS = new Vector4(0f, 1f, 0f, 0f);
        private float m_RayLength = VividAdditionalLightData.DefaultRayTracedShadowRayLength;
        private ShaderVariablesRayTracing m_ShaderVariablesRayTracing;

        internal readonly struct ResolvedDirectionalShadowRequest
        {
            public ResolvedDirectionalShadowRequest(
                bool shouldTrace,
                EntityId lightEntityId,
                Vector3 lightDirectionWS,
                float rayLength,
                bool usePipelineSettings,
                float rayBias,
                float distantRayBias)
            {
                ShouldTrace = shouldTrace;
                LightEntityId = lightEntityId;
                LightDirectionWS = lightDirectionWS;
                RayLength = rayLength;
                UsePipelineSettings = usePipelineSettings;
                RayBias = rayBias;
                DistantRayBias = distantRayBias;
            }

            public bool ShouldTrace { get; }

            public EntityId LightEntityId { get; }

            public Vector3 LightDirectionWS { get; }

            public float RayLength { get; }

            public bool UsePipelineSettings { get; }

            public float RayBias { get; }

            public float DistantRayBias { get; }
        }

        public DirectionalRayTracedShadowPass()
        {
            profilingSampler = new ProfilingSampler(nameof(DirectionalRayTracedShadowPass));
            m_SceneAccelerationStructure = CreateSceneAccelerationStructure();
            m_DepthTexture = CreateInputTexture("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_GBuffer1 = CreateInputTexture("GBuffer1", GraphicsFormat.R16G16_SFloat);
            m_DirectionalShadowTexture = CreateOutputTexture("DirectionalShadowTexture");
            
            m_debugTexture= CreateDebugTexture("DebugTexture");
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
                    m_DirectionalShadowTexture.innerHandle);
                
                nativeCmd.SetComputeTextureParam(
                    m_DirectionalRayTracedShadowCompute,
                    m_Kernel,
                    "DebugTexture",
                    m_debugTexture.innerHandle);

                // Debug.Log(m_LightDirectionWS);
                nativeCmd.SetComputeVectorParam(m_DirectionalRayTracedShadowCompute, LightDirectionWSId, m_LightDirectionWS);
                nativeCmd.SetComputeFloatParam(m_DirectionalRayTracedShadowCompute, RayLengthId, m_RayLength);

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
                additionalLightData.rayTracedShadowDistantRayBias);
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
            cmd.ClearRenderTarget(false, true, Color.white);
        }

        private void ConfigureOutputTexture(int width, int height)
        {
            if (m_DirectionalShadowTexture?.desc == null)
                return;

            m_DirectionalShadowTexture.desc.Width = width;
            m_DirectionalShadowTexture.desc.Height = height;
            m_DirectionalShadowTexture.desc.ColorFormat = GraphicsFormat.R16_SFloat;
            m_DirectionalShadowTexture.desc.DepthBufferBits = DepthBits.None;
            m_DirectionalShadowTexture.desc.MsaaSamples = MSAASamples.None;
            m_DirectionalShadowTexture.desc.FilterMode = FilterMode.Point;
            m_DirectionalShadowTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_DirectionalShadowTexture.desc.ClearBuffer = true;
            m_DirectionalShadowTexture.desc.ClearColor = Color.white;
            m_DirectionalShadowTexture.desc.UseMipMap = false;
            m_DirectionalShadowTexture.desc.AutoGenerateMips = false;
            m_DirectionalShadowTexture.desc.MipCount = 1;
            m_DirectionalShadowTexture.desc.EnableRandomWrite = true;
            m_DirectionalShadowTexture.desc.BindTextureMS = false;
            m_DirectionalShadowTexture.desc.Name = "DirectionalShadowTexture";
            
            
            m_debugTexture.desc.Width=width;
            m_debugTexture.desc.Height=height;
            m_debugTexture.desc.EnableRandomWrite=true;
        }


        private static RenderGraphAccelerationStructure CreateSceneAccelerationStructure()
        {
            return new RenderGraphAccelerationStructure
            {
                desc = RenderGraphAccelerationStructureDesc.Create("SceneRTAS")
            };
        }

        private static RenderGraphTexture CreateInputTexture(string name, GraphicsFormat format, DepthBits depthBits = DepthBits.None)
        {
            var texture = new RenderGraphTexture
            {
                desc = format == GraphicsFormat.None
                    ? RenderGraphTextureDesc.CreateDepthTarget(1, 1, depthBits)
                    : RenderGraphTextureDesc.CreateColorTarget(1, 1, format)
            };
            texture.desc.Name = name;
            texture.desc.ClearBuffer = false;
            texture.desc.EnableRandomWrite = false;
            return texture;
        }

        private static RenderGraphTexture CreateOutputTexture(string name)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R16_SFloat)
            };
            texture.desc.Name = name;
            texture.desc.ClearBuffer = true;
            texture.desc.ClearColor = Color.white;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.EnableRandomWrite = true;
            return texture;
        }
        private static RenderGraphTexture CreateDebugTexture(string name)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R32G32_SFloat)
            };
            texture.desc.Name = name;
            texture.desc.ClearBuffer = true;
            texture.desc.ClearColor = Color.white;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.EnableRandomWrite = true;
            return texture;
        }

    }
}

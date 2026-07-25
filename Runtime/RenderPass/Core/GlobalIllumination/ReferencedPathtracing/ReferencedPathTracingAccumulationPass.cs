using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// Unbounded progressive accumulation for the reference path tracer. The pass deliberately avoids
    /// reprojection and history clamping so a static camera converges to the arithmetic mean of all samples.
    /// </summary>
    public sealed class ReferencedPathTracingAccumulationPass : ComputePass, IRenderGraphSideEffectPass
    {
        internal const string KernelName = "ReferencedPathTracingAccumulation";

        private const float MatrixResetEpsilon = 1e-6f;
        private const float LightResetEpsilon = 1e-6f;

        private static readonly int SampleRadianceId = Shader.PropertyToID("_ReferencedPathTracingSampleRadiance");
        private static readonly int HistoryRadianceId = Shader.PropertyToID("_ReferencedPathTracingHistoryRadiance");
        private static readonly int HistoryWriteId = Shader.PropertyToID("_ReferencedPathTracingHistoryWrite");
        private static readonly int ResolvedColorId = Shader.PropertyToID("_ReferencedPathTracingResolvedColor");
        private static readonly int AccumulationParametersId =
            Shader.PropertyToID("_ReferencedPathTracingAccumulationParameters");
        private static readonly int ScreenSizeId = Shader.PropertyToID("_ReferencedPathTracingScreenSize");

        [RenderGraphResource(Name = "PathTracingSampleRadiance", Access = AccessFlags.Read)]
        private RenderGraphTexture m_SampleRadiance;

        private RenderGraphTexture m_AccumulationPrevious;

        private RenderGraphTexture m_AccumulationCurrent;

        [RenderGraphResource(Name = "PathTracingResolvedColor", Access = AccessFlags.WriteAll)]
        private RenderGraphTexture m_ResolvedColor;

        private sealed class AccumulationState : CameraRelativeState
        {
            public bool HasSignature;
            public int Width;
            public int Height;
            public Matrix4x4 ViewMatrix;
            public Matrix4x4 ProjectionMatrix;
            public Vector3 MainLightDirection;
            public Vector3 MainLightColor;
            public ulong LocalLightSignature;
            public ulong EnvironmentSignature;
            public ulong CameraBackgroundSignature;
            public ulong SampleCount;

            public override void Dispose()
            {
                HasSignature = false;
                SampleCount = 0;
            }
        }

        private readonly CameraRelativeSystem<AccumulationState> m_AccumulationStates = new();
        private readonly RenderGraphTextureDesc m_HistoryDescriptor =
            RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R32G32B32A32_SFloat);

        private ComputeShader m_ComputeShader;
        private int m_Kernel = -1;
        private int m_Width = 1;
        private int m_Height = 1;
        private CameraHistoryTexture m_AccumulationHistory;
        private bool m_HasValidHistory;
        private bool m_UseHistory;
        private float m_InverseSampleCount = 1.0f;

        public ReferencedPathTracingAccumulationPass()
        {
            profilingSampler = new ProfilingSampler(nameof(ReferencedPathTracingAccumulationPass));

            m_SampleRadiance = RenderGraphTexture.CreateInput(
                "PathTracingSampleRadiance",
                GraphicsFormat.R32G32B32A32_SFloat);
            m_AccumulationPrevious = RenderGraphTexture.CreateInput(
                "PathTracingAccumulationPrevious",
                GraphicsFormat.R32G32B32A32_SFloat);
            m_AccumulationCurrent = CreatePassOwnedTexture("PathTracingAccumulationCurrent");
            m_ResolvedColor = CreatePassOwnedTexture("PathTracingResolvedColor");
            ConfigureHistoryDescriptor(1, 1);
        }

        public override void Create()
        {
            m_ComputeShader = PipelineResourceManager
                .Get<VividRPCoreResources>()
                ?.ReferencedPathTracingAccumulationCompute;
            if (m_ComputeShader == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find the compute shader resource for {nameof(ReferencedPathTracingAccumulationPass)}.");
                return;
            }

            m_Kernel = m_ComputeShader.FindKernel(KernelName);
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

            ResizePassOwnedTexture(m_AccumulationCurrent, m_Width, m_Height);
            ResizePassOwnedTexture(m_ResolvedColor, m_Width, m_Height);
            ConfigureHistoryDescriptor(m_Width, m_Height);
            m_HasValidHistory = CameraHistoryRenderGraphBridge.PrepareTexturePair(
                this,
                cameraData?.camera,
                CameraHistoryIds.PathTracingAccumulation,
                m_AccumulationPrevious,
                m_AccumulationCurrent,
                m_HistoryDescriptor,
                out m_AccumulationHistory,
                AccessFlags.WriteAll);

            PrepareAccumulationState(frameData, cameraData);
            m_AccumulationStates.PurgeDestroyedCameras();
        }

        public override void Record(ComputePassContext context)
        {
            if (m_ComputeShader == null
                || m_Kernel < 0
                || m_SampleRadiance?.innerHandle.IsValid() != true
                || m_AccumulationCurrent?.innerHandle.IsValid() != true
                || m_ResolvedColor?.innerHandle.IsValid() != true)
            {
                return;
            }

            var historyHandle = m_UseHistory && m_AccumulationPrevious?.innerHandle.IsValid() == true
                ? m_AccumulationPrevious.innerHandle
                : m_SampleRadiance.innerHandle;

            var cmd = context.cmd;
            using (new ProfilingScope(cmd, profilingSampler))
            {
                cmd.SetComputeTextureParam(
                    m_ComputeShader,
                    m_Kernel,
                    SampleRadianceId,
                    m_SampleRadiance.innerHandle);
                cmd.SetComputeTextureParam(m_ComputeShader, m_Kernel, HistoryRadianceId, historyHandle);
                cmd.SetComputeTextureParam(
                    m_ComputeShader,
                    m_Kernel,
                    HistoryWriteId,
                    m_AccumulationCurrent.innerHandle);
                cmd.SetComputeTextureParam(
                    m_ComputeShader,
                    m_Kernel,
                    ResolvedColorId,
                    m_ResolvedColor.innerHandle);
                cmd.SetComputeVectorParam(
                    m_ComputeShader,
                    AccumulationParametersId,
                    new Vector4(m_InverseSampleCount, m_UseHistory ? 1.0f : 0.0f, 0.0f, 0.0f));
                cmd.SetComputeVectorParam(
                    m_ComputeShader,
                    ScreenSizeId,
                    new Vector4(m_Width, m_Height, 1.0f / m_Width, 1.0f / m_Height));
                m_AccumulationHistory?.MarkWritten();
                cmd.DispatchCompute(
                    m_ComputeShader,
                    m_Kernel,
                    CoreUtils.DivRoundUp(m_Width, 8),
                    CoreUtils.DivRoundUp(m_Height, 8),
                    1);
            }
        }

        public override void Dispose()
        {
            m_AccumulationStates.Dispose();
            m_ComputeShader = null;
            m_Kernel = -1;
            m_Width = 1;
            m_Height = 1;
            m_AccumulationHistory = null;
            m_HasValidHistory = false;
            m_UseHistory = false;
            m_InverseSampleCount = 1.0f;
        }

        private void PrepareAccumulationState(ContextContainer frameData, VividCameraData cameraData)
        {
            var camera = cameraData?.camera;
            if (camera == null)
            {
                m_UseHistory = false;
                m_InverseSampleCount = 1.0f;
                return;
            }

            var state = m_AccumulationStates.GetOrCreateBase(camera);
            var viewMatrix = cameraData.GetViewMatrix();
            var projectionMatrix = cameraData.GetProjectionMatrixNoJitter();
            ReferencedPathTracingLightSignatureUtility.Resolve(
                frameData.GetOrCreate<VividLightData>(),
                out var lightDirection,
                out var lightColor,
                out var localLightSignature);
            var environmentState = ReferencedPathTracingEnvironmentState.Resolve(
                frameData.GetOrCreate<VividSkyData>());
            var cameraBackgroundState =
                ReferencedPathTracingCameraBackgroundState.Resolve(cameraData);

            var temporalData = frameData.Get<VividTemporalData>();
            var signatureMatches = state.HasSignature
                && state.Width == m_Width
                && state.Height == m_Height
                && MatricesApproximatelyEqual(state.ViewMatrix, viewMatrix, MatrixResetEpsilon)
                && MatricesApproximatelyEqual(state.ProjectionMatrix, projectionMatrix, MatrixResetEpsilon)
                && VectorsApproximatelyEqual(state.MainLightDirection, lightDirection, LightResetEpsilon)
                && VectorsApproximatelyEqual(state.MainLightColor, lightColor, LightResetEpsilon)
                && state.LocalLightSignature == localLightSignature
                && state.EnvironmentSignature == environmentState.signature
                && state.CameraBackgroundSignature == cameraBackgroundState.signature;

            m_UseHistory = m_HasValidHistory
                && signatureMatches
                && (temporalData == null || !temporalData.isFirstFrame);

            if (m_UseHistory && state.SampleCount < ulong.MaxValue)
                state.SampleCount++;
            else if (!m_UseHistory)
                state.SampleCount = 1;

            m_InverseSampleCount = (float)(1.0 / state.SampleCount);
            state.HasSignature = true;
            state.Width = m_Width;
            state.Height = m_Height;
            state.ViewMatrix = viewMatrix;
            state.ProjectionMatrix = projectionMatrix;
            state.MainLightDirection = lightDirection;
            state.MainLightColor = lightColor;
            state.LocalLightSignature = localLightSignature;
            state.EnvironmentSignature = environmentState.signature;
            state.CameraBackgroundSignature = cameraBackgroundState.signature;
        }

        private void ConfigureHistoryDescriptor(int width, int height)
        {
            m_HistoryDescriptor.Width = Mathf.Max(1, width);
            m_HistoryDescriptor.Height = Mathf.Max(1, height);
            m_HistoryDescriptor.ColorFormat = GraphicsFormat.R32G32B32A32_SFloat;
            m_HistoryDescriptor.DepthBufferBits = DepthBits.None;
            m_HistoryDescriptor.MsaaSamples = MSAASamples.None;
            m_HistoryDescriptor.FilterMode = FilterMode.Point;
            m_HistoryDescriptor.WrapMode = TextureWrapMode.Clamp;
            m_HistoryDescriptor.ClearBuffer = false;
            m_HistoryDescriptor.UseMipMap = false;
            m_HistoryDescriptor.AutoGenerateMips = false;
            m_HistoryDescriptor.MipCount = 1;
            m_HistoryDescriptor.EnableRandomWrite = true;
            m_HistoryDescriptor.BindTextureMS = false;
            m_HistoryDescriptor.Name = "PathTracingAccumulationHistory";
        }

        private static RenderGraphTexture CreatePassOwnedTexture(string name)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R32G32B32A32_SFloat)
            };
            texture.desc.Name = name;
            texture.desc.EnableRandomWrite = true;
            texture.desc.ClearBuffer = false;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            return texture;
        }

        private static void ResizePassOwnedTexture(RenderGraphTexture texture, int width, int height)
        {
            if (texture?.desc == null)
                return;

            texture.Resize(width, height);
            texture.desc.ColorFormat = GraphicsFormat.R32G32B32A32_SFloat;
            texture.desc.EnableRandomWrite = true;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
        }

        private static bool MatricesApproximatelyEqual(Matrix4x4 lhs, Matrix4x4 rhs, float epsilon)
        {
            for (var index = 0; index < 16; index++)
            {
                if (Mathf.Abs(lhs[index] - rhs[index]) > epsilon)
                    return false;
            }

            return true;
        }

        private static bool VectorsApproximatelyEqual(Vector3 lhs, Vector3 rhs, float epsilon)
        {
            return (lhs - rhs).sqrMagnitude <= epsilon * epsilon;
        }
    }

    internal static class ReferencedPathTracingLightSignatureUtility
    {
        private const ulong FnvOffsetBasis = 14695981039346656037ul;
        private const ulong FnvPrime = 1099511628211ul;

        internal static void Resolve(
            VividLightData lightData,
            out Vector3 mainLightDirection,
            out Vector3 mainLightColor,
            out ulong localLightSignature)
        {
            mainLightDirection = Vector3.zero;
            mainLightColor = Vector3.zero;
            localLightSignature = 0ul;
            if (lightData == null)
                return;

            lightData.CompleteReGIRPrepare();
            if (lightData.hasMainDirectionalLight)
            {
                var mainLight = lightData.mainDirectionalLight;
                mainLightDirection = mainLight.directionWS.sqrMagnitude > 1e-8f
                    ? mainLight.directionWS.normalized
                    : Vector3.zero;
                mainLightColor = new Vector3(
                    Mathf.Max(mainLight.color.x, 0.0f),
                    Mathf.Max(mainLight.color.y, 0.0f),
                    Mathf.Max(mainLight.color.z, 0.0f));
            }

            var lightCount = Mathf.Clamp(
                lightData.reGIRLightCount,
                0,
                lightData.reGIRLights?.Length ?? 0);
            ulong signatureSum = 0ul;
            ulong signatureXor = 0ul;
            uint localLightCount = 0u;
            for (var lightIndex = 0; lightIndex < lightCount; lightIndex++)
            {
                var light = lightData.reGIRLights[lightIndex];
                var lightSignature = ComputeLocalLightSignature(light);
                var mixedSignature = Mix(lightSignature);
                signatureSum += mixedSignature;
                signatureXor ^= RotateLeft(mixedSignature, (int)(mixedSignature & 63ul));
                localLightCount++;
            }

            localLightSignature = Mix(
                signatureSum
                ^ signatureXor
                ^ ((ulong)localLightCount * 0x9e3779b97f4a7c15ul));
        }

        private static ulong ComputeLocalLightSignature(VividReGIRLightData light)
        {
            var signature = FnvOffsetBasis;
            Hash(ref signature, light.lightType);
            Hash(ref signature, light.positionWS);
            Hash(ref signature, light.range);
            Hash(ref signature, light.color);
            Hash(ref signature, light.shapeRadius);

            if (light.lightType == VividReGIRLightData.TypeSpot)
            {
                Hash(ref signature, light.directionWS);
                Hash(ref signature, light.angleScale);
                Hash(ref signature, light.angleOffset);
            }
            else if (light.lightType == VividReGIRLightData.TypeRectangle)
            {
                Hash(ref signature, light.directionWS);
                Hash(ref signature, light.rightWS);
                Hash(ref signature, light.upWS);
                Hash(ref signature, light.areaSize);
                Hash(ref signature, light.cosBarnDoorAngle);
                Hash(ref signature, light.barnDoorLength);
            }
            else if (light.lightType == VividReGIRLightData.TypeTube)
            {
                Hash(ref signature, light.rightWS);
                Hash(ref signature, light.areaSize);
            }

            return signature;
        }

        private static void Hash(ref ulong signature, Vector3 value)
        {
            Hash(ref signature, value.x);
            Hash(ref signature, value.y);
            Hash(ref signature, value.z);
        }

        private static void Hash(ref ulong signature, Vector2 value)
        {
            Hash(ref signature, value.x);
            Hash(ref signature, value.y);
        }

        private static void Hash(ref ulong signature, float value)
        {
            Hash(ref signature, unchecked((uint)value.GetHashCode()));
        }

        private static void Hash(ref ulong signature, uint value)
        {
            signature ^= value;
            signature *= FnvPrime;
        }

        private static ulong Mix(ulong value)
        {
            value ^= value >> 30;
            value *= 0xbf58476d1ce4e5b9ul;
            value ^= value >> 27;
            value *= 0x94d049bb133111ebul;
            value ^= value >> 31;
            return value;
        }

        private static ulong RotateLeft(ulong value, int bitCount)
        {
            if (bitCount == 0)
                return value;

            return (value << bitCount) | (value >> (64 - bitCount));
        }
    }
}

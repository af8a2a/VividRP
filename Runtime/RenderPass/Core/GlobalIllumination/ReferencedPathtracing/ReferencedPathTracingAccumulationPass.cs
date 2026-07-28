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
            public float MainLightAngularDiameter;
            public float MainLightShadowStrength;
            public ulong LocalLightSignature;
            public ulong EnvironmentSignature;
            public ulong AtmosphereSignature;
            public ulong CameraBackgroundSignature;
            public ulong IntegratorSignature;
            public ulong FrameSignature;
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
            var pathTracingData =
                frameData.GetOrCreate<VividReferencedPathTracingData>();
            var integratorState =
                ReferencedPathTracingIntegratorState.Resolve();
            var effectiveIntegratorSignature = pathTracingData.isValid
                ? pathTracingData.integratorSignature
                : integratorState.ResolveEffectiveSignature(
                    integratorState.pathSamplingMode);
            var viewMatrix = cameraData.GetViewMatrix();
            var projectionMatrix = cameraData.GetProjectionMatrixNoJitter();
            ReferencedPathTracingLightSignatureUtility.Resolve(
                frameData.GetOrCreate<VividLightData>(),
                out var lightDirection,
                out var lightColor,
                out var lightAngularDiameter,
                out var lightShadowStrength,
                out var localLightSignature);
            var environmentState = ReferencedPathTracingEnvironmentState.Resolve(
                frameData.GetOrCreate<VividSkyData>());
            var atmosphereState =
                ReferencedPathTracingAtmosphereState.Resolve(frameData);
            var cameraBackgroundState =
                ReferencedPathTracingCameraBackgroundState.Resolve(cameraData);

            // VividExposureData is deliberately excluded. History contains raw scene-linear
            // radiance; only m_ResolvedColor is converted to the frame's pre-exposed domain.
            var temporalData = frameData.Get<VividTemporalData>();
            var signatureMatches = state.HasSignature
                && state.Width == m_Width
                && state.Height == m_Height
                && MatricesApproximatelyEqual(state.ViewMatrix, viewMatrix, MatrixResetEpsilon)
                && MatricesApproximatelyEqual(state.ProjectionMatrix, projectionMatrix, MatrixResetEpsilon)
                && VectorsApproximatelyEqual(state.MainLightDirection, lightDirection, LightResetEpsilon)
                && VectorsApproximatelyEqual(state.MainLightColor, lightColor, LightResetEpsilon)
                && Mathf.Abs(state.MainLightAngularDiameter - lightAngularDiameter)
                    <= LightResetEpsilon
                && Mathf.Abs(state.MainLightShadowStrength - lightShadowStrength)
                    <= LightResetEpsilon
                && state.LocalLightSignature == localLightSignature
                && state.EnvironmentSignature == environmentState.signature
                && state.AtmosphereSignature == atmosphereState.signature
                && state.CameraBackgroundSignature == cameraBackgroundState.signature
                && state.IntegratorSignature
                    == effectiveIntegratorSignature
                && (!pathTracingData.isValid
                    || state.FrameSignature
                        == pathTracingData.frameSignature);
            var sampleSequenceMatches =
                !pathTracingData.isValid
                || pathTracingData.sampleIndex == state.SampleCount;

            m_UseHistory = m_HasValidHistory
                && signatureMatches
                && sampleSequenceMatches
                && (temporalData == null || !temporalData.isFirstFrame);

            if (pathTracingData.isValid)
            {
                if (!m_HasValidHistory && pathTracingData.sampleIndex > 0)
                    ReferencedPathTracingSampleSequence.RequestReset(camera);

                state.SampleCount = m_UseHistory
                    ? (ulong)pathTracingData.sampleIndex + 1ul
                    : 1ul;
            }
            else
            {
                if (m_UseHistory && state.SampleCount < ulong.MaxValue)
                    state.SampleCount++;
                else if (!m_UseHistory)
                    state.SampleCount = 1;
            }

            m_InverseSampleCount = (float)(1.0 / state.SampleCount);
            pathTracingData.accumulatedSampleCount = state.SampleCount;
            state.HasSignature = true;
            state.Width = m_Width;
            state.Height = m_Height;
            state.ViewMatrix = viewMatrix;
            state.ProjectionMatrix = projectionMatrix;
            state.MainLightDirection = lightDirection;
            state.MainLightColor = lightColor;
            state.MainLightAngularDiameter = lightAngularDiameter;
            state.MainLightShadowStrength = lightShadowStrength;
            state.LocalLightSignature = localLightSignature;
            state.EnvironmentSignature = environmentState.signature;
            state.AtmosphereSignature = atmosphereState.signature;
            state.CameraBackgroundSignature = cameraBackgroundState.signature;
            state.IntegratorSignature = effectiveIntegratorSignature;
            state.FrameSignature = pathTracingData.isValid
                ? pathTracingData.frameSignature
                : 0ul;
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
        private const float MainLightDeltaSinSquaredThreshold = 1e-12f;

        internal static bool HasFiniteMainLightSolidAngle(
            float angularDiameter)
        {
            var halfAngularDiameter = 0.5f * Mathf.Clamp(
                angularDiameter,
                0.0f,
                0.5f * Mathf.PI);
            var sinThetaMax = Mathf.Sin(halfAngularDiameter);
            return sinThetaMax * sinThetaMax
                > MainLightDeltaSinSquaredThreshold;
        }

        internal static void Resolve(
            VividLightData lightData,
            out Vector3 mainLightDirection,
            out Vector3 mainLightColor,
            out float mainLightAngularDiameter,
            out float mainLightShadowStrength,
            out ulong localLightSignature)
        {
            mainLightDirection = Vector3.zero;
            mainLightColor = Vector3.zero;
            mainLightAngularDiameter = 0.0f;
            mainLightShadowStrength = 0.0f;
            localLightSignature = 0ul;
            if (lightData == null)
                return;

            lightData.CompleteLightGridPrepare();
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
                mainLightAngularDiameter = Mathf.Clamp(
                    mainLight.angularDiameter,
                    0.0f,
                    0.5f * Mathf.PI);
                mainLightShadowStrength = Mathf.Clamp01(
                    mainLight.shadowStrength);
            }

            var lightDatabase = VividLightRenderDatabase.instance;
            lightDatabase.CompleteSceneLightPrepare();
            var buildResult = ReferencedPathTracingLightListBuilder.Build(
                lightDatabase.sceneLightData);
            localLightSignature =
                ((ulong)buildResult.parameters.signatureHigh << 32)
                | buildResult.parameters.signatureLow;
        }

    }
}

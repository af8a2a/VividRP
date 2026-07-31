using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Serialization;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// Asynchronously denoises reference path-tracing radiance through Unity's Open Image Denoise
    /// package, using primary diffuse-albedo and shading-normal AOVs from the path tracer. A request
    /// is submitted only after the configured target sample count is reached. Until that request
    /// completes, the scene-linear radiance input is used; the completed result remains stable for
    /// the rest of the accumulation cycle.
    /// </summary>
    public sealed class ReferencedPathTracingDenoisingPass : UnsafePass, IRenderGraphSideEffectPass
    {
        private const float MatrixResetEpsilon = 1e-6f;
        private const float LightResetEpsilon = 1e-6f;

        [RenderGraphResource(Name = "PathTracingRadiance", Access = AccessFlags.Read)]
        [FormerlySerializedAs("m_AccumulatedColor")]
        private RenderGraphTexture m_Radiance;

        [RenderGraphResource(Name = "PathTracingAlbedo", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Albedo;

        [RenderGraphResource(Name = "PathTracingNormal", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Normal;

        [RenderGraphResource(Name = "PathTracingDenoisedRadiance", Access = AccessFlags.WriteAll)]
        [PassBypass(nameof(m_Radiance))]
        [FormerlySerializedAs("m_DenoisedColor")]
        private RenderGraphTexture m_DenoisedRadiance;

        private sealed class DenoisingState : CameraRelativeState
        {
            public IReferencedPathTracingDenoiserBackend Backend;
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
            public ulong IntegratorSignature;
            public ulong FrameSignature;
            public int TargetSampleCount;
            public ulong AccumulatedSampleCount;
            public uint SampleIndex;
            public int RenderFrameIndex;

            public override void Dispose()
            {
                Backend?.Dispose();
                Backend = null;
                HasSignature = false;
            }
        }

        private readonly CameraRelativeSystem<DenoisingState> m_DenoisingStates = new();
        private readonly Func<IReferencedPathTracingDenoiserBackend> m_BackendFactory;
        private DenoisingState m_CurrentState;
        private int m_Width = 1;
        private int m_Height = 1;
        private bool m_IsDenoisingReady;

        public ReferencedPathTracingDenoisingPass()
            : this(ReferencedPathTracingDenoiserBackendFactory.Create)
        {
        }

        internal ReferencedPathTracingDenoisingPass(
            Func<IReferencedPathTracingDenoiserBackend> backendFactory)
        {
            m_BackendFactory = backendFactory
                ?? throw new ArgumentNullException(nameof(backendFactory));
            profilingSampler = new ProfilingSampler(nameof(ReferencedPathTracingDenoisingPass));

            m_Radiance = RenderGraphTexture.CreateInput(
                "PathTracingRadiance",
                GraphicsFormat.R32G32B32A32_SFloat);
            m_Albedo = RenderGraphTexture.CreateInput(
                "PathTracingAlbedo",
                GraphicsFormat.R32G32B32A32_SFloat);
            m_Normal = RenderGraphTexture.CreateInput(
                "PathTracingNormal",
                GraphicsFormat.R32G32B32A32_SFloat);
            m_DenoisedRadiance = RenderGraphTexture.CreateOutput(
                "PathTracingDenoisedRadiance",
                GraphicsFormat.R32G32B32A32_SFloat);
            ConfigureOutputDescriptor();
        }

        public override void Create()
        {
        }

        public override bool IsActive(ContextContainer frameData)
        {
            return ReferencedPathTracingDenoiserBackendFactory.IsPlatformSupported;
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

            m_DenoisedRadiance.Resize(m_Width, m_Height);
            ConfigureOutputDescriptor();
            PrepareDenoisingState(frameData, cameraData);
            m_DenoisingStates.PurgeDestroyedCameras();
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_Radiance?.innerHandle.IsValid() != true
                || m_Albedo?.innerHandle.IsValid() != true
                || m_Normal?.innerHandle.IsValid() != true
                || m_DenoisedRadiance?.innerHandle.IsValid() != true)
            {
                return;
            }

            var radiance = (RenderTexture)m_Radiance;
            var albedo = (RenderTexture)m_Albedo;
            var normal = (RenderTexture)m_Normal;
            var destination = (RenderTexture)m_DenoisedRadiance;
            if (radiance == null
                || albedo == null
                || normal == null
                || destination == null
                || !HasMatchingDimensions(radiance, destination)
                || !HasMatchingDimensions(albedo, destination)
                || !HasMatchingDimensions(normal, destination))
            {
                return;
            }

            var commandBuffer = context.GetNativeCommandBuffer();
            using (new ProfilingScope(commandBuffer, profilingSampler))
            {
                // The pass always has a deterministic output while OIDN readback/CPU work is pending.
                commandBuffer.CopyTexture(radiance, destination);
                if (m_IsDenoisingReady)
                {
                    m_CurrentState?.Backend?.Process(
                        commandBuffer,
                        radiance,
                        albedo,
                        normal,
                        destination,
                        m_Width,
                        m_Height);
                }
            }
        }

        public override void Dispose()
        {
            m_DenoisingStates.Dispose();
            m_CurrentState = null;
            m_Width = 1;
            m_Height = 1;
            m_IsDenoisingReady = false;
        }

        private void PrepareDenoisingState(ContextContainer frameData, VividCameraData cameraData)
        {
            var camera = cameraData?.camera;
            if (camera == null)
            {
                m_CurrentState = null;
                m_IsDenoisingReady = false;
                return;
            }

            var state = m_DenoisingStates.GetOrCreateBase(camera);
            state.Backend ??= m_BackendFactory();

            var viewMatrix = cameraData.GetViewMatrix();
            var projectionMatrix = cameraData.GetProjectionMatrixNoJitter();
            ReferencedPathTracingLightSignatureUtility.Resolve(
                frameData.GetOrCreate<VividLightData>(),
                out var lightDirection,
                out var lightColor,
                out var lightAngularDiameter,
                out var lightShadowStrength,
                out var localLightSignature);
            var pathTracingData =
                frameData.GetOrCreate<VividReferencedPathTracingData>();
            var renderFrameIndex = cameraData.frameIndex >= 0
                ? cameraData.frameIndex
                : Time.frameCount;

            if (!pathTracingData.isValid)
            {
                if (state.HasSignature)
                    state.Backend?.Invalidate();

                state.HasSignature = false;
                state.AccumulatedSampleCount = 0;
                m_CurrentState = state;
                m_IsDenoisingReady = false;
                return;
            }

            var accumulationRestarted = state.HasSignature
                && (pathTracingData.accumulatedSampleCount
                        < state.AccumulatedSampleCount
                    || pathTracingData.sampleIndex < state.SampleIndex
                    || (pathTracingData.sampleIndex == 0
                        && state.SampleIndex == 0
                        && state.AccumulatedSampleCount > 0
                        && state.RenderFrameIndex != renderFrameIndex));

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
                && state.IntegratorSignature
                    == pathTracingData.integratorSignature
                && state.FrameSignature
                    == pathTracingData.frameSignature
                && state.TargetSampleCount
                    == pathTracingData.targetSampleCount
                && !accumulationRestarted;

            if (!signatureMatches)
                state.Backend?.Invalidate();

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
            state.IntegratorSignature = pathTracingData.integratorSignature;
            state.FrameSignature = pathTracingData.frameSignature;
            state.TargetSampleCount = pathTracingData.targetSampleCount;
            state.AccumulatedSampleCount =
                pathTracingData.accumulatedSampleCount;
            state.SampleIndex = pathTracingData.sampleIndex;
            state.RenderFrameIndex = renderFrameIndex;
            m_CurrentState = state;
            m_IsDenoisingReady =
                ReferencedPathTracingDenoiserRequestPolicy
                    .HasReachedSampleTarget(
                        pathTracingData.isValid,
                        pathTracingData.accumulatedSampleCount,
                        pathTracingData.targetSampleCount);
        }

        private void ConfigureOutputDescriptor()
        {
            var descriptor = m_DenoisedRadiance.desc;
            descriptor.ColorFormat = GraphicsFormat.R32G32B32A32_SFloat;
            descriptor.DepthBufferBits = DepthBits.None;
            descriptor.MsaaSamples = MSAASamples.None;
            descriptor.FilterMode = FilterMode.Point;
            descriptor.WrapMode = TextureWrapMode.Clamp;
            descriptor.ClearBuffer = false;
            descriptor.EnableRandomWrite = false;
            descriptor.UseMipMap = false;
            descriptor.AutoGenerateMips = false;
            descriptor.MipCount = 1;
            descriptor.Name = "PathTracingDenoisedRadiance";
        }

        private static bool HasMatchingDimensions(
            RenderTexture texture,
            RenderTexture reference)
        {
            return texture.width == reference.width
                && texture.height == reference.height;
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
}

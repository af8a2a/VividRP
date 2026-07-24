using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// Asynchronously denoises the progressively accumulated reference path-tracing color through
    /// Unity's Open Image Denoise package. Until a request completes, the accumulated input is used.
    /// </summary>
    public sealed class ReferencedPathTracingDenoisingPass : UnsafePass, IRenderGraphSideEffectPass
    {
        private const float MatrixResetEpsilon = 1e-6f;
        private const float LightResetEpsilon = 1e-6f;

        [RenderGraphResource(Name = "PathTracingAccumulatedColor", Access = AccessFlags.Read)]
        private RenderGraphTexture m_AccumulatedColor;

        [RenderGraphResource(Name = "PathTracingDenoisedColor", Access = AccessFlags.WriteAll)]
        [PassBypass(nameof(m_AccumulatedColor))]
        private RenderGraphTexture m_DenoisedColor;

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
            public ulong LocalLightSignature;

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

            m_AccumulatedColor = RenderGraphTexture.CreateInput(
                "PathTracingAccumulatedColor",
                GraphicsFormat.R32G32B32A32_SFloat);
            m_DenoisedColor = RenderGraphTexture.CreateOutput(
                "PathTracingDenoisedColor",
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

            m_DenoisedColor.Resize(m_Width, m_Height);
            ConfigureOutputDescriptor();
            PrepareDenoisingState(frameData, cameraData);
            m_DenoisingStates.PurgeDestroyedCameras();
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_AccumulatedColor?.innerHandle.IsValid() != true
                || m_DenoisedColor?.innerHandle.IsValid() != true)
            {
                return;
            }

            var source = (RenderTexture)m_AccumulatedColor;
            var destination = (RenderTexture)m_DenoisedColor;
            if (source == null
                || destination == null
                || source.width != destination.width
                || source.height != destination.height)
            {
                return;
            }

            var commandBuffer = context.GetNativeCommandBuffer();
            using (new ProfilingScope(commandBuffer, profilingSampler))
            {
                // The pass always has a deterministic output while OIDN readback/CPU work is pending.
                commandBuffer.CopyTexture(source, destination);
                m_CurrentState?.Backend?.Process(
                    commandBuffer,
                    source,
                    destination,
                    m_Width,
                    m_Height);
            }
        }

        public override void Dispose()
        {
            m_DenoisingStates.Dispose();
            m_CurrentState = null;
            m_Width = 1;
            m_Height = 1;
        }

        private void PrepareDenoisingState(ContextContainer frameData, VividCameraData cameraData)
        {
            var camera = cameraData?.camera;
            if (camera == null)
            {
                m_CurrentState = null;
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
                out var localLightSignature);

            var signatureMatches = state.HasSignature
                && state.Width == m_Width
                && state.Height == m_Height
                && MatricesApproximatelyEqual(state.ViewMatrix, viewMatrix, MatrixResetEpsilon)
                && MatricesApproximatelyEqual(state.ProjectionMatrix, projectionMatrix, MatrixResetEpsilon)
                && VectorsApproximatelyEqual(state.MainLightDirection, lightDirection, LightResetEpsilon)
                && VectorsApproximatelyEqual(state.MainLightColor, lightColor, LightResetEpsilon)
                && state.LocalLightSignature == localLightSignature;

            if (!signatureMatches)
                state.Backend?.Invalidate();

            state.HasSignature = true;
            state.Width = m_Width;
            state.Height = m_Height;
            state.ViewMatrix = viewMatrix;
            state.ProjectionMatrix = projectionMatrix;
            state.MainLightDirection = lightDirection;
            state.MainLightColor = lightColor;
            state.LocalLightSignature = localLightSignature;
            m_CurrentState = state;
        }

        private void ConfigureOutputDescriptor()
        {
            var descriptor = m_DenoisedColor.desc;
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
            descriptor.Name = "PathTracingDenoisedColor";
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

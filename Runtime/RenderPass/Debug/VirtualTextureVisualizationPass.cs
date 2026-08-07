using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.VirtualTexture;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class VirtualTextureVisualizationPass : RasterPass
    {
        internal const string VirtualTextureVisualizationShaderName = "Hidden/VividRP/VirtualTextureVisualization";
        internal const float MinOverlayViewportFraction = 0.35f;

        private static readonly int SourceTextureId = Shader.PropertyToID("_SourceTexture");
        private static readonly int SourceTextureScaleBiasId = Shader.PropertyToID("_SourceTextureScaleBias");
        private static readonly int OverlayRectId = Shader.PropertyToID("_VTOverlayRect");
        private static readonly int OverlayOpacityId = Shader.PropertyToID("_VTOverlayOpacity");
        private static readonly int VisualizationModeId = Shader.PropertyToID("_VTVisualizationMode");
        private static readonly int VisualizationLayerId = Shader.PropertyToID("_VTVisualizationLayer");
        private static readonly int VisualizationAvailableId = Shader.PropertyToID("_VTVisualizationAvailable");
        private static readonly int VisualizationSpaceId = Shader.PropertyToID("_VTVisualizationSpaceId");

        [RenderGraphResource(Name = "SourceTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_SourceTexture;

        [RenderGraphResource(
            Name = "OutputTexture",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        private readonly float[] m_SpaceParams = new float[VirtualTextureSpaceShaderParams.IntCount];
        private readonly float[] m_MipOffsets = new float[VirtualTextureFeedbackProcessor.MaxMipCount];
        private readonly Vector4[] m_LayerFallbacks = new Vector4[VTStackDesc.MaxLayerCount];

        private Material m_Material;
        private VividVirtualTextureFrameData m_VirtualTextureFrameData;
        private VirtualTextureVisualizationMode m_ResolvedVisualizationMode = VirtualTextureVisualizationMode.None;
        private VirtualTextureVisualizationTarget m_ResolvedVisualizationTarget = VirtualTextureVisualizationTarget.Auto;
        private VirtualTextureVisualizationLayer m_ResolvedVisualizationLayer = VirtualTextureVisualizationLayer.BaseColor;
        private Vector4 m_OverlayRect = new(0.65f, 0.65f, MinOverlayViewportFraction, MinOverlayViewportFraction);
        private float m_ResolvedOpacity = 1f;
        private bool m_ShouldSkipExecution;

        public VirtualTextureVisualizationPass()
        {
            profilingSampler = new ProfilingSampler(nameof(VirtualTextureVisualizationPass));
            m_SourceTexture = RenderGraphTexture.CreateInput("SourceTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_OutputTexture = RenderGraphTexture.CreateColorTarget("OutputTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_OutputTexture.desc.ClearBuffer = false;
        }

        public override void Create()
        {
            Shader shader = Shader.Find(VirtualTextureVisualizationShaderName);
            if (shader == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find shader '{VirtualTextureVisualizationShaderName}' for {nameof(VirtualTextureVisualizationPass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_VirtualTextureFrameData = frameData?.GetOrCreate<VividVirtualTextureFrameData>();
            VirtualTextureSystem.RegisterPageTableReadDependencies(this, m_VirtualTextureFrameData);
            VividRenderingDebugSettingsData debugData = VividRenderingDebugDisplaySettings.Data;
            m_ResolvedVisualizationMode = ResolveVisualizationMode(debugData);
            m_ResolvedVisualizationTarget = debugData?.virtualTextureVisualizationTarget
                ?? VirtualTextureVisualizationTarget.Auto;
            m_ResolvedVisualizationLayer = debugData?.virtualTextureVisualizationLayer
                ?? VirtualTextureVisualizationLayer.BaseColor;
            m_ResolvedOpacity = debugData?.virtualTextureVisualizationOpacity ?? 1f;
            m_OverlayRect = ResolveOverlayRect(
                debugData?.virtualTextureVisualizationOverlayAmount ?? 0f);

            VividCameraData cameraData = frameData?.GetOrCreate<VividCameraData>();
            m_ShouldSkipExecution = DebugPassCameraUtility.ShouldSkipExecution(cameraData);
            int width = RenderGraphTextureDescUtility.ResolveMaxExplicitWidth(
                cameraData?.actualWidth ?? 0,
                cameraData?.pixelWidth ?? 0,
                Screen.width,
                m_SourceTexture?.desc);
            int height = RenderGraphTextureDescUtility.ResolveMaxExplicitHeight(
                cameraData?.actualHeight ?? 0,
                cameraData?.pixelHeight ?? 0,
                Screen.height,
                m_SourceTexture?.desc);

            ConfigureOutputTexture(width, height, m_SourceTexture?.desc);
        }

        public override void Record(RasterPassContext context)
        {
            if (m_ShouldSkipExecution)
            {
                DebugPassCameraUtility.TryPassThrough(context, m_SourceTexture, m_OutputTexture);
                return;
            }

            if (m_Material == null || !m_OutputTexture.innerHandle.IsValid())
                return;

            Texture sourceTexture = TextureResolveUtility.ResolveTexture(m_SourceTexture?.innerHandle);
            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            mpb.SetTexture(SourceTextureId, sourceTexture != null ? sourceTexture : Texture2D.blackTexture);
            mpb.SetVector(SourceTextureScaleBiasId, TextureScaleBiasUtility.GetScaleBias(m_SourceTexture?.innerHandle));
            mpb.SetVector(OverlayRectId, m_OverlayRect);
            mpb.SetFloat(OverlayOpacityId, m_ResolvedOpacity);
            mpb.SetInt(VisualizationModeId, (int)m_ResolvedVisualizationMode);
            mpb.SetInt(VisualizationLayerId, (int)m_ResolvedVisualizationLayer);

            VirtualTextureSpaceBinding binding = default;
            int gpuDrivenAllocationId = VividGPUDrivenSystem.TryGetVirtualTextureAllocationId(out int allocationId)
                ? allocationId
                : 0;
            bool hasBinding = TryResolveVisualizationBinding(
                m_VirtualTextureFrameData,
                m_ResolvedVisualizationTarget,
                gpuDrivenAllocationId,
                out binding);
            mpb.SetInt(VisualizationAvailableId, hasBinding ? 1 : 0);
            mpb.SetInt(VisualizationSpaceId, hasBinding ? binding.SpaceId : 0);

            if (hasBinding)
                BindSpaceProperties(mpb, binding);

            CoreUtils.DrawFullScreen(context.cmd, m_Material, mpb, 0);
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }

            m_VirtualTextureFrameData = null;
            m_ShouldSkipExecution = false;
        }

        internal static VirtualTextureVisualizationMode ResolveVisualizationMode(
            VividRenderingDebugSettingsData data)
        {
            return data?.virtualTextureVisualizationMode ?? VirtualTextureVisualizationMode.None;
        }

        internal static Vector4 ResolveOverlayRect(float overlayAmount)
        {
            float normalizedOverlayAmount = Mathf.Clamp01(overlayAmount);
            float size = Mathf.Lerp(MinOverlayViewportFraction, 1f, normalizedOverlayAmount);
            return new Vector4(1f - size, 1f - size, size, size);
        }

        internal static bool TryResolveVisualizationBinding(
            VividVirtualTextureFrameData frameData,
            VirtualTextureVisualizationTarget target,
            int gpuDrivenAllocationId,
            out VirtualTextureSpaceBinding binding)
        {
            binding = default;
            if (frameData == null)
                return false;

            switch (target)
            {
                case VirtualTextureVisualizationTarget.GPUDriven:
                    return TryGetGPUDrivenBinding(frameData, gpuDrivenAllocationId, out binding);
                case VirtualTextureVisualizationTarget.FirstPublic:
                    return TryGetFirstValidBinding(frameData, includePrivateSpaces: false, out binding);
                case VirtualTextureVisualizationTarget.FirstAvailable:
                    return TryGetFirstValidBinding(frameData, includePrivateSpaces: true, out binding);
                default:
                    if (TryGetGPUDrivenBinding(frameData, gpuDrivenAllocationId, out binding))
                        return true;
                    if (TryGetFirstValidBinding(frameData, includePrivateSpaces: false, out binding))
                        return true;
                    return TryGetFirstValidBinding(frameData, includePrivateSpaces: true, out binding);
            }
        }

        private static bool TryGetGPUDrivenBinding(
            VividVirtualTextureFrameData frameData,
            int gpuDrivenAllocationId,
            out VirtualTextureSpaceBinding binding)
        {
            if (gpuDrivenAllocationId > 0
                && frameData.TryGetBindingForAllocation(gpuDrivenAllocationId, out binding)
                && binding.IsValid)
            {
                return true;
            }

            IReadOnlyList<VirtualTextureSpaceBinding> bindings = frameData.Bindings;
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                VirtualTextureSpaceBinding candidate = bindings[bindingIndex];
                if (!candidate.IsValid
                    || !string.Equals(
                        candidate.SpaceName,
                        VirtualTextureGPUDrivenTextureBackend.SpaceName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                binding = candidate;
                return true;
            }

            binding = default;
            return false;
        }

        private static bool TryGetFirstValidBinding(
            VividVirtualTextureFrameData frameData,
            bool includePrivateSpaces,
            out VirtualTextureSpaceBinding binding)
        {
            IReadOnlyList<VirtualTextureSpaceBinding> bindings = frameData.Bindings;
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                VirtualTextureSpaceBinding candidate = bindings[bindingIndex];
                if (!candidate.IsValid || (!includePrivateSpaces && candidate.PrivateSpace))
                    continue;

                binding = candidate;
                return true;
            }

            binding = default;
            return false;
        }

        private void BindSpaceProperties(MaterialPropertyBlock mpb, in VirtualTextureSpaceBinding binding)
        {
            Array.Clear(m_SpaceParams, 0, m_SpaceParams.Length);
            Array.Clear(m_MipOffsets, 0, m_MipOffsets.Length);
            Array.Clear(m_LayerFallbacks, 0, m_LayerFallbacks.Length);

            binding.ShaderParams.CopyTo(m_SpaceParams);

            int[] mipOffsets = binding.MipOffsets;
            if (mipOffsets != null)
            {
                for (int mipIndex = 0; mipIndex < mipOffsets.Length && mipIndex < m_MipOffsets.Length; mipIndex++)
                    m_MipOffsets[mipIndex] = mipOffsets[mipIndex];
            }

            Vector4[] layerFallbacks = binding.LayerFallbacks;
            if (layerFallbacks != null)
            {
                for (int layerIndex = 0; layerIndex < layerFallbacks.Length && layerIndex < m_LayerFallbacks.Length; layerIndex++)
                    m_LayerFallbacks[layerIndex] = layerFallbacks[layerIndex];
            }

            mpb.SetBuffer(VirtualTextureShaderIDs._VTPageTable, binding.PageTableBuffer);
            BindPhysicalCaches(mpb, binding);
            mpb.SetFloatArray(VirtualTextureShaderIDs._VTSpaceParams, m_SpaceParams);
            mpb.SetFloatArray(VirtualTextureShaderIDs._VTMipOffsets, m_MipOffsets);
            mpb.SetVectorArray(VirtualTextureShaderIDs._VTLayerFallbacks, m_LayerFallbacks);
            mpb.SetFloat(
                VirtualTextureShaderIDs._VTAdaptiveMipBias,
                m_VirtualTextureFrameData.AdaptiveMipBias);
        }

        private static void BindPhysicalCaches(MaterialPropertyBlock mpb, in VirtualTextureSpaceBinding binding)
        {
            Texture2D fallback = binding.PhysicalCache;
            var physicalCaches = binding.PhysicalCaches;
            int[] shaderIds = VirtualTextureShaderIDs.PhysicalCaches;
            for (int physicalGroup = 0; physicalGroup < shaderIds.Length; physicalGroup++)
            {
                Texture2D cache = physicalCaches != null && physicalGroup < physicalCaches.Count
                    ? physicalCaches[physicalGroup]
                    : null;
                mpb.SetTexture(shaderIds[physicalGroup], cache != null ? cache : fallback);
            }
        }

        private void ConfigureOutputTexture(int width, int height, RenderGraphTextureDesc sourceDescriptor)
        {
            if (m_OutputTexture?.desc == null)
                return;

            m_OutputTexture.desc.Width = width;
            m_OutputTexture.desc.Height = height;
            m_OutputTexture.desc.ColorFormat = RenderGraphTextureDescUtility.ResolveColorFormat(sourceDescriptor);
            m_OutputTexture.desc.DepthBufferBits = DepthBits.None;
            m_OutputTexture.desc.MsaaSamples = MSAASamples.None;
            m_OutputTexture.desc.FilterMode = sourceDescriptor?.FilterMode ?? FilterMode.Bilinear;
            m_OutputTexture.desc.WrapMode = sourceDescriptor?.WrapMode ?? TextureWrapMode.Clamp;
            m_OutputTexture.desc.ClearBuffer = false;
            m_OutputTexture.desc.UseMipMap = false;
            m_OutputTexture.desc.AutoGenerateMips = false;
            m_OutputTexture.desc.MipCount = 1;
            m_OutputTexture.desc.EnableRandomWrite = false;
            m_OutputTexture.desc.BindTextureMS = false;
            m_OutputTexture.desc.Name = "OutputTexture";

            if (sourceDescriptor == null)
                return;

            m_OutputTexture.desc.Dimension = sourceDescriptor.Dimension;
            m_OutputTexture.desc.Slices = Mathf.Max(1, sourceDescriptor.Slices);
            m_OutputTexture.desc.UseDynamicScale = sourceDescriptor.UseDynamicScale;
            m_OutputTexture.desc.UseDynamicScaleExplicit = sourceDescriptor.UseDynamicScaleExplicit;
            m_OutputTexture.desc.ScaleFactor = sourceDescriptor.ScaleFactor;
        }

    }
}

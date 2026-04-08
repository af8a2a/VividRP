using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class AtmosphereLUTPass : ComputePass
    {
        private enum SkyLutRebuildReason
        {
            None,
            MissingTexture,
            DependenciesChanged,
            CameraChanged,
            ParametersChanged
        }

        internal const int TransmittanceWidth = 256;
        internal const int TransmittanceHeight = 64;
        internal const int MultiScatteringWidth = 32;
        internal const int MultiScatteringHeight = 32;
        internal const int SkyViewWidth = 192;
        internal const int SkyViewHeight = 108;
        internal const int SkyViewHistoryLayerCount = 4;

        private const string ComputePathWarning = "[VividRP] Atmosphere LUT compute shader is missing. Re-sync PipelineResources after Unity imports the new compute asset.";
        private const string TransmittanceKernelName = "TransmittanceLUT";
        private const string MultiScatteringKernelName = "MultiScatteringLUT";
        private const string SkyViewKernelName = "SkyViewLUT";
        private const string SkyViewSelectHistoryKernelName = "SkyViewLUTSelectHistoryLayer";
        private const string SkyViewStoreHistoryKernelName = "SkyViewLUTStoreHistory";
        private const string TransmittanceTextureName = "VividSkyTransmittanceLUT";
        private const string MultiScatteringTextureName = "VividSkyMultiScatteringLUT";
        private const string SkyViewTextureName = "VividSkySkyViewLUT";
        private const string SkyViewHistoryTextureKey = "SkyViewHistoryLayers";
        private const string SkyViewHistoryMetaKey = "SkyViewHistoryMeta";
        private const int SkyViewHistoryMetaStride = sizeof(uint) * 4 + sizeof(float) * 8;
        private const int SkyViewHistorySelectionStride = sizeof(uint) * 4;

        private static readonly ProfilingSampler s_TransmittanceMissingTextureSampler = new("AtmosphereLUTPass.RebuildTransmittance (MissingTexture)");
        private static readonly ProfilingSampler s_TransmittanceParametersChangedSampler = new("AtmosphereLUTPass.RebuildTransmittance (ParametersChanged)");
        private static readonly ProfilingSampler s_MultiScatteringMissingTextureSampler = new("AtmosphereLUTPass.RebuildMultiScattering (MissingTexture)");
        private static readonly ProfilingSampler s_MultiScatteringParametersChangedSampler = new("AtmosphereLUTPass.RebuildMultiScattering (ParametersChanged)");
        private static readonly ProfilingSampler s_SkyViewMissingTextureSampler = new("AtmosphereLUTPass.RebuildSkyView (MissingTexture)");
        private static readonly ProfilingSampler s_SkyViewDependenciesChangedSampler = new("AtmosphereLUTPass.RebuildSkyView (DependenciesChanged)");
        private static readonly ProfilingSampler s_SkyViewCameraChangedSampler = new("AtmosphereLUTPass.RebuildSkyView (CameraChanged)");
        private static readonly ProfilingSampler s_SkyViewParametersChangedSampler = new("AtmosphereLUTPass.RebuildSkyView (ParametersChanged)");

        private static readonly int SkyCameraPositionPsId = Shader.PropertyToID("_SkyCameraPositionPS");
        private static readonly int SkySunDirectionId = Shader.PropertyToID("_SkySunDirection");
        private static readonly int SkySunColorId = Shader.PropertyToID("_SkySunColor");
        private static readonly int SkyPlanetParamsId = Shader.PropertyToID("_SkyPlanetParams");
        private static readonly int SkyAirScatteringId = Shader.PropertyToID("_SkyAirScattering");
        private static readonly int SkyAirExtinctionId = Shader.PropertyToID("_SkyAirExtinction");
        private static readonly int SkyAerosolScatteringId = Shader.PropertyToID("_SkyAerosolScattering");
        private static readonly int SkyAerosolExtinctionId = Shader.PropertyToID("_SkyAerosolExtinction");
        private static readonly int SkyOzoneExtinctionId = Shader.PropertyToID("_SkyOzoneExtinction");
        private static readonly int SkyOzoneParamsId = Shader.PropertyToID("_SkyOzoneParams");
        private static readonly int SkyGroundTintId = Shader.PropertyToID("_SkyGroundTint");
        private static readonly int SkyFogParamsId = Shader.PropertyToID("_SkyFogParams");
        private static readonly int TransmittanceLutId = Shader.PropertyToID("_TransmittanceLUT");
        private static readonly int MultiScatteringLutId = Shader.PropertyToID("_MultiScatteringLUT");
        private static readonly int TransmittanceLutOutputId = Shader.PropertyToID("_TransmittanceLUTOutput");
        private static readonly int MultiScatteringLutOutputId = Shader.PropertyToID("_MultiScatteringLUTOutput");
        private static readonly int SkyViewLutOutputId = Shader.PropertyToID("_SkyViewLUTOutput");
        private static readonly int SkyViewLutSourceId = Shader.PropertyToID("_SkyViewLUTSource");
        private static readonly int SkyViewHistoryLayersPreviousId = Shader.PropertyToID("_SkyViewHistoryLayersPrevious");
        private static readonly int SkyViewHistoryLayersCurrentId = Shader.PropertyToID("_SkyViewHistoryLayersCurrent");
        private static readonly int SkyViewHistoryMetaPreviousId = Shader.PropertyToID("_SkyViewHistoryMetaPrevious");
        private static readonly int SkyViewHistoryMetaCurrentId = Shader.PropertyToID("_SkyViewHistoryMetaCurrent");
        private static readonly int SkyViewHistoryHasValidHistoryId = Shader.PropertyToID("_SkyViewHistoryHasValidHistory");
        private static readonly int SkyViewHistoryDependencyHashId = Shader.PropertyToID("_SkyViewHistoryDependencyHash");
        private static readonly int SkyViewHistoryParameterHashId = Shader.PropertyToID("_SkyViewHistoryParameterHash");
        private static readonly int SkyViewHistoryFrameIndexId = Shader.PropertyToID("_SkyViewHistoryFrameIndex");
        private static readonly int SkyViewHistorySelectionId = Shader.PropertyToID("_SkyViewHistorySelection");

        [StructLayout(LayoutKind.Sequential)]
        private struct SkyViewHistorySelectionEntry
        {
            public uint targetLayer;
            public uint sourceLayer;
            public uint hasHistoryResources;
            public uint hasMatchingLayer;
        }

        [RenderGraphResource(Name = "TransmittanceLUT", Access = AccessFlags.Write)]
        private RenderGraphTexture m_TransmittanceLUT;

        [RenderGraphResource(Name = "MultiScatteringLUT", Access = AccessFlags.Write)]
        private RenderGraphTexture m_MultiScatteringLUT;

        [RenderGraphResource(Name = "SkyViewLUT", Access = AccessFlags.Write)]
        private RenderGraphTexture m_SkyViewLUT;

        [RenderGraphResource(
            Name = "SkyViewHistoryLayersPrevious",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private readonly RenderGraphTexture m_SkyViewHistoryLayersPrevious = new();

        [RenderGraphResource(
            Name = "SkyViewHistoryLayersCurrent",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private readonly RenderGraphTexture m_SkyViewHistoryLayersCurrent = new();

        [RenderGraphResource(
            Name = "SkyViewHistoryMetaPrevious",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private readonly RenderGraphBuffer m_SkyViewHistoryMetaPrevious = new();

        [RenderGraphResource(
            Name = "SkyViewHistoryMetaCurrent",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private readonly RenderGraphBuffer m_SkyViewHistoryMetaCurrent = new();

        [RenderGraphResource(
            Name = "SkyViewHistorySelection",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private readonly RenderGraphBuffer m_SkyViewHistorySelection = new();

        private ComputeShader m_ComputeShader;
        private int m_TransmittanceKernel = -1;
        private int m_MultiScatteringKernel = -1;
        private int m_SkyViewKernel = -1;
        private int m_SkyViewSelectHistoryKernel = -1;
        private int m_SkyViewStoreHistoryKernel = -1;
        private bool m_IsActive;
        private bool m_ShouldRebuildTransmittance;
        private bool m_ShouldRebuildMultiScattering;
        private bool m_ShouldRebuildSkyView;
        private SkyLutRebuildReason m_TransmittanceRebuildReason;
        private SkyLutRebuildReason m_MultiScatteringRebuildReason;
        private SkyLutRebuildReason m_SkyViewRebuildReason;
        private bool m_TransmittanceCacheRecreated;
        private bool m_MultiScatteringCacheRecreated;
        private bool m_SkyViewCacheRecreated;
        private int m_CachedTransmittanceHash;
        private int m_CachedMultiScatteringHash;
        private int m_CachedSkyViewDependencyHash;
        private int m_CachedSkyViewParametersHash;
        private int m_CachedSkyViewCameraHash;
        private RTHandle m_CachedTransmittanceHandle;
        private RTHandle m_CachedMultiScatteringHandle;
        private RTHandle m_CachedSkyViewHandle;
        private RenderTexture m_CachedTransmittanceTexture;
        private RenderTexture m_CachedMultiScatteringTexture;
        private RenderTexture m_CachedSkyViewTexture;
        private PhysicallyBasedSkyShaderParameters m_Parameters;
        private bool m_HasValidSkyViewHistoryLayers;
        private bool m_HasValidSkyViewHistoryMeta;
        private int m_SkyViewHistoryDependencyHash;
        private int m_SkyViewHistoryParameterHash;
        private uint m_SkyViewHistoryFrameIndex;

        public AtmosphereLUTPass()
        {
            profilingSampler = new ProfilingSampler(nameof(AtmosphereLUTPass));

            m_TransmittanceLUT = RenderGraphTexture.CreateOutput("TransmittanceLUT", GraphicsFormat.R16G16B16A16_SFloat);
            m_MultiScatteringLUT = RenderGraphTexture.CreateOutput("MultiScatteringLUT", GraphicsFormat.R16G16B16A16_SFloat);
            m_SkyViewLUT = RenderGraphTexture.CreateOutput("SkyViewLUT", GraphicsFormat.R16G16B16A16_SFloat);

            ConfigureLutDescriptor(m_TransmittanceLUT, TransmittanceWidth, TransmittanceHeight);
            ConfigureLutDescriptor(m_MultiScatteringLUT, MultiScatteringWidth, MultiScatteringHeight);
            ConfigureLutDescriptor(m_SkyViewLUT, SkyViewWidth, SkyViewHeight);
            ConfigureSkyViewHistoryTextureDescriptor(m_SkyViewHistoryLayersPrevious, "SkyViewHistoryLayersPrevious");
            ConfigureSkyViewHistoryTextureDescriptor(m_SkyViewHistoryLayersCurrent, "SkyViewHistoryLayersCurrent");
            ConfigureSkyViewHistoryMetaDescriptor(m_SkyViewHistoryMetaPrevious, "SkyViewHistoryMetaPrevious");
            ConfigureSkyViewHistoryMetaDescriptor(m_SkyViewHistoryMetaCurrent, "SkyViewHistoryMetaCurrent");
            ConfigureSkyViewHistorySelectionDescriptor(m_SkyViewHistorySelection, "SkyViewHistorySelection");
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_ComputeShader = resources?.AtmosphereLUTCompute;
            if (m_ComputeShader == null)
            {
                Debug.LogWarning(ComputePathWarning);
                return;
            }

            m_TransmittanceKernel = m_ComputeShader.FindKernel(TransmittanceKernelName);
            m_MultiScatteringKernel = m_ComputeShader.FindKernel(MultiScatteringKernelName);
            m_SkyViewKernel = m_ComputeShader.FindKernel(SkyViewKernelName);
            m_SkyViewSelectHistoryKernel = m_ComputeShader.FindKernel(SkyViewSelectHistoryKernelName);
            m_SkyViewStoreHistoryKernel = m_ComputeShader.FindKernel(SkyViewStoreHistoryKernelName);
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_IsActive = PhysicallyBasedSkyShaderParameterBuilder.TryBuild(frameData, out m_Parameters);
            ResetFrameCacheState();

            ConfigureLutDescriptor(m_TransmittanceLUT, TransmittanceWidth, TransmittanceHeight);
            ConfigureLutDescriptor(m_MultiScatteringLUT, MultiScatteringWidth, MultiScatteringHeight);
            ConfigureLutDescriptor(m_SkyViewLUT, SkyViewWidth, SkyViewHeight);
            ConfigureSkyViewHistoryTextureDescriptor(m_SkyViewHistoryLayersPrevious, "SkyViewHistoryLayersPrevious");
            ConfigureSkyViewHistoryTextureDescriptor(m_SkyViewHistoryLayersCurrent, "SkyViewHistoryLayersCurrent");
            ConfigureSkyViewHistoryMetaDescriptor(m_SkyViewHistoryMetaPrevious, "SkyViewHistoryMetaPrevious");
            ConfigureSkyViewHistoryMetaDescriptor(m_SkyViewHistoryMetaCurrent, "SkyViewHistoryMetaCurrent");
            ConfigureSkyViewHistorySelectionDescriptor(m_SkyViewHistorySelection, "SkyViewHistorySelection");

            if (!m_IsActive
                || !PassRecorder.IsPassTextureImportActive
                || m_ComputeShader == null
                || m_TransmittanceKernel < 0
                || m_MultiScatteringKernel < 0
                || m_SkyViewKernel < 0)
            {
                return;
            }

            EnsureCachedLutResources();

            if (m_CachedTransmittanceHandle != null)
                PassRecorder.ImportTexture(m_TransmittanceLUT, m_CachedTransmittanceHandle);

            if (m_CachedMultiScatteringHandle != null)
                PassRecorder.ImportTexture(m_MultiScatteringLUT, m_CachedMultiScatteringHandle);

            if (m_CachedSkyViewHandle != null)
                PassRecorder.ImportTexture(m_SkyViewLUT, m_CachedSkyViewHandle);

            m_HasValidSkyViewHistoryLayers = AllocHistoryTexture(
                SkyViewHistoryTextureKey,
                m_SkyViewHistoryLayersPrevious,
                m_SkyViewHistoryLayersCurrent,
                m_SkyViewHistoryLayersCurrent.desc);
            m_HasValidSkyViewHistoryMeta = AllocHistoryBuffer(
                SkyViewHistoryMetaKey,
                m_SkyViewHistoryMetaPrevious,
                m_SkyViewHistoryMetaCurrent,
                m_SkyViewHistoryMetaCurrent.desc);

            var transmittanceHash = ComputeTransmittanceHash(m_Parameters);
            var multiScatteringHash = ComputeMultiScatteringHash(m_Parameters, transmittanceHash);
            var skyViewDependencyHash = ComputeSkyViewDependencyHash(multiScatteringHash);
            var skyViewParametersHash = ComputeSkyViewParametersHash(m_Parameters);
            var skyViewCameraHash = ComputeSkyViewCameraHash(m_Parameters);
            m_SkyViewHistoryDependencyHash = skyViewDependencyHash;
            m_SkyViewHistoryParameterHash = skyViewParametersHash;
            m_SkyViewHistoryFrameIndex = unchecked((uint)Time.frameCount);

            m_TransmittanceRebuildReason = ResolveRebuildReason(m_TransmittanceCacheRecreated, m_CachedTransmittanceHash, transmittanceHash);
            m_MultiScatteringRebuildReason = ResolveRebuildReason(m_MultiScatteringCacheRecreated, m_CachedMultiScatteringHash, multiScatteringHash);
            m_SkyViewRebuildReason = ResolveSkyViewRebuildReason(
                m_SkyViewCacheRecreated,
                m_CachedSkyViewDependencyHash,
                skyViewDependencyHash,
                m_CachedSkyViewParametersHash,
                skyViewParametersHash,
                m_CachedSkyViewCameraHash,
                skyViewCameraHash);

            m_ShouldRebuildTransmittance = m_TransmittanceRebuildReason != SkyLutRebuildReason.None;
            m_ShouldRebuildMultiScattering = m_MultiScatteringRebuildReason != SkyLutRebuildReason.None;
            m_ShouldRebuildSkyView = m_SkyViewRebuildReason != SkyLutRebuildReason.None;
        }

        public override void Record(ComputeGraphContext context)
        {
            if (!m_IsActive
                || m_ComputeShader == null
                || m_TransmittanceKernel < 0
                || m_MultiScatteringKernel < 0
                || m_SkyViewKernel < 0
                || m_TransmittanceLUT?.innerHandle.IsValid() != true
                || m_MultiScatteringLUT?.innerHandle.IsValid() != true
                || m_SkyViewLUT?.innerHandle.IsValid() != true)
            {
                return;
            }

            var cmd = context.cmd;

            if (m_ShouldRebuildTransmittance)
            {
                using (new ProfilingScope(cmd, GetTransmittanceRebuildSampler(m_TransmittanceRebuildReason)))
                {
                    BindCommonParameters(cmd);
                    cmd.SetComputeTextureParam(m_ComputeShader, m_TransmittanceKernel, TransmittanceLutOutputId, m_TransmittanceLUT.innerHandle);
                    cmd.DispatchCompute(
                        m_ComputeShader,
                        m_TransmittanceKernel,
                        CoreUtils.DivRoundUp(TransmittanceWidth, 8),
                        CoreUtils.DivRoundUp(TransmittanceHeight, 8),
                        1);
                }

                m_CachedTransmittanceHash = ComputeTransmittanceHash(m_Parameters);
            }

            if (m_ShouldRebuildMultiScattering)
            {
                using (new ProfilingScope(cmd, GetMultiScatteringRebuildSampler(m_MultiScatteringRebuildReason)))
                {
                    BindCommonParameters(cmd);
                    cmd.SetComputeTextureParam(m_ComputeShader, m_MultiScatteringKernel, TransmittanceLutId, m_TransmittanceLUT.innerHandle);
                    cmd.SetComputeTextureParam(m_ComputeShader, m_MultiScatteringKernel, MultiScatteringLutOutputId, m_MultiScatteringLUT.innerHandle);
                    cmd.DispatchCompute(
                        m_ComputeShader,
                        m_MultiScatteringKernel,
                        CoreUtils.DivRoundUp(MultiScatteringWidth, 8),
                        CoreUtils.DivRoundUp(MultiScatteringHeight, 8),
                        1);
                }

                m_CachedMultiScatteringHash = ComputeMultiScatteringHash(m_Parameters, m_CachedTransmittanceHash);
            }

            if (m_ShouldRebuildSkyView)
            {
                using (new ProfilingScope(cmd, GetSkyViewRebuildSampler(m_SkyViewRebuildReason)))
                {
                    BindCommonParameters(cmd);
                    cmd.SetComputeTextureParam(m_ComputeShader, m_SkyViewKernel, TransmittanceLutId, m_TransmittanceLUT.innerHandle);
                    cmd.SetComputeTextureParam(m_ComputeShader, m_SkyViewKernel, MultiScatteringLutId, m_MultiScatteringLUT.innerHandle);
                    cmd.SetComputeTextureParam(m_ComputeShader, m_SkyViewKernel, SkyViewLutOutputId, m_SkyViewLUT.innerHandle);
                    cmd.DispatchCompute(
                        m_ComputeShader,
                        m_SkyViewKernel,
                        CoreUtils.DivRoundUp(SkyViewWidth, 8),
                        CoreUtils.DivRoundUp(SkyViewHeight, 8),
                        1);
                }

                m_CachedSkyViewDependencyHash = ComputeSkyViewDependencyHash(m_CachedMultiScatteringHash);
                m_CachedSkyViewParametersHash = ComputeSkyViewParametersHash(m_Parameters);
                m_CachedSkyViewCameraHash = ComputeSkyViewCameraHash(m_Parameters);
            }

            SelectSkyViewHistoryLayer(cmd);
            StoreSkyViewHistory(cmd);
        }

        public override void Dispose()
        {
            m_ComputeShader = null;
            m_TransmittanceKernel = -1;
            m_MultiScatteringKernel = -1;
            m_SkyViewKernel = -1;
            m_SkyViewSelectHistoryKernel = -1;
            m_SkyViewStoreHistoryKernel = -1;
            ReleaseCachedLutResources();
            m_TransmittanceLUT?.ClearImportedHandle();
            m_MultiScatteringLUT?.ClearImportedHandle();
            m_SkyViewLUT?.ClearImportedHandle();
            m_SkyViewHistoryLayersPrevious?.ClearImportedHandle();
            m_SkyViewHistoryLayersCurrent?.ClearImportedHandle();
            m_SkyViewHistoryMetaPrevious?.ClearImportedBuffer();
            m_SkyViewHistoryMetaCurrent?.ClearImportedBuffer();
        }

        private void BindCommonParameters(ComputeCommandBuffer cmd)
        {
            cmd.SetComputeVectorParam(m_ComputeShader, SkyCameraPositionPsId, m_Parameters.skyCameraPositionPS);
            cmd.SetComputeVectorParam(m_ComputeShader, SkySunDirectionId, m_Parameters.skySunDirection);
            cmd.SetComputeVectorParam(m_ComputeShader, SkySunColorId, m_Parameters.skySunColor);
            cmd.SetComputeVectorParam(m_ComputeShader, SkyPlanetParamsId, m_Parameters.skyPlanetParams);
            cmd.SetComputeVectorParam(m_ComputeShader, SkyAirScatteringId, m_Parameters.skyAirScattering);
            cmd.SetComputeVectorParam(m_ComputeShader, SkyAirExtinctionId, m_Parameters.skyAirExtinction);
            cmd.SetComputeVectorParam(m_ComputeShader, SkyAerosolScatteringId, m_Parameters.skyAerosolScattering);
            cmd.SetComputeVectorParam(m_ComputeShader, SkyAerosolExtinctionId, m_Parameters.skyAerosolExtinction);
            cmd.SetComputeVectorParam(m_ComputeShader, SkyOzoneExtinctionId, m_Parameters.skyOzoneExtinction);
            cmd.SetComputeVectorParam(m_ComputeShader, SkyOzoneParamsId, m_Parameters.skyOzoneParams);
            cmd.SetComputeVectorParam(m_ComputeShader, SkyGroundTintId, m_Parameters.skyGroundTint);
            cmd.SetComputeVectorParam(m_ComputeShader, SkyFogParamsId, m_Parameters.skyFogParams);
        }

        private static void ConfigureLutDescriptor(RenderGraphTexture texture, int width, int height)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = width;
            texture.desc.Height = height;
            texture.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.Dimension = TextureDimension.Tex2D;
            texture.desc.Slices = 1;
            texture.desc.FilterMode = FilterMode.Bilinear;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.EnableRandomWrite = true;
            texture.desc.ClearBuffer = false;
        }

        private static void ConfigureSkyViewHistoryTextureDescriptor(RenderGraphTexture texture, string name)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = SkyViewWidth;
            texture.desc.Height = SkyViewHeight;
            texture.desc.Slices = SkyViewHistoryLayerCount;
            texture.desc.Dimension = TextureDimension.Tex2DArray;
            texture.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.FilterMode = FilterMode.Bilinear;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.EnableRandomWrite = true;
            texture.desc.ClearBuffer = false;
            texture.desc.Name = name;
        }

        private static void ConfigureSkyViewHistoryMetaDescriptor(RenderGraphBuffer buffer, string name)
        {
            if (buffer == null)
                return;

            buffer.desc ??= new RenderGraphBufferDesc();
            buffer.desc.Count = SkyViewHistoryLayerCount;
            buffer.desc.Stride = SkyViewHistoryMetaStride;
            buffer.desc.Target = GraphicsBuffer.Target.Structured;
            buffer.desc.Name = name;
        }

        private static void ConfigureSkyViewHistorySelectionDescriptor(RenderGraphBuffer buffer, string name)
        {
            if (buffer == null)
                return;

            buffer.desc ??= new RenderGraphBufferDesc();
            buffer.desc.Count = 1;
            buffer.desc.Stride = SkyViewHistorySelectionStride;
            buffer.desc.Target = GraphicsBuffer.Target.Structured;
            buffer.desc.Name = name;
        }

        private void ResetFrameCacheState()
        {
            m_ShouldRebuildTransmittance = false;
            m_ShouldRebuildMultiScattering = false;
            m_ShouldRebuildSkyView = false;
            m_TransmittanceRebuildReason = SkyLutRebuildReason.None;
            m_MultiScatteringRebuildReason = SkyLutRebuildReason.None;
            m_SkyViewRebuildReason = SkyLutRebuildReason.None;
            m_TransmittanceCacheRecreated = false;
            m_MultiScatteringCacheRecreated = false;
            m_SkyViewCacheRecreated = false;
            m_HasValidSkyViewHistoryLayers = false;
            m_HasValidSkyViewHistoryMeta = false;
            m_SkyViewHistoryDependencyHash = 0;
            m_SkyViewHistoryParameterHash = 0;
            m_SkyViewHistoryFrameIndex = 0u;
        }

        private void EnsureCachedLutResources()
        {
            m_TransmittanceCacheRecreated = EnsureLutResource(
                ref m_CachedTransmittanceTexture,
                ref m_CachedTransmittanceHandle,
                TransmittanceWidth,
                TransmittanceHeight,
                TransmittanceTextureName);
            m_MultiScatteringCacheRecreated = EnsureLutResource(
                ref m_CachedMultiScatteringTexture,
                ref m_CachedMultiScatteringHandle,
                MultiScatteringWidth,
                MultiScatteringHeight,
                MultiScatteringTextureName);
            m_SkyViewCacheRecreated = EnsureLutResource(
                ref m_CachedSkyViewTexture,
                ref m_CachedSkyViewHandle,
                SkyViewWidth,
                SkyViewHeight,
                SkyViewTextureName);
        }

        private bool EnsureLutResource(
            ref RenderTexture texture,
            ref RTHandle handle,
            int width,
            int height,
            string name)
        {
            if (IsLutResourceValid(texture, handle, width, height))
                return false;

            if (handle != null)
            {
                handle.Release();
                handle = null;
            }

            if (texture != null)
            {
                texture.Release();
                CoreUtils.Destroy(texture);
                texture = null;
            }

            texture = new RenderTexture(width, height, 0)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                dimension = TextureDimension.Tex2D,
                volumeDepth = 1,
                graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.Create();
            handle = RTHandles.Alloc(texture);
            return true;
        }

        private void ReleaseCachedLutResources()
        {
            ReleaseLutResource(ref m_CachedTransmittanceTexture, ref m_CachedTransmittanceHandle);
            ReleaseLutResource(ref m_CachedMultiScatteringTexture, ref m_CachedMultiScatteringHandle);
            ReleaseLutResource(ref m_CachedSkyViewTexture, ref m_CachedSkyViewHandle);
            m_CachedTransmittanceHash = 0;
            m_CachedMultiScatteringHash = 0;
            m_CachedSkyViewDependencyHash = 0;
            m_CachedSkyViewParametersHash = 0;
            m_CachedSkyViewCameraHash = 0;
        }

        private static void ReleaseLutResource(ref RenderTexture texture, ref RTHandle handle)
        {
            if (handle != null)
            {
                handle.Release();
                handle = null;
            }

            if (texture == null)
                return;

            texture.Release();
            CoreUtils.Destroy(texture);
            texture = null;
        }

        private static bool IsLutResourceValid(RenderTexture texture, RTHandle handle, int width, int height)
        {
            return texture != null
                && handle != null
                && texture.IsCreated()
                && texture.width == width
                && texture.height == height
                && texture.graphicsFormat == GraphicsFormat.R16G16B16A16_SFloat
                && texture.enableRandomWrite
                && texture.dimension == TextureDimension.Tex2D;
        }

        private static SkyLutRebuildReason ResolveRebuildReason(bool cacheRecreated, int cachedHash, int nextHash)
        {
            if (cacheRecreated)
                return SkyLutRebuildReason.MissingTexture;

            return cachedHash != nextHash
                ? SkyLutRebuildReason.ParametersChanged
                : SkyLutRebuildReason.None;
        }

        private static SkyLutRebuildReason ResolveSkyViewRebuildReason(
            bool cacheRecreated,
            int cachedDependencyHash,
            int nextDependencyHash,
            int cachedParametersHash,
            int nextParametersHash,
            int cachedCameraHash,
            int nextCameraHash)
        {
            if (cacheRecreated)
                return SkyLutRebuildReason.MissingTexture;

            if (cachedDependencyHash != nextDependencyHash)
                return SkyLutRebuildReason.DependenciesChanged;

            if (cachedParametersHash != nextParametersHash)
                return SkyLutRebuildReason.ParametersChanged;

            return cachedCameraHash != nextCameraHash
                ? SkyLutRebuildReason.CameraChanged
                : SkyLutRebuildReason.None;
        }

        private static int ComputeTransmittanceHash(PhysicallyBasedSkyShaderParameters parameters)
        {
            unchecked
            {
                var hash = 17;
                hash = AppendHash(hash, parameters.skyPlanetParams.x);
                hash = AppendHash(hash, parameters.skyPlanetParams.y);
                hash = AppendHash(hash, parameters.skyAirExtinction.x);
                hash = AppendHash(hash, parameters.skyAirExtinction.y);
                hash = AppendHash(hash, parameters.skyAirExtinction.z);
                hash = AppendHash(hash, parameters.skyAerosolExtinction.x);
                hash = AppendHash(hash, parameters.skyOzoneExtinction.x);
                hash = AppendHash(hash, parameters.skyOzoneExtinction.y);
                hash = AppendHash(hash, parameters.skyOzoneExtinction.z);
                hash = AppendHash(hash, parameters.skyOzoneExtinction.w);
                hash = AppendHash(hash, parameters.skyOzoneParams.x);
                hash = AppendHash(hash, parameters.skyOzoneParams.y);
                hash = AppendHash(hash, parameters.skyOzoneParams.w);
                return hash;
            }
        }

        private static int ComputeMultiScatteringHash(PhysicallyBasedSkyShaderParameters parameters, int transmittanceHash)
        {
            unchecked
            {
                var hash = 17;
                hash = AppendHash(hash, transmittanceHash);
                hash = AppendHash(hash, parameters.skyGroundTint.x);
                hash = AppendHash(hash, parameters.skyGroundTint.y);
                hash = AppendHash(hash, parameters.skyGroundTint.z);
                hash = AppendHash(hash, parameters.skyAirScattering.x);
                hash = AppendHash(hash, parameters.skyAirScattering.y);
                hash = AppendHash(hash, parameters.skyAirScattering.z);
                hash = AppendHash(hash, parameters.skyAerosolScattering.x);
                hash = AppendHash(hash, parameters.skyAerosolScattering.y);
                hash = AppendHash(hash, parameters.skyAerosolScattering.z);
                hash = AppendHash(hash, parameters.skySunColor.x);
                hash = AppendHash(hash, parameters.skySunColor.y);
                hash = AppendHash(hash, parameters.skySunColor.z);
                return hash;
            }
        }

        private static int ComputeSkyViewDependencyHash(int multiScatteringHash)
        {
            unchecked
            {
                var hash = 17;
                hash = AppendHash(hash, multiScatteringHash);
                return hash;
            }
        }

        private static int ComputeSkyViewParametersHash(PhysicallyBasedSkyShaderParameters parameters)
        {
            unchecked
            {
                var hash = 17;
                hash = AppendHash(hash, parameters.skySunDirection.x);
                hash = AppendHash(hash, parameters.skySunDirection.y);
                hash = AppendHash(hash, parameters.skySunDirection.z);
                hash = AppendHash(hash, parameters.skyPlanetParams.z);
                hash = AppendHash(hash, parameters.skyAerosolExtinction.w);
                return hash;
            }
        }

        private static int ComputeSkyViewCameraHash(PhysicallyBasedSkyShaderParameters parameters)
        {
            unchecked
            {
                var hash = 17;
                hash = AppendHash(hash, parameters.skyCameraPositionPS.x);
                hash = AppendHash(hash, parameters.skyCameraPositionPS.y);
                hash = AppendHash(hash, parameters.skyCameraPositionPS.z);
                return hash;
            }
        }

        private static int AppendHash(int hash, int value)
        {
            unchecked
            {
                return hash * 31 + value;
            }
        }

        private static int AppendHash(int hash, float value)
        {
            return AppendHash(hash, value.GetHashCode());
        }

        private void SelectSkyViewHistoryLayer(ComputeCommandBuffer cmd)
        {
            if (cmd == null
                || m_ComputeShader == null
                || m_SkyViewSelectHistoryKernel < 0
                || m_SkyViewHistoryMetaPrevious?.innerHandle.IsValid() != true
                || m_SkyViewHistorySelection?.innerHandle.IsValid() != true)
            {
                return;
            }

            BindCommonParameters(cmd);
            cmd.SetComputeBufferParam(m_ComputeShader, m_SkyViewSelectHistoryKernel, SkyViewHistoryMetaPreviousId, m_SkyViewHistoryMetaPrevious.innerHandle);
            cmd.SetComputeBufferParam(m_ComputeShader, m_SkyViewSelectHistoryKernel, SkyViewHistorySelectionId, m_SkyViewHistorySelection.innerHandle);
            cmd.SetComputeIntParam(m_ComputeShader, SkyViewHistoryHasValidHistoryId, m_HasValidSkyViewHistoryLayers && m_HasValidSkyViewHistoryMeta ? 1 : 0);
            cmd.SetComputeIntParam(m_ComputeShader, SkyViewHistoryDependencyHashId, m_SkyViewHistoryDependencyHash);
            cmd.SetComputeIntParam(m_ComputeShader, SkyViewHistoryParameterHashId, m_SkyViewHistoryParameterHash);
            cmd.DispatchCompute(m_ComputeShader, m_SkyViewSelectHistoryKernel, 1, 1, 1);
        }

        private void StoreSkyViewHistory(ComputeCommandBuffer cmd)
        {
            if (cmd == null
                || m_ComputeShader == null
                || m_SkyViewSelectHistoryKernel < 0
                || m_SkyViewStoreHistoryKernel < 0
                || m_SkyViewLUT?.innerHandle.IsValid() != true
                || m_SkyViewHistoryLayersPrevious?.innerHandle.IsValid() != true
                || m_SkyViewHistoryLayersCurrent?.innerHandle.IsValid() != true
                || m_SkyViewHistoryMetaPrevious?.innerHandle.IsValid() != true
                || m_SkyViewHistoryMetaCurrent?.innerHandle.IsValid() != true
                || m_SkyViewHistorySelection?.innerHandle.IsValid() != true)
            {
                return;
            }

            BindCommonParameters(cmd);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SkyViewStoreHistoryKernel, SkyViewLutSourceId, m_SkyViewLUT.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SkyViewStoreHistoryKernel, SkyViewHistoryLayersPreviousId, m_SkyViewHistoryLayersPrevious.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SkyViewStoreHistoryKernel, SkyViewHistoryLayersCurrentId, m_SkyViewHistoryLayersCurrent.innerHandle);
            cmd.SetComputeBufferParam(m_ComputeShader, m_SkyViewStoreHistoryKernel, SkyViewHistoryMetaPreviousId, m_SkyViewHistoryMetaPrevious.innerHandle);
            cmd.SetComputeBufferParam(m_ComputeShader, m_SkyViewStoreHistoryKernel, SkyViewHistoryMetaCurrentId, m_SkyViewHistoryMetaCurrent.innerHandle);
            cmd.SetComputeBufferParam(m_ComputeShader, m_SkyViewStoreHistoryKernel, SkyViewHistorySelectionId, m_SkyViewHistorySelection.innerHandle);
            cmd.SetComputeIntParam(m_ComputeShader, SkyViewHistoryDependencyHashId, m_SkyViewHistoryDependencyHash);
            cmd.SetComputeIntParam(m_ComputeShader, SkyViewHistoryParameterHashId, m_SkyViewHistoryParameterHash);
            cmd.SetComputeIntParam(m_ComputeShader, SkyViewHistoryFrameIndexId, unchecked((int)m_SkyViewHistoryFrameIndex));
            cmd.DispatchCompute(
                m_ComputeShader,
                m_SkyViewStoreHistoryKernel,
                CoreUtils.DivRoundUp(SkyViewWidth, 8),
                CoreUtils.DivRoundUp(SkyViewHeight, 8),
                SkyViewHistoryLayerCount);
        }

        private static ProfilingSampler GetTransmittanceRebuildSampler(SkyLutRebuildReason reason)
        {
            return reason == SkyLutRebuildReason.MissingTexture
                ? s_TransmittanceMissingTextureSampler
                : s_TransmittanceParametersChangedSampler;
        }

        private static ProfilingSampler GetMultiScatteringRebuildSampler(SkyLutRebuildReason reason)
        {
            return reason == SkyLutRebuildReason.MissingTexture
                ? s_MultiScatteringMissingTextureSampler
                : s_MultiScatteringParametersChangedSampler;
        }

        private static ProfilingSampler GetSkyViewRebuildSampler(SkyLutRebuildReason reason)
        {
            return reason switch
            {
                SkyLutRebuildReason.DependenciesChanged => s_SkyViewDependenciesChangedSampler,
                SkyLutRebuildReason.CameraChanged => s_SkyViewCameraChangedSampler,
                SkyLutRebuildReason.ParametersChanged => s_SkyViewParametersChangedSampler,
                _ => s_SkyViewMissingTextureSampler,
            };
        }
    }
}

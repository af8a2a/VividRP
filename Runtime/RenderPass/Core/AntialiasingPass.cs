using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class AntialiasingPass : UnsafePass, IRenderGraphRecordingPass, IStablePassResourceLayout
    {
        private static readonly int InputColorId = Shader.PropertyToID("_InputColor");
        private static readonly int MotionVectorsId = Shader.PropertyToID("_MotionVectors");
        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int HistoryColorId = Shader.PropertyToID("_HistoryColor");
        private static readonly int OutputColorId = Shader.PropertyToID("_OutputColor");
        private static readonly int HistoryColorWriteId = Shader.PropertyToID("_HistoryColorWrite");
        private static readonly int TAAParamsId = Shader.PropertyToID("_TAAParams");
        private static readonly int ScreenSizeId = Shader.PropertyToID("_ScreenSize");
        private static readonly int JitterId = Shader.PropertyToID("_Jitter");

        private static readonly BaseRenderFunc<TaaPassData, ComputeGraphContext> s_TaaRenderFunc =
            ExecuteTaaPass;
        private static readonly BaseRenderFunc<CopyPassData, UnsafeGraphContext> s_CopyRenderFunc =
            ExecuteCopyPass;

        [RenderGraphResource(Name = "Color", Access = AccessFlags.Read)]
        private RenderGraphTexture Color;

        [RenderGraphResource(Name = "MotionVectors", Access = AccessFlags.Read)]
        private RenderGraphTexture MotionVectors;

        [RenderGraphResource(Name = "CameraDepth", Access = AccessFlags.Read)]
        private RenderGraphTexture CameraDepth;

        [RenderGraphResource(Name = "AntialiasingOutput", Access = AccessFlags.Write)]
        private RenderGraphTexture AntialiasingOutput;

        private readonly RenderGraphTexture m_DefaultColor;
        private readonly RenderGraphTexture m_DefaultMotionVectors;
        private readonly RenderGraphTexture m_DefaultCameraDepth;
        private readonly RenderGraphTextureDesc m_OutputDescriptor =
            RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R16G16B16A16_SFloat);
        private readonly RenderGraphTextureDesc m_TaaHistoryColorDescriptor =
            RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R16G16B16A16_SFloat);
        private ComputeShader m_ComputeShader;
        private Material m_CopyMaterial;
        private CMAA2Pass m_Cmaa2Pass;
        private PassResource m_Cmaa2Resources;
        private FSR3UpscalerPass m_Fsr3Pass;
        private TSRUpscalerPass m_TsrPass;
#if DLSS_PLUGIN_INTEGRATE
        private DLSSPass m_DlssPass;
        private DLSSNeuralRenderingPass m_DlssNeuralRenderingPass;
#endif
        private int m_TaaKernel = -1;
        private int m_Width = 1;
        private int m_Height = 1;
        private CameraHistoryTexture m_TaaHistory;
        private TextureHandle m_TaaHistoryColorPrevious;
        private TextureHandle m_TaaHistoryColorCurrent;
        private bool m_HasValidTaaHistory;
        private bool m_IsFirstFrame = true;
        private bool m_ResetHistory;
        private bool m_IsPassResourceLayoutDirty;
        private VividAntialiasingMode m_EffectiveMode = VividAntialiasingMode.None;
        private TAASettings m_TaaSettings = TAASettings.Disabled;
        private Vector2 m_Jitter;
        private Vector2 m_PreviousJitter;

        public bool IsPassResourceLayoutDirty => m_IsPassResourceLayoutDirty;

        public AntialiasingPass()
        {
            profilingSampler = new ProfilingSampler(nameof(AntialiasingPass));
            Color = RenderGraphTexture.CreateInput("Color", GraphicsFormat.R16G16B16A16_SFloat);
            MotionVectors = RenderGraphTexture.CreateInput("MotionVectors", GraphicsFormat.R16G16_SFloat);
            CameraDepth = RenderGraphTexture.CreateInput("CameraDepth", GraphicsFormat.None, DepthBits.Depth32);
            m_DefaultColor = Color;
            m_DefaultMotionVectors = MotionVectors;
            m_DefaultCameraDepth = CameraDepth;
            AntialiasingOutput = CreatePassOwnedTexture("AntialiasingOutput", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
        }

        public void ClearPassResourceLayoutDirty()
        {
            m_IsPassResourceLayoutDirty = false;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_ComputeShader = resources?.TemporalAACompute;
            if (m_ComputeShader != null)
                m_TaaKernel = m_ComputeShader.FindKernel("TemporalAA");

            m_CopyMaterial = CoreUtils.CreateEngineMaterial(resources?.BlitShader);

            m_Cmaa2Pass = new CMAA2Pass();
            m_Cmaa2Pass.Create();
            m_Cmaa2Resources = null;
            m_Fsr3Pass = new FSR3UpscalerPass();
            m_TsrPass = new TSRUpscalerPass();
#if DLSS_PLUGIN_INTEGRATE
            m_DlssPass = new DLSSPass();
            m_DlssNeuralRenderingPass = new DLSSNeuralRenderingPass();
#endif
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var antialiasingData = frameData.Get<VividAntialiasingData>();
            var temporalData = frameData.Get<VividTemporalData>();

            m_EffectiveMode = antialiasingData != null
                ? antialiasingData.effectiveMode
                : VividAntialiasingMode.None;
            m_Width = ResolveRenderWidth(cameraData, antialiasingData);
            m_Height = ResolveRenderHeight(cameraData, antialiasingData);
            m_TaaSettings = TAASettings.FromCamera(cameraData?.additionalData);
            m_IsFirstFrame = temporalData == null || temporalData.isFirstFrame;
            m_ResetHistory = antialiasingData != null && antialiasingData.resetHistory;
            m_Jitter = temporalData != null ? temporalData.jitter : Vector2.zero;
            m_PreviousJitter = temporalData != null ? temporalData.previousJitter : Vector2.zero;

            UpdateOutputDescriptor(cameraData, antialiasingData);
            PrepareTaaHistory(cameraData);
            PrepareCmaa2(frameData);
        }

        public void RecordGraph(RenderGraphRecordingContext context)
        {
            if (context.RenderGraph == null)
                return;

            if (ReferenceEquals(Color, m_DefaultColor))
                return;

            if (Color?.innerHandle.IsValid() != true)
            {
                var colorHandle = context.GetOrCreateTextureHandle(Color);
                if (colorHandle.IsValid())
                    Color.innerHandle = colorHandle;
            }

            if (Color?.innerHandle.IsValid() != true)
                return;

            if (m_EffectiveMode == VividAntialiasingMode.None)
            {
                if (TryRegisterPassthrough(context))
                    return;

                RecordCopyPass(context);
                return;
            }

            ResolveInputHandle(context, MotionVectors);
            ResolveInputHandle(context, CameraDepth);

            switch (m_EffectiveMode)
            {
                case VividAntialiasingMode.TemporalAntiAliasing:
                    if (TryRecordTaaPass(context))
                        return;
                    break;
                case VividAntialiasingMode.CMAA2:
                    if (TryRecordCmaa2Pass(context))
                        return;
                    break;
                case VividAntialiasingMode.SpatialTemporalPostProcessing:
                    if (TryRecordStpPass(context))
                        return;
                    break;
                case VividAntialiasingMode.FidelityFXSuperResolution3:
                    if (TryRecordFsr3Pass(context))
                        return;
                    break;
                case VividAntialiasingMode.TemporalSuperResolution:
                    if (TryRecordTsrPass(context))
                        return;
                    break;
#if DLSS_PLUGIN_INTEGRATE
                case VividAntialiasingMode.DeepLearningSuperSampling:
                    if (TryRecordDlssPass(context))
                        return;
                    break;
                case VividAntialiasingMode.DLSSNeuralRendering:
                    if (TryRecordDlssNeuralRenderingPass(context))
                        return;
                    break;
#endif
            }

            if (TryRegisterPassthrough(context))
                return;

            RecordCopyPass(context);
        }

        public override void Record(UnsafePassContext context)
        {
        }

        public override void Dispose()
        {
            if (m_CopyMaterial != null)
            {
                CoreUtils.Destroy(m_CopyMaterial);
                m_CopyMaterial = null;
            }

            m_Cmaa2Pass?.Dispose();
            m_Cmaa2Pass = null;
            m_Cmaa2Resources = null;
            m_Fsr3Pass?.Dispose();
            m_Fsr3Pass = null;
            m_TsrPass?.Dispose();
            m_TsrPass = null;
#if DLSS_PLUGIN_INTEGRATE
            m_DlssPass?.Dispose();
            m_DlssPass = null;
            m_DlssNeuralRenderingPass?.Dispose();
            m_DlssNeuralRenderingPass = null;
#endif
            m_ComputeShader = null;
            m_TaaKernel = -1;
            m_TaaHistory = null;
            m_TaaHistoryColorPrevious = default;
            m_TaaHistoryColorCurrent = default;
            m_HasValidTaaHistory = false;
            m_IsPassResourceLayoutDirty = false;
        }

        private void PrepareTaaHistory(VividCameraData cameraData)
        {
            m_TaaHistory = null;
            m_TaaHistoryColorPrevious = default;
            m_TaaHistoryColorCurrent = default;

            if (m_EffectiveMode != VividAntialiasingMode.TemporalAntiAliasing || !m_TaaSettings.Enabled)
            {
                m_HasValidTaaHistory = false;
                return;
            }

            var camera = cameraData?.camera;
            if (camera == null)
            {
                m_HasValidTaaHistory = false;
                return;
            }

            var history = camera.GetVividCameraHistory();
            var descriptor = CameraHistoryRenderGraphBridge.CreateDescriptor(CreateTaaHistoryDescriptor());
            m_TaaHistory = history.GetOrCreateTexture(
                CameraHistoryIds.AntialiasingTaa,
                2,
                descriptor);
            m_HasValidTaaHistory = m_TaaHistory.IsValid();
            m_TaaHistoryColorPrevious = CameraHistoryRenderGraphBridge.Import(m_TaaHistory, 1);
            m_TaaHistoryColorCurrent = CameraHistoryRenderGraphBridge.Import(m_TaaHistory, 0);
        }

        private static void ResolveInputHandle(RenderGraphRecordingContext context, RenderGraphTexture texture)
        {
            if (texture == null || texture.innerHandle.IsValid())
                return;

            var handle = context.GetOrCreateTextureHandle(texture);
            if (handle.IsValid())
                texture.innerHandle = handle;
        }

        private void PrepareCmaa2(ContextContainer frameData)
        {
            if (m_EffectiveMode != VividAntialiasingMode.CMAA2 || m_Cmaa2Pass == null)
                return;

            m_Cmaa2Pass.SetInput(Color);
            m_Cmaa2Pass.SetOutput(AntialiasingOutput);
            m_Cmaa2Pass.Prepare(frameData);
        }

        private bool TryRecordTaaPass(RenderGraphRecordingContext context)
        {
            if (m_ComputeShader == null || m_TaaKernel < 0)
                return false;

            if (!HasTemporalInputs())
                return false;

            var sourceHandle = context.GetOrCreateTextureHandle(Color);
            var outputHandle = context.GetOrCreateTextureHandle(AntialiasingOutput);
            var motionHandle = context.GetOrCreateTextureHandle(MotionVectors);
            var depthHandle = context.GetOrCreateTextureHandle(CameraDepth);
            if (!sourceHandle.IsValid()
                || !outputHandle.IsValid()
                || !motionHandle.IsValid()
                || !depthHandle.IsValid())
            {
                return false;
            }

            var historyPreviousHandle = m_HasValidTaaHistory && !m_IsFirstFrame
                && !m_ResetHistory
                ? m_TaaHistoryColorPrevious
                : sourceHandle;
            var historyCurrentHandle = m_TaaHistoryColorCurrent;

            using var builder = context.RenderGraph.AddComputePass<TaaPassData>(
                "Antialiasing/TAA",
                out var passData);

            passData.Pass = this;
            passData.Source = sourceHandle;
            passData.MotionVectors = motionHandle;
            passData.Depth = depthHandle;
            passData.HistoryPrevious = historyPreviousHandle;
            passData.HistoryCurrent = historyCurrentHandle;
            passData.Output = outputHandle;

            builder.UseTexture(passData.Source, AccessFlags.Read);
            if (passData.MotionVectors.IsValid())
                builder.UseTexture(passData.MotionVectors, AccessFlags.Read);
            if (passData.Depth.IsValid())
                builder.UseTexture(passData.Depth, AccessFlags.Read);
            if (passData.HistoryPrevious.IsValid() && !passData.HistoryPrevious.Equals(passData.Source))
                builder.UseTexture(passData.HistoryPrevious, AccessFlags.Read);
            if (passData.HistoryCurrent.IsValid())
                builder.UseTexture(passData.HistoryCurrent, AccessFlags.ReadWrite);
            builder.UseTexture(passData.Output, AccessFlags.WriteAll);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(s_TaaRenderFunc);
            return true;
        }

        private bool TryRecordCmaa2Pass(RenderGraphRecordingContext context)
        {
            if (m_Cmaa2Pass == null)
                return false;

            m_Cmaa2Pass.SetInput(Color);
            m_Cmaa2Pass.SetOutput(AntialiasingOutput);
            var resources = GetCmaa2PassResources();
            if (resources == null)
                return false;

            context.RecordComputePass(
                m_Cmaa2Pass,
                resources,
                passName: "Antialiasing/CMAA2");
            return AntialiasingOutput?.innerHandle.IsValid() == true;
        }

        internal PassResource GetCmaa2PassResources()
        {
            if (m_Cmaa2Pass == null)
                return null;

            if (m_Cmaa2Resources == null)
            {
                m_Cmaa2Resources = ((IRenderPass)m_Cmaa2Pass).Initialize();
                m_Cmaa2Pass.ClearPassResourceLayoutDirty();
                return m_Cmaa2Resources;
            }

            if (!m_Cmaa2Pass.IsPassResourceLayoutDirty)
                return m_Cmaa2Resources;

            if (!m_Cmaa2Pass.TryRefresh(m_Cmaa2Resources))
                m_Cmaa2Resources = ((IRenderPass)m_Cmaa2Pass).Initialize();

            m_Cmaa2Pass.ClearPassResourceLayoutDirty();
            return m_Cmaa2Resources;
        }

        private bool TryRecordFsr3Pass(RenderGraphRecordingContext context)
        {
            if (m_Fsr3Pass == null)
                return false;

            if (!HasTemporalInputs())
                return false;

            var cameraData = context.FrameData.Get<VividCameraData>();
            var camera = cameraData?.camera;
            if (camera == null)
                return false;

            return m_Fsr3Pass.Record(
                context.RenderGraph,
                cameraData,
                FrameContextSystem.GetOrCreate(camera),
                Color,
                CameraDepth,
                MotionVectors,
                AntialiasingOutput,
                context.TextureCache,
                m_ResetHistory);
        }

        private bool TryRecordTsrPass(RenderGraphRecordingContext context)
        {
            if (m_TsrPass == null)
                return false;

            if (!HasTemporalInputs())
                return false;

            var cameraData = context.FrameData.Get<VividCameraData>();
            var camera = cameraData?.camera;
            if (camera == null)
                return false;

            var antialiasingData = context.FrameData.Get<VividAntialiasingData>();
            var renderSize = antialiasingData?.renderSize ?? new Vector2Int(m_Width, m_Height);
            var outputSize = antialiasingData?.outputSize ?? ResolveOutputDimensions(cameraData, antialiasingData);

            return m_TsrPass.Record(
                context.RenderGraph,
                cameraData,
                FrameContextSystem.GetOrCreate(camera),
                Color,
                CameraDepth,
                MotionVectors,
                AntialiasingOutput,
                renderSize,
                outputSize,
                context.TextureCache,
                m_ResetHistory);
        }

#if DLSS_PLUGIN_INTEGRATE
        private bool TryRecordDlssPass(RenderGraphRecordingContext context)
        {
            if (m_DlssPass == null)
                return false;

            if (!HasTemporalInputs())
                return false;

            var cameraData = context.FrameData.Get<VividCameraData>();
            var camera = cameraData?.camera;
            if (camera == null)
                return false;

            var exposureData = context.FrameData.Get<VividExposureData>();
            return m_DlssPass.Record(
                context.RenderGraph,
                cameraData,
                FrameContextSystem.GetOrCreate(camera),
                Color,
                CameraDepth,
                MotionVectors,
                AntialiasingOutput,
                context.TextureCache,
                exposureData,
                m_ResetHistory);
        }

        private bool TryRecordDlssNeuralRenderingPass(RenderGraphRecordingContext context)
        {
            if (m_DlssNeuralRenderingPass == null || !HasTemporalInputs())
                return false;

            var cameraData = context.FrameData.Get<VividCameraData>();
            if (cameraData?.camera == null)
                return false;

            var antialiasingData = context.FrameData.Get<VividAntialiasingData>();
            var inputSize = antialiasingData?.renderSize ?? new Vector2Int(m_Width, m_Height);
            var outputSize = antialiasingData?.outputSize
                ?? ResolveOutputDimensions(cameraData, antialiasingData);
            return m_DlssNeuralRenderingPass.Record(
                context.RenderGraph,
                cameraData,
                Color,
                CameraDepth,
                MotionVectors,
                AntialiasingOutput,
                inputSize,
                outputSize,
                context.TextureCache,
                m_ResetHistory);
        }
#endif

        private bool TryRecordStpPass(RenderGraphRecordingContext context)
        {
            if (!HasTemporalInputs())
                return false;

            if (Color == null
                || MotionVectors == null
                || CameraDepth == null
                || Color.innerHandle.IsValid() != true
                || MotionVectors.innerHandle.IsValid() != true
                || CameraDepth.innerHandle.IsValid() != true)
            {
                return false;
            }

            var cameraData = context.FrameData.Get<VividCameraData>();
            var camera = cameraData?.camera;
            if (camera == null)
                return false;

            var temporalData = FrameContextSystem.GetOrCreate(camera);
            if (temporalData == null)
                return false;

            var blueNoiseResources = PipelineResourceManager.Get<BlueNoiseResources>();
            var noiseTexture = blueNoiseResources?.OwenScrambledSequence;
            if (noiseTexture == null)
                return false;

            var currentImageSize = new Vector2Int(m_Width, m_Height);
            var priorImageSize = new Vector2Int(
                temporalData.PreviousWidth > 0 ? temporalData.PreviousWidth : currentImageSize.x,
                temporalData.PreviousHeight > 0 ? temporalData.PreviousHeight : currentImageSize.y);

            var historyContext = temporalData.GetOrCreateStpHistoryContext();
            var historyUpdateInfo = new STP.HistoryUpdateInfo
            {
                preUpscaleSize = currentImageSize,
                postUpscaleSize = currentImageSize,
                useHwDrs = false,
                useTexArray = false,
            };
            var hasValidHistory = historyContext.Update(ref historyUpdateInfo);
            if (m_ResetHistory)
                hasValidHistory = false;

            var perViewConfigs = STP.perViewConfigs;
            if (perViewConfigs == null || perViewConfigs.Length == 0)
            {
                perViewConfigs = new STP.PerViewConfig[1];
                STP.perViewConfigs = perViewConfigs;
            }

            perViewConfigs[0] = new STP.PerViewConfig
            {
                currentProj = cameraData.GetGPUProjectionMatrixNoJitter(),
                lastProj = temporalData.PreviousProjectionMatrix,
                lastLastProj = temporalData.PreviousPreviousProjectionMatrix,
                currentView = cameraData.GetViewMatrix(),
                lastView = temporalData.PreviousViewMatrix,
                lastLastView = temporalData.PreviousPreviousViewMatrix,
            };

            var outputHandle = context.RenderGraph.CreateTexture(AntialiasingOutput.desc);
            var config = new STP.Config
            {
                noiseTexture = noiseTexture,
                inputColor = Color.innerHandle,
                inputDepth = CameraDepth.innerHandle,
                inputMotion = MotionVectors.innerHandle,
                destination = outputHandle,
                historyContext = historyContext,
                enableHwDrs = false,
                enableTexArray = false,
                enableMotionScaling = temporalData.DeltaTime > 0f && temporalData.PreviousDeltaTime > 0f,
                nearPlane = Mathf.Max(camera.nearClipPlane, 0.0001f),
                farPlane = Mathf.Max(camera.farClipPlane, camera.nearClipPlane + 0.0001f),
                frameIndex = cameraData.frameIndex,
                hasValidHistory = hasValidHistory,
                stencilMask = 0,
                debugViewIndex = 0,
                deltaTime = temporalData.DeltaTime,
                lastDeltaTime = temporalData.PreviousDeltaTime > 0f
                    ? temporalData.PreviousDeltaTime
                    : temporalData.DeltaTime,
                currentImageSize = currentImageSize,
                priorImageSize = priorImageSize,
                outputImageSize = currentImageSize,
                numActiveViews = 1,
                perViewConfigs = perViewConfigs,
            };

            var stpOutputHandle = STP.Execute(context.RenderGraph, ref config);
            context.RegisterTextureHandle(AntialiasingOutput, stpOutputHandle);
            return stpOutputHandle.IsValid();
        }

        private bool HasTemporalInputs()
        {
            return !ReferenceEquals(MotionVectors, m_DefaultMotionVectors)
                && !ReferenceEquals(CameraDepth, m_DefaultCameraDepth);
        }

        private bool TryRegisterPassthrough(RenderGraphRecordingContext context)
        {
            if (!CanAliasPassthrough())
                return false;

            var sourceHandle = context.GetOrCreateTextureHandle(Color);
            if (!sourceHandle.IsValid())
                return false;

            context.RegisterTextureHandle(AntialiasingOutput, sourceHandle);
            return true;
        }

        private bool CanAliasPassthrough()
        {
            var sourceDesc = Color?.desc;
            var outputDesc = AntialiasingOutput?.desc;
            if (sourceDesc == null || outputDesc == null)
                return false;

            return sourceDesc.Width == outputDesc.Width
                && sourceDesc.Height == outputDesc.Height
                && sourceDesc.Slices == outputDesc.Slices
                && sourceDesc.Dimension == outputDesc.Dimension
                && sourceDesc.ColorFormat == outputDesc.ColorFormat
                && sourceDesc.DepthBufferBits == outputDesc.DepthBufferBits
                && sourceDesc.MsaaSamples == outputDesc.MsaaSamples;
        }

        private void RecordCopyPass(RenderGraphRecordingContext context)
        {
            if (m_CopyMaterial == null)
                return;

            var sourceHandle = context.GetOrCreateTextureHandle(Color);
            var outputHandle = context.GetOrCreateTextureHandle(AntialiasingOutput);
            if (!sourceHandle.IsValid() || !outputHandle.IsValid())
                return;

            using var builder = context.RenderGraph.AddUnsafePass<CopyPassData>(
                "Antialiasing/Copy",
                out var passData);

            passData.Material = m_CopyMaterial;
            passData.Source = sourceHandle;
            passData.Output = outputHandle;
            builder.UseTexture(passData.Source, AccessFlags.Read);
            builder.UseTexture(passData.Output, AccessFlags.WriteAll);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(s_CopyRenderFunc);
        }

        private void RecordTaa(ComputeCommandBuffer cmd, TaaPassData data)
        {
            cmd.SetComputeTextureParam(m_ComputeShader, m_TaaKernel, InputColorId, data.Source);

            if (data.MotionVectors.IsValid())
                cmd.SetComputeTextureParam(m_ComputeShader, m_TaaKernel, MotionVectorsId, data.MotionVectors);

            if (data.Depth.IsValid())
                cmd.SetComputeTextureParam(m_ComputeShader, m_TaaKernel, DepthTextureId, data.Depth);

            var historyHandle = data.HistoryPrevious.IsValid()
                ? data.HistoryPrevious
                : data.Source;
            cmd.SetComputeTextureParam(m_ComputeShader, m_TaaKernel, HistoryColorId, historyHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_TaaKernel, OutputColorId, data.Output);

            if (data.HistoryCurrent.IsValid())
                cmd.SetComputeTextureParam(m_ComputeShader, m_TaaKernel, HistoryColorWriteId, data.HistoryCurrent);

            var hasHistory = m_HasValidTaaHistory && !m_IsFirstFrame && !m_ResetHistory ? 1.0f : 0.0f;
            cmd.SetComputeVectorParam(
                m_ComputeShader,
                TAAParamsId,
                new Vector4(
                    m_TaaSettings.BaseBlendFactor,
                    m_TaaSettings.MotionWeightDecay,
                    m_TaaSettings.AntiFlickerIntensity,
                    hasHistory));
            cmd.SetComputeVectorParam(
                m_ComputeShader,
                ScreenSizeId,
                new Vector4(m_Width, m_Height, 1.0f / m_Width, 1.0f / m_Height));
            cmd.SetComputeVectorParam(
                m_ComputeShader,
                JitterId,
                new Vector4(m_Jitter.x, m_Jitter.y, m_PreviousJitter.x, m_PreviousJitter.y));

            m_TaaHistory?.MarkWritten();
            var dispatchX = CoreUtils.DivRoundUp(m_Width, 8);
            var dispatchY = CoreUtils.DivRoundUp(m_Height, 8);
            cmd.DispatchCompute(m_ComputeShader, m_TaaKernel, dispatchX, dispatchY, 1);
        }

        private void UpdateOutputDescriptor(VividCameraData cameraData, VividAntialiasingData antialiasingData)
        {
            AntialiasingOutput ??= CreatePassOwnedTexture("AntialiasingOutput", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);

            var sourceDescriptor = Color?.desc;
            var outputDescriptor = m_OutputDescriptor;
            if (sourceDescriptor != null)
            {
                sourceDescriptor.Copy(outputDescriptor);
            }
            else
            {
                ConfigureColorDescriptor(
                    outputDescriptor,
                    "AntialiasingOutput",
                    1,
                    1,
                    GraphicsFormat.R16G16B16A16_SFloat);
            }

            var outputSize = ResolveOutputDimensions(cameraData, antialiasingData);

            if (m_EffectiveMode == VividAntialiasingMode.None)
            {
                outputDescriptor.Name = "AntialiasingOutput";
                outputDescriptor.Width = Mathf.Max(1, outputSize.x);
                outputDescriptor.Height = Mathf.Max(1, outputSize.y);
                outputDescriptor.ClearBuffer = false;
                AntialiasingOutput.desc = outputDescriptor;
                return;
            }

            var colorFormat = outputDescriptor.ColorFormat != GraphicsFormat.None
                ? outputDescriptor.ColorFormat
                : GraphicsFormat.R16G16B16A16_SFloat;
            if (!SystemInfo.IsFormatSupported(colorFormat, GraphicsFormatUsage.LoadStore))
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat;

            outputDescriptor.Name = "AntialiasingOutput";
            outputDescriptor.Width = Mathf.Max(1, outputSize.x);
            outputDescriptor.Height = Mathf.Max(1, outputSize.y);
            outputDescriptor.ColorFormat = colorFormat;
            outputDescriptor.DepthBufferBits = DepthBits.None;
            outputDescriptor.MsaaSamples = MSAASamples.None;
            outputDescriptor.ClearBuffer = false;
            outputDescriptor.EnableRandomWrite = true;
            outputDescriptor.FilterMode = FilterMode.Bilinear;
            outputDescriptor.WrapMode = TextureWrapMode.Clamp;
            outputDescriptor.UseMipMap = false;
            outputDescriptor.AutoGenerateMips = false;
            outputDescriptor.MipCount = 1;
            outputDescriptor.BindTextureMS = false;

            AntialiasingOutput.desc = outputDescriptor;
        }

        private static RenderGraphTextureDesc ConfigureColorDescriptor(
            RenderGraphTextureDesc descriptor,
            string name,
            int width,
            int height,
            GraphicsFormat format)
        {
            if (descriptor == null)
                return null;

            descriptor.Name = name;
            descriptor.Width = Mathf.Max(1, width);
            descriptor.Height = Mathf.Max(1, height);
            descriptor.Slices = 1;
            descriptor.Dimension = TextureDimension.Tex2D;
            descriptor.ColorFormat = format;
            descriptor.DepthBufferBits = DepthBits.None;
            descriptor.MsaaSamples = MSAASamples.None;
            descriptor.FilterMode = FilterMode.Bilinear;
            descriptor.WrapMode = TextureWrapMode.Clamp;
            descriptor.AnisoLevel = 1;
            descriptor.MipMapBias = 0f;
            descriptor.ClearBuffer = false;
            descriptor.ClearColor = UnityEngine.Color.clear;
            descriptor.IsShadowMap = false;
            descriptor.EnableRandomWrite = false;
            descriptor.BindTextureMS = false;
            descriptor.UseDynamicScale = false;
            descriptor.UseDynamicScaleExplicit = false;
            descriptor.ScaleFactor = Vector2.one;
            descriptor.UseMipMap = false;
            descriptor.AutoGenerateMips = false;
            descriptor.MipCount = 1;
            return descriptor;
        }

        private Vector2Int ResolveOutputDimensions(VividCameraData cameraData, VividAntialiasingData antialiasingData)
        {
            if (m_EffectiveMode == VividAntialiasingMode.FidelityFXSuperResolution3
                || m_EffectiveMode == VividAntialiasingMode.TemporalSuperResolution)
            {
                return antialiasingData?.outputSize ?? new Vector2Int(m_Width, m_Height);
            }

#if DLSS_PLUGIN_INTEGRATE
            if (m_EffectiveMode == VividAntialiasingMode.DeepLearningSuperSampling
                || m_EffectiveMode == VividAntialiasingMode.DLSSNeuralRendering)
                return antialiasingData?.outputSize ?? new Vector2Int(m_Width, m_Height);
#endif

            return new Vector2Int(
                Mathf.Max(1, cameraData?.actualWidth > 0 ? cameraData.actualWidth : m_Width),
                Mathf.Max(1, cameraData?.actualHeight > 0 ? cameraData.actualHeight : m_Height));
        }

        private RenderGraphTextureDesc CreateTaaHistoryDescriptor()
        {
            var desc = m_TaaHistoryColorDescriptor;
            desc.Name = "AntialiasingTAAHistoryColorCurrent";
            desc.Width = m_Width;
            desc.Height = m_Height;
            desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            desc.DepthBufferBits = DepthBits.None;
            desc.MsaaSamples = MSAASamples.None;
            desc.ClearBuffer = false;
            desc.EnableRandomWrite = true;
            desc.FilterMode = FilterMode.Point;
            desc.WrapMode = TextureWrapMode.Clamp;
            desc.UseMipMap = false;
            desc.AutoGenerateMips = false;
            desc.MipCount = 1;
            desc.BindTextureMS = false;
            return desc;
        }

        private static int ResolveRenderWidth(VividCameraData cameraData, VividAntialiasingData antialiasingData)
        {
            if (antialiasingData != null && antialiasingData.renderSize.x > 0)
                return antialiasingData.renderSize.x;

            if (cameraData != null && cameraData.actualWidth > 0)
                return cameraData.actualWidth;

            if (cameraData != null && cameraData.pixelWidth > 0)
                return cameraData.pixelWidth;

            return Mathf.Max(1, Screen.width);
        }

        private static int ResolveRenderHeight(VividCameraData cameraData, VividAntialiasingData antialiasingData)
        {
            if (antialiasingData != null && antialiasingData.renderSize.y > 0)
                return antialiasingData.renderSize.y;

            if (cameraData != null && cameraData.actualHeight > 0)
                return cameraData.actualHeight;

            if (cameraData != null && cameraData.pixelHeight > 0)
                return cameraData.pixelHeight;

            return Mathf.Max(1, Screen.height);
        }

        private static RenderGraphTexture CreatePassOwnedTexture(
            string name,
            int width,
            int height,
            GraphicsFormat format)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(width, height, format)
            };
            texture.desc.Name = name;
            texture.desc.EnableRandomWrite = true;
            texture.desc.ClearBuffer = false;
            texture.desc.FilterMode = FilterMode.Bilinear;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        private static void ResizePassOwned(RenderGraphTexture texture, int width, int height)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = Mathf.Max(1, width);
            texture.desc.Height = Mathf.Max(1, height);
            texture.desc.EnableRandomWrite = true;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
        }

        private static void ExecuteTaaPass(TaaPassData data, ComputeGraphContext context)
        {
            data.Pass.RecordTaa(context.cmd, data);
        }

        private static void ExecuteCopyPass(CopyPassData data, UnsafeGraphContext context)
        {
            if (data.Material == null || !data.Source.IsValid() || !data.Output.IsValid())
                return;

            var cmd = context.cmd;
            var unsafeCmd = CommandBufferHelpers.GetNativeCommandBuffer(cmd);
            RTHandle sourceHandle = data.Source;
            var scaleBias = sourceHandle.GetScaleBias(
                context.GetTextureUVOrigin(data.Source),
                context.GetTextureUVOrigin(data.Output));

            cmd.SetRenderTarget(data.Output);
            Blitter.BlitTexture(unsafeCmd, sourceHandle, scaleBias, data.Material, 0);
        }

        private sealed class TaaPassData
        {
            public AntialiasingPass Pass;
            public TextureHandle Source;
            public TextureHandle MotionVectors;
            public TextureHandle Depth;
            public TextureHandle HistoryPrevious;
            public TextureHandle HistoryCurrent;
            public TextureHandle Output;
        }

        private sealed class CopyPassData
        {
            public Material Material;
            public TextureHandle Source;
            public TextureHandle Output;
        }
    }
}

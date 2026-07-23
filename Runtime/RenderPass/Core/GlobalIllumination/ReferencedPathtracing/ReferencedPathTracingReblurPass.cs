using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// NRD REBLUR DIFFUSE_SPECULAR integration for the reference path tracer. V1 uses the canonical
    /// pre-pass, temporal accumulation, history fix, blur and post-blur sequence. Optional hit-distance
    /// reconstruction and temporal stabilization remain disabled until checkerboard/base-color modes
    /// are exposed as pipeline settings.
    /// </summary>
    public sealed class ReferencedPathTracingReblurPass : UnsafePass, IRenderGraphSideEffectPass
    {
        private sealed class ReblurCameraState : CameraRelativeState
        {
            public bool hasSettings;
            public ReferencedPathTracingReblurSettings settings;

            public override void Dispose()
            {
                hasSettings = false;
                settings = default;
            }
        }

        private const string ViewZHistoryKey = "ReferencedPathTracingReblur.ViewZ";
        private const string NormalRoughnessHistoryKey = "ReferencedPathTracingReblur.NormalRoughness";
        private const string InternalDataHistoryKey = "ReferencedPathTracingReblur.InternalData";
        private const string DiffuseHistoryKey = "ReferencedPathTracingReblur.Diffuse";
        private const string DiffuseFastHistoryKey = "ReferencedPathTracingReblur.DiffuseFast";
        private const string SpecularHistoryKey = "ReferencedPathTracingReblur.Specular";
        private const string SpecularFastHistoryKey = "ReferencedPathTracingReblur.SpecularFast";
        private const string SpecularHitDistanceHistoryKey =
            "ReferencedPathTracingReblur.SpecularHitDistance";

        private static readonly int ClassifyTilesConstantsId =
            Shader.PropertyToID("REBLUR_ClassifyTilesConstants");
        private static readonly int PrePassConstantsId =
            Shader.PropertyToID("REBLUR_PrePassConstants");
        private static readonly int TemporalAccumulationConstantsId =
            Shader.PropertyToID("REBLUR_TemporalAccumulationConstants");
        private static readonly int HistoryFixConstantsId =
            Shader.PropertyToID("REBLUR_HistoryFixConstants");
        private static readonly int BlurConstantsId = Shader.PropertyToID("REBLUR_BlurConstants");
        private static readonly int PostBlurConstantsId = Shader.PropertyToID("REBLUR_PostBlurConstants");

        private static readonly int InViewZId = Shader.PropertyToID("gIn_ViewZ");
        private static readonly int InMotionVectorsId = Shader.PropertyToID("gIn_Mv");
        private static readonly int InNormalRoughnessId = Shader.PropertyToID("gIn_Normal_Roughness");
        private static readonly int InTilesId = Shader.PropertyToID("gIn_Tiles");
        private static readonly int OutTilesId = Shader.PropertyToID("gOut_Tiles");
        private static readonly int InDiffuseId = Shader.PropertyToID("gIn_Diff");
        private static readonly int InSpecularId = Shader.PropertyToID("gIn_Spec");
        private static readonly int OutDiffuseId = Shader.PropertyToID("gOut_Diff");
        private static readonly int OutSpecularId = Shader.PropertyToID("gOut_Spec");
        private static readonly int InData1Id = Shader.PropertyToID("gIn_Data1");
        private static readonly int OutData1Id = Shader.PropertyToID("gOut_Data1");
        private static readonly int OutData2Id = Shader.PropertyToID("gOut_Data2");
        private static readonly int InDiffuseFastId = Shader.PropertyToID("gIn_DiffFast");
        private static readonly int InSpecularFastId = Shader.PropertyToID("gIn_SpecFast");
        private static readonly int OutDiffuseFastId = Shader.PropertyToID("gOut_DiffFast");
        private static readonly int OutSpecularFastId = Shader.PropertyToID("gOut_SpecFast");
        private static readonly int PreviousViewZId = Shader.PropertyToID("gPrev_ViewZ");
        private static readonly int PreviousNormalRoughnessId =
            Shader.PropertyToID("gPrev_Normal_Roughness");
        private static readonly int PreviousInternalDataId = Shader.PropertyToID("gPrev_InternalData");
        private static readonly int HistoryDiffuseId = Shader.PropertyToID("gHistory_Diff");
        private static readonly int HistorySpecularId = Shader.PropertyToID("gHistory_Spec");
        private static readonly int HistoryDiffuseFastId = Shader.PropertyToID("gHistory_DiffFast");
        private static readonly int HistorySpecularFastId = Shader.PropertyToID("gHistory_SpecFast");
        private static readonly int PreviousSpecularHitDistanceId =
            Shader.PropertyToID("gPrev_SpecHitDistForTracking");
        private static readonly int InSpecularHitDistanceId =
            Shader.PropertyToID("gIn_SpecHitDistForTracking");
        private static readonly int OutSpecularHitDistanceId =
            Shader.PropertyToID("gOut_SpecHitDistForTracking");
        private static readonly int InDisocclusionThresholdMixId =
            Shader.PropertyToID("gIn_DisocclusionThresholdMix");
        private static readonly int InDiffuseConfidenceId = Shader.PropertyToID("gIn_DiffConfidence");
        private static readonly int InSpecularConfidenceId = Shader.PropertyToID("gIn_SpecConfidence");
        private static readonly int OutViewZId = Shader.PropertyToID("gOut_ViewZ");
        private static readonly int OutNormalRoughnessId = Shader.PropertyToID("gOut_Normal_Roughness");
        private static readonly int OutInternalDataId = Shader.PropertyToID("gOut_InternalData");
        private static readonly int OutDiffuseCopyId = Shader.PropertyToID("gOut_DiffCopy");
        private static readonly int OutSpecularCopyId = Shader.PropertyToID("gOut_SpecCopy");

        private static readonly int ResolveInputDiffuseId = Shader.PropertyToID("_ReblurDiffuse");
        private static readonly int ResolveInputSpecularId = Shader.PropertyToID("_ReblurSpecular");
        private static readonly int ResolveDiffuseId = Shader.PropertyToID("_ReblurResolvedDiffuse");
        private static readonly int ResolveSpecularId = Shader.PropertyToID("_ReblurResolvedSpecular");
        private static readonly int ResolveDiffuseMaterialFactorId =
            Shader.PropertyToID("_ReblurDiffuseMaterialFactor");
        private static readonly int ResolveSpecularMaterialFactorId =
            Shader.PropertyToID("_ReblurSpecularMaterialFactor");
        private static readonly int ResolveDirectLightingId =
            Shader.PropertyToID("_ReblurDirectLighting");
        private static readonly int ResolveEmissionId = Shader.PropertyToID("_ReblurEmission");
        private static readonly int ResolvePreparedDiffuseId =
            Shader.PropertyToID("_ReblurPreparedDiffuse");
        private static readonly int ResolvePreparedSpecularId =
            Shader.PropertyToID("_ReblurPreparedSpecular");
        private static readonly int ResolveColorId = Shader.PropertyToID("_ReblurResolvedColor");
        private static readonly int ResolveScreenSizeId = Shader.PropertyToID("_ReblurResolveScreenSize");

        [RenderGraphResource(Name = "DiffuseRadianceHitDistance", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DiffuseInput;

        [RenderGraphResource(Name = "SpecularRadianceHitDistance", Access = AccessFlags.Read)]
        private RenderGraphTexture m_SpecularInput;

        [RenderGraphResource(Name = "PathTracingDirectLighting", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DirectLightingInput;

        [RenderGraphResource(Name = "PathTracingEmission", Access = AccessFlags.Read)]
        private RenderGraphTexture m_EmissionInput;

        [RenderGraphResource(Name = "NrdDiffuseMaterialFactor", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DiffuseMaterialFactorInput;

        [RenderGraphResource(Name = "NrdSpecularMaterialFactor", Access = AccessFlags.Read)]
        private RenderGraphTexture m_SpecularMaterialFactorInput;

        [RenderGraphResource(Name = "NrdViewZ", Access = AccessFlags.Read)]
        private RenderGraphTexture m_ViewZInput;

        [RenderGraphResource(Name = "MotionVectors", Access = AccessFlags.Read)]
        private RenderGraphTexture m_MotionVectorsInput;

        [RenderGraphResource(Name = "NrdNormalRoughness", Access = AccessFlags.Read)]
        private RenderGraphTexture m_NormalRoughnessInput;

        [RenderGraphResource(
            Name = "DenoisedDiffuseRadianceHitDistance",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_DiffuseOutput;

        [RenderGraphResource(
            Name = "DenoisedSpecularRadianceHitDistance",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_SpecularOutput;

        [RenderGraphResource(
            Name = "ReblurResolvedColor",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ResolvedColor;

        [RenderGraphResource(Name = "ReblurPreviousViewZ", Access = AccessFlags.Read)]
        private RenderGraphTexture m_PreviousViewZ;

        [RenderGraphResource(Name = "ReblurCurrentViewZ", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_CurrentViewZ;

        [RenderGraphResource(Name = "ReblurPreviousNormalRoughness", Access = AccessFlags.Read)]
        private RenderGraphTexture m_PreviousNormalRoughness;

        [RenderGraphResource(Name = "ReblurCurrentNormalRoughness", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_CurrentNormalRoughness;

        [RenderGraphResource(Name = "ReblurPreviousInternalData", Access = AccessFlags.Read)]
        private RenderGraphTexture m_PreviousInternalData;

        [RenderGraphResource(Name = "ReblurCurrentInternalData", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_CurrentInternalData;

        [RenderGraphResource(Name = "ReblurPreviousDiffuse", Access = AccessFlags.Read)]
        private RenderGraphTexture m_PreviousDiffuse;

        [RenderGraphResource(Name = "ReblurCurrentDiffuse", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_CurrentDiffuse;

        [RenderGraphResource(Name = "ReblurPreviousDiffuseFast", Access = AccessFlags.Read)]
        private RenderGraphTexture m_PreviousDiffuseFast;

        [RenderGraphResource(Name = "ReblurCurrentDiffuseFast", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_CurrentDiffuseFast;

        [RenderGraphResource(Name = "ReblurPreviousSpecular", Access = AccessFlags.Read)]
        private RenderGraphTexture m_PreviousSpecular;

        [RenderGraphResource(Name = "ReblurCurrentSpecular", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_CurrentSpecular;

        [RenderGraphResource(Name = "ReblurPreviousSpecularFast", Access = AccessFlags.Read)]
        private RenderGraphTexture m_PreviousSpecularFast;

        [RenderGraphResource(Name = "ReblurCurrentSpecularFast", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_CurrentSpecularFast;

        [RenderGraphResource(Name = "ReblurPreviousSpecularHitDistance", Access = AccessFlags.Read)]
        private RenderGraphTexture m_PreviousSpecularHitDistance;

        [RenderGraphResource(Name = "ReblurCurrentSpecularHitDistance", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_CurrentSpecularHitDistance;

        [RenderGraphResource(Name = "ReblurTiles", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_Tiles;

        [RenderGraphResource(Name = "ReblurData1", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_Data1;

        [RenderGraphResource(Name = "ReblurData2", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_Data2;

        [RenderGraphResource(Name = "ReblurSpecularHitDistance", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_SpecularHitDistance;

        [RenderGraphResource(Name = "ReblurDiffuseTemp1", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_DiffuseTemp1;

        [RenderGraphResource(Name = "ReblurDiffuseTemp2", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_DiffuseTemp2;

        [RenderGraphResource(Name = "ReblurSpecularTemp1", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_SpecularTemp1;

        [RenderGraphResource(Name = "ReblurSpecularTemp2", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_SpecularTemp2;

        [RenderGraphResource(Name = "ReblurDiffuseFast", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_DiffuseFast;

        [RenderGraphResource(Name = "ReblurSpecularFast", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_SpecularFast;

        [RenderGraphResource(Name = "ReblurPreparedDiffuse", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_PreparedDiffuse;

        [RenderGraphResource(Name = "ReblurPreparedSpecular", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_PreparedSpecular;

        private ComputeShader m_ClassifyTiles;
        private ComputeShader m_PrePass;
        private ComputeShader m_TemporalAccumulation;
        private ComputeShader m_HistoryFix;
        private ComputeShader m_Blur;
        private ComputeShader m_PostBlur;
        private ComputeShader m_Resolve;
        private readonly CameraRelativeSystem<ReblurCameraState> m_CameraStates = new();
        private int m_PrepareKernel = -1;
        private int m_ResolveKernel = -1;
        private int m_ResolveRawKernel = -1;
        private ReblurSharedConstants m_Constants;
        private int m_Width = 1;
        private int m_Height = 1;
        private bool m_HasValidHistory;
        private ReferencedPathTracingReblurSettings m_Settings =
            ReferencedPathTracingReblurSettings.CreateDefault();

        public ReferencedPathTracingReblurPass()
        {
            profilingSampler = new ProfilingSampler(nameof(ReferencedPathTracingReblurPass));

            m_DiffuseInput = RenderGraphTexture.CreateInput(
                "DiffuseRadianceHitDistance",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_SpecularInput = RenderGraphTexture.CreateInput(
                "SpecularRadianceHitDistance",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_DirectLightingInput = RenderGraphTexture.CreateInput(
                "PathTracingDirectLighting",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_EmissionInput = RenderGraphTexture.CreateInput(
                "PathTracingEmission",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_DiffuseMaterialFactorInput = RenderGraphTexture.CreateInput(
                "NrdDiffuseMaterialFactor",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_SpecularMaterialFactorInput = RenderGraphTexture.CreateInput(
                "NrdSpecularMaterialFactor",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_ViewZInput = RenderGraphTexture.CreateInput("NrdViewZ", GraphicsFormat.R32_SFloat);
            m_MotionVectorsInput = RenderGraphTexture.CreateInput(
                "MotionVectors",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_NormalRoughnessInput = RenderGraphTexture.CreateInput(
                "NrdNormalRoughness",
                GraphicsFormat.A2B10G10R10_UNormPack32);

            m_DiffuseOutput = CreateTexture(
                "DenoisedDiffuseRadianceHitDistance",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_SpecularOutput = CreateTexture(
                "DenoisedSpecularRadianceHitDistance",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_ResolvedColor = CreateTexture(
                "ReblurResolvedColor",
                GraphicsFormat.R32G32B32A32_SFloat);

            m_PreviousViewZ = RenderGraphTexture.CreateInput("ReblurPreviousViewZ", GraphicsFormat.R32_SFloat);
            m_CurrentViewZ = CreateTexture("ReblurCurrentViewZ", GraphicsFormat.R32_SFloat);
            m_PreviousNormalRoughness = RenderGraphTexture.CreateInput(
                "ReblurPreviousNormalRoughness",
                GraphicsFormat.A2B10G10R10_UNormPack32);
            m_CurrentNormalRoughness = CreateTexture(
                "ReblurCurrentNormalRoughness",
                GraphicsFormat.A2B10G10R10_UNormPack32);
            m_PreviousInternalData = RenderGraphTexture.CreateInput(
                "ReblurPreviousInternalData",
                GraphicsFormat.R16_UInt);
            m_CurrentInternalData = CreateTexture("ReblurCurrentInternalData", GraphicsFormat.R16_UInt);
            m_PreviousDiffuse = RenderGraphTexture.CreateInput(
                "ReblurPreviousDiffuse",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_CurrentDiffuse = CreateTexture("ReblurCurrentDiffuse", GraphicsFormat.R16G16B16A16_SFloat);
            m_PreviousDiffuseFast = RenderGraphTexture.CreateInput(
                "ReblurPreviousDiffuseFast",
                GraphicsFormat.R16_SFloat);
            m_CurrentDiffuseFast = CreateTexture("ReblurCurrentDiffuseFast", GraphicsFormat.R16_SFloat);
            m_PreviousSpecular = RenderGraphTexture.CreateInput(
                "ReblurPreviousSpecular",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_CurrentSpecular = CreateTexture("ReblurCurrentSpecular", GraphicsFormat.R16G16B16A16_SFloat);
            m_PreviousSpecularFast = RenderGraphTexture.CreateInput(
                "ReblurPreviousSpecularFast",
                GraphicsFormat.R16_SFloat);
            m_CurrentSpecularFast = CreateTexture("ReblurCurrentSpecularFast", GraphicsFormat.R16_SFloat);
            m_PreviousSpecularHitDistance = RenderGraphTexture.CreateInput(
                "ReblurPreviousSpecularHitDistance",
                GraphicsFormat.R16_SFloat);
            m_CurrentSpecularHitDistance = CreateTexture(
                "ReblurCurrentSpecularHitDistance",
                GraphicsFormat.R16_SFloat);

            m_Tiles = CreateTexture("ReblurTiles", GraphicsFormat.R8_UNorm);
            m_Data1 = CreateTexture("ReblurData1", GraphicsFormat.R8G8_UNorm);
            m_Data2 = CreateTexture("ReblurData2", GraphicsFormat.R32_UInt);
            m_SpecularHitDistance = CreateTexture("ReblurSpecularHitDistance", GraphicsFormat.R16_SFloat);
            m_DiffuseTemp1 = CreateTexture("ReblurDiffuseTemp1", GraphicsFormat.R16G16B16A16_SFloat);
            m_DiffuseTemp2 = CreateTexture("ReblurDiffuseTemp2", GraphicsFormat.R16G16B16A16_SFloat);
            m_SpecularTemp1 = CreateTexture("ReblurSpecularTemp1", GraphicsFormat.R16G16B16A16_SFloat);
            m_SpecularTemp2 = CreateTexture("ReblurSpecularTemp2", GraphicsFormat.R16G16B16A16_SFloat);
            m_DiffuseFast = CreateTexture("ReblurDiffuseFast", GraphicsFormat.R16_SFloat);
            m_SpecularFast = CreateTexture("ReblurSpecularFast", GraphicsFormat.R16_SFloat);
            m_PreparedDiffuse = CreateTexture(
                "ReblurPreparedDiffuse",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_PreparedSpecular = CreateTexture(
                "ReblurPreparedSpecular",
                GraphicsFormat.R16G16B16A16_SFloat);
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            if (resources == null)
                return;

            m_ClassifyTiles = resources.REBLURDiffuseSpecularClassifyTilesCompute;
            m_PrePass = resources.REBLURDiffuseSpecularPrePassCompute;
            m_TemporalAccumulation = resources.REBLURDiffuseSpecularTemporalAccumulationCompute;
            m_HistoryFix = resources.REBLURDiffuseSpecularHistoryFixCompute;
            m_Blur = resources.REBLURDiffuseSpecularBlurCompute;
            m_PostBlur = resources.REBLURDiffuseSpecularPostBlurCompute;
            m_Resolve = resources.REBLURDiffuseSpecularResolveCompute;
            if (m_Resolve != null)
            {
                m_PrepareKernel = FindKernel(m_Resolve, "Prepare");
                m_ResolveKernel = FindKernel(m_Resolve, "Resolve");
                m_ResolveRawKernel = FindKernel(m_Resolve, "ResolveRaw");
            }
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var temporalData = frameData.GetOrCreate<VividTemporalData>();
            m_Width = CameraDimensionUtility.ResolveCameraDimension(
                cameraData?.actualWidth ?? 0,
                cameraData?.pixelWidth ?? 0,
                Screen.width);
            m_Height = CameraDimensionUtility.ResolveCameraDimension(
                cameraData?.actualHeight ?? 0,
                cameraData?.pixelHeight ?? 0,
                Screen.height);
            m_Settings = ReferencedPathTracingReblurSettingsResolver.Resolve();

            ResizeFullResolutionTextures();
            ResizeTexture(m_Tiles, CoreUtils.DivRoundUp(m_Width, 16), CoreUtils.DivRoundUp(m_Height, 16));

            bool hasViewZ = AllocateHistory(
                ViewZHistoryKey,
                m_PreviousViewZ,
                m_CurrentViewZ,
                GraphicsFormat.R32_SFloat);
            bool hasNormalRoughness = AllocateHistory(
                NormalRoughnessHistoryKey,
                m_PreviousNormalRoughness,
                m_CurrentNormalRoughness,
                GraphicsFormat.A2B10G10R10_UNormPack32);
            bool hasInternalData = AllocateHistory(
                InternalDataHistoryKey,
                m_PreviousInternalData,
                m_CurrentInternalData,
                GraphicsFormat.R16_UInt);
            bool hasDiffuse = AllocateHistory(
                DiffuseHistoryKey,
                m_PreviousDiffuse,
                m_CurrentDiffuse,
                GraphicsFormat.R16G16B16A16_SFloat);
            bool hasDiffuseFast = AllocateHistory(
                DiffuseFastHistoryKey,
                m_PreviousDiffuseFast,
                m_CurrentDiffuseFast,
                GraphicsFormat.R16_SFloat);
            bool hasSpecular = AllocateHistory(
                SpecularHistoryKey,
                m_PreviousSpecular,
                m_CurrentSpecular,
                GraphicsFormat.R16G16B16A16_SFloat);
            bool hasSpecularFast = AllocateHistory(
                SpecularFastHistoryKey,
                m_PreviousSpecularFast,
                m_CurrentSpecularFast,
                GraphicsFormat.R16_SFloat);
            bool hasSpecularHitDistance = AllocateHistory(
                SpecularHitDistanceHistoryKey,
                m_PreviousSpecularHitDistance,
                m_CurrentSpecularHitDistance,
                GraphicsFormat.R16_SFloat);

            bool settingsMatch = UpdateCameraSettings(cameraData?.camera, m_Settings);
            m_HasValidHistory = m_Settings.enabled
                && temporalData != null
                && !temporalData.isFirstFrame
                && settingsMatch
                && hasViewZ
                && hasNormalRoughness
                && hasInternalData
                && hasDiffuse
                && hasDiffuseFast
                && hasSpecular
                && hasSpecularFast
                && hasSpecularHitDistance;
            m_Constants = ReblurSharedConstants.Compute(
                cameraData,
                temporalData,
                m_Width,
                m_Height,
                m_HasValidHistory,
                m_Settings);
            m_CameraStates.PurgeDestroyedCameras();
        }

        public override void Record(UnsafePassContext context)
        {
            if (!CanResolveRaw())
                return;

            var cmd = context.GetNativeCommandBuffer();
            int dispatchX = CoreUtils.DivRoundUp(m_Width, 8);
            int dispatchY = CoreUtils.DivRoundUp(m_Height, 16);
            int tileX = CoreUtils.DivRoundUp(m_Width, 16);
            int tileY = CoreUtils.DivRoundUp(m_Height, 16);

            using (new ProfilingScope(cmd, profilingSampler))
            {
                if (!m_Settings.enabled || !CanExecute())
                {
                    DispatchResolveRaw(cmd);
                    return;
                }

                DispatchPrepare(cmd);

                ConstantBuffer.Push(cmd, m_Constants, m_ClassifyTiles, ClassifyTilesConstantsId);
                Bind(cmd, m_ClassifyTiles, InViewZId, m_ViewZInput);
                Bind(cmd, m_ClassifyTiles, OutTilesId, m_Tiles);
                cmd.DispatchCompute(m_ClassifyTiles, 0, tileX, tileY, 1);

                ConstantBuffer.Push(cmd, m_Constants, m_PrePass, PrePassConstantsId);
                BindCommonGuides(cmd, m_PrePass);
                Bind(cmd, m_PrePass, InTilesId, m_Tiles);
                Bind(cmd, m_PrePass, InDiffuseId, m_PreparedDiffuse);
                Bind(cmd, m_PrePass, InSpecularId, m_PreparedSpecular);
                Bind(cmd, m_PrePass, OutDiffuseId, m_DiffuseTemp1);
                Bind(cmd, m_PrePass, OutSpecularId, m_SpecularTemp1);
                Bind(cmd, m_PrePass, OutSpecularHitDistanceId, m_SpecularHitDistance);
                cmd.DispatchCompute(m_PrePass, 0, dispatchX, dispatchY, 1);

                ConstantBuffer.Push(
                    cmd,
                    m_Constants,
                    m_TemporalAccumulation,
                    TemporalAccumulationConstantsId);
                Bind(cmd, m_TemporalAccumulation, InTilesId, m_Tiles);
                BindCommonGuides(cmd, m_TemporalAccumulation);
                Bind(cmd, m_TemporalAccumulation, InMotionVectorsId, m_MotionVectorsInput);
                Bind(cmd, m_TemporalAccumulation, PreviousViewZId, m_PreviousViewZ);
                Bind(cmd, m_TemporalAccumulation, PreviousNormalRoughnessId, m_PreviousNormalRoughness);
                Bind(cmd, m_TemporalAccumulation, PreviousInternalDataId, m_PreviousInternalData);
                Bind(cmd, m_TemporalAccumulation, InDisocclusionThresholdMixId, m_ViewZInput);
                Bind(cmd, m_TemporalAccumulation, InDiffuseConfidenceId, m_ViewZInput);
                Bind(cmd, m_TemporalAccumulation, InSpecularConfidenceId, m_ViewZInput);
                Bind(cmd, m_TemporalAccumulation, InDiffuseId, m_DiffuseTemp1);
                Bind(cmd, m_TemporalAccumulation, InSpecularId, m_SpecularTemp1);
                Bind(cmd, m_TemporalAccumulation, HistoryDiffuseId, m_PreviousDiffuse);
                Bind(cmd, m_TemporalAccumulation, HistorySpecularId, m_PreviousSpecular);
                Bind(cmd, m_TemporalAccumulation, HistoryDiffuseFastId, m_PreviousDiffuseFast);
                Bind(cmd, m_TemporalAccumulation, HistorySpecularFastId, m_PreviousSpecularFast);
                Bind(
                    cmd,
                    m_TemporalAccumulation,
                    PreviousSpecularHitDistanceId,
                    m_PreviousSpecularHitDistance);
                Bind(cmd, m_TemporalAccumulation, InSpecularHitDistanceId, m_SpecularHitDistance);
                Bind(cmd, m_TemporalAccumulation, OutData1Id, m_Data1);
                Bind(cmd, m_TemporalAccumulation, OutDiffuseId, m_DiffuseTemp2);
                Bind(cmd, m_TemporalAccumulation, OutSpecularId, m_SpecularTemp2);
                Bind(cmd, m_TemporalAccumulation, OutDiffuseFastId, m_DiffuseFast);
                Bind(cmd, m_TemporalAccumulation, OutSpecularFastId, m_SpecularFast);
                Bind(
                    cmd,
                    m_TemporalAccumulation,
                    OutSpecularHitDistanceId,
                    m_CurrentSpecularHitDistance);
                Bind(cmd, m_TemporalAccumulation, OutData2Id, m_Data2);
                cmd.DispatchCompute(m_TemporalAccumulation, 0, dispatchX, dispatchY, 1);

                ConstantBuffer.Push(cmd, m_Constants, m_HistoryFix, HistoryFixConstantsId);
                Bind(cmd, m_HistoryFix, InTilesId, m_Tiles);
                BindCommonGuides(cmd, m_HistoryFix);
                Bind(cmd, m_HistoryFix, InData1Id, m_Data1);
                Bind(cmd, m_HistoryFix, InDiffuseId, m_DiffuseTemp2);
                Bind(cmd, m_HistoryFix, InSpecularId, m_SpecularTemp2);
                Bind(cmd, m_HistoryFix, InDiffuseFastId, m_DiffuseFast);
                Bind(cmd, m_HistoryFix, InSpecularFastId, m_SpecularFast);
                Bind(
                    cmd,
                    m_HistoryFix,
                    InSpecularHitDistanceId,
                    m_CurrentSpecularHitDistance);
                Bind(cmd, m_HistoryFix, OutDiffuseId, m_DiffuseTemp1);
                Bind(cmd, m_HistoryFix, OutSpecularId, m_SpecularTemp1);
                Bind(cmd, m_HistoryFix, OutDiffuseFastId, m_CurrentDiffuseFast);
                Bind(cmd, m_HistoryFix, OutSpecularFastId, m_CurrentSpecularFast);
                cmd.DispatchCompute(m_HistoryFix, 0, dispatchX, dispatchY, 1);

                ConstantBuffer.Push(cmd, m_Constants, m_Blur, BlurConstantsId);
                Bind(cmd, m_Blur, InTilesId, m_Tiles);
                BindCommonGuides(cmd, m_Blur);
                Bind(cmd, m_Blur, InData1Id, m_Data1);
                Bind(cmd, m_Blur, InDiffuseId, m_DiffuseTemp1);
                Bind(cmd, m_Blur, InSpecularId, m_SpecularTemp1);
                Bind(cmd, m_Blur, OutViewZId, m_CurrentViewZ);
                Bind(cmd, m_Blur, OutDiffuseId, m_DiffuseTemp2);
                Bind(cmd, m_Blur, OutSpecularId, m_SpecularTemp2);
                cmd.DispatchCompute(m_Blur, 0, dispatchX, dispatchY, 1);

                ConstantBuffer.Push(cmd, m_Constants, m_PostBlur, PostBlurConstantsId);
                Bind(cmd, m_PostBlur, InTilesId, m_Tiles);
                Bind(cmd, m_PostBlur, InNormalRoughnessId, m_NormalRoughnessInput);
                Bind(cmd, m_PostBlur, InData1Id, m_Data1);
                Bind(cmd, m_PostBlur, InViewZId, m_CurrentViewZ);
                Bind(cmd, m_PostBlur, InDiffuseId, m_DiffuseTemp2);
                Bind(cmd, m_PostBlur, InSpecularId, m_SpecularTemp2);
                Bind(cmd, m_PostBlur, OutNormalRoughnessId, m_CurrentNormalRoughness);
                Bind(cmd, m_PostBlur, OutDiffuseId, m_CurrentDiffuse);
                Bind(cmd, m_PostBlur, OutSpecularId, m_CurrentSpecular);
                Bind(cmd, m_PostBlur, OutInternalDataId, m_CurrentInternalData);
                Bind(cmd, m_PostBlur, OutDiffuseCopyId, m_DiffuseOutput);
                Bind(cmd, m_PostBlur, OutSpecularCopyId, m_SpecularOutput);
                cmd.DispatchCompute(m_PostBlur, 0, dispatchX, dispatchY, 1);

                DispatchResolve(cmd);
            }
        }

        public override void Dispose()
        {
            m_CameraStates.Dispose();
            m_ClassifyTiles = null;
            m_PrePass = null;
            m_TemporalAccumulation = null;
            m_HistoryFix = null;
            m_Blur = null;
            m_PostBlur = null;
            m_Resolve = null;
            m_PrepareKernel = -1;
            m_ResolveKernel = -1;
            m_ResolveRawKernel = -1;
            m_HasValidHistory = false;
            m_Width = 1;
            m_Height = 1;
            m_Settings = ReferencedPathTracingReblurSettings.CreateDefault();
        }

        private bool UpdateCameraSettings(
            Camera camera,
            ReferencedPathTracingReblurSettings settings)
        {
            if (camera == null)
                return false;

            var state = m_CameraStates.GetOrCreateBase(camera);
            bool matches = state.hasSettings && state.settings.Equals(settings);
            state.hasSettings = true;
            state.settings = settings;
            return matches;
        }

        private bool AllocateHistory(
            string key,
            RenderGraphTexture previous,
            RenderGraphTexture current,
            GraphicsFormat format)
        {
            var descriptor = RenderGraphTextureDesc.CreateColorTarget(m_Width, m_Height, format);
            descriptor.Name = key;
            descriptor.EnableRandomWrite = true;
            // REBLUR's gResetHistory path ignores previous contents and every stage overwrites current
            // history. Avoid requiring RTV support for packed/UAV-only history formats.
            descriptor.ClearBuffer = false;
            descriptor.ClearColor = Color.clear;
            descriptor.FilterMode = FilterMode.Point;
            descriptor.WrapMode = TextureWrapMode.Clamp;
            return AllocHistoryTexture(key, previous, current, descriptor);
        }

        private void ResizeFullResolutionTextures()
        {
            var textures = new[]
            {
                m_DiffuseOutput,
                m_SpecularOutput,
                m_ResolvedColor,
                m_CurrentViewZ,
                m_CurrentNormalRoughness,
                m_CurrentInternalData,
                m_CurrentDiffuse,
                m_CurrentDiffuseFast,
                m_CurrentSpecular,
                m_CurrentSpecularFast,
                m_CurrentSpecularHitDistance,
                m_Data1,
                m_Data2,
                m_SpecularHitDistance,
                m_DiffuseTemp1,
                m_DiffuseTemp2,
                m_SpecularTemp1,
                m_SpecularTemp2,
                m_DiffuseFast,
                m_SpecularFast,
                m_PreparedDiffuse,
                m_PreparedSpecular
            };

            foreach (var texture in textures)
                ResizeTexture(texture, m_Width, m_Height);
        }

        private bool CanExecute()
        {
            return m_ClassifyTiles != null
                && m_PrePass != null
                && m_TemporalAccumulation != null
                && m_HistoryFix != null
                && m_Blur != null
                && m_PostBlur != null
                && m_Resolve != null
                && m_PrepareKernel >= 0
                && m_ResolveKernel >= 0
                && IsValid(m_DiffuseInput)
                && IsValid(m_SpecularInput)
                && IsValid(m_DirectLightingInput)
                && IsValid(m_EmissionInput)
                && IsValid(m_DiffuseMaterialFactorInput)
                && IsValid(m_SpecularMaterialFactorInput)
                && IsValid(m_ViewZInput)
                && IsValid(m_MotionVectorsInput)
                && IsValid(m_NormalRoughnessInput)
                && IsValid(m_DiffuseOutput)
                && IsValid(m_SpecularOutput)
                && IsValid(m_ResolvedColor)
                && IsValid(m_PreviousViewZ)
                && IsValid(m_CurrentViewZ)
                && IsValid(m_PreviousNormalRoughness)
                && IsValid(m_CurrentNormalRoughness)
                && IsValid(m_PreviousInternalData)
                && IsValid(m_CurrentInternalData)
                && IsValid(m_PreviousDiffuse)
                && IsValid(m_CurrentDiffuse)
                && IsValid(m_PreviousDiffuseFast)
                && IsValid(m_CurrentDiffuseFast)
                && IsValid(m_PreviousSpecular)
                && IsValid(m_CurrentSpecular)
                && IsValid(m_PreviousSpecularFast)
                && IsValid(m_CurrentSpecularFast)
                && IsValid(m_PreviousSpecularHitDistance)
                && IsValid(m_CurrentSpecularHitDistance)
                && IsValid(m_Tiles)
                && IsValid(m_Data1)
                && IsValid(m_Data2)
                && IsValid(m_SpecularHitDistance)
                && IsValid(m_DiffuseTemp1)
                && IsValid(m_DiffuseTemp2)
                && IsValid(m_SpecularTemp1)
                && IsValid(m_SpecularTemp2)
                && IsValid(m_DiffuseFast)
                && IsValid(m_SpecularFast)
                && IsValid(m_PreparedDiffuse)
                && IsValid(m_PreparedSpecular);
        }

        private bool CanResolveRaw()
        {
            return m_Resolve != null
                && m_ResolveRawKernel >= 0
                && IsValid(m_DiffuseInput)
                && IsValid(m_SpecularInput)
                && IsValid(m_DirectLightingInput)
                && IsValid(m_EmissionInput)
                && IsValid(m_ResolvedColor);
        }

        private void DispatchPrepare(CommandBuffer cmd)
        {
            Bind(cmd, m_Resolve, m_PrepareKernel, ResolveInputDiffuseId, m_DiffuseInput);
            Bind(cmd, m_Resolve, m_PrepareKernel, ResolveInputSpecularId, m_SpecularInput);
            Bind(
                cmd,
                m_Resolve,
                m_PrepareKernel,
                ResolveDiffuseMaterialFactorId,
                m_DiffuseMaterialFactorInput);
            Bind(
                cmd,
                m_Resolve,
                m_PrepareKernel,
                ResolveSpecularMaterialFactorId,
                m_SpecularMaterialFactorInput);
            Bind(cmd, m_Resolve, m_PrepareKernel, ResolvePreparedDiffuseId, m_PreparedDiffuse);
            Bind(cmd, m_Resolve, m_PrepareKernel, ResolvePreparedSpecularId, m_PreparedSpecular);
            DispatchResolveKernel(cmd, m_PrepareKernel);
        }

        private void DispatchResolve(CommandBuffer cmd)
        {
            Bind(cmd, m_Resolve, m_ResolveKernel, ResolveDiffuseId, m_DiffuseOutput);
            Bind(cmd, m_Resolve, m_ResolveKernel, ResolveSpecularId, m_SpecularOutput);
            Bind(
                cmd,
                m_Resolve,
                m_ResolveKernel,
                ResolveDiffuseMaterialFactorId,
                m_DiffuseMaterialFactorInput);
            Bind(
                cmd,
                m_Resolve,
                m_ResolveKernel,
                ResolveSpecularMaterialFactorId,
                m_SpecularMaterialFactorInput);
            Bind(
                cmd,
                m_Resolve,
                m_ResolveKernel,
                ResolveDirectLightingId,
                m_DirectLightingInput);
            Bind(cmd, m_Resolve, m_ResolveKernel, ResolveEmissionId, m_EmissionInput);
            Bind(cmd, m_Resolve, m_ResolveKernel, ResolveColorId, m_ResolvedColor);
            DispatchResolveKernel(cmd, m_ResolveKernel);
        }

        private void DispatchResolveRaw(CommandBuffer cmd)
        {
            Bind(cmd, m_Resolve, m_ResolveRawKernel, ResolveInputDiffuseId, m_DiffuseInput);
            Bind(cmd, m_Resolve, m_ResolveRawKernel, ResolveInputSpecularId, m_SpecularInput);
            Bind(
                cmd,
                m_Resolve,
                m_ResolveRawKernel,
                ResolveDirectLightingId,
                m_DirectLightingInput);
            Bind(cmd, m_Resolve, m_ResolveRawKernel, ResolveEmissionId, m_EmissionInput);
            Bind(cmd, m_Resolve, m_ResolveRawKernel, ResolveColorId, m_ResolvedColor);
            DispatchResolveKernel(cmd, m_ResolveRawKernel);
        }

        private void DispatchResolveKernel(CommandBuffer cmd, int kernel)
        {
            cmd.SetComputeVectorParam(
                m_Resolve,
                ResolveScreenSizeId,
                new Vector4(m_Width, m_Height, 1.0f / m_Width, 1.0f / m_Height));
            cmd.DispatchCompute(
                m_Resolve,
                kernel,
                CoreUtils.DivRoundUp(m_Width, 8),
                CoreUtils.DivRoundUp(m_Height, 8),
                1);
        }

        private void BindCommonGuides(CommandBuffer cmd, ComputeShader shader)
        {
            Bind(cmd, shader, InViewZId, m_ViewZInput);
            Bind(cmd, shader, InNormalRoughnessId, m_NormalRoughnessInput);
        }

        private static void Bind(
            CommandBuffer cmd,
            ComputeShader shader,
            int propertyId,
            RenderGraphTexture texture)
        {
            Bind(cmd, shader, 0, propertyId, texture);
        }

        private static void Bind(
            CommandBuffer cmd,
            ComputeShader shader,
            int kernel,
            int propertyId,
            RenderGraphTexture texture)
        {
            cmd.SetComputeTextureParam(shader, kernel, propertyId, texture.innerHandle);
        }

        private static bool IsValid(RenderGraphTexture texture)
        {
            return texture?.innerHandle.IsValid() == true;
        }

        private static int FindKernel(ComputeShader shader, string name)
        {
            return shader != null && shader.HasKernel(name)
                ? shader.FindKernel(name)
                : -1;
        }

        private static RenderGraphTexture CreateTexture(string name, GraphicsFormat format)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, format)
            };
            texture.desc.Name = name;
            texture.desc.EnableRandomWrite = true;
            // All REBLUR dispatches fully overwrite their UAV outputs. Keeping this false also avoids
            // invalid RTV clears for packed/UAV-only formats on D3D12.
            texture.desc.ClearBuffer = false;
            texture.desc.ClearColor = Color.clear;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        private static void ResizeTexture(RenderGraphTexture texture, int width, int height)
        {
            if (texture?.desc == null)
                return;

            texture.Resize(width, height);
            texture.desc.EnableRandomWrite = true;
        }
    }
}

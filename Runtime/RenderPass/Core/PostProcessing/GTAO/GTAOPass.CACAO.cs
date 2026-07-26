using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed partial class GTAOPass
    {
        private const int CacaoPrepareThreadGroupSize = 8;
        private const int CacaoGenerateThreadGroupSize = 8;
        private const int CacaoGenerateSparseThreadGroupSizeX = 4;
        private const int CacaoGenerateSparseThreadGroupSizeY = 16;
        private const int CacaoGenerateSparseDispatchDepth = 5;
        private const int CacaoImportanceThreadGroupSize = 8;
        private const int CacaoBlurThreadGroupSize = 16;
        private const int CacaoApplyThreadGroupSize = 8;
        private const int CacaoUpscaleThreadGroupSize = 16;
        private const int CacaoSliceCount = 4;
        private const int CacaoDepthMipCount = 4;
        private const int CacaoMaxBlurPasses = 8;

        private static readonly int CacaoConstantBufferId = Shader.PropertyToID("SSAOConstantsBuffer");
        private static readonly int CacaoClearLoadCounterId =
            Shader.PropertyToID("g_ClearLoadCounter_LoadCounter");
        private static readonly int CacaoDepthInputId = Shader.PropertyToID("g_DepthIn");
        private static readonly int CacaoNormalInputId =
            Shader.PropertyToID("g_PrepareNormalsFromNormalsInput");
        private static readonly int CacaoPreparedDepthId =
            Shader.PropertyToID("g_PrepareDepthsOut");
        private static readonly int CacaoPreparedDepthMip0Id =
            Shader.PropertyToID("g_PrepareDepthsAndMips_OutMip0");
        private static readonly int CacaoPreparedDepthMip1Id =
            Shader.PropertyToID("g_PrepareDepthsAndMips_OutMip1");
        private static readonly int CacaoPreparedDepthMip2Id =
            Shader.PropertyToID("g_PrepareDepthsAndMips_OutMip2");
        private static readonly int CacaoPreparedDepthMip3Id =
            Shader.PropertyToID("g_PrepareDepthsAndMips_OutMip3");
        private static readonly int CacaoPreparedNormalId =
            Shader.PropertyToID("g_PrepareNormals_NormalOut");
        private static readonly int CacaoViewspaceDepthId =
            Shader.PropertyToID("g_ViewspaceDepthSource");
        private static readonly int CacaoDeinterleavedNormalsId =
            Shader.PropertyToID("g_DeinterleavedNormals");
        private static readonly int CacaoLoadCounterId = Shader.PropertyToID("g_LoadCounter");
        private static readonly int CacaoImportanceMapId = Shader.PropertyToID("g_ImportanceMap");
        private static readonly int CacaoFinalSsaoId = Shader.PropertyToID("g_FinalSSAO");
        private static readonly int CacaoSsaoOutputId = Shader.PropertyToID("g_SSAOOutput");
        private static readonly int CacaoImportanceFinalSsaoId =
            Shader.PropertyToID("g_ImportanceFinalSSAO");
        private static readonly int CacaoImportanceOutId = Shader.PropertyToID("g_ImportanceOut");
        private static readonly int CacaoImportanceAInputId =
            Shader.PropertyToID("g_ImportanceAIn");
        private static readonly int CacaoImportanceAOutputId =
            Shader.PropertyToID("g_ImportanceAOut");
        private static readonly int CacaoImportanceBInputId =
            Shader.PropertyToID("g_ImportanceBIn");
        private static readonly int CacaoImportanceBOutputId =
            Shader.PropertyToID("g_ImportanceBOut");
        private static readonly int CacaoImportanceBLoadCounterId =
            Shader.PropertyToID("g_ImportanceBLoadCounter");
        private static readonly int CacaoBlurInputId =
            Shader.PropertyToID("g_EdgeSensitiveBlur_Input");
        private static readonly int CacaoBlurOutputId =
            Shader.PropertyToID("g_EdgeSensitiveBlur_Output");
        private static readonly int CacaoApplyInputId = Shader.PropertyToID("g_ApplyFinalSSAO");
        private static readonly int CacaoApplyOutputId = Shader.PropertyToID("g_ApplyOutput");
        private static readonly int CacaoUpscaleInputId =
            Shader.PropertyToID("g_BilateralUpscaleInput");
        private static readonly int CacaoUpscaleDepthId =
            Shader.PropertyToID("g_BilateralUpscaleDepth");
        private static readonly int CacaoUpscaleDownscaledDepthId =
            Shader.PropertyToID("g_BilateralUpscaleDownscaledDepth");
        private static readonly int CacaoUpscaleOutputId =
            Shader.PropertyToID("g_BilateralUpscaleOutput");
        private static readonly int[] CacaoSubPassMap = { 0, 1, 4, 3, 2 };

        [StructLayout(LayoutKind.Sequential)]
        private struct CacaoConstantBufferData
        {
            public Vector2 DepthUnpackConsts;
            public Vector2 CameraTanHalfFOV;

            public Vector2 NDCToViewMul;
            public Vector2 NDCToViewAdd;

            public Vector2 DepthBufferUVToViewMul;
            public Vector2 DepthBufferUVToViewAdd;

            public float EffectRadius;
            public float EffectShadowStrength;
            public float EffectShadowPow;
            public float EffectShadowClamp;

            public float EffectFadeOutMul;
            public float EffectFadeOutAdd;
            public float EffectHorizonAngleThreshold;
            public float EffectSamplingRadiusNearLimitRec;

            public float DepthPrecisionOffsetMod;
            public float NegRecEffectRadius;
            public float LoadCounterAvgDiv;
            public float AdaptiveSampleCountLimit;

            public float InvSharpness;
            public int PassIndex;
            public float BilateralSigmaSquared;
            public float BilateralSimilarityDistanceSigma;

            public Vector4 PatternRotScaleMatrix0;
            public Vector4 PatternRotScaleMatrix1;
            public Vector4 PatternRotScaleMatrix2;
            public Vector4 PatternRotScaleMatrix3;
            public Vector4 PatternRotScaleMatrix4;

            public float NormalsUnpackMul;
            public float NormalsUnpackAdd;
            public float DetailAOStrength;
            public float Dummy0;

            public Vector2 SSAOBufferDimensions;
            public Vector2 SSAOBufferInverseDimensions;

            public Vector2 DepthBufferDimensions;
            public Vector2 DepthBufferInverseDimensions;

            public int DepthBufferOffsetX;
            public int DepthBufferOffsetY;
            public Vector2 PerPassFullResUVOffset;

            public Vector2 OutputBufferDimensions;
            public Vector2 OutputBufferInverseDimensions;

            public Vector2 ImportanceMapDimensions;
            public Vector2 ImportanceMapInverseDimensions;

            public Vector2 DeinterleavedDepthBufferDimensions;
            public Vector2 DeinterleavedDepthBufferInverseDimensions;

            public Vector2 DeinterleavedDepthBufferOffset;
            public Vector2 DeinterleavedDepthBufferNormalisedOffset;

            public Matrix4x4 NormalsWorldToViewspaceMatrix;
        }

        private readonly struct CacaoBufferSizeInfo
        {
            public CacaoBufferSizeInfo(
                int outputWidth,
                int outputHeight,
                int ssaoWidth,
                int ssaoHeight,
                int deinterleavedDepthWidth,
                int deinterleavedDepthHeight,
                int importanceWidth,
                int importanceHeight)
            {
                OutputWidth = outputWidth;
                OutputHeight = outputHeight;
                SsaoWidth = ssaoWidth;
                SsaoHeight = ssaoHeight;
                DeinterleavedDepthWidth = deinterleavedDepthWidth;
                DeinterleavedDepthHeight = deinterleavedDepthHeight;
                ImportanceWidth = importanceWidth;
                ImportanceHeight = importanceHeight;
            }

            public int OutputWidth { get; }
            public int OutputHeight { get; }
            public int SsaoWidth { get; }
            public int SsaoHeight { get; }
            public int DeinterleavedDepthWidth { get; }
            public int DeinterleavedDepthHeight { get; }
            public int ImportanceWidth { get; }
            public int ImportanceHeight { get; }
        }

        [RenderGraphResource(
            Name = "CACAODeinterleavedDepths",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_CacaoDeinterleavedDepths;

        [RenderGraphResource(
            Name = "CACAODeinterleavedNormals",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_CacaoDeinterleavedNormals;

        [RenderGraphResource(
            Name = "CACAOSSAOPing",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_CacaoSsaoPing;

        [RenderGraphResource(
            Name = "CACAOSSAOPong",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_CacaoSsaoPong;

        [RenderGraphResource(
            Name = "CACAOImportanceMap",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_CacaoImportanceMap;

        [RenderGraphResource(
            Name = "CACAOImportanceMapPong",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_CacaoImportanceMapPong;

        [RenderGraphResource(
            Name = "CACAOLoadCounter",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_CacaoLoadCounter;

        private readonly CacaoConstantBufferData[] m_CacaoConstantBuffers =
            new CacaoConstantBufferData[CacaoSliceCount];
        private readonly int[] m_CacaoBlurKernels = new int[CacaoMaxBlurPasses];

        private ComputeShader m_CacaoCompute;
        private int m_CacaoClearLoadCounterKernel = -1;
        private int m_CacaoPrepareDownsampledDepthsKernel = -1;
        private int m_CacaoPrepareNativeDepthsKernel = -1;
        private int m_CacaoPrepareDownsampledDepthsAndMipsKernel = -1;
        private int m_CacaoPrepareNativeDepthsAndMipsKernel = -1;
        private int m_CacaoPrepareDownsampledNormalsFromInputKernel = -1;
        private int m_CacaoPrepareNativeNormalsFromInputKernel = -1;
        private int m_CacaoPrepareDownsampledDepthsHalfKernel = -1;
        private int m_CacaoPrepareNativeDepthsHalfKernel = -1;
        private int m_CacaoGenerateQ0Kernel = -1;
        private int m_CacaoGenerateQ1Kernel = -1;
        private int m_CacaoGenerateQ2Kernel = -1;
        private int m_CacaoGenerateQ3Kernel = -1;
        private int m_CacaoGenerateQ3BaseKernel = -1;
        private int m_CacaoGenerateImportanceMapKernel = -1;
        private int m_CacaoPostprocessImportanceMapAKernel = -1;
        private int m_CacaoPostprocessImportanceMapBKernel = -1;
        private int m_CacaoApplyKernel = -1;
        private int m_CacaoNonSmartApplyKernel = -1;
        private int m_CacaoNonSmartHalfApplyKernel = -1;
        private int m_CacaoUpscaleSmartKernel = -1;
        private int m_CacaoUpscaleNonSmartKernel = -1;
        private int m_CacaoUpscaleHalfKernel = -1;
        private CacaoBufferSizeInfo m_CacaoBufferSizeInfo;

        private void InitializeCacaoResources()
        {
            m_CacaoDeinterleavedDepths = CreateCacaoTexture(
                "CACAODeinterleavedDepths",
                GraphicsFormat.R16_SFloat,
                CacaoSliceCount,
                CacaoDepthMipCount);
            m_CacaoDeinterleavedNormals = CreateCacaoTexture(
                "CACAODeinterleavedNormals",
                GraphicsFormat.R8G8B8A8_SNorm,
                CacaoSliceCount);
            m_CacaoSsaoPing = CreateCacaoTexture(
                "CACAOSSAOPing",
                GraphicsFormat.R8G8_UNorm,
                CacaoSliceCount);
            m_CacaoSsaoPong = CreateCacaoTexture(
                "CACAOSSAOPong",
                GraphicsFormat.R8G8_UNorm,
                CacaoSliceCount);
            m_CacaoImportanceMap = CreateCacaoTexture(
                "CACAOImportanceMap",
                GraphicsFormat.R8_UNorm);
            m_CacaoImportanceMapPong = CreateCacaoTexture(
                "CACAOImportanceMapPong",
                GraphicsFormat.R8_UNorm);
            m_CacaoLoadCounter = CreateCacaoTexture(
                "CACAOLoadCounter",
                GraphicsFormat.R32_UInt);
        }

        private void CreateCacao(VividRPCoreResources resources)
        {
            m_CacaoCompute = resources?.CACAOCompute;
            if (m_CacaoCompute == null)
                return;

            m_CacaoClearLoadCounterKernel =
                m_CacaoCompute.FindKernel("FFX_CACAO_ClearLoadCounter");
            m_CacaoPrepareDownsampledDepthsKernel =
                m_CacaoCompute.FindKernel("FFX_CACAO_PrepareDownsampledDepths");
            m_CacaoPrepareNativeDepthsKernel =
                m_CacaoCompute.FindKernel("FFX_CACAO_PrepareNativeDepths");
            m_CacaoPrepareDownsampledDepthsAndMipsKernel =
                m_CacaoCompute.FindKernel("FFX_CACAO_PrepareDownsampledDepthsAndMips");
            m_CacaoPrepareNativeDepthsAndMipsKernel =
                m_CacaoCompute.FindKernel("FFX_CACAO_PrepareNativeDepthsAndMips");
            m_CacaoPrepareDownsampledNormalsFromInputKernel =
                m_CacaoCompute.FindKernel("FFX_CACAO_PrepareDownsampledNormalsFromInputNormals");
            m_CacaoPrepareNativeNormalsFromInputKernel =
                m_CacaoCompute.FindKernel("FFX_CACAO_PrepareNativeNormalsFromInputNormals");
            m_CacaoPrepareDownsampledDepthsHalfKernel =
                m_CacaoCompute.FindKernel("FFX_CACAO_PrepareDownsampledDepthsHalf");
            m_CacaoPrepareNativeDepthsHalfKernel =
                m_CacaoCompute.FindKernel("FFX_CACAO_PrepareNativeDepthsHalf");
            m_CacaoGenerateQ0Kernel = m_CacaoCompute.FindKernel("FFX_CACAO_GenerateQ0");
            m_CacaoGenerateQ1Kernel = m_CacaoCompute.FindKernel("FFX_CACAO_GenerateQ1");
            m_CacaoGenerateQ2Kernel = m_CacaoCompute.FindKernel("FFX_CACAO_GenerateQ2");
            m_CacaoGenerateQ3Kernel = m_CacaoCompute.FindKernel("FFX_CACAO_GenerateQ3");
            m_CacaoGenerateQ3BaseKernel =
                m_CacaoCompute.FindKernel("FFX_CACAO_GenerateQ3Base");
            m_CacaoGenerateImportanceMapKernel =
                m_CacaoCompute.FindKernel("FFX_CACAO_GenerateImportanceMap");
            m_CacaoPostprocessImportanceMapAKernel =
                m_CacaoCompute.FindKernel("FFX_CACAO_PostprocessImportanceMapA");
            m_CacaoPostprocessImportanceMapBKernel =
                m_CacaoCompute.FindKernel("FFX_CACAO_PostprocessImportanceMapB");
            m_CacaoApplyKernel = m_CacaoCompute.FindKernel("FFX_CACAO_Apply");
            m_CacaoNonSmartApplyKernel =
                m_CacaoCompute.FindKernel("FFX_CACAO_NonSmartApply");
            m_CacaoNonSmartHalfApplyKernel =
                m_CacaoCompute.FindKernel("FFX_CACAO_NonSmartHalfApply");
            m_CacaoUpscaleSmartKernel =
                m_CacaoCompute.FindKernel("FFX_CACAO_UpscaleBilateral5x5Smart");
            m_CacaoUpscaleNonSmartKernel =
                m_CacaoCompute.FindKernel("FFX_CACAO_UpscaleBilateral5x5NonSmart");
            m_CacaoUpscaleHalfKernel =
                m_CacaoCompute.FindKernel("FFX_CACAO_UpscaleBilateral5x5Half");

            for (int blurPass = 0; blurPass < CacaoMaxBlurPasses; blurPass++)
            {
                m_CacaoBlurKernels[blurPass] =
                    m_CacaoCompute.FindKernel($"FFX_CACAO_EdgeSensitiveBlur{blurPass + 1}");
            }
        }

        private void PrepareCacao(VividCameraData cameraData, bool useCacao)
        {
            if (!useCacao)
            {
                m_CacaoBufferSizeInfo = CreateCacaoBufferSizeInfo(1, 1, false);
                ResizeCacaoResources(m_CacaoBufferSizeInfo);
                for (int pass = 0; pass < CacaoSliceCount; pass++)
                    m_CacaoConstantBuffers[pass] = default;
                return;
            }

            m_CacaoBufferSizeInfo = CreateCacaoBufferSizeInfo(
                m_Width,
                m_Height,
                m_Settings.cacaoDownsampled);
            ResizeCacaoResources(m_CacaoBufferSizeInfo);

            for (int pass = 0; pass < CacaoSliceCount; pass++)
            {
                m_CacaoConstantBuffers[pass] = BuildCacaoConstantBuffer(
                    cameraData,
                    m_Settings,
                    m_CacaoBufferSizeInfo,
                    pass);
            }
        }

        private void RecordCacao(ComputePassContext context)
        {
            var cmd = context.cmd;
            using (new ProfilingScope(cmd, profilingSampler))
            {
                if (!m_Settings.enabled || !CanExecuteCacao())
                    return;

                PushCacaoConstants(cmd, 0);
                cmd.SetComputeTextureParam(
                    m_CacaoCompute,
                    m_CacaoClearLoadCounterKernel,
                    CacaoClearLoadCounterId,
                    m_CacaoLoadCounter.innerHandle);
                cmd.DispatchCompute(m_CacaoCompute, m_CacaoClearLoadCounterKernel, 1, 1, 1);

                DispatchCacaoPrepare(cmd);

                if (m_Settings.qualityLevel == 4)
                    DispatchCacaoAdaptiveBase(cmd);

                DispatchCacaoMain(cmd);

                RenderGraphTexture resolvedSsao = m_CacaoSsaoPing;
                int blurPassCount = Mathf.Clamp(
                    m_Settings.cacaoBlurPasses,
                    0,
                    CacaoMaxBlurPasses);
                if (blurPassCount > 0)
                {
                    DispatchCacaoBlur(cmd, blurPassCount);
                    resolvedSsao = m_CacaoSsaoPong;
                }

                DispatchCacaoOutput(cmd, resolvedSsao);
            }
        }

        private void DispatchCacaoPrepare(ComputeCommandBuffer cmd)
        {
            int qualityLevel = Mathf.Clamp(m_Settings.qualityLevel, 0, 4);
            int prepareDepthKernel = ResolveCacaoPrepareDepthKernel(
                qualityLevel,
                m_Settings.cacaoDownsampled);

            PushCacaoConstants(cmd, 0);
            cmd.SetComputeTextureParam(
                m_CacaoCompute,
                prepareDepthKernel,
                CacaoDepthInputId,
                m_HzbTexture.innerHandle);

            bool prepareMips = qualityLevel >= 2;
            if (prepareMips)
            {
                cmd.SetComputeTextureParam(
                    m_CacaoCompute,
                    prepareDepthKernel,
                    CacaoPreparedDepthMip0Id,
                    m_CacaoDeinterleavedDepths,
                    0);
                cmd.SetComputeTextureParam(
                    m_CacaoCompute,
                    prepareDepthKernel,
                    CacaoPreparedDepthMip1Id,
                    m_CacaoDeinterleavedDepths,
                    1);
                cmd.SetComputeTextureParam(
                    m_CacaoCompute,
                    prepareDepthKernel,
                    CacaoPreparedDepthMip2Id,
                    m_CacaoDeinterleavedDepths,
                    2);
                cmd.SetComputeTextureParam(
                    m_CacaoCompute,
                    prepareDepthKernel,
                    CacaoPreparedDepthMip3Id,
                    m_CacaoDeinterleavedDepths,
                    3);
            }
            else
            {
                cmd.SetComputeTextureParam(
                    m_CacaoCompute,
                    prepareDepthKernel,
                    CacaoPreparedDepthId,
                    m_CacaoDeinterleavedDepths,
                    0);
            }

            cmd.DispatchCompute(
                m_CacaoCompute,
                prepareDepthKernel,
                CoreUtils.DivRoundUp(
                    m_CacaoBufferSizeInfo.DeinterleavedDepthWidth,
                    CacaoPrepareThreadGroupSize),
                CoreUtils.DivRoundUp(
                    m_CacaoBufferSizeInfo.DeinterleavedDepthHeight,
                    CacaoPrepareThreadGroupSize),
                1);

            int prepareNormalsKernel = m_Settings.cacaoDownsampled
                ? m_CacaoPrepareDownsampledNormalsFromInputKernel
                : m_CacaoPrepareNativeNormalsFromInputKernel;
            cmd.SetComputeTextureParam(
                m_CacaoCompute,
                prepareNormalsKernel,
                CacaoNormalInputId,
                m_GBuffer1.innerHandle);
            cmd.SetComputeTextureParam(
                m_CacaoCompute,
                prepareNormalsKernel,
                CacaoPreparedNormalId,
                m_CacaoDeinterleavedNormals.innerHandle);
            cmd.DispatchCompute(
                m_CacaoCompute,
                prepareNormalsKernel,
                CoreUtils.DivRoundUp(
                    m_CacaoBufferSizeInfo.SsaoWidth,
                    CacaoPrepareThreadGroupSize),
                CoreUtils.DivRoundUp(
                    m_CacaoBufferSizeInfo.SsaoHeight,
                    CacaoPrepareThreadGroupSize),
                1);
        }

        private void DispatchCacaoAdaptiveBase(ComputeCommandBuffer cmd)
        {
            for (int pass = 0; pass < CacaoSliceCount; pass++)
            {
                PushCacaoConstants(cmd, pass);
                BindCacaoGenerateTextures(
                    cmd,
                    m_CacaoGenerateQ3BaseKernel,
                    m_CacaoSsaoPong);
                cmd.DispatchCompute(
                    m_CacaoCompute,
                    m_CacaoGenerateQ3BaseKernel,
                    CoreUtils.DivRoundUp(
                        m_CacaoBufferSizeInfo.SsaoWidth,
                        CacaoGenerateThreadGroupSize),
                    CoreUtils.DivRoundUp(
                        m_CacaoBufferSizeInfo.SsaoHeight,
                        CacaoGenerateThreadGroupSize),
                    1);
            }

            PushCacaoConstants(cmd, 0);
            cmd.SetComputeTextureParam(
                m_CacaoCompute,
                m_CacaoGenerateImportanceMapKernel,
                CacaoImportanceFinalSsaoId,
                m_CacaoSsaoPong.innerHandle);
            cmd.SetComputeTextureParam(
                m_CacaoCompute,
                m_CacaoGenerateImportanceMapKernel,
                CacaoImportanceOutId,
                m_CacaoImportanceMap.innerHandle);
            DispatchCacaoImportance(cmd, m_CacaoGenerateImportanceMapKernel);

            cmd.SetComputeTextureParam(
                m_CacaoCompute,
                m_CacaoPostprocessImportanceMapAKernel,
                CacaoImportanceAInputId,
                m_CacaoImportanceMap.innerHandle);
            cmd.SetComputeTextureParam(
                m_CacaoCompute,
                m_CacaoPostprocessImportanceMapAKernel,
                CacaoImportanceAOutputId,
                m_CacaoImportanceMapPong.innerHandle);
            DispatchCacaoImportance(cmd, m_CacaoPostprocessImportanceMapAKernel);

            cmd.SetComputeTextureParam(
                m_CacaoCompute,
                m_CacaoPostprocessImportanceMapBKernel,
                CacaoImportanceBInputId,
                m_CacaoImportanceMapPong.innerHandle);
            cmd.SetComputeTextureParam(
                m_CacaoCompute,
                m_CacaoPostprocessImportanceMapBKernel,
                CacaoImportanceBOutputId,
                m_CacaoImportanceMap.innerHandle);
            cmd.SetComputeTextureParam(
                m_CacaoCompute,
                m_CacaoPostprocessImportanceMapBKernel,
                CacaoImportanceBLoadCounterId,
                m_CacaoLoadCounter.innerHandle);
            DispatchCacaoImportance(cmd, m_CacaoPostprocessImportanceMapBKernel);
        }

        private void DispatchCacaoMain(ComputeCommandBuffer cmd)
        {
            int qualityLevel = Mathf.Clamp(m_Settings.qualityLevel, 0, 4);
            int generateKernel = ResolveCacaoGenerateKernel(qualityLevel);
            bool useSparseDispatch = qualityLevel <= 2;

            int dispatchWidth;
            int dispatchHeight;
            int dispatchDepth;
            if (useSparseDispatch)
            {
                dispatchWidth = CoreUtils.DivRoundUp(
                    m_CacaoBufferSizeInfo.SsaoWidth,
                    CacaoGenerateSparseThreadGroupSizeX);
                dispatchWidth = CoreUtils.DivRoundUp(
                    dispatchWidth,
                    CacaoGenerateSparseDispatchDepth);
                dispatchHeight = CoreUtils.DivRoundUp(
                    m_CacaoBufferSizeInfo.SsaoHeight,
                    CacaoGenerateSparseThreadGroupSizeY);
                dispatchDepth = CacaoGenerateSparseDispatchDepth;
            }
            else
            {
                dispatchWidth = CoreUtils.DivRoundUp(
                    m_CacaoBufferSizeInfo.SsaoWidth,
                    CacaoGenerateThreadGroupSize);
                dispatchHeight = CoreUtils.DivRoundUp(
                    m_CacaoBufferSizeInfo.SsaoHeight,
                    CacaoGenerateThreadGroupSize);
                dispatchDepth = 1;
            }

            for (int pass = 0; pass < CacaoSliceCount; pass++)
            {
                if (qualityLevel == 0 && (pass == 1 || pass == 2))
                    continue;

                PushCacaoConstants(cmd, pass);
                BindCacaoGenerateTextures(cmd, generateKernel, m_CacaoSsaoPing);
                cmd.DispatchCompute(
                    m_CacaoCompute,
                    generateKernel,
                    dispatchWidth,
                    dispatchHeight,
                    dispatchDepth);
            }
        }

        private void DispatchCacaoBlur(ComputeCommandBuffer cmd, int blurPassCount)
        {
            int blurKernel = m_CacaoBlurKernels[blurPassCount - 1];
            int dispatchTileWidth = CacaoSliceCount * CacaoBlurThreadGroupSize
                - 2 * blurPassCount;
            int dispatchTileHeight = 3 * CacaoBlurThreadGroupSize
                - 2 * blurPassCount;
            int dispatchWidth = CoreUtils.DivRoundUp(
                m_CacaoBufferSizeInfo.SsaoWidth,
                dispatchTileWidth);
            int dispatchHeight = CoreUtils.DivRoundUp(
                m_CacaoBufferSizeInfo.SsaoHeight,
                dispatchTileHeight);
            int qualityLevel = Mathf.Clamp(m_Settings.qualityLevel, 0, 4);

            for (int pass = 0; pass < CacaoSliceCount; pass++)
            {
                if (qualityLevel == 0 && (pass == 1 || pass == 2))
                    continue;

                PushCacaoConstants(cmd, pass);
                cmd.SetComputeTextureParam(
                    m_CacaoCompute,
                    blurKernel,
                    CacaoBlurInputId,
                    m_CacaoSsaoPing.innerHandle);
                cmd.SetComputeTextureParam(
                    m_CacaoCompute,
                    blurKernel,
                    CacaoBlurOutputId,
                    m_CacaoSsaoPong.innerHandle);
                cmd.DispatchCompute(
                    m_CacaoCompute,
                    blurKernel,
                    dispatchWidth,
                    dispatchHeight,
                    1);
            }
        }

        private void DispatchCacaoOutput(
            ComputeCommandBuffer cmd,
            RenderGraphTexture resolvedSsao)
        {
            PushCacaoConstants(cmd, 0);
            int qualityLevel = Mathf.Clamp(m_Settings.qualityLevel, 0, 4);

            if (m_Settings.cacaoDownsampled)
            {
                int upscaleKernel = qualityLevel switch
                {
                    0 => m_CacaoUpscaleHalfKernel,
                    1 or 2 => m_CacaoUpscaleNonSmartKernel,
                    _ => m_CacaoUpscaleSmartKernel
                };

                cmd.SetComputeTextureParam(
                    m_CacaoCompute,
                    upscaleKernel,
                    CacaoUpscaleInputId,
                    resolvedSsao.innerHandle);
                cmd.SetComputeTextureParam(
                    m_CacaoCompute,
                    upscaleKernel,
                    CacaoUpscaleDepthId,
                    m_HzbTexture.innerHandle);
                cmd.SetComputeTextureParam(
                    m_CacaoCompute,
                    upscaleKernel,
                    CacaoUpscaleDownscaledDepthId,
                    m_CacaoDeinterleavedDepths,
                    0);
                cmd.SetComputeTextureParam(
                    m_CacaoCompute,
                    upscaleKernel,
                    CacaoUpscaleOutputId,
                    m_GTAOTexture.innerHandle);
                cmd.DispatchCompute(
                    m_CacaoCompute,
                    upscaleKernel,
                    CoreUtils.DivRoundUp(
                        m_CacaoBufferSizeInfo.OutputWidth,
                        CacaoUpscaleThreadGroupSize),
                    CoreUtils.DivRoundUp(
                        m_CacaoBufferSizeInfo.OutputHeight,
                        CacaoUpscaleThreadGroupSize),
                    1);
                return;
            }

            int applyKernel = qualityLevel switch
            {
                0 => m_CacaoNonSmartHalfApplyKernel,
                1 => m_CacaoNonSmartApplyKernel,
                _ => m_CacaoApplyKernel
            };
            cmd.SetComputeTextureParam(
                m_CacaoCompute,
                applyKernel,
                CacaoApplyInputId,
                resolvedSsao.innerHandle);
            cmd.SetComputeTextureParam(
                m_CacaoCompute,
                applyKernel,
                CacaoApplyOutputId,
                m_GTAOTexture.innerHandle);
            cmd.DispatchCompute(
                m_CacaoCompute,
                applyKernel,
                CoreUtils.DivRoundUp(
                    m_CacaoBufferSizeInfo.OutputWidth,
                    CacaoApplyThreadGroupSize),
                CoreUtils.DivRoundUp(
                    m_CacaoBufferSizeInfo.OutputHeight,
                    CacaoApplyThreadGroupSize),
                1);
        }

        private void BindCacaoGenerateTextures(
            ComputeCommandBuffer cmd,
            int kernel,
            RenderGraphTexture output)
        {
            cmd.SetComputeTextureParam(
                m_CacaoCompute,
                kernel,
                CacaoViewspaceDepthId,
                m_CacaoDeinterleavedDepths.innerHandle);
            cmd.SetComputeTextureParam(
                m_CacaoCompute,
                kernel,
                CacaoDeinterleavedNormalsId,
                m_CacaoDeinterleavedNormals.innerHandle);
            cmd.SetComputeTextureParam(
                m_CacaoCompute,
                kernel,
                CacaoLoadCounterId,
                m_CacaoLoadCounter.innerHandle);
            cmd.SetComputeTextureParam(
                m_CacaoCompute,
                kernel,
                CacaoImportanceMapId,
                m_CacaoImportanceMap.innerHandle);
            cmd.SetComputeTextureParam(
                m_CacaoCompute,
                kernel,
                CacaoFinalSsaoId,
                m_CacaoSsaoPong.innerHandle);
            cmd.SetComputeTextureParam(
                m_CacaoCompute,
                kernel,
                CacaoSsaoOutputId,
                output.innerHandle);
        }

        private void DispatchCacaoImportance(ComputeCommandBuffer cmd, int kernel)
        {
            cmd.DispatchCompute(
                m_CacaoCompute,
                kernel,
                CoreUtils.DivRoundUp(
                    m_CacaoBufferSizeInfo.ImportanceWidth,
                    CacaoImportanceThreadGroupSize),
                CoreUtils.DivRoundUp(
                    m_CacaoBufferSizeInfo.ImportanceHeight,
                    CacaoImportanceThreadGroupSize),
                1);
        }

        private void PushCacaoConstants(ComputeCommandBuffer cmd, int pass)
        {
            ConstantBuffer.Push(
                cmd,
                m_CacaoConstantBuffers[Mathf.Clamp(pass, 0, CacaoSliceCount - 1)],
                m_CacaoCompute,
                CacaoConstantBufferId);
        }

        private bool CanExecuteCacao()
        {
            bool requiresDepthMips = Mathf.Clamp(m_Settings.qualityLevel, 0, 4) >= 2;
            bool hasRequiredDepthMips = !requiresDepthMips
                || Mathf.Max(
                    m_CacaoBufferSizeInfo.DeinterleavedDepthWidth,
                    m_CacaoBufferSizeInfo.DeinterleavedDepthHeight) >= 8;
            bool requiresAdaptiveKernels =
                Mathf.Clamp(m_Settings.qualityLevel, 0, 4) == 4;
            bool hasAdaptiveKernels = !requiresAdaptiveKernels
                || (
                    m_CacaoGenerateQ3BaseKernel >= 0
                    && m_CacaoGenerateImportanceMapKernel >= 0
                    && m_CacaoPostprocessImportanceMapAKernel >= 0
                    && m_CacaoPostprocessImportanceMapBKernel >= 0);

            if (m_CacaoCompute == null
                || !hasRequiredDepthMips
                || !hasAdaptiveKernels
                || m_CacaoClearLoadCounterKernel < 0
                || ResolveCacaoPrepareDepthKernel(
                    m_Settings.qualityLevel,
                    m_Settings.cacaoDownsampled) < 0
                || ResolveCacaoGenerateKernel(m_Settings.qualityLevel) < 0
                || m_CacaoPrepareDownsampledNormalsFromInputKernel < 0
                || m_CacaoPrepareNativeNormalsFromInputKernel < 0
                || m_CacaoApplyKernel < 0
                || m_CacaoNonSmartApplyKernel < 0
                || m_CacaoNonSmartHalfApplyKernel < 0
                || m_CacaoUpscaleSmartKernel < 0
                || m_CacaoUpscaleNonSmartKernel < 0
                || m_CacaoUpscaleHalfKernel < 0)
            {
                return false;
            }

            for (int blurPass = 0; blurPass < m_CacaoBlurKernels.Length; blurPass++)
            {
                if (m_CacaoBlurKernels[blurPass] < 0)
                    return false;
            }

            return m_HzbTexture?.innerHandle.IsValid() == true
                && m_GBuffer1?.innerHandle.IsValid() == true
                && m_GTAOTexture?.innerHandle.IsValid() == true
                && m_CacaoDeinterleavedDepths?.innerHandle.IsValid() == true
                && m_CacaoDeinterleavedNormals?.innerHandle.IsValid() == true
                && m_CacaoSsaoPing?.innerHandle.IsValid() == true
                && m_CacaoSsaoPong?.innerHandle.IsValid() == true
                && m_CacaoImportanceMap?.innerHandle.IsValid() == true
                && m_CacaoImportanceMapPong?.innerHandle.IsValid() == true
                && m_CacaoLoadCounter?.innerHandle.IsValid() == true;
        }

        private int ResolveCacaoPrepareDepthKernel(int qualityLevel, bool downsampled)
        {
            qualityLevel = Mathf.Clamp(qualityLevel, 0, 4);
            if (qualityLevel == 0)
            {
                return downsampled
                    ? m_CacaoPrepareDownsampledDepthsHalfKernel
                    : m_CacaoPrepareNativeDepthsHalfKernel;
            }

            if (qualityLevel == 1)
            {
                return downsampled
                    ? m_CacaoPrepareDownsampledDepthsKernel
                    : m_CacaoPrepareNativeDepthsKernel;
            }

            return downsampled
                ? m_CacaoPrepareDownsampledDepthsAndMipsKernel
                : m_CacaoPrepareNativeDepthsAndMipsKernel;
        }

        private int ResolveCacaoGenerateKernel(int qualityLevel)
        {
            return Mathf.Clamp(qualityLevel, 0, 4) switch
            {
                0 or 1 => m_CacaoGenerateQ0Kernel,
                2 => m_CacaoGenerateQ1Kernel,
                3 => m_CacaoGenerateQ2Kernel,
                _ => m_CacaoGenerateQ3Kernel
            };
        }

        private void ResizeCacaoResources(CacaoBufferSizeInfo sizeInfo)
        {
            ConfigureCacaoTexture(
                m_CacaoDeinterleavedDepths,
                sizeInfo.DeinterleavedDepthWidth,
                sizeInfo.DeinterleavedDepthHeight,
                GraphicsFormat.R16_SFloat,
                CacaoSliceCount,
                CacaoDepthMipCount);
            ConfigureCacaoTexture(
                m_CacaoDeinterleavedNormals,
                sizeInfo.SsaoWidth,
                sizeInfo.SsaoHeight,
                GraphicsFormat.R8G8B8A8_SNorm,
                CacaoSliceCount);
            ConfigureCacaoTexture(
                m_CacaoSsaoPing,
                sizeInfo.SsaoWidth,
                sizeInfo.SsaoHeight,
                GraphicsFormat.R8G8_UNorm,
                CacaoSliceCount);
            ConfigureCacaoTexture(
                m_CacaoSsaoPong,
                sizeInfo.SsaoWidth,
                sizeInfo.SsaoHeight,
                GraphicsFormat.R8G8_UNorm,
                CacaoSliceCount);
            ConfigureCacaoTexture(
                m_CacaoImportanceMap,
                sizeInfo.ImportanceWidth,
                sizeInfo.ImportanceHeight,
                GraphicsFormat.R8_UNorm);
            ConfigureCacaoTexture(
                m_CacaoImportanceMapPong,
                sizeInfo.ImportanceWidth,
                sizeInfo.ImportanceHeight,
                GraphicsFormat.R8_UNorm);
            ConfigureCacaoTexture(
                m_CacaoLoadCounter,
                1,
                1,
                GraphicsFormat.R32_UInt);
        }

        private static CacaoBufferSizeInfo CreateCacaoBufferSizeInfo(
            int width,
            int height,
            bool downsampled)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            int halfWidth = (width + 1) / 2;
            int halfHeight = (height + 1) / 2;
            int quarterWidth = (halfWidth + 1) / 2;
            int quarterHeight = (halfHeight + 1) / 2;
            int eighthWidth = (quarterWidth + 1) / 2;
            int eighthHeight = (quarterHeight + 1) / 2;

            return downsampled
                ? new CacaoBufferSizeInfo(
                    width,
                    height,
                    quarterWidth,
                    quarterHeight,
                    quarterWidth,
                    quarterHeight,
                    eighthWidth,
                    eighthHeight)
                : new CacaoBufferSizeInfo(
                    width,
                    height,
                    halfWidth,
                    halfHeight,
                    halfWidth,
                    halfHeight,
                    quarterWidth,
                    quarterHeight);
        }

        private static CacaoConstantBufferData BuildCacaoConstantBuffer(
            VividCameraData cameraData,
            GTAOSettingsData settings,
            CacaoBufferSizeInfo sizeInfo,
            int pass)
        {
            Matrix4x4 projection = cameraData != null
                ? cameraData.GetGPUProjectionMatrix(renderIntoTexture: true)
                : Matrix4x4.Perspective(
                    60.0f,
                    Mathf.Max(
                        sizeInfo.OutputWidth / (float)Mathf.Max(sizeInfo.OutputHeight, 1),
                        0.0001f),
                    0.1f,
                    1000.0f);
            bool isOrthographic = cameraData?.camera != null && cameraData.camera.orthographic;
            ProjectionData projectionData = DecomposeProjection(projection, isOrthographic);
            if (!projectionData.IsLeftHanded)
            {
                projection = ConvertProjectionToLeftHanded(projection);
                projectionData = DecomposeProjection(projection, isOrthographic);
            }

            Vector2 ndcToViewAdd = new(
                projectionData.Frustum.x,
                projectionData.Frustum.y);
            Vector2 ndcToViewMul = new(
                projectionData.Frustum.z,
                projectionData.Frustum.w);
            Vector2 cameraTanHalfFov = new(
                Mathf.Max(Mathf.Abs(ndcToViewMul.x) * 0.5f, 0.0001f),
                Mathf.Max(Mathf.Abs(ndcToViewMul.y) * 0.5f, 0.0001f));
            Vector2 depthUnpackConsts = new(-projection[3, 2], projection[2, 2]);
            if (depthUnpackConsts.x * depthUnpackConsts.y < 0.0f)
                depthUnpackConsts.y = -depthUnpackConsts.y;

            float radius = Mathf.Clamp(settings.radius, 0.0001f, 100000.0f);
            float fadeRange = Mathf.Max(
                settings.cacaoFadeOutTo - settings.cacaoFadeOutFrom,
                0.001f);
            float effectSamplingRadiusNearLimit = radius * 1.2f;
            if (settings.qualityLevel <= 1)
            {
                effectSamplingRadiusNearLimit *= 1.5f;
                if (settings.qualityLevel == 0)
                    radius *= 0.8f;
            }

            effectSamplingRadiusNearLimit /= cameraTanHalfFov.y;
            float importancePixelCount = Mathf.Max(
                sizeInfo.ImportanceWidth * sizeInfo.ImportanceHeight,
                1);

            var constants = new CacaoConstantBufferData
            {
                DepthUnpackConsts = depthUnpackConsts,
                CameraTanHalfFOV = cameraTanHalfFov,
                NDCToViewMul = ndcToViewMul,
                NDCToViewAdd = ndcToViewAdd,
                DepthBufferUVToViewMul = ndcToViewMul,
                DepthBufferUVToViewAdd = ndcToViewAdd,
                EffectRadius = radius,
                EffectShadowStrength = Mathf.Clamp(
                    settings.cacaoShadowMultiplier * 4.3f,
                    0.0f,
                    10.0f),
                EffectShadowPow = Mathf.Clamp(settings.cacaoShadowPower, 0.0f, 10.0f),
                EffectShadowClamp = Mathf.Clamp01(settings.cacaoShadowClamp),
                EffectFadeOutMul = -1.0f / fadeRange,
                EffectFadeOutAdd = settings.cacaoFadeOutFrom / fadeRange + 1.0f,
                EffectHorizonAngleThreshold = Mathf.Clamp01(
                    settings.cacaoHorizonAngleThreshold),
                EffectSamplingRadiusNearLimitRec = 1.0f
                    / Mathf.Max(effectSamplingRadiusNearLimit, 0.0001f),
                DepthPrecisionOffsetMod = 0.9992f,
                NegRecEffectRadius = -1.0f / radius,
                LoadCounterAvgDiv = 9.0f / (importancePixelCount * 255.0f),
                AdaptiveSampleCountLimit = Mathf.Clamp01(
                    settings.cacaoAdaptiveQualityLimit),
                InvSharpness = Mathf.Clamp01(1.0f - settings.cacaoSharpness),
                PassIndex = Mathf.Clamp(pass, 0, CacaoSliceCount - 1),
                BilateralSigmaSquared = Mathf.Max(
                    settings.cacaoBilateralSigmaSquared,
                    0.0001f),
                BilateralSimilarityDistanceSigma = Mathf.Max(
                    settings.cacaoBilateralSimilarityDistanceSigma,
                    0.0001f),
                NormalsUnpackMul = 2.0f,
                NormalsUnpackAdd = -1.0f,
                DetailAOStrength = Mathf.Clamp(
                    settings.cacaoDetailShadowStrength,
                    0.0f,
                    5.0f),
                Dummy0 = 0.0f,
                SSAOBufferDimensions = new Vector2(sizeInfo.SsaoWidth, sizeInfo.SsaoHeight),
                SSAOBufferInverseDimensions = InverseDimensions(
                    sizeInfo.SsaoWidth,
                    sizeInfo.SsaoHeight),
                DepthBufferDimensions = new Vector2(
                    sizeInfo.OutputWidth,
                    sizeInfo.OutputHeight),
                DepthBufferInverseDimensions = InverseDimensions(
                    sizeInfo.OutputWidth,
                    sizeInfo.OutputHeight),
                DepthBufferOffsetX = 0,
                DepthBufferOffsetY = 0,
                PerPassFullResUVOffset = new Vector2(
                    (pass % 2) / (float)Mathf.Max(sizeInfo.SsaoWidth, 1),
                    (pass / 2) / (float)Mathf.Max(sizeInfo.SsaoHeight, 1)),
                OutputBufferDimensions = new Vector2(
                    sizeInfo.OutputWidth,
                    sizeInfo.OutputHeight),
                OutputBufferInverseDimensions = InverseDimensions(
                    sizeInfo.OutputWidth,
                    sizeInfo.OutputHeight),
                ImportanceMapDimensions = new Vector2(
                    sizeInfo.ImportanceWidth,
                    sizeInfo.ImportanceHeight),
                ImportanceMapInverseDimensions = InverseDimensions(
                    sizeInfo.ImportanceWidth,
                    sizeInfo.ImportanceHeight),
                DeinterleavedDepthBufferDimensions = new Vector2(
                    sizeInfo.DeinterleavedDepthWidth,
                    sizeInfo.DeinterleavedDepthHeight),
                DeinterleavedDepthBufferInverseDimensions = InverseDimensions(
                    sizeInfo.DeinterleavedDepthWidth,
                    sizeInfo.DeinterleavedDepthHeight),
                DeinterleavedDepthBufferOffset = Vector2.zero,
                DeinterleavedDepthBufferNormalisedOffset = Vector2.zero,
                NormalsWorldToViewspaceMatrix = Matrix4x4.identity
            };

            SetCacaoPatternMatrices(ref constants, pass);
            return constants;
        }

        private static void SetCacaoPatternMatrices(
            ref CacaoConstantBufferData constants,
            int pass)
        {
            for (int subPass = 0; subPass < CacaoSubPassMap.Length; subPass++)
            {
                int mappedSubPass = CacaoSubPassMap[subPass];
                float angle = (
                    pass
                    + mappedSubPass / (float)CacaoSubPassMap.Length)
                    * Mathf.PI
                    * 0.5f;
                float cosine = Mathf.Cos(angle);
                float sine = Mathf.Sin(angle);
                float scale = 1.0f
                    + (
                        pass
                        - 1.5f
                        + (mappedSubPass - (CacaoSubPassMap.Length - 1.0f) * 0.5f)
                        / CacaoSubPassMap.Length)
                    * 0.07f;
                Vector4 matrix = new(
                    scale * cosine,
                    scale * -sine,
                    -scale * sine,
                    -scale * cosine);

                switch (subPass)
                {
                    case 0:
                        constants.PatternRotScaleMatrix0 = matrix;
                        break;
                    case 1:
                        constants.PatternRotScaleMatrix1 = matrix;
                        break;
                    case 2:
                        constants.PatternRotScaleMatrix2 = matrix;
                        break;
                    case 3:
                        constants.PatternRotScaleMatrix3 = matrix;
                        break;
                    default:
                        constants.PatternRotScaleMatrix4 = matrix;
                        break;
                }
            }
        }

        private static Vector2 InverseDimensions(int width, int height)
        {
            return new Vector2(
                1.0f / Mathf.Max(width, 1),
                1.0f / Mathf.Max(height, 1));
        }

        private static RenderGraphTexture CreateCacaoTexture(
            string name,
            GraphicsFormat format,
            int slices = 1,
            int mipCount = 1)
        {
            var texture = CreateTexture(name, format);
            ConfigureCacaoTexture(texture, 1, 1, format, slices, mipCount);
            return texture;
        }

        private static void ConfigureCacaoTexture(
            RenderGraphTexture texture,
            int width,
            int height,
            GraphicsFormat format,
            int slices = 1,
            int mipCount = 1)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = Mathf.Max(width, 1);
            texture.desc.Height = Mathf.Max(height, 1);
            texture.desc.Slices = Mathf.Max(slices, 1);
            texture.desc.Dimension = slices > 1
                ? TextureDimension.Tex2DArray
                : TextureDimension.Tex2D;
            texture.desc.ColorFormat = format;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.EnableRandomWrite = true;
            texture.desc.UseMipMap = mipCount > 1;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = Mathf.Max(mipCount, 1);
            texture.desc.ClearBuffer = false;
            texture.desc.ClearColor = Color.clear;
        }

        private void DisposeCacao()
        {
            m_CacaoCompute = null;
            m_CacaoClearLoadCounterKernel = -1;
            m_CacaoPrepareDownsampledDepthsKernel = -1;
            m_CacaoPrepareNativeDepthsKernel = -1;
            m_CacaoPrepareDownsampledDepthsAndMipsKernel = -1;
            m_CacaoPrepareNativeDepthsAndMipsKernel = -1;
            m_CacaoPrepareDownsampledNormalsFromInputKernel = -1;
            m_CacaoPrepareNativeNormalsFromInputKernel = -1;
            m_CacaoPrepareDownsampledDepthsHalfKernel = -1;
            m_CacaoPrepareNativeDepthsHalfKernel = -1;
            m_CacaoGenerateQ0Kernel = -1;
            m_CacaoGenerateQ1Kernel = -1;
            m_CacaoGenerateQ2Kernel = -1;
            m_CacaoGenerateQ3Kernel = -1;
            m_CacaoGenerateQ3BaseKernel = -1;
            m_CacaoGenerateImportanceMapKernel = -1;
            m_CacaoPostprocessImportanceMapAKernel = -1;
            m_CacaoPostprocessImportanceMapBKernel = -1;
            m_CacaoApplyKernel = -1;
            m_CacaoNonSmartApplyKernel = -1;
            m_CacaoNonSmartHalfApplyKernel = -1;
            m_CacaoUpscaleSmartKernel = -1;
            m_CacaoUpscaleNonSmartKernel = -1;
            m_CacaoUpscaleHalfKernel = -1;
            m_CacaoBufferSizeInfo = default;

            for (int index = 0; index < m_CacaoBlurKernels.Length; index++)
                m_CacaoBlurKernels[index] = -1;

            for (int index = 0; index < m_CacaoConstantBuffers.Length; index++)
                m_CacaoConstantBuffers[index] = default;
        }
    }
}

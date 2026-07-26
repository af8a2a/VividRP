using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    internal enum BloomFftExecutionPath
    {
        MultiDispatch,
        Wave32,
        Wave64
    }

    internal readonly struct BloomFftDomain
    {
        public readonly int ImageWidth;
        public readonly int ImageHeight;
        public readonly int KernelSize;
        public readonly int Padding;
        public readonly int FrequencyWidth;
        public readonly int FrequencyHeight;
        public readonly int Log2Width;
        public readonly int Log2Height;

        public BloomFftDomain(
            int imageWidth,
            int imageHeight,
            int kernelSize,
            int padding,
            int frequencyWidth,
            int frequencyHeight)
        {
            ImageWidth = imageWidth;
            ImageHeight = imageHeight;
            KernelSize = kernelSize;
            Padding = padding;
            FrequencyWidth = frequencyWidth;
            FrequencyHeight = frequencyHeight;
            Log2Width = IntegerLog2(frequencyWidth);
            Log2Height = IntegerLog2(frequencyHeight);
        }

        public bool IsValid =>
            ImageWidth > 0
            && ImageHeight > 0
            && FrequencyWidth >= ImageWidth
            && FrequencyHeight >= ImageHeight;

        public int TransformStageCount => Log2Width + Log2Height;

        private static int IntegerLog2(int value)
        {
            int result = 0;
            while ((1 << result) < value)
                result++;

            return result;
        }
    }

    public partial class BloomPass
    {
        private const int k_MaxFftSize = 4096;
        private const int k_MaxWaveFftSize = 2048;
        private const int k_MaxFftReductionLevels = 13;

        private static readonly int FftSizeId = Shader.PropertyToID("_FFTSize");
        private static readonly int FftImageSizeId = Shader.PropertyToID("_FFTImageSize");
        private static readonly int FftStageId = Shader.PropertyToID("_FFTStage");
        private static readonly int FftInverseId = Shader.PropertyToID("_FFTInverse");
        private static readonly int FftKernelParamsId = Shader.PropertyToID("_FFTKernelParams");
        private static readonly int FftResolveScaleId = Shader.PropertyToID("_FFTResolveScale");
        private static readonly int FftInputSizeId = Shader.PropertyToID("_FFTInputSize");
        private static readonly int FftOutputSizeId = Shader.PropertyToID("_FFTOutputSize");
        private static readonly int FftSourceTextureId = Shader.PropertyToID("_FFTSourceTexture");
        private static readonly int FftKernelTextureId = Shader.PropertyToID("_FFTKernelTexture");
        private static readonly int FftRealInputId = Shader.PropertyToID("_FFTRealInput");
        private static readonly int FftImagInputId = Shader.PropertyToID("_FFTImagInput");
        private static readonly int FftRealOutputId = Shader.PropertyToID("_FFTRealOutput");
        private static readonly int FftImagOutputId = Shader.PropertyToID("_FFTImagOutput");
        private static readonly int FftKernelRealId = Shader.PropertyToID("_FFTKernelReal");
        private static readonly int FftKernelImagId = Shader.PropertyToID("_FFTKernelImag");
        private static readonly int FftEnergyInputId = Shader.PropertyToID("_FFTEnergyInput");
        private static readonly int FftEnergyOutputId = Shader.PropertyToID("_FFTEnergyOutput");
        private static readonly int FftKernelEnergyId = Shader.PropertyToID("_FFTKernelEnergy");
        private static readonly int FftOutputTextureId = Shader.PropertyToID("_FFTOutputTexture");

        private readonly RTHandle[] m_FftRealHandles = new RTHandle[2];
        private readonly RTHandle[] m_FftImagHandles = new RTHandle[2];
        private readonly RTHandle[] m_FftKernelRealHandles = new RTHandle[2];
        private readonly RTHandle[] m_FftKernelImagHandles = new RTHandle[2];
        private readonly RTHandle[] m_FftEnergyHandles = new RTHandle[k_MaxFftReductionLevels];

        private readonly TextureHandle[] m_FftRealTH = new TextureHandle[2];
        private readonly TextureHandle[] m_FftImagTH = new TextureHandle[2];
        private readonly TextureHandle[] m_FftKernelRealTH = new TextureHandle[2];
        private readonly TextureHandle[] m_FftKernelImagTH = new TextureHandle[2];
        private readonly TextureHandle[] m_FftEnergyTH = new TextureHandle[k_MaxFftReductionLevels];

        private ComputeShader m_FftCS;
        private int m_FftPrepareSourceKernel = -1;
        private int m_FftPrepareKernelKernel = -1;
        private int m_FftHorizontalKernel = -1;
        private int m_FftVerticalKernel = -1;
        private int m_FftWaveHorizontal32Kernel = -1;
        private int m_FftWaveVertical32Kernel = -1;
        private int m_FftWaveHorizontal64Kernel = -1;
        private int m_FftWaveVertical64Kernel = -1;
        private int m_FftSelectedWaveHorizontalKernel = -1;
        private int m_FftSelectedWaveVerticalKernel = -1;
        private int m_FftMultiplyKernel = -1;
        private int m_FftResolveKernel = -1;
        private int m_FftReduceEnergyKernel = -1;

        private BloomFftDomain m_FftDomain;
        private int m_FftEnergyLevelCount;
        private bool m_UseFftConvolution;
        private bool m_FftKernelsReady;
        private bool m_FftKernelCacheValid;
        private bool m_FftKernelNeedsUpdate;
        private BloomFftExecutionPath m_FftExecutionPath;

        private Texture m_CachedFftKernel;
        private Hash128 m_CachedFftKernelHash;
        private int m_CachedFftFrequencyWidth;
        private int m_CachedFftFrequencyHeight;
        private int m_CachedFftImageWidth;
        private int m_CachedFftImageHeight;
        private int m_CachedFftKernelSize;
        private float m_CachedFftKernelClamp;
        private Vector2 m_CachedFftKernelCenter;
        private BloomFftExecutionPath m_CachedFftExecutionPath;

        internal static bool ShouldUseFftConvolution(
            bool requested,
            bool hasKernel,
            bool kernelsReady)
        {
            return requested && hasKernel && kernelsReady;
        }

        internal static BloomFftExecutionPath ResolveFftExecutionPath(
            int computeSubGroupSize,
            int frequencyWidth,
            int frequencyHeight,
            bool hasWave32Kernels,
            bool hasWave64Kernels)
        {
            int minimumAxis = Mathf.Min(frequencyWidth, frequencyHeight);
            int maximumAxis = Mathf.Max(frequencyWidth, frequencyHeight);
            if (maximumAxis > k_MaxWaveFftSize)
                return BloomFftExecutionPath.MultiDispatch;

            if (computeSubGroupSize == 64
                && minimumAxis >= 64
                && hasWave64Kernels)
                return BloomFftExecutionPath.Wave64;

            if (computeSubGroupSize == 32
                && minimumAxis >= 32
                && hasWave32Kernels)
                return BloomFftExecutionPath.Wave32;

            return BloomFftExecutionPath.MultiDispatch;
        }

        internal static int GetFftTransformOutputIndex(
            int initialIndex,
            BloomFftExecutionPath executionPath,
            int multiDispatchStageCount)
        {
            if (executionPath != BloomFftExecutionPath.MultiDispatch)
                return initialIndex;

            return initialIndex ^ (multiDispatchStageCount & 1);
        }

        internal static BloomFftDomain CalculateFftDomain(
            int screenWidth,
            int screenHeight,
            float resolutionScale,
            float convolutionSize,
            float bufferScale)
        {
            int imageWidth = Mathf.Max(1, Mathf.CeilToInt(screenWidth * Mathf.Clamp(resolutionScale, 0.1f, 0.5f)));
            int imageHeight = Mathf.Max(1, Mathf.CeilToInt(screenHeight * Mathf.Clamp(resolutionScale, 0.1f, 0.5f)));

            int imageMajorAxis = Mathf.Max(imageWidth, imageHeight);
            if (imageMajorAxis > k_MaxFftSize)
            {
                float downscale = k_MaxFftSize / (float)imageMajorAxis;
                imageWidth = Mathf.Max(1, Mathf.FloorToInt(imageWidth * downscale));
                imageHeight = Mathf.Max(1, Mathf.FloorToInt(imageHeight * downscale));
                imageMajorAxis = Mathf.Max(imageWidth, imageHeight);
            }

            float clampedConvolutionSize = Mathf.Clamp01(convolutionSize);
            int kernelSize = Mathf.Max(1, Mathf.CeilToInt(clampedConvolutionSize * imageMajorAxis));
            float supportScale = bufferScale > 0f
                ? Mathf.Min(clampedConvolutionSize, Mathf.Clamp01(bufferScale))
                : clampedConvolutionSize;
            int padding = Mathf.CeilToInt(0.5f * supportScale * imageMajorAxis);
            padding = Mathf.Min(padding, Mathf.Max(0, k_MaxFftSize - imageMajorAxis));

            int paddedWidth = Mathf.Min(
                k_MaxFftSize,
                Mathf.Max(imageWidth + padding, kernelSize));
            int paddedHeight = Mathf.Min(
                k_MaxFftSize,
                Mathf.Max(imageHeight + padding, kernelSize));

            int frequencyWidth = Mathf.Min(k_MaxFftSize, Mathf.NextPowerOfTwo(paddedWidth));
            int frequencyHeight = Mathf.Min(k_MaxFftSize, Mathf.NextPowerOfTwo(paddedHeight));

            return new BloomFftDomain(
                imageWidth,
                imageHeight,
                kernelSize,
                padding,
                frequencyWidth,
                frequencyHeight);
        }

        private void InitializeFftKernels(ComputeShader computeShader)
        {
            m_FftCS = computeShader;
            m_FftKernelsReady = false;
            if (m_FftCS == null)
                return;

            try
            {
                m_FftPrepareSourceKernel = m_FftCS.FindKernel("KFFTPrepareSource");
                m_FftPrepareKernelKernel = m_FftCS.FindKernel("KFFTPrepareKernel");
                m_FftHorizontalKernel = m_FftCS.FindKernel("KFFTStageHorizontal");
                m_FftVerticalKernel = m_FftCS.FindKernel("KFFTStageVertical");
                m_FftMultiplyKernel = m_FftCS.FindKernel("KFFTMultiplyAndBitReverse");
                m_FftResolveKernel = m_FftCS.FindKernel("KFFTResolve");
                m_FftReduceEnergyKernel = m_FftCS.FindKernel("KFFTReduceEnergy");
                m_FftKernelsReady = true;

                m_FftWaveHorizontal32Kernel = TryFindFftKernel(m_FftCS, "KFFTWaveHorizontal32");
                m_FftWaveVertical32Kernel = TryFindFftKernel(m_FftCS, "KFFTWaveVertical32");
                m_FftWaveHorizontal64Kernel = TryFindFftKernel(m_FftCS, "KFFTWaveHorizontal64");
                m_FftWaveVertical64Kernel = TryFindFftKernel(m_FftCS, "KFFTWaveVertical64");
            }
            catch (ArgumentException)
            {
                m_FftPrepareSourceKernel = -1;
                m_FftPrepareKernelKernel = -1;
                m_FftHorizontalKernel = -1;
                m_FftVerticalKernel = -1;
                m_FftWaveHorizontal32Kernel = -1;
                m_FftWaveVertical32Kernel = -1;
                m_FftWaveHorizontal64Kernel = -1;
                m_FftWaveVertical64Kernel = -1;
                m_FftMultiplyKernel = -1;
                m_FftResolveKernel = -1;
                m_FftReduceEnergyKernel = -1;
            }
        }

        private static int TryFindFftKernel(ComputeShader computeShader, string kernelName)
        {
            return computeShader.HasKernel(kernelName)
                ? computeShader.FindKernel(kernelName)
                : -1;
        }

        private bool PrepareFftResources()
        {
            m_FftDomain = CalculateFftDomain(
                m_ScreenWidth,
                m_ScreenHeight,
                m_Settings.convolutionResolutionScale,
                m_Settings.convolutionSize,
                m_Settings.convolutionBufferScale);
            if (!m_FftDomain.IsValid)
                return false;

            m_FftExecutionPath = ResolveFftExecutionPath(
                SystemInfo.computeSubGroupSize,
                m_FftDomain.FrequencyWidth,
                m_FftDomain.FrequencyHeight,
                m_FftWaveHorizontal32Kernel >= 0 && m_FftWaveVertical32Kernel >= 0,
                m_FftWaveHorizontal64Kernel >= 0 && m_FftWaveVertical64Kernel >= 0);
            SelectWaveFftKernels();

            int availableMipCount = Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Log(Mathf.Max(m_FftDomain.ImageWidth, m_FftDomain.ImageHeight), 2f)) - 2,
                1,
                k_MaxBloomMipCount);
            m_ScreenSpaceLensFlareBloomMip = Mathf.Clamp(
                m_ScreenSpaceLensFlareSettings.bloomMip,
                0,
                availableMipCount - 1);

            ConfigureOutputTexture(
                bloomTexture,
                m_FftDomain.ImageWidth,
                m_FftDomain.ImageHeight,
                "BloomTexture");
            ConfigureOutputTexture(
                screenSpaceLensFlareBloomMipTexture,
                Mathf.Max(1, m_FftDomain.ImageWidth >> m_ScreenSpaceLensFlareBloomMip),
                Mathf.Max(1, m_FftDomain.ImageHeight >> m_ScreenSpaceLensFlareBloomMip),
                "ScreenSpaceLensFlareBloomMipTexture");

            bool resourcesChanged = false;
            for (int i = 0; i < 2; i++)
            {
                resourcesChanged |= EnsureFftHandle(
                    ref m_FftRealHandles[i],
                    m_FftDomain.FrequencyWidth,
                    m_FftDomain.FrequencyHeight,
                    $"BloomFFTReal{i}");
                resourcesChanged |= EnsureFftHandle(
                    ref m_FftImagHandles[i],
                    m_FftDomain.FrequencyWidth,
                    m_FftDomain.FrequencyHeight,
                    $"BloomFFTImag{i}");
                resourcesChanged |= EnsureFftHandle(
                    ref m_FftKernelRealHandles[i],
                    m_FftDomain.FrequencyWidth,
                    m_FftDomain.FrequencyHeight,
                    $"BloomFFTKernelReal{i}");
                resourcesChanged |= EnsureFftHandle(
                    ref m_FftKernelImagHandles[i],
                    m_FftDomain.FrequencyWidth,
                    m_FftDomain.FrequencyHeight,
                    $"BloomFFTKernelImag{i}");

                m_FftRealTH[i] = Import(m_FftRealHandles[i]);
                m_FftImagTH[i] = Import(m_FftImagHandles[i]);
                m_FftKernelRealTH[i] = Import(m_FftKernelRealHandles[i]);
                m_FftKernelImagTH[i] = Import(m_FftKernelImagHandles[i]);
            }

            int reductionWidth = m_FftDomain.FrequencyWidth;
            int reductionHeight = m_FftDomain.FrequencyHeight;
            m_FftEnergyLevelCount = 0;
            while ((reductionWidth > 1 || reductionHeight > 1)
                   && m_FftEnergyLevelCount < k_MaxFftReductionLevels)
            {
                reductionWidth = Mathf.Max(1, (reductionWidth + 1) / 2);
                reductionHeight = Mathf.Max(1, (reductionHeight + 1) / 2);
                resourcesChanged |= EnsureFftHandle(
                    ref m_FftEnergyHandles[m_FftEnergyLevelCount],
                    reductionWidth,
                    reductionHeight,
                    $"BloomFFTKernelEnergy{m_FftEnergyLevelCount}",
                    GraphicsFormat.R32G32B32A32_SFloat);
                m_FftEnergyTH[m_FftEnergyLevelCount] = Import(m_FftEnergyHandles[m_FftEnergyLevelCount]);
                m_FftEnergyLevelCount++;
            }

            for (int i = m_FftEnergyLevelCount; i < k_MaxFftReductionLevels; i++)
            {
                if (m_FftEnergyHandles[i] == null)
                    continue;

                m_FftEnergyHandles[i].Release();
                m_FftEnergyHandles[i] = null;
                resourcesChanged = true;
            }

            Texture kernel = m_Settings.convolutionKernel;
            Hash128 kernelHash = kernel.imageContentsHash;
            m_FftKernelNeedsUpdate = resourcesChanged
                || !m_FftKernelCacheValid
                || m_CachedFftKernel != kernel
                || m_CachedFftKernelHash != kernelHash
                || m_CachedFftFrequencyWidth != m_FftDomain.FrequencyWidth
                || m_CachedFftFrequencyHeight != m_FftDomain.FrequencyHeight
                || m_CachedFftImageWidth != m_FftDomain.ImageWidth
                || m_CachedFftImageHeight != m_FftDomain.ImageHeight
                || m_CachedFftKernelSize != m_FftDomain.KernelSize
                || !Mathf.Approximately(m_CachedFftKernelClamp, m_Settings.convolutionKernelClamp)
                || m_CachedFftKernelCenter != m_Settings.convolutionCenter
                || m_CachedFftExecutionPath != m_FftExecutionPath;

            if (resourcesChanged)
                m_FftKernelCacheValid = false;

            return m_FftEnergyLevelCount > 0;
        }

        private void ExecuteFftBloom(CommandBuffer cmd)
        {
            SetFftDomainParameters(cmd);

            int kernelSpectralIndex = GetFftTransformOutputIndex(
                0,
                m_FftExecutionPath,
                m_FftDomain.TransformStageCount);
            if (m_FftKernelNeedsUpdate)
            {
                ExecuteFftKernelPreparation(cmd);
                kernelSpectralIndex = ExecuteFftTransform(
                    cmd,
                    m_FftKernelRealTH,
                    m_FftKernelImagTH,
                    0,
                    inverse: false);
                CommitFftKernelCache();
            }

            ExecuteFftSourcePreparation(cmd);
            int sourceSpectralIndex = ExecuteFftTransform(
                cmd,
                m_FftRealTH,
                m_FftImagTH,
                0,
                inverse: false);

            int inverseInputIndex = 1 - sourceSpectralIndex;
            BindFftInput(cmd, m_FftMultiplyKernel, m_FftRealTH, m_FftImagTH, sourceSpectralIndex);
            cmd.SetComputeTextureParam(
                m_FftCS,
                m_FftMultiplyKernel,
                FftKernelRealId,
                m_FftKernelRealTH[kernelSpectralIndex]);
            cmd.SetComputeTextureParam(
                m_FftCS,
                m_FftMultiplyKernel,
                FftKernelImagId,
                m_FftKernelImagTH[kernelSpectralIndex]);
            BindFftOutput(cmd, m_FftMultiplyKernel, m_FftRealTH, m_FftImagTH, inverseInputIndex);
            DispatchFftDomain(cmd, m_FftMultiplyKernel);

            int resolvedIndex = ExecuteFftTransform(
                cmd,
                m_FftRealTH,
                m_FftImagTH,
                inverseInputIndex,
                inverse: true);

            float unitaryConvolutionScale = Mathf.Sqrt(
                (float)m_FftDomain.FrequencyWidth * m_FftDomain.FrequencyHeight);
            cmd.SetComputeFloatParam(m_FftCS, FftResolveScaleId, unitaryConvolutionScale);
            cmd.SetComputeTextureParam(
                m_FftCS,
                m_FftResolveKernel,
                FftRealInputId,
                m_FftRealTH[resolvedIndex]);
            cmd.SetComputeTextureParam(
                m_FftCS,
                m_FftResolveKernel,
                FftKernelEnergyId,
                m_FftEnergyTH[m_FftEnergyLevelCount - 1]);
            cmd.SetComputeTextureParam(
                m_FftCS,
                m_FftResolveKernel,
                FftOutputTextureId,
                bloomTexture.innerHandle);
            cmd.DispatchCompute(
                m_FftCS,
                m_FftResolveKernel,
                DivUp(m_FftDomain.ImageWidth, 8),
                DivUp(m_FftDomain.ImageHeight, 8),
                1);

            var bloomOutput = (RTHandle)bloomTexture.innerHandle;
            var lensFlareOutput = (RTHandle)screenSpaceLensFlareBloomMipTexture.innerHandle;
            if (lensFlareOutput != null)
            {
                if (m_ShouldOutputScreenSpaceLensFlareMip && bloomOutput != null)
                    Blitter.BlitCameraTexture(cmd, bloomOutput, lensFlareOutput, 0f, true);
                else
                    ClearTexture(cmd, lensFlareOutput);
            }

            BindFftBloomGlobals(cmd, bloomOutput);
        }

        private void ExecuteFftKernelPreparation(CommandBuffer cmd)
        {
            var kernelParams = new Vector4(
                m_FftDomain.KernelSize,
                m_Settings.convolutionKernelClamp,
                Mathf.Clamp01(m_Settings.convolutionCenter.x),
                Mathf.Clamp01(m_Settings.convolutionCenter.y));
            cmd.SetComputeVectorParam(m_FftCS, FftKernelParamsId, kernelParams);
            cmd.SetComputeTextureParam(
                m_FftCS,
                m_FftPrepareKernelKernel,
                FftKernelTextureId,
                m_Settings.convolutionKernel);
            BindFftOutput(
                cmd,
                m_FftPrepareKernelKernel,
                m_FftKernelRealTH,
                m_FftKernelImagTH,
                0);
            DispatchFftDomain(cmd, m_FftPrepareKernelKernel);

            TextureHandle energyInput = m_FftKernelRealTH[0];
            int inputWidth = m_FftDomain.FrequencyWidth;
            int inputHeight = m_FftDomain.FrequencyHeight;
            for (int level = 0; level < m_FftEnergyLevelCount; level++)
            {
                int outputWidth = m_FftEnergyHandles[level].rt.width;
                int outputHeight = m_FftEnergyHandles[level].rt.height;
                cmd.SetComputeVectorParam(
                    m_FftCS,
                    FftInputSizeId,
                    new Vector4(inputWidth, inputHeight, 1f / inputWidth, 1f / inputHeight));
                cmd.SetComputeVectorParam(
                    m_FftCS,
                    FftOutputSizeId,
                    new Vector4(outputWidth, outputHeight, 1f / outputWidth, 1f / outputHeight));
                cmd.SetComputeTextureParam(
                    m_FftCS,
                    m_FftReduceEnergyKernel,
                    FftEnergyInputId,
                    energyInput);
                cmd.SetComputeTextureParam(
                    m_FftCS,
                    m_FftReduceEnergyKernel,
                    FftEnergyOutputId,
                    m_FftEnergyTH[level]);
                cmd.DispatchCompute(
                    m_FftCS,
                    m_FftReduceEnergyKernel,
                    DivUp(outputWidth, 8),
                    DivUp(outputHeight, 8),
                    1);

                energyInput = m_FftEnergyTH[level];
                inputWidth = outputWidth;
                inputHeight = outputHeight;
            }
        }

        private void ExecuteFftSourcePreparation(CommandBuffer cmd)
        {
            float linearThreshold = Mathf.GammaToLinearSpace(m_Settings.threshold);
            float knee = linearThreshold * 0.5f + 1e-5f;
            cmd.SetComputeVectorParam(
                m_FftCS,
                BloomThresholdId,
                new Vector4(linearThreshold, linearThreshold - knee, knee * 2f, 0.25f / knee));
            cmd.SetComputeTextureParam(
                m_FftCS,
                m_FftPrepareSourceKernel,
                FftSourceTextureId,
                source.innerHandle);
            BindFftOutput(cmd, m_FftPrepareSourceKernel, m_FftRealTH, m_FftImagTH, 0);
            DispatchFftDomain(cmd, m_FftPrepareSourceKernel);
        }

        private int ExecuteFftTransform(
            CommandBuffer cmd,
            TextureHandle[] realTextures,
            TextureHandle[] imaginaryTextures,
            int initialIndex,
            bool inverse)
        {
            if (m_FftExecutionPath != BloomFftExecutionPath.MultiDispatch)
            {
                return ExecuteWaveFftTransform(
                    cmd,
                    realTextures,
                    imaginaryTextures,
                    initialIndex,
                    inverse);
            }

            return ExecuteMultiDispatchFftTransform(
                cmd,
                realTextures,
                imaginaryTextures,
                initialIndex,
                inverse);
        }

        private int ExecuteWaveFftTransform(
            CommandBuffer cmd,
            TextureHandle[] realTextures,
            TextureHandle[] imaginaryTextures,
            int initialIndex,
            bool inverse)
        {
            cmd.SetComputeIntParam(m_FftCS, FftInverseId, inverse ? 1 : 0);

            int horizontalOutputIndex = 1 - initialIndex;
            BindFftInput(
                cmd,
                m_FftSelectedWaveHorizontalKernel,
                realTextures,
                imaginaryTextures,
                initialIndex);
            BindFftOutput(
                cmd,
                m_FftSelectedWaveHorizontalKernel,
                realTextures,
                imaginaryTextures,
                horizontalOutputIndex);
            cmd.DispatchCompute(
                m_FftCS,
                m_FftSelectedWaveHorizontalKernel,
                m_FftDomain.FrequencyHeight,
                1,
                1);

            int verticalOutputIndex = 1 - horizontalOutputIndex;
            BindFftInput(
                cmd,
                m_FftSelectedWaveVerticalKernel,
                realTextures,
                imaginaryTextures,
                horizontalOutputIndex);
            BindFftOutput(
                cmd,
                m_FftSelectedWaveVerticalKernel,
                realTextures,
                imaginaryTextures,
                verticalOutputIndex);
            cmd.DispatchCompute(
                m_FftCS,
                m_FftSelectedWaveVerticalKernel,
                m_FftDomain.FrequencyWidth,
                1,
                1);

            return verticalOutputIndex;
        }

        private int ExecuteMultiDispatchFftTransform(
            CommandBuffer cmd,
            TextureHandle[] realTextures,
            TextureHandle[] imaginaryTextures,
            int initialIndex,
            bool inverse)
        {
            cmd.SetComputeIntParam(m_FftCS, FftInverseId, inverse ? 1 : 0);
            int currentIndex = initialIndex;

            for (int stage = 0; stage < m_FftDomain.Log2Width; stage++)
            {
                int outputIndex = 1 - currentIndex;
                cmd.SetComputeIntParam(m_FftCS, FftStageId, stage);
                BindFftInput(cmd, m_FftHorizontalKernel, realTextures, imaginaryTextures, currentIndex);
                BindFftOutput(cmd, m_FftHorizontalKernel, realTextures, imaginaryTextures, outputIndex);
                DispatchFftDomain(cmd, m_FftHorizontalKernel);
                currentIndex = outputIndex;
            }

            for (int stage = 0; stage < m_FftDomain.Log2Height; stage++)
            {
                int outputIndex = 1 - currentIndex;
                cmd.SetComputeIntParam(m_FftCS, FftStageId, stage);
                BindFftInput(cmd, m_FftVerticalKernel, realTextures, imaginaryTextures, currentIndex);
                BindFftOutput(cmd, m_FftVerticalKernel, realTextures, imaginaryTextures, outputIndex);
                DispatchFftDomain(cmd, m_FftVerticalKernel);
                currentIndex = outputIndex;
            }

            return currentIndex;
        }

        private void SelectWaveFftKernels()
        {
            switch (m_FftExecutionPath)
            {
                case BloomFftExecutionPath.Wave32:
                    m_FftSelectedWaveHorizontalKernel = m_FftWaveHorizontal32Kernel;
                    m_FftSelectedWaveVerticalKernel = m_FftWaveVertical32Kernel;
                    break;
                case BloomFftExecutionPath.Wave64:
                    m_FftSelectedWaveHorizontalKernel = m_FftWaveHorizontal64Kernel;
                    m_FftSelectedWaveVerticalKernel = m_FftWaveVertical64Kernel;
                    break;
                default:
                    m_FftSelectedWaveHorizontalKernel = -1;
                    m_FftSelectedWaveVerticalKernel = -1;
                    break;
            }
        }

        private void SetFftDomainParameters(CommandBuffer cmd)
        {
            cmd.SetComputeVectorParam(
                m_FftCS,
                FftSizeId,
                new Vector4(
                    m_FftDomain.FrequencyWidth,
                    m_FftDomain.FrequencyHeight,
                    m_FftDomain.Log2Width,
                    m_FftDomain.Log2Height));
            cmd.SetComputeVectorParam(
                m_FftCS,
                FftImageSizeId,
                new Vector4(
                    m_FftDomain.ImageWidth,
                    m_FftDomain.ImageHeight,
                    1f / m_FftDomain.ImageWidth,
                    1f / m_FftDomain.ImageHeight));
        }

        private void BindFftInput(
            CommandBuffer cmd,
            int kernel,
            TextureHandle[] realTextures,
            TextureHandle[] imaginaryTextures,
            int index)
        {
            cmd.SetComputeTextureParam(m_FftCS, kernel, FftRealInputId, realTextures[index]);
            cmd.SetComputeTextureParam(m_FftCS, kernel, FftImagInputId, imaginaryTextures[index]);
        }

        private void BindFftOutput(
            CommandBuffer cmd,
            int kernel,
            TextureHandle[] realTextures,
            TextureHandle[] imaginaryTextures,
            int index)
        {
            cmd.SetComputeTextureParam(m_FftCS, kernel, FftRealOutputId, realTextures[index]);
            cmd.SetComputeTextureParam(m_FftCS, kernel, FftImagOutputId, imaginaryTextures[index]);
        }

        private void DispatchFftDomain(CommandBuffer cmd, int kernel)
        {
            cmd.DispatchCompute(
                m_FftCS,
                kernel,
                DivUp(m_FftDomain.FrequencyWidth, 8),
                DivUp(m_FftDomain.FrequencyHeight, 8),
                1);
        }

        private void BindFftBloomGlobals(CommandBuffer cmd, RTHandle bloomOutput)
        {
            float bloomIntensity = Mathf.Pow(2f, m_Settings.intensity) - 1f;
            bool hasDirt = m_Settings.dirtTexture != null && m_Settings.dirtIntensity > 0f;
            Vector4 tint = m_Settings.tint.linear;

            cmd.SetGlobalTexture(
                VividBloomTextureId,
                bloomOutput != null ? bloomOutput : Texture2D.blackTexture);
            cmd.SetGlobalVector(
                VividBloomParamsId,
                new Vector4(bloomIntensity, m_Settings.dirtIntensity, 1f, hasDirt ? 1f : 0f));
            cmd.SetGlobalVector(VividBloomTintId, new Vector4(tint.x, tint.y, tint.z, 1f));

            if (!hasDirt)
                return;

            cmd.SetGlobalTexture(VividBloomDirtTextureId, m_Settings.dirtTexture);
            float dirtRatio = (float)m_Settings.dirtTexture.width / m_Settings.dirtTexture.height;
            float screenRatio = (float)m_ScreenWidth / m_ScreenHeight;
            Vector4 dirtScale;
            if (dirtRatio > screenRatio)
            {
                float scale = screenRatio / dirtRatio;
                dirtScale = new Vector4(scale, 1f, (1f - scale) * 0.5f, 0f);
            }
            else
            {
                float scale = dirtRatio / screenRatio;
                dirtScale = new Vector4(1f, scale, 0f, (1f - scale) * 0.5f);
            }

            cmd.SetGlobalVector(VividBloomDirtScaleId, dirtScale);
        }

        private void CommitFftKernelCache()
        {
            m_CachedFftKernel = m_Settings.convolutionKernel;
            m_CachedFftKernelHash = m_Settings.convolutionKernel.imageContentsHash;
            m_CachedFftFrequencyWidth = m_FftDomain.FrequencyWidth;
            m_CachedFftFrequencyHeight = m_FftDomain.FrequencyHeight;
            m_CachedFftImageWidth = m_FftDomain.ImageWidth;
            m_CachedFftImageHeight = m_FftDomain.ImageHeight;
            m_CachedFftKernelSize = m_FftDomain.KernelSize;
            m_CachedFftKernelClamp = m_Settings.convolutionKernelClamp;
            m_CachedFftKernelCenter = m_Settings.convolutionCenter;
            m_CachedFftExecutionPath = m_FftExecutionPath;
            m_FftKernelCacheValid = true;
            m_FftKernelNeedsUpdate = false;
        }

        private void DisposeFftResources()
        {
            for (int i = 0; i < 2; i++)
            {
                ReleaseFftHandle(ref m_FftRealHandles[i]);
                ReleaseFftHandle(ref m_FftImagHandles[i]);
                ReleaseFftHandle(ref m_FftKernelRealHandles[i]);
                ReleaseFftHandle(ref m_FftKernelImagHandles[i]);
            }

            for (int i = 0; i < k_MaxFftReductionLevels; i++)
                ReleaseFftHandle(ref m_FftEnergyHandles[i]);

            m_FftKernelCacheValid = false;
            m_FftKernelNeedsUpdate = false;
        }

        private static bool EnsureFftHandle(
            ref RTHandle handle,
            int width,
            int height,
            string name,
            GraphicsFormat format = GraphicsFormat.R16G16B16A16_SFloat)
        {
            if (handle != null
                && handle.rt != null
                && handle.rt.width == width
                && handle.rt.height == height
                && handle.rt.graphicsFormat == format)
                return false;

            ReleaseFftHandle(ref handle);
            handle = RTHandles.Alloc(
                width,
                height,
                colorFormat: format,
                enableRandomWrite: true,
                filterMode: FilterMode.Point,
                wrapMode: TextureWrapMode.Clamp,
                name: name);
            return true;
        }

        private static void ReleaseFftHandle(ref RTHandle handle)
        {
            handle?.Release();
            handle = null;
        }
    }
}

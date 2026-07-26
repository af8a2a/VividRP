using System.Reflection;
using NUnit.Framework;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class BloomPassTests
    {
        [Test]
        public void BloomPass_UsesStableResourceLayout_ForSourceOverrides()
        {
            Assert.That(typeof(IStablePassResourceLayout).IsAssignableFrom(typeof(BloomPass)), Is.True);
        }

        [Test]
        public void BloomPass_CachesMipHandleNames_ForPrepareReuse()
        {
            var downNames = GetPrivateStaticStringArray("s_MipDownNames");
            var upNames = GetPrivateStaticStringArray("s_MipUpNames");

            Assert.That(downNames, Has.Length.EqualTo(16));
            Assert.That(upNames, Has.Length.EqualTo(16));
            Assert.That(downNames[0], Is.EqualTo("BloomMipDown0"));
            Assert.That(downNames[15], Is.EqualTo("BloomMipDown15"));
            Assert.That(upNames[0], Is.EqualTo("BloomMipUp0"));
            Assert.That(upNames[15], Is.EqualTo("BloomMipUp15"));
        }

        [Test]
        public void BloomSettingsData_DefaultsExperimentalSpdDownsampleOff()
        {
            var settings = BloomSettingsData.CreateDefault();

            Assert.That(settings.experimentalSpdDownsample, Is.False);
        }

        [Test]
        public void BloomSettingsData_DefaultsToScatteringWithQuarterResolutionFft()
        {
            var settings = BloomSettingsData.CreateDefault();

            Assert.That(settings.mode, Is.EqualTo(BloomMode.Scattering));
            Assert.That(settings.convolutionResolutionScale, Is.EqualTo(0.25f));
            Assert.That(settings.convolutionKernel, Is.Null);
        }

        [Test]
        public void ShouldUseFftConvolution_RequiresModeKernelAndKernels()
        {
            Assert.That(BloomPass.ShouldUseFftConvolution(false, true, true), Is.False);
            Assert.That(BloomPass.ShouldUseFftConvolution(true, false, true), Is.False);
            Assert.That(BloomPass.ShouldUseFftConvolution(true, true, false), Is.False);
            Assert.That(BloomPass.ShouldUseFftConvolution(true, true, true), Is.True);
        }

        [Test]
        public void CalculateFftDomain_UsesKernelPaddingAndPowerOfTwoExtent()
        {
            var domain = BloomPass.CalculateFftDomain(
                1920,
                1080,
                0.25f,
                0.15f,
                0.25f);

            Assert.That(domain.ImageWidth, Is.EqualTo(480));
            Assert.That(domain.ImageHeight, Is.EqualTo(270));
            Assert.That(domain.KernelSize, Is.EqualTo(72));
            Assert.That(domain.Padding, Is.EqualTo(36));
            Assert.That(domain.FrequencyWidth, Is.EqualTo(1024));
            Assert.That(domain.FrequencyHeight, Is.EqualTo(512));
        }

        [TestCase(32, true, true, (int)BloomFftExecutionPath.Wave32)]
        [TestCase(64, true, true, (int)BloomFftExecutionPath.Wave64)]
        [TestCase(16, true, true, (int)BloomFftExecutionPath.Lds)]
        [TestCase(32, false, true, (int)BloomFftExecutionPath.Lds)]
        [TestCase(64, true, false, (int)BloomFftExecutionPath.Lds)]
        public void ResolveFftExecutionPath_SelectsMatchingWaveKernels(
            int computeSubGroupSize,
            bool hasWave32Kernels,
            bool hasWave64Kernels,
            int expected)
        {
            var path = BloomPass.ResolveFftExecutionPath(
                computeSubGroupSize,
                1024,
                512,
                hasWave32Kernels,
                hasWave64Kernels);

            Assert.That((int)path, Is.EqualTo(expected));
        }

        [Test]
        public void ResolveFftExecutionPath_UsesLdsOutsideWaveLimits()
        {
            Assert.That(
                BloomPass.ResolveFftExecutionPath(32, 4096, 2048, true, true),
                Is.EqualTo(BloomFftExecutionPath.Lds));
            Assert.That(
                BloomPass.ResolveFftExecutionPath(64, 2048, 32, true, true),
                Is.EqualTo(BloomFftExecutionPath.Lds));
        }

        [Test]
        public void BloomBlurCompute_ContainsFftConvolutionKernels()
        {
            var shader = PipelineResourceManager.Get<VividRPCoreResources>().BloomBlurCompute;
            Assert.That(shader, Is.Not.Null);

            Assert.That(shader.HasKernel("KFFTPrepareSource"), Is.True);
            Assert.That(shader.HasKernel("KFFTPrepareKernel"), Is.True);
            Assert.That(shader.HasKernel("KFFTLdsHorizontal"), Is.True);
            Assert.That(shader.HasKernel("KFFTLdsVertical"), Is.True);
            Assert.That(shader.HasKernel("KFFTWaveHorizontal32"), Is.True);
            Assert.That(shader.HasKernel("KFFTWaveVertical32"), Is.True);
            Assert.That(shader.HasKernel("KFFTWaveHorizontal64"), Is.True);
            Assert.That(shader.HasKernel("KFFTWaveVertical64"), Is.True);
            Assert.That(shader.HasKernel("KFFTMultiplyAndBitReverse"), Is.True);
            Assert.That(shader.HasKernel("KFFTResolve"), Is.True);
            Assert.That(shader.HasKernel("KFFTReduceEnergy"), Is.True);
        }

        [Test]
        public void ShouldUseSpdDownsample_RequiresRequestAndEligibleResources()
        {
            Assert.That(BloomPass.ShouldUseSpdDownsample(false, 8, true, true), Is.False);
            Assert.That(BloomPass.ShouldUseSpdDownsample(true, 1, true, true), Is.False);
            Assert.That(BloomPass.ShouldUseSpdDownsample(true, 14, true, true), Is.False);
            Assert.That(BloomPass.ShouldUseSpdDownsample(true, 8, false, true), Is.False);
            Assert.That(BloomPass.ShouldUseSpdDownsample(true, 8, true, false), Is.False);
            Assert.That(BloomPass.ShouldUseSpdDownsample(true, 8, true, true), Is.True);
            Assert.That(BloomPass.ShouldUseSpdDownsample(true, 13, true, true), Is.True);
        }

        [Test]
        public void GetBoundSpdMipIndex_ClampsToLastAvailableMip()
        {
            Assert.That(BloomPass.GetBoundSpdMipIndex(0, 8), Is.EqualTo(0));
            Assert.That(BloomPass.GetBoundSpdMipIndex(7, 8), Is.EqualTo(7));
            Assert.That(BloomPass.GetBoundSpdMipIndex(12, 8), Is.EqualTo(7));
            Assert.That(BloomPass.GetBoundSpdMipIndex(12, 13), Is.EqualTo(12));
            Assert.That(BloomPass.GetBoundSpdMipIndex(12, 0), Is.EqualTo(0));
        }

        private static string[] GetPrivateStaticStringArray(string fieldName)
        {
            var field = typeof(BloomPass).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (string[])field.GetValue(null);
        }
    }
}

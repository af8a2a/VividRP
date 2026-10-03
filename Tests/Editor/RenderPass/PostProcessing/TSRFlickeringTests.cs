using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    // Run with the Editor closed, in the project's Unity Test Framework batch.
    // Non-multiple-of-eight extents exercise all tile edges and halo padding.
    public class TSRFlickeringTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void PeriodicGradientReversals_AccumulateError(bool waveOps)
        {
            using var f = new Fixture(waveOps);
            for (int i = 0; i < 120; i++) f.Step(i % 2 == 0 ? 0.2f : 0.8f, i > 0);
            Assert.That(f.MaxError(), Is.GreaterThan(0.1f));
        }

        [TestCase(0)] // Constant input.
        [TestCase(1)] // One actual lighting transition.
        [TestCase(2)] // Monotonic lighting ramp.
        [TestCase(3)] // Eight-frame oscillation exceeds the default period.
        public void NonFlickeringSequences_DoNotAccumulateError(int sequence)
        {
            using var f = new Fixture(false);
            for (int i = 0; i < 120; i++)
            {
                float value = sequence == 0 ? 0.5f : sequence == 1 ? (i < 30 ? 0.2f : 0.8f)
                    : sequence == 2 ? 0.2f + 0.6f * i / 119 : ((i / 4) % 2 == 0 ? 0.2f : 0.8f);
                f.Step(value, i > 0);
            }
            Assert.That(f.MaxError(), Is.LessThan(0.001f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MovingOrDisoccludedPixels_ClearProtection(bool disoccluded)
        {
            using var f = new Fixture(false);
            for (int i = 0; i < 100; i++) f.Step(i % 2 == 0 ? 0.2f : 0.8f, i > 0);
            Assert.That(f.MaxError(), Is.GreaterThan(0.1f));
            f.Step(0.2f, !disoccluded, disoccluded ? 1 : 0);
            Assert.That(f.MaxError(), Is.LessThan(0.001f));
        }

        [Test]
        public void FrameRateAdjustment_DoesNotProtectSlowVisibleFlashing()
        {
            using var f = new Fixture(false, 0.5f); // 15 Hz at default 60 Hz cap.
            for (int i = 0; i < 120; i++) f.Step(i % 2 == 0 ? 0.2f : 0.8f, i > 0);
            Assert.That(f.MaxError(), Is.LessThan(0.001f));
        }

        private sealed class Fixture : IDisposable
        {
            private const int Width = 13, Height = 11;
            private readonly ComputeShader shader;
            private readonly int analyze, update;
            private readonly Texture2D input;
            private readonly Color[] pixels = new Color[Width * Height];
            private readonly RenderTexture gradient, error;
            private RenderTexture previous, current;

            internal Fixture(bool waveOps, float period = 2)
            {
                Assume.That(SystemInfo.supportsComputeShaders && SystemInfo.supportsAsyncGPUReadback, Is.True);
                if (waveOps) Assume.That(SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12
                    || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan, Is.True);
                var asset = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.vivid.render-pipelines/Shaders/Core/Private/TSR/TSRRejectShading.compute");
                Assert.That(asset, Is.Not.Null);
                shader = Object.Instantiate(asset);
                if (waveOps) shader.EnableKeyword("VIVID_TSR_WAVE_OPS");
                else shader.DisableKeyword("VIVID_TSR_WAVE_OPS");
                analyze = shader.FindKernel("CSAnalyzeFlicker"); update = shader.FindKernel("CSUpdateFlicker");
                shader.SetVector("_RenderSize", new Vector4(Width, Height, 1f / Width, 1f / Height));
                shader.SetVector("_FlickerParams", new Vector4(period, 0, 0, 0));
                input = new Texture2D(Width, Height, GraphicsFormat.R32G32B32A32_SFloat, TextureCreationFlags.None);
                gradient = Create(GraphicsFormat.R32G32B32A32_SFloat);
                error = Create(GraphicsFormat.R32_SFloat);
                previous = Create(GraphicsFormat.R8G8B8A8_UNorm); current = Create(GraphicsFormat.R8G8B8A8_UNorm);
                var original = RenderTexture.active;
                RenderTexture.active = previous; GL.Clear(false, true, new Color(0, 127f / 255f, 0, 0));
                RenderTexture.active = original;
            }
            private static RenderTexture Create(GraphicsFormat format)
            {
                var t = new RenderTexture(Width, Height, 0) { graphicsFormat = format, enableRandomWrite = true };
                t.Create(); return t;
            }
            internal void Step(float smcsLuma, bool valid, float stationary = 1)
            {
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(smcsLuma, stationary, valid ? 1 : 0, 0);
                input.SetPixels(pixels); input.Apply(false, false);
                shader.SetVector("_TSRParams", new Vector4(valid ? 1 : 0, 16, 0, 0));
                shader.SetTexture(analyze, "_FlickerInput", input);
                shader.SetTexture(analyze, "_ReprojectedFlickerHistory", previous);
                shader.SetTexture(analyze, "_OutputFlickerGradient", gradient);
                shader.Dispatch(analyze, 2, 2, 1);
                shader.SetTexture(update, "_FlickerInput", input);
                shader.SetTexture(update, "_ReprojectedFlickerHistory", previous);
                shader.SetTexture(update, "_FlickerGradient", gradient);
                shader.SetTexture(update, "_CurrentFlickerHistory", current);
                shader.SetTexture(update, "_OutputFlickerError", error);
                shader.Dispatch(update, 2, 2, 1);
                (previous, current) = (current, previous);
            }
            internal float MaxError()
            {
                var request = AsyncGPUReadback.Request(error); request.WaitForCompletion();
                Assert.That(request.hasError, Is.False);
                var values = request.GetData<float>(); float maximum = 0;
                for (int i = 0; i < values.Length; i++)
                {
                    Assert.That(float.IsNaN(values[i]) || float.IsInfinity(values[i]), Is.False);
                    maximum = Mathf.Max(maximum, values[i]);
                }
                return maximum;
            }
            public void Dispose()
            {
                foreach (var t in new[] { previous, current, gradient, error }) { t.Release(); Object.DestroyImmediate(t); }
                Object.DestroyImmediate(input); Object.DestroyImmediate(shader);
            }
        }
    }
}

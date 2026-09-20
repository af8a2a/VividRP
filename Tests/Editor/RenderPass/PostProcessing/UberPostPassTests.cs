using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class UberPostPassTests
    {
        [Test]
        public void Initialize_SeparatesEffectsNeuralRenderingAndPresentationResources()
        {
            var uber = ((IRenderPass)new UberPostPass()).Initialize();
            var nr = ((IRenderPass)new DLSSNeuralRenderingPass()).Initialize();
            var final = ((IRenderPass)new FinalBlitPass()).Initialize();
            Assert.That(uber.Textures, Has.Length.EqualTo(4));
            Assert.That(uber.Textures[3].Name, Is.EqualTo("UberPostOutput"));
            Assert.That(uber.Textures[3].Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(uber.Textures[3].IsTransient, Is.False);
            Assert.That(nr.Textures, Has.Length.EqualTo(4));
            Assert.That(nr.Textures[1].Name, Is.EqualTo("CameraDepth"));
            Assert.That(nr.Textures[2].Name, Is.EqualTo("MotionVectors"));
            Assert.That(nr.BypassRules, Has.Length.EqualTo(1));
            Assert.That(nr.BypassRules[0].SourceFieldName, Is.EqualTo("m_Source"));
            Assert.That(nr.BypassRules[0].OutputFieldName, Is.EqualTo("m_Output"));
            Assert.That(final.Textures, Has.Length.EqualTo(1));
            Assert.That(typeof(IRenderGizmoPrePostProcessBoundaryPass).IsAssignableFrom(typeof(UberPostPass)), Is.True);
            Assert.That(typeof(IRenderGizmoPrePostProcessBoundaryPass).IsAssignableFrom(typeof(FinalBlitPass)), Is.False);
        }

        [Test]
        public void PrepareOutput_PreservesSourceOwnershipAndUsesLinearFloatingPoint()
        {
            var pass = new UberPostPass();
            var source = RenderGraphTexture.CreateColorTarget("Source", GraphicsFormat.R8G8B8A8_SRGB);
            source.desc.Width = 640;
            source.desc.Height = 360;
            pass.SetSourceTexture(source);
            pass.PrepareOutput(null);
            var output = GetTexture(pass, "m_OutputTexture");
            Assert.That(output.desc, Is.Not.SameAs(source.desc));
            Assert.That(output.desc.Width, Is.EqualTo(640));
            Assert.That(output.desc.Height, Is.EqualTo(360));
            Assert.That(output.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(source.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_SRGB));
            Assert.That(output.desc.EnableRandomWrite, Is.False);
            var descriptor = output.desc;
            pass.PrepareOutput(null);
            Assert.That(output.desc, Is.SameAs(descriptor));
        }

        [Test]
        public void Prepare_StableOutputDescriptorsDoNotAllocate()
        {
            var uber = new UberPostPass();
            var nr = new DLSSNeuralRenderingPass();
            using var frameData = new ContextContainer();
            frameData.GetOrCreate<VividCameraData>();
            var aa = frameData.GetOrCreate<VividAntialiasingData>();
            aa.renderSize = new Vector2Int(960, 540);
            aa.outputSize = new Vector2Int(1920, 1080);
#if DLSS_PLUGIN_INTEGRATE
            aa.effectiveMode = VividAntialiasingMode.DLSSNeuralRendering;
#endif
            for (var i = 0; i < 32; i++)
            {
                uber.PrepareOutput(aa);
                nr.Prepare(frameData);
            }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 32; i++)
            {
                uber.PrepareOutput(aa);
                nr.Prepare(frameData);
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
            var output = GetTexture(nr, "m_Output");
            Assert.That(output.desc.Width, Is.EqualTo(1920));
            Assert.That(output.desc.Height, Is.EqualTo(1080));
            Assert.That(output.desc.EnableRandomWrite, Is.True);
#if DLSS_PLUGIN_INTEGRATE
            Assert.That(GetTexture(uber, "m_OutputTexture").desc.Width, Is.EqualTo(960));
            Assert.That(GetTexture(uber, "m_OutputTexture").desc.Height, Is.EqualTo(540));
#endif
        }

        [Test]
        public void NeuralRendering_IsInactiveWithoutRequestedModeOrGuides()
        {
            var pass = new DLSSNeuralRenderingPass();
            using var frameData = new ContextContainer();
            var aa = frameData.GetOrCreate<VividAntialiasingData>();
            Assert.That(pass.IsActive(frameData), Is.False);
#if DLSS_PLUGIN_INTEGRATE
            aa.effectiveMode = VividAntialiasingMode.DLSSNeuralRendering;
            Assert.That(pass.IsActive(frameData), Is.False);
            foreach (var name in new[] { "m_Source", "m_Depth", "m_MotionVectors" })
                typeof(DLSSNeuralRenderingPass).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(pass, new RenderGraphTexture());
            Assert.That(pass.IsActive(frameData), Is.True);
            aa.effectiveMode = VividAntialiasingMode.None;
            Assert.That(pass.IsActive(frameData), Is.False);
#endif
        }

#if DLSS_PLUGIN_INTEGRATE
        [Test]
        public void Resolve_NeuralRenderingRequiresItsOwnPass_AndDoesNotRequireAntialiasingPass()
        {
            var resolved = typeof(VividAntialiasingRuntimeUtility).GetField(
                "s_HasResolvedDlssNeuralRenderingSupport", BindingFlags.Static | BindingFlags.NonPublic);
            var supported = typeof(VividAntialiasingRuntimeUtility).GetField(
                "s_CachedDlssNeuralRenderingSupport", BindingFlags.Static | BindingFlags.NonPublic);
            var originalResolved = resolved.GetValue(null);
            var originalSupported = supported.GetValue(null);
            var gameObject = new GameObject("NR pass availability");
            try
            {
                var camera = gameObject.AddComponent<Camera>();
                var additionalData = gameObject.AddComponent<VividAdditionalCameraData>();
                additionalData.antialiasing = VividAntialiasingMode.DLSSNeuralRendering;
                resolved.SetValue(null, true);
                supported.SetValue(null, true);
                var data = new VividAntialiasingData();
                VividAntialiasingRuntimeUtility.Resolve(camera, additionalData, true, data, false);
                Assert.That(data.effectiveMode, Is.EqualTo(VividAntialiasingMode.None));
                VividAntialiasingRuntimeUtility.Resolve(camera, additionalData, false, data, true);
                Assert.That(data.effectiveMode, Is.EqualTo(VividAntialiasingMode.DLSSNeuralRendering));
                Assert.That(data.hasNeuralRenderingPass, Is.True);
                Assert.That(data.hasAntialiasingPass, Is.False);
                Assert.That(data.usesTemporalJitter, Is.False);
            }
            finally
            {
                VividAntialiasingRuntimeUtility.Clear();
                resolved.SetValue(null, originalResolved);
                supported.SetValue(null, originalSupported);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }
#endif

        [Test]
        public void HdrKeywords_ConvertInUberAndEncodeOnlyInFinalBlit()
        {
            var uber = new Material(Shader.Find("Hidden/VividRP/UberPost"));
            var final = new Material(Shader.Find("Hidden/VividRP/FinalBlit"));
            try
            {
                ConfigureHdr(typeof(UberPostPass), uber, true);
                ConfigureHdr(typeof(FinalBlitPass), final, true);
                Assert.That(uber.IsKeywordEnabled(HDROutputUtils.ShaderKeywords.HDR_COLORSPACE_CONVERSION), Is.True);
                Assert.That(uber.IsKeywordEnabled(HDROutputUtils.ShaderKeywords.HDR_ENCODING), Is.False);
                Assert.That(final.IsKeywordEnabled(HDROutputUtils.ShaderKeywords.HDR_ENCODING), Is.True);
                Assert.That(final.IsKeywordEnabled(HDROutputUtils.ShaderKeywords.HDR_COLORSPACE_CONVERSION), Is.False);
                ConfigureHdr(typeof(UberPostPass), uber, false);
                ConfigureHdr(typeof(FinalBlitPass), final, false);
                Assert.That(uber.IsKeywordEnabled(HDROutputUtils.ShaderKeywords.HDR_COLORSPACE_CONVERSION), Is.False);
                Assert.That(final.IsKeywordEnabled(HDROutputUtils.ShaderKeywords.HDR_ENCODING), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(uber);
                UnityEngine.Object.DestroyImmediate(final);
            }
        }

        private static void ConfigureHdr(Type type, Material material, bool enabled)
        {
            type.GetMethod("ConfigureMaterialHDROutput", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { material, enabled, ColorGamut.sRGB });
        }

        private static RenderGraphTexture GetTexture(object pass, string name)
        {
            return (RenderGraphTexture)pass.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pass);
        }
    }
}

using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class RefractionPassesTests
    {
        [Test]
        public void RefractionPasses_IsActive_RespectsStaticControlsAndDisposesCleanly()
        {
            bool prevRefraction = ForwardRefractionPass.EnableRefraction;
            bool prevBackface = ForwardRefractionPass.EnableBackfaceRefraction;
            bool prevCaustics = TransmissiveCausticsPass.EnableCaustics;
            var frameData = new ContextContainer();
            var backfacePass = new TransparentBackfacePass();
            var forwardPass = new ForwardRefractionPass();
            var pyramidPass = new OpaqueColorPyramidPass();
            var causticsPass = new TransmissiveCausticsPass();

            try
            {
                ForwardRefractionPass.EnableRefraction = true;
                ForwardRefractionPass.EnableBackfaceRefraction = true;
                TransmissiveCausticsPass.EnableCaustics = true;
                Assert.That(forwardPass.IsActive(frameData), Is.True);
                Assert.That(backfacePass.IsActive(frameData), Is.True);

                TransmissiveCausticsPass.EnableCaustics = false;
                Assert.That(causticsPass.IsActive(frameData), Is.False);

                ForwardRefractionPass.EnableBackfaceRefraction = false;
                Assert.That(backfacePass.IsActive(frameData), Is.False);
                Assert.That(forwardPass.IsActive(frameData), Is.True);

                ForwardRefractionPass.EnableRefraction = false;
                TransmissiveCausticsPass.EnableCaustics = true;
                Assert.That(forwardPass.IsActive(frameData), Is.False);
                Assert.That(backfacePass.IsActive(frameData), Is.False);
                Assert.That(pyramidPass.IsActive(frameData), Is.False);
                Assert.That(causticsPass.IsActive(frameData), Is.False);
            }
            finally
            {
                backfacePass.Dispose();
                forwardPass.Dispose();
                pyramidPass.Dispose();
                causticsPass.Dispose();
                ForwardRefractionPass.EnableRefraction = prevRefraction;
                ForwardRefractionPass.EnableBackfaceRefraction = prevBackface;
                TransmissiveCausticsPass.EnableCaustics = prevCaustics;
            }
        }

        [Test]
        public void GlassShader_ContainsAllPhase2PassesAndOpenPbrFormulas()
        {
            string packageRoot = Path.GetFullPath("Packages/com.vivid.render-pipelines");
            string glassPath = Path.Combine(packageRoot, "Shaders/Material/Glass/Glass.shader");
            string causticsComputePath = Path.Combine(packageRoot, "Shaders/Core/Private/TransmissiveCaustics.compute");
            Assert.That(File.Exists(glassPath), Is.True, glassPath);
            Assert.That(File.Exists(causticsComputePath), Is.True, causticsComputePath);

            string src = File.ReadAllText(glassPath);
            Assert.That(src, Does.Contain("VividTransparentBackface"));
            Assert.That(src, Does.Contain("VividRefraction"));
            Assert.That(src, Does.Contain("ShadowCaster"));
            Assert.That(src, Does.Contain("ReferencedPathtracingDXR"));
            Assert.That(src, Does.Contain("VividCausticsBackface"));
            Assert.That(src, Does.Contain("VividCausticsFrontface"));
            Assert.That(src, Does.Contain("VividGlassBeerLambert"));
            Assert.That(src, Does.Contain("VividGlassDispersionIor"));
            Assert.That(src, Does.Contain("VividGlassFresnelDielectric"));
            Assert.That(src, Does.Contain("VividGlassHenyeyGreensteinPhase"));
            Assert.That(src, Does.Contain("EvaluateGlassVolumeSingleScattering"));
            Assert.That(src, Does.Contain("backDirectSpec"));
            Assert.That(src, Does.Contain("ReferencedPathtracing.hlsl"));

            string computeSrc = File.ReadAllText(causticsComputePath);
            Assert.That(computeSrc, Does.Contain("KApplyTransmissiveCaustics"));
            Assert.That(computeSrc, Does.Contain("SampleTransmissiveCausticPCF"));
            Assert.That(computeSrc, Does.Contain("VividApplyPreExposure"));
        }

        [Test]
        public void GlassShader_IsSrpBatcherCompatible()
        {
            var shader = Shader.Find("VividRP/Material/Glass/OpticalGlass");
            Assert.That(shader, Is.Not.Null, "VividRP/Material/Glass/OpticalGlass shader not found.");

            var getCompatCode = typeof(ShaderUtil).GetMethod(
                "GetSRPBatcherCompatibilityCode",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(getCompatCode, Is.Not.Null, "ShaderUtil.GetSRPBatcherCompatibilityCode not found.");

            int compatCode = (int)getCompatCode.Invoke(null, new object[] { shader, 0 });
            Assert.That(compatCode, Is.EqualTo(0), "Glass.shader must be SRP Batcher compatible (code 0).");
        }

        [Test]
        public void StandardRenderGraphTemplate_ContainsRefractionAndCausticsPassNodes()
        {
            string packageRoot = Path.GetFullPath("Packages/com.vivid.render-pipelines");
            string templatePath = Path.Combine(packageRoot, "Editor/RenderGraph/Templates/StandardRenderGraph.vrdg.txt");
            Assert.That(File.Exists(templatePath), Is.True, templatePath);

            string src = File.ReadAllText(templatePath);
            Assert.That(src, Does.Contain("TransmissiveCausticsPass"));
            Assert.That(src, Does.Contain("OpaqueColorPyramidPass"));
            Assert.That(src, Does.Contain("TransparentBackfacePass"));
            Assert.That(src, Does.Contain("ForwardRefractionPass"));
        }
    }
}

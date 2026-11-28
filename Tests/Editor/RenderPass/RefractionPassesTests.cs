using System.IO;
using NUnit.Framework;
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
            var frameData = new ContextContainer();
            var backfacePass = new TransparentBackfacePass();
            var forwardPass = new ForwardRefractionPass();
            var pyramidPass = new OpaqueColorPyramidPass();

            try
            {
                ForwardRefractionPass.EnableRefraction = true;
                ForwardRefractionPass.EnableBackfaceRefraction = true;
                Assert.That(forwardPass.IsActive(frameData), Is.True);
                Assert.That(backfacePass.IsActive(frameData), Is.True);

                ForwardRefractionPass.EnableBackfaceRefraction = false;
                Assert.That(backfacePass.IsActive(frameData), Is.False);
                Assert.That(forwardPass.IsActive(frameData), Is.True);

                ForwardRefractionPass.EnableRefraction = false;
                Assert.That(forwardPass.IsActive(frameData), Is.False);
                Assert.That(backfacePass.IsActive(frameData), Is.False);
                Assert.That(pyramidPass.IsActive(frameData), Is.False);
            }
            finally
            {
                backfacePass.Dispose();
                forwardPass.Dispose();
                pyramidPass.Dispose();
                ForwardRefractionPass.EnableRefraction = prevRefraction;
                ForwardRefractionPass.EnableBackfaceRefraction = prevBackface;
            }
        }

        [Test]
        public void GlassShader_ContainsAllFourPassesAndOpenPbrFormulas()
        {
            string packageRoot = Path.GetFullPath("Packages/com.vivid.render-pipelines");
            string glassPath = Path.Combine(packageRoot, "Shaders/Material/Glass/Glass.shader");
            Assert.That(File.Exists(glassPath), Is.True, glassPath);

            string src = File.ReadAllText(glassPath);
            Assert.That(src, Does.Contain("VividTransparentBackface"));
            Assert.That(src, Does.Contain("VividRefraction"));
            Assert.That(src, Does.Contain("ShadowCaster"));
            Assert.That(src, Does.Contain("ReferencedPathtracingDXR"));
            Assert.That(src, Does.Contain("VividGlassBeerLambert"));
            Assert.That(src, Does.Contain("VividGlassDispersionIor"));
            Assert.That(src, Does.Contain("VividGlassFresnelDielectric"));
            Assert.That(src, Does.Contain("ReferencedPathtracing.hlsl"));
        }

        [Test]
        public void StandardRenderGraphTemplate_ContainsRefractionPassNodes()
        {
            string packageRoot = Path.GetFullPath("Packages/com.vivid.render-pipelines");
            string templatePath = Path.Combine(packageRoot, "Editor/RenderGraph/Templates/StandardRenderGraph.vrdg.txt");
            Assert.That(File.Exists(templatePath), Is.True, templatePath);

            string src = File.ReadAllText(templatePath);
            Assert.That(src, Does.Contain("OpaqueColorPyramidPass"));
            Assert.That(src, Does.Contain("TransparentBackfacePass"));
            Assert.That(src, Does.Contain("ForwardRefractionPass"));
        }
    }
}

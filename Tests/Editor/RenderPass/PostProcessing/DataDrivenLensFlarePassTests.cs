using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class DataDrivenLensFlarePassTests
    {
        private sealed class AutoRegisteredDataDrivenLensFlarePassNode : RenderPassNodeData
        {
            internal override System.Type GetRegisteredPassType() => typeof(DataDrivenLensFlarePass);
        }

        [Test]
        public void Initialize_RegistersSourceAndDepthResources()
        {
            IRenderPass renderPass = new DataDrivenLensFlarePass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "DepthTexture",
                "source",
            }));
            Assert.That(textureEntries.Single(entry => entry.Name == "source").Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(textureEntries.Single(entry => entry.Name == "DepthTexture").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(textureEntries.Single(entry => entry.Name == "source").Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(textureEntries.Single(entry => entry.Name == "DepthTexture").Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32_SFloat));
            Assert.That(textureEntries.Single(entry => entry.Name == "DepthTexture").Texture.desc.FilterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(resources.Buffers, Is.Empty);
            Assert.That(resources.RenderLists, Is.Empty);
        }

        [Test]
        public void DataDrivenLensFlarePass_InheritsFromUnsafePass_AndAllowsGlobalState()
        {
            Assert.That(typeof(UnsafePass).IsAssignableFrom(typeof(DataDrivenLensFlarePass)), Is.True);
            Assert.That(typeof(IAllowGlobalStateModificationPass).IsAssignableFrom(typeof(DataDrivenLensFlarePass)), Is.True);
        }

        [Test]
        public void DataDrivenLensFlarePassNode_DefinesSourceReadWriteAndDepthInputPorts()
        {
            var node = new AutoRegisteredDataDrivenLensFlarePassNode();

            Assert.That(node.GetInputPortByName("source_In"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("source_Out"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("depthTexture"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("depthTexture"), Is.Null);
        }

        [Test]
        public void LensFlareShader_UsesVividPipelineAndCoreLensFlareBody()
        {
            var shaderSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "PostProcessing",
                "LensFlare",
                "LensFlareDataDriven.shader"));

            Assert.That(shaderSource, Does.Contain("Shader \"Hidden/VividRP/PostProcessing/LensFlareDataDriven\""));
            Assert.That(shaderSource, Does.Contain("\"RenderPipeline\" = \"VividRenderPipeline\""));
            Assert.That(shaderSource, Does.Contain("TEXTURE2D_X_FLOAT(_CameraDepthTexture);"));
            Assert.That(shaderSource, Does.Contain("LensFlareCommon.hlsl"));
            Assert.That(shaderSource, Does.Contain("Name \"LensFlareOcclusion\""));
        }

        [Test]
        public void GeneratedNodeRegistry_ContainsDataDrivenLensFlarePass()
        {
            var source = File.ReadAllText(GetPackageFilePath("Editor", "RenderGraph", "GeneratedRenderPassNodes.g.cs"));

            Assert.That(source, Does.Contain("internal sealed class DataDrivenLensFlarePass : RenderPassNodeData"));
        }

        [Test]
        public void VividRenderPipeline_InitializesAndDisposesLensFlareCommon()
        {
            var pipelineSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderPipeline",
                "VividRenderPipeline.cs"));

            Assert.That(pipelineSource, Does.Contain("LensFlareCommonSRP.Initialize();"));
            Assert.That(pipelineSource, Does.Contain("LensFlareCommonSRP.Dispose();"));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packageRoots = new[]
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }
    }
}

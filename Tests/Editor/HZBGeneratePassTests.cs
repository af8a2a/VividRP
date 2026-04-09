using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class HZBGeneratePassTests
    {
        [Serializable]
        private sealed class AutoRegisteredHZBGeneratePassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(HZBGeneratePass).AssemblyQualifiedName;
        }

        [Test]
        public void Initialize_RegistersExpectedResources()
        {
            IRenderPass renderPass = new HZBGeneratePass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "Depth",
                "HZB",
            }));

            Assert.That(resources.Buffers.Select(entry => entry.Name), Is.EqualTo(new[] { "HZBGlobalAtomic" }));
        }

        [Test]
        public void Prepare_ConfiguresMippedRandomWriteTexture()
        {
            var pass = new HZBGeneratePass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            try
            {
                pass.Prepare(frameData);

                var hzbTexture = GetTextureField(pass, "m_HzbTexture");
                Assert.That(hzbTexture.desc.Width, Is.EqualTo(1920));
                Assert.That(hzbTexture.desc.Height, Is.EqualTo(1080));
                Assert.That(hzbTexture.desc.UseMipMap, Is.True);
                Assert.That(hzbTexture.desc.AutoGenerateMips, Is.False);
                Assert.That(hzbTexture.desc.EnableRandomWrite, Is.True);
                Assert.That(hzbTexture.desc.FilterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(hzbTexture.desc.MipCount, Is.EqualTo(11));
                Assert.That(hzbTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void Prepare_ClampsMipCountToShaderLimit()
        {
            var pass = new HZBGeneratePass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 8192;
            cameraData.actualHeight = 8192;

            try
            {
                pass.Prepare(frameData);

                var hzbTexture = GetTextureField(pass, "m_HzbTexture");
                Assert.That(hzbTexture.desc.MipCount, Is.EqualTo(13));
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void GeneratedNodeRegistry_ContainsHZBGeneratePass()
        {
            var source = File.ReadAllText(GetPackageFilePath("Editor", "RenderGraph", "GeneratedRenderPassNodes.g.cs"));

            Assert.That(source, Does.Contain("internal sealed class HZBGeneratePass : RenderPassNodeData"));
            Assert.That(source, Does.Contain("VividRP.Runtime.RenderPass.Core.HZBGeneratePass, VividRP.Runtime"));
        }

        [Test]
        public void HZBGeneratePassNode_DefinesDepthInputAndHzbOutput()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredHZBGeneratePassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_DepthTexture"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_HzbTexture"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_GlobalAtomicBuffer"), Is.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void VividRPCoreResources_DeclaresHzbComputePath()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.HZBGenerateCompute));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<VividRP.Runtime.ResourcePathAttribute>();
            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Shaders/Core/Private/DownSample/HZBGenerate"));
        }

        [Test]
        public void FidelityFxSpd_PackedWaveReduction_UsesWavePath()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "FidelityFX", "ffx_spd.hlsl"));

            Assert.That(source, Does.Contain("AH4 SpdReduceQuadH(AH4 v)"));
            Assert.That(source, Does.Contain("#if defined(A_HLSL) && defined(FFX_WAVE)"));
        }

        [Test]
        public void LightGridPass_RegistersHzbInput()
        {
            IRenderPass renderPass = new LightGridPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Contain("HZB"));
        }

        private static RenderGraphTexture GetTextureField(HZBGeneratePass pass, string fieldName)
        {
            var field = typeof(HZBGeneratePass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on HZBGeneratePass");
            return (RenderGraphTexture)field.GetValue(pass);
        }

        private static string GetPackageFilePath(params string[] parts)
        {
            var customPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "Custom_URP"));
            if (Directory.Exists(customPath))
                return Path.Combine(customPath, Path.Combine(parts));

            var vividPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "VividRP"));
            if (Directory.Exists(vividPath))
                return Path.Combine(vividPath, Path.Combine(parts));

            var legacyPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "com.af8a2a.vividrp"));
            return Path.Combine(legacyPath, Path.Combine(parts));
        }
    }
}

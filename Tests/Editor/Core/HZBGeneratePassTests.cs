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
            internal override Type GetRegisteredPassType() => typeof(HZBGeneratePass);
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
                Assert.That(hzbTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16_SFloat));
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
        public void BindMipFallback_UsesLastAvailableMipView()
        {
            Assert.That(GetBoundMipIndex(0, 11), Is.EqualTo(0));
            Assert.That(GetBoundMipIndex(10, 11), Is.EqualTo(10));
            Assert.That(GetBoundMipIndex(11, 11), Is.EqualTo(10));
            Assert.That(GetBoundMipIndex(12, 11), Is.EqualTo(10));
            Assert.That(GetBoundMipIndex(12, 13), Is.EqualTo(12));
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

            var resourcePath = field.GetCustomAttribute<VividRP.Runtime.VividResourcePathAttribute>();
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
        public void HZBGenerateCompute_PackedStore_MapsContinuationMips()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "DownSample", "HZBGenerate.compute"));
            int start = source.IndexOf("void SpdStoreH(ASU2 p, AH4 value, AU1 mip)", StringComparison.Ordinal);
            int end = source.IndexOf("AH4 SpdLoadIntermediateH(AU1 x, AU1 y)", start, StringComparison.Ordinal);

            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            Assert.That(end, Is.GreaterThan(start));

            var storeSource = source.Substring(start, end - start);
            Assert.That(storeSource, Does.Contain("rw_spd_mip6[p] = AF4(value);"));
            Assert.That(storeSource, Does.Contain("rw_spd_mip7[p] = value;"));
            Assert.That(storeSource, Does.Contain("rw_spd_mip12[p] = value;"));
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

        private static int GetBoundMipIndex(int shaderMipIndex, int mipCount)
        {
            var method = typeof(HZBGeneratePass).GetMethod("GetBoundMipIndex", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "GetBoundMipIndex method not found on HZBGeneratePass");
            return (int)method.Invoke(null, new object[] { shaderMipIndex, mipCount });
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

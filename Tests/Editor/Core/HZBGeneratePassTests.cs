using System;
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
        public void Resize_ConfiguresMippedRandomWriteTexture()
        {
            var pass = new HZBGeneratePass();

            try
            {
                pass.Resize(1920, 1080);

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
        public void Resize_ClampsMipCountToShaderLimit()
        {
            var pass = new HZBGeneratePass();

            try
            {
                pass.Resize(8192, 8192);

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
    }
}

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class RenderPassNodeBindingModeTests
    {
        [Test]
        public void PassOwnedResourceNode_HidesInputPortByDefault_AndDefinesOverrideOption()
        {
            var node = new PassOwnedTextureProducerNode();

            Assert.That(node.HasOverrideOption(PassOwnedTextureProducerPass.ColorFieldName), Is.True);
            Assert.That(node.GetInputPortByName($"{PassOwnedTextureProducerPass.ColorFieldName}_In"), Is.Null);
            Assert.That(node.GetOutputPortByName(PassOwnedTextureProducerPass.ColorFieldName), Is.Not.Null);
        }

        [Test]
        public void PassOwnedResourceNode_DefinesInputPort_WhenOverrideEnabled()
        {
            var node = new PassOwnedTextureProducerOverrideNode();

            Assert.That(node.GetInputPortByName($"{PassOwnedTextureProducerPass.ColorFieldName}_In"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName(PassOwnedTextureProducerPass.ColorFieldName), Is.Not.Null);
        }

        [Test]
        public void MixedBindingNode_KeepsExternalInputPort_WhenPassOwnedInputIsHidden()
        {
            var node = new MixedBindingPassNode();

            Assert.That(node.GetInputPortByName(MixedBindingPass.ExternalFieldName), Is.Not.Null);
            Assert.That(node.GetInputPortByName($"{MixedBindingPass.OwnedFieldName}_In"), Is.Null);
            Assert.That(node.GetOutputPortByName(MixedBindingPass.OwnedFieldName), Is.Not.Null);
        }
    }

    public class RenderGraphCompilerBindingModeTests
    {
        [Test]
        public void Compile_DoesNotCreateResourceBinding_WhenPassOwnedOverrideIsDisabled()
        {
            var graph = new RenderGraphEditorGraph();
            var producerNode = new PassOwnedTextureProducerNode();

            graph.AddNode(producerNode);

            var result = RenderGraphCompiler.Compile(graph);

            Assert.That(result.Passes, Has.Count.EqualTo(1));
            Assert.That(result.Passes[0].PassType, Is.EqualTo(GetPassTypeName<PassOwnedTextureProducerPass>()));
            Assert.That(result.Passes[0].ResourceBindings, Is.Empty);
        }

        [Test]
        public void Compile_CreatesResourceBinding_WhenPassOwnedOverrideIsEnabled_AndResourceNodeConnected()
        {
            var graph = new RenderGraphEditorGraph();
            var textureNode = new TextureResourceNodeData();
            var producerNode = new PassOwnedTextureProducerOverrideNode();

            graph.AddNode(textureNode);
            graph.AddNode(producerNode);
            graph.Connect(
                textureNode.GetOutputPortByName(TextureResourceNodeData.OutputPortName),
                producerNode.GetInputPortByName($"{PassOwnedTextureProducerPass.ColorFieldName}_In"));

            var result = RenderGraphCompiler.Compile(graph);
            var binding = result.Passes[0].ResourceBindings.Single();

            Assert.That(binding.FieldName, Is.EqualTo(PassOwnedTextureProducerPass.ColorFieldName));
            Assert.That(binding.ResourceKind, Is.EqualTo(RenderGraphResourceKind.Texture));
            Assert.That(binding.SourceKind, Is.EqualTo(RenderGraphPassBindingSourceKind.Resource));
            Assert.That(binding.ConnectionKind, Is.EqualTo(RenderGraphPassBindingConnectionKind.Input));
        }

        [Test]
        public void Compile_UsesPassFieldBinding_WhenConsumingPassOwnedOutput()
        {
            var graph = new RenderGraphEditorGraph();
            var producerNode = new PassOwnedTextureProducerNode();
            var finalBlitNode = new FinalBlitPassNode();

            graph.AddNode(finalBlitNode);
            graph.AddNode(producerNode);
            graph.Connect(
                producerNode.GetOutputPortByName(PassOwnedTextureProducerPass.ColorFieldName),
                finalBlitNode.GetInputPortByName("source"));

            var result = RenderGraphCompiler.Compile(graph);
            var consumerBinding = result.Passes[1].ResourceBindings.Single();

            Assert.That(result.Passes[0].PassType, Is.EqualTo(GetPassTypeName<PassOwnedTextureProducerPass>()));
            Assert.That(result.Passes[0].ResourceBindings, Is.Empty);
            Assert.That(result.Passes[1].PassType, Is.EqualTo(GetPassTypeName<FinalBlitPass>()));
            Assert.That(consumerBinding.SourceKind, Is.EqualTo(RenderGraphPassBindingSourceKind.PassField));
            Assert.That(consumerBinding.ConnectionKind, Is.EqualTo(RenderGraphPassBindingConnectionKind.Input));
            Assert.That(consumerBinding.SourceFieldName, Is.EqualTo(PassOwnedTextureProducerPass.ColorFieldName));
        }

        private static string GetPassTypeName<T>()
        {
            var type = typeof(T);
            return $"{type.FullName}, {type.Assembly.GetName().Name}";
        }

        [Serializable]
        private sealed class FinalBlitPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(FinalBlitPass).AssemblyQualifiedName;
        }
    }

    public sealed class PassOwnedTextureProducerPass : RasterPass
    {
        internal const string ColorFieldName = "m_Color";

        [RenderGraphResource(Name = "Color", Access = AccessFlags.Write, AttachmentIndex = 0, BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_Color;

        public PassOwnedTextureProducerPass()
        {
            m_Color = new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 1,
                    Height = 1,
                    ColorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                    Name = "CtorOwnedColor"
                }
            };
        }

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
        }

        public override void Record(RasterGraphContext context)
        {
        }

        public override void Dispose()
        {
        }
    }

    public sealed class MixedBindingPass : RasterPass
    {
        internal const string ExternalFieldName = "m_External";
        internal const string OwnedFieldName = "m_Owned";

        [RenderGraphResource(Name = "External", Access = AccessFlags.Read)]
        private RenderGraphTexture m_External = new RenderGraphTexture();

        [RenderGraphResource(Name = "Owned", Access = AccessFlags.Write, BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_Owned = new RenderGraphTexture();

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
        }

        public override void Record(RasterGraphContext context)
        {
        }

        public override void Dispose()
        {
        }
    }

    [Serializable]
    internal class PassOwnedTextureProducerNode : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => typeof(PassOwnedTextureProducerPass).AssemblyQualifiedName;

        internal bool HasOverrideOption(string fieldName)
        {
            var option = GetNodeOptionByName(RenderPassPortUtility.GetOverrideOptionName(fieldName));
            return option != null;
        }
    }

    [Serializable]
    internal sealed class PassOwnedTextureProducerOverrideNode : PassOwnedTextureProducerNode
    {
        protected override bool GetPassOwnedResourceOverrideEnabled(FieldInfo field, RenderGraphResource attr)
        {
            return (field != null
                    && field.Name == PassOwnedTextureProducerPass.ColorFieldName)
                   || base.GetPassOwnedResourceOverrideEnabled(field, attr);
        }
    }

    [Serializable]
    internal sealed class MixedBindingPassNode : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => typeof(MixedBindingPass).AssemblyQualifiedName;
    }
}

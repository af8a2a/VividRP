using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.TestTools;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using static VividRP.Editor.Tests.RenderGraphSubSystemTestUtility;

namespace VividRP.Editor.Tests
{
    public sealed class TransientResourceReflectionTests
    {
        [Test]
        public void Initialize_CollectsTransientMetadata_WhenResourceFieldIsMarkedTransient()
        {
            IRenderPass renderPass = new TransientScratchPass();

            var resources = renderPass.Initialize();

            var scratchTexture = resources.Textures.Single(entry => entry.Field.Name == TransientScratchPass.TextureFieldName);
            var scratchBuffer = resources.Buffers.Single(entry => entry.Field.Name == TransientScratchPass.BufferFieldName);
            var normalTexture = resources.Textures.Single(entry => entry.Field.Name == TransientScratchPass.NormalTextureFieldName);

            Assert.That(scratchTexture.IsTransient, Is.True);
            Assert.That(scratchBuffer.IsTransient, Is.True);
            Assert.That(normalTexture.IsTransient, Is.False);
        }
    }

    public sealed class TransientResourceNodeTests
    {
        [Test]
        public void RenderPassNode_DoesNotDefinePortsOrOverrideOptions_ForTransientResources()
        {
            var node = new TransientScratchPassNode();

            Assert.That(node.GetInputPortByName($"{TransientScratchPass.TextureFieldName}_In"), Is.Null);
            Assert.That(node.GetOutputPortByName($"{TransientScratchPass.TextureFieldName}_Out"), Is.Null);
            Assert.That(node.GetInputPortByName($"{TransientScratchPass.BufferFieldName}_In"), Is.Null);
            Assert.That(node.GetOutputPortByName($"{TransientScratchPass.BufferFieldName}_Out"), Is.Null);
            Assert.That(node.HasOverrideOption(TransientScratchPass.TextureFieldName), Is.False);
            Assert.That(node.HasOverrideOption(TransientScratchPass.BufferFieldName), Is.False);
            Assert.That(node.GetInputPortByName(TransientScratchPass.NormalTextureFieldName), Is.Not.Null);
        }
    }

    public sealed class TransientResourceCompilerTests
    {
        [Test]
        public void Compile_DoesNotCreateResourceBindings_ForTransientFields()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new TransientScratchPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.Passes, Has.Count.EqualTo(1));
                Assert.That(result.Passes[0].PassType, Is.EqualTo(GetPassTypeName<TransientScratchPass>()));
                Assert.That(result.Passes[0].ResourceBindings, Is.Empty);
                Assert.That(result.TextureDescriptors, Is.Empty);
                Assert.That(result.BufferDescriptors, Is.Empty);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }

    public sealed class TransientResourceValidatorTests
    {
        [Test]
        public void Validate_LogsError_WhenTransientFieldLacksRenderGraphResource()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                RenderGraphTestUtility.AddTestNode(graph, new MissingRenderGraphResourceTransientPassNode());
                var sink = new TestErrorsAndWarnings();
                var logger = CreateLogger(sink);

                RenderGraphEditorValidator.Validate(graph, logger);

                Assert.That(sink.Errors.Any(message => message.Contains("must also be marked")), Is.True);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Validate_LogsError_WhenTransientFieldUsesUnsupportedType()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                RenderGraphTestUtility.AddTestNode(graph, new UnsupportedTransientResourcePassNode());
                var sink = new TestErrorsAndWarnings();
                var logger = CreateLogger(sink);

                RenderGraphEditorValidator.Validate(graph, logger);

                Assert.That(sink.Errors.Any(message => message.Contains("unsupported type")), Is.True);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Validate_LogsWarning_WhenTransientFieldAlsoSetsBindingMode()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                RenderGraphTestUtility.AddTestNode(graph, new TransientWithBindingModePassNode());
                var sink = new TestErrorsAndWarnings();
                var logger = CreateLogger(sink);

                RenderGraphEditorValidator.Validate(graph, logger);

                Assert.That(sink.Warnings.Any(message => message.Contains("should not set deprecated")), Is.True);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Validate_LogsError_WhenLegacyPortStillReferencesTransientField()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var textureNode = new TextureResourceNodeData();
                var passNode = new LegacyTransientScratchPassNode();
                graph.AddNode(textureNode);
                RenderGraphTestUtility.AddTestNode(graph, passNode);
                Assert.That(graph.Connect(
                    textureNode.GetOutputPortByName(TextureResourceNodeData.OutputPortName),
                    passNode.GetInputPortByName($"{TransientScratchPass.TextureFieldName}_In")),
                    Is.True);

                var sink = new TestErrorsAndWarnings();
                var logger = CreateLogger(sink);

                RenderGraphEditorValidator.Validate(graph, logger);

                Assert.That(sink.Errors.Any(message => message.Contains("cannot be connected")), Is.True);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }

    public sealed class TransientResourcePassRecorderTests
    {
        [SetUp]
        public void SetUp()
        {
            PassRecorder.Dispose();
        }

        [TearDown]
        public void TearDown()
        {
            PassRecorder.Dispose();
        }

        [Test]
        public void SetupComputeResources_CreatesTransientTextureAndBuffer_ForTransientEntries()
        {
            var pass = new TransientOnlyScratchPass();
            IRenderPass renderPass = pass;
            var resources = renderPass.Initialize();
            var builder = new FakeRenderGraphBuilder();

            InvokeSetupComputeResources(builder, resources);

            Assert.That(builder.CreateTransientTextureCount, Is.EqualTo(1));
            Assert.That(builder.CreateTransientBufferCount, Is.EqualTo(1));
            Assert.That(builder.UseTextureCount, Is.Zero);
            Assert.That(builder.UseBufferCount, Is.Zero);
        }

        [Test]
        public void SetupRasterResources_BindsTransientAttachments_AfterCreatingTransientTexture()
        {
            IRenderPass renderPass = new TransientAttachmentPass();
            var resources = renderPass.Initialize();
            var builder = new FakeRenderGraphBuilder();

            InvokeSetupRasterResources(builder, resources);

            Assert.That(builder.CreateTransientTextureCount, Is.EqualTo(2));
            Assert.That(builder.SetRenderAttachmentCount, Is.EqualTo(1));
            Assert.That(builder.SetRenderAttachmentDepthCount, Is.EqualTo(1));
            Assert.That(builder.UseTextureCount, Is.Zero);
        }

        [Test]
        public void Compile_SkipsLegacyRuntimeBinding_WhenBindingTargetsTransientField()
        {
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();
            graphAsset.TextureDescriptors.Add(RenderGraphTextureDesc.CreateColorTarget(8, 8));
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<TransientScratchPass>(),
                ResourceBindings =
                {
                    new RenderGraphPassResourceBinding
                    {
                        FieldName = TransientScratchPass.TextureFieldName,
                        ResourceKind = RenderGraphResourceKind.Texture,
                        ResourceIndex = 0,
                        SourceKind = RenderGraphPassBindingSourceKind.Resource,
                        ConnectionKind = RenderGraphPassBindingConnectionKind.Input,
                    }
                }
            });

            try
            {
                LogAssert.Expect(
                    LogType.Warning,
                    new Regex("Skipping legacy RenderGraph binding.*" + TransientScratchPass.TextureFieldName));

                Compile(graphAsset);

                var pass = GetCompiledPasses().Single() as TransientScratchPass;
                var scratch = GetTextureField(pass, TransientScratchPass.TextureFieldName);

                Assert.That(scratch.desc.Name, Is.EqualTo("ScratchTexture"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graphAsset);
            }
        }

        private static void InvokeSetupComputeResources(
            IComputeRenderGraphBuilder builder,
            PassResource resources)
        {
            InvokeResourceSetup("SetupComputeResources", builder, resources);
        }

        private static void InvokeSetupRasterResources(
            IRasterRenderGraphBuilder builder,
            PassResource resources)
        {
            InvokeResourceSetup("SetupRasterResources", builder, resources);
        }

        private static void InvokeResourceSetup(string methodName, object builder, PassResource resources)
        {
            var method = typeof(PassRecorder).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new[]
            {
                null,
                builder,
                resources,
                new Dictionary<RenderGraphTexture, TextureHandle>(),
                new Dictionary<RenderGraphBuffer, BufferHandle>(),
                new Dictionary<RenderGraphRenderList, RendererListHandle>(),
                new Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle>(),
            });
        }

        private static void Compile(RenderGraphData graphAsset)
        {
            var method = typeof(PassRecorder).GetMethod("Compile", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { graphAsset });
        }

        private static IList<IRenderPass> GetCompiledPasses()
        {
            var field = typeof(PassRecorder).GetField("s_RenderPasses", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null);
            return (IList<IRenderPass>)field.GetValue(null);
        }

        private static RenderGraphTexture GetTextureField(object pass, string fieldName)
        {
            Assert.That(pass, Is.Not.Null);

            var field = pass.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (RenderGraphTexture)field.GetValue(pass);
        }
    }

    public sealed class TransientScratchPass : ComputePass, IAllowGlobalStateModificationPass
    {
        internal const string TextureFieldName = "m_ScratchTexture";
        internal const string BufferFieldName = "m_ScratchBuffer";
        internal const string NormalTextureFieldName = "m_NormalTexture";

        [RenderGraphResource(
            Name = "ScratchTexture",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_ScratchTexture =
            RenderGraphTexture.CreateColorTarget("ScratchTexture", GraphicsFormat.R8G8B8A8_UNorm);

        [RenderGraphResource(
            Name = "ScratchBuffer",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphBuffer m_ScratchBuffer =
            RenderGraphBuffer.CreateStructured("ScratchBuffer", 1, 16);

        [RenderGraphResource(Name = "NormalTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_NormalTexture =
            RenderGraphTexture.CreateInput("NormalTexture", GraphicsFormat.R8G8B8A8_UNorm);

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
        }

        public override void Record(ComputePassContext context)
        {
        }

        public override void Dispose()
        {
        }
    }

    public sealed class TransientWithBindingModePass : ComputePass
    {
        [RenderGraphResource(
            Name = "ScratchTexture",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        [TransientResource]
        private RenderGraphTexture m_ScratchTexture =
            RenderGraphTexture.CreateColorTarget("ScratchTexture", GraphicsFormat.R8G8B8A8_UNorm);

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
        }

        public override void Record(ComputePassContext context)
        {
        }

        public override void Dispose()
        {
        }
    }

    public sealed class TransientOnlyScratchPass : ComputePass
    {
        [RenderGraphResource(Name = "ScratchTexture", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_ScratchTexture =
            RenderGraphTexture.CreateColorTarget("ScratchTexture", GraphicsFormat.R8G8B8A8_UNorm);

        [RenderGraphResource(Name = "ScratchBuffer", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphBuffer m_ScratchBuffer =
            RenderGraphBuffer.CreateStructured("ScratchBuffer", 1, 16);

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
        }

        public override void Record(ComputePassContext context)
        {
        }

        public override void Dispose()
        {
        }
    }

    public sealed class TransientAttachmentPass : RasterPass
    {
        [RenderGraphResource(Name = "Color", Access = AccessFlags.Write, AttachmentIndex = 0)]
        [TransientResource]
        private RenderGraphTexture m_Color =
            RenderGraphTexture.CreateColorTarget("Color", GraphicsFormat.R8G8B8A8_UNorm);

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.ReadWrite, IsDepthAttachment = true)]
        [TransientResource]
        private RenderGraphTexture m_Depth =
            RenderGraphTexture.CreateDepthTarget("Depth", DepthBits.Depth32);

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
        }

        public override void Record(RasterPassContext context)
        {
        }

        public override void Dispose()
        {
        }
    }

    public sealed class MissingRenderGraphResourceTransientPass : ComputePass
    {
        [TransientResource]
        private RenderGraphTexture m_ScratchTexture =
            RenderGraphTexture.CreateColorTarget("ScratchTexture", GraphicsFormat.R8G8B8A8_UNorm);

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
        }

        public override void Record(ComputePassContext context)
        {
        }

        public override void Dispose()
        {
        }
    }

    public sealed class UnsupportedTransientResourcePass : ComputePass
    {
        [RenderGraphResource(Name = "RenderList", Access = AccessFlags.Read)]
        [TransientResource]
        private RenderGraphRenderList m_RenderList = new RenderGraphRenderList();

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
        }

        public override void Record(ComputePassContext context)
        {
        }

        public override void Dispose()
        {
        }
    }

    [Serializable]
    internal class TransientScratchPassNode : RenderPassNodeData
    {
        internal override Type GetRegisteredPassType() => typeof(TransientScratchPass);

        internal bool HasOverrideOption(string fieldName)
        {
            return GetNodeOptionByName(RenderPassPortUtility.GetOverrideOptionName(fieldName)) != null;
        }
    }

    [Serializable]
    internal sealed class LegacyTransientScratchPassNode : TransientScratchPassNode
    {
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            base.OnDefinePorts(context);
            context.AddInputPort<RenderGraphTexture>($"{TransientScratchPass.TextureFieldName}_In").Build();
        }
    }

    [Serializable]
    internal sealed class MissingRenderGraphResourceTransientPassNode : RenderPassNodeData
    {
        internal override Type GetRegisteredPassType() => typeof(MissingRenderGraphResourceTransientPass);
    }

    [Serializable]
    internal sealed class UnsupportedTransientResourcePassNode : RenderPassNodeData
    {
        internal override Type GetRegisteredPassType() => typeof(UnsupportedTransientResourcePass);
    }

    [Serializable]
    internal sealed class TransientWithBindingModePassNode : RenderPassNodeData
    {
        internal override Type GetRegisteredPassType() => typeof(TransientWithBindingModePass);
    }

    internal sealed class FakeRenderGraphBuilder :
        IComputeRenderGraphBuilder,
        IRasterRenderGraphBuilder,
        IUnsafeRenderGraphBuilder
    {
        internal int CreateTransientTextureCount;
        internal int CreateTransientBufferCount;
        internal int UseTextureCount;
        internal int UseBufferCount;
        internal int SetRenderAttachmentCount;
        internal int SetRenderAttachmentDepthCount;

        public void Dispose()
        {
        }

        public void UseTexture(in TextureHandle input, AccessFlags flags = AccessFlags.Read)
        {
            UseTextureCount++;
        }

        public void UseGlobalTexture(int propertyId, AccessFlags flags = AccessFlags.Read)
        {
        }

        public void UseAllGlobalTextures(bool enable)
        {
        }

        public void SetGlobalTextureAfterPass(in TextureHandle input, int propertyId)
        {
        }

        public BufferHandle UseBuffer(in BufferHandle input, AccessFlags flags = AccessFlags.Read)
        {
            UseBufferCount++;
            return input;
        }

        public TextureHandle CreateTransientTexture(in TextureDesc desc)
        {
            CreateTransientTextureCount++;
            return TextureHandle.nullHandle;
        }

        public TextureHandle CreateTransientTexture(in TextureHandle texture)
        {
            CreateTransientTextureCount++;
            return TextureHandle.nullHandle;
        }

        public BufferHandle CreateTransientBuffer(in BufferDesc desc)
        {
            CreateTransientBufferCount++;
            return BufferHandle.nullHandle;
        }

        public BufferHandle CreateTransientBuffer(in BufferHandle computebuffer)
        {
            CreateTransientBufferCount++;
            return BufferHandle.nullHandle;
        }

        public void UseRendererList(in RendererListHandle input)
        {
        }

        public void EnableAsyncCompute(bool value)
        {
        }

        public void AllowPassCulling(bool value)
        {
        }

        public void AllowGlobalStateModification(bool value)
        {
        }

        public void EnableFoveatedRasterization(bool value)
        {
        }

        public void GenerateDebugData(bool value)
        {
        }

        public void SetRenderAttachment(TextureHandle tex, int index, AccessFlags flags, int mipLevel, int depthSlice)
        {
            SetRenderAttachmentCount++;
        }

        public void SetRenderAttachmentDepth(TextureHandle tex, AccessFlags flags, int mipLevel, int depthSlice)
        {
            SetRenderAttachmentDepthCount++;
        }

        public TextureHandle SetRandomAccessAttachment(TextureHandle tex, int index, AccessFlags flags = AccessFlags.ReadWrite)
        {
            return tex;
        }

        public BufferHandle UseBufferRandomAccess(BufferHandle tex, int index, AccessFlags flags = AccessFlags.Read)
        {
            return tex;
        }

        public BufferHandle UseBufferRandomAccess(BufferHandle tex, int index, bool preserveCounterValue, AccessFlags flags = AccessFlags.Read)
        {
            return tex;
        }

        public void SetInputAttachment(TextureHandle tex, int index, AccessFlags flags, int mipLevel, int depthSlice)
        {
        }

        public void SetShadingRateImageAttachment(in TextureHandle tex)
        {
        }

        public void SetShadingRateFragmentSize(ShadingRateFragmentSize shadingRateFragmentSize)
        {
        }

        public void SetShadingRateCombiner(ShadingRateCombinerStage stage, ShadingRateCombiner combiner)
        {
        }

        public void SetExtendedFeatureFlags(ExtendedFeatureFlags extendedFeatureFlags)
        {
        }

        public void SetRenderFunc<PassData>(BaseRenderFunc<PassData, ComputeGraphContext> renderFunc)
            where PassData : class, new()
        {
        }

        public void SetRenderFunc<PassData>(BaseRenderFunc<PassData, RasterGraphContext> renderFunc)
            where PassData : class, new()
        {
        }

        public void SetRenderFunc<PassData>(BaseRenderFunc<PassData, UnsafeGraphContext> renderFunc)
            where PassData : class, new()
        {
        }
    }
}

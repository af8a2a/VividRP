using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using UnityRenderGraph = UnityEngine.Rendering.RenderGraphModule.RenderGraph;
using static VividRP.Editor.Tests.PassBypassTestUtility;

namespace VividRP.Editor.Tests
{
    public sealed class PassBypassReflectionTests
    {
        [Test]
        public void Collect_AddsBypassRule_WhenOutputDeclaresValidSource()
        {
            var pass = new TextureBypassPass();

            var resources = pass.Collect();

            Assert.That(resources.BypassRules, Has.Length.EqualTo(1));
            Assert.That(resources.BypassRules[0].SourceFieldName, Is.EqualTo(TextureBypassPass.SourceFieldName));
            Assert.That(resources.BypassRules[0].OutputFieldName, Is.EqualTo(TextureBypassPass.OutputFieldName));
            Assert.That(resources.BypassRules[0].ResourceType, Is.EqualTo(PassResourceType.Texture));
        }

        [Test]
        public void Collect_AddsBypassRule_ForStopNaNPass()
        {
            var pass = new StopNaNPass();

            var resources = pass.Collect();

            Assert.That(resources.BypassRules, Has.Length.EqualTo(1));
            Assert.That(resources.BypassRules[0].SourceFieldName, Is.EqualTo("m_Source"));
            Assert.That(resources.BypassRules[0].OutputFieldName, Is.EqualTo("m_OutputTexture"));
            Assert.That(resources.BypassRules[0].ResourceType, Is.EqualTo(PassResourceType.Texture));
        }

        [Test]
        public void ValidateBypassRule_ReturnsFalse_WhenSourceTypeDoesNotMatchOutput()
        {
            var result = TryValidateBypassRule<MismatchedBypassPass>(
                MismatchedBypassPass.OutputFieldName,
                out var error);

            Assert.That(result, Is.False);
            Assert.That(error, Does.Contain("same resource type"));
        }

        [Test]
        public void ValidateBypassRule_ReturnsFalse_WhenSourceIsTransient()
        {
            var result = TryValidateBypassRule<TransientSourceBypassPass>(
                TransientSourceBypassPass.OutputFieldName,
                out var error);

            Assert.That(result, Is.False);
            Assert.That(error, Does.Contain("cannot be transient"));
        }

        [Test]
        public void ValidateBypassRule_ReturnsFalse_WhenOutputTypeIsUnsupported()
        {
            var result = TryValidateBypassRule<UnsupportedBypassPass>(
                UnsupportedBypassPass.OutputFieldName,
                out var error);

            Assert.That(result, Is.False);
            Assert.That(error, Does.Contain("Only RenderGraphTexture and RenderGraphBuffer"));
        }

        private static bool TryValidateBypassRule<TPass>(string outputFieldName, out string error)
        {
            var passType = typeof(TPass);
            var outputField = passType.GetField(outputFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(outputField, Is.Not.Null);

            var outputAttr = outputField.GetCustomAttribute<RenderGraphResource>();
            var bypassAttr = outputField.GetCustomAttribute<PassBypassAttribute>();
            Assert.That(bypassAttr, Is.Not.Null);

            return PassResourceCollector.TryValidateBypassRule(
                passType,
                outputField,
                outputAttr,
                outputField.IsDeclaredTransientResourceField(),
                bypassAttr.SourceFieldName,
                out _,
                out _,
                out error);
        }
    }

    public sealed class PassBypassRecorderTests
    {
        [Test]
        public void InactiveStopNaNPassBypassDescriptors_CopiesSourceDescriptorToOutput()
        {
            var pass = new StopNaNPass();
            var source = RenderGraphTexture.CreateInput("SceneColor", GraphicsFormat.R16G16B16A16_SFloat);
            source.desc.Width = 640;
            source.desc.Height = 360;
            SetField(pass, "m_Source", source);

            var resources = pass.Collect();
            var output = GetField<RenderGraphTexture>(pass, "m_OutputTexture");
            output.desc.Width = 1;
            output.desc.Height = 1;

            InvokeApplyInactivePassBypassDescriptors(pass, resources);

            Assert.That(output.desc.Width, Is.EqualTo(640));
            Assert.That(output.desc.Height, Is.EqualTo(360));
            Assert.That(output.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        [Test]
        public void InactiveStopNaNPassBypassHandles_ForwardsSourceHandleToOutput()
        {
            var renderGraph = new UnityRenderGraph("VividRP StopNaN PassBypass Test");
            var pass = new StopNaNPass();
            var source = RenderGraphTexture.CreateInput("SceneColor", GraphicsFormat.R8G8B8A8_UNorm);
            SetField(pass, "m_Source", source);

            var resources = pass.Collect();
            var output = GetField<RenderGraphTexture>(pass, "m_OutputTexture");

            try
            {
                InvokeApplyInactivePassBypassHandles(renderGraph, pass, resources);

                Assert.That(source.innerHandle.IsValid(), Is.True);
                Assert.That(output.innerHandle.Equals(source.innerHandle), Is.True);
            }
            finally
            {
                renderGraph.Cleanup();
            }
        }

        [Test]
        public void InactiveStopNaNPassBypassHandles_DoesNotAllocate_WhenHandlesAreCached()
        {
            var renderGraph = new UnityRenderGraph("VividRP StopNaN PassBypass Allocation Test");
            var pass = new StopNaNPass();
            var source = RenderGraphTexture.CreateInput("SceneColor", GraphicsFormat.R8G8B8A8_UNorm);
            SetField(pass, "m_Source", source);

            var resources = pass.Collect();
            var textureCache = new Dictionary<RenderGraphTexture, TextureHandle>(4);
            var bufferCache = new Dictionary<RenderGraphBuffer, BufferHandle>(1);

            try
            {
                PassRecorder.ApplyInactivePassBypassHandles(
                    renderGraph,
                    pass,
                    resources,
                    null,
                    textureCache,
                    bufferCache);
                GC.Collect();

                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                PassRecorder.ApplyInactivePassBypassHandles(
                    renderGraph,
                    pass,
                    resources,
                    null,
                    textureCache,
                    bufferCache);
                var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

                Assert.That(allocatedBytes, Is.EqualTo(0));
            }
            finally
            {
                renderGraph.Cleanup();
            }
        }

        [Test]
        public void StopNaNPass_IsActive_FollowsCameraStopNaNsSetting()
        {
            var pass = new StopNaNPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var gameObject = new GameObject("StopNaNPass Test Camera");
            gameObject.AddComponent<Camera>();
            var additionalData = gameObject.AddComponent<VividAdditionalCameraData>();

            try
            {
                cameraData.additionalData = additionalData;
                additionalData.stopNaNs = false;
                Assert.That(pass.IsActive(frameData), Is.False);

                additionalData.stopNaNs = true;
                Assert.That(pass.IsActive(frameData), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void InactivePassBypassHandles_ForwardsBufferHandleToOutput()
        {
            var renderGraph = new UnityRenderGraph("VividRP PassBypass Buffer Test");
            var pass = new BufferBypassPass();
            var resources = pass.Collect();
            var source = GetField<RenderGraphBuffer>(pass, BufferBypassPass.SourceFieldName);
            var output = GetField<RenderGraphBuffer>(pass, BufferBypassPass.OutputFieldName);

            try
            {
                InvokeApplyInactivePassBypassHandles(renderGraph, pass, resources);

                Assert.That(source.innerHandle.IsValid(), Is.True);
                Assert.That(output.innerHandle.Equals(source.innerHandle), Is.True);
            }
            finally
            {
                renderGraph.Cleanup();
            }
        }

        [Test]
        public void InactiveReadWritePass_LeavesExistingHandleWithoutBypassRule()
        {
            var renderGraph = new UnityRenderGraph("VividRP PassBypass ReadWrite Test");
            var pass = new ReadWriteInactivePass();
            var resources = pass.Collect();
            var color = GetField<RenderGraphTexture>(pass, ReadWriteInactivePass.ColorFieldName);

            try
            {
                var handle = renderGraph.CreateTexture(color.desc);
                color.innerHandle = handle;

                InvokeApplyInactivePassBypassHandles(renderGraph, pass, resources);

                Assert.That(color.innerHandle.Equals(handle), Is.True);
            }
            finally
            {
                renderGraph.Cleanup();
            }
        }

        private static void InvokeApplyInactivePassBypassDescriptors(
            IRenderPass pass,
            PassResource resources,
            RenderGraphPassDefinition passDefinition = null)
        {
            var method = typeof(PassRecorder).GetMethod(
                "ApplyInactivePassBypassDescriptors",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { pass, resources, passDefinition });
        }

        private static void InvokeApplyInactivePassBypassHandles(
            UnityRenderGraph renderGraph,
            IRenderPass pass,
            PassResource resources,
            RenderGraphPassDefinition passDefinition = null)
        {
            PassRecorder.ApplyInactivePassBypassHandles(
                renderGraph,
                pass,
                resources,
                passDefinition,
                new Dictionary<RenderGraphTexture, TextureHandle>(),
                new Dictionary<RenderGraphBuffer, BufferHandle>());
        }
    }

    public sealed class TextureBypassPass : ComputePass
    {
        internal const string SourceFieldName = "m_Source";
        internal const string OutputFieldName = "m_Output";

        [RenderGraphResource(Access = AccessFlags.Read)]
        private RenderGraphTexture m_Source = RenderGraphTexture.CreateInput(
            "Source",
            GraphicsFormat.R8G8B8A8_UNorm);

        [RenderGraphResource(Access = AccessFlags.Write)]
        [PassBypass(nameof(m_Source))]
        private RenderGraphTexture m_Output = RenderGraphTexture.CreateOutput(
            "Output",
            GraphicsFormat.R8G8B8A8_UNorm);

        public override bool IsActive(ContextContainer frameData) => false;
        public override void Create() { }
        public override void Prepare(ContextContainer frameData) { }
        public override void Record(ComputePassContext context) { }
        public override void Dispose() { }
    }

    public sealed class BufferBypassPass : ComputePass
    {
        internal const string SourceFieldName = "m_Source";
        internal const string OutputFieldName = "m_Output";

        [RenderGraphResource(Access = AccessFlags.Read)]
        private RenderGraphBuffer m_Source = RenderGraphBuffer.CreateStructured("Source", 16);

        [RenderGraphResource(Access = AccessFlags.Write)]
        [PassBypass(nameof(m_Source))]
        private RenderGraphBuffer m_Output = RenderGraphBuffer.CreateStructured("Output", 16);

        public override bool IsActive(ContextContainer frameData) => false;
        public override void Create() { }
        public override void Prepare(ContextContainer frameData) { }
        public override void Record(ComputePassContext context) { }
        public override void Dispose() { }
    }

    public sealed class ReadWriteInactivePass : ComputePass
    {
        internal const string ColorFieldName = "m_Color";

        [RenderGraphResource(Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_Color = RenderGraphTexture.CreateOutput(
            "Color",
            GraphicsFormat.R8G8B8A8_UNorm);

        public override bool IsActive(ContextContainer frameData) => false;
        public override void Create() { }
        public override void Prepare(ContextContainer frameData) { }
        public override void Record(ComputePassContext context) { }
        public override void Dispose() { }
    }

    public sealed class MismatchedBypassPass
    {
        [RenderGraphResource(Access = AccessFlags.Read)]
        private RenderGraphBuffer m_Source = RenderGraphBuffer.CreateStructured("Source", 16);

        [RenderGraphResource(Access = AccessFlags.Write)]
        [PassBypass(nameof(m_Source))]
        private RenderGraphTexture m_Output = RenderGraphTexture.CreateOutput(
            "Output",
            GraphicsFormat.R8G8B8A8_UNorm);

        internal const string OutputFieldName = "m_Output";
    }

    public sealed class TransientSourceBypassPass
    {
        [RenderGraphResource(Access = AccessFlags.Read)]
        [TransientResource]
        private RenderGraphTexture m_Source = RenderGraphTexture.CreateInput(
            "Source",
            GraphicsFormat.R8G8B8A8_UNorm);

        [RenderGraphResource(Access = AccessFlags.Write)]
        [PassBypass(nameof(m_Source))]
        private RenderGraphTexture m_Output = RenderGraphTexture.CreateOutput(
            "Output",
            GraphicsFormat.R8G8B8A8_UNorm);

        internal const string OutputFieldName = "m_Output";
    }

    public sealed class UnsupportedBypassPass
    {
        [RenderGraphResource(Access = AccessFlags.Read)]
        private RenderGraphRenderList m_Source = new();

        [RenderGraphResource(Access = AccessFlags.Write)]
        [PassBypass(nameof(m_Source))]
        private RenderGraphRenderList m_Output = new();

        internal const string OutputFieldName = "m_Output";
    }

    internal static class PassBypassTestUtility
    {
        internal static T GetField<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(target);
        }

        internal static void SetField<T>(object target, string fieldName, T value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}

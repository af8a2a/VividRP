using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class ReGIRDebugPassTests
    {
        [Serializable]
        private sealed class AutoRegisteredReGIRDebugPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(ReGIRDebugPass);
        }

        [Test]
        public void Initialize_RegistersSourceDepthReGIRInputsAndColorOutput()
        {
            IRenderPass renderPass = new ReGIRDebugPass();

            var resources = renderPass.Initialize();
            var sourceEntry = resources.Textures.Single(entry => entry.Name == "SourceTexture");
            var depthEntry = resources.Textures.Single(entry => entry.Name == "DepthTexture");
            var outputEntry = resources.Textures.Single(entry => entry.Name == "OutputTexture");
            var parametersEntry = resources.Buffers.Single(entry => entry.Name == "ReGIRParameters");
            var reservoirsEntry = resources.Buffers.Single(entry => entry.Name == "ReGIRReservoirs");

            Assert.That(resources.Textures, Has.Length.EqualTo(3));
            Assert.That(resources.Buffers, Has.Length.EqualTo(2));
            Assert.That(sourceEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(depthEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(outputEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(outputEntry.AttachmentIndex, Is.EqualTo(0));
            Assert.That(outputEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
            Assert.That(parametersEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(parametersEntry.Buffer.desc.Stride, Is.EqualTo(VividReGIRParameters.Stride));
            Assert.That(reservoirsEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(reservoirsEntry.Buffer.desc.Stride, Is.EqualTo(VividReGIRReservoir.Stride));
        }

        [Test]
        public void Prepare_UsesSourceTextureSizeAndFormat_WhenConfigured()
        {
            var pass = new ReGIRDebugPass();
            var sourceTexture = GetTextureField(pass, "m_SourceTexture");
            var outputTexture = GetTextureField(pass, "m_OutputTexture");

            sourceTexture.desc.Width = 1280;
            sourceTexture.desc.Height = 720;
            sourceTexture.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;

            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            pass.Prepare(frameData);

            Assert.That(outputTexture.desc.Width, Is.EqualTo(1280));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(720));
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        [Test]
        public void ResolveSettings_UsesRenderingDebuggerValues()
        {
            var settings = ReGIRDebugPass.ResolveSettings(
                new VividRenderingDebugSettingsData
                {
                    reGIRDebugMode = ReGIRDebugVisualizationMode.ReservoirOccupancy,
                    reGIRDebugOpacity = 0.6f,
                });

            Assert.That(settings.visualizationMode, Is.EqualTo(ReGIRDebugVisualizationMode.ReservoirOccupancy));
            Assert.That(settings.opacity, Is.EqualTo(0.6f));
            Assert.That(settings.enabled, Is.True);
        }

        [Test]
        public void ResolveSettings_DisablesDebug_WhenRenderingDebuggerModeIsNone()
        {
            var settings = ReGIRDebugPass.ResolveSettings(
                new VividRenderingDebugSettingsData
                {
                    reGIRDebugMode = ReGIRDebugVisualizationMode.None,
                    reGIRDebugOpacity = 1f,
                });

            Assert.That(settings.visualizationMode, Is.EqualTo(ReGIRDebugVisualizationMode.None));
            Assert.That(settings.enabled, Is.False);
        }

        [Test]
        public void ResolveSettings_UsesRenderingDebuggerDefaults_WhenDebuggerDataIsMissing()
        {
            var settings = ReGIRDebugPass.ResolveSettings(null);

            Assert.That(settings.visualizationMode, Is.EqualTo(VividRenderingDebugSettingsData.DefaultReGIRDebugMode));
            Assert.That(settings.opacity, Is.EqualTo(VividRenderingDebugSettingsData.DefaultReGIRDebugOpacity));
            Assert.That(settings.enabled, Is.False);
        }

        [Test]
        public void Prepare_UsesRenderingDebuggerSettings()
        {
            try
            {
                VividRenderingDebugDisplaySettings.Data.reGIRDebugMode =
                    ReGIRDebugVisualizationMode.ReservoirWeight;
                VividRenderingDebugDisplaySettings.Data.reGIRDebugOpacity = 0.7f;

                var pass = new ReGIRDebugPass();
                var frameData = new ContextContainer();
                frameData.GetOrCreate<VividCameraData>().actualWidth = 64;
                frameData.GetOrCreate<VividCameraData>().actualHeight = 64;

                pass.Prepare(frameData);

                var settings = GetFieldValue<ReGIRDebugPass.ReGIRDebugSettingsData>(
                    pass,
                    "m_ResolvedSettings");
                Assert.That(settings.visualizationMode, Is.EqualTo(ReGIRDebugVisualizationMode.ReservoirWeight));
                Assert.That(settings.opacity, Is.EqualTo(0.7f));
            }
            finally
            {
                VividRenderingDebugDisplaySettings.Data.Reset();
            }
        }

        [Test]
        public void ReGIRDebugPassNode_DefinesInputsOutputAndNoInspectorOptions()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredReGIRDebugPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_SourceTexture"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DepthTexture"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_ReGIRParameterBuffer"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_ReGIRReservoirBuffer"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_OutputTexture"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_DoesNotIncludeFloatOrEnumParameters_WhenReGIRDebugPassNodeIsPresent()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredReGIRDebugPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.Passes, Has.Count.EqualTo(1));
                Assert.That(result.Passes[0].FloatParameters, Is.Empty);
                Assert.That(result.Passes[0].EnumParameters, Is.Empty);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void RenderGraphCompilation_OrdersReGIRBuildBeforeReGIRDebug()
        {
            var passDefinitions = new[]
            {
                new RenderGraphPassDefinition
                {
                    PassType = GetPassTypeName<ReGIRDebugPass>(),
                    ResourceBindings =
                    {
                        new RenderGraphPassResourceBinding
                        {
                            FieldName = "m_ReGIRParameterBuffer",
                            ResourceKind = RenderGraphResourceKind.Buffer,
                            SourceKind = RenderGraphPassBindingSourceKind.PassField,
                            SourcePassIndex = 1,
                            SourceFieldName = "m_ReGIRParameterBuffer",
                        },
                        new RenderGraphPassResourceBinding
                        {
                            FieldName = "m_ReGIRReservoirBuffer",
                            ResourceKind = RenderGraphResourceKind.Buffer,
                            SourceKind = RenderGraphPassBindingSourceKind.PassField,
                            SourcePassIndex = 1,
                            SourceFieldName = "m_ReGIRReservoirBuffer",
                        }
                    }
                },
                new RenderGraphPassDefinition
                {
                    PassType = GetPassTypeName<ReGIRGridBuildPass>(),
                }
            };

            var ordered = RenderGraphPassCompilationUtility.OrderPassDefinitions(passDefinitions);

            Assert.That(ordered.Select(pass => pass.PassType), Is.EqualTo(new[]
            {
                GetPassTypeName<ReGIRGridBuildPass>(),
                GetPassTypeName<ReGIRDebugPass>(),
            }));
            Assert.That(ordered[1].ResourceBindings.Select(binding => binding.SourcePassIndex), Is.All.Zero);
        }

        private static RenderGraphTexture GetTextureField(ReGIRDebugPass pass, string fieldName)
        {
            var texture = GetFieldValue<RenderGraphTexture>(pass, fieldName);
            Assert.That(texture, Is.Not.Null);
            return texture;
        }

        private static T GetFieldValue<T>(ReGIRDebugPass pass, string fieldName)
        {
            var field = typeof(ReGIRDebugPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(pass);
        }

        private static string GetPassTypeName<T>()
        {
            var type = typeof(T);
            return $"{type.FullName}, {type.Assembly.GetName().Name}";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class ReGIRGridBuildPassTests
    {
        [Serializable]
        private sealed class AutoRegisteredReGIRGridBuildPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(ReGIRGridBuildPass);

            internal bool TryGetMode(out VividReGIRMode value)
            {
                return TryGetEnumParameterValue("m_Mode", out value);
            }
        }

        [Test]
        public void ReGIRGridBuildPass_DeclaresOutputBufferPorts()
        {
            AssertBufferResource("m_ReGIRLightBuffer", "ReGIRLights");
            AssertBufferResource("m_ReGIRParameterBuffer", "ReGIRParameters");
            AssertBufferResource("m_ReGIRReservoirBuffer", "ReGIRReservoirs");
        }

        [Test]
        public void ReGIRGridBuildPassNode_ExposesOutputPorts()
        {
            var node = new AutoRegisteredReGIRGridBuildPassNode();

            Assert.That(node.GetOutputPortByName("m_ReGIRLightBuffer"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_ReGIRParameterBuffer"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_ReGIRReservoirBuffer"), Is.Not.Null);
            Assert.That(node.TryGetMode(out var mode), Is.True);
            Assert.That(mode, Is.EqualTo(ReGIRGridBuildPass.DefaultMode));
        }

        [Test]
        public void BuildRegistrations_IncludesReGIRGridBuildPass()
        {
            var registrations = RenderPassNodeRegistryBuilder.BuildRegistrations(new[] { typeof(ReGIRGridBuildPass) });

            Assert.That(registrations.Select(registration => registration.NodeClassName), Contains.Item(nameof(ReGIRGridBuildPass)));
        }

        [Test]
        public void ApplyEnumParameters_UpdatesMode()
        {
            var pass = new ReGIRGridBuildPass();

            RenderGraphPassEnumParameterUtility.ApplyEnumParameters(
                pass,
                typeof(ReGIRGridBuildPass),
                new List<RenderGraphPassEnumParameter>
                {
                    new()
                    {
                        FieldName = "m_Mode",
                        Value = (int)VividReGIRMode.Onion,
                    }
                });

            Assert.That(pass.Mode, Is.EqualTo(VividReGIRMode.Onion));
        }

        [Test]
        public void Compile_IncludesModeEnumParameter_WhenReGIRGridBuildPassNodeIsPresent()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredReGIRGridBuildPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.Passes, Has.Count.EqualTo(1));
                Assert.That(result.Passes[0].EnumParameters, Has.Count.EqualTo(1));
                Assert.That(result.Passes[0].EnumParameters.Single().FieldName, Is.EqualTo("m_Mode"));
                Assert.That(result.Passes[0].EnumParameters.Single().Value, Is.EqualTo((int)ReGIRGridBuildPass.DefaultMode));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Prepare_ResizesAndImportsBuffers_ForVisibleReGIRLights()
        {
            var pass = new ReGIRGridBuildPass();

            try
            {
                var frameData = new ContextContainer();
                var lightData = frameData.GetOrCreate<VividLightData>();
                lightData.reGIRLights = new[]
                {
                    new VividReGIRLightData { range = 3.0f, power = 1.0f },
                    new VividReGIRLightData { range = 5.0f, power = 2.0f },
                };
                lightData.reGIRLightCount = 2;

                pass.Prepare(frameData);

                var lightBuffer = GetBuffer(pass, "m_ReGIRLightBuffer");
                var parameterBuffer = GetBuffer(pass, "m_ReGIRParameterBuffer");
                var reservoirBuffer = GetBuffer(pass, "m_ReGIRReservoirBuffer");

                Assert.That(lightBuffer.desc.Count, Is.EqualTo(2));
                Assert.That(lightBuffer.desc.Stride, Is.EqualTo(VividReGIRLightData.Stride));
                Assert.That(parameterBuffer.desc.Count, Is.EqualTo(1));
                Assert.That(parameterBuffer.desc.Stride, Is.EqualTo(VividReGIRParameters.Stride));
                Assert.That(reservoirBuffer.desc.Count, Is.EqualTo(ExpectedDefaultSlotCount()));
                Assert.That(reservoirBuffer.desc.Stride, Is.EqualTo(VividReGIRReservoir.Stride));
                Assert.That(GetFieldValue<VividReGIRParameters>(pass, "m_ReGIRParameters").mode, Is.EqualTo(VividReGIRMode.Grid));
                AssertImportedBackingBuffer(lightBuffer);
                AssertImportedBackingBuffer(parameterBuffer);
                AssertImportedBackingBuffer(reservoirBuffer);
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void Prepare_ResizesReservoirBuffer_ForOnionMode()
        {
            var pass = new ReGIRGridBuildPass
            {
                Mode = VividReGIRMode.Onion,
            };

            try
            {
                var frameData = new ContextContainer();

                pass.Prepare(frameData);

                var reservoirBuffer = GetBuffer(pass, "m_ReGIRReservoirBuffer");
                var parameters = GetFieldValue<VividReGIRParameters>(pass, "m_ReGIRParameters");

                Assert.That(reservoirBuffer.desc.Count, Is.EqualTo(ExpectedDefaultOnionSlotCount()));
                Assert.That(parameters.mode, Is.EqualTo(VividReGIRMode.Onion));
                Assert.That(parameters.onionCellCount, Is.EqualTo((uint)ExpectedDefaultOnionCellCount()));
                Assert.That(parameters.onionLayerGroupCount, Is.EqualTo((uint)ReGIRGridBuildPass.DefaultOnionDetailLayerGroups));
                Assert.That(parameters.onionRingCount, Is.EqualTo(25u));
                Assert.That(parameters.onionCubicRootFactor, Is.GreaterThan(0f));
                Assert.That(parameters.onionLinearFactor, Is.GreaterThan(0f));
                AssertImportedBackingBuffer(reservoirBuffer);
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void Prepare_KeepsOneSizedLightBuffer_WhenThereAreNoReGIRLights()
        {
            var pass = new ReGIRGridBuildPass();

            try
            {
                var frameData = new ContextContainer();

                pass.Prepare(frameData);

                var lightBuffer = GetBuffer(pass, "m_ReGIRLightBuffer");
                var parameterBuffer = GetBuffer(pass, "m_ReGIRParameterBuffer");
                var reservoirBuffer = GetBuffer(pass, "m_ReGIRReservoirBuffer");

                Assert.That(lightBuffer.desc.Count, Is.EqualTo(1));
                Assert.That(parameterBuffer.desc.Count, Is.EqualTo(1));
                Assert.That(reservoirBuffer.desc.Count, Is.EqualTo(ExpectedDefaultSlotCount()));
                AssertImportedBackingBuffer(lightBuffer);
                AssertImportedBackingBuffer(parameterBuffer);
                AssertImportedBackingBuffer(reservoirBuffer);
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void RenderGraphCompilation_OrdersReGIRBeforeReservoirConsumer()
        {
            var passDefinitions = new[]
            {
                new RenderGraphPassDefinition
                {
                    PassType = GetPassTypeName<ReGIRReservoirConsumerPass>(),
                    ResourceBindings =
                    {
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
                GetPassTypeName<ReGIRReservoirConsumerPass>(),
            }));
            Assert.That(ordered[1].ResourceBindings[0].SourcePassIndex, Is.Zero);
        }

        private static void AssertBufferResource(string fieldName, string expectedName)
        {
            var field = typeof(ReGIRGridBuildPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            Assert.That(field.FieldType, Is.EqualTo(typeof(RenderGraphBuffer)), fieldName);

            var attr = field.GetCustomAttribute<RenderGraphResource>();
            Assert.That(attr, Is.Not.Null, fieldName);
            Assert.That(attr.Name, Is.EqualTo(expectedName), fieldName);
            Assert.That(attr.Access, Is.EqualTo(AccessFlags.Write), fieldName);
            Assert.That(attr.BindingMode, Is.EqualTo(RenderGraphResourceBindingMode.External), fieldName);
        }

        private static RenderGraphBuffer GetBuffer(ReGIRGridBuildPass pass, string fieldName)
        {
            var field = typeof(ReGIRGridBuildPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            return (RenderGraphBuffer)field.GetValue(pass);
        }

        private static T GetFieldValue<T>(ReGIRGridBuildPass pass, string fieldName)
        {
            var field = typeof(ReGIRGridBuildPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            return (T)field.GetValue(pass);
        }

        private static void AssertImportedBackingBuffer(RenderGraphBuffer buffer)
        {
            var importedGraphicsBufferProperty = typeof(RenderGraphBuffer).GetProperty(
                "ImportedGraphicsBuffer",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(importedGraphicsBufferProperty, Is.Not.Null);

            var importedGraphicsBuffer = (GraphicsBuffer)importedGraphicsBufferProperty.GetValue(buffer);
            Assert.That(importedGraphicsBuffer, Is.Not.Null);
            Assert.That(importedGraphicsBuffer.count, Is.GreaterThanOrEqualTo(buffer.desc.Count));
            Assert.That(importedGraphicsBuffer.stride, Is.EqualTo(buffer.desc.Stride));
        }

        private static int ExpectedDefaultSlotCount()
        {
            return ReGIRGridBuildPass.DefaultGridSizeX
                * ReGIRGridBuildPass.DefaultGridSizeY
                * ReGIRGridBuildPass.DefaultGridSizeZ
                * ReGIRGridBuildPass.DefaultLightsPerCell;
        }

        private static int ExpectedDefaultOnionCellCount()
        {
            return ReGIRGridBuildPass.ComputeOnionCellCount(
                ReGIRGridBuildPass.DefaultOnionDetailLayerGroups,
                ReGIRGridBuildPass.DefaultOnionCoverageLayers);
        }

        private static int ExpectedDefaultOnionSlotCount()
        {
            return ExpectedDefaultOnionCellCount() * ReGIRGridBuildPass.DefaultLightsPerCell;
        }

        private static string GetPassTypeName<T>()
        {
            var type = typeof(T);
            return $"{type.FullName}, {type.Assembly.GetName().Name}";
        }

        private sealed class ReGIRReservoirConsumerPass : ComputePass
        {
            [RenderGraphResource(Name = "ReGIRReservoirs", Access = AccessFlags.Read)]
            private readonly RenderGraphBuffer m_ReGIRReservoirBuffer =
                RenderGraphBuffer.CreateStructured("ReGIRReservoirs", VividReGIRReservoir.Stride);

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
    }
}

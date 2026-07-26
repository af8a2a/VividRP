using System;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class ReferencedPathTracingLightListPassTests
    {
        [Serializable]
        private sealed class AutoRegisteredLightListPassNode
            : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() =>
                typeof(ReferencedPathTracingLightListPass);
        }

        [Test]
        public void Initialize_RegistersPersistentListAndParameterOutputs()
        {
            IRenderPass renderPass = new ReferencedPathTracingLightListPass();

            var resources = renderPass.Initialize();
            var lightList = resources.Buffers.Single(
                resource => resource.Name == "ReferenceLightList");
            var parameters = resources.Buffers.Single(
                resource => resource.Name == "ReferenceLightListParameters");

            Assert.That(lightList.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(lightList.Buffer.desc.Count, Is.EqualTo(1));
            Assert.That(
                lightList.Buffer.desc.Stride,
                Is.EqualTo(ReferencedPathTracingLightRecord.Stride));
            Assert.That(
                lightList.Buffer.desc.Target,
                Is.EqualTo(GraphicsBuffer.Target.Structured));
            Assert.That(parameters.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(
                parameters.Buffer.desc.Count,
                Is.EqualTo(
                    ReferencedPathTracingLightSpatialIndexBuilder
                        .EmptyStorageBlockCount));
            Assert.That(
                parameters.Buffer.desc.Stride,
                Is.EqualTo(ReferencedPathTracingLightListParameters.Stride));
            Assert.That(
                parameters.Buffer.desc.Target,
                Is.EqualTo(GraphicsBuffer.Target.Structured));
        }

        [Test]
        public void Abi_StridesMatchSequentialCpuLayouts()
        {
            Assert.That(
                Marshal.SizeOf<ReferencedPathTracingLightRecord>(),
                Is.EqualTo(ReferencedPathTracingLightRecord.Stride));
            Assert.That(
                Marshal.SizeOf<ReferencedPathTracingLightListParameters>(),
                Is.EqualTo(ReferencedPathTracingLightListParameters.Stride));
            Assert.That(
                Marshal.SizeOf<ReferencedPathTracingLightListStorageBlock>(),
                Is.EqualTo(ReferencedPathTracingLightListStorageBlock.Stride));
            Assert.That(
                ReferencedPathTracingLightListBuilder
                    .Build(null)
                    .storageBlocks
                    .Length,
                Is.EqualTo(
                    ReferencedPathTracingLightSpatialIndexBuilder
                        .EmptyStorageBlockCount));
        }

        [Test]
        public void Build_SortsByStableIdAndBuildsNormalizedPowerCdf()
        {
            var rectangle = CreateLight(
                1,
                LightType.Rectangle,
                new Vector3(2.0f, 1.0f, 0.5f));
            rectangle.areaSize = new Vector2(2.0f, 3.0f);
            var point = CreateLight(
                2,
                LightType.Point,
                new Vector3(4.0f, 1.0f, 1.0f));

            var forward = ReferencedPathTracingLightListBuilder.Build(
                new[] { point, rectangle });
            var reversed = ReferencedPathTracingLightListBuilder.Build(
                new[] { rectangle, point });

            Assert.That(forward.records, Has.Length.EqualTo(2));
            Assert.That(forward.records[0].stableIdLow, Is.EqualTo(1u));
            Assert.That(forward.records[1].stableIdLow, Is.EqualTo(2u));
            Assert.That(
                forward.records[0].selectionWeight,
                Is.EqualTo(12.0f).Within(1e-6f));
            Assert.That(
                forward.records[1].selectionWeight,
                Is.EqualTo(4.0f).Within(1e-6f));
            Assert.That(
                forward.records[0].selectionPdf,
                Is.EqualTo(0.75f).Within(1e-6f));
            Assert.That(
                forward.records[0].cdf,
                Is.EqualTo(0.75f).Within(1e-6f));
            Assert.That(forward.records[1].cdf, Is.EqualTo(1.0f));
            Assert.That(forward.parameters.lightCount, Is.EqualTo(2u));
            Assert.That(forward.parameters.activeLightCount, Is.EqualTo(2u));
            Assert.That(
                forward.parameters.totalSelectionWeight,
                Is.EqualTo(16.0f).Within(1e-6f));
            Assert.That(
                forward.parameters.incompleteLocalProposalLightCount,
                Is.EqualTo(1u));
            Assert.That(
                forward.parameters.signatureLow,
                Is.EqualTo(reversed.parameters.signatureLow));
            Assert.That(
                forward.parameters.signatureHigh,
                Is.EqualTo(reversed.parameters.signatureHigh));
            CollectionAssert.AreEqual(
                forward.spatialIndex.words,
                reversed.spatialIndex.words);
            Assert.That(
                forward.storageBlocks[0].word0,
                Is.EqualTo(forward.parameters.lightCount));
            Assert.That(
                forward.storageBlocks[0].word8,
                Is.EqualTo(ReferencedPathTracingLightListParameters.Version));
            Assert.That(
                forward.storageBlocks[0].word10,
                Is.EqualTo(1u));
        }

        [Test]
        public void Build_TracksWhetherLocalProposalHasCompleteSupport()
        {
            var directional = CreateLight(
                1,
                LightType.Directional,
                Vector3.one);
            var point = CreateLight(2, LightType.Point, Vector3.one);
            var spot = CreateLight(3, LightType.Spot, Vector3.one);

            var punctualResult =
                ReferencedPathTracingLightListBuilder.Build(
                    new[] { spot, directional, point });

            Assert.That(
                punctualResult.parameters
                    .incompleteLocalProposalLightCount,
                Is.Zero);
            Assert.That(
                punctualResult.storageBlocks[0].word10,
                Is.Zero);

            var rectangle = CreateLight(
                4,
                LightType.Rectangle,
                Vector3.one);
            rectangle.areaSize = Vector2.one;
            var tube = CreateLight(5, LightType.Tube, Vector3.one);
            tube.areaSize = Vector2.one;
            var disc = CreateLight(6, LightType.Disc, Vector3.one);
            disc.shapeRadius = 0.5f;

            var shapedResult =
                ReferencedPathTracingLightListBuilder.Build(
                    new[] { disc, tube, rectangle, spot, point });

            Assert.That(
                shapedResult.parameters
                    .incompleteLocalProposalLightCount,
                Is.EqualTo(3u));
            Assert.That(
                shapedResult.storageBlocks[0].word10,
                Is.EqualTo(3u));
        }

        [Test]
        public void Build_CreatesDeterministicProjectedSpatialIndex()
        {
            var directional = CreateLight(
                1,
                LightType.Directional,
                Vector3.one);
            var point = CreateLight(2, LightType.Point, Vector3.one);
            point.positionWS = new Vector3(10.0f, -2.0f, 4.0f);
            point.range = 3.0f;
            point.rangeAttenuationScale = 1.0f / 9.0f;
            var disc = CreateLight(3, LightType.Disc, Vector3.one);
            disc.positionWS = new Vector3(-5.0f, 1.0f, 2.0f);
            disc.range = 2.0f;
            disc.rangeAttenuationScale = 0.25f;
            disc.shapeRadius = 0.5f;

            var forward = ReferencedPathTracingLightListBuilder.Build(
                new[] { disc, directional, point });
            var reversed = ReferencedPathTracingLightListBuilder.Build(
                new[] { point, directional, disc });

            Assert.That(
                forward.spatialIndex.finiteLightCount,
                Is.EqualTo(2));
            Assert.That(
                forward.spatialIndex.unboundedLightCount,
                Is.EqualTo(1));
            Assert.That(
                forward.spatialIndex.overflowCellCount,
                Is.Zero);
            Assert.That(
                forward.spatialIndex.inverseBoundsExtent.x,
                Is.GreaterThan(0.0f));
            Assert.That(
                forward.spatialIndex.inverseBoundsExtent.y,
                Is.GreaterThan(0.0f));
            Assert.That(
                forward.spatialIndex.inverseBoundsExtent.z,
                Is.GreaterThan(0.0f));
            CollectionAssert.AreEqual(
                forward.spatialIndex.words,
                reversed.spatialIndex.words);
            Assert.That(
                forward.storageBlocks.Length,
                Is.GreaterThan(1));
        }

        [Test]
        public void Build_MarksSpatialCellOverflowForCompleteScanFallback()
        {
            var lights = Enumerable.Range(
                    0,
                    ReferencedPathTracingLightSpatialIndexBuilder.CellCapacity
                        + 1)
                .Select(index =>
                {
                    var light = CreateLight(
                        (ulong)(index + 1),
                        LightType.Point,
                        Vector3.one);
                    light.positionWS = Vector3.zero;
                    light.range = 10.0f;
                    light.rangeAttenuationScale = 0.01f;
                    return light;
                })
                .ToArray();

            var result =
                ReferencedPathTracingLightListBuilder.Build(lights);

            Assert.That(
                result.spatialIndex.overflowCellCount,
                Is.GreaterThan(0));
            Assert.That(
                result.spatialIndex.finiteLightCount,
                Is.EqualTo(lights.Length));
            Assert.That(
                result.spatialIndex.words[7],
                Is.EqualTo(
                    (uint)result.spatialIndex.overflowCellCount));
        }

        [Test]
        public void Build_EncodesDeltaAndContinuousMeasureFlags()
        {
            var directional = CreateLight(
                10,
                LightType.Directional,
                Vector3.one);
            directional.angularDiameter = 0.533f;
            var point = CreateLight(11, LightType.Point, Vector3.one);
            var tube = CreateLight(12, LightType.Tube, Vector3.one);
            tube.areaSize = new Vector2(2.0f, 0.0f);
            var disc = CreateLight(13, LightType.Disc, Vector3.one);
            disc.shapeRadius = 0.5f;

            var result = ReferencedPathTracingLightListBuilder.Build(
                new[] { disc, tube, point, directional });

            var directionalFlags =
                (ReferencedPathTracingLightFlags)result.records[0].flags;
            var pointFlags =
                (ReferencedPathTracingLightFlags)result.records[1].flags;
            var tubeFlags =
                (ReferencedPathTracingLightFlags)result.records[2].flags;
            var discFlags =
                (ReferencedPathTracingLightFlags)result.records[3].flags;

            Assert.That(
                result.records[0].angularDiameter,
                Is.EqualTo(0.533f * Mathf.Deg2Rad).Within(1e-6f));
            Assert.That(
                directionalFlags
                & ReferencedPathTracingLightFlags.Infinite,
                Is.Not.EqualTo(ReferencedPathTracingLightFlags.None));
            Assert.That(
                directionalFlags
                & ReferencedPathTracingLightFlags.BsdfReachable,
                Is.Not.EqualTo(ReferencedPathTracingLightFlags.None));
            Assert.That(
                pointFlags & ReferencedPathTracingLightFlags.Singular,
                Is.Not.EqualTo(ReferencedPathTracingLightFlags.None));
            Assert.That(
                tubeFlags
                & ReferencedPathTracingLightFlags.UsesLineMeasure,
                Is.Not.EqualTo(ReferencedPathTracingLightFlags.None));
            Assert.That(
                discFlags
                & ReferencedPathTracingLightFlags.UsesAreaMeasure,
                Is.Not.EqualTo(ReferencedPathTracingLightFlags.None));
            Assert.That(
                discFlags
                & ReferencedPathTracingLightFlags.BsdfReachable,
                Is.Not.EqualTo(ReferencedPathTracingLightFlags.None));
        }

        [Test]
        public void Build_ReportsUnsupportedAndUnstableActiveLights()
        {
            var unsupported = CreateLight(1, LightType.Box, Vector3.one);
            var unstable = CreateLight(2, LightType.Point, Vector3.one);
            unstable.lightEntityId = EntityId.None;
            var disabled = CreateLight(3, LightType.Point, Vector3.one);
            disabled.flags = VividLightRenderDataFlags.ActiveInHierarchy;

            var result = ReferencedPathTracingLightListBuilder.Build(
                new[] { unsupported, unstable, disabled });

            Assert.That(result.records, Is.Empty);
            Assert.That(result.parameters.lightCount, Is.Zero);
            Assert.That(result.parameters.activeLightCount, Is.Zero);
            Assert.That(result.parameters.unsupportedLightCount, Is.EqualTo(1u));
            Assert.That(result.parameters.unstableLightCount, Is.EqualTo(1u));
            Assert.That(result.parameters.totalSelectionWeight, Is.Zero);
            Assert.That(result.parameters.inverseTotalSelectionWeight, Is.Zero);
        }

        [Test]
        public void Build_ZeroPowerListKeepsFiniteZeroPdf()
        {
            var result = ReferencedPathTracingLightListBuilder.Build(
                new[]
                {
                    CreateLight(1, LightType.Point, Vector3.zero),
                });

            Assert.That(result.records, Has.Length.EqualTo(1));
            Assert.That(result.records[0].selectionWeight, Is.Zero);
            Assert.That(result.records[0].selectionPdf, Is.Zero);
            Assert.That(result.records[0].cdf, Is.Zero);
            Assert.That(result.parameters.activeLightCount, Is.Zero);
            Assert.That(result.parameters.totalSelectionWeight, Is.Zero);
        }

        [Test]
        public void Build_RejectsDuplicateStableIdsWithoutOrderDependence()
        {
            var red = CreateLight(
                7,
                LightType.Point,
                new Vector3(1.0f, 0.0f, 0.0f));
            var blue = CreateLight(
                7,
                LightType.Point,
                new Vector3(0.0f, 0.0f, 1.0f));

            var forward = ReferencedPathTracingLightListBuilder.Build(
                new[] { red, blue });
            var reversed = ReferencedPathTracingLightListBuilder.Build(
                new[] { blue, red });

            Assert.That(forward.records, Is.Empty);
            Assert.That(forward.parameters.unstableLightCount, Is.EqualTo(2u));
            Assert.That(
                forward.parameters.signatureLow,
                Is.EqualTo(reversed.parameters.signatureLow));
            Assert.That(
                forward.parameters.signatureHigh,
                Is.EqualTo(reversed.parameters.signatureHigh));
        }

        [Test]
        public void RenderGraphNode_DefinesListAndParameterOutputs()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredLightListPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(
                    node.GetOutputPortByName("m_ReferenceLightList"),
                    Is.Not.Null);
                Assert.That(
                    node.GetOutputPortByName(
                        "m_ReferenceLightListParameters"),
                    Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        private static VividLightRenderData CreateLight(
            ulong stableId,
            LightType lightType,
            Vector3 color)
        {
            return new VividLightRenderData
            {
                lightEntityId = EntityId.FromULong(stableId),
                lightType = lightType,
                positionWS = new Vector3(1.0f, 2.0f, 3.0f),
                range = 10.0f,
                forwardWS = Vector3.forward,
                rightWS = Vector3.right,
                upWS = Vector3.up,
                areaSize = Vector2.one,
                shapeRadius = 0.25f,
                color = color,
                shadowStrength = 1.0f,
                spotAngle = 60.0f,
                innerSpotAngle = 30.0f,
                rangeAttenuationScale = 0.01f,
                rangeAttenuationBias = 1.0f,
                renderingLayerMask = uint.MaxValue,
                shadowRenderingLayerMask = uint.MaxValue,
                flags = VividLightRenderDataFlags.Enabled
                    | VividLightRenderDataFlags.ActiveInHierarchy
                    | VividLightRenderDataFlags.CastShadows,
            };
        }
    }
}

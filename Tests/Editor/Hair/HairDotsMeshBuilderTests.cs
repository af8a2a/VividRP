using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class HairDotsMeshBuilderTests
    {
        [Test]
        public void Build_EmitsFourTrianglesAndStableEndpointAttributes()
        {
            var segment = new HairStrandSegment(
                new HairStrandPoint(
                    new Vector3(1.0f, 2.0f, 3.0f),
                    0.1f,
                    new Vector2(0.0f, 0.25f)),
                new HairStrandPoint(
                    new Vector3(1.0f, 4.0f, 3.0f),
                    0.05f,
                    new Vector2(1.0f, 0.75f)));
            var mesh = HairDotsMeshBuilder.Build(new[] { segment });

            try
            {
                Assert.That(
                    mesh.vertexCount,
                    Is.EqualTo(HairDotsMeshBuilder.VertexCountPerSegment));
                Assert.That(
                    mesh.triangles.Length,
                    Is.EqualTo(
                        HairDotsMeshBuilder.TriangleCountPerSegment * 3));

                var positions = mesh.vertices;
                var normals = mesh.normals;
                var radii = new List<Vector2>();
                mesh.GetUVs(1, radii);
                var previousCenterlines = new List<Vector4>();
                mesh.GetUVs(2, previousCenterlines);
                var indices = mesh.triangles;

                for (var triangleIndex = 0;
                     triangleIndex
                        < HairDotsMeshBuilder.TriangleCountPerSegment;
                     triangleIndex++)
                {
                    var firstIndex = indices[triangleIndex * 3];
                    var lastIndex = indices[triangleIndex * 3 + 2];
                    var recoveredStart = positions[firstIndex]
                        - normals[firstIndex] * radii[firstIndex].x;
                    var recoveredEnd = positions[lastIndex]
                        - normals[lastIndex] * radii[lastIndex].x;

                    AssertVectorApproximately(
                        segment.Start.Position,
                        recoveredStart);
                    AssertVectorApproximately(
                        segment.End.Position,
                        recoveredEnd);
                    Assert.That(radii[firstIndex].y, Is.EqualTo(0.0f));
                    Assert.That(radii[lastIndex].y, Is.EqualTo(1.0f));
                    AssertVectorApproximately(
                        segment.Start.Position,
                        previousCenterlines[firstIndex]);
                    AssertVectorApproximately(
                        segment.End.Position,
                        previousCenterlines[lastIndex]);
                    Assert.That(
                        previousCenterlines[firstIndex].w,
                        Is.EqualTo(radii[firstIndex].x).Within(1e-6f));
                    Assert.That(
                        previousCenterlines[lastIndex].w,
                        Is.EqualTo(radii[lastIndex].x).Within(1e-6f));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BuildDynamic_StoresPreviousCenterlineAndRadiusInUv2()
        {
            var current = new HairStrandSegment(
                new HairStrandPoint(
                    new Vector3(0.5f, 0.0f, 0.0f),
                    0.12f,
                    Vector2.zero),
                new HairStrandPoint(
                    new Vector3(0.5f, 1.0f, 0.25f),
                    0.06f,
                    Vector2.one));
            var previous = new HairStrandSegment(
                new HairStrandPoint(
                    new Vector3(-0.25f, 0.0f, 0.0f),
                    0.1f,
                    Vector2.zero),
                new HairStrandPoint(
                    new Vector3(-0.1f, 0.9f, -0.2f),
                    0.04f,
                    Vector2.one));
            var mesh = HairDotsMeshBuilder.BuildDynamic(
                new[] { current },
                new[] { previous });

            try
            {
                var previousCenterlines = new List<Vector4>();
                mesh.GetUVs(2, previousCenterlines);
                var indices = mesh.triangles;

                for (var triangleIndex = 0;
                     triangleIndex
                        < HairDotsMeshBuilder.TriangleCountPerSegment;
                     triangleIndex++)
                {
                    var firstIndex = indices[triangleIndex * 3];
                    var lastIndex = indices[triangleIndex * 3 + 2];
                    AssertVectorApproximately(
                        previous.Start.Position,
                        previousCenterlines[firstIndex]);
                    AssertVectorApproximately(
                        previous.End.Position,
                        previousCenterlines[lastIndex]);
                    Assert.That(
                        previousCenterlines[firstIndex].w,
                        Is.EqualTo(
                                previous.Start.Radius
                                * HairDotsMeshBuilder.RadiusCompensation)
                            .Within(1e-6f));
                    Assert.That(
                        previousCenterlines[lastIndex].w,
                        Is.EqualTo(
                                previous.End.Radius
                                * HairDotsMeshBuilder.RadiusCompensation)
                            .Within(1e-6f));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BuildDynamic_RejectsMismatchedFrameTopology()
        {
            var segment = new HairStrandSegment(
                new HairStrandPoint(Vector3.zero, 0.1f, Vector2.zero),
                new HairStrandPoint(Vector3.up, 0.1f, Vector2.one));

            Assert.Throws<ArgumentException>(() =>
                HairDotsMeshBuilder.BuildDynamic(
                    new[] { segment },
                    Array.Empty<HairStrandSegment>()));
        }

        [Test]
        public void CreatePersistent_UsesFixedRawVertexLayout()
        {
            const int segmentCount = 3;
            var mesh = HairDotsMeshBuilder.CreatePersistent(segmentCount);

            try
            {
                Assert.That(
                    mesh.vertexCount,
                    Is.EqualTo(
                        segmentCount
                        * HairDotsMeshBuilder.VertexCountPerSegment));
                Assert.That(mesh.subMeshCount, Is.EqualTo(1));
                Assert.That(
                    mesh.GetIndexCount(0),
                    Is.EqualTo((uint)mesh.vertexCount));
                Assert.That(mesh.indexFormat, Is.EqualTo(IndexFormat.UInt32));
                Assert.That(
                    mesh.vertexBufferTarget & GraphicsBuffer.Target.Raw,
                    Is.EqualTo(GraphicsBuffer.Target.Raw));
                Assert.That(
                    mesh.GetVertexBufferStride(0),
                    Is.EqualTo(HairDotsMeshBuilder.PersistentVertexStride));

                AssertVertexAttribute(
                    mesh,
                    VertexAttribute.Position,
                    3,
                    0);
                AssertVertexAttribute(
                    mesh,
                    VertexAttribute.Normal,
                    3,
                    12);
                AssertVertexAttribute(
                    mesh,
                    VertexAttribute.Tangent,
                    4,
                    24);
                AssertVertexAttribute(
                    mesh,
                    VertexAttribute.TexCoord0,
                    2,
                    40);
                AssertVertexAttribute(
                    mesh,
                    VertexAttribute.TexCoord1,
                    2,
                    48);
                AssertVertexAttribute(
                    mesh,
                    VertexAttribute.TexCoord2,
                    4,
                    56);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Build_AppliesRadiusScaleAndVolumeCompensation()
        {
            const float radiusScale = 2.0f;
            var segment = new HairStrandSegment(
                new HairStrandPoint(Vector3.zero, 0.25f, Vector2.zero),
                new HairStrandPoint(Vector3.forward, 0.5f, Vector2.one));
            var mesh = HairDotsMeshBuilder.Build(
                new[] { segment },
                radiusScale: radiusScale);

            try
            {
                var radii = new List<Vector2>();
                mesh.GetUVs(1, radii);
                Assert.That(
                    radii[0].x,
                    Is.EqualTo(
                            segment.Start.Radius
                            * radiusScale
                            * HairDotsMeshBuilder.RadiusCompensation)
                        .Within(1e-6f));
                Assert.That(
                    radii[2].x,
                    Is.EqualTo(
                            segment.End.Radius
                            * radiusScale
                            * HairDotsMeshBuilder.RadiusCompensation)
                        .Within(1e-6f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Build_RejectsDegenerateOrInvalidSegments()
        {
            var coincident = new HairStrandSegment(
                new HairStrandPoint(Vector3.zero, 0.1f, Vector2.zero),
                new HairStrandPoint(Vector3.zero, 0.1f, Vector2.one));
            var invalidRadius = new HairStrandSegment(
                new HairStrandPoint(Vector3.zero, 0.0f, Vector2.zero),
                new HairStrandPoint(Vector3.forward, 0.1f, Vector2.one));

            Assert.Throws<ArgumentException>(
                () => HairDotsMeshBuilder.Build(new[] { coincident }));
            Assert.Throws<ArgumentException>(
                () => HairDotsMeshBuilder.Build(new[] { invalidRadius }));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => HairDotsMeshBuilder.Build(
                    Array.Empty<HairStrandSegment>(),
                    radiusScale: 0.0f));
        }

        private static void AssertVectorApproximately(
            Vector3 expected,
            Vector3 actual)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-5f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-5f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(1e-5f));
        }

        private static void AssertVectorApproximately(
            Vector3 expected,
            Vector4 actual)
        {
            AssertVectorApproximately(
                expected,
                new Vector3(actual.x, actual.y, actual.z));
        }

        private static void AssertVertexAttribute(
            Mesh mesh,
            VertexAttribute attribute,
            int dimension,
            int offset)
        {
            Assert.That(mesh.HasVertexAttribute(attribute), Is.True);
            Assert.That(
                mesh.GetVertexAttributeDimension(attribute),
                Is.EqualTo(dimension));
            Assert.That(
                mesh.GetVertexAttributeFormat(attribute),
                Is.EqualTo(VertexAttributeFormat.Float32));
            Assert.That(
                mesh.GetVertexAttributeStream(attribute),
                Is.EqualTo(0));
            Assert.That(
                mesh.GetVertexAttributeOffset(attribute),
                Is.EqualTo(offset));
        }
    }
}

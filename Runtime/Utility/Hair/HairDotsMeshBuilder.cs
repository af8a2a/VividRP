using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public static class HairDotsMeshBuilder
    {
        public const int TriangleCountPerSegment = 4;
        public const int VertexCountPerSegment = TriangleCountPerSegment * 3;
        public const int PersistentVertexStride = 72;
        public static readonly float RadiusCompensation =
            1.0f / (Mathf.Sin(Mathf.PI / 4.0f) / (Mathf.PI / 4.0f));

        /// <summary>
        /// Builds a static DOTS mesh. The current centerline is also written as
        /// previous-frame history so object and camera motion remain valid.
        /// </summary>
        public static Mesh Build(
            IReadOnlyList<HairStrandSegment> segments,
            Mesh target = null,
            float radiusScale = 1.0f)
        {
            return BuildInternal(
                segments,
                segments,
                target,
                radiusScale,
                false);
        }

        /// <summary>
        /// Builds or updates a deforming DOTS mesh with explicit previous-frame
        /// centerline and radius data. Frame arrays must have matching topology
        /// and segment ordering.
        /// </summary>
        public static Mesh BuildDynamic(
            IReadOnlyList<HairStrandSegment> currentSegments,
            IReadOnlyList<HairStrandSegment> previousSegments,
            Mesh target = null,
            float radiusScale = 1.0f)
        {
            return BuildInternal(
                currentSegments,
                previousSegments,
                target,
                radiusScale,
                true);
        }

        /// <summary>
        /// Allocates fixed vertex and index storage for compute-driven DOTS
        /// expansion. The returned mesh must be populated before RTAS build.
        /// </summary>
        public static Mesh CreatePersistent(
            int segmentCount,
            Mesh target = null)
        {
            if (segmentCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(segmentCount));

            int vertexCount;
            try
            {
                vertexCount = checked(segmentCount * VertexCountPerSegment);
            }
            catch (OverflowException exception)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(segmentCount),
                    exception.Message);
            }

            var mesh = target != null ? target : new Mesh();
            mesh.Clear();
            mesh.name = string.IsNullOrEmpty(mesh.name)
                ? "Persistent Hair DOTS Mesh"
                : mesh.name;
            mesh.MarkDynamic();
            mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            mesh.SetVertexBufferParams(
                vertexCount,
                new VertexAttributeDescriptor(
                    VertexAttribute.Position,
                    VertexAttributeFormat.Float32,
                    3),
                new VertexAttributeDescriptor(
                    VertexAttribute.Normal,
                    VertexAttributeFormat.Float32,
                    3),
                new VertexAttributeDescriptor(
                    VertexAttribute.Tangent,
                    VertexAttributeFormat.Float32,
                    4),
                new VertexAttributeDescriptor(
                    VertexAttribute.TexCoord0,
                    VertexAttributeFormat.Float32,
                    2),
                new VertexAttributeDescriptor(
                    VertexAttribute.TexCoord1,
                    VertexAttributeFormat.Float32,
                    2),
                new VertexAttributeDescriptor(
                    VertexAttribute.TexCoord2,
                    VertexAttributeFormat.Float32,
                    4));

            var indices = new int[vertexCount];
            for (var index = 0; index < vertexCount; index++)
                indices[index] = index;
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetIndices(
                indices,
                MeshTopology.Triangles,
                0,
                false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 0.001f);
            return mesh;
        }

        private static Mesh BuildInternal(
            IReadOnlyList<HairStrandSegment> currentSegments,
            IReadOnlyList<HairStrandSegment> previousSegments,
            Mesh target,
            float radiusScale,
            bool markDynamic)
        {
            if (currentSegments == null)
                throw new ArgumentNullException(nameof(currentSegments));
            if (previousSegments == null)
                throw new ArgumentNullException(nameof(previousSegments));
            if (currentSegments.Count != previousSegments.Count)
            {
                throw new ArgumentException(
                    "Current and previous strand segment counts must match.",
                    nameof(previousSegments));
            }
            if (!IsFinite(radiusScale) || radiusScale <= 0.0f)
                throw new ArgumentOutOfRangeException(nameof(radiusScale));

            int vertexCount;
            try
            {
                vertexCount = checked(
                    currentSegments.Count * VertexCountPerSegment);
            }
            catch (OverflowException exception)
            {
                throw new ArgumentException(
                    "The strand collection is too large for a Unity mesh.",
                    nameof(currentSegments),
                    exception);
            }

            var positions = new List<Vector3>(vertexCount);
            var normals = new List<Vector3>(vertexCount);
            var tangents = new List<Vector4>(vertexCount);
            var strandUVs = new List<Vector2>(vertexCount);
            var radiusAndEndpoint = new List<Vector2>(vertexCount);
            var previousCenterlineAndRadius =
                new List<Vector4>(vertexCount);
            var indices = new List<int>(vertexCount);

            for (var segmentIndex = 0;
                 segmentIndex < currentSegments.Count;
                 segmentIndex++)
            {
                AppendSegment(
                    currentSegments[segmentIndex],
                    previousSegments[segmentIndex],
                    segmentIndex,
                    radiusScale,
                    positions,
                    normals,
                    tangents,
                    strandUVs,
                    radiusAndEndpoint,
                    previousCenterlineAndRadius,
                    indices);
            }

            var mesh = target != null ? target : new Mesh();
            if (markDynamic)
                mesh.MarkDynamic();
            mesh.Clear();
            mesh.name = string.IsNullOrEmpty(mesh.name)
                ? "Hair DOTS Mesh"
                : mesh.name;
            mesh.indexFormat = vertexCount > ushort.MaxValue
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, strandUVs);
            mesh.SetUVs(1, radiusAndEndpoint);
            mesh.SetUVs(2, previousCenterlineAndRadius);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AppendSegment(
            HairStrandSegment currentSegment,
            HairStrandSegment previousSegment,
            int segmentIndex,
            float radiusScale,
            List<Vector3> positions,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> strandUVs,
            List<Vector2> radiusAndEndpoint,
            List<Vector4> previousCenterlineAndRadius,
            List<int> indices)
        {
            ValidatePoint(
                currentSegment.Start,
                segmentIndex,
                "current start");
            ValidatePoint(
                currentSegment.End,
                segmentIndex,
                "current end");
            ValidatePoint(
                previousSegment.Start,
                segmentIndex,
                "previous start");
            ValidatePoint(
                previousSegment.End,
                segmentIndex,
                "previous end");

            var segmentVector =
                currentSegment.End.Position - currentSegment.Start.Position;
            var length = segmentVector.magnitude;
            if (!IsFinite(length) || length <= 1e-7f)
            {
                throw new ArgumentException(
                    $"Hair segment {segmentIndex} has coincident endpoints.",
                    nameof(currentSegment));
            }

            var previousSegmentVector = previousSegment.End.Position
                - previousSegment.Start.Position;
            var previousLength = previousSegmentVector.magnitude;
            if (!IsFinite(previousLength) || previousLength <= 1e-7f)
            {
                throw new ArgumentException(
                    $"Previous hair segment {segmentIndex} has "
                    + "coincident endpoints.",
                    nameof(previousSegment));
            }

            var tangent = segmentVector / length;
            var referenceAxis = Mathf.Abs(Vector3.Dot(tangent, Vector3.up))
                < 0.999f
                ? Vector3.up
                : Vector3.right;
            var firstAxis = Vector3.Normalize(
                Vector3.Cross(tangent, referenceAxis));
            var secondAxis = Vector3.Normalize(
                Vector3.Cross(tangent, firstAxis));
            var tangent4 = new Vector4(
                tangent.x,
                tangent.y,
                tangent.z,
                1.0f);
            var radius0 = currentSegment.Start.Radius
                * radiusScale
                * RadiusCompensation;
            var radius1 = currentSegment.End.Radius
                * radiusScale
                * RadiusCompensation;
            var previousRadius0 = previousSegment.Start.Radius
                * radiusScale
                * RadiusCompensation;
            var previousRadius1 = previousSegment.End.Radius
                * radiusScale
                * RadiusCompensation;

            AppendFace(
                currentSegment,
                previousSegment,
                firstAxis,
                tangent4,
                radius0,
                radius1,
                previousRadius0,
                previousRadius1,
                positions,
                normals,
                tangents,
                strandUVs,
                radiusAndEndpoint,
                previousCenterlineAndRadius,
                indices);
            AppendFace(
                currentSegment,
                previousSegment,
                secondAxis,
                tangent4,
                radius0,
                radius1,
                previousRadius0,
                previousRadius1,
                positions,
                normals,
                tangents,
                strandUVs,
                radiusAndEndpoint,
                previousCenterlineAndRadius,
                indices);
        }

        private static void AppendFace(
            HairStrandSegment currentSegment,
            HairStrandSegment previousSegment,
            Vector3 axis,
            Vector4 tangent,
            float radius0,
            float radius1,
            float previousRadius0,
            float previousRadius1,
            List<Vector3> positions,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> strandUVs,
            List<Vector2> radiusAndEndpoint,
            List<Vector4> previousCenterlineAndRadius,
            List<int> indices)
        {
            var positiveStart =
                currentSegment.Start.Position + axis * radius0;
            var negativeStart =
                currentSegment.Start.Position - axis * radius0;
            var positiveEnd = currentSegment.End.Position + axis * radius1;
            var negativeEnd = currentSegment.End.Position - axis * radius1;

            AppendVertex(
                positiveStart,
                axis,
                tangent,
                currentSegment.Start.UV,
                radius0,
                0.0f,
                previousSegment.Start,
                previousRadius0,
                positions,
                normals,
                tangents,
                strandUVs,
                radiusAndEndpoint,
                previousCenterlineAndRadius,
                indices);
            AppendVertex(
                negativeEnd,
                -axis,
                tangent,
                currentSegment.End.UV,
                radius1,
                1.0f,
                previousSegment.End,
                previousRadius1,
                positions,
                normals,
                tangents,
                strandUVs,
                radiusAndEndpoint,
                previousCenterlineAndRadius,
                indices);
            AppendVertex(
                positiveEnd,
                axis,
                tangent,
                currentSegment.End.UV,
                radius1,
                1.0f,
                previousSegment.End,
                previousRadius1,
                positions,
                normals,
                tangents,
                strandUVs,
                radiusAndEndpoint,
                previousCenterlineAndRadius,
                indices);

            AppendVertex(
                positiveStart,
                axis,
                tangent,
                currentSegment.Start.UV,
                radius0,
                0.0f,
                previousSegment.Start,
                previousRadius0,
                positions,
                normals,
                tangents,
                strandUVs,
                radiusAndEndpoint,
                previousCenterlineAndRadius,
                indices);
            AppendVertex(
                negativeStart,
                -axis,
                tangent,
                currentSegment.Start.UV,
                radius0,
                0.0f,
                previousSegment.Start,
                previousRadius0,
                positions,
                normals,
                tangents,
                strandUVs,
                radiusAndEndpoint,
                previousCenterlineAndRadius,
                indices);
            AppendVertex(
                negativeEnd,
                -axis,
                tangent,
                currentSegment.End.UV,
                radius1,
                1.0f,
                previousSegment.End,
                previousRadius1,
                positions,
                normals,
                tangents,
                strandUVs,
                radiusAndEndpoint,
                previousCenterlineAndRadius,
                indices);
        }

        private static void AppendVertex(
            Vector3 position,
            Vector3 normal,
            Vector4 tangent,
            Vector2 uv,
            float radius,
            float endpoint,
            HairStrandPoint previousPoint,
            float previousRadius,
            List<Vector3> positions,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> strandUVs,
            List<Vector2> radiusAndEndpoint,
            List<Vector4> previousCenterlineAndRadius,
            List<int> indices)
        {
            indices.Add(positions.Count);
            positions.Add(position);
            normals.Add(normal);
            tangents.Add(tangent);
            strandUVs.Add(uv);
            radiusAndEndpoint.Add(new Vector2(radius, endpoint));
            previousCenterlineAndRadius.Add(new Vector4(
                previousPoint.Position.x,
                previousPoint.Position.y,
                previousPoint.Position.z,
                previousRadius));
        }

        private static void ValidatePoint(
            HairStrandPoint point,
            int segmentIndex,
            string endpointName)
        {
            if (!IsFinite(point.Position)
                || !IsFinite(point.UV)
                || !IsFinite(point.Radius)
                || point.Radius <= 0.0f)
            {
                throw new ArgumentException(
                    $"Hair segment {segmentIndex} has an invalid {endpointName}.",
                    nameof(point));
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x)
                   && IsFinite(value.y)
                   && IsFinite(value.z);
        }

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}

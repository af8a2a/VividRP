using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal static class HairDotsMeshBuilder
    {
        internal const int TriangleCountPerSegment = 4;
        internal const int VertexCountPerSegment = TriangleCountPerSegment * 3;
        internal static readonly float RadiusCompensation =
            1.0f / (Mathf.Sin(Mathf.PI / 4.0f) / (Mathf.PI / 4.0f));

        internal static Mesh Build(
            IReadOnlyList<HairStrandSegment> segments,
            Mesh target = null,
            float radiusScale = 1.0f)
        {
            if (segments == null)
                throw new ArgumentNullException(nameof(segments));
            if (!IsFinite(radiusScale) || radiusScale <= 0.0f)
                throw new ArgumentOutOfRangeException(nameof(radiusScale));

            int vertexCount;
            try
            {
                vertexCount = checked(segments.Count * VertexCountPerSegment);
            }
            catch (OverflowException exception)
            {
                throw new ArgumentException(
                    "The strand collection is too large for a Unity mesh.",
                    nameof(segments),
                    exception);
            }

            var positions = new List<Vector3>(vertexCount);
            var normals = new List<Vector3>(vertexCount);
            var tangents = new List<Vector4>(vertexCount);
            var strandUVs = new List<Vector2>(vertexCount);
            var radiusAndEndpoint = new List<Vector2>(vertexCount);
            var indices = new List<int>(vertexCount);

            for (var segmentIndex = 0;
                 segmentIndex < segments.Count;
                 segmentIndex++)
            {
                AppendSegment(
                    segments[segmentIndex],
                    segmentIndex,
                    radiusScale,
                    positions,
                    normals,
                    tangents,
                    strandUVs,
                    radiusAndEndpoint,
                    indices);
            }

            var mesh = target != null ? target : new Mesh();
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
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AppendSegment(
            HairStrandSegment segment,
            int segmentIndex,
            float radiusScale,
            List<Vector3> positions,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> strandUVs,
            List<Vector2> radiusAndEndpoint,
            List<int> indices)
        {
            ValidatePoint(segment.Start, segmentIndex, nameof(segment.Start));
            ValidatePoint(segment.End, segmentIndex, nameof(segment.End));

            var segmentVector = segment.End.Position - segment.Start.Position;
            var length = segmentVector.magnitude;
            if (!IsFinite(length) || length <= 1e-7f)
            {
                throw new ArgumentException(
                    $"Hair segment {segmentIndex} has coincident endpoints.",
                    nameof(segment));
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
            var radius0 = segment.Start.Radius
                * radiusScale
                * RadiusCompensation;
            var radius1 = segment.End.Radius
                * radiusScale
                * RadiusCompensation;

            AppendFace(
                segment,
                firstAxis,
                tangent4,
                radius0,
                radius1,
                positions,
                normals,
                tangents,
                strandUVs,
                radiusAndEndpoint,
                indices);
            AppendFace(
                segment,
                secondAxis,
                tangent4,
                radius0,
                radius1,
                positions,
                normals,
                tangents,
                strandUVs,
                radiusAndEndpoint,
                indices);
        }

        private static void AppendFace(
            HairStrandSegment segment,
            Vector3 axis,
            Vector4 tangent,
            float radius0,
            float radius1,
            List<Vector3> positions,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> strandUVs,
            List<Vector2> radiusAndEndpoint,
            List<int> indices)
        {
            var positiveStart = segment.Start.Position + axis * radius0;
            var negativeStart = segment.Start.Position - axis * radius0;
            var positiveEnd = segment.End.Position + axis * radius1;
            var negativeEnd = segment.End.Position - axis * radius1;

            AppendVertex(
                positiveStart,
                axis,
                tangent,
                segment.Start.UV,
                radius0,
                0.0f,
                positions,
                normals,
                tangents,
                strandUVs,
                radiusAndEndpoint,
                indices);
            AppendVertex(
                negativeEnd,
                -axis,
                tangent,
                segment.End.UV,
                radius1,
                1.0f,
                positions,
                normals,
                tangents,
                strandUVs,
                radiusAndEndpoint,
                indices);
            AppendVertex(
                positiveEnd,
                axis,
                tangent,
                segment.End.UV,
                radius1,
                1.0f,
                positions,
                normals,
                tangents,
                strandUVs,
                radiusAndEndpoint,
                indices);

            AppendVertex(
                positiveStart,
                axis,
                tangent,
                segment.Start.UV,
                radius0,
                0.0f,
                positions,
                normals,
                tangents,
                strandUVs,
                radiusAndEndpoint,
                indices);
            AppendVertex(
                negativeStart,
                -axis,
                tangent,
                segment.Start.UV,
                radius0,
                0.0f,
                positions,
                normals,
                tangents,
                strandUVs,
                radiusAndEndpoint,
                indices);
            AppendVertex(
                negativeEnd,
                -axis,
                tangent,
                segment.End.UV,
                radius1,
                1.0f,
                positions,
                normals,
                tangents,
                strandUVs,
                radiusAndEndpoint,
                indices);
        }

        private static void AppendVertex(
            Vector3 position,
            Vector3 normal,
            Vector4 tangent,
            Vector2 uv,
            float radius,
            float endpoint,
            List<Vector3> positions,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> strandUVs,
            List<Vector2> radiusAndEndpoint,
            List<int> indices)
        {
            indices.Add(positions.Count);
            positions.Add(position);
            normals.Add(normal);
            tangents.Add(tangent);
            strandUVs.Add(uv);
            radiusAndEndpoint.Add(new Vector2(radius, endpoint));
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

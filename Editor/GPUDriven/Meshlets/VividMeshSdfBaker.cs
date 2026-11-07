using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Editor.GPUDriven.Meshlets
{
    internal static class VividMeshSdfBaker
    {
        private const int PaddingSamples = 4;
        private const int TrianglesPerLeaf = 4;

        internal static void Bake(
            VividMeshSdfAsset asset,
            Mesh source,
            Mesh geometry,
            float targetVoxelSize,
            int maxAxisSamples,
            VividMeshSdfSignMode signMode,
            float shellThickness)
        {
            if (!math.isfinite(targetVoxelSize) || targetVoxelSize <= 0f
                || maxAxisSamples < 16 || maxAxisSamples > 256
                || !math.isfinite(shellThickness) || shellThickness < 0f
                || !Enum.IsDefined(typeof(VividMeshSdfSignMode), signMode))
                throw new ArgumentException("Invalid Mesh SDF bake settings.");

            Triangle[] triangles = ReadTriangles(geometry, out bool closed, out Bounds bounds);
            if (signMode == VividMeshSdfSignMode.Auto)
                signMode = closed ? VividMeshSdfSignMode.Solid : VividMeshSdfSignMode.ThinShell;
            if (signMode == VividMeshSdfSignMode.Solid && !closed)
                throw new ArgumentException("Solid Mesh SDF requires closed edges with consistent winding. Use Thin Shell or a closed replacement mesh.");

            bool thinShell = signMode == VividMeshSdfSignMode.ThinShell;
            float explicitThickness = thinShell ? shellThickness : 0f;
            bool autoThickness = thinShell && explicitThickness == 0f;
            float3 fieldSize = (float3)bounds.size + explicitThickness;
            // Leave one sample of headroom for rounding the longest interval count up.
            int interiorIntervals = maxAxisSamples - 2 * PaddingSamples - 2 - (autoThickness ? 2 : 0);
            float voxelSize = math.max(targetVoxelSize, math.cmax(fieldSize) / interiorIntervals);
            if (autoThickness)
                fieldSize += 2f * voxelSize;
            int3 dimensions = (int3)math.ceil(fieldSize / voxelSize) + 2 * PaddingSamples + 1;
            float3 sampleExtent = (float3)(dimensions - 1) * voxelSize;
            if (!math.all(math.isfinite(sampleExtent)) || math.length(sampleExtent) > 65504f)
                throw new ArgumentException("Mesh SDF sample bounds exceed the R16 distance range.");

            float3 origin = (float3)bounds.center - 0.5f * sampleExtent;
            float thickness = autoThickness ? 2f * voxelSize : explicitThickness;
            if (thinShell && thickness < 2f * voxelSize)
                throw new ArgumentException($"Shell thickness must span at least two voxels ({2f * voxelSize:G6} local units).");
            if (!math.all(math.isfinite(origin)) || math.any(origin + voxelSize == origin))
                throw new ArgumentException("Mesh SDF sample coordinates exceed the precision of the local grid.");

            var nodes = new List<Node>();
            BuildNode(triangles, nodes, 0, triangles.Length);

            using var nativeTriangles = new NativeArray<Triangle>(triangles, Allocator.TempJob);
            using var nativeNodes = new NativeArray<Node>(nodes.ToArray(), Allocator.TempJob);
            using var samples = new NativeArray<half>(
                dimensions.x * dimensions.y * dimensions.z, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            new BakeJob
            {
                Triangles = nativeTriangles,
                Nodes = nativeNodes,
                Samples = samples,
                Dimensions = dimensions,
                Origin = origin,
                VoxelSize = voxelSize,
                HalfThickness = thickness * 0.5f,
                Solid = !thinShell,
            }.Schedule(samples.Length, 64).Complete();

            var texture = new Texture3D(
                dimensions.x, dimensions.y, dimensions.z, GraphicsFormat.R16_SFloat, TextureCreationFlags.None)
            {
                name = "Distance Field",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            try
            {
                texture.SetPixelData(samples, 0);
                texture.Apply(false, false);
                asset.Initialize(source, texture, bounds, origin, voxelSize, signMode, thickness);
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
        }

        private static Triangle[] ReadTriangles(Mesh mesh, out bool closed, out Bounds bounds)
        {
            using var snapshot = MeshUtility.AcquireReadOnlyMeshData(mesh);
            Mesh.MeshData data = snapshot[0];
            using var vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            data.GetVertices(vertices);
            var triangles = new List<Triangle>();
            var welded = new Dictionary<Vector3, int>();
            var edges = new Dictionary<(int, int), (int count, int winding)>();
            bounds = default;

            for (int submesh = 0; submesh < data.subMeshCount; submesh++)
            {
                var section = data.GetSubMesh(submesh);
                if (section.topology != MeshTopology.Triangles)
                    continue;
                using var indices = new NativeArray<int>(section.indexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                data.GetIndices(indices, submesh, true);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    float3 a = vertices[indices[i]];
                    float3 b = vertices[indices[i + 1]];
                    float3 c = vertices[indices[i + 2]];
                    float3 normal = math.cross(b - a, c - a);
                    float normalSq = math.lengthsq(normal);
                    if (!math.all(math.isfinite(a)) || !math.all(math.isfinite(b)) || !math.all(math.isfinite(c))
                        || !math.isfinite(normalSq))
                        throw new ArgumentException("Mesh SDF requires finite triangle positions and areas.");
                    if (normalSq == 0f)
                        continue;

                    if (triangles.Count == 0)
                        bounds = new Bounds(a, Vector3.zero);
                    bounds.Encapsulate(a);
                    bounds.Encapsulate(b);
                    bounds.Encapsulate(c);
                    triangles.Add(new Triangle { A = a, B = b, C = c, Normal = normal, NormalSq = normalSq });
                    int ia = Weld(a);
                    int ib = Weld(b);
                    int ic = Weld(c);
                    AddEdge(ia, ib);
                    AddEdge(ib, ic);
                    AddEdge(ic, ia);
                }
            }

            if (triangles.Count == 0)
                throw new ArgumentException("Mesh SDF requires at least one non-degenerate triangle.");

            closed = true;
            foreach (var edge in edges.Values)
                closed &= edge.count == 2 && edge.winding == 0;
            return triangles.ToArray();

            int Weld(Vector3 point)
            {
                // Vertex splits for UVs or normals share the same position.
                if (!welded.TryGetValue(point, out int index))
                {
                    index = welded.Count;
                    welded.Add(point, index);
                }
                return index;
            }

            void AddEdge(int a, int b)
            {
                bool ascending = a < b;
                var key = ascending ? (a, b) : (b, a);
                edges.TryGetValue(key, out var edge);
                edges[key] = (edge.count + 1, edge.winding + (ascending ? 1 : -1));
            }
        }

        private struct Triangle
        {
            public float3 A;
            public float3 B;
            public float3 C;
            public float3 Normal;
            public float NormalSq;
        }

        private struct Node
        {
            public float3 Min;
            public float3 Max;
            public int First;
            public int Count;
            public int Escape;
        }

        private static void BuildNode(Triangle[] triangles, List<Node> nodes, int first, int count)
        {
            var node = new Node { Min = float.PositiveInfinity, Max = float.NegativeInfinity };
            float3 centroidMin = float.PositiveInfinity;
            float3 centroidMax = float.NegativeInfinity;
            for (int i = first; i < first + count; i++)
            {
                Triangle t = triangles[i];
                node.Min = math.min(node.Min, math.min(t.A, math.min(t.B, t.C)));
                node.Max = math.max(node.Max, math.max(t.A, math.max(t.B, t.C)));
                float3 centroid = (t.A + t.B + t.C) / 3f;
                centroidMin = math.min(centroidMin, centroid);
                centroidMax = math.max(centroidMax, centroid);
            }

            int nodeIndex = nodes.Count;
            nodes.Add(default);
            if (count <= TrianglesPerLeaf)
            {
                node.First = first;
                node.Count = count;
            }
            else
            {
                float3 extent = centroidMax - centroidMin;
                int axis = extent.x >= extent.y && extent.x >= extent.z ? 0 : extent.y >= extent.z ? 1 : 2;
                Array.Sort(triangles, first, count, Comparer<Triangle>.Create((a, b) =>
                    (a.A[axis] + a.B[axis] + a.C[axis]).CompareTo(b.A[axis] + b.B[axis] + b.C[axis])));
                int leftCount = count / 2;
                BuildNode(triangles, nodes, first, leftCount);
                BuildNode(triangles, nodes, first + leftCount, count - leftCount);
            }
            node.Escape = nodes.Count;
            nodes[nodeIndex] = node;
        }

        [BurstCompile]
        private struct BakeJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Triangle> Triangles;
            [ReadOnly] public NativeArray<Node> Nodes;
            [WriteOnly] public NativeArray<half> Samples;
            public int3 Dimensions;
            public float3 Origin;
            public float VoxelSize;
            public float HalfThickness;
            public bool Solid;

            public void Execute(int index)
            {
                int row = index / Dimensions.x;
                var coord = new int3(index % Dimensions.x, row % Dimensions.y, row / Dimensions.y);
                float3 point = Origin + (float3)coord * VoxelSize;
                float distance = math.sqrt(NearestDistanceSq(point));
                if (Solid && distance > 0f && IsInside(point))
                    distance = -distance;
                Samples[index] = (half)(distance - HalfThickness);
            }

            private float NearestDistanceSq(float3 point)
            {
                float nearest = float.PositiveInfinity;
                for (int i = 0; i < Nodes.Length;)
                {
                    Node node = Nodes[i];
                    float3 delta = math.max(0f, math.max(node.Min - point, point - node.Max));
                    if (math.lengthsq(delta) > nearest)
                    {
                        i = node.Escape;
                        continue;
                    }
                    for (int j = node.First; j < node.First + node.Count; j++)
                        nearest = math.min(nearest, TriangleDistanceSq(point, Triangles[j]));
                    i++;
                }
                return nearest;
            }

            private bool IsInside(float3 point)
            {
                // Independent ray directions handle shared-edge hits without inflating a solid.
                bool a = RayWinding(point, new float3(1f, 0.37139f, 0.12983f));
                bool b = RayWinding(point, new float3(-0.19317f, 1f, 0.47619f));
                return a == b ? a : RayWinding(point, new float3(0.23419f, -0.32917f, 1f));
            }

            private bool RayWinding(float3 point, float3 direction)
            {
                int winding = 0;
                float3 invDir = 1f / direction;
                for (int i = 0; i < Nodes.Length;)
                {
                    Node node = Nodes[i];
                    float3 tMin = (node.Min - point) * invDir;
                    float3 tMax = (node.Max - point) * invDir;
                    if (math.cmin(math.max(tMin, tMax)) < math.max(0f, math.cmax(math.min(tMin, tMax))))
                    {
                        i = node.Escape;
                        continue;
                    }
                    for (int j = node.First; j < node.First + node.Count; j++)
                        winding += RayTriangleWinding(point, direction, Triangles[j]);
                    i++;
                }
                return winding != 0;
            }

            private static int RayTriangleWinding(float3 point, float3 direction, Triangle triangle)
            {
                float3 ab = triangle.B - triangle.A;
                float3 ac = triangle.C - triangle.A;
                float3 p = math.cross(direction, ac);
                float determinant = math.dot(ab, p);
                if (determinant == 0f)
                    return 0;

                float invDeterminant = 1f / determinant;
                float3 s = point - triangle.A;
                float u = math.dot(s, p) * invDeterminant;
                if (u < 0f || u > 1f)
                    return 0;

                float3 q = math.cross(s, ab);
                float v = math.dot(direction, q) * invDeterminant;
                float t = math.dot(ac, q) * invDeterminant;
                if (v < 0f || u + v > 1f || t <= 0f)
                    return 0;
                return determinant > 0f ? 1 : -1;
            }

            private static float TriangleDistanceSq(float3 point, Triangle triangle)
            {
                float3 ap = point - triangle.A;
                float3 bp = point - triangle.B;
                float3 cp = point - triangle.C;
                float3 ab = triangle.B - triangle.A;
                float3 bc = triangle.C - triangle.B;
                float3 ca = triangle.A - triangle.C;
                if (math.dot(math.cross(ab, ap), triangle.Normal) >= 0f
                    && math.dot(math.cross(bc, bp), triangle.Normal) >= 0f
                    && math.dot(math.cross(ca, cp), triangle.Normal) >= 0f)
                {
                    float plane = math.dot(ap, triangle.Normal);
                    return plane * plane / triangle.NormalSq;
                }
                return math.min(SegmentDistanceSq(ap, ab), math.min(SegmentDistanceSq(bp, bc), SegmentDistanceSq(cp, ca)));
            }

            private static float SegmentDistanceSq(float3 offset, float3 edge)
            {
                float t = math.saturate(math.dot(offset, edge) / math.lengthsq(edge));
                return math.lengthsq(offset - t * edge);
            }
        }
    }
}

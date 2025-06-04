using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityEditor.Rendering.Universal
{
/*
MIT License
Copyright (c) 2023 betsukelig
Written by George Betsukeli
https://github.com/betsukelig/Mesh-Edge-Detector/
 */
    public class MeshTriangleNeighbors
    {
        public static Dictionary<Vector2Int, Vector2Int> GetEdgesWithTriangles(Mesh mesh)
        {
            // Get the triangles of the mesh
            int[] triangles = mesh.triangles;

            // Create a dictionary to store the edges and their connected triangles
            Dictionary<Vector2Int, Vector2Int> edgesWithTriangles = new Dictionary<Vector2Int, Vector2Int>();

            // Iterate through the triangles
            for (int i = 0; i < triangles.Length; i += 3)
            {
                // Get the triangle index
                int triangleIndex = i / 3;

                // Get the vertex indexes of the triangle
                int v1 = triangles[i];
                int v2 = triangles[i + 1];
                int v3 = triangles[i + 2];

                // Create the edges of the triangle
                Vector2Int edge1 = new Vector2Int(Mathf.Min(v1, v2), Mathf.Max(v1, v2));
                Vector2Int edge2 = new Vector2Int(Mathf.Min(v2, v3), Mathf.Max(v2, v3));
                Vector2Int edge3 = new Vector2Int(Mathf.Min(v3, v1), Mathf.Max(v3, v1));

                // Add the edges and their connected triangle to the dictionary
                AddEdgeWithTriangle(edgesWithTriangles, edge1, triangleIndex);
                AddEdgeWithTriangle(edgesWithTriangles, edge2, triangleIndex);
                AddEdgeWithTriangle(edgesWithTriangles, edge3, triangleIndex);
            }

            return edgesWithTriangles;
        }

        private static void AddEdgeWithTriangle(Dictionary<Vector2Int, Vector2Int> edgesWithTriangles, Vector2Int edge, int triangleIndex)
        {
            if (edgesWithTriangles.ContainsKey(edge))
            {
                // The edge is already in the dictionary, so add the second connected triangle
                edgesWithTriangles[edge] = new Vector2Int(edgesWithTriangles[edge].x, triangleIndex);
            }
            else
            {
                // The edge is not in the dictionary yet, so add it with its first connected triangle
                edgesWithTriangles.Add(edge, new Vector2Int(triangleIndex, -1));
            }
        }


        public class Vertex
        {
            public Vector3 position;
        }

        public struct Edge
        {
            public Vertex v1;
            public Vertex v2;

            public Edge(Vertex aV1, Vertex aV2)
            {
                // ensure the same order to guarantee equality
                if (aV1.GetHashCode() > aV2.GetHashCode())
                {
                    v1 = aV1;
                    v2 = aV2;
                }
                else
                {
                    v1 = aV2;
                    v2 = aV1;
                }
            }
        }

        public class TrianglePair
        {
            public int t1 = -1;
            public int t2 = -1;

            public bool Add(int aTriangleIndex)
            {
                if (t1 == -1) t1 = aTriangleIndex;
                else if (t2 == -1) t2 = aTriangleIndex;
                else return false;

                return true;
            }
        }

        public class Neighbors
        {
            public int t1 = -1;
            public int t2 = -1;
            public int t3 = -1;
        }

        Dictionary<int, Vertex> verticesLookup = new Dictionary<int, Vertex>();
        Dictionary<Edge, TrianglePair> edges;

        // mesh vertex index as key
        public static List<Vertex> FindSharedVertices(Vector3[] aVertices)
        {
            var list = new List<Vertex>();
            for (int i = 0; i < aVertices.Length; i++)
            {
                Vertex v = null;
                foreach (var item in list)
                {
                    if ((item.position - aVertices[i]).sqrMagnitude < 0.0001f)
                    {
                        v = item;
                        break;
                    }
                }

                if (v == null)
                {
                    v = new Vertex { position = aVertices[i] };
                }

                list.Add(v);
            }

            return list;
        }

        public static Dictionary<Edge, TrianglePair> CreateEdgeList(List<Vertex> aTriangles)
        {
            var res = new Dictionary<Edge, TrianglePair>();
            int count = aTriangles.Count / 3;
            for (int i = 0; i < count; i++)
            {
                Vertex v1 = aTriangles[i * 3];
                Vertex v2 = aTriangles[i * 3 + 1];
                Vertex v3 = aTriangles[i * 3 + 2];

                TrianglePair p;

                Edge e;

                e = new Edge(v1, v2);
                if (!res.TryGetValue(e, out p))
                {
                    p = new TrianglePair();
                    res.Add(e, p);
                }

                p.Add(i);

                e = new Edge(v2, v3);
                if (!res.TryGetValue(e, out p))
                {
                    p = new TrianglePair();
                    res.Add(e, p);
                }

                p.Add(i);

                e = new Edge(v3, v1);
                if (!res.TryGetValue(e, out p))
                {
                    p = new TrianglePair();
                    res.Add(e, p);
                }

                p.Add(i);
            }

            return res;
        }

        public static List<(int, int)> GetNeighbors(Dictionary<Edge, TrianglePair> aEdgeList, List<Vertex> aTriangles)
        {
            var res = new List<(int, int)>();
            int count = aTriangles.Count / 3;

            for (int i = 0; i < count; i++)
            {
                Vertex v1 = aTriangles[i * 3];
                Vertex v2 = aTriangles[i * 3 + 1];
                Vertex v3 = aTriangles[i * 3 + 2];

                TrianglePair p;

                if (aEdgeList.TryGetValue(new Edge(v1, v2), out p)) // if current triangle edge match any edge in dict, get the 2 triangles of that dict 
                {
                    //as neighborindex, add as a neighbor whichever triangle is not the current triangle and i*3 as edge index to get v1 and v2
                    if (p.t1 == i) res.Add((p.t2, i * 3));
                    else res.Add((p.t1, i * 3));
                }
                else res.Add((-1, -1));

                if (aEdgeList.TryGetValue(new Edge(v2, v3), out p))
                {
                    if (p.t1 == i) res.Add((p.t2, i * 3 + 1));
                    else res.Add((p.t1, i * 3 + 1));
                }
                else res.Add((-1, -1));

                if (aEdgeList.TryGetValue(new Edge(v3, v1), out p))
                {
                    if (p.t1 == i) res.Add((p.t2, i * 3 + 2));
                    else res.Add((p.t1, i * 3 + 2));
                }
                else res.Add((-1, -1));
            }

            return res;
        }

        public static List<(int, int)> GetNeighbors(Mesh aMesh)
        {
            var vertexList = FindSharedVertices(aMesh.vertices);
            //Debug.Log("verts " + vertexList.Count.ToString());

            var tris = aMesh.triangles;
            var triangles = new List<Vertex>(tris.Length);
            foreach (var t in tris) triangles.Add(vertexList[t]);
            var edges = CreateEdgeList(triangles);
            return GetNeighbors(edges, triangles);
        }

        public static List<(Vector3, Vector3, Vector3, Vector3)> GetNeighborVectors(Mesh aMesh)
        {
            var res = new List<(Vector3, Vector3, Vector3, Vector3)>();
            var vertexList = FindSharedVertices(aMesh.vertices);
            var tris = aMesh.triangles;
            var triangles = new List<Vertex>(tris.Length);
            foreach (var t in tris) triangles.Add(vertexList[t]);
            var edges = CreateEdgeList(triangles);
            var neighbors = GetNeighbors(edges, triangles);
            foreach (var pair in neighbors)
            {
                int t1 = pair.Item1;
                int t2 = (pair.Item2 / 3) * 3 + (pair.Item2 + 1) % 3;

                if (t1 != -1 && t2 != -1)
                {
                    // Get vertex indices for first triangle
                    int i1 = tris[t1 * 3];
                    int i2 = tris[t1 * 3 + 1];
                    int i3 = tris[t1 * 3 + 2];

                    // Get vertex indices for second triangle
                    if (t2 * 3 > tris.Length - 1)
                        continue;
                    int j1 = tris[t2 * 3];
                    int j2 = tris[t2 * 3 + 1];
                    int j3 = tris[t2 * 3 + 2];

                    // Find shared vertices
                    List<int> sharedIndices = new List<int> { i1, i2, i3 }.Intersect(new List<int> { j1, j2, j3 }).ToList();
                    if (sharedIndices.Count == 2)
                    {
                        Vector3 v1 = aMesh.vertices[sharedIndices[0]];
                        Vector3 v2 = aMesh.vertices[sharedIndices[1]];

                        // Get normals for both triangles
                        Vector3 n1 = aMesh.normals[t1];
                        Vector3 n2 = aMesh.normals[t2];

                        res.Add((v1, v2, n1, n2));
                    }
                }
            }

            return res;
        }
    }
}
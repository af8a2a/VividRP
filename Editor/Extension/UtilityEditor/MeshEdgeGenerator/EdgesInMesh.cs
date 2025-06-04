using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor.Rendering.Universal
{
/*
MIT License
Copyright (c) 2023 betsukelig
Written by George Betsukeli
https://github.com/betsukelig/Mesh-Edge-Detector/
 */
    public class EdgesInMesh
    {
        static float prefabSize = .6f;

        public static float PrefabSize
        {
            get { return prefabSize; }
        }

        public float maxAngleValue = 95.0f;
        static float maxAngle;

        public struct Edge
        {
            public Vector3 Start;
            public Vector3 End;
            public Vector3 Tri1Normal;
            public Vector3 Tri2Normal;
            public Vector3 Tri1Pos;
            public Vector3 Tri2Pos;

            public Edge(Vector3 start, Vector3 end, Vector3 normal1, Vector3 normal2, Vector3 tri1Direction, Vector3 tri2Direction)
            {
                Start = start;
                End = end;
                Tri1Normal = normal1;
                Tri2Normal = normal2;
                Tri1Pos = tri1Direction;
                Tri2Pos = tri2Direction;
            }
        }

        Transform MeshTransform;
        Mesh mesh;
        Vector3[] vertices;
        int[] triangles;

        public EdgesInMesh(Transform meshTransform, Mesh meshobject)
        {
            MeshTransform = meshTransform;
            mesh = meshobject;
        }

        public Edge[] GetEdges(float maxAngle = 95f)
        {
            //mesh = Decimate(mesh, .95f);       
            List<Edge> edges = FindEdgesFast((mesh), maxAngle);
            int edgeDifference = edges.Count;

            //Debug.Log("mesh: " + mesh.name + " vertices " + mesh.vertexCount.ToString() + " triangles " + (mesh.triangles.Length / 3).ToString());
            //Debug.Log("edges " + edges.Count.ToString());

            edges = RemoveDupesInEdgePositions(edges, Mathf.Epsilon);
            edgeDifference -= edges.Count;
            // if (edgeDifference > 0)
            // Debug.Log("Removed " + edgeDifference.ToString() + " edges\n New count: " + edges.Count.ToString());
            edges = new List<Edge>(CombineEdges(edges.ToArray()));
            edgeDifference -= edges.Count;
            // Debug.Log("Combined " + edgeDifference.ToString() + " edges\n New count: " + edges.Count.ToString());
            return edges.ToArray();
        }

        public Edge[] GetBeveledEdges(float maxAngle = 150f, float bevelRadius = .1f) //.2
        {
            List<Edge> edges = FindEdgesFast((mesh), maxAngle);
            int edgeDifference = edges.Count;

            //Debug.Log("mesh: " + mesh.name + " vertices " + mesh.vertexCount.ToString() + " triangles " + (mesh.triangles.Length / 3).ToString());
            //Debug.Log("edges " + edges.Count.ToString());

            edges = RemoveDupesInEdgePositions(edges, Mathf.Epsilon);
            edgeDifference -= edges.Count;
            // if (edgeDifference > 0)
            // Debug.Log("Removed " + edgeDifference.ToString() + " edges\n New count: " + edges.Count.ToString());

            edgeDifference = edges.Count;
            edges = new List<Edge>(CombineEdges(edges.ToArray()));
            edgeDifference -= edges.Count;
            //Debug.Log("Combined " + edgeDifference.ToString() + " edges\n New count: " + edges.Count.ToString());

            edgeDifference = edges.Count;
            edges = CombineBevels(edges, bevelRadius);
            edgeDifference -= edges.Count;
            // Debug.Log("Combined Bevels " + edgeDifference.ToString() + " edges\n New count: " + edges.Count.ToString());
            
            return edges.ToArray();
        }

        class BevelEdge
        {
            public List<int> edgeIndices;
            public List<Edge> edges;
            float proximity;
            Vector3 averagePosSide1; //add up all positions divide by edges.count to get center
            Vector3 averagePosSide2;

            public BevelEdge(int EdgeIndex, Edge firstEdge, float groupProximityRadius)
            {
                edgeIndices = new List<int>();
                edgeIndices.Add(EdgeIndex);
                edges = new List<Edge>();
                edges.Add(firstEdge);
                proximity = groupProximityRadius;
                averagePosSide1 = (firstEdge.Start);
                averagePosSide2 = (firstEdge.End);
            }

            public bool AddEdge(int edgeIndex, Edge edge)
            {
                Vector3 pos1 = averagePosSide1 / edges.Count;
                Vector3 pos2 = averagePosSide2 / edges.Count;
                Vector3 edgeStart = (edge.Start);
                Vector3 edgeEnd = (edge.End);

                bool wasAddedToBevel = false;
                if (Vector3.Distance(pos1, edgeStart) < proximity && Vector3.Distance(pos2, edgeEnd) < proximity)
                {
                    edges.Add(edge);
                    averagePosSide1 += edgeStart;
                    averagePosSide2 += edgeEnd;
                    //Debug.Log("combined!");
                    wasAddedToBevel = true;
                }
                else if (Vector3.Distance(pos2, edgeStart) < proximity &&
                         Vector3.Distance(pos1, edgeEnd) < proximity) //possibly unnecessary check if edges are sorted.
                {
                    //if this happens, sorting flipping the edge position order effects the normals, so fix them
                    edges.Add(edge);
                    averagePosSide1 += edgeEnd;
                    averagePosSide2 += edgeStart;
                    //Debug.Log("combined!2");
                    wasAddedToBevel = true;
                }

                return wasAddedToBevel;
            }

            (Vector3, Vector3, Vector3, Vector3)
                MaxNormalDifferencePair() //return normal and triangleposition of the two edges with the biggest angle difference. 
            {
                // get the two normals with the biggest angle difference
                float smallestAngle = float.MaxValue;
                (Vector3, Vector3, Vector3, Vector3) result = (Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero);
                for (int i = 0; i < edges.Count; i++)
                {
                    float dotProduct0 = Vector3.Dot(edges[i].Tri1Normal, edges[i].Tri2Normal);
                    if (dotProduct0 < smallestAngle)
                    {
                        smallestAngle = (dotProduct0);
                        result.Item1 = edges[i].Tri1Normal;
                        result.Item2 = edges[i].Tri2Normal;
                        result.Item3 = edges[i].Tri1Pos;
                        result.Item4 = edges[i].Tri2Pos;
                    }

                    for (int j = i + 1; j < edges.Count; j++)
                    {
                        float dotProduct2 = Vector3.Dot(edges[i].Tri1Normal, edges[j].Tri2Normal);
                        if (dotProduct2 < smallestAngle)
                        {
                            smallestAngle = (dotProduct2);
                            result.Item1 = edges[i].Tri1Normal;
                            result.Item2 = edges[j].Tri2Normal;
                            result.Item3 = edges[i].Tri1Pos;
                            result.Item4 = edges[j].Tri2Pos;
                        }

                        float dotProduct3 = Vector3.Dot(edges[i].Tri2Normal, edges[j].Tri1Normal);
                        if (dotProduct3 < smallestAngle)
                        {
                            smallestAngle = (dotProduct3);
                            result.Item1 = edges[i].Tri2Normal;
                            result.Item2 = edges[j].Tri1Normal;
                            result.Item3 = edges[i].Tri2Pos;
                            result.Item4 = edges[j].Tri1Pos;
                        }
                    }
                }

                return result;
            }

            public Edge GetAverageEdge()
            {
                (Vector3, Vector3, Vector3, Vector3) NormalPair = MaxNormalDifferencePair();
                //Debug.Log("BevelEdge count " +edges.Count());
                return new Edge(averagePosSide1 / edges.Count, averagePosSide2 / edges.Count, NormalPair.Item1, NormalPair.Item2, NormalPair.Item3,
                    NormalPair.Item4);
            }
        }

        List<Edge> CombineBevels(List<Edge> edgePositions, float bevelRadius) //bevelradius needs to be usd in worldspace
        {
            //get bevelEdges a list of edges that share their 2 vertex positions, 
            List<Edge> NearbyEdges = new List<Edge>();
            List<BevelEdge> BevelEdges = new List<BevelEdge>();
            for (int i = 0; i < edgePositions.Count; i++)
            {
                Vector3 currentEdgeV1 = edgePositions[i].Start;
                Vector3 currentEdgeV2 = edgePositions[i].End;
                bool edgeHasNeighbors = false;
                foreach (BevelEdge bevelEdge in BevelEdges)
                    if (bevelEdge.AddEdge(i, edgePositions[i])) //add edge if its in the bevel group proximity, if it's not in proximity
                        edgeHasNeighbors = true;
                if (!edgeHasNeighbors)
                {
                    BevelEdges.Add(new BevelEdge(i, edgePositions[i], bevelRadius));
                }
            }

            List<Edge> returnEdges = new List<Edge>();
            foreach (BevelEdge bEdge in BevelEdges)
                returnEdges.Add(bEdge.GetAverageEdge());
            //
            return returnEdges;
        }


        private void OnDisable()
        {
        }


        List<Edge> FindEdgesFast(Mesh mesh, float maxAngle = 95f)
        {
            //Object Data Properties > Clear Custom Split Normals Data

            List<Edge> edges = new List<Edge>();
            List<(int neighborIndex, int sharedEdgeIndex)> neighbors = MeshTriangleNeighbors.GetNeighbors(mesh);

            for (int i = 0; i < neighbors.Count; i += 3)
            {
                //each triangle "triangleIndex" in the original mesh has 3 potential neighbors usually. meshes with holes will have triangles with less than 3 neighbor triangle connected.   
                int triangleIndex = i / 3;
                int edgeIndex = neighbors[i].sharedEdgeIndex;
                for (int v = 0; v < 3; v++) //3 at a time 
                {
                    //compare this triangle  "triangleIndex" with the 3 neighbors
                    int neighborTriIndex = neighbors[i + v].neighborIndex; //will return -1 if edge has no neighbor
                    if (neighborTriIndex == -1)
                    {
                        break;
                    }

                    int sharedEdgeIndex = neighbors[i + v].sharedEdgeIndex;

                    //Debug.Log("Triangle " + triangleIndex + " has neighbor " + neighborTriIndex + " with shared edge " + sharedEdgeIndex);


                    if (formsConvexAngle(mesh, triangleIndex, neighborTriIndex, maxAngle))
                    {
                        /*
                        Debug.Log("shared edge index " + sharedEdgeIndex.ToString()+"\n " +
                            "t index " + neighborTriIndex.ToString()+" t2 index "+((sharedEdgeIndex / 3) * 3 + (sharedEdgeIndex + 1) % 3).ToString());
                        Debug.Log("shared edge index " + sharedEdgeIndex.ToString() + "\n " +
                           "t index " + triangleIndex.ToString() + " t2 index " + (sharedEdgeIndex).ToString());
                       */
                        int v1 = mesh.triangles[sharedEdgeIndex];
                        int v2 = mesh.triangles[(sharedEdgeIndex / 3) * 3 + (sharedEdgeIndex + 1) % 3]; //gives next vertex in triangle
                        string debug = ((sharedEdgeIndex / 3) * 3 + (sharedEdgeIndex + 1) % 3).ToString();
                        Vector3 edgePos1 = MeshTransform.TransformPoint(mesh.vertices[v1]);
                        Vector3 edgePos2 = MeshTransform.TransformPoint(mesh.vertices[v2]);
                        Vector3 normal1 = MeshTransform.TransformDirection(triangleNormal(triangleIndex));
                        Vector3 normal2 = MeshTransform.TransformDirection(triangleNormal(neighborTriIndex));
                        Vector3 edgeCenter = (edgePos1 + edgePos2) / 2f;
                        Vector3 t1center = MeshTransform.TransformPoint(triangleCenter(triangleIndex));
                        Vector3 t2center = MeshTransform.TransformPoint(triangleCenter(neighborTriIndex));

                        edges.Add(new Edge(edgePos1, edgePos2, normal1, normal2, t1center, t2center));
                    }
                }
            }

            return edges;
        }

        static List<Edge> RemoveDupesInEdgePositions(List<Edge> edgePositions, float distance = 0.1f)
        {
            for (int i = 0; i < edgePositions.Count; i++)
            {
                Vector3 currentEdgeV1 = edgePositions[i].Start;
                Vector3 currentEdgeV2 = edgePositions[i].End;

                for (int j = i + 1; j < edgePositions.Count; j++)
                {
                    Vector3 nextEdgeV1 = edgePositions[j].Start;
                    Vector3 nextEdgeV2 = edgePositions[j].End;
                    if ((Vector3.Distance(currentEdgeV1, nextEdgeV1) < distance && Vector3.Distance(currentEdgeV2, nextEdgeV2) < distance) ||
                        (Vector3.Distance(currentEdgeV1, nextEdgeV2) < distance && Vector3.Distance(currentEdgeV2, nextEdgeV1) < distance))
                    {
                        //remove douplicate
                        edgePositions.RemoveAt(j);

                        j = j - 1; //j = i + 1;
                    }
                }
            }

            return edgePositions;
        }

        static Edge[] CombineEdges(Edge[] edges)
        {
            List<Edge> combinedEdges = new List<Edge>(edges);
            Edge[] lines = edges;
            List<Edge> combinedLines = new List<Edge>();

            // Iterate over the lines
            for (int i = 0; i < lines.Length; i++)
            {
                Edge line1 = lines[i];

                // Check if the line has already been combined
                if (line1.Start == line1.End)
                {
                    continue;
                }

                // Iterate over the remaining lines
                for (int j = i + 1; j < lines.Length; j++)
                {
                    Edge line2 = lines[j];

                    // Check if the lines share a vertex and are parallel
                    if ((line1.Start == line2.Start || line1.Start == line2.End || line1.End == line2.Start || line1.End == line2.End) &&
                        Vector3.Cross(line1.End - line1.Start, line2.End - line2.Start) == Vector3.zero)
                    {
                        // Combine the lines
                        line1 = new Edge(line1.Start == line2.Start || line1.Start == line2.End ? line1.End : line1.Start,
                            line2.Start == line1.Start || line2.Start == line1.End ? line2.End : line2.Start, line1.Tri1Normal, line2.Tri2Normal, line1.Tri1Pos,
                            line2.Tri2Pos);
                        break;
                    }
                }

                // Add the combined line to the list
                combinedLines.Add(line1);
            }

            return combinedLines.ToArray();
        }

        Vector3 triangleCenter(int triangleIndex) //for debug
        {
            // Get the vertices of the first triangle
            Vector3 v1 = mesh.vertices[mesh.triangles[triangleIndex * 3]];
            Vector3 v2 = mesh.vertices[mesh.triangles[triangleIndex * 3 + 1]];
            Vector3 v3 = mesh.vertices[mesh.triangles[triangleIndex * 3 + 2]];
            float x = (v1.x + v2.x + v3.x) / 3;
            float y = (v1.y + v2.y + v3.y) / 3;
            float z = (v1.z + v2.z + v3.z) / 3;
            return new Vector3(x, y, z);
        }

        Vector3 triangleNormal(int triangleIndex)
        {
            int triangle1VertIndex1 = mesh.triangles[triangleIndex * 3];
            int triangle1VertIndex2 = mesh.triangles[triangleIndex * 3 + 1];
            int triangle1VertIndex3 = mesh.triangles[triangleIndex * 3 + 2];
            // Get the vertices of the first triangle
            Vector3 v1 = mesh.vertices[triangle1VertIndex1];
            Vector3 v2 = mesh.vertices[triangle1VertIndex2];
            Vector3 v3 = mesh.vertices[triangle1VertIndex3];

            // Calculate the normal vector of the first triangle
            return Vector3.Cross(v2 - v1, v3 - v1).normalized;
        }

        bool formsConvexAngle(Mesh mesh, int triangle1Index, int triangle2Index, float maxAngle = 95f)
        {
            float dotProduct = Vector3.Dot(triangleNormal(triangle1Index), triangleNormal(triangle2Index));

            bool isConcave = (IsConcave(triangleCenter(triangle1Index), triangleCenter(triangle2Index), triangleNormal(triangle1Index),
                triangleNormal(triangle2Index)));

            float cosAngle = Mathf.Cos(maxAngle * Mathf.PI / 180f);
            if (dotProduct < -cosAngle && !isConcave)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        static bool IsConcave(Vector3 Am, Vector3 Bm, Vector3 An, Vector3 Bn)
        {
            return Vector3.Dot(Bm - Am, An) > 0 && Vector3.Dot(Am - Bm, Bn) > 0;
        }
    }
}
using System.Collections.Generic;
using Autodesk.Fbx;
using Mikktspace.NET;
using UnityEngine;
using static Unity.Mathematics.math;

namespace UnityEditor.Rendering.Universal
{
    public class TangentUtils
    {
        public static FbxVector4[] GetGeneratorMeshTangents(FbxMesh mesh)
        {
            int polygonCnt = mesh.GetPolygonCount();

            void GetPosition(int polygonInd, int indInPolygon, out float x, out float y, out float z)
            {
                int ctrlInd = mesh.GetPolygonVertex(polygonInd, indInPolygon);
                FbxVector4 pos = mesh.GetControlPointAt(ctrlInd);
                x = (float)pos.X;
                y = (float)pos.Y;
                z = (float)pos.Z;
            }

            void GetNormal(int polygonInd, int indInPolygon, out float x, out float y, out float z)
            {
                int ctrlInd = mesh.GetPolygonVertex(polygonInd, indInPolygon);
                mesh.GetPolygonVertexNormal(polygonInd, indInPolygon, out var normal);
                normal /= normal.Length();
                x = (float)normal.X;
                y = (float)normal.Y;
                z = (float)normal.Z;
            }

            List<List<int>> uvIndexInPolygon = new List<List<int>>();

            FbxLayer layer = mesh.GetLayer(0);
            var elementUV = layer.GetUVs();
            var directArray = elementUV.GetDirectArray();
            int maxPolygonVertCnt = 4;
            if (elementUV.GetReferenceMode() == FbxLayerElement.EReferenceMode.eDirect)
            {
                int uvIndex = 0;
                for (int i = 0; i < polygonCnt; ++i)
                {
                    List<int> uvIndexes = new List<int>();
                    var polygonVertCnt = mesh.GetPolygonSize(i);
                    maxPolygonVertCnt = max(maxPolygonVertCnt, polygonVertCnt);
                    for (int j = 0; j < polygonVertCnt; ++j)
                    {
                        uvIndexes.Add(uvIndex++);
                    }

                    uvIndexInPolygon.Add(uvIndexes);
                }
            }
            else
            {
                int arrayIndex = 0;
                var indexArray = elementUV.GetIndexArray();
                for (int i = 0; i < polygonCnt; ++i)
                {
                    List<int> uvIndexes = new List<int>();
                    var polygonVertCnt = mesh.GetPolygonSize(i);
                    maxPolygonVertCnt = max(maxPolygonVertCnt, polygonVertCnt);
                    for (int j = 0; j < polygonVertCnt; ++j)
                    {
                        var uvIndex = indexArray.GetAt(arrayIndex++);
                        uvIndexes.Add(uvIndex);
                    }
                    uvIndexInPolygon.Add(uvIndexes);
                }
            }

            void GetUV(int polygonInd, int indInPolygon, out float u, out float v)
            {
                
                int ctrlInd = elementUV.GetMappingMode() == FbxLayerElement.EMappingMode.eByControlPoint ? mesh.GetPolygonVertex(polygonInd, indInPolygon) : uvIndexInPolygon[polygonInd][indInPolygon];
                FbxVector2 uv = directArray.GetAt(ctrlInd); // 
                u = (float)uv.X;
                v = (float)uv.Y;
            }

            FbxVector4[,] polygonTangents = new FbxVector4[polygonCnt, maxPolygonVertCnt];

            void SetTangent(int polygonInd, int indInPolygon, float tangentX, float tangentY, float tangentZ,
                float sign)
            {
                polygonTangents[polygonInd, indInPolygon] = new FbxVector4(tangentX, tangentY, tangentZ, sign);
            }

            MikkGenerator.GenerateTangentSpace(polygonCnt, mesh.GetPolygonSize, GetPosition, GetNormal, GetUV,
                SetTangent);

            FbxVector4[] tangents = new FbxVector4[polygonCnt * maxPolygonVertCnt];
            int ind = 0;
            for (int i = 0; i < polygonCnt; ++i)
            {
                if (i >= polygonTangents.Length)
                {
                    Debug.LogWarning(string.Format("{0} is larger than {1}", i, polygonTangents.Length));
                }

                int vertexCnt = mesh.GetPolygonSize(i);
                if (vertexCnt > 4)
                {
                    Debug.LogWarning(string.Format("{0} is larger than {1}", vertexCnt, 4));
                }

                for (int j = 0; j < vertexCnt; ++j)
                {
                    if (ind >= tangents.Length)
                    {
                        Debug.LogWarning(string.Format("{0} is larger than {1}", ind, tangents.Length));
                    }

                    tangents[ind++] = polygonTangents[i, j];
                }
            }

            return tangents;
        }
    }
}
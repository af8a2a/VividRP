using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Autodesk.Fbx;
using UnityEngine;

namespace UnityEditor.Rendering.Universal
{
    public static class SmoothNormalUtil
    {
        [MenuItem("Assets/Write Smooth Normal to UV8", false)]
        public static void WriteSmoothNormal()
        {
            List<GameObject> fbxs = new List<GameObject>();
            List<string> fbxPaths = new List<string>();
            foreach (var guid in Selection.assetGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.ToLower().EndsWith(".fbx"))
                {
                    fbxs.Add(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                }
                else
                {
                    if (AssetDatabase.IsValidFolder(path))
                    {
                        fbxPaths.Add(path);
                    }
                }
            }

            if (fbxPaths.Count > 0)
            {
                var guids = AssetDatabase.FindAssets("t:Model", fbxPaths.ToArray());
                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.ToLower().EndsWith(".fbx"))
                    {
                        fbxs.Add(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                    }
                }
            }

            foreach (var gameObject in fbxs)
            {
                CreateAvgNormalForFBX(gameObject, 7);
                Debug.LogFormat($"Write Smooth Normal to UV8: {gameObject.name}");
            }
        }


        public static void CreateAvgNormalForFBX(GameObject obj, int uvChannel)
        {
            if (!obj)
                return;
            var path = AssetDatabase.GetAssetPath(obj);
            var lowerCasePath = path.ToLower();
            if (!lowerCasePath.EndsWith("fbx"))
                return;
            FbxManager fbxManager = FbxManager.Create();
            FbxIOSettings fbxIOSettings = FbxIOSettings.Create(fbxManager, Globals.IOSROOT);
            fbxManager.SetIOSettings(fbxIOSettings);
            FbxImporter fbxImporter = FbxImporter.Create(fbxManager, "");
            if (!fbxImporter.Initialize(path, -1, fbxIOSettings))
            {
                Debug.Log(fbxImporter.GetStatus().GetErrorString());
                return;
            }

            string name = obj.name;
            FbxScene fbxScene = FbxScene.Create(fbxManager, obj.name);
            fbxImporter.Import(fbxScene);
            fbxImporter.Destroy();
            var rootNode = fbxScene.GetRootNode();

            RecursiveEncodeMesh(rootNode, uvChannel);

            var dir = Path.GetDirectoryName(path);
            string writtenPath = string.Format("{0}/{1}.FBX", dir, name);
            FbxExporter fbxExporter = FbxExporter.Create(fbxManager, "");
            if (!fbxExporter.Initialize(writtenPath, -1, fbxIOSettings))
            {
                Debug.Log(fbxExporter.GetStatus().GetErrorString());
                return;
            }

            fbxExporter.Export(fbxScene);
            fbxExporter.Destroy();
            fbxManager.Destroy();
            AssetDatabase.Refresh();
        }


        struct VertexInfo
        {
            public int vertexIndex;
            public FbxVector4 normal;
            public double weight;
        }
        
        private static void RecursiveEncodeMesh(FbxNode node, int uvChannel)
        {
            if (node == null)
                return;
            SmoothMeshNormals(node, uvChannel);
            var nodeCnt = node.GetChildCount();
            for (int i = 0; i < nodeCnt; ++i)
            {
                RecursiveEncodeMesh(node.GetChild(i), uvChannel);
            }
        }

        #region GetMeshTangents

        static FbxVector4[] GetMeshTangents(FbxMesh mesh)
        {
            var tangents = TangentUtils.GetGeneratorMeshTangents(mesh);
            return tangents;
        }

        #endregion

        #region GetPolygonNormal

        static FbxVector4 GetPolygonNormal(FbxVector4 p0, FbxVector4 p1, FbxVector4 p2)
        {
            FbxVector4 p01 = p1 - p0;
            FbxVector4 p12 = p2 - p1;
            FbxVector4 norm = p01.CrossProduct(p12);
            norm /= Math.Max(norm.Length(), 0.001);
            return norm;
        }

        #endregion

        #region UnitVectorToOctahedron

        static FbxVector2 UnitVectorToOctahedron(FbxVector4 n)
        {
            FbxVector4 one = new FbxVector4(1.0f, 1.0f, 1.0f, 1.0f);
            double l = one.DotProduct(n.Abs());
            FbxVector2 tmp = new FbxVector2(n.X, n.Y);
            tmp /= l;
            FbxVector2 ret = tmp;
            if (n.Z <= 0.0f)
            {
                ret.X = (1.0f - Math.Abs(tmp.Y)) * (tmp.X > 0.0f ? 1.0f : -1.0f);
                ret.Y = (1.0f - Math.Abs(tmp.X)) * (tmp.Y > 0.0f ? 1.0f : -1.0f);
            }

            ret *= 0.5f;
            ret.X += 0.5f;
            ret.Y += 0.5f;
            return ret;
        }

        #endregion

        #region SmoothMeshNormals

        static void SmoothMeshNormals(FbxNode node, int uvChannel)
        {
            var nodeAttribute = node.GetNodeAttribute();
            if (nodeAttribute == null)
                return;
            if (nodeAttribute.GetAttributeType() != FbxNodeAttribute.EType.eMesh)
                return;

            var mesh = node.GetMesh();

            int ctrlPointCnt = mesh.GetControlPointsCount();
            List<List<VertexInfo>> ctrlPointInfo = new List<List<VertexInfo>>();
            for (int ind = 0; ind < ctrlPointCnt; ++ind)
            {
                ctrlPointInfo.Add(new List<VertexInfo>());
            }

            FbxVector4[] meshTangents = GetMeshTangents(mesh);

            int vertexIndex = 0;
            for (int polygonIndex = 0; polygonIndex < mesh.GetPolygonCount(); ++polygonIndex)
            {
                int vertexCntInPolygon = mesh.GetPolygonSize(polygonIndex);
                for (int vertexIndInPolygon = 0; vertexIndInPolygon < vertexCntInPolygon; ++vertexIndInPolygon)
                {
                    int prevIndex = (vertexIndInPolygon + vertexCntInPolygon - 1) % vertexCntInPolygon;
                    int nextIndex = (vertexIndInPolygon + 1) % vertexCntInPolygon;

                    int ctrlPointInd = mesh.GetPolygonVertex(polygonIndex, vertexIndInPolygon);
                    int prevPointInd = mesh.GetPolygonVertex(polygonIndex, prevIndex);
                    int nextPointInd = mesh.GetPolygonVertex(polygonIndex, nextIndex);

                    FbxVector4 ctrlPoint = mesh.GetControlPointAt(ctrlPointInd);
                    FbxVector4 prevPoint = mesh.GetControlPointAt(prevPointInd);
                    FbxVector4 nextPoint = mesh.GetControlPointAt(nextPointInd);

                    mesh.GetPolygonVertexNormal(polygonIndex, vertexIndInPolygon, out var normal);
                    normal /= normal.Length();
                    FbxVector4 e0 = prevPoint - ctrlPoint;
                    FbxVector4 e1 = nextPoint - ctrlPoint;

                    e0 = e0 / e0.Length();
                    e1 = e1 / e1.Length();

                    double weight = Math.Acos(e0.DotProduct(e1));
                    List<VertexInfo> ctrlVertexInfo = ctrlPointInfo[ctrlPointInd];
                    ctrlVertexInfo.Add(new VertexInfo
                    {
                        vertexIndex = vertexIndex++,
                        normal = normal,
                        weight = weight
                    });
                }
            }

            int layerCount = mesh.GetLayerCount();
            for (int ind = 0; ind < uvChannel - layerCount + 1; ++ind)
            {
                int tmpInd = mesh.CreateLayer();
                var tmpLayer = mesh.GetLayer(tmpInd);
                tmpLayer.SetUVs(FbxLayerElementUV.Create(mesh, ""));
            }

            var layer = mesh.GetLayer(uvChannel);
            var elementUV = layer.GetUVs();
            elementUV.SetMappingMode(FbxLayerElement.EMappingMode.eByPolygonVertex);
            elementUV.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
            var uvDirectArray = elementUV.GetDirectArray();
            uvDirectArray.SetCount(vertexIndex);


            for (int ctrlInd = 0; ctrlInd < ctrlPointCnt; ++ctrlInd)
            {
                List<VertexInfo> vertexInfos = ctrlPointInfo[ctrlInd];
                FbxVector4 smoothNormal = new FbxVector4();
                foreach (VertexInfo vertexInfo in vertexInfos)
                {
                    smoothNormal += vertexInfo.weight * vertexInfo.normal;
                }

                smoothNormal /= smoothNormal.Length();

                foreach (VertexInfo vertexInfo in vertexInfos)
                {
                    FbxVector4 tangent = meshTangents[vertexInfo.vertexIndex];
                    var normal = vertexInfo.normal;
                    FbxVector4 bitangent = normal.CrossProduct(tangent) * tangent.W;
                    double normalTSX = smoothNormal.DotProduct(tangent);
                    double normalTSY = smoothNormal.DotProduct(bitangent);
                    double normalTSZ = smoothNormal.DotProduct(normal);
                    FbxVector4 normalTS = new FbxVector4(normalTSX, normalTSY, normalTSZ);
                    normalTS /= normalTS.Length();
                    uvDirectArray.SetAt(vertexInfo.vertexIndex, UnitVectorToOctahedron(normalTS));
                }
            }
        }


        #endregion
    }
}
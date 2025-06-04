using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityEditor.Rendering.Universal
{
/*
MIT License
Copyright (c) 2023 betsukelig
Written by George Betsukeli
https://github.com/betsukelig/Mesh-Edge-Detector/
 */

#if UNITY_EDITOR
    public class AddEdgesToMeshWindow : EditorWindow
    {
        [MenuItem("GioG/FPPS/AddEdges")]
        public static void AddEdgesAddCollider()
        {
            foreach (GameObject selectedObject in Selection.gameObjects)
                AddEdgesToMesh.AddEdgesAddCollider(selectedObject, 100f);
        }

        [MenuItem("GioG/FPPS/AddEdgesWideAngle")]
        public static void AddEdgesWideAngle()
        {
            foreach (GameObject selectedObject in Selection.gameObjects)
                AddEdgesToMesh.AddEdgesAddCollider(selectedObject, 175f);
        }

        [MenuItem("GioG/FPPS/AddEdgesBeveled")]
        public static void AddEdgesBeveled()
        {
            foreach (GameObject selectedObject in Selection.gameObjects)
                AddEdgesToMesh.AddEdgesAddCollider(selectedObject, 175f, true);
        }

        /*
        [MenuItem("GioG/FPPS/DisableAllEdges")]
        public static void DisableAllEdges()
        {
            EdgeManager edgeManager = EdgeManager.GetEdgeManager();
            edgeManager.CleanUp();
            foreach (Transform pointtransforms in edgeManager.GetEdges())
                pointtransforms.gameObject.SetActive(false);
        }*/
        [MenuItem("GioG/FPPS/TopDownEdgesOnly")]
        public static void IsolateTopDownEdges()
        {
            GameObject[] selectedObjects = Selection.gameObjects;

            foreach (GameObject selectedObject in selectedObjects)
            {
                AddEdgesToMesh.TopDownEdgesOnly(selectedObject.transform);
            }
        }

        [MenuItem("GioG/FPPS/RemoveEdges")]
        public static void RemoveEdges()
        {
            GameObject[] selectedObjects = Selection.gameObjects;

            foreach (GameObject selectedObject in selectedObjects)
            {
                AddEdgesToMesh.RemoveEdges(selectedObject.transform);
            }
        }
    }
#endif
    public class AddEdgesToMesh
    {
        public delegate void AddedEdgesToMesh(Transform objectTransform, Transform[] edgeTransforms);

        public static AddedEdgesToMesh OnFinishAddingEdges;

        public delegate void RemovedEdgesToMesh(Transform objectTransform);

        public static RemovedEdgesToMesh OnRemovedEdges;
        //recompilation nullifies these delegates, so assign your functions to them every reload
        //heres an example:
        /*
    #if UNITY_EDITOR
        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            AddEdgesToMesh.OnFinishAddingEdges = yourfunction;
            AddEdgesToMesh.OnRemovedEdges = yourfunction;
        }
    #endif*/


        static string pointPrefabPath = "Assets/Point.prefab";

        public static void AddEdgesAddCollider(GameObject selectedObject, float angle = 100f, bool beveled = false)
        {
            MeshRenderer meshRenderer = selectedObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
                return;
            MeshCollider meshCollider = meshRenderer.gameObject.GetComponent<MeshCollider>();

            if (meshCollider == null)
                meshCollider = meshRenderer.gameObject.AddComponent<MeshCollider>();
            EdgesInMesh edgesInMesh = new EdgesInMesh(selectedObject.transform, meshCollider.sharedMesh);


            if (meshRenderer.gameObject.name == "Point")
                return;
            if (beveled)
                AddEdges(meshRenderer.transform, edgesInMesh.GetBeveledEdges(angle, .2f));
            else
                AddEdges(meshRenderer.transform, edgesInMesh.GetEdges(angle));
        }

        static void AddEdges(Transform transform, EdgesInMesh.Edge[] edges)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pointPrefabPath);
            if (prefab == null)
                prefab = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            float prefabsize = EdgesInMesh.PrefabSize;
            float offsethalf = prefabsize / 2f;
            List<Transform> edgeTransforms = new List<Transform>();

            foreach (EdgesInMesh.Edge edge in edges)
            {
                Vector3 startPos = edge.Start; // transform.TransformPoint();
                Vector3 endPos = edge.End; //transform.TransformPoint();
                Vector3 pointPosition = edge.Start;
                Vector3 edgeNormal = (edge.End - edge.Start).normalized;
                Vector3 tri1Dir = Vector3.Cross(edge.Tri1Normal, (edgeNormal)); //get direction towards the triangle perpendicular to edge normal. 
                Vector3 tri2Dir = Vector3.Cross(edge.Tri2Normal, (edgeNormal));
                Quaternion side1Rotation = Quaternion.LookRotation((tri1Dir), edge.Tri1Normal);
                Quaternion side2Rotation = Quaternion.LookRotation((tri2Dir), edge.Tri2Normal);
                int maxcount = (int)(Vector3.Distance(startPos, endPos) / prefabsize);
                for (int i = 0; i < maxcount; i++)
                {
                    pointPosition = Vector3.Lerp(startPos, endPos, (float)i / (float)maxcount + (0.5f / (float)maxcount));

                    Transform pointSide1Transform = ((GameObject)PrefabUtility.InstantiatePrefab(prefab)).transform;
                    pointSide1Transform.position = pointPosition;
                    pointSide1Transform.rotation = side1Rotation;

                    if (IsClockwise(edge.Start, edge.End, edge.Tri1Pos, edge.Tri1Normal, true))
                    {
                        pointSide1Transform.Rotate(Vector3.up, 180f, Space.Self);
                    }

                    pointSide1Transform.SetParent(transform);


                    Transform pointSide2Transform = ((GameObject)PrefabUtility.InstantiatePrefab(prefab)).transform;

                    pointSide2Transform.position = pointPosition; //rotate 90 around y
                    pointSide2Transform.rotation = side2Rotation;

                    if (IsClockwise(edge.Start, edge.End, edge.Tri2Pos, edge.Tri2Normal, true))
                    {
                        pointSide2Transform.Rotate(Vector3.up, 180f, Space.Self);
                    }

                    pointSide2Transform.SetParent(transform);


                    edgeTransforms.Add(pointSide1Transform);
                    edgeTransforms.Add(pointSide2Transform);
                    //pointSide1Transform.gameObject.AddComponent<ClimbPoint>();
                    //pointSide2Transform.gameObject.AddComponent<ClimbPoint>();
                }
            }

            if (OnFinishAddingEdges != null)
                OnFinishAddingEdges(transform, edgeTransforms.ToArray());
        }

        public static void RemoveEdges(Transform transform)
        {
            for (int t = transform.childCount - 1; t > -1; t--)
            {
                string goname = transform.GetChild(t).gameObject.name;
                if (goname == "Point")
                {
                    GameObject.DestroyImmediate(transform.GetChild(t).gameObject);
                }
            }

            if (OnRemovedEdges != null)
                OnRemovedEdges(transform);
        }

        public static void TopDownEdgesOnly(Transform transform)
        {
            //remove non horizontal points. isolate edges that are facing top down
            for (int t = transform.childCount - 1; t > -1; t--)
            {
                Transform child = transform.GetChild(t);
                string goname = child.gameObject.name;
                if (goname == "Point")
                {
                    if (Vector3.Dot(child.up, Vector3.up) < 0.5f)
                    {
                        GameObject.DestroyImmediate(child.gameObject);
                    }
                }
            }
        }

        static bool IsClockwise(Vector3 v1, Vector3 v2, Vector3 pivot, Vector3 dir, bool clockwise)
        {
            Vector3 crossProduct = Vector3.Cross(v1 - pivot, v2 - pivot);
            float dotProduct = Vector3.Dot(crossProduct, dir);
            if (dotProduct > 0)
                return !clockwise;
            else
                return clockwise;
        }
    }
}
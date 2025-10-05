using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CustomEditor(typeof(NSFluidPlane))]
    public class NSFluidPlaneEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            var fluidPlane = (NSFluidPlane)target;
            GUILayout.Label("Test");
            if (GUILayout.Button("Add Interactor"))
            {
                var go = new GameObject("Interactor" + fluidPlane.interactorsCount);
                go.transform.SetPositionAndRotation(fluidPlane.transform.position, fluidPlane.transform.rotation);
                var interactor = go.AddComponent<FluidInteractor>();
                fluidPlane.RegisterInteractor(interactor);
            }
            

        }

        void OnSceneGUI() {
            var fluid2d = (NSFluidPlane)target;
            var areaExtents = fluid2d.areaSize * 0.5f;
            //var rect = new Rect(-areaSize * 0.5f, areaSize);

            using (new Handles.DrawingScope(Color.yellow, fluid2d.transform.localToWorldMatrix)) {
                Vector3 LB = new Vector3(-areaExtents.x, 0, -areaExtents.y);
                Vector3 LT = new Vector3(-areaExtents.x, 0,  areaExtents.y);
                Vector3 RT = new Vector3( areaExtents.x, 0,  areaExtents.y);
                Vector3 RB = new Vector3( areaExtents.x, 0, -areaExtents.y);
                Handles.DrawLine(LB, LT);
                Handles.DrawLine(LT, RT);
                Handles.DrawLine(RT, RB);
                Handles.DrawLine(RB, LB);
            }
        }

    }
}
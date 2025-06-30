using UnityEngine;

namespace UnityEngine.Rendering.Universal
{
    [ExecuteAlways]
    public class ScreenSpacePlanarReflectionPlane : MonoBehaviour
    {
        internal delegate void ScreenSpacePlanarReflectionPlaneAction(ScreenSpacePlanarReflectionPlane plane);


        public bool IsValid => plandeRenderer != null;
        [HideInInspector] public Renderer plandeRenderer;

        private void OnEnable()
        {
            plandeRenderer = GetComponent<Renderer>();

            var instance = PlaneManager.instance;

            instance.PlaneAdd(this);
        }

        void OnDisable()
        {
            var instance = PlaneManager.instance;

            instance.PlaneRemove(this);
        }
    }
}
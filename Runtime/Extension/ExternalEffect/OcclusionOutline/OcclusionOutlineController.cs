using System;
using UnityEngine;

namespace UnityEngine.Rendering.Universal
{
    [ExecuteInEditMode]
    public class OcclusionOutlineController : MonoBehaviour
    {
       [Range(-1,10)] public float Intensity = 0;
       public Color OutlineColor = Color.white;
        // Update is called once per frame
        void Update()
        {
            OcclusionOutlineDrawSystem.Instance.Register(this);
        }


        private void OnEnable()
        {
            OcclusionOutlineDrawSystem.Instance.Register(this);
        }

        private void OnDisable()
        {
            OcclusionOutlineDrawSystem.Instance.Unregister(this);
        }

    }
}
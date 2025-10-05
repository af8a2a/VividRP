using System;

namespace UnityEngine.Rendering.Universal
{
    public class FluidInteractor : MonoBehaviour
    {
        private Vector3 m_PreviousPosition;
        private Vector3 m_CurrentPosition;

        [Min(0.01f)] public float radius = 0.5f;
        [Min(0f)] public float forceScale = 100f;


        public Vector4 interactParameter => new Vector4(transform.position.x, transform.position.y, transform.position.z, radius);

        public Vector3 PreviousPosition => m_PreviousPosition;
        public Vector3 CurrentPosition => m_CurrentPosition;

        private void OnEnable()
        {
            m_PreviousPosition = transform.position;
            m_CurrentPosition = transform.position;
        }

        private void Update()
        {
            m_PreviousPosition = m_CurrentPosition;
            m_CurrentPosition = transform.position;
        }
    }
}
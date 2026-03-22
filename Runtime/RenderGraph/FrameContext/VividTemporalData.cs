using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public class VividTemporalData : ContextItem
    {
        public Matrix4x4 previousViewProjectionMatrix;
        public Matrix4x4 nonJitteredViewProjectionMatrix;
        public Matrix4x4 previousViewMatrix;
        public Matrix4x4 previousProjectionMatrix;
        public Vector2 jitter;
        public Vector2 previousJitter;
        public bool isFirstFrame;

        public override void Reset()
        {
            previousViewProjectionMatrix = Matrix4x4.identity;
            nonJitteredViewProjectionMatrix = Matrix4x4.identity;
            previousViewMatrix = Matrix4x4.identity;
            previousProjectionMatrix = Matrix4x4.identity;
            jitter = Vector2.zero;
            previousJitter = Vector2.zero;
            isFirstFrame = true;
        }
    }
}

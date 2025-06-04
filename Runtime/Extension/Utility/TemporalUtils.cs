using System;
using Features.Filter.TemporalDenoiser;
using Unity.Mathematics;
using UnityEngine;
using Random = System.Random;

namespace UnityEngine.Rendering.Universal
{
    public static class TemporalUtils
    {
        const int k_SampleCount = 8;

        public static int sampleIndex { get; private set; }

        public static Vector2 GenerateRandomOffset()
        {
            // The variance between 0 and the actual halton sequence values reveals noticeable instability
            // in Unity's shadow maps, so we avoid index 0.
            var offset = new Vector2(
                HaltonSeq.Get((sampleIndex & 1023) + 1, 2) - 0.5f,
                HaltonSeq.Get((sampleIndex & 1023) + 1, 3) - 0.5f
            );

            if (++sampleIndex >= k_SampleCount)
                sampleIndex = 0;

            return offset;
        }


        public static Vector4[] HBAOJitter()
        {
            var jitter = new Vector4[16];
            var rand = new System.Random();
            
            float numDir = 8; // keep in sync to glsl
            
            for (int i = 0; i < 16; i++)
            {
                var rand1 =(float)rand.NextDouble() ;
                var rand2 =(float)rand.NextDouble() ;
                float angle = math.PI2 * rand1 / numDir;
                jitter[i].x = math.cos(angle);
                jitter[i].y = math.sin(angle);
                jitter[i].z = rand2;
                jitter[i].w = 0;
            }

            return jitter;
        }


        /// <summary>
        /// Gets a jittered orthographic projection matrix for a given camera.
        /// </summary>
        /// <param name="camera">The camera to build the orthographic matrix for</param>
        /// <param name="offset">The jitter offset</param>
        /// <returns>A jittered projection matrix</returns>
        public static Matrix4x4 GetJitteredOrthographicProjectionMatrix(Camera camera, Vector2 offset)
        {
            float vertical = camera.orthographicSize;
            float horizontal = vertical * camera.aspect;

            offset.x *= horizontal / (0.5f * camera.pixelWidth);
            offset.y *= vertical / (0.5f * camera.pixelHeight);

            float left = offset.x - horizontal;
            float right = offset.x + horizontal;
            float top = offset.y + vertical;
            float bottom = offset.y - vertical;

            return Matrix4x4.Ortho(left, right, bottom, top, camera.nearClipPlane, camera.farClipPlane);
        }

        /// <summary>
        /// Gets a jittered perspective projection matrix for a given camera.
        /// </summary>
        /// <param name="camera">The camera to build the projection matrix for</param>
        /// <param name="offset">The jitter offset</param>
        /// <returns>A jittered projection matrix</returns>
        public static Matrix4x4 GetJitteredPerspectiveProjectionMatrix(Camera camera, Vector2 offset)
        {
            float near = camera.nearClipPlane;
            float far = camera.farClipPlane;

            float vertical = Mathf.Tan(0.5f * Mathf.Deg2Rad * camera.fieldOfView) * near;
            float horizontal = vertical * camera.aspect;

            offset.x *= horizontal / (0.5f * camera.pixelWidth);
            offset.y *= vertical / (0.5f * camera.pixelHeight);

            var matrix = camera.projectionMatrix;

            matrix[0, 2] += offset.x / horizontal;
            matrix[1, 2] += offset.y / vertical;

            return matrix;
        }
    }
}
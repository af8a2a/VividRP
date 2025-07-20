using Unity.Mathematics;
using UnityEngine;

namespace UnityEngine.Rendering.Universal
{
    public static class RenderingUtilsExt
    {
        /// <summary>
        /// Divides one value by another and rounds up to the next integer.
        /// This is often used to calculate dispatch dimensions for compute shaders.
        /// </summary>
        /// <param name="value">The value to divide.</param>
        /// <param name="divisor">The value to divide by.</param>
        /// <returns>The value divided by the divisor rounded up to the next integer.</returns>
        public static int DivRoundUp(int value, int divisor)
        {
            return (value + (divisor - 1)) / divisor;
        }


        public static float ComputeViewportScale(int viewportSize, int bufferSize)
        {
            float rcpBufferSize = 1.0f / bufferSize;

            // Scale by (vp_dim / buf_dim).
            return viewportSize * rcpBufferSize;
        }

        public static float ComputeViewportLimit(int viewportSize, int bufferSize)
        {
            float rcpBufferSize = 1.0f / bufferSize;

            // Clamp to (vp_dim - 0.5) / buf_dim.
            return (viewportSize - 0.5f) * rcpBufferSize;
        }

        public static Vector4 ComputeViewportScaleAndLimit(Vector2Int viewportSize, Vector2Int bufferSize)
        {
            return new Vector4(ComputeViewportScale(viewportSize.x, bufferSize.x), // Scale(x)
                ComputeViewportScale(viewportSize.y, bufferSize.y), // Scale(y)
                ComputeViewportLimit(viewportSize.x, bufferSize.x), // Limit(x)
                ComputeViewportLimit(viewportSize.y, bufferSize.y)); // Limit(y)
        }


        public static int RoundUpToPowerOfTwo(int arg)
        {
            return 1 << math.ceillog2(arg);
        }

        public static int CalcMipCount(Vector2Int textureSize)
        {
            int maxLength = Mathf.Max(textureSize.x, textureSize.y);
            return (int)Mathf.Log(maxLength, 2);
        }
        
        
        
                internal static Matrix4x4 ComputePixelCoordToWorldSpaceViewDirectionMatrix(float verticalFoV, Vector2 lensShift, Vector4 screenSize, Matrix4x4 worldToViewMatrix, bool renderToCubemap, float aspectRatio = -1, bool isOrthographic = false)
        {
            Matrix4x4 viewSpaceRasterTransform;

            if (isOrthographic)
            {
                // For ortho cameras, project the skybox with no perspective
                // the same way as builtin does (case 1264647)
                viewSpaceRasterTransform = new Matrix4x4(
                    new Vector4(-2.0f * screenSize.z, 0.0f, 0.0f, 0.0f),
                    new Vector4(0.0f, -2.0f * screenSize.w, 0.0f, 0.0f),
                    new Vector4(1.0f, 1.0f, -1.0f, 0.0f),
                    new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            }
            else
            {
                // Compose the view space version first.
                // V = -(X, Y, Z), s.t. Z = 1,
                // X = (2x / resX - 1) * tan(vFoV / 2) * ar = x * [(2 / resX) * tan(vFoV / 2) * ar] + [-tan(vFoV / 2) * ar] = x * [-m00] + [-m20]
                // Y = (2y / resY - 1) * tan(vFoV / 2)      = y * [(2 / resY) * tan(vFoV / 2)]      + [-tan(vFoV / 2)]      = y * [-m11] + [-m21]

                aspectRatio = aspectRatio < 0 ? screenSize.x * screenSize.w : aspectRatio;
                float tanHalfVertFoV = Mathf.Tan(0.5f * verticalFoV);

                // Compose the matrix.
                float m21 = (1.0f - 2.0f * lensShift.y) * tanHalfVertFoV;
                float m11 = -2.0f * screenSize.w * tanHalfVertFoV;

                float m20 = (1.0f - 2.0f * lensShift.x) * tanHalfVertFoV * aspectRatio;
                float m00 = -2.0f * screenSize.z * tanHalfVertFoV * aspectRatio;

                if (renderToCubemap)
                {
                    // Flip Y.
                    m11 = -m11;
                    m21 = -m21;
                }

                viewSpaceRasterTransform = new Matrix4x4(new Vector4(m00, 0.0f, 0.0f, 0.0f),
                    new Vector4(0.0f, m11, 0.0f, 0.0f),
                    new Vector4(m20, m21, -1.0f, 0.0f),
                    new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            }

            // Remove the translation component.
            var homogeneousZero = new Vector4(0, 0, 0, 1);
            worldToViewMatrix.SetColumn(3, homogeneousZero);

            // Flip the Z to make the coordinate system left-handed.
            worldToViewMatrix.SetRow(2, -worldToViewMatrix.GetRow(2));

            // Transpose for HLSL.
            return Matrix4x4.Transpose(worldToViewMatrix.transpose * viewSpaceRasterTransform);
        }

        /// <summary>
        /// Compute the matrix from screen space (pixel) to world space direction (RHS).
        /// 
        /// You can use this matrix on the GPU to compute the direction to look in a cubemap for a specific
        /// screen pixel.
        /// </summary>
        /// <param name="camera"></param>
        /// <param name="viewMatrix"></param>
        /// <param name="gpuProjMatrix"></param>
        /// <param name="resolution"></param>
        /// <param name="aspect">
        /// The aspect ratio to use.
        ///
        /// if negative, then the aspect ratio of <paramref name="resolution"/> will be used.
        ///
        /// It is different from the aspect ratio of <paramref name="resolution"/> for anamorphic projections.
        /// </param>
        /// <returns></returns>
        internal static Matrix4x4 ComputePixelCoordToWorldSpaceViewDirectionMatrix(Camera camera, Matrix4x4 viewMatrix, Matrix4x4 gpuProjMatrix, Vector4 resolution, float aspect = -1)
        {
            //// In XR mode, or if explicitely required, use a more generic matrix to account for asymmetry in the projection
            //var useGenericMatrix = xr.enabled || frameSettings.IsEnabled(FrameSettingsField.AsymmetricProjection);

            // Asymmetry is also possible from a user-provided projection, so we must check for it too.
            // Note however, that in case of physical camera, the lens shift term is the only source of
            // asymmetry, and this is accounted for in the optimized path below. Additionally, Unity C++ will
            // automatically disable physical camera when the projection is overridden by user.
            //useGenericMatrix |= HDUtils.IsProjectionMatrixAsymmetric(viewConstants.projMatrix) && !camera.usePhysicalProperties;

            //if (useGenericMatrix)
            //{
            //    var viewSpaceRasterTransform = new Matrix4x4(
            //        new Vector4(2.0f * resolution.z, 0.0f, 0.0f, -1.0f),
            //        new Vector4(0.0f, -2.0f * resolution.w, 0.0f, 1.0f),
            //        new Vector4(0.0f, 0.0f, 1.0f, 0.0f),
            //        new Vector4(0.0f, 0.0f, 0.0f, 1.0f));

            //    var transformT = viewConstants.invViewProjMatrix.transpose * Matrix4x4.Scale(new Vector3(-1.0f, -1.0f, -1.0f));
            //    return viewSpaceRasterTransform * transformT;
            //}

            float verticalFoV = camera.GetGateFittedFieldOfView() * Mathf.Deg2Rad;
            if (!camera.usePhysicalProperties)
            {
                verticalFoV = Mathf.Atan(-1.0f / gpuProjMatrix[1, 1]) * 2;
            }
            Vector2 lensShift = camera.GetGateFittedLensShift();

            return ComputePixelCoordToWorldSpaceViewDirectionMatrix(verticalFoV, lensShift, resolution, viewMatrix, false, aspect, camera.orthographic);
        }
        
        
        /// <summary>Get the aspect ratio of a projection matrix.</summary>
        /// <param name="matrix"></param>
        /// <returns></returns>
        internal static float ProjectionMatrixAspect(in Matrix4x4 matrix)
            => -matrix.m11 / matrix.m00;


        public static float GetPixelSpreadTangent(float fov, int width, int height)
        {
            return Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f) * 2.0f / Mathf.Min(width, height);
        }

        public static float GetPixelSpreadAngle(float fov, int width, int height)
        {
            return Mathf.Atan(GetPixelSpreadTangent(fov, width, height));
        }
        
        
        internal static Vector4 EvaluateRayTracingHistorySizeAndScale( RTHandle buffer)
        {
            return new Vector4(RTHandles.rtHandleProperties.previousViewportSize.x,
                RTHandles.rtHandleProperties.previousViewportSize.y,
                (float)RTHandles.rtHandleProperties.previousViewportSize.x / buffer.rt.width,
                (float)RTHandles.rtHandleProperties.previousViewportSize.y / buffer.rt.height);
        }


        
        static private UniversalAdditionalCameraData s_DefaultUniversalAdditionalCameraData { get { return ComponentSingleton<UniversalAdditionalCameraData>.instance; } }

        internal static UniversalAdditionalCameraData TryGetAdditionalCameraDataOrDefault(Camera camera)
        {
            if (camera == null || camera.Equals(null))
                return s_DefaultUniversalAdditionalCameraData;

            if (camera.TryGetComponent<UniversalAdditionalCameraData>(out var cameraData))
            {
                
                return cameraData;
            }

            return camera.gameObject.AddComponent<UniversalAdditionalCameraData>();

        }
        
        public static float3 ExtractDirection(Matrix4x4 localToWorldMatrix) =>
            -((float4) localToWorldMatrix.GetColumn(2)).xyz;

    }
}
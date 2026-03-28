using System.Runtime.InteropServices;
using UnityEngine;

namespace VividRP.Runtime
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ShaderVariablesGlobal
    {
        public static readonly int ConstantBufferShaderId = Shader.PropertyToID("ShaderVariablesGlobal");

        public Vector4 _VividTime;
        public Vector4 _VividSinTime;
        public Vector4 _VividCosTime;
        public Vector4 _VividDeltaTime;
        public Vector4 _VividTimeParameters;
        public Vector4 _VividLastTimeParameters;
        public Vector4 _VividWorldSpaceCameraPos;
        public Vector4 _VividProjectionParams;
        public Vector4 _VividScreenParams;
        public Vector4 _VividZBufferParams;
        public Vector4 _VividOrthoParams;
        public Vector4 _VividScaleBias;
        public Vector4 _VividScaleBiasRt;
        public Vector4 _VividRTHandleScale;
        public Matrix4x4 _VividCameraProjection;
        public Matrix4x4 _VividCameraInvProjection;
        public Matrix4x4 _VividWorldToCamera;
        public Matrix4x4 _VividCameraToWorld;
        public Matrix4x4 _VividGlstateMatrixProjection;
        public Matrix4x4 _VividMatrixV;
        public Matrix4x4 _VividMatrixInvV;
        public Matrix4x4 _VividMatrixInvP;
        public Matrix4x4 _VividMatrixVP;
        public Matrix4x4 _VividMatrixInvVP;
        public Matrix4x4 _VividPrevViewProjMatrix;
        public Matrix4x4 _VividNonJitteredViewProjMatrix;
        public Matrix4x4 _VividViewProjMatrix;
        public Matrix4x4 _VividViewMatrix;
        public Matrix4x4 _VividProjMatrix;
        public Matrix4x4 _VividInvViewProjMatrix;
        public Matrix4x4 _VividInvViewMatrix;
        public Matrix4x4 _VividInvProjMatrix;
        public Vector4 _VividInvProjParam;
        public Vector4 _VividScreenSize;
        public Vector4 _VividGlobalMipBias;
        public Vector4 _VividScaledScreenParams;
        public Matrix4x4 _VividPrevViewMatrix;
        public Matrix4x4 _VividPrevProjMatrix;
        public Vector4 _VividJitterParams;
        public Vector4 _VividLightModelAmbient;
        public Vector4 _VividAmbientSky;
        public Vector4 _VividAmbientEquator;
        public Vector4 _VividAmbientGround;
        public Vector4 _VividIndirectSpecColor;
        public Vector4 _VividFogParams;
        public Vector4 _VividFogColor;
        public Vector4 _VividShadowColor;

        public static ShaderVariablesGlobal Create(
            VividCameraData.ShaderVariables shaderVariables,
            CameraTemporalData temporalData)
        {
            var currentTime = Time.timeSinceLevelLoad;
            var deltaTime = Mathf.Max(Time.deltaTime, 1e-6f);
            var smoothDeltaTime = Mathf.Max(Time.smoothDeltaTime > 0.0f ? Time.smoothDeltaTime : deltaTime, 1e-6f);
            var previousTime = currentTime - deltaTime;

            return new ShaderVariablesGlobal
            {
                _VividTime = CreateTimeVector(currentTime),
                _VividSinTime = CreateSinTimeVector(currentTime),
                _VividCosTime = CreateCosTimeVector(currentTime),
                _VividDeltaTime = new Vector4(deltaTime, 1.0f / deltaTime, smoothDeltaTime, 1.0f / smoothDeltaTime),
                _VividTimeParameters = new Vector4(currentTime, Mathf.Sin(currentTime), Mathf.Cos(currentTime), 0.0f),
                _VividLastTimeParameters = new Vector4(previousTime, Mathf.Sin(previousTime), Mathf.Cos(previousTime), 0.0f),
                _VividWorldSpaceCameraPos = shaderVariables.worldSpaceCameraPos,
                _VividProjectionParams = shaderVariables.projectionParams,
                _VividScreenParams = shaderVariables.screenParams,
                _VividZBufferParams = shaderVariables.zBufferParams,
                _VividOrthoParams = shaderVariables.orthoParams,
                _VividScaleBias = shaderVariables.scaleBias,
                _VividScaleBiasRt = shaderVariables.scaleBiasRt,
                _VividRTHandleScale = shaderVariables.rtHandleScale,
                _VividCameraProjection = shaderVariables.cameraProjection,
                _VividCameraInvProjection = shaderVariables.cameraInvProjection,
                _VividWorldToCamera = shaderVariables.worldToCamera,
                _VividCameraToWorld = shaderVariables.cameraToWorld,
                _VividGlstateMatrixProjection = shaderVariables.glstateMatrixProjection,
                _VividMatrixV = shaderVariables.matrixV,
                _VividMatrixInvV = shaderVariables.matrixInvV,
                _VividMatrixInvP = shaderVariables.matrixInvP,
                _VividMatrixVP = shaderVariables.matrixVP,
                _VividMatrixInvVP = shaderVariables.matrixInvVP,
                _VividPrevViewProjMatrix = shaderVariables.prevViewProjMatrix,
                _VividNonJitteredViewProjMatrix = shaderVariables.nonJitteredViewProjMatrix,
                _VividViewProjMatrix = shaderVariables.viewProjMatrix,
                _VividViewMatrix = shaderVariables.viewMatrix,
                _VividProjMatrix = shaderVariables.projMatrix,
                _VividInvViewProjMatrix = shaderVariables.invViewProjMatrix,
                _VividInvViewMatrix = shaderVariables.invViewMatrix,
                _VividInvProjMatrix = shaderVariables.invProjMatrix,
                _VividInvProjParam = shaderVariables.invProjParam,
                _VividScreenSize = shaderVariables.screenSize,
                _VividGlobalMipBias = new Vector4(shaderVariables.globalMipBias.x, shaderVariables.globalMipBias.y, 0.0f, 0.0f),
                _VividScaledScreenParams = shaderVariables.scaledScreenParams,
                _VividPrevViewMatrix = temporalData != null ? temporalData.PreviousViewMatrix : shaderVariables.matrixV,
                _VividPrevProjMatrix = temporalData != null ? temporalData.PreviousProjectionMatrix : shaderVariables.projMatrix,
                _VividJitterParams = temporalData != null
                    ? new Vector4(
                        temporalData.Jitter.x,
                        temporalData.Jitter.y,
                        temporalData.PreviousJitter.x,
                        temporalData.PreviousJitter.y)
                    : Vector4.zero,
                _VividLightModelAmbient = ToVector4(RenderSettings.ambientLight),
                _VividAmbientSky = ToVector4(RenderSettings.ambientSkyColor),
                _VividAmbientEquator = ToVector4(RenderSettings.ambientEquatorColor),
                _VividAmbientGround = ToVector4(RenderSettings.ambientGroundColor),
                _VividIndirectSpecColor = CreateIndirectSpecColor(),
                _VividFogParams = CreateFogParams(),
                _VividFogColor = ToVector4(RenderSettings.fogColor),
                _VividShadowColor = ToVector4(RenderSettings.subtractiveShadowColor),
            };
        }

        private static Vector4 CreateTimeVector(float timeValue)
        {
            return new Vector4(timeValue * 0.05f, timeValue, timeValue * 2.0f, timeValue * 3.0f);
        }

        private static Vector4 CreateSinTimeVector(float timeValue)
        {
            return new Vector4(
                Mathf.Sin(timeValue * 0.125f),
                Mathf.Sin(timeValue * 0.25f),
                Mathf.Sin(timeValue * 0.5f),
                Mathf.Sin(timeValue));
        }

        private static Vector4 CreateCosTimeVector(float timeValue)
        {
            return new Vector4(
                Mathf.Cos(timeValue * 0.125f),
                Mathf.Cos(timeValue * 0.25f),
                Mathf.Cos(timeValue * 0.5f),
                Mathf.Cos(timeValue));
        }

        private static Vector4 CreateIndirectSpecColor()
        {
            var intensity = Mathf.Max(RenderSettings.reflectionIntensity, 0.0f);
            return new Vector4(intensity, intensity, intensity, 1.0f);
        }

        private static Vector4 CreateFogParams()
        {
            if (!RenderSettings.fog)
                return Vector4.zero;

            var density = Mathf.Max(RenderSettings.fogDensity, 0.0f);
            var linearStart = RenderSettings.fogStartDistance;
            var linearEnd = Mathf.Max(RenderSettings.fogEndDistance, linearStart + 0.0001f);
            var linearRange = linearEnd - linearStart;
            const float sqrtLn2 = 0.832554611f;
            const float ln2 = 0.693147181f;

            return new Vector4(
                density / sqrtLn2,
                density / ln2,
                -1.0f / linearRange,
                linearEnd / linearRange);
        }

        private static Vector4 ToVector4(Color color)
        {
            return new Vector4(color.r, color.g, color.b, color.a);
        }
    }
}

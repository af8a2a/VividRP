using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class FrameContextSystem : CameraRelativeSystem<CameraTemporalData>
    {
        private static readonly FrameContextSystem s_Instance = new();

        // Temporal shader IDs
        private static readonly int PrevViewProjMatrixId = Shader.PropertyToID("_PrevViewProjMatrix");
        private static readonly int NonJitteredViewProjMatrixId = Shader.PropertyToID("_NonJitteredViewProjMatrix");
        private static readonly int PrevViewMatrixId = Shader.PropertyToID("_PrevViewMatrix");
        private static readonly int PrevProjMatrixId = Shader.PropertyToID("_PrevProjMatrix");
        private static readonly int JitterParamsId = Shader.PropertyToID("_JitterParams");

        // Camera shader IDs
        private static readonly int WorldSpaceCameraPosId = Shader.PropertyToID("_WorldSpaceCameraPos");
        private static readonly int ProjectionParamsId = Shader.PropertyToID("_ProjectionParams");
        private static readonly int ScreenParamsId = Shader.PropertyToID("_ScreenParams");
        private static readonly int ZBufferParamsId = Shader.PropertyToID("_ZBufferParams");
        private static readonly int OrthoParamsId = Shader.PropertyToID("unity_OrthoParams");
        private static readonly int ScaleBiasId = Shader.PropertyToID("_ScaleBias");
        private static readonly int ScaleBiasRtId = Shader.PropertyToID("_ScaleBiasRt");
        private static readonly int RtHandleScaleId = Shader.PropertyToID("_RTHandleScale");
        private static readonly int CameraWorldClipPlanesId = Shader.PropertyToID("unity_CameraWorldClipPlanes");
        private static readonly int CameraProjectionId = Shader.PropertyToID("unity_CameraProjection");
        private static readonly int CameraInvProjectionId = Shader.PropertyToID("unity_CameraInvProjection");
        private static readonly int WorldToCameraId = Shader.PropertyToID("unity_WorldToCamera");
        private static readonly int CameraToWorldId = Shader.PropertyToID("unity_CameraToWorld");
        private static readonly int GlstateMatrixProjectionId = Shader.PropertyToID("glstate_matrix_projection");
        private static readonly int MatrixVId = Shader.PropertyToID("unity_MatrixV");
        private static readonly int MatrixInvVId = Shader.PropertyToID("unity_MatrixInvV");
        private static readonly int MatrixInvPId = Shader.PropertyToID("unity_MatrixInvP");
        private static readonly int MatrixVPId = Shader.PropertyToID("unity_MatrixVP");
        private static readonly int MatrixInvVPId = Shader.PropertyToID("unity_MatrixInvVP");
        private static readonly int ViewProjMatrixId = Shader.PropertyToID("_ViewProjMatrix");
        private static readonly int ViewMatrixId = Shader.PropertyToID("_ViewMatrix");
        private static readonly int ProjMatrixId = Shader.PropertyToID("_ProjMatrix");
        private static readonly int InvViewProjMatrixId = Shader.PropertyToID("_InvViewProjMatrix");
        private static readonly int InvViewMatrixId = Shader.PropertyToID("_InvViewMatrix");
        private static readonly int InvProjMatrixId = Shader.PropertyToID("_InvProjMatrix");
        private static readonly int InvProjParamId = Shader.PropertyToID("_InvProjParam");
        private static readonly int ScreenSizeId = Shader.PropertyToID("_ScreenSize");
        private static readonly int FrustumPlanesId = Shader.PropertyToID("_FrustumPlanes");
        private static readonly int GlobalMipBiasId = Shader.PropertyToID("_GlobalMipBias");
        private static readonly int ScaledScreenParamsId = Shader.PropertyToID("_ScaledScreenParams");

        public static void Update(ContextContainer frameData, CommandBuffer cmd)
        {
            s_Instance.PurgeDestroyedCameras();

            var cameraData = frameData.Get<VividCameraData>();
            var temporalData = GetOrCreate(cameraData.camera);

            // 1. Advance temporal state
            temporalData.Update(cameraData);

            // 2. Populate ContextItem for passes
            var vividTemporalData = frameData.GetOrCreate<VividTemporalData>();
            vividTemporalData.previousViewProjectionMatrix = temporalData.PreviousViewProjection;
            vividTemporalData.nonJitteredViewProjectionMatrix = temporalData.ViewProjection;
            vividTemporalData.previousViewMatrix = temporalData.PreviousViewMatrix;
            vividTemporalData.previousProjectionMatrix = temporalData.PreviousProjectionMatrix;
            vividTemporalData.jitter = temporalData.Jitter;
            vividTemporalData.previousJitter = temporalData.PreviousJitter;
            vividTemporalData.isFirstFrame = temporalData.IsFirstFrame;

            // 3. Build and set all shader globals in one place
            var shaderVariables = cameraData.BuildShaderVariables(temporalData);
            SetShaderGlobals(cmd, shaderVariables, temporalData);
        }

        public new static CameraTemporalData GetOrCreate(Camera camera)
        {
            if (camera == null)
                return null;

            return s_Instance.GetOrCreateBase(camera);
        }

        public static void Clear()
        {
            s_Instance.Dispose();
        }

        private static void SetShaderGlobals(CommandBuffer cmd, VividCameraData.ShaderVariables sv,
            CameraTemporalData temporalData)
        {
            // Vectors
            cmd.SetGlobalVector(WorldSpaceCameraPosId, sv.worldSpaceCameraPos);
            cmd.SetGlobalVector(ProjectionParamsId, sv.projectionParams);
            cmd.SetGlobalVector(ScreenParamsId, sv.screenParams);
            cmd.SetGlobalVector(ZBufferParamsId, sv.zBufferParams);
            cmd.SetGlobalVector(OrthoParamsId, sv.orthoParams);
            cmd.SetGlobalVector(ScaleBiasId, sv.scaleBias);
            cmd.SetGlobalVector(ScaleBiasRtId, sv.scaleBiasRt);
            cmd.SetGlobalVector(RtHandleScaleId, sv.rtHandleScale);
            cmd.SetGlobalVector(GlobalMipBiasId, new Vector4(sv.globalMipBias.x, sv.globalMipBias.y, 0.0f, 0.0f));
            cmd.SetGlobalVector(ScaledScreenParamsId, sv.scaledScreenParams);
            cmd.SetGlobalVector(ScreenSizeId, sv.screenSize);
            cmd.SetGlobalVector(InvProjParamId, sv.invProjParam);

            // Matrices — camera
            cmd.SetGlobalMatrix(CameraProjectionId, sv.cameraProjection);
            cmd.SetGlobalMatrix(CameraInvProjectionId, sv.cameraInvProjection);
            cmd.SetGlobalMatrix(WorldToCameraId, sv.worldToCamera);
            cmd.SetGlobalMatrix(CameraToWorldId, sv.cameraToWorld);
            cmd.SetGlobalMatrix(GlstateMatrixProjectionId, sv.glstateMatrixProjection);
            cmd.SetGlobalMatrix(MatrixVId, sv.matrixV);
            cmd.SetGlobalMatrix(MatrixInvVId, sv.matrixInvV);
            cmd.SetGlobalMatrix(MatrixInvPId, sv.matrixInvP);
            cmd.SetGlobalMatrix(MatrixVPId, sv.matrixVP);
            cmd.SetGlobalMatrix(MatrixInvVPId, sv.matrixInvVP);
            cmd.SetGlobalMatrix(ViewProjMatrixId, sv.viewProjMatrix);
            cmd.SetGlobalMatrix(ViewMatrixId, sv.viewMatrix);
            cmd.SetGlobalMatrix(ProjMatrixId, sv.projMatrix);
            cmd.SetGlobalMatrix(InvViewProjMatrixId, sv.invViewProjMatrix);
            cmd.SetGlobalMatrix(InvViewMatrixId, sv.invViewMatrix);
            cmd.SetGlobalMatrix(InvProjMatrixId, sv.invProjMatrix);

            // Matrices — temporal
            cmd.SetGlobalMatrix(PrevViewProjMatrixId, sv.prevViewProjMatrix);
            cmd.SetGlobalMatrix(NonJitteredViewProjMatrixId, sv.nonJitteredViewProjMatrix);
            cmd.SetGlobalMatrix(PrevViewMatrixId, temporalData != null ? temporalData.PreviousViewMatrix : sv.matrixV);
            cmd.SetGlobalMatrix(PrevProjMatrixId,
                temporalData != null ? temporalData.PreviousProjectionMatrix : sv.projMatrix);

            // Jitter
            cmd.SetGlobalVector(JitterParamsId, temporalData != null
                ? new Vector4(temporalData.Jitter.x, temporalData.Jitter.y,
                    temporalData.PreviousJitter.x, temporalData.PreviousJitter.y)
                : Vector4.zero);

            // Frustum planes
            cmd.SetGlobalVectorArray(CameraWorldClipPlanesId, sv.cameraWorldClipPlanes);
            cmd.SetGlobalVectorArray(FrustumPlanesId, sv.frustumPlanes);
        }
    }
}
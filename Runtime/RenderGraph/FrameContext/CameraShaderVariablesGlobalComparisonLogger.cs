using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VividRP.Runtime
{
    internal static class CameraShaderVariablesGlobalComparisonLogger
    {
        private const float SnapshotRecencySeconds = 1.0f;
        private const float ScalarTolerance = 0.001f;
        private const int MaxReportedDifferences = 10;

        internal struct Snapshot
        {
            public EntityId cameraInstanceId;
            public string cameraName;
            public CameraType cameraType;
            public float captureTime;
            public Rect pixelRect;
            public int actualWidth;
            public int actualHeight;
            public int pixelWidth;
            public int pixelHeight;
            public int scaledPixelWidth;
            public int scaledPixelHeight;
            public float nearClipPlane;
            public float farClipPlane;
            public float fieldOfView;
            public float aspect;
            public bool orthographic;
            public float orthographicSize;
            public bool hasTargetTexture;
            public bool renderIntoTexture;
            public bool hasAdditionalData;
            public Matrix4x4 rawCameraViewMatrix;
            public Matrix4x4 rawCameraProjectionMatrix;
            public Matrix4x4 rawCameraNonJitteredProjectionMatrix;
            public Matrix4x4 effectiveViewMatrix;
            public Matrix4x4 effectiveProjectionMatrix;
            public Matrix4x4 effectiveNonJitteredProjectionMatrix;
            public Matrix4x4 effectiveJitterMatrix;
            public ShaderVariablesGlobal shaderVariablesGlobal;

            public static Snapshot Create(VividCameraData cameraData, ShaderVariablesGlobal shaderVariablesGlobal, float captureTime)
            {
                var camera = cameraData.camera;
                var additionalData = cameraData.additionalData;
                return new Snapshot
                {
                    cameraInstanceId = camera != null ? camera.GetEntityId() : EntityId.None,
                    cameraName = camera != null ? camera.name : "<null>",
                    cameraType = camera != null ? camera.cameraType : default,
                    captureTime = captureTime,
                    pixelRect = cameraData.pixelRect,
                    actualWidth = cameraData.actualWidth,
                    actualHeight = cameraData.actualHeight,
                    pixelWidth = cameraData.pixelWidth,
                    pixelHeight = cameraData.pixelHeight,
                    scaledPixelWidth = camera != null ? camera.scaledPixelWidth : 0,
                    scaledPixelHeight = camera != null ? camera.scaledPixelHeight : 0,
                    nearClipPlane = camera != null ? camera.nearClipPlane : 0.0f,
                    farClipPlane = camera != null ? camera.farClipPlane : 0.0f,
                    fieldOfView = camera != null ? camera.fieldOfView : 0.0f,
                    aspect = camera != null ? camera.aspect : 0.0f,
                    orthographic = camera != null && camera.orthographic,
                    orthographicSize = camera != null ? camera.orthographicSize : 0.0f,
                    hasTargetTexture = camera != null && camera.targetTexture != null,
                    renderIntoTexture = camera != null && (camera.targetTexture != null || camera.cameraType == CameraType.SceneView),
                    hasAdditionalData = additionalData != null,
                    rawCameraViewMatrix = camera != null ? camera.worldToCameraMatrix : Matrix4x4.identity,
                    rawCameraProjectionMatrix = camera != null ? camera.projectionMatrix : Matrix4x4.identity,
                    rawCameraNonJitteredProjectionMatrix = camera != null ? camera.nonJitteredProjectionMatrix : Matrix4x4.identity,
                    effectiveViewMatrix = cameraData.GetViewMatrix(),
                    effectiveProjectionMatrix = cameraData.GetProjectionMatrix(),
                    effectiveNonJitteredProjectionMatrix = cameraData.GetProjectionMatrixNoJitter(),
                    effectiveJitterMatrix = cameraData.GetJitterMatrix(),
                    shaderVariablesGlobal = shaderVariablesGlobal,
                };
            }
        }

        private static Snapshot s_LastSceneViewSnapshot;
        private static Snapshot s_LastGameSnapshot;
        private static bool s_HasSceneViewSnapshot;
        private static bool s_HasGameSnapshot;
        private static string s_LastMismatchSignature;
        private static float s_LastMismatchTime;

        internal static void Reset()
        {
            s_LastSceneViewSnapshot = default;
            s_LastGameSnapshot = default;
            s_HasSceneViewSnapshot = false;
            s_HasGameSnapshot = false;
            s_LastMismatchSignature = null;
            s_LastMismatchTime = 0.0f;
        }

        [Conditional("VIVIDRP_DEBUG")]
        internal static void CaptureAndCompare(VividCameraData cameraData, ShaderVariablesGlobal shaderVariablesGlobal)
        {
            var camera = cameraData.camera;
            if (camera == null)
            {
                return;
            }

            if (camera.cameraType != CameraType.SceneView && camera.cameraType != CameraType.Game)
            {
                return;
            }

            var snapshot = Snapshot.Create(cameraData, shaderVariablesGlobal, Time.realtimeSinceStartup);

            if (camera.cameraType == CameraType.SceneView)
            {
                s_LastSceneViewSnapshot = snapshot;
                s_HasSceneViewSnapshot = true;
            }
            else
            {
                s_LastGameSnapshot = snapshot;
                s_HasGameSnapshot = true;
            }

            if (!s_HasSceneViewSnapshot || !s_HasGameSnapshot)
            {
                return;
            }

            if (Mathf.Abs(s_LastSceneViewSnapshot.captureTime - s_LastGameSnapshot.captureTime) > SnapshotRecencySeconds)
            {
                return;
            }

            if (!TryBuildDifferenceReport(s_LastSceneViewSnapshot, s_LastGameSnapshot, out var report, out var signature))
            {
                s_LastMismatchSignature = null;
                return;
            }

            if (signature == s_LastMismatchSignature && Time.realtimeSinceStartup - s_LastMismatchTime < SnapshotRecencySeconds)
            {
                return;
            }

            s_LastMismatchSignature = signature;
            s_LastMismatchTime = Time.realtimeSinceStartup;
            Debug.LogWarning(report, camera);
        }

        internal static bool TryBuildDifferenceReport(Snapshot sceneSnapshot, Snapshot gameSnapshot, out string report,
            out string signature)
        {
            var details = new StringBuilder();
            var signatureBuilder = new StringBuilder();
            var differenceCount = 0;
            var omittedDifferenceCount = 0;
            AppendScalarDifference("nearClipPlane", sceneSnapshot.nearClipPlane, gameSnapshot.nearClipPlane, ScalarTolerance,
                details, signatureBuilder, ref differenceCount, ref omittedDifferenceCount);
            AppendScalarDifference("farClipPlane", sceneSnapshot.farClipPlane, gameSnapshot.farClipPlane, ScalarTolerance,
                details, signatureBuilder, ref differenceCount, ref omittedDifferenceCount);
            AppendBoolDifference("orthographic", sceneSnapshot.orthographic, gameSnapshot.orthographic, details,
                signatureBuilder, ref differenceCount, ref omittedDifferenceCount);
            if (!sceneSnapshot.orthographic && !gameSnapshot.orthographic)
            {
                AppendScalarDifference("fieldOfView", sceneSnapshot.fieldOfView, gameSnapshot.fieldOfView, ScalarTolerance,
                    details, signatureBuilder, ref differenceCount, ref omittedDifferenceCount);
            }
            else
            {
                AppendScalarDifference("orthographicSize", sceneSnapshot.orthographicSize, gameSnapshot.orthographicSize,
                    ScalarTolerance, details, signatureBuilder, ref differenceCount, ref omittedDifferenceCount);
            }

            var sceneGlobal = sceneSnapshot.shaderVariablesGlobal;
            var gameGlobal = gameSnapshot.shaderVariablesGlobal;
            AppendVectorDifference("_VividWorldSpaceCameraPos", sceneGlobal._VividWorldSpaceCameraPos,
                gameGlobal._VividWorldSpaceCameraPos, ScalarTolerance, details, signatureBuilder, ref differenceCount,
                ref omittedDifferenceCount);
            AppendVectorDifference("_VividProjectionParams", sceneGlobal._VividProjectionParams, gameGlobal._VividProjectionParams,
                ScalarTolerance, details, signatureBuilder, ref differenceCount, ref omittedDifferenceCount);
            AppendVectorDifference("_VividZBufferParams", sceneGlobal._VividZBufferParams, gameGlobal._VividZBufferParams,
                ScalarTolerance, details, signatureBuilder, ref differenceCount, ref omittedDifferenceCount);
            AppendVectorDifference("_VividOrthoParams", sceneGlobal._VividOrthoParams, gameGlobal._VividOrthoParams,
                ScalarTolerance, details, signatureBuilder, ref differenceCount, ref omittedDifferenceCount);
            AppendVectorDifference("_VividRTHandleScale", sceneGlobal._VividRTHandleScale, gameGlobal._VividRTHandleScale,
                ScalarTolerance, details, signatureBuilder, ref differenceCount, ref omittedDifferenceCount);
            AppendVectorDifference("_VividPlanetCenterRadius", sceneGlobal._VividPlanetCenterRadius, gameGlobal._VividPlanetCenterRadius,
                ScalarTolerance, details, signatureBuilder, ref differenceCount, ref omittedDifferenceCount);
            AppendVectorDifference("_VividPlanetUpAltitude", sceneGlobal._VividPlanetUpAltitude, gameGlobal._VividPlanetUpAltitude,
                ScalarTolerance, details, signatureBuilder, ref differenceCount, ref omittedDifferenceCount);
            AppendMatrixDifference("_VividWorldToCamera", sceneGlobal._VividWorldToCamera, gameGlobal._VividWorldToCamera,
                ScalarTolerance, details, signatureBuilder, ref differenceCount, ref omittedDifferenceCount);
            AppendMatrixDifference("_VividCameraToWorld", sceneGlobal._VividCameraToWorld, gameGlobal._VividCameraToWorld,
                ScalarTolerance, details, signatureBuilder, ref differenceCount, ref omittedDifferenceCount);
            AppendMatrixDifference("_VividGlstateMatrixProjection", sceneGlobal._VividGlstateMatrixProjection,
                gameGlobal._VividGlstateMatrixProjection, ScalarTolerance, details, signatureBuilder, ref differenceCount,
                ref omittedDifferenceCount);
            AppendMatrixDifference("_VividViewProjMatrix", sceneGlobal._VividViewProjMatrix, gameGlobal._VividViewProjMatrix,
                ScalarTolerance, details, signatureBuilder, ref differenceCount, ref omittedDifferenceCount);
            AppendMatrixDifference("_VividNonJitteredViewProjMatrix", sceneGlobal._VividNonJitteredViewProjMatrix,
                gameGlobal._VividNonJitteredViewProjMatrix, ScalarTolerance, details, signatureBuilder, ref differenceCount,
                ref omittedDifferenceCount);
            AppendMatrixDifference("_VividInvViewProjMatrix", sceneGlobal._VividInvViewProjMatrix,
                gameGlobal._VividInvViewProjMatrix, ScalarTolerance, details, signatureBuilder, ref differenceCount,
                ref omittedDifferenceCount);
            AppendVectorDifference("_VividInvProjParam", sceneGlobal._VividInvProjParam, gameGlobal._VividInvProjParam,
                ScalarTolerance, details, signatureBuilder, ref differenceCount, ref omittedDifferenceCount);
            
            if (differenceCount == 0)
            {
                report = null;
                signature = null;
                return false;
            }

            if (omittedDifferenceCount > 0)
            {
                details.AppendLine($"- ... {omittedDifferenceCount} more differing fields omitted");
            }

            report =
                $"[VividRP][CameraGlobalsMismatch] SceneView='{sceneSnapshot.cameraName}' Game='{gameSnapshot.cameraName}'" +
                $"\nScene raw: rect={FormatRect(sceneSnapshot.pixelRect)} actual={sceneSnapshot.actualWidth}x{sceneSnapshot.actualHeight} pixel={sceneSnapshot.pixelWidth}x{sceneSnapshot.pixelHeight} scaled={sceneSnapshot.scaledPixelWidth}x{sceneSnapshot.scaledPixelHeight} near={sceneSnapshot.nearClipPlane:F4} far={sceneSnapshot.farClipPlane:F4} fov={sceneSnapshot.fieldOfView:F4} aspect={sceneSnapshot.aspect:F6} ortho={sceneSnapshot.orthographic} orthoSize={sceneSnapshot.orthographicSize:F4} targetTexture={sceneSnapshot.hasTargetTexture} renderIntoTexture={sceneSnapshot.renderIntoTexture}" +
                $"\nGame raw:  rect={FormatRect(gameSnapshot.pixelRect)} actual={gameSnapshot.actualWidth}x{gameSnapshot.actualHeight} pixel={gameSnapshot.pixelWidth}x{gameSnapshot.pixelHeight} scaled={gameSnapshot.scaledPixelWidth}x{gameSnapshot.scaledPixelHeight} near={gameSnapshot.nearClipPlane:F4} far={gameSnapshot.farClipPlane:F4} fov={gameSnapshot.fieldOfView:F4} aspect={gameSnapshot.aspect:F6} ortho={gameSnapshot.orthographic} orthoSize={gameSnapshot.orthographicSize:F4} targetTexture={gameSnapshot.hasTargetTexture} renderIntoTexture={gameSnapshot.renderIntoTexture}" +
                $"\nScene source: additionalData={sceneSnapshot.hasAdditionalData} rawProjIdentity={LooksLikeIdentity(sceneSnapshot.rawCameraProjectionMatrix)} rawNonJitteredIdentity={LooksLikeIdentity(sceneSnapshot.rawCameraNonJitteredProjectionMatrix)} effectiveProjIdentity={LooksLikeIdentity(sceneSnapshot.effectiveProjectionMatrix)} effectiveNonJitteredIdentity={LooksLikeIdentity(sceneSnapshot.effectiveNonJitteredProjectionMatrix)} rawVsEffectiveProjMaxDiff={MaxAbsDiff(sceneSnapshot.rawCameraProjectionMatrix, sceneSnapshot.effectiveProjectionMatrix):F6}" +
                $"\nGame source:  additionalData={gameSnapshot.hasAdditionalData} rawProjIdentity={LooksLikeIdentity(gameSnapshot.rawCameraProjectionMatrix)} rawNonJitteredIdentity={LooksLikeIdentity(gameSnapshot.rawCameraNonJitteredProjectionMatrix)} effectiveProjIdentity={LooksLikeIdentity(gameSnapshot.effectiveProjectionMatrix)} effectiveNonJitteredIdentity={LooksLikeIdentity(gameSnapshot.effectiveNonJitteredProjectionMatrix)} rawVsEffectiveProjMaxDiff={MaxAbsDiff(gameSnapshot.rawCameraProjectionMatrix, gameSnapshot.effectiveProjectionMatrix):F6}" +
                $"\nScene rawCamera.projectionMatrix:\n{sceneSnapshot.rawCameraProjectionMatrix.ToString("F4")}" +
                $"\nScene rawCamera.nonJitteredProjectionMatrix:\n{sceneSnapshot.rawCameraNonJitteredProjectionMatrix.ToString("F4")}" +
                $"\nScene effectiveProjectionMatrix:\n{sceneSnapshot.effectiveProjectionMatrix.ToString("F4")}" +
                $"\nScene effectiveNonJitteredProjectionMatrix:\n{sceneSnapshot.effectiveNonJitteredProjectionMatrix.ToString("F4")}" +
                $"\nGame rawCamera.projectionMatrix:\n{gameSnapshot.rawCameraProjectionMatrix.ToString("F4")}" +
                $"\nGame rawCamera.nonJitteredProjectionMatrix:\n{gameSnapshot.rawCameraNonJitteredProjectionMatrix.ToString("F4")}" +
                $"\nGame effectiveProjectionMatrix:\n{gameSnapshot.effectiveProjectionMatrix.ToString("F4")}" +
                $"\nGame effectiveNonJitteredProjectionMatrix:\n{gameSnapshot.effectiveNonJitteredProjectionMatrix.ToString("F4")}" +
                "\nDiffering camera-related globals:" +
                $"\n{details}";
            signature = signatureBuilder.ToString();
            return true;
        }

        private static void AppendBoolDifference(string label, bool sceneValue, bool gameValue, StringBuilder details,
            StringBuilder signature, ref int differenceCount, ref int omittedDifferenceCount)
        {
            if (sceneValue == gameValue)
            {
                return;
            }

            AppendDifference(label, 1.0f, $"{label}: scene={sceneValue} game={gameValue}", details, signature,
                ref differenceCount, ref omittedDifferenceCount);
        }

        private static void AppendScalarDifference(string label, float sceneValue, float gameValue, float tolerance,
            StringBuilder details, StringBuilder signature, ref int differenceCount, ref int omittedDifferenceCount)
        {
            var maxDiff = Mathf.Abs(sceneValue - gameValue);
            if (maxDiff <= tolerance)
            {
                return;
            }

            AppendDifference(label, maxDiff, $"{label}: scene={sceneValue:F6} game={gameValue:F6}", details, signature,
                ref differenceCount, ref omittedDifferenceCount);
        }

        private static void AppendVectorDifference(string label, Vector4 sceneValue, Vector4 gameValue, float tolerance,
            StringBuilder details, StringBuilder signature, ref int differenceCount, ref int omittedDifferenceCount)
        {
            var maxDiff = MaxAbsDiff(sceneValue, gameValue);
            if (maxDiff <= tolerance)
            {
                return;
            }

            AppendDifference(label, maxDiff, $"{label}: scene={sceneValue.ToString("F4")} game={gameValue.ToString("F4")}",
                details, signature, ref differenceCount, ref omittedDifferenceCount);
        }

        private static void AppendMatrixDifference(string label, Matrix4x4 sceneValue, Matrix4x4 gameValue, float tolerance,
            StringBuilder details, StringBuilder signature, ref int differenceCount, ref int omittedDifferenceCount)
        {
            var maxDiff = MaxAbsDiff(sceneValue, gameValue);
            if (maxDiff <= tolerance)
            {
                return;
            }

            AppendDifference(label, maxDiff, $"{label}: scene={sceneValue.ToString("F4")} game={gameValue.ToString("F4")}",
                details, signature, ref differenceCount, ref omittedDifferenceCount);
        }

        private static void AppendDifference(string label, float maxDiff, string detail, StringBuilder details,
            StringBuilder signature, ref int differenceCount, ref int omittedDifferenceCount)
        {
            differenceCount++;
            signature.Append(label);
            signature.Append('=');
            signature.Append(maxDiff.ToString("F3"));
            signature.Append('|');

            if (differenceCount > MaxReportedDifferences)
            {
                omittedDifferenceCount++;
                return;
            }

            details.Append("- ");
            details.Append(detail);
            details.Append(" maxAbsDiff=");
            details.AppendLine(maxDiff.ToString("F6"));
        }

        private static float MaxAbsDiff(Vector4 sceneValue, Vector4 gameValue)
        {
            return Mathf.Max(
                Mathf.Abs(sceneValue.x - gameValue.x),
                Mathf.Abs(sceneValue.y - gameValue.y),
                Mathf.Abs(sceneValue.z - gameValue.z),
                Mathf.Abs(sceneValue.w - gameValue.w));
        }

        private static float MaxAbsDiff(Matrix4x4 sceneValue, Matrix4x4 gameValue)
        {
            var maxDiff = 0.0f;
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    maxDiff = Mathf.Max(maxDiff, Mathf.Abs(sceneValue[row, column] - gameValue[row, column]));
                }
            }

            return maxDiff;
        }

        private static bool LooksLikeIdentity(Matrix4x4 matrix)
        {
            return MaxAbsDiff(matrix, Matrix4x4.identity) <= ScalarTolerance;
        }

        private static string FormatRect(Rect rect)
        {
            return rect.ToString("F2");
        }
    }
}

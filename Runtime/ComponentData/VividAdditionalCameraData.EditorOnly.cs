#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VividRP.Runtime
{
    public partial class VividAdditionalCameraData
    {
        internal const string ExportFinalFrameScreenshotButtonText = "Export Final Frame Screenshot";
        internal const string ExportFinalFrameScreenshotPendingText =
            "Waiting for the next render of this camera to export the screenshot.";

        private const string ScreenshotExtension = ".png";
        private const string LastScreenshotDirectoryPreferenceKey =
            "VividRP.AdditionalCameraData.LastScreenshotDirectory";

        private static readonly List<FinalFrameScreenshotRequest> s_FinalFrameScreenshotRequests = new();
        private static bool s_FinalFrameScreenshotCallbacksRegistered;

        private sealed class FinalFrameScreenshotRequest
        {
            public EntityId CameraEntityId;
            public string Path;
            public RenderTexture CaptureTarget;
            public bool CaptureTargetWritten;
        }

        internal bool TryPromptAndRequestFinalFrameScreenshot()
        {
            var currentCamera = camera;
            if (currentCamera == null)
            {
                Debug.LogError("[VividRP] Cannot export a screenshot without an attached Camera.", this);
                return false;
            }

            var path = EditorUtility.SaveFilePanel(
                "Export Final Frame Screenshot",
                GetInitialScreenshotDirectory(),
                CreateFinalFrameScreenshotFileName(currentCamera),
                ScreenshotExtension.TrimStart('.'));

            if (string.IsNullOrEmpty(path))
                return false;

            RequestFinalFrameScreenshot(path);
            return true;
        }

        internal void RequestFinalFrameScreenshot(string path)
        {
            var currentCamera = camera;
            if (currentCamera == null)
                throw new InvalidOperationException("Cannot request a screenshot without an attached Camera.");

            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Screenshot path must not be empty.", nameof(path));

            QueueFinalFrameScreenshot(currentCamera, EnsurePngExtension(path));
        }

        internal bool IsFinalFrameScreenshotPending()
        {
            return IsFinalFrameScreenshotPending(camera);
        }

        internal static bool IsFinalFrameScreenshotPending(Camera camera)
        {
            return camera != null && FindFinalFrameScreenshotRequest(camera.GetEntityId()) != null;
        }

        internal static bool TryGetFinalFrameScreenshotCaptureTarget(Camera camera, out RenderTexture captureTarget)
        {
            var request = camera != null ? FindFinalFrameScreenshotRequest(camera.GetEntityId()) : null;
            captureTarget = request?.CaptureTarget;
            return captureTarget != null;
        }

        internal static void MarkFinalFrameScreenshotCaptureTargetWritten(Camera camera)
        {
            var request = camera != null ? FindFinalFrameScreenshotRequest(camera.GetEntityId()) : null;
            if (request != null)
                request.CaptureTargetWritten = true;
        }

        internal static string CreateFinalFrameScreenshotFileName(Camera camera)
        {
            var cameraName = camera != null && !string.IsNullOrWhiteSpace(camera.name)
                ? camera.name
                : "Camera";

            return $"{SanitizeFileName(cameraName)}_{DateTime.Now:yyyyMMdd_HHmmss}{ScreenshotExtension}";
        }

        internal static Rect ClampFinalFrameScreenshotReadbackRect(Rect rect, int targetWidth, int targetHeight)
        {
            targetWidth = Mathf.Max(1, targetWidth);
            targetHeight = Mathf.Max(1, targetHeight);

            if (rect.width <= 0f || rect.height <= 0f)
                return new Rect(0f, 0f, targetWidth, targetHeight);

            var xMin = Mathf.Clamp(Mathf.FloorToInt(rect.xMin), 0, targetWidth - 1);
            var yMin = Mathf.Clamp(Mathf.FloorToInt(rect.yMin), 0, targetHeight - 1);
            var xMax = Mathf.Clamp(Mathf.CeilToInt(rect.xMax), xMin + 1, targetWidth);
            var yMax = Mathf.Clamp(Mathf.CeilToInt(rect.yMax), yMin + 1, targetHeight);
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        private static void QueueFinalFrameScreenshot(Camera camera, string path)
        {
            var cameraEntityId = camera.GetEntityId();
            RemoveFinalFrameScreenshotRequest(cameraEntityId);

            var captureSize = ResolveFinalFrameScreenshotSize(camera);
            s_FinalFrameScreenshotRequests.Add(new FinalFrameScreenshotRequest
            {
                CameraEntityId = cameraEntityId,
                Path = path,
                CaptureTarget = CreateFinalFrameScreenshotCaptureTarget(camera, captureSize),
            });

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                EditorPrefs.SetString(LastScreenshotDirectoryPreferenceKey, directory);

            RegisterFinalFrameScreenshotCallbacks();
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        private static Vector2Int ResolveFinalFrameScreenshotSize(Camera camera)
        {
            var targetTexture = camera.targetTexture;
            if (targetTexture != null)
            {
                return new Vector2Int(
                    Mathf.Max(1, targetTexture.width),
                    Mathf.Max(1, targetTexture.height));
            }

            if (TryGetMainGameViewSize(out var gameViewSize))
                return gameViewSize;

            return new Vector2Int(
                Mathf.Max(1, camera.pixelWidth > 0 ? camera.pixelWidth : Screen.width),
                Mathf.Max(1, camera.pixelHeight > 0 ? camera.pixelHeight : Screen.height));
        }

        private static RenderTexture CreateFinalFrameScreenshotCaptureTarget(Camera camera, Vector2Int size)
        {
            var captureTarget = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
            {
                name = $"VividRP Final Frame Screenshot ({camera.name})",
                hideFlags = HideFlags.HideAndDontSave,
                useMipMap = false,
                autoGenerateMips = false,
            };
            captureTarget.Create();
            return captureTarget;
        }

        private static void RegisterFinalFrameScreenshotCallbacks()
        {
            if (s_FinalFrameScreenshotCallbacksRegistered)
                return;

            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            Camera.onPostRender += OnCameraPostRender;
            s_FinalFrameScreenshotCallbacksRegistered = true;
        }

        private static void UnregisterFinalFrameScreenshotCallbacks()
        {
            if (!s_FinalFrameScreenshotCallbacksRegistered || s_FinalFrameScreenshotRequests.Count > 0)
                return;

            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            Camera.onPostRender -= OnCameraPostRender;
            s_FinalFrameScreenshotCallbacksRegistered = false;
        }

        private static void OnEndCameraRendering(ScriptableRenderContext context, Camera renderedCamera)
        {
            TryWriteFinalFrameScreenshot(renderedCamera);
        }

        private static void OnCameraPostRender(Camera renderedCamera)
        {
            TryWriteFinalFrameScreenshot(renderedCamera);
        }

        private static void TryWriteFinalFrameScreenshot(Camera renderedCamera)
        {
            if (renderedCamera == null)
                return;

            var cameraEntityId = renderedCamera.GetEntityId();
            var request = FindFinalFrameScreenshotRequest(cameraEntityId);
            if (request == null)
                return;

            RemoveFinalFrameScreenshotRequest(cameraEntityId, releaseCaptureTarget: false);

            try
            {
                if (request.CaptureTargetWritten)
                    WriteCaptureTarget(request.CaptureTarget, request.Path);
                else
                    WriteCameraTarget(renderedCamera, request.Path);

                RefreshAssetDatabaseIfNeeded(request.Path);
                Debug.Log($"[VividRP] Exported final frame screenshot: {request.Path}", renderedCamera);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, renderedCamera);
            }
            finally
            {
                ReleaseCaptureTarget(request.CaptureTarget);
                UnregisterFinalFrameScreenshotCallbacks();
            }
        }

        private static void WriteCaptureTarget(RenderTexture captureTarget, string path)
        {
            if (captureTarget == null)
                throw new InvalidOperationException("Screenshot capture target was not allocated.");

            WriteRenderTexture(captureTarget, path, new Rect(0f, 0f, captureTarget.width, captureTarget.height));
        }

        private static void WriteCameraTarget(Camera renderedCamera, string path)
        {
            var targetTexture = renderedCamera.targetTexture;
            if (targetTexture != null)
            {
                WriteRenderTexture(
                    targetTexture,
                    path,
                    new Rect(0f, 0f, targetTexture.width, targetTexture.height));
                return;
            }

            var screenWidth = Mathf.Max(1, Screen.width > 0 ? Screen.width : renderedCamera.pixelWidth);
            var screenHeight = Mathf.Max(1, Screen.height > 0 ? Screen.height : renderedCamera.pixelHeight);
            if (TryGetMainGameViewSize(out var gameViewSize))
            {
                screenWidth = gameViewSize.x;
                screenHeight = gameViewSize.y;
            }

            var screenRect = new Rect(0f, 0f, screenWidth, screenHeight);
            var previousActive = RenderTexture.active;
            try
            {
                RenderTexture.active = null;
                WriteActiveRenderTarget(path, screenRect);
            }
            finally
            {
                RenderTexture.active = previousActive;
            }
        }

        private static void WriteRenderTexture(RenderTexture source, string path, Rect readbackRect)
        {
            var previousActive = RenderTexture.active;
            try
            {
                RenderTexture.active = source;
                WriteActiveRenderTarget(path, readbackRect);
            }
            finally
            {
                RenderTexture.active = previousActive;
            }
        }

        private static void WriteActiveRenderTarget(string path, Rect readbackRect)
        {
            var width = Mathf.Max(1, Mathf.RoundToInt(readbackRect.width));
            var height = Mathf.Max(1, Mathf.RoundToInt(readbackRect.height));
            Texture2D texture = null;

            try
            {
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                texture.ReadPixels(new Rect(readbackRect.x, readbackRect.y, width, height), 0, 0, false);
                texture.Apply(false, false);

                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                if (texture != null)
                    Object.DestroyImmediate(texture);
            }
        }

        private static FinalFrameScreenshotRequest FindFinalFrameScreenshotRequest(EntityId cameraEntityId)
        {
            for (var index = 0; index < s_FinalFrameScreenshotRequests.Count; index++)
            {
                var request = s_FinalFrameScreenshotRequests[index];
                if (request.CameraEntityId.Equals(cameraEntityId))
                    return request;
            }

            return null;
        }

        private static void RemoveFinalFrameScreenshotRequest(
            EntityId cameraEntityId,
            bool releaseCaptureTarget = true)
        {
            for (var index = s_FinalFrameScreenshotRequests.Count - 1; index >= 0; index--)
            {
                var request = s_FinalFrameScreenshotRequests[index];
                if (!request.CameraEntityId.Equals(cameraEntityId))
                    continue;

                s_FinalFrameScreenshotRequests.RemoveAt(index);
                if (releaseCaptureTarget)
                    ReleaseCaptureTarget(request.CaptureTarget);
            }

            UnregisterFinalFrameScreenshotCallbacks();
        }

        private static void ReleaseCaptureTarget(RenderTexture captureTarget)
        {
            if (captureTarget == null)
                return;

            captureTarget.Release();
            Object.DestroyImmediate(captureTarget);
        }

        private static string GetInitialScreenshotDirectory()
        {
            var directory = EditorPrefs.GetString(LastScreenshotDirectoryPreferenceKey, Application.dataPath);
            return !string.IsNullOrEmpty(directory) && Directory.Exists(directory)
                ? directory
                : Application.dataPath;
        }

        private static bool TryGetMainGameViewSize(out Vector2Int size)
        {
            size = default;

            var method = typeof(Handles).GetMethod(
                "GetMainGameViewSize",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
                return false;

            if (method.Invoke(null, null) is not Vector2 gameViewSize)
                return false;

            var width = Mathf.RoundToInt(gameViewSize.x);
            var height = Mathf.RoundToInt(gameViewSize.y);
            if (width <= 0 || height <= 0)
                return false;

            size = new Vector2Int(width, height);
            return true;
        }

        private static string EnsurePngExtension(string path)
        {
            return string.Equals(Path.GetExtension(path), ScreenshotExtension, StringComparison.OrdinalIgnoreCase)
                ? path
                : Path.ChangeExtension(path, ScreenshotExtension);
        }

        private static string SanitizeFileName(string fileName)
        {
            var invalidCharacters = Path.GetInvalidFileNameChars();
            var characters = fileName.ToCharArray();
            for (var index = 0; index < characters.Length; index++)
            {
                if (Array.IndexOf(invalidCharacters, characters[index]) >= 0)
                    characters[index] = '_';
            }

            var sanitized = new string(characters).Trim();
            return string.IsNullOrEmpty(sanitized) ? "Camera" : sanitized;
        }

        private static void RefreshAssetDatabaseIfNeeded(string path)
        {
            var projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                AssetDatabase.Refresh();
        }
    }
}
#endif

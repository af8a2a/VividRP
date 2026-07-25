using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public enum ReferencedPathTracingCaptureStatus
    {
        None = 0,
        WaitingForSamples = 1,
        ReadbackPending = 2,
        Completed = 3,
        Failed = 4
    }

    public readonly struct ReferencedPathTracingCaptureResult
    {
        internal ReferencedPathTracingCaptureResult(
            ReferencedPathTracingCaptureStatus status,
            string exrPath,
            string metadataPath,
            string message)
        {
            this.status = status;
            this.exrPath = exrPath;
            this.metadataPath = metadataPath;
            this.message = message;
        }

        public ReferencedPathTracingCaptureStatus status { get; }
        public string exrPath { get; }
        public string metadataPath { get; }
        public string message { get; }
    }

    /// <summary>
    /// Request surface for canonical raw path-tracing captures. Requests are consumed only by
    /// ReferencedPathTracingCapturePass and never capture the pre-exposed resolved or denoised path.
    /// Existing files are not overwritten.
    /// </summary>
    public static class ReferencedPathTracingCapture
    {
        internal sealed class CaptureRequest
        {
            internal Camera camera;
            internal string corpusCaseId;
            internal string exrPath;
            internal string metadataPath;
            internal int unsupportedMaterialCount;
            internal ReferencedPathTracingCaptureStatus status;
            internal string message;
            internal ReferencedPathTracingCaptureMetadata metadata;
        }

        internal sealed class CaptureTask
        {
            internal CaptureRequest request;
            internal Action<AsyncGPUReadbackRequest> callback;
        }

        private static readonly Dictionary<Camera, CaptureRequest> s_Requests =
            new();

        public static bool Request(
            Camera camera,
            string corpusCaseId,
            string exrPath,
            out string failure)
        {
            return Request(
                camera,
                corpusCaseId,
                exrPath,
                0,
                out failure);
        }

        public static bool Request(
            Camera camera,
            string corpusCaseId,
            string exrPath,
            int unsupportedMaterialCount,
            out string failure)
        {
            if (camera == null)
                return Fail("Capture camera is missing.", out failure);
            if (!ReferencedPathTracingV1Corpus.TryGetCase(
                    corpusCaseId,
                    out _))
            {
                return Fail(
                    $"Unknown HDRI corpus case '{corpusCaseId}'.",
                    out failure);
            }
            if (string.IsNullOrWhiteSpace(exrPath))
                return Fail("Capture path is empty.", out failure);

            string absoluteExrPath;
            string metadataPath;
            try
            {
                absoluteExrPath = Path.GetFullPath(exrPath);
                metadataPath =
                    Path.ChangeExtension(absoluteExrPath, ".json");
            }
            catch (Exception exception)
            {
                return Fail(exception.Message, out failure);
            }

            if (!string.Equals(
                    Path.GetExtension(absoluteExrPath),
                    ".exr",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Fail("Canonical capture path must use .exr.", out failure);
            }

            if (File.Exists(absoluteExrPath) || File.Exists(metadataPath))
                return Fail("Capture output already exists.", out failure);
            if (s_Requests.TryGetValue(camera, out var existing)
                && existing.status is ReferencedPathTracingCaptureStatus.WaitingForSamples
                    or ReferencedPathTracingCaptureStatus.ReadbackPending)
            {
                return Fail("The camera already has a pending capture.", out failure);
            }

            s_Requests[camera] = new CaptureRequest
            {
                camera = camera,
                corpusCaseId = corpusCaseId,
                exrPath = absoluteExrPath,
                metadataPath = metadataPath,
                unsupportedMaterialCount =
                    Mathf.Max(0, unsupportedMaterialCount),
                status =
                    ReferencedPathTracingCaptureStatus.WaitingForSamples,
                message = "Waiting for the canonical target SPP."
            };
            failure = string.Empty;
            return true;
        }

        public static bool TryGetResult(
            Camera camera,
            out ReferencedPathTracingCaptureResult result)
        {
            if (camera != null
                && s_Requests.TryGetValue(camera, out var request))
            {
                result = new ReferencedPathTracingCaptureResult(
                    request.status,
                    request.exrPath,
                    request.metadataPath,
                    request.message);
                return true;
            }

            result = default;
            return false;
        }

        public static void Cancel(Camera camera)
        {
            if (camera == null
                || !s_Requests.TryGetValue(camera, out var request)
                || request.status
                    == ReferencedPathTracingCaptureStatus.ReadbackPending)
            {
                return;
            }

            s_Requests.Remove(camera);
        }

        internal static bool TryBegin(
            Camera camera,
            ContextContainer frameData,
            ulong accumulatedSampleCount,
            out CaptureTask task)
        {
            task = null;
            if (camera == null
                || !s_Requests.TryGetValue(camera, out var request)
                || request.status
                    != ReferencedPathTracingCaptureStatus.WaitingForSamples
                || !ReferencedPathTracingV1Corpus.TryGetCase(
                    request.corpusCaseId,
                    out var corpusCase))
            {
                return false;
            }

            var targetSampleCount = (ulong)corpusCase.targetSampleCount;
            if (accumulatedSampleCount < targetSampleCount)
                return false;
            if (accumulatedSampleCount > targetSampleCount)
            {
                SetFailed(
                    request,
                    "Capture request missed the exact canonical target SPP.");
                return false;
            }

            request.metadata =
                ReferencedPathTracingV1FreezeGate.BuildMetadata(
                    request.corpusCaseId,
                    frameData,
                    accumulatedSampleCount,
                    request.unsupportedMaterialCount);
            if (!ReferencedPathTracingV1FreezeGate.ValidateCaptureContract(
                    request.metadata,
                    out var failure))
            {
                SetFailed(request, failure);
                return false;
            }

            request.status =
                ReferencedPathTracingCaptureStatus.ReadbackPending;
            request.message = "Raw FP32 readback is pending.";
            var captureTask = new CaptureTask
            {
                request = request
            };
            captureTask.callback =
                readback => Complete(captureTask, readback);
            task = captureTask;
            return true;
        }

        internal static void SetFailed(CaptureTask task, string message)
        {
            if (task?.request != null)
                SetFailed(task.request, message);
        }

        private static void Complete(
            CaptureTask task,
            AsyncGPUReadbackRequest readback)
        {
            var request = task?.request;
            if (request == null)
                return;
            if (readback.hasError)
            {
                SetFailed(request, "Raw FP32 GPU readback failed.");
                return;
            }

            Texture2D texture = null;
            var wroteExr = false;
            try
            {
                var metadata = request.metadata;
                texture = new Texture2D(
                    metadata.width,
                    metadata.height,
                    TextureFormat.RGBAFloat,
                    false,
                    true)
                {
                    name = "ReferencedPathTracingCanonicalCapture",
                    hideFlags = HideFlags.HideAndDontSave
                };
                var rawData = readback.GetData<float>();
                UpdateBasicImageMetrics(metadata, rawData);
                texture.SetPixelData(rawData, 0);
                texture.Apply(false, false);
                var exrBytes = texture.EncodeToEXR(
                    Texture2D.EXRFlags.OutputAsFloat
                    | Texture2D.EXRFlags.CompressZIP);
                metadata.validation.referenceImageSha256 =
                    ComputeSha256(exrBytes);

                WriteNewFile(request.exrPath, exrBytes);
                wroteExr = true;
                WriteNewFile(
                    request.metadataPath,
                    Encoding.UTF8.GetBytes(
                        JsonUtility.ToJson(metadata, true)));

                request.status =
                    ReferencedPathTracingCaptureStatus.Completed;
                request.message = metadata.validation.status
                    == ReferencedPathTracingValidationStatus.Failed
                    ? "Raw EXR was written, but basic radiance validation failed."
                    : "Raw EXR and pending-validation metadata were written.";
            }
            catch (Exception exception)
            {
                if (wroteExr && !File.Exists(request.metadataPath))
                {
                    try
                    {
                        File.Delete(request.exrPath);
                    }
                    catch
                    {
                        // Preserve the original capture exception.
                    }
                }

                SetFailed(request, exception.Message);
            }
            finally
            {
                if (texture != null)
                    CoreUtils.Destroy(texture);
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(bytes);
            var builder = new StringBuilder(hash.Length * 2);
            for (var index = 0; index < hash.Length; index++)
                builder.Append(hash[index].ToString("x2"));
            return builder.ToString();
        }

        private static void UpdateBasicImageMetrics(
            ReferencedPathTracingCaptureMetadata metadata,
            Unity.Collections.NativeArray<float> rawData)
        {
            var pixelCount = Math.Max(1, metadata.width * metadata.height);
            var finitePixelCount = 0;
            var negativePixelCount = 0;
            double luminanceSum = 0.0;
            for (var pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
            {
                var elementIndex = pixelIndex * 4;
                if (elementIndex + 2 >= rawData.Length)
                    break;

                var red = rawData[elementIndex];
                var green = rawData[elementIndex + 1];
                var blue = rawData[elementIndex + 2];
                var isFinite =
                    !float.IsNaN(red)
                    && !float.IsInfinity(red)
                    && !float.IsNaN(green)
                    && !float.IsInfinity(green)
                    && !float.IsNaN(blue)
                    && !float.IsInfinity(blue);
                if (!isFinite)
                    continue;

                finitePixelCount++;
                if (red < 0.0f || green < 0.0f || blue < 0.0f)
                    negativePixelCount++;
                luminanceSum +=
                    red * 0.2126
                    + green * 0.7152
                    + blue * 0.0722;
            }

            var evidence = metadata.validation;
            evidence.finitePixelFraction =
                (float)finitePixelCount / pixelCount;
            evidence.negativeRadianceFraction =
                (float)negativePixelCount / pixelCount;
            evidence.meanLuminance = finitePixelCount > 0
                ? (float)(luminanceSum / finitePixelCount)
                : 0.0f;
            if (evidence.finitePixelFraction < 1.0f
                || evidence.negativeRadianceFraction > 0.0f)
            {
                evidence.status =
                    ReferencedPathTracingValidationStatus.Failed;
                evidence.notes =
                    "Basic raw-radiance integrity validation failed.";
            }
        }

        private static void WriteNewFile(string path, byte[] bytes)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void SetFailed(
            CaptureRequest request,
            string message)
        {
            request.status = ReferencedPathTracingCaptureStatus.Failed;
            request.message = message ?? "Capture failed.";
        }

        private static bool Fail(string message, out string failure)
        {
            failure = message;
            return false;
        }
    }

    /// <summary>
    /// Optional terminal RenderGraph stage for canonical raw accumulation capture. Connect
    /// PathTracingAccumulationRaw to the accumulation pass' FP32 current-history output.
    /// </summary>
    public sealed class ReferencedPathTracingCapturePass
        : UnsafePass, IRenderGraphSideEffectPass
    {
        [RenderGraphResource(
            Name = "PathTracingAccumulationRaw",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_RawAccumulation;

        private ReferencedPathTracingCapture.CaptureTask m_CaptureTask;

        public ReferencedPathTracingCapturePass()
        {
            profilingSampler =
                new ProfilingSampler(nameof(ReferencedPathTracingCapturePass));
            m_RawAccumulation = RenderGraphTexture.CreateInput(
                "PathTracingAccumulationRaw",
                GraphicsFormat.R32G32B32A32_SFloat);
        }

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_CaptureTask = null;
            var cameraData = frameData.Get<VividCameraData>();
            var pathTracingData =
                frameData.GetOrCreate<VividReferencedPathTracingData>();
            ReferencedPathTracingCapture.TryBegin(
                cameraData?.camera,
                frameData,
                pathTracingData.accumulatedSampleCount,
                out m_CaptureTask);
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_CaptureTask == null
                || m_RawAccumulation?.innerHandle.IsValid() != true)
            {
                return;
            }

            var source =
                TextureResolveUtility.ResolveTexture(
                    m_RawAccumulation.innerHandle);
            if (source == null)
            {
                ReferencedPathTracingCapture.SetFailed(
                    m_CaptureTask,
                    "Raw accumulation texture is unavailable.");
                m_CaptureTask = null;
                return;
            }

            using (new ProfilingScope(
                       context.GetNativeCommandBuffer(),
                       profilingSampler))
            {
                context.GetNativeCommandBuffer().RequestAsyncReadback(
                    source,
                    0,
                    m_CaptureTask.callback);
            }

            m_CaptureTask = null;
        }

        public override void Dispose()
        {
            m_CaptureTask = null;
        }
    }
}

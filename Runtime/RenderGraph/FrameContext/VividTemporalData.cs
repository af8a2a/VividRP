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

    /// <summary>
    /// Per-frame handshake shared by the reference ray dispatch, raw accumulation, and capture
    /// stages. It is intentionally separate from display temporal data because canonical sample
    /// indices must reset from the path-tracing scene signature rather than presentation frames.
    /// </summary>
    internal sealed class VividReferencedPathTracingData : ContextItem
    {
        internal bool isValid;
        internal bool deterministicSampling;
        internal uint sampleIndex;
        internal ulong frameSignature;
        internal ulong integratorSignature;
        internal ulong accumulatedSampleCount;

        public override void Reset()
        {
            isValid = false;
            deterministicSampling = false;
            sampleIndex = 0;
            frameSignature = 0;
            integratorSignature = 0;
            accumulatedSampleCount = 0;
        }
    }
}

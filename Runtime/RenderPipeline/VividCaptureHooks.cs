using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    /// <summary>Result of the whole camera invocation, including submission and public callbacks.</summary>
    public enum VividCaptureOutcome
    {
        Submitted,
        ResourcesUnavailable,
        CullingUnavailable,
        PreviewCamera,
        RenderGraphFailed,
        Failed
    }

#if UNITY_EDITOR
    public interface IVividCaptureObserver
    {
        void BeginCamera(ScriptableRenderContext context, Camera camera, int frameIndex);
        void EndCamera(ScriptableRenderContext context, Camera camera, int frameIndex, VividCaptureOutcome outcome);
        void CameraSkipped(Camera camera, VividCaptureOutcome outcome);
    }

    /// <summary>
    /// Main-thread, exclusive diagnostic observer. The idle path only reads a reference;
    /// no event enumeration, reflection, delegates or per-camera allocations.
    /// Observers must handle their own failures without interrupting rendering.
    /// </summary>
    public static class VividCaptureHooks
    {
        private static IVividCaptureObserver owner;

        public static bool TryAcquire(IVividCaptureObserver observer)
        {
            if (observer == null || owner != null) return false;
            owner = observer;
            return true;
        }

        public static void Release(IVividCaptureObserver observer)
        {
            if (ReferenceEquals(owner, observer)) owner = null;
        }

        internal static IVividCaptureObserver Begin(ScriptableRenderContext context, Camera camera, int frameIndex)
        {
            var current = owner;
            current?.BeginCamera(context, camera, frameIndex);
            return current;
        }

        internal static void End(IVividCaptureObserver current, ScriptableRenderContext context,
            Camera camera, int frameIndex, VividCaptureOutcome outcome)
        {
            if (ReferenceEquals(current, owner)) current?.EndCamera(context, camera, frameIndex, outcome);
        }

        internal static void Skip(Camera camera, VividCaptureOutcome outcome) => owner?.CameraSkipped(camera, outcome);
    }
#endif
}

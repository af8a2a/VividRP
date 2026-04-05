using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    // HDR passes should rely on this frame-global binding instead of per-pass fallback buffers.
    internal static class AutoExposureShaderBindings
    {
        internal static readonly int PreExposureBufferId = Shader.PropertyToID("_VividAutoExposurePreExposureBuffer");

        internal static void BindFrameGlobals(CommandBuffer cmd, VividExposureData exposureData)
        {
            if (cmd == null)
                return;

            var preExposureBuffer = ResolvePreExposureBuffer(exposureData);
            if (preExposureBuffer != null)
                cmd.SetGlobalBuffer(PreExposureBufferId, preExposureBuffer);
        }

        internal static GraphicsBuffer ResolvePreExposureBuffer(VividExposureData exposureData)
        {
            return exposureData?.preExposureBuffer
                ?? exposureData?.defaultExposureBuffer
                ?? AutoExposureRuntimeManager.GetOrCreateDefaultExposureBuffer();
        }
    }
}

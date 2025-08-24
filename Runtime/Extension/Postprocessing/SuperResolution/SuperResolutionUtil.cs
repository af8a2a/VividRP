using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal.SuperResolution
{
    public class SuperResolutionUtil
    {
        internal class UpdateCameraResolutionPassData
        {
            internal Vector2Int newCameraTargetSize;
        }

        // Updates render target descriptors and shader constants to reflect a new render size
        // This should be called immediately after the resolution changes mid-frame (typically after an upscaling operation).
        public static void UpdateCameraResolution(RenderGraph renderGraph, UniversalCameraData cameraData, Vector2Int newCameraTargetSize)
        {
            //todo:should do it?

            // cameraData.renderScale = 1;

            cameraData.cameraTargetDescriptor.width = newCameraTargetSize.x;
            cameraData.cameraTargetDescriptor.height = newCameraTargetSize.y;

            // Update the shader constants to reflect the new camera resolution
            using (var builder = renderGraph.AddUnsafePass<UpdateCameraResolutionPassData>("Update Camera Resolution", out var passData))
            {
                passData.newCameraTargetSize = newCameraTargetSize;

                // This pass only modifies shader constants so we need to set some special flags to ensure it isn't culled or optimized away
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (UpdateCameraResolutionPassData data, UnsafeGraphContext ctx) =>
                {
                    ctx.cmd.SetGlobalVector(
                        ShaderPropertyId.screenSize,
                        new Vector4(
                            data.newCameraTargetSize.x,
                            data.newCameraTargetSize.y,
                            1.0f / data.newCameraTargetSize.x,
                            1.0f / data.newCameraTargetSize.y
                        )
                    );
                });
            }
        }
    }
}
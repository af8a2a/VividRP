using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class ExposureSetupPass : ScriptableRenderPass
    {
        public ExposureSetupPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingGbuffer;
        }

        public static readonly int _ExposureTexture = Shader.PropertyToID("_ExposureTexture");
        public static readonly int _PrevExposureTexture = Shader.PropertyToID("_PrevExposureTexture");


        class PassData
        {
            internal TextureHandle currExposureTexture;
            internal TextureHandle prevExposureTexture;
        }

        internal const GraphicsFormat k_ExposureFormat = GraphicsFormat.R32G32_SFloat;

        internal static void SetExposureTextureToEmpty(RTHandle exposureTexture)
        {
            var tex = new Texture2D(1, 1, GraphicsFormat.R16G16_SFloat, TextureCreationFlags.None);
            tex.SetPixel(0, 0, new Color(1f, ColorUtils.ConvertExposureToEV100(1f), 0f, 0f));
            tex.Apply();
            Graphics.Blit(tex, exposureTexture);
            CoreUtils.Destroy(tex);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddComputePass<PassData>("Exposure Setup", out var data))
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                var historyRTSystem = cameraData.historyFrameRTSystem;
                var currentTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.Exposure);
                if (currentTexture == null)
                {
                    RTHandle Allocator(string id, int frameIndex, RTHandleSystem rtHandleSystem)
                    {
                        // r: multiplier, g: EV100
                        var rt = rtHandleSystem.Alloc(1, 1, colorFormat: k_ExposureFormat,
                            enableRandomWrite: true, name: $"{id} Exposure Texture {frameIndex}"
                        );
                        SetExposureTextureToEmpty(rt);
                        return rt;
                    }

                    currentTexture = historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.Exposure, Allocator, 2);
                }

                data.currExposureTexture = renderGraph.ImportTexture(historyRTSystem.GetPreviousFrameRT(HistoryFrameType.Exposure));
                data.prevExposureTexture = renderGraph.ImportTexture(currentTexture);
                builder.UseTexture(data.currExposureTexture);
                builder.UseTexture(data.prevExposureTexture);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc<PassData>((passData, context) =>
                {
                    context.cmd.SetGlobalTexture(_ExposureTexture, passData.currExposureTexture);
                    context.cmd.SetGlobalTexture(_PrevExposureTexture, passData.prevExposureTexture);
                });
            }
        }
    }
}
using System;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public partial class TemporalDenoiser
    {
        private Material _material;

        private Material TemporalDenoiserMaterial => _material ??= new Material(Shader.Find("TAA"));


        public class TemporalAntiAliasingPSData
        {
            public Material TemporalAntiAliasingMaterial;
            public Vector2 Resolution;
            public TextureHandle motionTexture;
            public TextureHandle depthTexture;
            public TextureHandle currentHistory;
            public TextureHandle inputTexture;
            public TextureHandle denoiseOutput;
            public float feedback;
        }

        public TextureHandle DoColorTemporalDenoisePS(RenderGraph renderGraph,
            Camera camera,
            TextureHandle motionVectors,
            TextureHandle depthTexture,
            TextureHandle inputTexture,
            TextureHandle currentHistory,
            TextureHandle outputHistory,
            TemporalDenoiserSetting setting)
        {
            using var builder =
                renderGraph.AddRasterRenderPass<TemporalAntiAliasingPSData>("Temporal Denoise PS", out var passData);
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            passData.Resolution = new Vector2(camera.pixelWidth, camera.pixelHeight);


            passData.TemporalAntiAliasingMaterial = TemporalDenoiserMaterial;
            passData.motionTexture = motionVectors;
            passData.inputTexture = inputTexture;
            passData.depthTexture = depthTexture;
            passData.currentHistory = currentHistory;
            passData.denoiseOutput = outputHistory;
            passData.feedback = setting.feedback.value;

            builder.UseTexture(passData.motionTexture);
            builder.UseTexture(passData.depthTexture);
            builder.UseTexture(passData.currentHistory);
            builder.UseTexture(passData.inputTexture);

            var material = passData.TemporalAntiAliasingMaterial;
            material.SetMatrix("_I_P_Current_jittered",
                TemporalUtils.GetJitteredPerspectiveProjectionMatrix(camera, TemporalUtils.GenerateRandomOffset()));
            var offset = setting.spread.value * TemporalUtils.GenerateRandomOffset() / passData.Resolution;
            material.SetVector("_TAA_Params",
                new Vector4(offset.x, offset.y, setting.feedback.value, 0));

            CoreUtils.SetKeyword(material,"_LOW_TAA",false);
            CoreUtils.SetKeyword(material,"_MIDDLE_TAA",false);
            CoreUtils.SetKeyword(material,"_HIGH_TAA",false);

            switch (setting.quality.value)
            {
                case MotionBlurQuality.Low:
                    CoreUtils.SetKeyword(material,"_LOW_TAA",true);
                    break;
                case MotionBlurQuality.Medium:
                    CoreUtils.SetKeyword(material,"_MIDDLE_TAA",true);
                    break;
                case MotionBlurQuality.High:
                    CoreUtils.SetKeyword(material,"_HIGH_TAA",true);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            
            builder.SetRenderAttachment(passData.denoiseOutput, 0);
            builder.SetRenderFunc(
                (TemporalAntiAliasingPSData data, RasterGraphContext ctx) =>
                {
                    passData.TemporalAntiAliasingMaterial.SetTexture("_MotionTexture", passData.motionTexture);
                    passData.TemporalAntiAliasingMaterial.SetTexture("_TAA_Pretexture", passData.currentHistory);

                    Blitter.BlitTexture(ctx.cmd, data.inputTexture, Vector2.one, data.TemporalAntiAliasingMaterial, 0);
                });
            return passData.denoiseOutput;
        }
    }
}
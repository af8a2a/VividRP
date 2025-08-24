using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class LensFlareScreenSpacePass
    {
        Material material;
        Texture2D m_InternalSpectralLut;

        private class LensFlareScreenSpacePassData
        {
            internal TextureHandle destinationTexture;
            internal TextureHandle streakTmpTexture;
            internal TextureHandle streakTmpTexture2;
            internal TextureHandle originalBloomTexture;
            internal TextureHandle screenSpaceLensFlareBloomMipTexture;
            internal TextureHandle result;
            internal Texture lensFlareScreenSpaceSpectralLut;
            internal RenderTextureDescriptor sourceDescriptor;
            internal Camera camera;
            internal Material material;
            internal ScreenSpaceLensFlare lensFlareScreenSpace;
            internal int downsample;
        }


        Texture2D GetOrCreateDefaultInternalSpectralLut()
        {
            if (m_InternalSpectralLut == null)
            {
                m_InternalSpectralLut = new Texture2D(3, 1, GraphicsFormat.R8G8B8A8_SRGB, TextureCreationFlags.None)
                {
                    name = "Chromatic Aberration Spectral LUT",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0,
                    hideFlags = HideFlags.DontSave
                };

                m_InternalSpectralLut.SetPixels(new[]
                {
                    new Color(1f, 0f, 0f, 1f),
                    new Color(0f, 1f, 0f, 1f),
                    new Color(0f, 0f, 1f, 1f)
                });

                m_InternalSpectralLut.Apply();
            }

            return m_InternalSpectralLut;
        }


        // public TextureHandle RenderLensFlareScreenSpace(RenderGraph renderGraph, Camera camera, in TextureHandle destination, TextureHandle originalBloomTexture, TextureHandle screenSpaceLensFlareBloomMipTexture, bool enableXR)
        public TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source)

        {
            if (!material)
            {
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<PostProcessingRuntimeShader>();
                material = CoreUtils.CreateEngineMaterial(runtimeShader.lensFlareScreenSpace);
            }


            var lensFlareScreenSpace = VolumeManager.instance.stack.GetComponent<ScreenSpaceLensFlare>();


            var bloom = VolumeManager.instance.stack.GetComponent<MobileBloom>();
            if (bloom.mode.value is BloomMode.None||!lensFlareScreenSpace.IsActive())
            {
                return source;
            }

            var downsample = (int)lensFlareScreenSpace.resolution.value;

            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();

            int width = Math.Max(cameraData.scaledWidth / downsample, 1);
            int height = Math.Max(cameraData.scaledHeight / downsample, 1);


            var format = RenderingUtilsExt.PickPostProcessingFormat();
            var streakTmpTexture = renderGraph.CreateTexture(new TextureDesc(width, height)
            {
                format = format,
                name = "_StreakTmpTexture",
                clearBuffer = true,
                filterMode = FilterMode.Bilinear
            });

            var streakTmpTexture2 = renderGraph.CreateTexture(new TextureDesc(width, height)
            {
                format = format,
                name = "_StreakTmpTexture2",
                clearBuffer = true,
                filterMode = FilterMode.Bilinear
            });


            var resultTexture = renderGraph.CreateTexture(new TextureDesc(width, height)
            {
                enableRandomWrite = true,
                format = format,
                name = "Lens Flare Screen Space Result",
                clearBuffer = true,
                filterMode = FilterMode.Bilinear
            });

            int maxBloomMip = Mathf.Clamp(lensFlareScreenSpace.bloomMip.value, 0, bloom.maxBloomMip);


            using (var builder = renderGraph.AddUnsafePass<LensFlareScreenSpacePassData>("Lens Flare Screen Space Pass", out var passData,
                       ProfilingSampler.Get(URPProfileId.LensFlareScreenSpace)))
            {
                // Use WriteTexture here because DoLensFlareScreenSpaceCommon will call SetRenderTarget internally.
                // TODO RENDERGRAPH: convert SRP core lensflare to be rendergraph friendly
                passData.destinationTexture = source;
                builder.UseTexture(passData.destinationTexture, AccessFlags.Write);
                passData.streakTmpTexture = streakTmpTexture;
                builder.UseTexture(passData.streakTmpTexture, AccessFlags.ReadWrite);
                passData.streakTmpTexture2 = streakTmpTexture2;
                builder.UseTexture(passData.streakTmpTexture2, AccessFlags.ReadWrite);
                passData.screenSpaceLensFlareBloomMipTexture = resourceData.bloomMipUpTexture[maxBloomMip];
                builder.UseTexture(passData.screenSpaceLensFlareBloomMipTexture, AccessFlags.ReadWrite);
                passData.originalBloomTexture = resourceData.bloomMipUpTexture[0];
                builder.UseTexture(passData.originalBloomTexture, AccessFlags.ReadWrite);
                passData.sourceDescriptor = new RenderTextureDescriptor(cameraData.scaledWidth, cameraData.scaledHeight);
                passData.camera = cameraData.camera;
                passData.material = material;
                passData.lensFlareScreenSpace = lensFlareScreenSpace; // NOTE: reference, assumed constant until executed.
                passData.downsample = downsample;
                passData.result = resultTexture;
                builder.UseTexture(resultTexture, AccessFlags.Write);

                passData.lensFlareScreenSpaceSpectralLut = GetOrCreateDefaultInternalSpectralLut();

                builder.SetRenderFunc(static (LensFlareScreenSpacePassData data, UnsafeGraphContext context) =>
                {
                    var cmd = context.cmd;
                    var camera = data.camera;
                    var lensFlareScreenSpace = data.lensFlareScreenSpace;

                    LensFlareCommonSRP.DoLensFlareScreenSpaceCommon(
                        data.material,
                        camera,
                        (float)data.sourceDescriptor.width,
                        (float)data.sourceDescriptor.height,
                        data.lensFlareScreenSpace.tintColor.value,
                        data.originalBloomTexture,
                        data.screenSpaceLensFlareBloomMipTexture,
                        data.lensFlareScreenSpaceSpectralLut, // We don't have any spectral LUT in URP
                        data.streakTmpTexture,
                        data.streakTmpTexture2,
                        new Vector4(
                            lensFlareScreenSpace.intensity.value,
                            lensFlareScreenSpace.firstFlareIntensity.value,
                            lensFlareScreenSpace.secondaryFlareIntensity.value,
                            lensFlareScreenSpace.warpedFlareIntensity.value),
                        new Vector4(
                            lensFlareScreenSpace.vignetteEffect.value,
                            lensFlareScreenSpace.startingPosition.value,
                            lensFlareScreenSpace.scale.value,
                            0), // Free slot, not used
                        new Vector4(
                            lensFlareScreenSpace.samples.value,
                            lensFlareScreenSpace.sampleDimmer.value,
                            lensFlareScreenSpace.chromaticAbberationIntensity.value,
                            0), // No need to pass a chromatic aberration sample count, hardcoded at 3 in shader
                        new Vector4(
                            lensFlareScreenSpace.streaksIntensity.value,
                            lensFlareScreenSpace.streaksLength.value,
                            lensFlareScreenSpace.streaksOrientation.value,
                            lensFlareScreenSpace.streaksThreshold.value),
                        new Vector4(
                            data.downsample,
                            lensFlareScreenSpace.warpedFlareScale.value.x,
                            lensFlareScreenSpace.warpedFlareScale.value.y,
                            0), // Free slot, not used
                        cmd,
                        data.result,
                        false);
                });
                return passData.originalBloomTexture;
            }
        }
    }
}
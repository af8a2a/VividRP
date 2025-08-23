using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class SMAAPass
    {
        Material material;
        Texture2D areaTexture;
        Texture2D searchTexture;

        private class SMAASetupPassData
        {
            internal Vector4 metrics;
            internal Texture2D areaTexture;
            internal Texture2D searchTexture;
            internal float stencilRef;
            internal float stencilMask;
            internal AntialiasingQuality antialiasingQuality;
            internal Material material;
        }

        private class SMAAPassData
        {
            internal TextureHandle destinationTexture;
            internal TextureHandle sourceTexture;
            internal TextureHandle depthStencilTexture;
            internal TextureHandle blendTexture;
            internal Material material;
        }


        public TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source)
        {
            
            if (!material)
            {
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<PostProcessingRuntimeShader>();
                material = CoreUtils.CreateEngineMaterial(runtimeShader.smaaShader);
                areaTexture = runtimeShader.smaaAreaTex;
                searchTexture = runtimeShader.smaaSearchTex;
            }
            


            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            if (cameraData.antialiasing is not AntialiasingMode.SubpixelMorphologicalAntiAliasing)
            {
                return source;
            }
                
                
            var SMAATarget = renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
            {
                enableRandomWrite = true,
                format = renderGraph.GetTextureDesc(source).format,
                name = "_SMAATarget",
                clearBuffer = true,
                filterMode = FilterMode.Bilinear,
            });


            var edgeTexture = renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
            {
                enableRandomWrite = true,
                format = GraphicsFormat.R8G8B8A8_UNorm,
                name = "_EdgeStencilTexture",
                clearBuffer = true,
                filterMode = FilterMode.Bilinear,
            });


            var edgeTextureStencil = renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
            {
                format = GraphicsFormat.None,
                depthBufferBits = DepthBits.Depth24,
                name = "_EdgeTexture",
                clearBuffer = true,
                filterMode = FilterMode.Bilinear,
            });

            var blendTexture = renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
            {
                enableRandomWrite = true,
                format = GraphicsFormat.R8G8B8A8_UNorm,
                name = "_BlendTexture",
                clearBuffer = true,
                filterMode = FilterMode.Point,
            });


            // Anti-aliasing

            using (var builder = renderGraph.AddRasterRenderPass<SMAASetupPassData>("SMAA Material Setup", out var passData,
                       ProfilingSampler.Get(URPProfileId.RG_SMAAMaterialSetup)))
            {
                const int kStencilBit = 64;
                // TODO RENDERGRAPH: handle dynamic scaling
                passData.metrics = new Vector4(1f / cameraData.scaledWidth, 1f / cameraData.scaledHeight, cameraData.scaledWidth, cameraData.scaledHeight);
                passData.areaTexture = areaTexture;
                passData.searchTexture = searchTexture;
                passData.stencilRef = (float)kStencilBit;
                passData.stencilMask = (float)kStencilBit;
                passData.antialiasingQuality = cameraData.antialiasingQuality;
                passData.material = material;

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (SMAASetupPassData data, RasterGraphContext context) =>
                {
                    // Globals
                    data.material.SetVector(ShaderConstants._Metrics, data.metrics);
                    data.material.SetTexture(ShaderConstants._AreaTexture, data.areaTexture);
                    data.material.SetTexture(ShaderConstants._SearchTexture, data.searchTexture);
                    data.material.SetFloat(ShaderConstants._StencilRef, data.stencilRef);
                    data.material.SetFloat(ShaderConstants._StencilMask, data.stencilMask);

                    // Quality presets
                    data.material.shaderKeywords = null;

                    switch (data.antialiasingQuality)
                    {
                        case AntialiasingQuality.Low:
                            data.material.EnableKeyword(ShaderKeywordStrings.SmaaLow);
                            break;
                        case AntialiasingQuality.Medium:
                            data.material.EnableKeyword(ShaderKeywordStrings.SmaaMedium);
                            break;
                        case AntialiasingQuality.High:
                            data.material.EnableKeyword(ShaderKeywordStrings.SmaaHigh);
                            break;
                    }
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<SMAAPassData>("SMAA Edge Detection", out var passData,
                       ProfilingSampler.Get(URPProfileId.RG_SMAAEdgeDetection)))
            {
                passData.destinationTexture = edgeTexture;
                builder.SetRenderAttachment(edgeTexture, 0, AccessFlags.Write);
                passData.depthStencilTexture = edgeTextureStencil;
                builder.SetRenderAttachmentDepth(edgeTextureStencil, AccessFlags.Write);
                passData.sourceTexture = source;
                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(resourceData.cameraDepth, AccessFlags.Read);
                passData.material = material;

                builder.SetRenderFunc(static (SMAAPassData data, RasterGraphContext context) =>
                {
                    var SMAAMaterial = data.material;
                    var cmd = context.cmd;
                    RTHandle sourceTextureHdl = data.sourceTexture;

                    // Pass 1: Edge detection
                    Vector2 viewportScale = sourceTextureHdl.useScaling
                        ? new Vector2(sourceTextureHdl.rtHandleProperties.rtHandleScale.x, sourceTextureHdl.rtHandleProperties.rtHandleScale.y)
                        : Vector2.one;
                    Blitter.BlitTexture(cmd, sourceTextureHdl, viewportScale, SMAAMaterial, 0);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<SMAAPassData>("SMAA Blend weights", out var passData,
                       ProfilingSampler.Get(URPProfileId.RG_SMAABlendWeight)))
            {
                passData.destinationTexture = blendTexture;
                builder.SetRenderAttachment(blendTexture, 0, AccessFlags.Write);
                passData.depthStencilTexture = edgeTextureStencil;
                builder.SetRenderAttachmentDepth(edgeTextureStencil, AccessFlags.Read);
                passData.sourceTexture = edgeTexture;
                builder.UseTexture(edgeTexture, AccessFlags.Read);
                passData.material = material;

                builder.SetRenderFunc(static (SMAAPassData data, RasterGraphContext context) =>
                {
                    var SMAAMaterial = data.material;
                    var cmd = context.cmd;
                    RTHandle sourceTextureHdl = data.sourceTexture;

                    // Pass 2: Blend weights
                    Vector2 viewportScale = sourceTextureHdl.useScaling
                        ? new Vector2(sourceTextureHdl.rtHandleProperties.rtHandleScale.x, sourceTextureHdl.rtHandleProperties.rtHandleScale.y)
                        : Vector2.one;
                    Blitter.BlitTexture(cmd, sourceTextureHdl, viewportScale, SMAAMaterial, 1);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<SMAAPassData>("SMAA Neighborhood blending", out var passData,
                       ProfilingSampler.Get(URPProfileId.RG_SMAANeighborhoodBlend)))
            {
                builder.AllowGlobalStateModification(true);
                passData.destinationTexture = SMAATarget;
                builder.SetRenderAttachment(SMAATarget, 0, AccessFlags.Write);
                passData.sourceTexture = source;
                builder.UseTexture(source, AccessFlags.Read);
                passData.blendTexture = blendTexture;
                builder.UseTexture(blendTexture, AccessFlags.Read);
                passData.material = material;

                builder.SetRenderFunc(static (SMAAPassData data, RasterGraphContext context) =>
                {
                    var SMAAMaterial = data.material;
                    var cmd = context.cmd;
                    RTHandle sourceTextureHdl = data.sourceTexture;

                    // Pass 3: Neighborhood blending
                    SMAAMaterial.SetTexture(ShaderConstants._BlendTexture, data.blendTexture);
                    Vector2 viewportScale = sourceTextureHdl.useScaling
                        ? new Vector2(sourceTextureHdl.rtHandleProperties.rtHandleScale.x, sourceTextureHdl.rtHandleProperties.rtHandleScale.y)
                        : Vector2.one;
                    Blitter.BlitTexture(cmd, sourceTextureHdl, viewportScale, SMAAMaterial, 2);
                });
            }

            return SMAATarget;
        }
    }
}
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class GranTurismoToneMappingPass : ScriptableRenderPass
    {
        static class ShaderID
        {
            public static readonly int _GTToneMap_Params0 = Shader.PropertyToID("_GTToneMap_Params0");
            public static readonly int _GTToneMap_Params1 = Shader.PropertyToID("_GTToneMap_Params1");
        }

        private Material material;

        private Material ToneMappingMaterial
        {
            get
            {
                if (material == null)
                {
                    material = new Material(Shader.Find("PostProcessing/CustomToneMapping"));
                }

                return material;
            }
        }


        class PassData
        {
            internal Material material;
            internal float4 gtToneMapParams0;
            internal float4 gtToneMapParams1;

            internal TextureHandle cameraTexture;
        }


        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("GranTurismo ToneMapping", out var data))
            {
                var toneMapping = VolumeManager.instance.stack.GetComponent<GranTurismo>();

                if (toneMapping == null || !toneMapping.IsActive())
                {
                    return;
                }

                data.material = ToneMappingMaterial;
                data.gtToneMapParams0 = new Vector4(toneMapping.maxBrightness.value, toneMapping.contrast.value,
                    toneMapping.linearSectionStart.value, toneMapping.linearSectionLength.value);
                data.gtToneMapParams1 = new Vector4(toneMapping.blackPow.value, toneMapping.blackMin.value, 0.0f,
                    0.0f);
                var resourceData = frameData.Get<UniversalResourceData>();

                var desc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
                var targetTexture = renderGraph.CreateTexture(desc);
                
                data.cameraTexture = resourceData.activeColorTexture;
                
                builder.SetRenderAttachment(targetTexture, 0);
                builder.UseTexture(data.cameraTexture);

                builder.SetRenderFunc((PassData passData, RasterGraphContext ctx) =>
                {
                    var cmd = ctx.cmd;

                    passData.material.SetVector(ShaderID._GTToneMap_Params0, passData.gtToneMapParams0);
                    passData.material.SetVector(ShaderID._GTToneMap_Params1, passData.gtToneMapParams1);

                    Blitter.BlitTexture(cmd, data.cameraTexture, new Vector4(1, 1, 0, 0), passData.material, 0);
                });
                resourceData.cameraColor = targetTexture;
            }
        }
    }
}
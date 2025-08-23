using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class DiffusionPass : ScriptableRenderPass
    {
        private Material m_DiffusionMaterial;


        class ShaderID
        {
            public static readonly int _Multiply = Shader.PropertyToID("_Multiply");
            public static readonly int _Filter = Shader.PropertyToID("_Filter");
            public static readonly int _Intensity = Shader.PropertyToID("_Intensity");
            public static readonly int _BlurScale = Shader.PropertyToID("_BlurScale");
            public static readonly int _BlurTexture = Shader.PropertyToID("_BlurTexture");
        }


        class PassData
        {
            internal Material material;
            internal Diffusion setting;

            internal TextureHandle cameraColor;
            internal TextureHandle diffusionTexture;
            internal TextureHandle tempTexture1;
            internal TextureHandle tempTexture2;
        }


        //in VividRP, ColorAttachment default set enableRandomWrite
        public TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, in TextureHandle source)
        {
            if (!m_DiffusionMaterial)
            {
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<DiffusionRuntimeShader>();
                m_DiffusionMaterial = CoreUtils.CreateEngineMaterial(runtimeShader.diffusionShader);
            }

            using (var builder = renderGraph.AddUnsafePass<PassData>("Diffusion", out var passData))
            {
                var setting = VolumeManager.instance.stack.GetComponent<Diffusion>();
                if (!setting.IsActive())
                {
                    return source;
                }

                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraColorDesc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);

                var diffsuionTexture = renderGraph.CreateTexture(new TextureDesc(Vector2.one * 0.5f)
                {
                    format = cameraColorDesc.format,
                    name = "Diffusion Texture"
                });

                var tempRT1 = builder.CreateTransientTexture(new TextureDesc(Vector2.one)
                {
                    format = cameraColorDesc.format,
                });
                var tempRT2 = builder.CreateTransientTexture(new TextureDesc(Vector2.one)
                {
                    format = cameraColorDesc.format,
                });

                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                // passData.sourceTexture = cameraColor;
                passData.diffusionTexture = diffsuionTexture;
                passData.tempTexture1 = tempRT1;
                passData.tempTexture2 = tempRT2;
                passData.material = m_DiffusionMaterial;
                passData.cameraColor = source;
                passData.setting = setting;

                builder.UseTexture(passData.cameraColor, AccessFlags.ReadWrite);
                builder.UseTexture(passData.diffusionTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.tempTexture1, AccessFlags.ReadWrite);
                builder.UseTexture(passData.tempTexture2, AccessFlags.ReadWrite);


                builder.SetRenderFunc((PassData data, UnsafeGraphContext rgContext) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(rgContext.cmd);

                    data.material.SetFloat(ShaderID._Multiply, data.setting.multiply.value);
                    data.material.SetFloat(ShaderID._Filter, data.setting.filter.value);
                    data.material.SetFloat(ShaderID._Intensity, data.setting.intensity.value);
                    data.material.SetFloat(ShaderID._BlurScale, data.setting.blurScale.value);


                    //  multiply
                    Blitter.BlitCameraTexture(cmd, data.cameraColor, data.diffusionTexture, data.material, 3);

                    //  blur
                    Blitter.BlitCameraTexture(cmd, data.cameraColor, data.tempTexture1, data.material, 0);
                    Blitter.BlitCameraTexture(cmd, data.tempTexture1, data.tempTexture2, data.material, 1);


                    //  max or filter
                    if (data.setting.mode.value == DiffusionMode.Max)
                    {
                        Blitter.BlitCameraTexture(cmd, data.tempTexture2, data.cameraColor, data.material, 2);
                    }
                    else
                    {
                        data.material.SetTexture(ShaderID._BlurTexture, data.tempTexture2);
                        Blitter.BlitCameraTexture(cmd, data.diffusionTexture, data.cameraColor, data.material, 4);
                    }
                });
                return passData.cameraColor;
            }
        }


        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddUnsafePass<PassData>("Diffusion", out var passData))
            {
                if (!m_DiffusionMaterial)
                {
                    var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<DiffusionRuntimeShader>();
                    m_DiffusionMaterial = CoreUtils.CreateEngineMaterial(runtimeShader.diffusionShader);
                }

                var setting = VolumeManager.instance.stack.GetComponent<Diffusion>();
                if (setting == null || !setting.IsActive())
                {
                    return;
                }

                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraColorDesc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);

                var diffsuionTexture = renderGraph.CreateTexture(new TextureDesc(Vector2.one * 0.5f)
                {
                    format = cameraColorDesc.format,
                    name = "Diffusion Texture"
                });

                var tempRT1 = builder.CreateTransientTexture(new TextureDesc(Vector2.one)
                {
                    format = cameraColorDesc.format,
                });
                var tempRT2 = builder.CreateTransientTexture(new TextureDesc(Vector2.one)
                {
                    format = cameraColorDesc.format,
                });

                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                // passData.sourceTexture = cameraColor;
                passData.diffusionTexture = diffsuionTexture;
                passData.tempTexture1 = tempRT1;
                passData.tempTexture2 = tempRT2;
                passData.material = m_DiffusionMaterial;
                passData.cameraColor = resourceData.activeColorTexture;
                passData.setting = setting;

                builder.UseTexture(passData.cameraColor, AccessFlags.ReadWrite);
                builder.UseTexture(passData.diffusionTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.tempTexture1, AccessFlags.ReadWrite);
                builder.UseTexture(passData.tempTexture2, AccessFlags.ReadWrite);


                builder.SetRenderFunc((PassData data, UnsafeGraphContext rgContext) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(rgContext.cmd);

                    data.material.SetFloat(ShaderID._Multiply, data.setting.multiply.value);
                    data.material.SetFloat(ShaderID._Filter, data.setting.filter.value);
                    data.material.SetFloat(ShaderID._Intensity, data.setting.intensity.value);
                    data.material.SetFloat(ShaderID._BlurScale, data.setting.blurScale.value);


                    //  multiply
                    Blitter.BlitCameraTexture(cmd, data.cameraColor, data.diffusionTexture, data.material, 3);

                    //  blur
                    Blitter.BlitCameraTexture(cmd, data.cameraColor, data.tempTexture1, data.material, 0);
                    Blitter.BlitCameraTexture(cmd, data.tempTexture1, data.tempTexture2, data.material, 1);


                    //  max or filter
                    if (data.setting.mode.value == DiffusionMode.Max)
                    {
                        Blitter.BlitCameraTexture(cmd, data.tempTexture2, data.cameraColor, data.material, 2);
                    }
                    else
                    {
                        data.material.SetTexture(ShaderID._BlurTexture, data.tempTexture2);
                        Blitter.BlitCameraTexture(cmd, data.diffusionTexture, data.cameraColor, data.material, 4);
                    }
                });
            }
        }
    }
}
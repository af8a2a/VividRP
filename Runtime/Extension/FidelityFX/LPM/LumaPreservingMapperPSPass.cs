using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    class LumaPreservingMapperPSPass : ScriptableRenderPass
    {
        private const string lpmShaderName = "LPM";

        [SerializeField] private Material material;
        private String lastKeyword = "SDR";

        static class ShaderConstants
        {
            public static readonly int _SoftGap = Shader.PropertyToID("_SoftGap");
            public static readonly int _HdrMax = Shader.PropertyToID("_HdrMax");
            public static readonly int _Exposure = Shader.PropertyToID("_Exposure");
            public static readonly int _LPMExposure = Shader.PropertyToID("_LPMExposure");
            public static readonly int _Contrast = Shader.PropertyToID("_Contrast");
            public static readonly int _ShoulderContrast = Shader.PropertyToID("_ShoulderContrast");
            public static readonly int _Saturation = Shader.PropertyToID("_Saturation");
            public static readonly int _Crosstalk = Shader.PropertyToID("_Crosstalk");
            public static readonly int _Intensity = Shader.PropertyToID("_LPMIntensity");
            public static readonly int _DisplayMinMaxLuminance = Shader.PropertyToID("_DisplayMinMaxLuminance");
        }

        private Material lpmMaterial
        {
            get
            {
                if (material == null)
                {
                    material = new Material(Shader.Find(lpmShaderName));
                }

                return material;
            }
        }

        public LumaPreservingMapperPSPass()
        {
            profilingSampler = new ProfilingSampler(nameof(LumaPreservingMapperPSPass));
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // var volume = VolumeManager.instance.stack.GetComponent<LumaPreservingMapper>();
            // if (volume == null || !volume.IsActive())
            // {
            //     return;
            // }
            //
            // UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            // UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            // lpmMaterial.SetFloat(ShaderConstants._Intensity, volume.Intensity.value);
            // lpmMaterial.SetFloat(ShaderConstants._SoftGap, volume.SoftGap.value);
            // lpmMaterial.SetFloat(ShaderConstants._HdrMax, volume.HdrMax.value);
            // lpmMaterial.SetFloat(ShaderConstants._LPMExposure, volume.LPMExposure.value);
            // lpmMaterial.SetFloat(ShaderConstants._Exposure, MathF.Pow(2.0f, volume.Exposure.value));
            // lpmMaterial.SetFloat(ShaderConstants._Contrast, volume.Contrast.value);
            // lpmMaterial.SetFloat(ShaderConstants._ShoulderContrast, volume.ShoulderContrast.value);
            // lpmMaterial.SetFloat(ShaderConstants._Intensity, volume.Intensity.value);
            //
            // lpmMaterial.SetVector(ShaderConstants._Saturation, volume.Saturation.value);
            // lpmMaterial.SetVector(ShaderConstants._Crosstalk, volume.Crosstalk.value);
            // if (cameraData.isHDROutputActive)
            // {
            //     if (volume.displayMode.value != DisplayMode.SDR)
            //     {
            //         lpmMaterial.SetVector(ShaderConstants._DisplayMinMaxLuminance,
            //             new Vector2(cameraData.hdrDisplayInformation.minToneMapLuminance,
            //                 cameraData.hdrDisplayInformation.maxToneMapLuminance));
            //     }
            // }
            //
            // //now only support SDR
            // lpmMaterial.DisableKeyword(lastKeyword);
            // switch (volume.displayMode.value)
            // {
            //     case DisplayMode.FfxLpmDisplaymodeLDR:
            //         lpmMaterial.EnableKeyword("SDR");
            //         lastKeyword = "SDR";
            //         break;
            //     case DisplayMode.FfxLpmDisplaymodeHDR10Scrgb:
            //         lpmMaterial.EnableKeyword("DISPLAYMODE_HDR10_SCRGB");
            //         lastKeyword = "DISPLAYMODE_HDR10_SCRGB";
            //         break;
            //     case DisplayMode.FfxLpmDisplaymodeHDR102084:
            //         lpmMaterial.EnableKeyword("DISPLAYMODE_HDR10_2084");
            //         lastKeyword = "DISPLAYMODE_HDR10_2084";
            //         break;
            // }
            //
            //
            // TextureHandle source = resourceData.activeColorTexture;
            // var destinationDesc = renderGraph.GetTextureDesc(source);
            // destinationDesc.name = $"CameraColor-{nameof(lpmMaterial)}";
            // destinationDesc.clearBuffer = false;
            //
            // TextureHandle destination = renderGraph.CreateTexture(destinationDesc);
            //
            // RenderGraphUtils.BlitMaterialParameters para = new(source, destination, lpmMaterial,
            //     0);
            // renderGraph.AddBlitPass(para, passName: nameof(LumaPreservingMapperPSPass));
            // resourceData.cameraColor = destination;
        }
    }
}
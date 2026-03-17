using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal static class VividPreIntegratedFGD
    {
        internal const int TextureResolution = 64;
        internal const string GGXDisneyDiffuseShaderName = "Hidden/VividRP/PreIntegratedFGD_GGXDisneyDiffuse";
        internal const string CharlieFabricLambertShaderName = "Hidden/VividRP/PreIntegratedFGD_CharlieFabricLambert";

        internal static readonly int GGXDisneyDiffuseTextureId = Shader.PropertyToID("_PreIntegratedFGD_GGXDisneyDiffuse");
        internal static readonly int CharlieAndFabricTextureId = Shader.PropertyToID("_PreIntegratedFGD_CharlieAndFabric");

        internal static Material CreateGGXDisneyDiffuseMaterial(VividRPCoreResources resources)
        {
            return CreateMaterial(resources?.PreIntegratedFGDGGXDisneyDiffuseShader, GGXDisneyDiffuseShaderName);
        }

        internal static Material CreateCharlieFabricLambertMaterial(VividRPCoreResources resources)
        {
            return CreateMaterial(resources?.PreIntegratedFGDCharlieFabricLambertShader, CharlieFabricLambertShaderName);
        }

        internal static RenderGraphTexture CreateTexture(string name)
        {
            return new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = TextureResolution,
                    Height = TextureResolution,
                    Dimension = TextureDimension.Tex2D,
                    ColorFormat = GraphicsFormat.A2B10G10R10_UNormPack32,
                    DepthBufferBits = DepthBits.None,
                    FilterMode = FilterMode.Bilinear,
                    WrapMode = TextureWrapMode.Clamp,
                    UseMipMap = false,
                    AutoGenerateMips = false,
                    MipCount = 1,
                    ClearBuffer = false,
                    EnableRandomWrite = false,
                    Name = name
                }
            };
        }

        private static Material CreateMaterial(Shader shader, string fallbackShaderName)
        {
            shader ??= Shader.Find(fallbackShaderName);

            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{fallbackShaderName}' for pre-integrated FGD generation.");
                return null;
            }

            return CoreUtils.CreateEngineMaterial(shader);
        }
    }
}

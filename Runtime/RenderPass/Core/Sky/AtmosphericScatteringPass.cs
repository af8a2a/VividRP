using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class AtmosphericScatteringPass : RasterPass
    {
        internal const string OpaqueAtmosphericScatteringPassName = "Opaque Atmospheric Scattering";
        internal const string OpaqueAtmosphericScatteringShaderName = "Hidden/VividRP/OpaqueAtmosphericScattering";
        private const int DefaultShaderPassIndex = 0;
        private const int HDRISkyShaderPassIndex = 1;

        private static readonly int InputColorId = Shader.PropertyToID("_InputColor");
        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int AtmosphericScatteringLutId = Shader.PropertyToID("_AtmosphericScatteringLUT");
        private static readonly int SkyTextureId = Shader.PropertyToID("_SkyTexture");
        private static readonly int SkyTextureTintId = Shader.PropertyToID("_SkyTextureTint");
        private static readonly int SkyTextureParamsId = Shader.PropertyToID("_SkyTextureParams");
        private static readonly int FogColorId = Shader.PropertyToID("_FogColor");
        private static readonly int FogColorModeId = Shader.PropertyToID("_FogColorMode");
        private static readonly int MipFogParametersId = Shader.PropertyToID("_MipFogParameters");
        private static readonly int PixelCoordToViewDirWSId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private static readonly int SkyFogParamsId = Shader.PropertyToID("_SkyFogParams");
        private static readonly int VBufferLightingId = Shader.PropertyToID("_VBufferLighting");
        private static readonly int VolumetricEnabledId = Shader.PropertyToID("_VolumetricEnabled");
        private static readonly int ShaderVariablesVolumetricId = Shader.PropertyToID("ShaderVariablesVolumetric");

        [RenderGraphResource(Name = "Color", Access = AccessFlags.Read)]
        private RenderGraphTexture m_ColorInput;

        [RenderGraphResource(Name = "CameraDepth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "SkyTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_SkyTexture;

        [RenderGraphResource(Name = "VBufferLighting", Access = AccessFlags.Read)]
        private RenderGraphTexture m_VBufferLighting;

        [RenderGraphResource(
            Name = "OutputColor",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        private Material m_Material;
        private bool m_IsActive;
        private bool m_HasMaterialParameters;
        private PhysicallyBasedSkyShaderParameters m_Parameters;
        private PhysicallyBasedSkyMaterialParameters m_MaterialParameters;
        private Vector4 m_FogParameters;
        private Texture3D m_FallbackAtmosphericScatteringLut;
        private Texture3D m_FallbackVBufferLighting;
        private Cubemap m_FallbackSkyTexture;
        private RTHandle m_AtmosphericScatteringLutHandle;
        private SkyType m_ActiveSkyType;
        private int m_SkyMaxMipLevel;
        private Color m_SkyTint = Color.white;
        private float m_SkyExposure = 1.0f;
        private float m_SkyRotation;
        private bool m_VolumetricEnabled;
        private ShaderVariablesVolumetric m_ShaderVariablesVolumetric;

        public AtmosphericScatteringPass()
        {
            profilingSampler = new ProfilingSampler(OpaqueAtmosphericScatteringPassName);

            m_ColorInput = RenderGraphTexture.CreateInput("Color", GraphicsFormat.R16G16B16A16_SFloat);
            m_DepthTexture = RenderGraphTexture.CreateInput("CameraDepth", GraphicsFormat.None, DepthBits.Depth32);
            m_DepthTexture.desc.FilterMode = FilterMode.Point;
            m_SkyTexture = CreateSkyTexture("SkyTexture");
            m_VBufferLighting = VolumetricDensityPass.CreateVBufferTexture("VBufferLighting");
            m_VBufferLighting.desc.ClearColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
            m_OutputTexture = RenderGraphTexture.CreateOutput("OutputColor", GraphicsFormat.R16G16B16A16_SFloat);
            m_OutputTexture.desc.ClearBuffer = false;
        }

        public override void Create()
        {
            SkyManager.Initialize();

            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var shader = resources?.AerialPerspectiveShader;
            shader ??= Shader.Find(OpaqueAtmosphericScatteringShaderName);

            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{OpaqueAtmosphericScatteringShaderName}' for {nameof(AtmosphericScatteringPass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
            EnsureFallbackAtmosphericScatteringLut();
            EnsureFallbackVBufferLighting();
            EnsureFallbackSkyTexture();
        }

        public override void Prepare(ContextContainer frameData)
        {
            var skyData = frameData?.GetOrCreate<VividSkyData>();
            m_ActiveSkyType = skyData?.activeSkyType ?? SkyType.None;
            if (m_ActiveSkyType == SkyType.HDRI)
            {
                m_Parameters = default;
                m_HasMaterialParameters = false;
                m_IsActive = TryBuildHDRISkyFogParameters(out m_FogParameters);
            }
            else
            {
                m_IsActive = PhysicallyBasedSkyShaderParameterBuilder.TryBuild(frameData, out m_Parameters)
                    && m_Parameters.skyFogParams.x > 0.5f;
                m_HasMaterialParameters = PhysicallyBasedSkyShaderParameterBuilder.TryBuildMaterialParameters(frameData, out m_MaterialParameters);
                m_FogParameters = m_Parameters.skyFogParams;
            }

            m_AtmosphericScatteringLutHandle = skyData?.atmosphericScatteringLutHandle;
            if (m_ActiveSkyType != SkyType.HDRI && m_AtmosphericScatteringLutHandle != null)
                PassRecorder.ImportTextureForPass(this, m_AtmosphericScatteringLutHandle);

            SkyManager.ImportSpecularCubemap(m_SkyTexture, skyData);
            m_SkyMaxMipLevel = skyData != null && skyData.activeSkyType != SkyType.None
                ? SkyManager.GetSpecularCubemapMaxMip(skyData)
                : 0;
            m_SkyTint = skyData?.tint ?? Color.white;
            m_SkyExposure = skyData?.exposure ?? 1.0f;
            m_SkyRotation = skyData?.rotation ?? 0.0f;

            var volumetricData = frameData.GetOrCreate<VividVolumetricData>();
            m_VolumetricEnabled = volumetricData.enabled;
            m_ShaderVariablesVolumetric = volumetricData.shaderVariables;

            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var colorInputDescriptor = m_ColorInput?.desc;
            var width = ResolveOutputWidth(
                cameraData.actualWidth,
                cameraData.pixelWidth,
                Screen.width,
                colorInputDescriptor);
            var height = ResolveOutputHeight(
                cameraData.actualHeight,
                cameraData.pixelHeight,
                Screen.height,
                colorInputDescriptor);
            ConfigureOutputTexture(width, height, colorInputDescriptor);
        }

        public override void Record(RasterPassContext context)
        {
            if (m_Material == null
                || m_ColorInput?.innerHandle.IsValid() != true
                || m_OutputTexture?.innerHandle.IsValid() != true)
            {
                return;
            }

            var inputColor = m_ColorInput.innerHandle.ResolveTexture();
            if (inputColor == null)
                return;

            var depthTexture = m_DepthTexture.innerHandle.ResolveTexture() ?? Texture2D.whiteTexture;
            var atmosphericScatteringLut = m_AtmosphericScatteringLutHandle.ResolveTexture();
            var skyTexture = m_SkyTexture.ResolveTexture();
            var vBufferLighting = m_VBufferLighting.innerHandle.ResolveTexture();
            var hasValidAtmosphericScatteringLut = HasValidAtmosphericScatteringLut(atmosphericScatteringLut);
            var hasValidSkyTexture = HasValidSkyTexture(skyTexture);
            var hasVBufferLighting = HasValidVBuffer(vBufferLighting);
            if (!hasValidAtmosphericScatteringLut)
                EnsureFallbackAtmosphericScatteringLut();
            if (!hasValidSkyTexture)
                EnsureFallbackSkyTexture();
            if (!hasVBufferLighting)
                EnsureFallbackVBufferLighting();

            var hasUsableDepthTexture = HasUsableDepthTexture(depthTexture);
            var requiresAtmosphericScatteringLut = m_ActiveSkyType != SkyType.HDRI;

            var fogParams = m_IsActive
                            && hasUsableDepthTexture
                            && (!requiresAtmosphericScatteringLut
                                || (m_HasMaterialParameters && hasValidAtmosphericScatteringLut))
                ? m_FogParameters
                : Vector4.zero;

            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            mpb.SetTexture(InputColorId, inputColor);
            SetDepthTexture(mpb, depthTexture);
            mpb.SetTexture(
                AtmosphericScatteringLutId,
                hasValidAtmosphericScatteringLut ? atmosphericScatteringLut : m_FallbackAtmosphericScatteringLut);
            mpb.SetTexture(SkyTextureId, hasValidSkyTexture ? skyTexture : m_FallbackSkyTexture);
            mpb.SetTexture(VBufferLightingId, hasVBufferLighting ? vBufferLighting : m_FallbackVBufferLighting);
            mpb.SetFloat(VolumetricEnabledId, m_VolumetricEnabled && hasVBufferLighting ? 1.0f : 0.0f);
            mpb.SetColor(FogColorId, Color.white);
            mpb.SetFloat(FogColorModeId, m_IsActive && hasValidSkyTexture ? 1.0f : 0.0f);
            mpb.SetVector(MipFogParametersId, BuildMipFogParameters(fogParams));
            mpb.SetColor(SkyTextureTintId, m_SkyTint);
            mpb.SetVector(
                SkyTextureParamsId,
                new Vector4(
                    Mathf.Max(m_SkyExposure, 0.0f),
                    m_SkyRotation,
                    Mathf.Max(0, m_SkyMaxMipLevel),
                    hasValidSkyTexture ? 1.0f : 0.0f));
            mpb.SetMatrix(
                PixelCoordToViewDirWSId,
                m_IsActive && m_ActiveSkyType != SkyType.HDRI
                    ? m_Parameters.pixelCoordToViewDirWS
                    : Matrix4x4.identity);
            mpb.SetVector(SkyFogParamsId, fogParams);
            if (m_HasMaterialParameters)
                PhysicallyBasedSkyMaterialPropertyBinder.Apply(mpb, m_MaterialParameters, VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume());

            CoreUtils.DrawFullScreen(context.cmd, m_Material, mpb, ResolveShaderPassIndex());
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }

            if (m_FallbackAtmosphericScatteringLut != null)
            {
                CoreUtils.Destroy(m_FallbackAtmosphericScatteringLut);
                m_FallbackAtmosphericScatteringLut = null;
            }

            if (m_FallbackVBufferLighting != null)
            {
                CoreUtils.Destroy(m_FallbackVBufferLighting);
                m_FallbackVBufferLighting = null;
            }

            if (m_FallbackSkyTexture != null)
            {
                CoreUtils.Destroy(m_FallbackSkyTexture);
                m_FallbackSkyTexture = null;
            }
        }

        private void ConfigureOutputTexture(int width, int height, RenderGraphTextureDesc sourceDescriptor)
        {
            if (m_OutputTexture?.desc == null)
                return;

            m_OutputTexture.desc.Width = width;
            m_OutputTexture.desc.Height = height;
            m_OutputTexture.desc.ColorFormat = sourceDescriptor != null && sourceDescriptor.ColorFormat != GraphicsFormat.None
                ? sourceDescriptor.ColorFormat
                : GraphicsFormat.R16G16B16A16_SFloat;
            m_OutputTexture.desc.DepthBufferBits = DepthBits.None;
            m_OutputTexture.desc.MsaaSamples = MSAASamples.None;
            m_OutputTexture.desc.FilterMode = sourceDescriptor?.FilterMode ?? FilterMode.Bilinear;
            m_OutputTexture.desc.WrapMode = sourceDescriptor?.WrapMode ?? TextureWrapMode.Clamp;
            m_OutputTexture.desc.ClearBuffer = false;
            m_OutputTexture.desc.UseMipMap = false;
            m_OutputTexture.desc.AutoGenerateMips = false;
            m_OutputTexture.desc.MipCount = 1;
            m_OutputTexture.desc.EnableRandomWrite = false;
            m_OutputTexture.desc.BindTextureMS = false;
            m_OutputTexture.desc.Dimension = sourceDescriptor?.Dimension ?? TextureDimension.Tex2D;
            m_OutputTexture.desc.Slices = Mathf.Max(1, sourceDescriptor?.Slices ?? 1);
            m_OutputTexture.desc.UseDynamicScale = sourceDescriptor?.UseDynamicScale ?? false;
            m_OutputTexture.desc.UseDynamicScaleExplicit = sourceDescriptor?.UseDynamicScaleExplicit ?? false;
            m_OutputTexture.desc.ScaleFactor = sourceDescriptor?.ScaleFactor ?? Vector2.one;
        }

        private void EnsureFallbackAtmosphericScatteringLut()
        {
            if (m_FallbackAtmosphericScatteringLut != null)
                return;

            m_FallbackAtmosphericScatteringLut = new Texture3D(1, 1, 1, TextureFormat.RGBAHalf, false)
            {
                name = "VividFallbackAtmosphericScatteringLUT",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            m_FallbackAtmosphericScatteringLut.SetPixels(new[] { Color.black });
            m_FallbackAtmosphericScatteringLut.Apply(false, true);
        }

        private void EnsureFallbackVBufferLighting()
        {
            if (m_FallbackVBufferLighting != null)
                return;

            m_FallbackVBufferLighting = new Texture3D(1, 1, 1, TextureFormat.RGBAHalf, false)
            {
                name = "VividFallbackVBufferLighting",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            m_FallbackVBufferLighting.SetPixels(new[] { new Color(0.0f, 0.0f, 0.0f, 1.0f) });
            m_FallbackVBufferLighting.Apply(false, true);
        }

        private void EnsureFallbackSkyTexture()
        {
            if (m_FallbackSkyTexture != null)
                return;

            m_FallbackSkyTexture = new Cubemap(1, TextureFormat.RGBA32, false)
            {
                name = "VividFallbackSkyTexture",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var colors = new[] { Color.black };
            m_FallbackSkyTexture.SetPixels(colors, CubemapFace.PositiveX);
            m_FallbackSkyTexture.SetPixels(colors, CubemapFace.NegativeX);
            m_FallbackSkyTexture.SetPixels(colors, CubemapFace.PositiveY);
            m_FallbackSkyTexture.SetPixels(colors, CubemapFace.NegativeY);
            m_FallbackSkyTexture.SetPixels(colors, CubemapFace.PositiveZ);
            m_FallbackSkyTexture.SetPixels(colors, CubemapFace.NegativeZ);
            m_FallbackSkyTexture.Apply(false, true);
        }

        private static Vector4 BuildMipFogParameters(Vector4 fogParams)
        {
            return new Vector4(
                0.0f,
                Mathf.Max(fogParams.w, 1.0f),
                1.0f,
                0.0f);
        }

        private int ResolveShaderPassIndex()
        {
            return m_ActiveSkyType == SkyType.HDRI
                ? HDRISkyShaderPassIndex
                : DefaultShaderPassIndex;
        }

        private static bool TryBuildHDRISkyFogParameters(out Vector4 fogParameters)
        {
            var volume = VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume();
            if (volume == null || !volume.IsHeightFogActive())
            {
                fogParameters = Vector4.zero;
                return false;
            }

            fogParameters = new Vector4(
                1.0f,
                volume.fogBaseHeight.value,
                Mathf.Max(volume.fogDensity.value, 0.0f),
                Mathf.Max(volume.fogMaxDistance.value, 0.0f));
            return true;
        }

        private static int ResolveOutputWidth(
            int actualCameraDimension,
            int cameraDimension,
            int screenDimension,
            RenderGraphTextureDesc descriptor)
        {
            if (descriptor.HasExplicitSize())
                return Mathf.Max(1, descriptor.Width);

            return CameraDimensionUtility.ResolveCameraDimension(
                actualCameraDimension,
                cameraDimension,
                screenDimension);
        }

        private static int ResolveOutputHeight(
            int actualCameraDimension,
            int cameraDimension,
            int screenDimension,
            RenderGraphTextureDesc descriptor)
        {
            if (descriptor.HasExplicitSize())
                return Mathf.Max(1, descriptor.Height);

            return CameraDimensionUtility.ResolveCameraDimension(
                actualCameraDimension,
                cameraDimension,
                screenDimension);
        }

        private static void SetDepthTexture(MaterialPropertyBlock properties, Texture texture)
        {
            if (properties == null)
                return;

            if (texture is RenderTexture renderTexture
                && (renderTexture.depth > 0
                    || renderTexture.depthStencilFormat != GraphicsFormat.None))
            {
                properties.SetTexture(DepthTextureId, renderTexture, RenderTextureSubElement.Depth);
                return;
            }

            properties.SetTexture(DepthTextureId, texture);
        }

        private static bool HasUsableDepthTexture(Texture texture)
        {
            return texture != null
                && texture != Texture2D.whiteTexture;
        }

        private static bool HasValidAtmosphericScatteringLut(Texture texture)
        {
            if (texture == null
                || texture.dimension != TextureDimension.Tex3D
                || texture.width <= 1
                || texture.height <= 1)
            {
                return false;
            }

            if (texture is RenderTexture renderTexture)
                return renderTexture.volumeDepth > 1;

            return texture is Texture3D texture3D && texture3D.depth > 1;
        }

        private static bool HasValidSkyTexture(Texture texture)
        {
            if (texture == null
                || texture.dimension != TextureDimension.Cube
                || texture.width <= 0
                || texture.height <= 0)
            {
                return false;
            }

            return texture is not RenderTexture renderTexture || renderTexture.IsCreated();
        }

        private static bool HasValidVBuffer(Texture texture)
        {
            if (texture == null || texture.dimension != TextureDimension.Tex3D)
                return false;

            if (texture is RenderTexture renderTexture)
                return renderTexture.IsCreated() && renderTexture.volumeDepth > 1;

            return texture is Texture3D texture3D && texture3D.depth > 1;
        }

        private static RenderGraphTexture CreateSkyTexture(string name)
        {
            return new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 1,
                    Height = 1,
                    Dimension = TextureDimension.Cube,
                    ColorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    DepthBufferBits = DepthBits.None,
                    FilterMode = FilterMode.Trilinear,
                    WrapMode = TextureWrapMode.Clamp,
                    UseMipMap = true,
                    AutoGenerateMips = false,
                    Name = name
                }
            };
        }
    }
}

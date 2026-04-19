using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class DiffusionPass : UnsafePass, IPostProcessSourceOverridePass
    {
        internal const string DiffusionShaderName = "Hidden/VividRP/PostProcessing/Diffusion";

        private const int BlurHorizontalPassIndex = 0;
        private const int BlurVerticalPassIndex = 1;
        private const int MaxBlendPassIndex = 2;
        private const int MultiplyPassIndex = 3;
        private const int FilterPassIndex = 4;
        private const int CopyPassIndex = 5;

        private static readonly int MultiplyId = Shader.PropertyToID("_Multiply");
        private static readonly int FilterId = Shader.PropertyToID("_Filter");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int BlurScaleId = Shader.PropertyToID("_BlurScale");
        private static readonly int BlurIntensityId = Shader.PropertyToID("_BlurIntensity");
        private static readonly int BlurTextureId = Shader.PropertyToID("_BlurTexture");

        [RenderGraphResource(Access = AccessFlags.Read)]
        private RenderGraphTexture source = new();

        [RenderGraphResource(
            Name = "DiffusionTexture",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private RenderGraphTexture m_DiffusionTexture;

        [RenderGraphResource(
            Name = "DiffusionTemp1",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private RenderGraphTexture m_TempTexture1;

        [RenderGraphResource(
            Name = "DiffusionTemp2",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private RenderGraphTexture m_TempTexture2;

        [RenderGraphResource(
            Name = "DiffusionOutput",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        private Material m_Material;
        private DiffusionSettingsData m_Settings;
        private bool m_IsPassResourceLayoutDirty;
        private RenderGraphTexture m_OriginalSource;
        private bool m_HasSourceTextureOverride;

        public bool IsPassResourceLayoutDirty => m_IsPassResourceLayoutDirty;

        public DiffusionPass()
        {
            profilingSampler = new ProfilingSampler(nameof(DiffusionPass));

            m_DiffusionTexture = CreatePassOwnedTexture("DiffusionTexture", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_TempTexture1 = CreatePassOwnedTexture("DiffusionTemp1", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_TempTexture2 = CreatePassOwnedTexture("DiffusionTemp2", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_OutputTexture = CreatePassOwnedTexture("DiffusionOutput", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
        }

        public void ClearPassResourceLayoutDirty()
        {
            m_IsPassResourceLayoutDirty = false;
        }

        internal RenderGraphTexture GetSourceTexture()
        {
            return source;
        }

        internal void SetSourceTexture(RenderGraphTexture sourceTexture)
        {
            if (sourceTexture == null)
                throw new ArgumentNullException(nameof(sourceTexture));

            if (ReferenceEquals(source, sourceTexture))
                return;

            if (!m_HasSourceTextureOverride)
                m_OriginalSource = source;

            source = sourceTexture;
            m_HasSourceTextureOverride = true;
            m_IsPassResourceLayoutDirty = true;
        }

        internal void RestoreSourceTexture()
        {
            if (!m_HasSourceTextureOverride)
                return;

            if (!ReferenceEquals(source, m_OriginalSource) && m_OriginalSource != null)
            {
                source = m_OriginalSource;
                m_IsPassResourceLayoutDirty = true;
            }

            m_OriginalSource = null;
            m_HasSourceTextureOverride = false;
        }

        RenderGraphTexture IPostProcessSourceOverridePass.GetSourceTexture() => GetSourceTexture();

        void IPostProcessSourceOverridePass.SetSourceTexture(RenderGraphTexture sourceTexture) => SetSourceTexture(sourceTexture);

        void IPostProcessSourceOverridePass.RestoreSourceTexture() => RestoreSourceTexture();

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var shader = resources?.DiffusionShader;
            shader ??= Shader.Find(DiffusionShaderName);

            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{DiffusionShaderName}' for {nameof(DiffusionPass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Prepare(ContextContainer frameData)
        {

            var cameraData = frameData.Get<VividCameraData>();
            var camera = cameraData?.camera;
            var postProcessingAllowed = camera != null && CoreUtils.ArePostProcessesEnabled(camera);
            m_Settings = postProcessingAllowed
                ? DiffusionSettingsResolver.Resolve()
                : DiffusionSettingsData.CreateDefault();

            var sourceDescriptor = source.desc;
            var width = ResolveDimension(sourceDescriptor.Width, cameraData?.actualWidth ?? 0, cameraData?.pixelWidth ?? 0, Screen.width);
            var height = ResolveDimension(sourceDescriptor.Height, cameraData?.actualHeight ?? 0, cameraData?.pixelHeight ?? 0, Screen.height);

            ConfigureTexture(m_OutputTexture, sourceDescriptor, "DiffusionOutput", width, height, 1f);
            ConfigureTexture(m_TempTexture1, sourceDescriptor, "DiffusionTemp1", width, height, 1f);
            ConfigureTexture(m_TempTexture2, sourceDescriptor, "DiffusionTemp2", width, height, 1f);
            ConfigureTexture(
                m_DiffusionTexture,
                sourceDescriptor,
                "DiffusionTexture",
                Mathf.Max(1, width / 2),
                Mathf.Max(1, height / 2),
                0.5f);
        }

        public override void Record(UnsafeGraphContext context)
        {

            var sourceHandle = source.innerHandle;
            var outputHandle = m_OutputTexture.innerHandle;
            var unsafeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            // if (!m_Settings.enabled
            //     || m_DiffusionTexture?.innerHandle.IsValid() != true
            //     || m_TempTexture1?.innerHandle.IsValid() != true
            //     || m_TempTexture2?.innerHandle.IsValid() != true)
            // {
            //     Blit(unsafeCmd, context, sourceHandle, outputHandle, CopyPassIndex);
            //     return;
            // }

            using (new ProfilingScope(context.cmd, profilingSampler))
            {
                m_Material.SetFloat(MultiplyId, m_Settings.multiply);
                m_Material.SetFloat(FilterId, m_Settings.filter);
                m_Material.SetFloat(IntensityId, m_Settings.intensity);
                m_Material.SetFloat(BlurScaleId, m_Settings.blurScale);
                m_Material.SetFloat(BlurIntensityId, m_Settings.blurIntensity);

                Blit(unsafeCmd, context, sourceHandle, m_DiffusionTexture.innerHandle, MultiplyPassIndex);
                Blit(unsafeCmd, context, sourceHandle, m_TempTexture1.innerHandle, BlurHorizontalPassIndex);
                Blit(unsafeCmd, context, m_TempTexture1.innerHandle, m_TempTexture2.innerHandle, BlurVerticalPassIndex);

                if (m_Settings.mode == DiffusionMode.Max)
                {
                    Blit(unsafeCmd, context, sourceHandle, outputHandle, CopyPassIndex);
                    Blit(unsafeCmd, context, m_TempTexture2.innerHandle, outputHandle, MaxBlendPassIndex);
                }
                else
                {
                    m_Material.SetTexture(BlurTextureId, ResolveTexture(m_TempTexture2.innerHandle));
                    Blit(unsafeCmd, context, m_DiffusionTexture.innerHandle, outputHandle, FilterPassIndex);
                }
            }
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }

            m_IsPassResourceLayoutDirty = false;
        }

        internal RenderGraphTexture GetOutputTexture()
        {
            return m_OutputTexture;
        }

        private void Blit(
            CommandBuffer cmd,
            UnsafeGraphContext context,
            RTHandle sourceHandle,
            RTHandle destinationHandle,
            int materialPassIndex)
        {
            if (sourceHandle == null || destinationHandle == null)
                return;

            cmd.SetRenderTarget(destinationHandle);
            
            var scale = Vector2.one;

            if (sourceHandle.useScaling)
            {
                scale.x = sourceHandle.rtHandleProperties.rtHandleScale.x;
                scale.y = sourceHandle.rtHandleProperties.rtHandleScale.y;
            }

            Blitter.BlitTexture(
                cmd,
                sourceHandle,
                scale,
                m_Material,
                materialPassIndex);
        }

        private static Texture ResolveTexture(RTHandle handle)
        {
            return handle?.rt;
        }

        private static void ConfigureTexture(
            RenderGraphTexture texture,
            RenderGraphTextureDesc sourceDescriptor,
            string name,
            int width,
            int height,
            float scaleFactor)
        {
            if (texture?.desc == null)
                return;

            if (sourceDescriptor != null)
                texture.desc = sourceDescriptor.Clone();

            texture.desc.Name = name;
            texture.desc.Width = Mathf.Max(1, width);
            texture.desc.Height = Mathf.Max(1, height);
            texture.desc.ColorFormat = ResolveColorFormat(sourceDescriptor);
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.FilterMode = FilterMode.Bilinear;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.ClearBuffer = false;
            texture.desc.EnableRandomWrite = false;
            texture.desc.BindTextureMS = false;
            texture.desc.Slices = sourceDescriptor != null ? Mathf.Max(1, sourceDescriptor.Slices) : 1;
            texture.desc.Dimension = sourceDescriptor?.Dimension ?? TextureDimension.Tex2D;
            texture.desc.UseDynamicScale = sourceDescriptor?.UseDynamicScale ?? false;
            texture.desc.UseDynamicScaleExplicit = sourceDescriptor?.UseDynamicScaleExplicit ?? false;
            texture.desc.ScaleFactor = sourceDescriptor?.ScaleFactor ?? Vector2.one;

            if (Mathf.Approximately(scaleFactor, 1f))
                return;

            if (texture.desc.UseDynamicScale || texture.desc.UseDynamicScaleExplicit)
            {
                texture.desc.UseDynamicScaleExplicit = true;
                texture.desc.ScaleFactor = new Vector2(
                    Mathf.Max(0.001f, texture.desc.ScaleFactor.x * scaleFactor),
                    Mathf.Max(0.001f, texture.desc.ScaleFactor.y * scaleFactor));
            }
        }

        private static int ResolveDimension(int descriptorDimension, int actualCameraDimension, int cameraDimension, int screenDimension)
        {
            if (descriptorDimension > 0)
                return descriptorDimension;
            if (actualCameraDimension > 0)
                return actualCameraDimension;
            if (cameraDimension > 0)
                return cameraDimension;
            return Mathf.Max(1, screenDimension);
        }

        private static GraphicsFormat ResolveColorFormat(RenderGraphTextureDesc descriptor)
        {
            return descriptor != null && descriptor.ColorFormat != GraphicsFormat.None
                ? descriptor.ColorFormat
                : GraphicsFormat.R16G16B16A16_SFloat;
        }

        private static RenderGraphTexture CreatePassOwnedTexture(
            string name,
            int width,
            int height,
            GraphicsFormat format)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(width, height, format)
            };
            texture.desc.Name = name;
            texture.desc.ClearBuffer = false;
            texture.desc.FilterMode = FilterMode.Bilinear;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            return texture;
        }
    }
}

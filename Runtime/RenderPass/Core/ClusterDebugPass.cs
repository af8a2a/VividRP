using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class ClusterDebugPass : RasterPass
    {
        internal const string ClusterDebugShaderName = "Hidden/VividRP/ClusterDebug";

        private static readonly int SourceTextureId = Shader.PropertyToID("_SourceTexture");
        private static readonly int CameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int SourceTextureScaleBiasId = Shader.PropertyToID("_SourceTextureScaleBias");
        private static readonly int CameraDepthTextureScaleBiasId = Shader.PropertyToID("_CameraDepthTextureScaleBias");
        private static readonly int TileClusterDebugId = Shader.PropertyToID("_TileClusterDebug");
        private static readonly int ViewTilesFlagsId = Shader.PropertyToID("_ViewTilesFlags");
        private static readonly int ClusterDebugModeId = Shader.PropertyToID("_ClusterDebugMode");
        private static readonly int ClusterDebugDistanceId = Shader.PropertyToID("_ClusterDebugDistance");
        private static readonly int ClusterDebugLightViewportSizeId = Shader.PropertyToID("_ClusterDebugLightViewportSize");
        private static readonly int ClusterDebugMaxLightCountId = Shader.PropertyToID("_ClusterDebugMaxLightCount");

        [RenderGraphResource(Name = "SourceTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_SourceTexture;

        [RenderGraphResource(Name = "DepthTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(
            Name = "OutputTexture",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        private Material m_Material;
        private ClusterDebugSettingsData m_ResolvedSettings;
        private Vector4 m_ClusterDebugLightViewportSize = new(1f, 1f, 1f, 1f);

        internal readonly struct ClusterDebugSettingsData
        {
            public readonly TileClusterDebug tileClusterDebug;
            public readonly TileClusterCategoryDebug tileClusterDebugByCategory;
            public readonly ClusterDebugMode clusterDebugMode;
            public readonly float clusterDebugDistance;

            public ClusterDebugSettingsData(
                TileClusterDebug tileClusterDebug,
                TileClusterCategoryDebug tileClusterDebugByCategory,
                ClusterDebugMode clusterDebugMode,
                float clusterDebugDistance)
            {
                this.tileClusterDebug = tileClusterDebug;
                this.tileClusterDebugByCategory = tileClusterDebugByCategory;
                this.clusterDebugMode = clusterDebugMode;
                this.clusterDebugDistance = clusterDebugDistance;
            }
        }

        public ClusterDebugPass()
        {
            profilingSampler = new ProfilingSampler(nameof(ClusterDebugPass));

            m_SourceTexture = CreateInputTexture("SourceTexture");
            m_DepthTexture = CreateDepthTexture("DepthTexture");
            m_OutputTexture = CreateOutputTexture("OutputTexture");
            m_ResolvedSettings = new ClusterDebugSettingsData(
                TileClusterDebug.None,
                TileClusterCategoryDebug.Punctual,
                ClusterDebugMode.VisualizeOpaque,
                1f);
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var shader = resources?.ClusterDebugShader;
            shader ??= Shader.Find(ClusterDebugShaderName);

            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{ClusterDebugShaderName}' for {nameof(ClusterDebugPass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_ResolvedSettings = ResolveSettings(VividVolumeManagerUtility.GetClusterDebugVolume());

            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var width = ResolveOutputDimension(
                descriptor => descriptor.Width,
                cameraData.actualWidth,
                cameraData.pixelWidth,
                Screen.width,
                m_SourceTexture?.desc);
            var height = ResolveOutputDimension(
                descriptor => descriptor.Height,
                cameraData.actualHeight,
                cameraData.pixelHeight,
                Screen.height,
                m_SourceTexture?.desc);

            ConfigureOutputTexture(width, height, GetPreferredSourceDescriptor());
            m_ClusterDebugLightViewportSize = new Vector4(
                width,
                height,
                1f / Mathf.Max(1, width),
                1f / Mathf.Max(1, height));
        }

        public override void Record(RasterGraphContext context)
        {
            if (m_Material == null
                || !m_SourceTexture.innerHandle.IsValid()
                || !m_OutputTexture.innerHandle.IsValid())
            {
                return;
            }

            var sourceTexture = ResolveTexture(m_SourceTexture.innerHandle);
            if (sourceTexture == null)
                return;

            var depthTexture = ResolveTexture(m_DepthTexture.innerHandle) ?? Texture2D.whiteTexture;
            var tileClusterDebug = m_ResolvedSettings.tileClusterDebug;

            if (depthTexture == Texture2D.whiteTexture)
                tileClusterDebug = TileClusterDebug.None;

            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            mpb.SetTexture(SourceTextureId, sourceTexture);
            mpb.SetTexture(CameraDepthTextureId, depthTexture);
            mpb.SetVector(SourceTextureScaleBiasId, GetScaleBias(m_SourceTexture.innerHandle));
            mpb.SetVector(CameraDepthTextureScaleBiasId, GetScaleBias(m_DepthTexture.innerHandle));
            mpb.SetInt(TileClusterDebugId, (int)tileClusterDebug);
            mpb.SetInt(ViewTilesFlagsId, (int)m_ResolvedSettings.tileClusterDebugByCategory);
            mpb.SetInt(ClusterDebugModeId, (int)m_ResolvedSettings.clusterDebugMode);
            mpb.SetFloat(ClusterDebugDistanceId, m_ResolvedSettings.clusterDebugDistance);
            mpb.SetVector(ClusterDebugLightViewportSizeId, m_ClusterDebugLightViewportSize);
            mpb.SetFloat(ClusterDebugMaxLightCountId, VividRP.Runtime.LightGridPass.ClusterMaxLightsPerCluster);

            CoreUtils.DrawFullScreen(context.cmd, m_Material, mpb, 0);
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }
        }

        internal static ClusterDebugSettingsData ResolveSettings(ClusterDebugVolume volume)
        {
            var tileClusterDebug = TileClusterDebug.None;
            var tileClusterDebugByCategory = TileClusterCategoryDebug.Punctual;
            var clusterDebugMode = ClusterDebugMode.VisualizeOpaque;
            var clusterDebugDistance = 1f;

            if (volume == null || !volume.active)
            {
                return new ClusterDebugSettingsData(
                    tileClusterDebug,
                    tileClusterDebugByCategory,
                    clusterDebugMode,
                    clusterDebugDistance);
            }

            if (volume.tileClusterDebug != null && volume.tileClusterDebug.overrideState)
                tileClusterDebug = volume.tileClusterDebug.value;

            if (volume.tileClusterDebugByCategory != null && volume.tileClusterDebugByCategory.overrideState)
                tileClusterDebugByCategory = volume.tileClusterDebugByCategory.value;

            if (volume.clusterDebugMode != null && volume.clusterDebugMode.overrideState)
                clusterDebugMode = volume.clusterDebugMode.value;

            if (volume.clusterDebugDistance != null && volume.clusterDebugDistance.overrideState)
                clusterDebugDistance = Mathf.Max(0f, volume.clusterDebugDistance.value);

            return new ClusterDebugSettingsData(
                tileClusterDebug,
                tileClusterDebugByCategory,
                clusterDebugMode,
                clusterDebugDistance);
        }

        private void ConfigureOutputTexture(int width, int height, RenderGraphTextureDesc sourceDescriptor)
        {
            if (m_OutputTexture?.desc == null)
                return;

            m_OutputTexture.desc.Width = width;
            m_OutputTexture.desc.Height = height;
            m_OutputTexture.desc.ColorFormat = ResolveOutputFormat(sourceDescriptor);
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
            m_OutputTexture.desc.Name = "OutputTexture";

            if (sourceDescriptor == null)
                return;

            m_OutputTexture.desc.Dimension = sourceDescriptor.Dimension;
            m_OutputTexture.desc.Slices = Mathf.Max(1, sourceDescriptor.Slices);
            m_OutputTexture.desc.UseDynamicScale = sourceDescriptor.UseDynamicScale;
            m_OutputTexture.desc.UseDynamicScaleExplicit = sourceDescriptor.UseDynamicScaleExplicit;
            m_OutputTexture.desc.ScaleFactor = sourceDescriptor.ScaleFactor;
        }

        private RenderGraphTextureDesc GetPreferredSourceDescriptor()
        {
            if (HasExplicitSize(m_SourceTexture?.desc))
                return m_SourceTexture.desc;

            return m_SourceTexture?.desc;
        }

        private static int ResolveOutputDimension(
            System.Func<RenderGraphTextureDesc, int> selector,
            int actualCameraDimension,
            int cameraDimension,
            int screenDimension,
            params RenderGraphTextureDesc[] descriptors)
        {
            var resolved = 0;

            for (var i = 0; i < descriptors.Length; i++)
            {
                var descriptor = descriptors[i];
                if (!HasExplicitSize(descriptor))
                    continue;

                resolved = Mathf.Max(resolved, selector(descriptor));
            }

            if (resolved > 0)
                return resolved;

            return ResolveCameraDimension(actualCameraDimension, cameraDimension, screenDimension);
        }

        private static GraphicsFormat ResolveOutputFormat(RenderGraphTextureDesc sourceDescriptor)
        {
            if (sourceDescriptor != null && sourceDescriptor.ColorFormat != GraphicsFormat.None)
                return sourceDescriptor.ColorFormat;

            return GraphicsFormat.R8G8B8A8_UNorm;
        }

        private static Texture ResolveTexture(RTHandle handle)
        {
            if (handle == null)
                return null;

            if (handle.rt != null)
                return handle.rt;

            return handle.externalTexture;
        }

        private static bool HasExplicitSize(RenderGraphTextureDesc descriptor)
        {
            return descriptor != null
                && descriptor.Width > 0
                && descriptor.Height > 0
                && !(descriptor.Width == 1 && descriptor.Height == 1);
        }

        private static int ResolveCameraDimension(int actualCameraDimension, int cameraDimension, int screenDimension)
        {
            if (actualCameraDimension > 0)
                return actualCameraDimension;

            if (cameraDimension > 0)
                return cameraDimension;

            return Mathf.Max(1, screenDimension);
        }

        private static Vector4 GetScaleBias(RTHandle handle)
        {
            if (handle == null || !handle.useScaling)
                return new Vector4(1f, 1f, 0f, 0f);

            var scale = handle.rtHandleProperties.rtHandleScale;
            return new Vector4(scale.x, scale.y, 0f, 0f);
        }

        private static RenderGraphTexture CreateInputTexture(string name)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R8G8B8A8_UNorm)
            };
            texture.desc.Name = name;
            texture.desc.ClearBuffer = false;
            return texture;
        }

        private static RenderGraphTexture CreateDepthTexture(string name)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R32_SFloat)
            };
            texture.desc.Name = name;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.ClearBuffer = false;
            return texture;
        }

        private static RenderGraphTexture CreateOutputTexture(string name)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R8G8B8A8_UNorm)
            };
            texture.desc.Name = name;
            texture.desc.ClearBuffer = false;
            return texture;
        }
    }
}

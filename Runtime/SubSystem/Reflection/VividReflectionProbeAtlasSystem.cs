using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class VividReflectionProbeAtlasSystem : VividSubsystem<VividReflectionProbeAtlasSystem>
    {
        private static readonly int ReflectionAtlasId = Shader.PropertyToID("_ReflectionAtlas");
        private static readonly int ReflectionAtlasCubeDataId = Shader.PropertyToID("_ReflectionAtlasCubeData");
        private static readonly int ReflectionAtlasMipCountId = Shader.PropertyToID("_ReflectionAtlasMipCount");
        private static readonly int ReflectionAtlasSliceCountId = Shader.PropertyToID("_ReflectionAtlasSliceCount");

        private static ProfilingSampler m_ProfilingSampler = new ProfilingSampler("VividReflectionProbeAtlas");
        private VividReflectionProbeTextureCache m_TextureCache;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        private static void AutoInitialize()
        {
            Initialize();
        }

        internal static void ClearAtlas(CommandBuffer cmd)
        {
            RawInstance?.m_TextureCache?.Clear(cmd);
        }

        internal static bool TryGetAtlasDebugData(
            out Texture atlasTexture,
            out Vector2Int atlasDimensions,
            out int mipCount,
            out int sliceCount)
        {
            atlasTexture = null;
            atlasDimensions = Vector2Int.zero;
            mipCount = 0;
            sliceCount = 0;

            var textureCache = RawInstance?.m_TextureCache;
            atlasTexture = textureCache?.GetAtlasTexture();
            if (atlasTexture == null)
                return false;

            atlasDimensions = new Vector2Int(atlasTexture.width, atlasTexture.height);
            mipCount = textureCache.GetAtlasMipCount();
            sliceCount = textureCache.GetEnvSliceSize();
            return true;
        }

        protected override void OnInitialize()
        {
        }

        protected override void OnDeinitialize()
        {
            ReleaseTextureCache();
        }

        protected override void OnUpdate(ContextContainer frameData, CommandBuffer cmd)
        {
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                if (frameData == null)
                {
                    BindGlobalAtlas(cmd);
                    return;
                }

                var asset = VividRenderPipelineAsset.GetActiveAsset();
                EnsureTextureCache(asset);

                var lightData = frameData.GetOrCreate<VividLightData>();

                if (cmd == null)
                    return;

                if (m_TextureCache == null)
                {
                    BindGlobalAtlas(cmd);
                    return;
                }

                m_TextureCache.NewRender();
                m_TextureCache.NewFrame();
                lightData.UpdateReflectionProbeAtlasData(cmd, m_TextureCache);
                m_TextureCache.GarbageCollectTmpResources();
                BindGlobalAtlas(cmd);
            }
        }

        private void BindGlobalAtlas(CommandBuffer cmd)
        {
            if (cmd == null)
                return;

            if (m_TextureCache == null)
            {
                cmd.SetGlobalVector(ReflectionAtlasCubeDataId, Vector4.zero);
                cmd.SetGlobalInt(ReflectionAtlasMipCountId, 0);
                cmd.SetGlobalInt(ReflectionAtlasSliceCountId, 0);
                return;
            }

            cmd.SetGlobalTexture(ReflectionAtlasId, m_TextureCache.GetAtlasTexture());
            cmd.SetGlobalVector(ReflectionAtlasCubeDataId, m_TextureCache.GetTextureAtlasCubeData());
            cmd.SetGlobalInt(ReflectionAtlasMipCountId, m_TextureCache.GetAtlasMipCount());
            cmd.SetGlobalInt(ReflectionAtlasSliceCountId, m_TextureCache.GetEnvSliceSize());
        }

        private void EnsureTextureCache(VividRenderPipelineAsset asset)
        {
            var dimensions = asset != null
                ? asset.ReflectionProbeAtlasDimensions
                : VividReflectionProbeAtlasSettings.ResolveDimensions(
                    VividReflectionProbeAtlasResolution.Resolution4096x4096);
            var format = asset != null
                ? asset.ReflectionProbeAtlasGraphicsFormat
                : GraphicsFormat.R16G16B16A16_SFloat;
            var decreaseResToFit = asset == null || asset.ReflectionProbeAtlasDecreaseResToFit;
            var lastValidCubeMip = asset != null ? asset.ReflectionProbeAtlasLastValidCubeMip : 3;

            dimensions.x = Mathf.Max(512, dimensions.x);
            dimensions.y = Mathf.Max(512, dimensions.y);

            if (m_TextureCache != null
                && m_TextureCache.MatchesSettings(dimensions.x, dimensions.y, format, decreaseResToFit, lastValidCubeMip))
            {
                return;
            }

            ReleaseTextureCache();

            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_TextureCache = new VividReflectionProbeTextureCache(
                resources,
                dimensions.x,
                dimensions.y,
                format,
                decreaseResToFit,
                lastValidCubeMip);
        }

        private void ReleaseTextureCache()
        {
            m_TextureCache?.Dispose();
            m_TextureCache = null;
        }
    }
}

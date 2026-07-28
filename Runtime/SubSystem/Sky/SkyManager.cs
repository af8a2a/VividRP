using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    internal sealed class SkyManager : VividSubsystem<SkyManager>
    {
        private static readonly int SkyTextureId = Shader.PropertyToID("_SkyTexture");
        private static readonly int SkyTextureTintId = Shader.PropertyToID("_SkyTextureTint");
        private static readonly int SkyTextureParamsId = Shader.PropertyToID("_SkyTextureParams");
        private static readonly Dictionary<SkyType, ISkyRenderer> s_Renderers = new(new SkyTypeComparer());
        private static readonly VividSkyData s_CachedSkyData = new();
        private static readonly SkyAmbientProbeConvolution s_AmbientProbeConvolution = new();
        private static readonly SkySpecularCache s_SpecularCache = new();
        private const bool ForceAmbientProbeConvolutionEveryFrame = true;

        private static bool s_UpdateRequested;
        private static float s_LastUpdateTime;
        private static ISkyRenderer s_ActiveRenderer;
        private static ISkyRenderer s_PendingSkyRenderer;
        private static Camera s_PendingSkyCamera;
        private static int s_SkyUpdateVersion;
        private static int s_PendingSkyUpdateVersion = -1;

        protected override void OnInitialize()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            RegisterRenderer(new HDRISkyRenderer(), resources);
            RegisterRenderer(new PhysicallyBasedSkyRenderer(), resources);
            s_AmbientProbeConvolution.Build(resources);
            s_SpecularCache.Build(resources);
            s_CachedSkyData.Reset();
            s_LastUpdateTime = float.NegativeInfinity;
            s_UpdateRequested = false;
            s_ActiveRenderer = null;
            s_PendingSkyRenderer = null;
            s_PendingSkyCamera = null;
            s_SkyUpdateVersion = 0;
            s_PendingSkyUpdateVersion = -1;
        }

        protected override void OnDeinitialize()
        {
            foreach (var renderer in s_Renderers.Values)
                renderer.Dispose();

            s_AmbientProbeConvolution.Cleanup();
            s_SpecularCache.Dispose();
            s_Renderers.Clear();
            s_CachedSkyData.Reset();
            s_LastUpdateTime = float.NegativeInfinity;
            s_UpdateRequested = false;
            s_ActiveRenderer = null;
            s_PendingSkyRenderer = null;
            s_PendingSkyCamera = null;
            s_SkyUpdateVersion = 0;
            s_PendingSkyUpdateVersion = -1;
        }

        internal static void RequestUpdate()
        {
            s_UpdateRequested = true;
        }

        protected override void OnUpdate(ContextContainer frameData, CommandBuffer cmd)
        {
            using (RenderPassProfilingUtility.PrepareFrameSubsystemSkyMarker.Auto())
            {
                UpdateCore(frameData, cmd);
            }
        }

        private static void UpdateCore(ContextContainer frameData, CommandBuffer cmd)
        {
            if (frameData == null)
                return;

            if (!IsInitialized)
                Initialize();

            VividCameraData cameraData;
            VividSkyData skyData;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemSkyFrameDataMarker.Auto())
            {
                cameraData = frameData.GetOrCreate<VividCameraData>();
                skyData = frameData.GetOrCreate<VividSkyData>();
                skyData.Reset();

                s_SkyUpdateVersion++;
                s_PendingSkyRenderer = null;
                s_PendingSkyCamera = null;
                s_PendingSkyUpdateVersion = -1;
            }

            if (!ShouldUpdateGlobalSkyEnvironment(cameraData))
            {
                using (RenderPassProfilingUtility.PrepareFrameSubsystemSkyCopyToFrameMarker.Auto())
                {
                    CopyCachedSkyDataToFrame(skyData, cmd);
                }
                return;
            }

            SkySettingsVolume skySettings;
            SkyType activeSkyType;
            ISkyRenderer renderer = null;
            bool hasActiveSky;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemSkyActiveRendererMarker.Auto())
            {
                skySettings = VividVolumeManagerUtility.GetSkySettingsVolume();
                activeSkyType = skySettings?.skyType.value ?? SkyType.HDRI;
                hasActiveSky =
                    activeSkyType != SkyType.None
                    && s_Renderers.TryGetValue(activeSkyType, out renderer)
                    && renderer != null
                    && renderer.IsActive();
                s_ActiveRenderer = hasActiveSky ? renderer : null;
            }

            var forceRebuild = false;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemSkyRendererUpdateMarker.Auto())
            {
                if (hasActiveSky)
                {
                    SkyRendererContext context;
                    using (RenderPassProfilingUtility.PrepareFrameSubsystemSkyBuildContextMarker.Auto())
                    {
                        context = BuildRendererContext(frameData, cameraData, activeSkyType);
                    }

                    renderer.UpdateFrameResources(context, s_CachedSkyData, cmd);
                    var skyHash = renderer.GetSkyHash(context);

                    if (NeedsUpdate(activeSkyType, skySettings, skyHash, out forceRebuild))
                    {
                        s_CachedSkyData.Reset();
                        renderer.Update(context, s_CachedSkyData, cmd, skyHash, forceRebuild);
                        s_CachedSkyData.activeSkyType = activeSkyType;
                        s_CachedSkyData.skyHash = skyHash;
                        s_LastUpdateTime = Time.realtimeSinceStartup;
                        s_UpdateRequested = false;
                    }
                }
                else
                {
                    s_CachedSkyData.Reset();
                }
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemSkyEnvironmentMarker.Auto())
            {
                using (RenderPassProfilingUtility.PrepareFrameSubsystemSkyEnvironmentSpecularMarker.Auto())
                {
                    UpdateSpecularCubemap(cmd, s_CachedSkyData, forceRebuild);
                }

                using (RenderPassProfilingUtility.PrepareFrameSubsystemSkyEnvironmentDiffuseMarker.Auto())
                {
                    UpdateDiffuseAmbientProbe(cmd, s_CachedSkyData, forceRebuild);
                }

                using (RenderPassProfilingUtility.PrepareFrameSubsystemSkyEnvironmentGlobalsMarker.Auto())
                {
                    BindGlobalSkyTexture(cmd, s_CachedSkyData);
                }
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemSkyCopyToFrameMarker.Auto())
            {
                skyData.CopyFrom(s_CachedSkyData);
            }
        }

        internal static bool PrepareSkyInjection(
            ContextContainer frameData,
            RenderGraphTexture colorTarget,
            RenderGraphTexture depthTexture,
            RenderGraphTexture skyViewLut,
            RenderGraphTexture directionalShadowTexture,
            RenderGraphTexture csmShadowAtlas,
            bool hasConnectedCSMShadowAtlas)
        {
            if (frameData == null)
                return false;

            if (!IsInitialized)
                Initialize();

            var cameraData = frameData.GetOrCreate<VividCameraData>();
            if (!ShouldUpdateGlobalSkyEnvironment(cameraData))
                return false;

            if (s_ActiveRenderer == null)
                return false;

            var skyData = frameData.GetOrCreate<VividSkyData>();
            if (skyData.activeSkyType == SkyType.None)
                return false;

            var context = BuildRendererContext(frameData, cameraData, skyData.activeSkyType);
            var camera = context.cameraData?.camera;
            if (ReferenceEquals(s_PendingSkyCamera, camera)
                && s_PendingSkyUpdateVersion == s_SkyUpdateVersion)
            {
                return false;
            }

            s_ActiveRenderer.PrepareSkyRendering(
                context,
                skyData,
                colorTarget,
                depthTexture,
                skyViewLut,
                directionalShadowTexture,
                csmShadowAtlas,
                hasConnectedCSMShadowAtlas);

            s_PendingSkyRenderer = s_ActiveRenderer;
            s_PendingSkyCamera = camera;
            s_PendingSkyUpdateVersion = s_SkyUpdateVersion;
            return true;
        }

        internal static void RenderSkyInjection(UnsafePassContext context)
        {
            if (s_PendingSkyRenderer == null)
                return;

            s_PendingSkyRenderer.RenderSky(context);
            s_PendingSkyRenderer = null;
        }

        internal static RTHandle GetSpecularCubemapHandle()
        {
            if (!IsInitialized)
                Initialize();

            return s_SpecularCache.Cubemap;
        }

        internal static RTHandle GetSkySourceCubemapHandle()
        {
            if (!IsInitialized)
                Initialize();

            return s_SpecularCache.SourceCubemap;
        }

        internal static int GetSpecularCubemapMaxMip(VividSkyData skyData = null)
        {
            if (!IsInitialized)
                Initialize();

            var source = skyData?.specularCubemap;
            if (skyData != null && !s_SpecularCache.HasSource(source))
                return source != null ? Mathf.Max(0, source.mipmapCount - 1) : 0;

            return s_SpecularCache.MaxMipLevel;
        }

        internal static int GetSpecularCubemapResolution(VividSkyData skyData = null)
        {
            if (!IsInitialized)
                Initialize();

            var source = skyData?.specularCubemap;
            if (skyData != null && !s_SpecularCache.HasSource(source))
                return source != null ? Mathf.Max(1, source.width) : 1;

            return s_SpecularCache.Resolution;
        }

        internal static void ImportSpecularCubemap(RenderGraphTexture texture, VividSkyData skyData = null)
        {
            if (texture == null || !PassRecorder.IsPassTextureImportActive)
                return;

            RTHandle handle;
            if (skyData != null && HasValidSkyTexture(skyData.specularCubemap))
            {
                UpdateSpecularCubemap(skyData);
                handle = GetSpecularCubemapHandle();
            }
            else
            {
                handle = s_SpecularCache.FallbackCubemap;
            }

            if (handle != null)
                PassRecorder.ImportTexture(texture, handle);
        }

        internal static void ImportSkySourceCubemap(
            RenderGraphTexture texture,
            VividSkyData skyData = null)
        {
            if (texture == null || !PassRecorder.IsPassTextureImportActive)
                return;

            RTHandle handle;
            if (skyData != null && HasValidSkyTexture(skyData.specularCubemap))
            {
                UpdateSpecularCubemap(skyData);
                handle = GetSkySourceCubemapHandle();
            }
            else
            {
                handle = s_SpecularCache.FallbackCubemap;
            }

            if (handle != null)
                PassRecorder.ImportTexture(texture, handle);
        }

        private static bool NeedsUpdate(SkyType skyType, SkySettingsVolume skySettings, int skyHash, out bool forceRebuild)
        {
            forceRebuild = false;

            if (s_CachedSkyData.activeSkyType != skyType || !HasValidSkyTexture(s_CachedSkyData.specularCubemap))
                return true;

            if (s_UpdateRequested)
            {
                forceRebuild = true;
                return true;
            }

            var updateMode = skySettings?.updateMode.value ?? SkyUpdateMode.OnChanged;
            var updatePeriod = skySettings?.updatePeriod.value ?? 0.0f;
            var elapsedTime = Time.realtimeSinceStartup - s_LastUpdateTime;

            switch (updateMode)
            {
                case SkyUpdateMode.OnDemand:
                    return false;
                case SkyUpdateMode.Realtime:
                    if (skyHash != s_CachedSkyData.skyHash)
                        return true;
                    if (updatePeriod <= 0.0f || elapsedTime >= updatePeriod)
                    {
                        forceRebuild = true;
                        return true;
                    }
                    return false;
                default:
                    return skyHash != s_CachedSkyData.skyHash;
            }
        }

        private static void RegisterRenderer(ISkyRenderer renderer, VividRPCoreResources resources)
        {
            renderer.Build(resources);
            s_Renderers[renderer.Type] = renderer;
        }

        private static SkyRendererContext BuildRendererContext(
            ContextContainer frameData,
            VividCameraData cameraData,
            SkyType skyType)
        {
            if (skyType != SkyType.PhysicallyBased)
                return new SkyRendererContext(cameraData, null);

            var exposureData = frameData.Contains<VividExposureData>()
                ? frameData.Get<VividExposureData>()
                : null;

            return new SkyRendererContext(
                cameraData,
                frameData.GetOrCreate<VividLightData>(),
                exposureData);
        }

        private sealed class SkyTypeComparer : IEqualityComparer<SkyType>
        {
            public bool Equals(SkyType x, SkyType y)
            {
                return x == y;
            }

            public int GetHashCode(SkyType obj)
            {
                return (int)obj;
            }
        }

        private static bool ShouldUpdateGlobalSkyEnvironment(VividCameraData cameraData)
        {
            var camera = cameraData?.camera;
            if (camera == null)
                return true;

            return camera.cameraType != CameraType.Preview
                   && camera.cameraType != CameraType.Reflection
                   && !camera.isProcessingRenderRequest;
        }

        private static void CopyCachedSkyDataToFrame(VividSkyData skyData, CommandBuffer cmd)
        {
            skyData?.CopyFrom(s_CachedSkyData);
            s_AmbientProbeConvolution.BindGlobalBuffer(cmd, s_CachedSkyData.ambientProbeCubemap == null);
            BindGlobalSkyTexture(cmd, s_CachedSkyData);
        }

        private static void UpdateSpecularCubemap(CommandBuffer cmd, VividSkyData skyData, bool forceRebuild = false)
        {
            var source = skyData?.specularCubemap;
            s_SpecularCache.Update(
                cmd,
                source,
                ResolveSkyContentHash(skyData),
                ResolveSkyReflectionResolution(source),
                forceRebuild || (skyData?.specularCubemapDirty == true));
            if (skyData != null)
                skyData.specularCubemapDirty = false;
        }

        private static void UpdateSpecularCubemap(VividSkyData skyData)
        {
            var source = skyData?.specularCubemap;
            s_SpecularCache.Update(
                source,
                ResolveSkyContentHash(skyData),
                ResolveSkyReflectionResolution(source));
        }

        private static void UpdateDiffuseAmbientProbe(CommandBuffer cmd, VividSkyData skyData, bool forceRebuild)
        {
            var useDefaultAmbientProbe = skyData == null || skyData.ambientProbeCubemap == null;
            var fogParameters = BuildVolumetricAmbientProbeFogParameters();
            var ambientProbeHash = BuildVolumetricAmbientProbeHash(skyData?.ambientProbeHash ?? 0, fogParameters);
            if (!useDefaultAmbientProbe)
            {
                s_AmbientProbeConvolution.RequestUpdate(
                    cmd,
                    skyData.ambientProbeCubemap,
                    ambientProbeHash,
                    fogParameters,
                    forceRebuild || ForceAmbientProbeConvolutionEveryFrame);
            }

            s_AmbientProbeConvolution.BindGlobalBuffer(cmd, useDefaultAmbientProbe);
        }

        internal static bool HasValidSkyTexture(Texture texture)
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

        internal static int GetSkyTextureContentHash(Texture texture)
        {
            if (!HasValidSkyTexture(texture))
                return 0;

            unchecked
            {
                var hash = 17;
                hash = (hash * 397) ^ texture.imageContentsHash.GetHashCode();
                hash = (hash * 397) ^ texture.width;
                hash = (hash * 397) ^ texture.height;
                hash = (hash * 397) ^ texture.mipmapCount;
                hash = (hash * 397) ^ texture.graphicsFormat.GetHashCode();
                hash = (hash * 397) ^ texture.dimension.GetHashCode();
                return hash;
            }
        }

        private static int ResolveSkyContentHash(VividSkyData skyData)
        {
            if (skyData == null)
                return 0;

            return skyData.skyContentHash != 0
                ? skyData.skyContentHash
                : GetSkyTextureContentHash(skyData.specularCubemap);
        }

        private static int ResolveSkyReflectionResolution(Texture source)
        {
            var requestedResolution = SkySettingsVolume.GetSkyReflectionResolution(
                VividVolumeManagerUtility.GetSkySettingsVolume());
            return source != null
                ? Mathf.Clamp(requestedResolution, 1, Mathf.Max(1, source.width))
                : requestedResolution;
        }

        private static Vector4 BuildVolumetricAmbientProbeFogParameters()
        {
            var fog = VividVolumeManagerUtility.GetVolumetricFogVolume();
            if (fog == null || !fog.IsActive())
                return Vector4.zero;

            return new Vector4(
                Mathf.Max(fog.globalLightProbeDimmer.value, 0.0f),
                Mathf.Clamp(fog.anisotropy.value, -0.95f, 0.95f),
                0.0f,
                0.0f);
        }

        private static int BuildVolumetricAmbientProbeHash(int ambientProbeHash, Vector4 fogParameters)
        {
            unchecked
            {
                var hash = ambientProbeHash;
                hash = (hash * 397) ^ fogParameters.x.GetHashCode();
                hash = (hash * 397) ^ fogParameters.y.GetHashCode();
                hash = (hash * 397) ^ fogParameters.z.GetHashCode();
                hash = (hash * 397) ^ fogParameters.w.GetHashCode();
                return hash;
            }
        }

        private static void BindGlobalSkyTexture(CommandBuffer cmd, VividSkyData skyData)
        {
            if (cmd == null)
                return;

            var skyTextureHandle = GetSpecularCubemapHandle();
            if (skyTextureHandle != null)
                cmd.SetGlobalTexture(SkyTextureId, skyTextureHandle);

            var hasActiveSky = skyData != null && skyData.activeSkyType != SkyType.None;
            var skyTextureTint = hasActiveSky ? skyData.tint : Color.white;
            var skyTextureParams = BuildSkyTextureParams(
                hasActiveSky ? GetSpecularCubemapMaxMip(skyData) : 0,
                hasActiveSky ? skyData.exposure : 1.0f,
                hasActiveSky ? skyData.rotation : 0.0f,
                hasActiveSky);

            cmd.SetGlobalVector(SkyTextureTintId, skyTextureTint);
            cmd.SetGlobalVector(SkyTextureParamsId, skyTextureParams);
        }

        private static Vector4 BuildSkyTextureParams(int maxMip, float intensityMultiplier, float rotation, bool enabled)
        {
            return new Vector4(
                Mathf.Max(intensityMultiplier, 0.0f),
                rotation,
                Mathf.Max(0, maxMip),
                enabled ? 1.0f : 0.0f);
        }
    }
}

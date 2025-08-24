using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class LensFlareDataDrivenPass
    {
        Material material;

        private class LensFlarePassData
        {
            internal TextureHandle destinationTexture;
            internal UniversalCameraData cameraData;
            internal Material material;
            internal Rect viewport;
            internal float paniniDistance;
            internal float paniniCropToFit;
            internal float width;
            internal float height;
            internal bool usePanini;
        }

        void LensFlareDataDrivenComputeOcclusion(RenderGraph renderGraph, UniversalResourceData resourceData, UniversalCameraData cameraData)
        {
            if (!LensFlareCommonSRP.IsOcclusionRTCompatible())
                return;

            using (var builder = renderGraph.AddUnsafePass<LensFlarePassData>("Lens Flare Compute Occlusion", out var passData,
                       ProfilingSampler.Get(URPProfileId.LensFlareDataDrivenComputeOcclusion)))
            {
                RTHandle occH = LensFlareCommonSRP.occlusionRT;
                TextureHandle occlusionHandle = renderGraph.ImportTexture(LensFlareCommonSRP.occlusionRT);
                passData.destinationTexture = occlusionHandle;
                builder.UseTexture(occlusionHandle, AccessFlags.Write);
                passData.cameraData = cameraData;
                passData.viewport = cameraData.pixelRect;
                passData.material = material;
                passData.width = (float)cameraData.scaledWidth;
                passData.height = (float)cameraData.scaledHeight;
                var paniniProjection = VolumeManager.instance.stack.GetComponent<PaniniProjection>();
                if (paniniProjection.IsActive())
                {
                    passData.usePanini = true;
                    passData.paniniDistance = paniniProjection.distance.value;
                    passData.paniniCropToFit = paniniProjection.cropToFit.value;
                }
                else
                {
                    passData.usePanini = false;
                    passData.paniniDistance = 1.0f;
                    passData.paniniCropToFit = 1.0f;
                }

                builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);

                builder.SetRenderFunc(static (LensFlarePassData data, UnsafeGraphContext ctx) =>
                {
                    Camera camera = data.cameraData.camera;
                    XRPass xr = data.cameraData.xr;

                    Matrix4x4 nonJitteredViewProjMatrix0;
                    int xrId0;
#if ENABLE_VR && ENABLE_XR_MODULE
                    // Not VR or Multi-Pass
                    if (xr.enabled)
                    {
                        if (xr.singlePassEnabled)
                        {
                            nonJitteredViewProjMatrix0 = GL.GetGPUProjectionMatrix(data.cameraData.GetProjectionMatrixNoJitter(0), true) *
                                                         data.cameraData.GetViewMatrix(0);
                            xrId0 = 0;
                        }
                        else
                        {
                            var gpuNonJitteredProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                            nonJitteredViewProjMatrix0 = gpuNonJitteredProj * camera.worldToCameraMatrix;
                            xrId0 = data.cameraData.xr.multipassId;
                        }
                    }
                    else
                    {
                        nonJitteredViewProjMatrix0 = GL.GetGPUProjectionMatrix(data.cameraData.GetProjectionMatrixNoJitter(0), true) *
                                                     data.cameraData.GetViewMatrix(0);
                        xrId0 = 0;
                    }
#else
                        var gpuNonJitteredProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                        nonJitteredViewProjMatrix0 = gpuNonJitteredProj * camera.worldToCameraMatrix;
                        xrId0 = xr.multipassId;
#endif

                    LensFlareCommonSRP.ComputeOcclusion(
                        data.material, camera, xr, xr.multipassId,
                        data.width, data.height,
                        data.usePanini, data.paniniDistance, data.paniniCropToFit, true,
                        camera.transform.position,
                        nonJitteredViewProjMatrix0,
                        ctx.cmd,
                        false, false, null, null);


#if ENABLE_VR && ENABLE_XR_MODULE
                    if (xr.enabled && xr.singlePassEnabled)
                    {
                        //ctx.cmd.SetGlobalTexture(m_Depth.name, m_Depth.nameID);

                        for (int xrIdx = 1; xrIdx < xr.viewCount; ++xrIdx)
                        {
                            Matrix4x4 gpuVPXR = GL.GetGPUProjectionMatrix(data.cameraData.GetProjectionMatrixNoJitter(xrIdx), true) *
                                                data.cameraData.GetViewMatrix(xrIdx);

                            // Bypass single pass version
                            LensFlareCommonSRP.ComputeOcclusion(
                                data.material, camera, xr, xrIdx,
                                data.width, data.height,
                                data.usePanini, data.paniniDistance, data.paniniCropToFit, true,
                                camera.transform.position,
                                gpuVPXR,
                                ctx.cmd,
                                false, false, null, null);
                        }
                    }
#endif
                });
            }
        }


        static float GetLensFlareLightAttenuation(Light light, Camera cam, Vector3 wo)
        {
            // Must always be true
            if (light != null)
            {
                switch (light.type)
                {
                    case LightType.Directional:
                        return LensFlareCommonSRP.ShapeAttenuationDirLight(light.transform.forward, cam.transform.forward);
                    case LightType.Point:
                        return LensFlareCommonSRP.ShapeAttenuationPointLight();
                    case LightType.Spot:
                        return LensFlareCommonSRP.ShapeAttenuationSpotConeLight(light.transform.forward, wo, light.spotAngle, light.innerSpotAngle / 180.0f);
                    default:
                        return 1.0f;
                }
            }

            return 1.0f;
        }


        public TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source)
        {
            UniversalPostProcessingData postProcessingData = frameData.Get<UniversalPostProcessingData>();

            bool useLensFlare = !LensFlareCommonSRP.Instance.IsEmpty() && postProcessingData.supportDataDrivenLensFlare;

            if (!useLensFlare)
            {
                return source;
            }

            if (!material)
            {
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<PostProcessingRuntimeShader>();
                material = CoreUtils.CreateEngineMaterial(runtimeShader.lensFlareDataDriven);
            }

            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            LensFlareDataDrivenComputeOcclusion(renderGraph, resourceData, cameraData);

            var paniniProjection = VolumeManager.instance.stack.GetComponent<PaniniProjection>();


            using (var builder = renderGraph.AddUnsafePass<LensFlarePassData>("Lens Flare Data Driven Pass", out var passData,
                       ProfilingSampler.Get(URPProfileId.LensFlareDataDriven)))
            {
                // Use WriteTexture here because DoLensFlareDataDrivenCommon will call SetRenderTarget internally.
                // TODO RENDERGRAPH: convert SRP core lens flare to be rendergraph friendly
                passData.destinationTexture = source;
                builder.UseTexture(passData.destinationTexture, AccessFlags.Write);
                passData.cameraData = cameraData;
                passData.material = material;
                passData.width = (float)cameraData.scaledWidth;
                passData.height = (float)cameraData.scaledHeight;
                passData.viewport.x = 0.0f;
                passData.viewport.y = 0.0f;
                passData.viewport.width = (float)cameraData.scaledWidth;
                passData.viewport.height = (float)cameraData.scaledHeight;
                if (paniniProjection.IsActive())
                {
                    passData.usePanini = true;
                    passData.paniniDistance = paniniProjection.distance.value;
                    passData.paniniCropToFit = paniniProjection.cropToFit.value;
                }
                else
                {
                    passData.usePanini = false;
                    passData.paniniDistance = 1.0f;
                    passData.paniniCropToFit = 1.0f;
                }

                if (LensFlareCommonSRP.IsOcclusionRTCompatible())
                {
                    TextureHandle occlusionHandle = renderGraph.ImportTexture(LensFlareCommonSRP.occlusionRT);
                    builder.UseTexture(occlusionHandle, AccessFlags.Read);
                }
                else
                {
                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                }

                builder.SetRenderFunc(static (LensFlarePassData data, UnsafeGraphContext ctx) =>
                {
                    Camera camera = data.cameraData.camera;
                    XRPass xr = data.cameraData.xr;

#if ENABLE_VR && ENABLE_XR_MODULE
                    // Not VR or Multi-Pass
                    if (!xr.enabled ||
                        (xr.enabled && !xr.singlePassEnabled))
#endif
                    {
                        var gpuNonJitteredProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                        Matrix4x4 nonJitteredViewProjMatrix0 = gpuNonJitteredProj * camera.worldToCameraMatrix;

                        LensFlareCommonSRP.DoLensFlareDataDrivenCommon(
                            data.material, data.cameraData.camera, data.viewport, xr, data.cameraData.xr.multipassId,
                            data.width, data.height,
                            data.usePanini, data.paniniDistance, data.paniniCropToFit,
                            true,
                            camera.transform.position,
                            nonJitteredViewProjMatrix0,
                            ctx.cmd,
                            false, false, null, null,
                            data.destinationTexture,
                            GetLensFlareLightAttenuation,
                            false);
                    }
#if ENABLE_VR && ENABLE_XR_MODULE
                    else
                    {
                        for (int xrIdx = 0; xrIdx < xr.viewCount; ++xrIdx)
                        {
                            Matrix4x4 nonJitteredViewProjMatrix_k = GL.GetGPUProjectionMatrix(data.cameraData.GetProjectionMatrixNoJitter(xrIdx), true) *
                                                                    data.cameraData.GetViewMatrix(xrIdx);

                            LensFlareCommonSRP.DoLensFlareDataDrivenCommon(
                                data.material, data.cameraData.camera, data.viewport, xr, data.cameraData.xr.multipassId,
                                data.width, data.height,
                                data.usePanini, data.paniniDistance, data.paniniCropToFit,
                                true,
                                camera.transform.position,
                                nonJitteredViewProjMatrix_k,
                                ctx.cmd,
                                false, false, null, null,
                                data.destinationTexture,
                                GetLensFlareLightAttenuation,
                                false);
                        }
                    }
#endif
                });
            }

            return source;
        }
    }
}
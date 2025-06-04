using System.Collections.Generic;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Structure that keeps track of the ray tracing and path tracing effects that are enabled for a given camera.
    /// </summary>
    public struct RayTracedEffectsParameters
    {
        /// <summary>
        /// Specifies if ray traced shadows are active.
        /// </summary>
        public bool shadows;
        /// <summary>
        /// Specifies if ray traced ambient occlusion is active.
        /// </summary>
        public bool ambientOcclusion;
        /// <summary>
        /// Specifies the layer mask that will be used to evaluate ray traced ambient occlusion.
        /// </summary>
        public int aoLayerMask;
        /// <summary>
        /// Specifies if ray traced reflections are active.
        /// </summary>
        public bool reflections;
        /// <summary>
        /// Specifies the layer mask that will be used to evaluate ray traced reflections.
        /// </summary>
        public int reflLayerMask;
        /// <summary>
        /// Specifies if ray traced global illumination is active.
        /// </summary>
        public bool globalIllumination;
        /// <summary>
        /// Specifies the layer mask that will be used to evaluate ray traced global illumination.
        /// </summary>
        public int giLayerMask;
        /// <summary>
        /// Specifies if recursive rendering is active.
        /// </summary>
        public bool recursiveRendering;
        /// <summary>
        /// Specifies the layer mask that will be used to evaluate recursive rendering.
        /// </summary>
        public int recursiveLayerMask;
        /// <summary>
        /// Specifies if ray traced sub-surface scattering is active.
        /// </summary>
        public bool subSurface;
        /// <summary>
        /// Specifies if path tracing is active.
        /// </summary>
        public bool pathTracing;
        /// <summary>
        /// Specifies the layer mask that will be used to evaluate path tracing.
        /// </summary>
        public int ptLayerMask;
        /// <summary>
        /// Specifies if at least one ray tracing effect is enabled.
        /// </summary>
        public bool rayTracingRequired;
        /// <summary>
        /// Specifies if the visual effects should be included in the ray tracing acceleration structure.
        /// </summary>
        public bool includeVFX;
        /// <summary>
        /// Specifies the layer mask that characters will be used to evaluate ray traced shadows.
        /// </summary>
        public int characterShadowLayerMask;
    };

    /// <summary>
    /// RayTracingAccelerationStructure build System.
    /// Get it for per Camera.
    /// </summary>
    internal class RayTracingAccelerationStructureSystem
    {
        public RayTracingAccelerationStructure rtas = null;
        public RayTracingInstanceCullingConfig cullingConfig = new RayTracingInstanceCullingConfig();
        public List<RayTracingInstanceCullingTest> instanceTestArray = new List<RayTracingInstanceCullingTest>();
        internal Plane[] rtCullingPlaneArray = new Plane[6];

        // Culling tests
        RayTracingInstanceCullingTest ShT_CT = new RayTracingInstanceCullingTest();
        RayTracingInstanceCullingTest ShO_CT = new RayTracingInstanceCullingTest();
        RayTracingInstanceCullingTest AO_CT = new RayTracingInstanceCullingTest();
        RayTracingInstanceCullingTest Refl_CT = new RayTracingInstanceCullingTest();
        RayTracingInstanceCullingTest GI_CT = new RayTracingInstanceCullingTest();
        //RayTracingInstanceCullingTest RR_CT = new RayTracingInstanceCullingTest();
        //RayTracingInstanceCullingTest SSS_CT = new RayTracingInstanceCullingTest();
        //RayTracingInstanceCullingTest PT_CT = new RayTracingInstanceCullingTest();
        RayTracingInstanceCullingTest ShChar_CT = new RayTracingInstanceCullingTest();

        // Path tracing dirtiness parameters
        public bool transformsDirty;
        public bool materialsDirty;

        private Camera m_Camera;
        public void Initialize(Camera camera)
        {
            m_Camera = camera;

            // We only support perspective projection, so we flag the lod parameters as always non orthographic.
            cullingConfig.lodParameters.orthoSize = 0;
            cullingConfig.lodParameters.isOrthographic = false;

            // Opaque sub meshes need to be included and do not need to have their any hit enabled
            cullingConfig.subMeshFlagsConfig.opaqueMaterials = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;

            // Transparent sub meshes need to be included and we need the guarantee that they will trigger their any hit only once
            //cullingConfig.subMeshFlagsConfig.transparentMaterials = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.UniqueAnyHitCalls;
            cullingConfig.subMeshFlagsConfig.transparentMaterials = RayTracingSubMeshFlags.Disabled; // Disable transparent geometries.

            // Alpha tested sub meshes need to be included. (Note, not sure how it combines with transparency)
            cullingConfig.subMeshFlagsConfig.alphaTestedMaterials = RayTracingSubMeshFlags.Enabled;

            // Controls for the double sidedness
            cullingConfig.triangleCullingConfig.checkDoubleSidedGIMaterial = true;
            cullingConfig.triangleCullingConfig.frontTriangleCounterClockwise = false;
            cullingConfig.triangleCullingConfig.optionalDoubleSidedShaderKeywords = new string[1];
            cullingConfig.triangleCullingConfig.optionalDoubleSidedShaderKeywords[0] = "_DOUBLESIDED_ON";

            // Flags for the alpha testing, Use default queue.
            //cullingConfig.alphaTestedMaterialConfig.renderQueueLowerBound = HDRenderQueue.k_RenderQueue_OpaqueAlphaTest.lowerBound;
            //cullingConfig.alphaTestedMaterialConfig.renderQueueUpperBound = HDRenderQueue.k_RenderQueue_OpaqueAlphaTest.upperBound;
            cullingConfig.alphaTestedMaterialConfig.optionalShaderKeywords = new string[1];
            cullingConfig.alphaTestedMaterialConfig.optionalShaderKeywords[0] = "_ALPHATEST_ON";

            // Flags for the transparency, Use default queue.
            //cullingConfig.transparentMaterialConfig.renderQueueLowerBound = HDRenderQueue.k_RenderQueue_PreRefraction.lowerBound;
            //cullingConfig.transparentMaterialConfig.renderQueueUpperBound = HDRenderQueue.k_RenderQueue_Transparent.upperBound;
            cullingConfig.transparentMaterialConfig.optionalShaderKeywords = new string[1];
            cullingConfig.transparentMaterialConfig.optionalShaderKeywords[0] = "_SURFACE_TYPE_TRANSPARENT";

            // Flags that define which shaders to include (HDRP shaders only)
            cullingConfig.materialTest.requiredShaderTags = new RayTracingInstanceCullingShaderTagConfig[1];
            cullingConfig.materialTest.requiredShaderTags[0].tagId = new ShaderTagId("RayTracingRenderPipeline");
            cullingConfig.materialTest.requiredShaderTags[0].tagValueId = new ShaderTagId("DanbaidongRP");
            //cullingConfig.materialTest.deniedShaderPasses = DecalSystem.s_MaterialDecalPassNames;
            cullingConfig.instanceTests = new RayTracingInstanceCullingTest[9];

            // Setup the culling data for transparent shadows
            ShT_CT.allowOpaqueMaterials = true;
            ShT_CT.allowAlphaTestedMaterials = true;
            ShT_CT.allowTransparentMaterials = true;
            ShT_CT.layerMask = -1;
            ShT_CT.shadowCastingModeMask = (1 << (int)ShadowCastingMode.On) | (1 << (int)ShadowCastingMode.TwoSided) | (1 << (int)ShadowCastingMode.ShadowsOnly);
            ShT_CT.instanceMask = (uint)RayTracingRendererFlag.CastShadowTransparent;

            // Setup the culling data for opaque shadows
            ShO_CT.allowOpaqueMaterials = true;
            ShO_CT.allowAlphaTestedMaterials = true;
            ShO_CT.allowTransparentMaterials = false;
            ShO_CT.layerMask = -1;
            ShO_CT.shadowCastingModeMask = (1 << (int)ShadowCastingMode.On) | (1 << (int)ShadowCastingMode.TwoSided) | (1 << (int)ShadowCastingMode.ShadowsOnly);
            ShO_CT.instanceMask = (uint)RayTracingRendererFlag.CastShadowOpaque;

            // Setup the culling data for the ambient occlusion
            AO_CT.allowOpaqueMaterials = true;
            AO_CT.allowAlphaTestedMaterials = true;
            AO_CT.allowTransparentMaterials = false;
            AO_CT.layerMask = -1;
            AO_CT.shadowCastingModeMask = (1 << (int)ShadowCastingMode.Off) | (1 << (int)ShadowCastingMode.On) | (1 << (int)ShadowCastingMode.TwoSided);
            AO_CT.instanceMask = (uint)RayTracingRendererFlag.AmbientOcclusion;

            // Setup the culling data for the reflections
            Refl_CT.allowOpaqueMaterials = true;
            Refl_CT.allowAlphaTestedMaterials = true;
            Refl_CT.allowTransparentMaterials = false;
            Refl_CT.layerMask = -1;
            Refl_CT.shadowCastingModeMask = (1 << (int)ShadowCastingMode.Off) | (1 << (int)ShadowCastingMode.On) | (1 << (int)ShadowCastingMode.TwoSided);
            Refl_CT.instanceMask = (uint)RayTracingRendererFlag.Reflection;

            // Setup the culling data for the global illumination
            GI_CT.allowOpaqueMaterials = true;
            GI_CT.allowAlphaTestedMaterials = true;
            GI_CT.allowTransparentMaterials = false;
            GI_CT.layerMask = -1;
            GI_CT.shadowCastingModeMask = (1 << (int)ShadowCastingMode.Off) | (1 << (int)ShadowCastingMode.On) | (1 << (int)ShadowCastingMode.TwoSided);
            GI_CT.instanceMask = (uint)RayTracingRendererFlag.GlobalIllumination;

            // Setup the culling data for the recursive rendering
            //RR_CT.allowOpaqueMaterials = true;
            //RR_CT.allowAlphaTestedMaterials = true;
            //RR_CT.allowTransparentMaterials = true;
            //RR_CT.layerMask = -1;
            //RR_CT.shadowCastingModeMask = (1 << (int)ShadowCastingMode.Off) | (1 << (int)ShadowCastingMode.On) | (1 << (int)ShadowCastingMode.TwoSided);
            //RR_CT.instanceMask = (uint)RayTracingRendererFlag.RecursiveRendering;

            // Setup the culling data for the recursive rendering
            //RR_CT.allowOpaqueMaterials = true;
            //RR_CT.allowAlphaTestedMaterials = true;
            //RR_CT.allowTransparentMaterials = true;
            //RR_CT.layerMask = -1;
            //RR_CT.shadowCastingModeMask = (1 << (int)ShadowCastingMode.Off) | (1 << (int)ShadowCastingMode.On) | (1 << (int)ShadowCastingMode.TwoSided);
            //RR_CT.instanceMask = (uint)RayTracingRendererFlag.RecursiveRendering;

            // Setup the culling data for the SSS
            //SSS_CT.allowOpaqueMaterials = true;
            //SSS_CT.allowAlphaTestedMaterials = true;
            //SSS_CT.allowTransparentMaterials = false;
            //SSS_CT.layerMask = -1;
            //SSS_CT.shadowCastingModeMask = -1;
            //SSS_CT.instanceMask = (uint)RayTracingRendererFlag.Opaque;

            // Setup the culling data for the recursive rendering
            //PT_CT.allowOpaqueMaterials = true;
            //PT_CT.allowAlphaTestedMaterials = true;
            //PT_CT.allowTransparentMaterials = true;
            //PT_CT.layerMask = -1;
            //PT_CT.shadowCastingModeMask = (1 << (int)ShadowCastingMode.Off) | (1 << (int)ShadowCastingMode.On) | (1 << (int)ShadowCastingMode.TwoSided);
            //PT_CT.instanceMask = (uint)RayTracingRendererFlag.PathTracing;

            // Setup the culling data for character shadows
            ShChar_CT.allowOpaqueMaterials = true;
            ShChar_CT.allowAlphaTestedMaterials = true;
            ShChar_CT.allowTransparentMaterials = false;
            ShChar_CT.layerMask = 0;
            ShChar_CT.shadowCastingModeMask = (1 << (int)ShadowCastingMode.On) | (1 << (int)ShadowCastingMode.TwoSided) | (1 << (int)ShadowCastingMode.ShadowsOnly);
            ShChar_CT.instanceMask = (uint)RayTracingRendererFlag.CastShadowCharacter;
        }

        void SetupCullingData(bool pathTracingEnabled)
        {
            // Grab the ray tracing settings parameter
            RayTracingSettings rtSettings = VolumeManager.instance.stack.GetComponent<RayTracingSettings>();
            switch (rtSettings.cullingMode.value)
            {
                case RTASCullingMode.ExtendedFrustum:
                    {
                        // We'll be using an extension
                        cullingConfig.flags = RayTracingInstanceCullingFlags.EnablePlaneCulling;

                        // Build the culling plane data
                        Vector3 camerPosWS = m_Camera.transform.position;
                        Vector3 forward = m_Camera.transform.forward;
                        Vector3 right = m_Camera.transform.right;
                        Vector3 up = m_Camera.transform.up;

                        float far, height, width;
                        far = m_Camera.farClipPlane;
                        height = Mathf.Tan(Mathf.Deg2Rad * m_Camera.fieldOfView * 0.5f) * far;
                        float horizontalFov = Camera.VerticalToHorizontalFieldOfView(m_Camera.fieldOfView, m_Camera.aspect);
                        width = Mathf.Tan(Mathf.Deg2Rad * horizontalFov * 0.5f) * far;

                        // Front plane
                        rtCullingPlaneArray[0].normal = -forward;
                        rtCullingPlaneArray[0].distance = -Vector3.Dot(camerPosWS + forward * far, -forward);

                        // Back plane
                        rtCullingPlaneArray[1].normal = forward;
                        rtCullingPlaneArray[1].distance = -Vector3.Dot(camerPosWS - forward * far, forward);

                        // Right plane
                        rtCullingPlaneArray[2].normal = -right;
                        rtCullingPlaneArray[2].distance = -Vector3.Dot(camerPosWS + right * width, -right);

                        // Left plane
                        rtCullingPlaneArray[3].normal = right;
                        rtCullingPlaneArray[3].distance = -Vector3.Dot(camerPosWS - right * width, right);

                        // Top plane
                        rtCullingPlaneArray[4].normal = -up;
                        rtCullingPlaneArray[4].distance = -Vector3.Dot(camerPosWS + up * height, -up);

                        // Bottom plane
                        rtCullingPlaneArray[5].normal = up;
                        rtCullingPlaneArray[5].distance = -Vector3.Dot(camerPosWS - up * height, up);

                        // Set the planes
                        cullingConfig.planes = rtCullingPlaneArray;
                    }
                    break;
                case RTASCullingMode.Sphere:
                    {
                        // We use a sphere
                        cullingConfig.flags = RayTracingInstanceCullingFlags.EnableSphereCulling;
                        cullingConfig.sphereRadius = rtSettings.cullingDistance.value;
                        cullingConfig.sphereCenter = m_Camera.transform.position;
                    }
                    break;
                default:
                    {
                        // We explicitly want no culling.
                        cullingConfig.flags = RayTracingInstanceCullingFlags.None;
                    }
                    break;
            }

            // We want the LODs to match the rasterization and we want to exclude reflection probes
            cullingConfig.flags |= RayTracingInstanceCullingFlags.EnableLODCulling | RayTracingInstanceCullingFlags.IgnoreReflectionProbes;

            // Dirtiness need to be kept track of for the path tracing (when enabled)
            if (pathTracingEnabled)
                cullingConfig.flags |= RayTracingInstanceCullingFlags.ComputeMaterialsCRC;
        }

        public RayTracingInstanceCullingResults Cull(in RayTracedEffectsParameters parameters)
        {
            // The list of instanceTestArray needs to be cleared every frame as the list depends on the active effects and their parameters.
            instanceTestArray.Clear();

            // Set up the culling data
            SetupCullingData(parameters.pathTracing);

            // Set up the LOD flags
            cullingConfig.lodParameters.fieldOfView = m_Camera.fieldOfView;
            cullingConfig.lodParameters.cameraPosition = m_Camera.transform.position;
            cullingConfig.lodParameters.cameraPixelHeight = m_Camera.pixelHeight;

            // If we have path tracing, the shadow inclusion constraints must be aggregated with the layer masks of the path tracing.
            //if (parameters.pathTracing)
            //{
            //    ShO_CT.layerMask = parameters.ptLayerMask;
            //    ShT_CT.layerMask = parameters.ptLayerMask;
            //    ShO_CT.allowVisualEffects = false;
            //    ShT_CT.allowVisualEffects = false;
            //}

            if (parameters.shadows || parameters.pathTracing)
            {
                ShO_CT.allowVisualEffects = parameters.includeVFX;
                ShT_CT.allowVisualEffects = parameters.includeVFX;
                // We exclude character layer for self raytracing shadows.
                ShO_CT.layerMask = ~parameters.characterShadowLayerMask;
                ShT_CT.layerMask = ~parameters.characterShadowLayerMask;
                ShChar_CT.layerMask = parameters.characterShadowLayerMask;
                instanceTestArray.Add(ShO_CT);
                instanceTestArray.Add(ShT_CT);
                instanceTestArray.Add(ShChar_CT);
            }

            if (parameters.ambientOcclusion)
            {
                AO_CT.layerMask = parameters.aoLayerMask;
                AO_CT.allowVisualEffects = parameters.includeVFX;
                instanceTestArray.Add(AO_CT);
            }

            if (parameters.reflections)
            {
                Refl_CT.layerMask = parameters.reflLayerMask;
                Refl_CT.allowVisualEffects = false;
                instanceTestArray.Add(Refl_CT);
            }

            if (parameters.globalIllumination)
            {
                GI_CT.layerMask = parameters.giLayerMask;
                GI_CT.allowVisualEffects = parameters.includeVFX;
                instanceTestArray.Add(GI_CT);
            }

            //if (parameters.recursiveRendering)
            //{
            //    RR_CT.layerMask = parameters.recursiveLayerMask;
            //    RR_CT.allowVisualEffects = parameters.includeVFX;
            //    instanceTestArray.Add(RR_CT);
            //}

            //if (parameters.subSurface)
            //{
            //    SSS_CT.allowVisualEffects = parameters.includeVFX;
            //    instanceTestArray.Add(SSS_CT);
            //}

            //if (parameters.pathTracing)
            //{
            //    PT_CT.layerMask = parameters.ptLayerMask;
            //    PT_CT.allowVisualEffects = false;
            //    instanceTestArray.Add(PT_CT);
            //}

            // avoid reallocation uf previous instanceTests array is the same size as the current one
            if (cullingConfig.instanceTests.Length != instanceTestArray.Count)
                cullingConfig.instanceTests = instanceTestArray.ToArray();
            else
                instanceTestArray.CopyTo(0, cullingConfig.instanceTests, 0, instanceTestArray.Count);

            return rtas.CullInstances(ref cullingConfig);
        }


        public void Build()
        {
            // CameraRelativeRendering
            //rtas.Build(camera.transform.position);

            rtas.Build();
        }

        public void Reset()
        {
            // Clear all the per frame-data or allocate the rtas if it is the first time)
            if (rtas != null)
                rtas.ClearInstances();
            else
                rtas = new RayTracingAccelerationStructure();
        }

        public void Dispose()
        {
            if (rtas != null)
                rtas.Dispose();
        }

    }
}


using System.Collections.Generic;
using System.Linq;
using UnityEngine.VFX;

namespace UnityEngine.Rendering.Universal
{
    [GenerateHLSL]
    internal enum RayTracingRendererFlag
    {
        Opaque = 0x01,
        CastShadowTransparent = 0x02,
        CastShadowOpaque = 0x04,
        CastShadow = CastShadowOpaque | CastShadowTransparent,
        AmbientOcclusion = 0x08,
        Reflection = 0x10,
        GlobalIllumination = 0x20,

        //RecursiveRendering = 0x40,
        CastShadowCharacter = 0x40,
        PathTracing = 0x80,
        All = Opaque | CastShadow | AmbientOcclusion | Reflection | GlobalIllumination | CastShadowCharacter | PathTracing,
    }

    /// <summary>
    /// Flags returned when trying to add a renderer into the ray tracing acceleration structure.
    /// </summary>
    public enum AccelerationStructureStatus
    {
        /// <summary>Initial flag state.</summary>
        Clear = 0x0,

        /// <summary>Flag that indicates that the renderer was successfully added to the ray tracing acceleration structure.</summary>
        Added = 0x1,

        /// <summary>Flag that indicates that the renderer was excluded from the ray tracing acceleration structure.</summary>
        Excluded = 0x02,

        /// <summary>Flag that indicates that the renderer was added to the ray tracing acceleration structure, but it had transparent and opaque sub-meshes.</summary>
        TransparencyIssue = 0x04,

        /// <summary>Flag that indicates that the renderer was not included into the ray tracing acceleration structure because of a missing material</summary>
        NullMaterial = 0x08,

        /// <summary>Flag that indicates that the renderer was not included into the ray tracing acceleration structure because of a missing mesh</summary>
        MissingMesh = 0x10
    }

    /// <summary>
    /// Different from HDRP.
    /// RayTracing System will only handles tracing part, no denoiser, different denoiser system will add in the future.
    /// </summary>
    public class RayTracingSystem : CameraRelatedSystem<RayTracingSystem>
    {
        private RayTracingAccelerationStructure m_UserFedAccelerationStructure;
        private RayTracingAccelerationStructureSystem m_AccelerationStructureSystem;

        private ShaderVariablesRaytracing m_ShaderVariablesRayTracingCB = new ShaderVariablesRaytracing();

        bool m_ValidRayTracingState = false;
        bool m_ValidRayTracingCluster = false;
        bool m_ValidRayTracingClusterCulling = false;
        bool m_RayTracedShadowsRequired = false;
        bool m_RayTracedContactShadowsRequired = false;

        // Static variables used for the dirtiness and manual rtas management
        const int maxNumSubMeshes = 32;
        static RayTracingSubMeshFlags[] subMeshFlagArray = new RayTracingSubMeshFlags[maxNumSubMeshes];
        static uint[] vfxSystemMasks = new uint[maxNumSubMeshes];
        static List<Material> materialArray = new List<Material>(maxNumSubMeshes);
        static Dictionary<int, int> m_MaterialCRCs = new Dictionary<int, int>();


        #region NVAPI_SER

        public bool SupportSER = false;
        public bool SERSetup = false;

        private bool SetNvShaderExtnSlot;
        private ShaderExecutionReordering _shaderExecutionReordering;
        private GraphicsBuffer nvidiaExt;

        public GraphicsBuffer NVAPI_Buffer => nvidiaExt;
        #endregion

        public static bool SupportedCamera(Camera camera)
        {
            return camera.cameraType == CameraType.SceneView || camera.cameraType == CameraType.Game;
        }

        protected override void Initialize(Camera camera)
        {
            if (!SupportedCamera(camera))
            {
                Debug.LogError("Camera type " + camera.cameraType + " not supported RayTracing");
                return;
            }


            if (ExtensionSystem.RegisteredExtensions.TryGetValue(HardwareExtension.ShaderExecutionReordering,out var ser ))
            {
                SupportSER = true;
                _shaderExecutionReordering = ser as ShaderExecutionReordering;
                nvidiaExt = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4);
            }
            // Ray count system

            // light cluster system

            // RTAS system
            m_AccelerationStructureSystem = new RayTracingAccelerationStructureSystem();
            m_AccelerationStructureSystem.Initialize(camera);
        }

        public override void Dispose()
        {
            // Ray count system

            // light cluster system

            // RTAS system
            if (m_AccelerationStructureSystem != null)
            {
                m_AccelerationStructureSystem.Dispose();
                m_AccelerationStructureSystem = null;
            }

            NVAPI_Buffer?.Release();
        }

        static bool IsValidRayTracedMaterial(Material currentMaterial)
        {
            if (currentMaterial == null || currentMaterial.shader == null)
                return false;

            // For the time being, we only consider non-decal HDRP materials as valid
            return currentMaterial.GetTag("RenderPipeline", false) == "UniversalPipeline";
        }

        // TODO: ensure this queue.
        static bool IsTransparentMaterial(Material currentMaterial)
        {
            return currentMaterial.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") || currentMaterial.renderQueue >= ((int)RenderQueue.Transparent - 100);
        }

        static bool IsAlphaTestedMaterial(Material currentMaterial)
        {
            return currentMaterial.IsKeywordEnabled("_ALPHATEST_ON") || currentMaterial.renderQueue == (int)RenderQueue.AlphaTest;
        }

        private static bool UpdateMaterialCRC(int matInstanceId, int matCRC)
        {
            int matPrevCRC;
            if (m_MaterialCRCs.TryGetValue(matInstanceId, out matPrevCRC))
            {
                m_MaterialCRCs[matInstanceId] = matCRC;
                return (matCRC != matPrevCRC);
            }
            else
            {
                m_MaterialCRCs.Add(matInstanceId, matCRC);
                return true;
            }
        }

        /// <summary>
        /// Function that adds a renderer to a ray tracing acceleration structure.
        /// </summary>
        /// <param name="targetRTAS">Ray Tracing Acceleration structure the renderer should be added to.</param>
        /// <param name="currentRenderer">The renderer that should be added to the RTAS.</param>
        /// <param name="effectsParameters">Structure defining the enabled ray tracing and path tracing effects for a camera.</param>
        /// <param name="transformDirty">Flag that indicates if the renderer's transform has changed.</param>
        /// <param name="materialsDirty">Flag that indicates if any of the renderer's materials have changed.</param>
        /// <returns></returns>
        public static AccelerationStructureStatus AddInstanceToRAS(RayTracingAccelerationStructure targetRTAS, Renderer currentRenderer,
            RayTracedEffectsParameters effectsParameters, ref bool transformDirty, ref bool materialsDirty)
        {
            if (currentRenderer is VFXRenderer vfxRenderer)
                return AddVFXInstanceToRAS(targetRTAS, vfxRenderer, effectsParameters, ref transformDirty, ref materialsDirty);
            return AddRegularInstanceToRAS(targetRTAS, currentRenderer, effectsParameters, ref transformDirty, ref materialsDirty);
        }

        private static AccelerationStructureStatus AddRegularInstanceToRAS(RayTracingAccelerationStructure targetRTAS,
            Renderer currentRenderer, RayTracedEffectsParameters effectsParameters, ref bool transformDirty, ref bool materialsDirty)
        {
            // For every sub-mesh/sub-material let's build the right flags
            int numSubMeshes = 1;
            if (currentRenderer.GetType() == typeof(SkinnedMeshRenderer))
            {
                SkinnedMeshRenderer skinnedMesh = (SkinnedMeshRenderer)currentRenderer;
                if (skinnedMesh.sharedMesh == null) return AccelerationStructureStatus.MissingMesh;
                currentRenderer.GetSharedMaterials(materialArray);
                numSubMeshes = skinnedMesh.sharedMesh.subMeshCount;
            }
            else
            {
                currentRenderer.TryGetComponent(out MeshFilter meshFilter);
                if (meshFilter == null || meshFilter.sharedMesh == null) return AccelerationStructureStatus.MissingMesh;
                currentRenderer.GetSharedMaterials(materialArray);
                numSubMeshes = meshFilter.sharedMesh.subMeshCount;
            }

            // If the material array is null, we are done
            if (materialArray == null) return AccelerationStructureStatus.NullMaterial;

            // Let's clamp the number of sub-meshes to avoid throwing an unwanted error
            numSubMeshes = Mathf.Min(numSubMeshes, maxNumSubMeshes);

            bool doubleSided = false;
            bool materialIsOnlyTransparent = true;
            bool hasTransparentSubMaterial = false;
            for (int subGeomIdx = 0; subGeomIdx < numSubMeshes; ++subGeomIdx)
            {
                // Initially we consider the potential mesh as invalid
                bool validMesh = false;
                if (materialArray.Count > subGeomIdx)
                {
                    // Grab the material for the current sub-mesh
                    Material currentMaterial = materialArray[subGeomIdx];

                    // Make sure that the material is HDRP's and non-decal
                    if (IsValidRayTracedMaterial(currentMaterial))
                    {
                        // Mesh is valid given that all requirements are ok
                        validMesh = true;

                        // Evaluate what kind of materials we are dealing with
                        bool alphaTested = IsAlphaTestedMaterial(currentMaterial);
                        bool transparentMaterial = IsTransparentMaterial(currentMaterial);

                        // Aggregate the transparency info
                        materialIsOnlyTransparent &= transparentMaterial;
                        hasTransparentSubMaterial |= transparentMaterial;

                        ComputeSubMeshFlag(subGeomIdx, transparentMaterial, alphaTested, currentMaterial, ref doubleSided);

                        // Check if the material has changed since last time we were here
                        if (!materialsDirty)
                        {
                            materialsDirty |= UpdateMaterialCRC(currentMaterial.GetInstanceID(), currentMaterial.ComputeCRC());
                        }
                    }
                }

                // If the mesh was not valid, exclude it (without affecting sidedness)
                if (!validMesh)
                    subMeshFlagArray[subGeomIdx] = RayTracingSubMeshFlags.Disabled;
            }

            // If the material is considered opaque, every sub-mesh has to be enabled and with unique any hit calls
            if (!materialIsOnlyTransparent && hasTransparentSubMaterial)
                for (int meshIdx = 0; meshIdx < numSubMeshes; ++meshIdx)
                    subMeshFlagArray[meshIdx] = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.UniqueAnyHitCalls;

            // We need to build the instance flag for this renderer
            uint instanceFlag = ComputeInstanceFlag(currentRenderer, currentRenderer.shadowCastingMode, effectsParameters,
                hasTransparentSubMaterial, materialIsOnlyTransparent);

            // If the object was not referenced
            if (instanceFlag == 0) return AccelerationStructureStatus.Added;

            targetRTAS.AddInstance(currentRenderer, subMeshFlags: subMeshFlagArray, enableTriangleCulling: !doubleSided,
                mask: instanceFlag);

            // Indicates that a transform has changed in our scene (mesh or light)
            transformDirty |= currentRenderer.transform.hasChanged;
            currentRenderer.transform.hasChanged = false;

            // return the status
            return (!materialIsOnlyTransparent && hasTransparentSubMaterial)
                ? AccelerationStructureStatus.TransparencyIssue
                : AccelerationStructureStatus.Added;
        }

        private static AccelerationStructureStatus AddVFXInstanceToRAS(RayTracingAccelerationStructure targetRTAS, VFXRenderer currentRenderer,
            RayTracedEffectsParameters effectsParameters, ref bool transformDirty, ref bool materialsDirty)
        {
            // If we should exclude Visual effects, skip right now
            if (!effectsParameters.includeVFX)
                return AccelerationStructureStatus.Excluded;

            currentRenderer.GetSharedMaterials(materialArray);
            int numSubGeom = materialArray.Count;

            // If the material array is null, we are done
            if (materialArray == null) return AccelerationStructureStatus.NullMaterial;

            // Let's clamp the number of sub-meshes to avoid throwing an unwanted error
            numSubGeom = Mathf.Min(numSubGeom, maxNumSubMeshes);

            bool materialIsOnlyTransparent = true;
            bool hasTransparentSubMaterial = false;
            int compactedSubGeomIndex = 0; // For VFX, we expect no holes in the system mask array
            bool hasAnyValidInstance = false;
            for (int subGeomIdx = 0; subGeomIdx < numSubGeom; ++subGeomIdx)
            {
                if (materialArray.Count > subGeomIdx)
                {
                    // Grab the material for the current sub-mesh
                    Material currentMaterial = materialArray[subGeomIdx];

                    // Make sure that the material is HDRP's and non-decal
                    if (IsValidRayTracedMaterial(currentMaterial))
                    {
                        // Evaluate what kind of materials we are dealing with
                        bool transparentMaterial = IsTransparentMaterial(currentMaterial);

                        // Aggregate the transparency info
                        materialIsOnlyTransparent &= transparentMaterial;
                        hasTransparentSubMaterial |= transparentMaterial;

                        ShadowCastingMode shadowCastingMode = currentMaterial.FindPass("ShadowCaster") != -1 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                        uint instanceFlag = ComputeInstanceFlag(currentRenderer, shadowCastingMode, effectsParameters, transparentMaterial,
                            transparentMaterial);
                        hasAnyValidInstance |= (instanceFlag != 0);
                        vfxSystemMasks[compactedSubGeomIndex] = instanceFlag;
                        compactedSubGeomIndex++;

                        // Check if the material has changed since last time we were here
                        if (!materialsDirty)
                        {
                            materialsDirty |= UpdateMaterialCRC(currentMaterial.GetInstanceID(), currentMaterial.ComputeCRC());
                        }
                    }
                }
            }

            // If the object was not referenced
            if (!hasAnyValidInstance) return AccelerationStructureStatus.Added;

            // Add it to the acceleration structure
            targetRTAS.AddVFXInstances(currentRenderer, vfxSystemMasks);

            // Indicates that a transform has changed in our scene (mesh or light)
            transformDirty |= currentRenderer.transform.hasChanged;
            currentRenderer.transform.hasChanged = false;

            // return the status
            return (!materialIsOnlyTransparent && hasTransparentSubMaterial)
                ? AccelerationStructureStatus.TransparencyIssue
                : AccelerationStructureStatus.Added;
        }

        private static void ComputeSubMeshFlag(int meshIdx, bool transparentMaterial, bool alphaTested,
            Material currentMaterial,
            ref bool doubleSided)
        {
            // Mark the thing as valid
            subMeshFlagArray[meshIdx] = RayTracingSubMeshFlags.Enabled;

            // Append the additional flags depending on what kind of sub mesh this is
            if (!transparentMaterial && !alphaTested)
                subMeshFlagArray[meshIdx] |= RayTracingSubMeshFlags.ClosestHitOnly;
            else if (transparentMaterial)
                subMeshFlagArray[meshIdx] |= RayTracingSubMeshFlags.UniqueAnyHitCalls;

            // Check if we want to enable double-sidedness for the mesh
            // (note that a mix of single and double-sided materials will result in a double-sided mesh in the AS)
            doubleSided |= currentMaterial.doubleSidedGI || currentMaterial.IsKeywordEnabled("_DOUBLESIDED_ON");
        }

        private static uint ComputeInstanceFlag(Renderer currentRenderer, ShadowCastingMode shadowCastingMode, RayTracedEffectsParameters effectsParameters,
            bool hasTransparentSubMaterial, bool materialIsOnlyTransparent)
        {
            uint instanceFlag = 0x00;

            // Get the layer of this object
            int objectLayerValue = 1 << currentRenderer.gameObject.layer;

            // We disregard the ray traced shadows option when in Path Tracing
            bool rayTracedShadow = effectsParameters.shadows && !effectsParameters.pathTracing;

            // Deactivate Path Tracing if the object does not belong to the path traced layer(s)
            bool pathTracing = effectsParameters.pathTracing && (bool)((effectsParameters.ptLayerMask & objectLayerValue) != 0);

            // Propagate the opacity mask only if all sub materials are opaque
            bool isOpaque = !hasTransparentSubMaterial;
            if (isOpaque)
            {
                instanceFlag |= (uint)(RayTracingRendererFlag.Opaque);
            }

            if (rayTracedShadow || pathTracing)
            {
                if (hasTransparentSubMaterial)
                {
                    // Raise the shadow casting flag if needed
                    instanceFlag |= ((shadowCastingMode != ShadowCastingMode.Off)
                        ? (uint)(RayTracingRendererFlag.CastShadowTransparent)
                        : 0x00);
                }
                else
                {
                    // Raise the shadow casting flag if needed
                    instanceFlag |= ((shadowCastingMode != ShadowCastingMode.Off)
                        ? (uint)(RayTracingRendererFlag.CastShadowOpaque)
                        : 0x00);
                }

                instanceFlag |= ((effectsParameters.characterShadowLayerMask & objectLayerValue) != 0)
                    ? (uint)(RayTracingRendererFlag.CastShadowCharacter)
                    : 0x00;
            }

            // We consider a mesh visible by reflection, gi, etc if it is not in the shadow only mode.
            bool meshIsVisible = shadowCastingMode != ShadowCastingMode.ShadowsOnly;

            if (effectsParameters.ambientOcclusion && !materialIsOnlyTransparent && meshIsVisible)
            {
                // Raise the Ambient Occlusion flag if needed
                instanceFlag |= ((effectsParameters.aoLayerMask & objectLayerValue) != 0)
                    ? (uint)(RayTracingRendererFlag.AmbientOcclusion)
                    : 0x00;
            }

            if (effectsParameters.reflections && !materialIsOnlyTransparent && meshIsVisible)
            {
                // Raise the Screen Space Reflection flag if needed
                instanceFlag |= ((effectsParameters.reflLayerMask & objectLayerValue) != 0)
                    ? (uint)(RayTracingRendererFlag.Reflection)
                    : 0x00;
            }

            if (effectsParameters.globalIllumination && !materialIsOnlyTransparent && meshIsVisible)
            {
                // Raise the Global Illumination flag if needed
                instanceFlag |= ((effectsParameters.giLayerMask & objectLayerValue) != 0)
                    ? (uint)(RayTracingRendererFlag.GlobalIllumination)
                    : 0x00;
            }

            //if (effectsParameters.recursiveRendering && meshIsVisible)
            //{
            //    // Raise the Recursive Rendering flag if needed
            //    instanceFlag |= ((effectsParameters.recursiveLayerMask & objectLayerValue) != 0)
            //        ? (uint)(RayTracingRendererFlag.RecursiveRendering)
            //        : 0x00;
            //}

            if (effectsParameters.pathTracing && meshIsVisible)
            {
                // Raise the Path Tracing flag if needed
                instanceFlag |= (uint)(RayTracingRendererFlag.PathTracing);
            }

            return instanceFlag;
        }


        public static RayTracedEffectsParameters EvaluateEffectsParameters(VolumeStack volumeStack, bool rayTracedShadows, bool rayTracedContactShadows)
        {
            RayTracedEffectsParameters parameters = new RayTracedEffectsParameters();

            // Aggregate the shadow requirements
            Shadows shadowSettings = volumeStack.GetComponent<Shadows>();
            bool opaqueShadows = shadowSettings.rayTracing.value;
            // bool transparentReflections = shadowSettings.enabledTransparent.value;
            parameters.shadows = opaqueShadows;
            parameters.characterShadowLayerMask = shadowSettings.characterLayerMask.value;

            
            // Aggregate the ambient occlusion parameters
            
            // Aggregate the reflections parameters
            ScreenSpaceReflection reflSettings = volumeStack.GetComponent<ScreenSpaceReflection>();
            bool opaqueReflections = reflSettings.enabled.value;
            bool transparentReflections = reflSettings.enabledTransparent.value;
            parameters.reflections = ScreenSpaceReflection.RayTracingActive(reflSettings) && (opaqueReflections || transparentReflections);
            parameters.reflLayerMask = reflSettings.layerMask.value;

            // Aggregate the global illumination parameters


            var aoSetting = volumeStack.GetComponent<RaytracingAmbientOcclusion>();

            parameters.ambientOcclusion = aoSetting.IsActive();


            // We need to check if at least one effect will require the acceleration structure
            parameters.rayTracingRequired = parameters.ambientOcclusion || parameters.reflections
                                                                        || parameters.globalIllumination || parameters.shadows;
            // Return the result
            return parameters;
        }

        internal void BuildRayTracingAccelerationStructure()
        {
            // Resets the rtas
            m_AccelerationStructureSystem.Reset();

            // Reset all the flags
            m_ValidRayTracingState = false;
            m_ValidRayTracingCluster = false;
            m_ValidRayTracingClusterCulling = false;
            m_RayTracedShadowsRequired = false;
            m_RayTracedContactShadowsRequired = false;

            // Only build SceneView and GameView.
            if (camera.cameraType != CameraType.SceneView && camera.cameraType != CameraType.Game)
                return;

            // TODO: Light information?

            var volumeStack = VolumeManager.instance.stack;

            var effectParameters = EvaluateEffectsParameters(volumeStack, m_RayTracedShadowsRequired, m_RayTracedContactShadowsRequired);

            if (!effectParameters.rayTracingRequired)
                return;

            // Grab the ray tracing settings
            RayTracingSettings rtSettings = volumeStack.GetComponent<RayTracingSettings>();

#if UNITY_EDITOR
            if (rtSettings.buildMode.value == RTASBuildMode.Automatic || camera.cameraType == CameraType.SceneView)
#else
            if (rtSettings.buildMode.value == RTASBuildMode.Automatic)
#endif
            {
                // Cull the scene for the RTAS
                RayTracingInstanceCullingResults cullingResults = m_AccelerationStructureSystem.Cull(effectParameters);

                // Update the material dirtiness for the PT
                if (effectParameters.pathTracing)
                {
                    m_AccelerationStructureSystem.transformsDirty |= cullingResults.transformsChanged;
                    for (int i = 0; i < cullingResults.materialsCRC.Length; i++)
                    {
                        RayTracingInstanceMaterialCRC matCRC = cullingResults.materialsCRC[i];
                        m_AccelerationStructureSystem.materialsDirty |= UpdateMaterialCRC(matCRC.instanceID, matCRC.crc);
                    }
                }

                // Build the ray tracing acceleration structure
                m_AccelerationStructureSystem.Build();

                // tag the structures as valid
                m_ValidRayTracingState = true;
            }
            else
            {
                // If the user fed a non null ray tracing acceleration structure, then we are all set.
                if (m_UserFedAccelerationStructure != null)
                    m_ValidRayTracingState = true;
            }
        }

        // TODO:
        internal bool RayTracingLightClusterRequired()
        {
            return false;
        }

        // TODO:
        internal void CullForRayTracing(CommandBuffer cmd)
        {
            if (m_ValidRayTracingState && RayTracingLightClusterRequired())
            {
                //m_RayTracingLightCluster.CullForRayTracing();
                m_ValidRayTracingClusterCulling = true;
            }
        }

        // TODO: Delete
        internal bool GetCullingState()
        {
            return m_ValidRayTracingClusterCulling;
        }

        internal bool GetRayTracingState()
        {
            return m_ValidRayTracingState;
        }

        internal bool GetRayTracingClusterState()
        {
            return m_ValidRayTracingCluster;
        }

        internal RayTracingAccelerationStructure RequestAccelerationStructure()
        {
            if (m_ValidRayTracingState)
            {
                RayTracingSettings rtSettings = VolumeManager.instance.stack.GetComponent<RayTracingSettings>();
#if UNITY_EDITOR
                if (rtSettings.buildMode.value == RTASBuildMode.Automatic || camera.cameraType == CameraType.SceneView)
#else
                if (rtSettings.buildMode.value == RTASBuildMode.Automatic)
#endif
                    return m_AccelerationStructureSystem.rtas;
                else
                    return m_UserFedAccelerationStructure;
            }

            return null;
        }


        internal ShaderVariablesRaytracing GetShaderVariablesRaytracingCB(Vector2Int pixelSize, RayTracingSettings rayTracingSettings)
        {
            m_ShaderVariablesRayTracingCB._RayTracingRayBias = rayTracingSettings.rayBias.value;
            m_ShaderVariablesRayTracingCB._RayTracingDistantRayBias = rayTracingSettings.distantRayBias.value;
            m_ShaderVariablesRayTracingCB._RayCountEnabled = 0;
            m_ShaderVariablesRayTracingCB._RaytracingCameraNearPlane = camera.nearClipPlane;
            m_ShaderVariablesRayTracingCB._RaytracingPixelSpreadAngle = RenderingUtilsExt.GetPixelSpreadAngle(camera.fieldOfView, pixelSize.x, pixelSize.y);
            m_ShaderVariablesRayTracingCB._DirectionalShadowFallbackIntensity = rayTracingSettings.directionalShadowFallbackIntensity.value;
            m_ShaderVariablesRayTracingCB._RayTracingLodBias = 0;

            return m_ShaderVariablesRayTracingCB;
        }

        public static readonly int _ShaderVariablesRaytracing = Shader.PropertyToID("ShaderVariablesRaytracing");
    }
}
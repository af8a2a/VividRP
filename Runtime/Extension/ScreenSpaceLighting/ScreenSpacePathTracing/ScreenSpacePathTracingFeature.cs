using System.Reflection;

namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature]

    public class ScreenSpacePathTracingFeature : ScriptableRendererFeature
    {
        public enum Accumulation
        {
            [InspectorName("Disable")] [Tooltip("Disable accumulation.")]
            None = 0,

            [InspectorName("Offline")] [Tooltip("Offline mode provides the best quality.")]
            Camera = 1,

            [InspectorName("Real-time")] [Tooltip("Real-time mode will only execute in play mode.")]
            PerObject = 2,

            [InspectorName("Real-time + Spatial Denoise")]
            [Tooltip("Real-time + Spatial Denoise mode will only execute in play mode.")]
            PerObjectBlur = 3
        };


        public enum SpatialDenoise
        {
            [InspectorName("Low")] [Tooltip("1-Pass Denoiser.")]
            Low = 0,

            [InspectorName("Medium")] [Tooltip("3-Pass Denoiser.")]
            Medium = 1,

            [InspectorName("High")] [Tooltip("5-Pass Denoiser.")]
            High = 2,
        }


        // Shader Property IDs
        private static readonly int _MaxSample = Shader.PropertyToID("_MaxSample");
        private static readonly int _Sample = Shader.PropertyToID("_Sample");
        private static readonly int _MaxSteps = Shader.PropertyToID("_MaxSteps");
        private static readonly int _StepSize = Shader.PropertyToID("_StepSize");
        private static readonly int _MaxBounce = Shader.PropertyToID("_MaxBounce");
        private static readonly int _RayCount = Shader.PropertyToID("_RayCount");
        private static readonly int _TemporalIntensity = Shader.PropertyToID("_TemporalIntensity");
        private static readonly int _MaxBrightness = Shader.PropertyToID("_MaxBrightness");
        private static readonly int _IsProbeCamera = Shader.PropertyToID("_IsProbeCamera");
        private static readonly int _BackDepthEnabled = Shader.PropertyToID("_BackDepthEnabled");
        private static readonly int _IsAccumulationPaused = Shader.PropertyToID("_IsAccumulationPaused");
        private static readonly int _PrevInvViewProjMatrix = Shader.PropertyToID("_PrevInvViewProjMatrix");
        private static readonly int _PrevCameraPositionWS = Shader.PropertyToID("_PrevCameraPositionWS");
        private static readonly int _PixelSpreadAngleTangent = Shader.PropertyToID("_PixelSpreadAngleTangent");
        private static readonly int _FrameIndex = Shader.PropertyToID("_FrameIndex");

        private static readonly int _PathTracingEmissionTexture = Shader.PropertyToID("_PathTracingEmissionTexture");
        private static readonly int _CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int _CameraDepthAttachment = Shader.PropertyToID("_CameraDepthAttachment");
        private static readonly int _CameraBackDepthTexture = Shader.PropertyToID("_CameraBackDepthTexture");
        private static readonly int _CameraBackNormalsTexture = Shader.PropertyToID("_CameraBackNormalsTexture");

        private static readonly int _PathTracingAccumulationTexture =
            Shader.PropertyToID("_PathTracingAccumulationTexture");

        private static readonly int _PathTracingHistoryTexture = Shader.PropertyToID("_PathTracingHistoryTexture");

        private static readonly int _PathTracingHistoryEmissionTexture =
            Shader.PropertyToID("_PathTracingHistoryEmissionTexture");

        private static readonly int _PathTracingSampleTexture = Shader.PropertyToID("_PathTracingSampleTexture");

        private static readonly int _PathTracingHistorySampleTexture =
            Shader.PropertyToID("_PathTracingHistorySampleTexture");

        private static readonly int _PathTracingHistoryDepthTexture =
            Shader.PropertyToID("_PathTracingHistoryDepthTexture");

        private static readonly int _TransparentGBuffer0 = Shader.PropertyToID("_TransparentGBuffer0");
        private static readonly int _TransparentGBuffer1 = Shader.PropertyToID("_TransparentGBuffer1");
        private static readonly int _TransparentGBuffer2 = Shader.PropertyToID("_TransparentGBuffer2");
        private static readonly int _GBuffer0 = Shader.PropertyToID("_GBuffer0");
        private static readonly int _GBuffer1 = Shader.PropertyToID("_GBuffer1");
        private static readonly int _GBuffer2 = Shader.PropertyToID("_GBuffer2");


        public override void Create()
        {
            if (m_PathTracingMaterial != null)
            {
                if (m_PathTracingMaterial.shader != Shader.Find(m_PathTracingShaderName))
                {
                    if (!isMaterialMismatchLogPrinted)
                    {
                        //Debug.LogErrorFormat("Screen Space Path Tracing: Path Tracing material is not using {0} shader.", m_PathTracingShaderName); 
                        isMaterialMismatchLogPrinted = true;
                    }

                    return;
                }
                else
                    isMaterialMismatchLogPrinted = false;
            }
            else
            {
                if (!isEmptyMaterialLogPrinted)
                {
                    Debug.LogError("Screen Space Path Tracing: Path Tracing material is empty.");
                    isEmptyMaterialLogPrinted = true;
                }

                return;
            }

            isEmptyMaterialLogPrinted = false;

            if (m_PathTracingPass == null)
            {
                m_PathTracingPass = new ScreenSpacePathTracingPass(m_PathTracingMaterial);
                m_PathTracingPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            }

            if (m_AccumulationPass == null)
            {
                m_AccumulationPass = new ScreenSpaceAccumulationPass(m_PathTracingMaterial, progressBar);
                // URP Upscaling is done after "AfterRenderingPostProcessing".
                // Offline: avoid PP-effects (panini projection, ...) distorting the progress bar.
                // Real-time: requires current frame Motion Vectors.
#if UNITY_2023_3_OR_NEWER
                // The injection point between URP Post-processing and Final PP was fixed.
                m_AccumulationPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing - 1;
#else
            m_AccumulationPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
#endif
            }

            if (m_BackfaceDepthPass == null)
            {
                m_BackfaceDepthPass = new BackfaceDepthPass();
                m_BackfaceDepthPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques - 1;
            }

            m_BackfaceDepthPass.m_AccurateThickness = refraction ? AccurateThickness.DepthNormals : accurateThickness;

            if (m_TransparentGBufferPass == null)
            {
                m_TransparentGBufferPass = new TransparentGBufferPass(m_GBufferPassNames);
                m_TransparentGBufferPass.renderPassEvent = RenderPassEvent.AfterRenderingSkybox + 1;
            }

        }

        protected override void Dispose(bool disposing)
        {
            if (m_PathTracingPass != null)
                m_PathTracingPass.Dispose();
            if (m_AccumulationPass != null)
                m_AccumulationPass.Dispose();
            if (m_BackfaceDepthPass != null)
            {
                // Turn off accurate thickness since the render pass is disabled.
                m_PathTracingMaterial.SetFloat(_BackDepthEnabled, 0.0f);
                // m_BackfaceDepthPass.Dispose();
            }

            if (m_TransparentGBufferPass! != null)
                m_TransparentGBufferPass.Dispose();
        }

        void StoreAmbientSettings(ScreenSpacePathTracing ssptVolume)
        {
            if (!ssptVolume.ambientStored.value)
            {
                ssptVolume.ambientIntensity.value = RenderSettings.ambientIntensity;
                ssptVolume.ambientLight.value = RenderSettings.ambientLight;
                ssptVolume.ambientGroundColor.value = RenderSettings.ambientGroundColor;
                ssptVolume.ambientEquatorColor.value = RenderSettings.ambientEquatorColor;
                ssptVolume.ambientSkyColor.value = RenderSettings.ambientSkyColor;
                ssptVolume.ambientStored.value = true;
            }
        }

        void RestoreAmbientSettings(ScreenSpacePathTracing ssptVolume)
        {
            if (ssptVolume != null && ssptVolume.ambientStored.value)
            {
                RenderSettings.ambientIntensity = ssptVolume.ambientIntensity.value;
                RenderSettings.ambientLight = ssptVolume.ambientLight.value;
                RenderSettings.ambientGroundColor = ssptVolume.ambientGroundColor.value;
                RenderSettings.ambientEquatorColor = ssptVolume.ambientEquatorColor.value;
                RenderSettings.ambientSkyColor = ssptVolume.ambientSkyColor.value;
                ssptVolume.ambientStored.value = false;
            }
        }

        void DisableAmbientSettings(ScreenSpacePathTracing ssptVolume)
        {
            if (ssptVolume.ambientStored.value)
            {
                RenderSettings.ambientIntensity = 0.0f;
                RenderSettings.ambientLight = Color.black;
                RenderSettings.ambientGroundColor = Color.black;
                RenderSettings.ambientEquatorColor = Color.black;
                RenderSettings.ambientSkyColor = Color.black;
            }
        }

        [Tooltip("The material of path tracing shader.")] [SerializeField]
        private Material m_PathTracingMaterial;

        [Header("Path Tracing Extensions")]
        [Tooltip(
            "Render the backface depth of scene geometries. This improves the accuracy of screen space path tracing, but may not work well in scenes with lots of single-sided objects.")]
        [SerializeField]
        private AccurateThickness accurateThickness = AccurateThickness.DepthOnly;

        [Header("Additional Lighting Models")]
        [Tooltip("Specifies if the effect calculates path tracing refractions.")]
        [SerializeField]
        private bool refraction = false;

        [Header("Accumulation")]
        [Tooltip("Add a progress bar to show the offline accumulation progress.")]
        [SerializeField]
        private bool progressBar = true;

        [Tooltip("Specifies the quality of Edge-Avoiding Spatial Denoiser.")] [SerializeField]
        private SpatialDenoise spatialDenoise = SpatialDenoise.Medium;


        private const string m_PathTracingShaderName = "Hidden/Universal Render Pipeline/Screen Space Path Tracing";
        private readonly string[] m_GBufferPassNames = new string[] { "UniversalGBuffer" };
        private ScreenSpacePathTracingPass m_PathTracingPass;
        private ScreenSpaceAccumulationPass m_AccumulationPass;
        private BackfaceDepthPass m_BackfaceDepthPass;
        private TransparentGBufferPass m_TransparentGBufferPass;

        private readonly static FieldInfo renderingModeFieldInfo =
            typeof(UniversalRenderer).GetField("m_RenderingMode", BindingFlags.NonPublic | BindingFlags.Instance);

        // Used in Forward GBuffer render pass
        private readonly static FieldInfo gBufferFieldInfo =
            typeof(UniversalRenderer).GetField("m_GBufferPass", BindingFlags.NonPublic | BindingFlags.Instance);

        // [Resolve Later] The "_CameraNormalsTexture" still exists after disabling DepthNormals Prepass, which may cause issue during rendering.
        // So instead of checking the RTHandle, we need to check if DepthNormals Prepass is enqueued.
        //private readonly static FieldInfo normalsTextureFieldInfo = typeof(UniversalRenderer).GetField("m_NormalsTexture", BindingFlags.NonPublic | BindingFlags.Instance);

        // Avoid printing messages every frame
        private bool isMRTLogPrinted = false;
        private bool isMSAALogPrinted = false;
        private bool isMaterialMismatchLogPrinted = false;
        private bool isEmptyMaterialLogPrinted = false;


        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // Do not add render passes if any error occurs.
            if (isMaterialMismatchLogPrinted || isEmptyMaterialLogPrinted || isMRTLogPrinted)
                return;

            // Currently MSAA is not supported, because we sample the camera depth buffer without resolving AA.
            if (renderingData.cameraData.cameraTargetDescriptor.msaaSamples != 1)
            {
                if (!isMSAALogPrinted)
                {
                    Debug.LogError("Screen Space Path Tracing: Camera with MSAA enabled is not currently supported.");
                    isMSAALogPrinted = true;
                }

                return;
            }
            else
                isMSAALogPrinted = false;

            var stack = VolumeManager.instance.stack;
            ScreenSpacePathTracing ssptVolume = stack.GetComponent<ScreenSpacePathTracing>();
            bool isActive = ssptVolume != null && ssptVolume.IsActive();

            // [WIP] Try to automatically adjust the ambient settings (vary by scene) to improve usability.
            if (isActive && this.isActive)
            {
                StoreAmbientSettings(ssptVolume);
                DisableAmbientSettings(ssptVolume);
            }
            else
            {
                RestoreAmbientSettings(ssptVolume);
                if (m_AccumulationPass != null)
                {
                    m_AccumulationPass.sample = 0;
                }

                return;
            }

            m_PathTracingMaterial.SetFloat(_MaxSteps, ssptVolume.maximumSteps.value);
            m_PathTracingMaterial.SetFloat(_StepSize, ssptVolume.stepSize.value);
            m_PathTracingMaterial.SetFloat(_MaxBounce, ssptVolume.maximumDepth.value);
            m_PathTracingMaterial.SetFloat(_RayCount, ssptVolume.samplesPerPixel.value);
            m_PathTracingMaterial.SetFloat(_TemporalIntensity,
                Mathf.Lerp(0.8f, 0.97f, ssptVolume.accumFactor.value * 2.0f - 1.0f));
            m_PathTracingMaterial.SetFloat(_MaxBrightness, ssptVolume.maximumIntensity.value);

            m_AccumulationPass.maximumSample = ssptVolume.maximumSamples.value;
            m_AccumulationPass.ssptVolume = ssptVolume;

            if (ssptVolume.noiseMethod.value == ScreenSpacePathTracing.NoiseType.HashedRandom)
            {
                m_PathTracingMaterial.EnableKeyword("_METHOD_HASHED_RANDOM");
                m_PathTracingMaterial.DisableKeyword("_METHOD_BLUE_NOISE");
            }
            else
            {
                m_PathTracingMaterial.EnableKeyword("_METHOD_BLUE_NOISE");
                m_PathTracingMaterial.DisableKeyword("_METHOD_HASHED_RANDOM");
            }

            if (ssptVolume.denoiser.value == ScreenSpacePathTracing.DenoiserType.Temporal ||
                ssptVolume.denoiser.value == ScreenSpacePathTracing.DenoiserType.SpatialTemporal)
                m_PathTracingMaterial.EnableKeyword("_TEMPORAL_ACCUMULATION");
            else
                m_PathTracingMaterial.DisableKeyword("_TEMPORAL_ACCUMULATION");

            var universalRenderer = renderingData.cameraData.renderer as UniversalRenderer;
            var renderingMode = (RenderingMode)renderingModeFieldInfo.GetValue(renderer);
            if (renderingMode == RenderingMode.ForwardPlus)
            {
                m_PathTracingMaterial.EnableKeyword("_FP_REFL_PROBE_ATLAS");
            }
            else
            {
                m_PathTracingMaterial.DisableKeyword("_FP_REFL_PROBE_ATLAS");
            }

            // Update accumulation mode each frame since we support runtime changing of these properties.
            m_AccumulationPass.m_Accumulation = (Accumulation)ssptVolume.denoiser.value;
            m_AccumulationPass.m_SpatialDenoise = spatialDenoise;

            if (renderingData.cameraData.camera.cameraType == CameraType.Reflection)
            {
                m_PathTracingMaterial.SetFloat(_IsProbeCamera, 1.0f);
            }
            else
            {
                m_PathTracingMaterial.SetFloat(_IsProbeCamera, 0.0f);
            }

#if UNITY_EDITOR
            // Motion Vectors of URP SceneView don't get updated each frame when not entering play mode. (Might be fixed when supporting scene view anti-aliasing)
            // Change the method to multi-frame accumulation (offline mode) if SceneView is not in play mode.
            bool isPlayMode = UnityEditor.EditorApplication.isPlaying;
            if (renderingData.cameraData.camera.cameraType == CameraType.SceneView && !isPlayMode &&
                ((Accumulation)ssptVolume.denoiser.value == Accumulation.PerObject ||
                 (Accumulation)ssptVolume.denoiser.value == Accumulation.PerObjectBlur))
                m_AccumulationPass.m_Accumulation = Accumulation.Camera;
#endif
            // Stop path tracing after reaching the maximum number of offline accumulation samples.
            if (renderingData.cameraData.camera.cameraType != CameraType.Preview &&
                !(m_AccumulationPass.m_Accumulation == Accumulation.Camera &&
                  m_AccumulationPass.sample == m_AccumulationPass.maximumSample))
                renderer.EnqueuePass(m_PathTracingPass);

            // No need to accumulate when rendering reflection probes, this will also break game view accumulation.
            bool shouldAccumulate = ((Accumulation)ssptVolume.denoiser.value == Accumulation.Camera)
                ? (renderingData.cameraData.camera.cameraType != CameraType.Reflection)
                : (renderingData.cameraData.camera.cameraType != CameraType.Reflection &&
                   renderingData.cameraData.camera.cameraType != CameraType.Preview);
            if (shouldAccumulate)
            {
                // Update progress bar toggle each frame since we support runtime changing of these properties.
                m_AccumulationPass.m_ProgressBar = progressBar;
#if UNITY_6000_0_OR_NEWER
                // [Solution 1]
                // There seems to be a bug related to CopyDepthPass
                // the "After Transparent" option behaves the same as "After Opaque", 
                // and "CopyDepthMode" returns the correct value ("After Transparent") only once, then "After Opaque" every time.
                /*
                FieldInfo copyDepthModeFieldInfo = typeof(UniversalRenderer).GetField("m_CopyDepthMode", BindingFlags.NonPublic | BindingFlags.Instance);
                var copyDepthMode = (CopyDepthMode)copyDepthModeFieldInfo.GetValue(universalRenderer);
                RenderPassEvent accumulationPassEvent = (copyDepthMode != CopyDepthMode.AfterTransparent) ? RenderPassEvent.BeforeRenderingTransparents : RenderPassEvent.AfterRenderingPostProcessing - 1;
                */

                // [Solution 2]
                // Change back to solution 1 when the bug is fixed.
                FieldInfo motionVectorPassFieldInfo = typeof(UniversalRenderer).GetField("m_MotionVectorPass",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var motionVectorPass = motionVectorPassFieldInfo.GetValue(universalRenderer);
                PropertyInfo renderPassEventPropertyInfo = motionVectorPass.GetType()
                    .GetProperty("renderPassEvent", BindingFlags.Public | BindingFlags.Instance);
                if (renderPassEventPropertyInfo != null)
                {
                    RenderPassEvent renderPassEvent =
                        (RenderPassEvent)renderPassEventPropertyInfo.GetValue(motionVectorPass);
                    m_AccumulationPass.renderPassEvent = (renderPassEvent < RenderPassEvent.AfterRenderingTransparents)
                        ? RenderPassEvent.BeforeRenderingTransparents
                        : RenderPassEvent.AfterRenderingPostProcessing - 1;
                }
                else
                    m_AccumulationPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing - 1;
#endif

#if UNITY_EDITOR
                // Disable real-time accumulation when motion vectors are not updated (playing & paused) in editor
                if (!(UnityEditor.EditorApplication.isPlaying && UnityEditor.EditorApplication.isPaused &&
                      (m_AccumulationPass.m_Accumulation == Accumulation.PerObject ||
                       m_AccumulationPass.m_Accumulation == Accumulation.PerObjectBlur)))
                    renderer.EnqueuePass(m_AccumulationPass);
#else
            renderer.EnqueuePass(m_AccumulationPass);
#endif
            }

            if (m_BackfaceDepthPass.m_AccurateThickness != AccurateThickness.None)
            {
                renderer.EnqueuePass(m_BackfaceDepthPass);
                m_PathTracingMaterial.EnableKeyword("_BACKFACE_TEXTURES");
                if (m_BackfaceDepthPass.m_AccurateThickness == AccurateThickness.DepthOnly)
                    m_PathTracingMaterial.SetFloat(_BackDepthEnabled, 1.0f); // DepthOnly
                else
                    m_PathTracingMaterial.SetFloat(_BackDepthEnabled, 2.0f); // DepthNormals
            }
            else
            {
                m_PathTracingMaterial.DisableKeyword("_BACKFACE_TEXTURES");
                m_PathTracingMaterial.SetFloat(_BackDepthEnabled, 0.0f);
            }

            if (refraction)
            {
                m_PathTracingMaterial.EnableKeyword("_SUPPORT_REFRACTION");
                renderer.EnqueuePass(m_TransparentGBufferPass);
            }
            else
            {
                m_PathTracingMaterial.DisableKeyword("_SUPPORT_REFRACTION");
            }

        }
    }
}
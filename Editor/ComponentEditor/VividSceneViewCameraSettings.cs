using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [Serializable]
    internal sealed class VividSceneViewCameraSettings
        : SceneView.AdditionalSettings<VividRenderPipelineAsset, VividAdditionalCameraData>
    {
        [Serializable]
        internal struct AntialiasingSettings
        {
            public VividAntialiasingMode mode;
            [Range(0.2f, 2f)] public float taaJitterSpread;
            [Range(4, 64)] public int taaSampleCount;
            [Range(0f, 0.99f)] public float taaBaseBlendFactor;
            [Range(0.5f, 6f)] public float taaMotionWeightDecay;
            [Range(0f, 1f)] public float taaAntiFlickerIntensity;
            public VividFsr3QualityMode fsr3Quality;
            public bool fsr3EnableSharpening;
            [Range(0f, 1f)] public float fsr3Sharpness;
            public VividTsrQualityMode tsrQuality;
            public bool tsrEnableSharpening;
            [Range(0f, 1f)] public float tsrSharpness;
            [Range(8, 32)] public int tsrHistorySampleCount;
#if DLSS_PLUGIN_INTEGRATE
            public DLSSQuality dlssQuality;
            public DLSSNeuralRenderingPreset dlssNeuralRenderingPreset;
            public DLSSNeuralRenderingStyle dlssNeuralRenderingStyle;
            public bool dlssNeuralRenderingUpscaling;
            [Range(0f, 2f)] public float dlssNeuralRenderingIntensity;
            [Range(0f, 2f)] public float dlssNeuralRenderingLocalToneStrength;
            [Range(0f, 2f)] public float dlssNeuralRenderingLocalStructureStrength;
            [Range(-1f, 2f)] public float dlssNeuralRenderingSkinStructureStrength;
            public bool dlssNeuralRenderingUseAutoMask;
            public bool dlssNeuralRenderingUICorrection;
#endif

            internal static AntialiasingSettings Default => new()
            {
                mode = VividAntialiasingMode.TemporalAntiAliasing,
                taaJitterSpread = 1f,
                taaSampleCount = 8,
                taaBaseBlendFactor = 0.95f,
                taaMotionWeightDecay = 3f,
                taaAntiFlickerIntensity = 0.5f,
                fsr3Quality = VividFsr3QualityMode.NativeAA,
                fsr3EnableSharpening = true,
                fsr3Sharpness = 0.2f,
                tsrQuality = VividTsrQualityMode.NativeAA,
                tsrEnableSharpening = true,
                tsrSharpness = 0.2f,
                tsrHistorySampleCount = 16,
#if DLSS_PLUGIN_INTEGRATE
                dlssQuality = DLSSQuality.Balanced,
                dlssNeuralRenderingPreset = DLSSNeuralRenderingPreset.Default,
                dlssNeuralRenderingStyle = DLSSNeuralRenderingStyle.Default,
                dlssNeuralRenderingIntensity = 1f,
                dlssNeuralRenderingLocalToneStrength = 1f,
                dlssNeuralRenderingLocalStructureStrength = 1f,
                dlssNeuralRenderingSkinStructureStrength = -1f,
#endif
            };
        }

        [SerializeField] internal AntialiasingSettings antialiasing = AntialiasingSettings.Default;

        public override void Reset()
        {
            antialiasing = AntialiasingSettings.Default;
        }

        public override void Apply()
        {
            var data = linkedComponent;
            if (data == null)
                return;

            data.antialiasing = antialiasing.mode;
            data.taaJitterSpread = antialiasing.taaJitterSpread;
            data.taaSampleCount = antialiasing.taaSampleCount;
            data.taaBaseBlendFactor = antialiasing.taaBaseBlendFactor;
            data.taaMotionWeightDecay = antialiasing.taaMotionWeightDecay;
            data.taaAntiFlickerIntensity = antialiasing.taaAntiFlickerIntensity;
            data.fsr3Quality = antialiasing.fsr3Quality;
            data.fsr3EnableSharpening = antialiasing.fsr3EnableSharpening;
            data.fsr3Sharpness = antialiasing.fsr3Sharpness;
            data.tsrQuality = antialiasing.tsrQuality;
            data.tsrEnableSharpening = antialiasing.tsrEnableSharpening;
            data.tsrSharpness = antialiasing.tsrSharpness;
            data.tsrHistorySampleCount = antialiasing.tsrHistorySampleCount;
#if DLSS_PLUGIN_INTEGRATE
            data.dlssQuality = antialiasing.dlssQuality;
            data.dlssNeuralRenderingPreset = antialiasing.dlssNeuralRenderingPreset;
            data.dlssNeuralRenderingStyle = antialiasing.dlssNeuralRenderingStyle;
            data.dlssNeuralRenderingUpscaling = antialiasing.dlssNeuralRenderingUpscaling;
            data.dlssNeuralRenderingIntensity = antialiasing.dlssNeuralRenderingIntensity;
            data.dlssNeuralRenderingLocalToneStrength = antialiasing.dlssNeuralRenderingLocalToneStrength;
            data.dlssNeuralRenderingLocalStructureStrength = antialiasing.dlssNeuralRenderingLocalStructureStrength;
            data.dlssNeuralRenderingSkinStructureStrength = antialiasing.dlssNeuralRenderingSkinStructureStrength;
            data.dlssNeuralRenderingUseAutoMask = antialiasing.dlssNeuralRenderingUseAutoMask;
            data.dlssNeuralRenderingUICorrection = antialiasing.dlssNeuralRenderingUICorrection;
#endif
            data.ResetPostProcessingHistory();
        }

        internal void Bind(Camera camera)
        {
            if (linkedComponent != null && linkedComponent.camera == camera)
                return;

            linkedComponent = camera.GetVividAdditionalCameraData();
            linkedComponent.hideFlags = HideFlags.HideAndDontSave;
            Apply();
        }
    }

    [InitializeOnLoad]
    internal static class VividSceneViewCameraSettingsUI
    {
        private static readonly Action<ScriptableRenderContext, Camera> s_BeginCameraRendering = BeginCameraRendering;
        private static readonly Func<SceneView, VisualElement> s_CreateSettingsGUI = CreateSettingsGUI;
        private static readonly Action<SceneView, VisualElement> s_BindSettingsGUI = BindSettingsGUI;
        private static readonly Action s_UpdateRegistration = UpdateRegistration;

        static VividSceneViewCameraSettingsUI()
        {
            RenderPipelineManager.beginCameraRendering += s_BeginCameraRendering;
            RenderPipelineManager.activeRenderPipelineTypeChanged += s_UpdateRegistration;
            UpdateRegistration();
        }

        private static void UpdateRegistration()
        {
            SceneViewCameraWindow.createAdditionalSettingsGUI -= s_CreateSettingsGUI;
            SceneViewCameraWindow.bindAdditionalSettings -= s_BindSettingsGUI;
            if (GraphicsSettings.currentRenderPipeline is VividRenderPipelineAsset)
            {
                SceneViewCameraWindow.createAdditionalSettingsGUI += s_CreateSettingsGUI;
                SceneViewCameraWindow.bindAdditionalSettings += s_BindSettingsGUI;
            }
        }

        private static void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.SceneView || GraphicsSettings.currentRenderPipeline is not VividRenderPipelineAsset)
                return;

            var views = SceneView.sceneViews;
            for (var index = 0; index < views.Count; index++)
            {
                var view = (SceneView)views[index];
                if (view.camera != camera)
                    continue;

                GetOrCreateSettings(view);
                // Preserve Scene View's existing main-camera volume mask inheritance.
                camera.GetComponent<VividAdditionalCameraData>().volumeLayerMask =
                    VividVolumeManagerUtility.ResolveVolumeLayerMask(camera);
                return;
            }
        }

        internal static VividSceneViewCameraSettings GetOrCreateSettings(SceneView view)
        {
            var settings = view.GetAdditionalSettings<VividSceneViewCameraSettings>();
            if (settings == null)
            {
                settings = new VividSceneViewCameraSettings();
                view.AddAdditionalSettings(settings);
            }

            settings.Bind(view.camera);
            return settings;
        }

        internal static VisualElement CreateSettingsGUI(SceneView view)
        {
            if (GraphicsSettings.currentRenderPipeline is not VividRenderPipelineAsset)
                return null;

            var root = new VisualElement { name = "vividrp-scene-camera-antialiasing" };
            root.RegisterCallback<SerializedPropertyChangeEvent>(evt =>
            {
                GetOrCreateSettings(view).Apply();
                view.Repaint();
                EditorApplication.QueuePlayerLoopUpdate();
            });
            BindSettingsGUI(view, root);
            return root;
        }

        private static void BindSettingsGUI(SceneView view, VisualElement root)
        {
            if (root.name != "vividrp-scene-camera-antialiasing")
                return;

            root.Unbind();
            root.Clear();
            var settings = GetOrCreateSettings(view);
            var serializedView = new SerializedObject(view);
            var settingsList = serializedView.FindProperty("m_AdditionalSettings");
            SerializedProperty properties = null;
            for (var index = 0; index < settingsList.arraySize; index++)
            {
                var element = settingsList.GetArrayElementAtIndex(index);
                if (ReferenceEquals(element.managedReferenceValue, settings))
                {
                    properties = element.FindPropertyRelative("antialiasing");
                    break;
                }
            }

            root.Add(new Label("VividRP Anti-Aliasing") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            var mode = new PropertyField(properties.FindPropertyRelative("mode"), "Anti-Aliasing");
            root.Add(mode);
            var parameters = new VisualElement();
            root.Add(parameters);
            AddParameters(parameters, properties, settings.antialiasing.mode);
            root.Bind(serializedView);
            mode.RegisterCallback<SerializedPropertyChangeEvent>(evt =>
            {
                parameters.Clear();
                AddParameters(parameters, properties,
                    (VividAntialiasingMode)properties.FindPropertyRelative("mode").intValue);
                parameters.Bind(serializedView);
            });
        }

        private static void AddParameters(VisualElement root, SerializedProperty settings, VividAntialiasingMode mode)
        {
            switch (mode)
            {
                case VividAntialiasingMode.TemporalAntiAliasing:
                    Add("taaJitterSpread", "Jitter Spread");
                    Add("taaSampleCount", "Sample Count");
                    Add("taaBaseBlendFactor", "Base Blend");
                    Add("taaMotionWeightDecay", "Motion Decay");
                    Add("taaAntiFlickerIntensity", "Anti-Flicker");
                    break;
                case VividAntialiasingMode.FidelityFXSuperResolution3:
                    Add("fsr3Quality", "Quality");
                    Add("fsr3EnableSharpening", "Sharpening");
                    Add("fsr3Sharpness", "Sharpness");
                    break;
                case VividAntialiasingMode.TemporalSuperResolution:
                    Add("tsrQuality", "Quality");
                    Add("tsrHistorySampleCount", "History Samples");
                    Add("tsrEnableSharpening", "Sharpening");
                    Add("tsrSharpness", "Sharpness");
                    break;
#if DLSS_PLUGIN_INTEGRATE
                case VividAntialiasingMode.DeepLearningSuperSampling:
                    Add("dlssQuality", "Quality");
                    break;
                case VividAntialiasingMode.DLSSNeuralRendering:
                    Add("dlssNeuralRenderingPreset", "Preset");
                    Add("dlssNeuralRenderingStyle", "Style");
                    Add("dlssNeuralRenderingUpscaling", "Upscaling");
                    Add("dlssNeuralRenderingIntensity", "Intensity");
                    Add("dlssNeuralRenderingLocalToneStrength", "Local Tone Strength");
                    Add("dlssNeuralRenderingLocalStructureStrength", "Local Structure Strength");
                    Add("dlssNeuralRenderingSkinStructureStrength", "Skin Structure Strength");
                    Add("dlssNeuralRenderingUseAutoMask", "Auto Mask");
                    Add("dlssNeuralRenderingUICorrection", "UI Correction");
                    break;
#endif
            }

            void Add(string name, string label)
            {
                root.Add(new PropertyField(settings.FindPropertyRelative(name), label));
            }
        }
    }
}

using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using VividRP.Runtime;

namespace VividRP.Editor.RenderPipeline
{
    [CustomEditor(typeof(VividRenderPipelineAsset))]
    internal sealed class VividRenderPipelineAssetEditor : UnityEditor.Editor
    {
        private static readonly GUIContent s_RenderGraphLabel = EditorGUIUtility.TrTextContent("Render Graph Asset");
        private static readonly GUIContent s_ColorGradingSpaceLabel = EditorGUIUtility.TrTextContent(
            "Color Grading Space",
            "Set the color space used for color grading. ACES tonemapping always grades in ACEScg; use this to select the grading space for the other tonemappers.");
        private static readonly GUIContent s_AutoExposureImplementationLabel = EditorGUIUtility.TrTextContent("Auto Exposure Implementation");
        private static readonly GUIContent s_AsyncComputeLabel = EditorGUIUtility.TrTextContent("Async Compute");
        private static readonly GUIContent s_GpuDrivenLabel = EditorGUIUtility.TrTextContent("GPU Driven");
        private static readonly GUIContent s_GpuDrivenDecalLabel = EditorGUIUtility.TrTextContent(
            "GPU Driven Decal",
            "Experimental. Requires GPU Driven rendering and bindless texture descriptors; silently disables itself when bindless is unavailable.");
        private static readonly GUIContent s_SrpBatcherLabel = EditorGUIUtility.TrTextContent("SRP Batcher");
        private static readonly GUIContent s_SupportProbeVolumeLabel = EditorGUIUtility.TrTextContent("Adaptive Probe Volumes");
        private static readonly GUIContent s_ProbeVolumeShBandsLabel = EditorGUIUtility.TrTextContent("APV SH Bands");
        private static readonly GUIContent s_MaxLocalVolumetricFogCountLabel =
            EditorGUIUtility.TrTextContent("Max Local Volumetric Fog Count", "Maximum number of visible Local Volumetric Fog volumes allocated and processed per camera.");
        private static readonly string s_DefaultVolumeSharedMessage =
            "Default Volume is stored in VividRP Global Settings and shared by all VividRP pipeline assets.";
        private static readonly string s_DefaultVolumeInactiveMessage =
            "Assign a VividRP asset in Project Settings > Graphics to edit Default Volume component overrides here.";

        private DefaultVolumeProfileEditor m_DefaultVolumeProfileEditor;
        private SerializedObject m_GlobalSettingsSerializedObject;
        private ObjectField m_DefaultVolumeProfileField;
        private HelpBox m_DefaultVolumeStatusHelpBox;
        private VisualElement m_DefaultVolumeEditorContainer;
        private bool m_IsSubscribedToPipelineCreated;

        private void OnDisable()
        {
            UnsubscribeFromPipelineCreated();
            DestroyDefaultVolumeProfileEditor();
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement
            {
                name = "vivid-rp-asset-root",
            };

            var renderGraphField = new PropertyField(serializedObject.FindProperty("RenderGraphAsset"), s_RenderGraphLabel.text)
            {
                name = "vivid-rp-asset-render-graph-field",
            };
            root.Add(renderGraphField);

            var colorGradingSpaceField = new PropertyField(
                serializedObject.FindProperty("m_ColorGradingSpace"),
                s_ColorGradingSpaceLabel.text)
            {
                name = "vivid-rp-asset-color-grading-space-field",
                tooltip = s_ColorGradingSpaceLabel.tooltip,
            };
            root.Add(colorGradingSpaceField);

            var autoExposureImplementationField = new PropertyField(
                serializedObject.FindProperty("m_AutoExposureImplementation"),
                s_AutoExposureImplementationLabel.text)
            {
                name = "vivid-rp-asset-auto-exposure-implementation-field",
            };
            root.Add(autoExposureImplementationField);

            var asyncComputeField = new PropertyField(serializedObject.FindProperty("m_EnableAsyncCompute"), s_AsyncComputeLabel.text)
            {
                name = "vivid-rp-asset-async-compute-field",
            };
            root.Add(asyncComputeField);

            var gpuDrivenField = new PropertyField(serializedObject.FindProperty("m_EnableGPUDriven"), s_GpuDrivenLabel.text)
            {
                name = "vivid-rp-asset-gpu-driven-field",
            };
            root.Add(gpuDrivenField);

            var gpuDrivenDecalField = new PropertyField(
                serializedObject.FindProperty("m_EnableGPUDrivenDecal"),
                s_GpuDrivenDecalLabel.text)
            {
                name = "vivid-rp-asset-gpu-driven-decal-field",
                tooltip = s_GpuDrivenDecalLabel.tooltip,
            };
            root.Add(gpuDrivenDecalField);

            var srpBatcherField = new PropertyField(serializedObject.FindProperty("m_EnableSRPBatcher"), s_SrpBatcherLabel.text)
            {
                name = "vivid-rp-asset-srp-batcher-field",
            };
            root.Add(srpBatcherField);

            var supportProbeVolumeField = new PropertyField(
                serializedObject.FindProperty("m_SupportProbeVolume"),
                s_SupportProbeVolumeLabel.text)
            {
                name = "vivid-rp-asset-support-probe-volume-field",
            };
            root.Add(supportProbeVolumeField);

            var probeVolumeShBandsField = new PropertyField(
                serializedObject.FindProperty("m_ProbeVolumeSHBands"),
                s_ProbeVolumeShBandsLabel.text)
            {
                name = "vivid-rp-asset-probe-volume-sh-bands-field",
            };
            root.Add(probeVolumeShBandsField);

            root.Bind(serializedObject);

            RefreshGlobalSettingsSerializedObject();
            AddLocalVolumetricFogFoldout(root);

            var sharedInfoHelpBox = new HelpBox(s_DefaultVolumeSharedMessage, HelpBoxMessageType.Info)
            {
                name = "vivid-rp-asset-default-volume-shared-info",
            };
            root.Add(sharedInfoHelpBox);

            var defaultVolumeFoldout = new Foldout
            {
                text = "Default Volume",
                value = true,
                name = "vivid-rp-asset-default-volume-foldout",
            };

            m_DefaultVolumeProfileField = new ObjectField("Default Volume Profile")
            {
                name = "vivid-rp-asset-default-volume-field",
                objectType = typeof(VolumeProfile),
                allowSceneObjects = false,
            };
            m_DefaultVolumeProfileField.RegisterValueChangedCallback(OnDefaultVolumeProfileChanged);
            defaultVolumeFoldout.Add(m_DefaultVolumeProfileField);

            m_DefaultVolumeStatusHelpBox = new HelpBox(string.Empty, HelpBoxMessageType.Info)
            {
                name = "vivid-rp-asset-default-volume-status",
            };
            m_DefaultVolumeStatusHelpBox.style.display = DisplayStyle.None;
            defaultVolumeFoldout.Add(m_DefaultVolumeStatusHelpBox);

            m_DefaultVolumeEditorContainer = new VisualElement
            {
                name = "vivid-rp-asset-default-volume-editor-container",
            };
            defaultVolumeFoldout.Add(m_DefaultVolumeEditorContainer);

            root.Add(defaultVolumeFoldout);

            RefreshDefaultVolumeInspector();
            SubscribeToPipelineCreated();

            return root;
        }

        private void AddLocalVolumetricFogFoldout(VisualElement root)
        {
            var foldout = new Foldout
            {
                text = "Local Volumetric Fog",
                value = true,
                name = "vivid-rp-asset-local-volumetric-fog-foldout",
            };

            if (m_GlobalSettingsSerializedObject == null)
            {
                foldout.Add(new HelpBox("Unable to load the VividRP global settings asset.", HelpBoxMessageType.Warning));
                root.Add(foldout);
                return;
            }

            AddGlobalSettingsProperty(
                foldout,
                "m_MaxLocalVolumetricFogCount",
                s_MaxLocalVolumetricFogCountLabel,
                "vivid-rp-asset-max-local-volumetric-fog-count-field");

            foldout.Bind(m_GlobalSettingsSerializedObject);
            root.Add(foldout);
        }

        private void AddGlobalSettingsProperty(
            VisualElement root,
            string propertyName,
            GUIContent label,
            string elementName)
        {
            var property = m_GlobalSettingsSerializedObject.FindProperty(propertyName);
            if (property == null)
                return;

            var field = new PropertyField(property, label.text)
            {
                name = elementName,
                tooltip = label.tooltip,
            };
            root.Add(field);
        }

        private void OnDefaultVolumeProfileChanged(ChangeEvent<Object> evt)
        {
            var globalSettings = VividRenderPipelineGlobalSettings.Ensure();
            if (globalSettings == null)
            {
                RefreshDefaultVolumeInspector();
                return;
            }

            var previousProfile = evt.previousValue as VolumeProfile;
            var profile = evt.newValue as VolumeProfile;
            if (profile == null)
            {
                if (previousProfile != null)
                {
                    m_DefaultVolumeProfileField.SetValueWithoutNotify(previousProfile);
                    return;
                }

                profile = VividDefaultVolumeProfileEditorUtility.EnsureDefaultVolumeProfile(globalSettings);
            }

            if (profile == null)
            {
                RefreshDefaultVolumeInspector();
                return;
            }

            if (RenderPipelineManager.currentPipeline is VividRenderPipeline
                && !ReferenceEquals(previousProfile, profile)
                && !VolumeProfileUtils.UpdateGlobalDefaultVolumeProfileWithConfirmation<VividRenderPipeline>(profile, previousProfile))
            {
                m_DefaultVolumeProfileField.SetValueWithoutNotify(previousProfile);
                return;
            }

            var volumeSettings = globalSettings.GetSettings<VividDefaultVolumeProfileSettings>();
            if (volumeSettings == null)
            {
                RefreshDefaultVolumeInspector();
                return;
            }

            Undo.RecordObject(globalSettings, "Change Vivid Default Volume Profile");
            volumeSettings.volumeProfile = profile;
            EditorUtility.SetDirty(globalSettings);
            AssetDatabase.SaveAssetIfDirty(globalSettings);

            m_DefaultVolumeProfileField.SetValueWithoutNotify(profile);
            m_GlobalSettingsSerializedObject = new SerializedObject(globalSettings);
            RebuildDefaultVolumeProfileEditor(profile);
        }

        private void RefreshDefaultVolumeInspector()
        {
            var globalSettings = VividRenderPipelineGlobalSettings.Ensure();
            var profile = VividDefaultVolumeProfileEditorUtility.EnsureDefaultVolumeProfile(globalSettings);

            m_GlobalSettingsSerializedObject = globalSettings != null ? new SerializedObject(globalSettings) : null;
            m_DefaultVolumeProfileField?.SetValueWithoutNotify(profile);
            RebuildDefaultVolumeProfileEditor(profile);
        }

        private void RefreshGlobalSettingsSerializedObject()
        {
            var globalSettings = VividRenderPipelineGlobalSettings.Ensure();
            m_GlobalSettingsSerializedObject = globalSettings != null ? new SerializedObject(globalSettings) : null;
        }

        private void RebuildDefaultVolumeProfileEditor(VolumeProfile profile)
        {
            DestroyDefaultVolumeProfileEditor();

            if (m_DefaultVolumeStatusHelpBox == null || m_DefaultVolumeEditorContainer == null)
                return;

            if (profile == null)
            {
                ShowStatus("Unable to resolve the VividRP Default Volume Profile.", HelpBoxMessageType.Warning);
                return;
            }

            if (!CanShowDetailedVolumeEditor())
            {
                ShowStatus(s_DefaultVolumeInactiveMessage, HelpBoxMessageType.Info);
                return;
            }

            HideStatus();

            if (m_GlobalSettingsSerializedObject == null)
            {
                ShowStatus("Unable to load the VividRP global settings asset.", HelpBoxMessageType.Warning);
                return;
            }

            var profileEditor = new DefaultVolumeProfileEditor(profile, m_GlobalSettingsSerializedObject);
            var editorElement = profileEditor.Create();
            m_DefaultVolumeProfileEditor = profileEditor;
            m_DefaultVolumeEditorContainer.Add(editorElement);
        }

        private bool CanShowDetailedVolumeEditor()
        {
            if (!VolumeManager.instance.isInitialized)
                return false;

            return RenderPipelineManager.currentPipeline is VividRenderPipeline
                || GraphicsSettings.currentRenderPipelineAssetType == typeof(VividRenderPipelineAsset);
        }

        private void ShowStatus(string text, HelpBoxMessageType messageType)
        {
            m_DefaultVolumeStatusHelpBox.text = text;
            m_DefaultVolumeStatusHelpBox.messageType = messageType;
            m_DefaultVolumeStatusHelpBox.style.display = DisplayStyle.Flex;
        }

        private void HideStatus()
        {
            m_DefaultVolumeStatusHelpBox.style.display = DisplayStyle.None;
        }

        private void DestroyDefaultVolumeProfileEditor()
        {
            m_DefaultVolumeEditorContainer?.Clear();

            if (m_DefaultVolumeProfileEditor != null)
            {
                m_DefaultVolumeProfileEditor.Destroy();
                m_DefaultVolumeProfileEditor = null;
            }
        }

        private void SubscribeToPipelineCreated()
        {
            if (m_IsSubscribedToPipelineCreated)
                return;

            RenderPipelineManager.activeRenderPipelineCreated += OnActiveRenderPipelineCreated;
            m_IsSubscribedToPipelineCreated = true;
        }

        private void UnsubscribeFromPipelineCreated()
        {
            if (!m_IsSubscribedToPipelineCreated)
                return;

            RenderPipelineManager.activeRenderPipelineCreated -= OnActiveRenderPipelineCreated;
            m_IsSubscribedToPipelineCreated = false;
        }

        private void OnActiveRenderPipelineCreated()
        {
            RefreshDefaultVolumeInspector();
        }
    }
}

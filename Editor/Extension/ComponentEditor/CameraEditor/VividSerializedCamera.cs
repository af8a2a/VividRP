using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    class VividSerializedCamera : ISerializedCamera
    {
        public SerializedObject serializedObject { get; }
        public SerializedObject serializedAdditionalDataObject { get; }
        public CameraEditor.Settings baseCameraSettings { get; }

        // This one is internal in UnityEditor for whatever reason...
        public SerializedProperty projectionMatrixMode { get; }

        // Common properties
        public SerializedProperty dithering { get; }
        public SerializedProperty stopNaNs { get; }
        public SerializedProperty allowDynamicResolution { get; }
        public SerializedProperty volumeLayerMask { get; }
        public SerializedProperty clearDepth { get; }
        public SerializedProperty antialiasing { get; }

        // URP specific properties
        public SerializedProperty renderShadows { get; }
        public SerializedProperty renderDepth { get; }
        public SerializedProperty renderOpaque { get; }
        public SerializedProperty renderer { get; }
        public SerializedProperty cameraType { get; }
        public SerializedProperty cameras { get; set; }
        public SerializedProperty volumeTrigger { get; }
        public SerializedProperty volumeFrameworkUpdateMode { get; }
        public SerializedProperty renderPostProcessing { get; }
        public SerializedProperty antialiasingQuality { get; }
#if ENABLE_VR && ENABLE_XR_MODULE
        public SerializedProperty allowXRRendering { get; }
#endif
        public SerializedProperty allowHDROutput { get; }

        #region RenderScale

        public SerializedProperty renderScale { get; }

        public SerializedProperty upscalerTechnique { get; }

        #region TAA

        public SerializedProperty taaQuality { get; }
        public SerializedProperty taaFrameInfluence { get; }
        public SerializedProperty taaJitterScale { get; }
        public SerializedProperty taaMipBias { get; }
        public SerializedProperty taaVarianceClampScale { get; }
        public SerializedProperty taaContrastAdaptiveSharpening { get; }

        #endregion
        
        

        #region TAAU

        public SerializedProperty taaSharpenMode;
        public SerializedProperty taaSharpenStrength;
        public SerializedProperty taaRingingReduction;
        public SerializedProperty taaHistorySharpening;
        public SerializedProperty taaAntiFlicker;
        public SerializedProperty taaMotionVectorRejection;
        public SerializedProperty taaAntiRinging;
        public SerializedProperty taaBaseBlendFactor;
        public SerializedProperty taaQualityLevel;


        #endregion
        

        #endregion


        public (Camera camera, VividSerializedCamera serializedCamera) this[int index]
        {
            get
            {
                if (index < 0 || index >= numCameras)
                    throw new ArgumentOutOfRangeException($"{index} is out of bounds [0 - {numCameras}]");

                // Return the camera on that index
                return (cameras.GetArrayElementAtIndex(index).objectReferenceValue as Camera, cameraSerializedObjects[index]);
            }
        }

        public int numCameras => cameras?.arraySize ?? 0;

        VividSerializedCamera[] cameraSerializedObjects { get; set; }

        public UniversalAdditionalCameraData[] camerasAdditionalData { get; }

        public VividSerializedCamera(SerializedObject serializedObject, CameraEditor.Settings settings = null)
        {
            this.serializedObject = serializedObject;
            projectionMatrixMode = serializedObject.FindProperty("m_projectionMatrixMode");

            allowDynamicResolution = serializedObject.FindProperty("m_AllowDynamicResolution");

            if (settings == null)
            {
                baseCameraSettings = new CameraEditor.Settings(serializedObject);
                baseCameraSettings.OnEnable();
            }
            else
            {
                baseCameraSettings = settings;
            }

            camerasAdditionalData = CoreEditorUtils
                .GetAdditionalData<UniversalAdditionalCameraData>(serializedObject.targetObjects);
            serializedAdditionalDataObject = new SerializedObject(camerasAdditionalData);

            // Common properties
            stopNaNs = serializedAdditionalDataObject.FindProperty("m_StopNaN");
            dithering = serializedAdditionalDataObject.FindProperty("m_Dithering");
            antialiasing = serializedAdditionalDataObject.FindProperty("m_Antialiasing");
            volumeLayerMask = serializedAdditionalDataObject.FindProperty("m_VolumeLayerMask");
            clearDepth = serializedAdditionalDataObject.FindProperty("m_ClearDepth");

            // URP specific properties
            renderShadows = serializedAdditionalDataObject.FindProperty("m_RenderShadows");
            renderDepth = serializedAdditionalDataObject.FindProperty("m_RequiresDepthTextureOption");
            renderOpaque = serializedAdditionalDataObject.FindProperty("m_RequiresOpaqueTextureOption");
            renderer = serializedAdditionalDataObject.FindProperty("m_RendererIndex");
            volumeLayerMask = serializedAdditionalDataObject.FindProperty("m_VolumeLayerMask");
            volumeTrigger = serializedAdditionalDataObject.FindProperty("m_VolumeTrigger");
            volumeFrameworkUpdateMode = serializedAdditionalDataObject.FindProperty("m_VolumeFrameworkUpdateModeOption");
            renderPostProcessing = serializedAdditionalDataObject.FindProperty("m_RenderPostProcessing");
            antialiasingQuality = serializedAdditionalDataObject.FindProperty("m_AntialiasingQuality");
            cameraType = serializedAdditionalDataObject.FindProperty("m_CameraType");

#if ENABLE_VR && ENABLE_XR_MODULE
            allowXRRendering = serializedAdditionalDataObject.FindProperty("m_AllowXRRendering");
#endif

            var taaSettings = serializedAdditionalDataObject.FindProperty(nameof(UniversalAdditionalCameraData.m_TaaSettings));
            taaQuality = taaSettings.FindPropertyRelative(nameof(TemporalAA.Settings.m_Quality));
            taaFrameInfluence = taaSettings.FindPropertyRelative(nameof(TemporalAA.Settings.m_FrameInfluence));
            taaJitterScale = taaSettings.FindPropertyRelative(nameof(TemporalAA.Settings.m_JitterScale));
            taaMipBias = taaSettings.FindPropertyRelative(nameof(TemporalAA.Settings.m_MipBias));
            taaVarianceClampScale = taaSettings.FindPropertyRelative(nameof(TemporalAA.Settings.m_VarianceClampScale));
            taaContrastAdaptiveSharpening = taaSettings.FindPropertyRelative(nameof(TemporalAA.Settings.m_ContrastAdaptiveSharpening));

            allowHDROutput = serializedAdditionalDataObject.FindProperty("m_AllowHDROutput");


            #region Upscale

            using (var o = new PropertyFetcher<UniversalAdditionalCameraData>(serializedAdditionalDataObject))
            {
                renderScale = o.Find(x => x.renderScale);
                upscalerTechnique = o.Find(x => x.upscalerTechnique);
                
                
                
                taaSharpenMode = serializedAdditionalDataObject.Find((UniversalAdditionalCameraData d) => d.taaSharpenMode);
                taaSharpenStrength = serializedAdditionalDataObject.Find((UniversalAdditionalCameraData d) => d.taaSharpenStrength);
                taaRingingReduction = serializedAdditionalDataObject.Find((UniversalAdditionalCameraData d) => d.taaRingingReduction);
                taaQualityLevel = serializedAdditionalDataObject.Find((UniversalAdditionalCameraData d) => d.TAAQuality);
                taaHistorySharpening = serializedAdditionalDataObject.Find((UniversalAdditionalCameraData d) => d.taaHistorySharpening);
                taaAntiFlicker = serializedAdditionalDataObject.Find((UniversalAdditionalCameraData d) => d.taaAntiFlicker);
                taaMotionVectorRejection = serializedAdditionalDataObject.Find((UniversalAdditionalCameraData d) => d.taaMotionVectorRejection);
                taaAntiRinging = serializedAdditionalDataObject.Find((UniversalAdditionalCameraData d) => d.taaAntiHistoryRinging);
                taaQualityLevel = serializedAdditionalDataObject.Find((UniversalAdditionalCameraData d) => d.TAAQuality);
                taaBaseBlendFactor = serializedAdditionalDataObject.Find((UniversalAdditionalCameraData d) => d.taaBaseBlendFactor);
                taaJitterScale = serializedAdditionalDataObject.Find((UniversalAdditionalCameraData d) => d.taaJitterScale);

                
            }

            #endregion
        }

        /// <summary>
        /// Updates the internal serialized objects
        /// </summary>
        public void Update()
        {
            UpdateInternal();

            if (cameraSerializedObjects == null || cameraSerializedObjects.Length != numCameras)
                Refresh();

            for (int i = 0; i < numCameras; ++i)
                cameraSerializedObjects[i]?.UpdateInternal();
        }

        private void UpdateInternal()
        {
            baseCameraSettings.Update();
            serializedObject.Update();
            serializedAdditionalDataObject.Update();
        }

        /// <summary>
        /// Applies the modified properties to the serialized objects
        /// </summary>
        public void Apply()
        {
            ApplyInternal();

            if (cameraSerializedObjects == null || cameraSerializedObjects.Length != numCameras)
                Refresh();

            for (int i = 0; i < numCameras; ++i)
                cameraSerializedObjects[i]?.ApplyInternal();
        }

        private void ApplyInternal()
        {
            baseCameraSettings.ApplyModifiedProperties();
            serializedObject.ApplyModifiedProperties();
            serializedAdditionalDataObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Refreshes the serialized properties from the serialized objects
        /// </summary>
        public void Refresh()
        {
            var o = new PropertyFetcher<UniversalAdditionalCameraData>(serializedAdditionalDataObject);
            cameras = o.Find("m_Cameras");

            cameraSerializedObjects = new VividSerializedCamera[numCameras];
            for (int i = 0; i < numCameras; ++i)
            {
                Camera cam = cameras.GetArrayElementAtIndex(i).objectReferenceValue as Camera;
                if (cam != null)
                    cameraSerializedObjects[i] = new VividSerializedCamera(new SerializedObject(cam));
            }
        }
    }
}
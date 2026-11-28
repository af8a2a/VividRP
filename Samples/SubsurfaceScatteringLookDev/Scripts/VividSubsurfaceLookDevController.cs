using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Samples.SubsurfaceScatteringLookDev
{
    public enum SubsurfaceLookDevCameraPreset
    {
        Overview = 0,
        HeroFaceCloseUp = 1,
        HeroThreeQuarter = 2,
        OpaqueVsSkinSplit = 3,
        WaxCloseUp = 4,
    }

    public enum SubsurfaceLookDevQualityMode
    {
        BlueNoise16Tap = 0,
        GoldenAngle32Tap = 1,
    }

    public enum SubsurfaceLookDevDebugMode
    {
        Composite = 0,
        ScatteredDiffuseOnly = 1,
        MeanFreePathRgb = 2,
    }

    [ExecuteAlways]
    public sealed class VividSubsurfaceLookDevController : MonoBehaviour
    {
        [Header("Studio References")]
        public Camera StudioCamera;
        public Volume StudioVolume;
        public GameObject StudioLabelsRoot;
        public TMP_Text StatusTelemetryText;

        [Header("Subsurface Scattering Controls")]
        public bool EnableSubsurfaceScattering = true;
        public SubsurfaceLookDevQualityMode QualityMode = SubsurfaceLookDevQualityMode.BlueNoise16Tap;
        public SubsurfaceLookDevDebugMode DebugMode = SubsurfaceLookDevDebugMode.Composite;
        [Range(0.0f, 3.0f)]
        public float GlobalWorldScale = 1.0f;
        [Range(8.0f, 96.0f)]
        public float MaxScreenRadiusPixels = 48.0f;
        public bool ShowStudioLabels = true;

        [Header("Camera Presets")]
        public SubsurfaceLookDevCameraPreset ActiveCameraPreset = SubsurfaceLookDevCameraPreset.Overview;
        public Transform OverviewRig;
        public Transform HeroFaceCloseUpRig;
        public Transform HeroThreeQuarterRig;
        public Transform OpaqueVsSkinSplitRig;
        public Transform WaxCloseUpRig;

        private void OnEnable()
        {
            ApplySettings();
        }

        private void OnValidate()
        {
            ApplySettings();
        }

        public void SetCameraPreset(SubsurfaceLookDevCameraPreset preset)
        {
            ActiveCameraPreset = preset;
            ApplyCameraPreset();
            UpdateTelemetryText();
        }

        public void ApplyCameraPreset()
        {
            if (StudioCamera == null)
                return;

            Transform target = ActiveCameraPreset switch
            {
                SubsurfaceLookDevCameraPreset.HeroFaceCloseUp => HeroFaceCloseUpRig,
                SubsurfaceLookDevCameraPreset.HeroThreeQuarter => HeroThreeQuarterRig,
                SubsurfaceLookDevCameraPreset.OpaqueVsSkinSplit => OpaqueVsSkinSplitRig,
                SubsurfaceLookDevCameraPreset.WaxCloseUp => WaxCloseUpRig,
                _ => OverviewRig,
            };

            if (target != null)
                StudioCamera.transform.SetPositionAndRotation(target.position, target.rotation);
        }

        [ContextMenu("Apply Subsurface LookDev Settings")]
        public void ApplySettings()
        {
            SubsurfaceScatteringPass.EnableSubsurfaceScattering = EnableSubsurfaceScattering;
            SubsurfaceScatteringPass.QualityMode = (int)QualityMode;
            SubsurfaceScatteringPass.DebugMode = (int)DebugMode;
            SubsurfaceScatteringPass.GlobalWorldScale = GlobalWorldScale;
            SubsurfaceScatteringPass.MaxScreenRadiusPixels = MaxScreenRadiusPixels;

            if (StudioLabelsRoot != null)
                StudioLabelsRoot.SetActive(ShowStudioLabels);

            ApplyCameraPreset();
            UpdateTelemetryText();
        }

        public void UpdateTelemetryText()
        {
            if (StatusTelemetryText == null)
                return;

            string qualityLabel = QualityMode == SubsurfaceLookDevQualityMode.BlueNoise16Tap
                ? "16-TAP STBN BLUE-NOISE"
                : "32-TAP DETERMINISTIC GOLDEN-ANGLE";
            string debugLabel = DebugMode switch
            {
                SubsurfaceLookDevDebugMode.ScatteredDiffuseOnly => "DEBUG: SCATTERED DIFFUSE",
                SubsurfaceLookDevDebugMode.MeanFreePathRgb => "DEBUG: RGB MEAN FREE PATH",
                _ => "COMPOSITE",
            };

            StatusTelemetryText.text =
                $"MODEL: LEE PERRY-SMITH HEAD SCAN (CC-BY 3.0)  |  SSS: {(EnableSubsurfaceScattering ? "ON" : "OFF")}  |  {qualityLabel}  |  {debugLabel}\n" +
                "STATION 01: OPAQUE REFERENCE (MFP 0 mm)  |  STATION 02: HUMAN SKIN (MFP 6.5 mm, RGB 1.0/0.34/0.18)  |  STATION 03: WARM WAX (MFP 18 mm)";
        }
    }
}

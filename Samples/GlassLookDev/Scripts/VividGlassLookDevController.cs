using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Samples.GlassLookDev
{
    public enum GlassLookDevCameraPreset
    {
        Overview = 0,
        AmberDragonCloseUp = 1,
        FourPresetsRow = 2,
        DispersionCloseUp = 3,
        SideProfile = 4,
    }

    public enum GlassLookDevDebugMode
    {
        Composite = 0,
        ThicknessMeters = 1,
        BackfaceNormal = 2,
        BeerLambertTransmittance = 3,
    }

    [ExecuteAlways]
    public sealed class VividGlassLookDevController : MonoBehaviour
    {
        [Header("Studio References")]
        public Camera StudioCamera;
        public Volume StudioVolume;
        public GameObject StudioLabelsRoot;
        public TMP_Text StatusTelemetryText;

        [Header("Refraction Controls")]
        public bool EnableRefraction = true;
        public bool EnableDispersion = true;
        public bool EnableBackfaceRefraction = true;
        public GlassLookDevDebugMode DebugMode = GlassLookDevDebugMode.Composite;
        public bool ShowStudioLabels = true;

        [Header("Camera Presets")]
        public GlassLookDevCameraPreset ActiveCameraPreset = GlassLookDevCameraPreset.Overview;
        public Transform OverviewRig;
        public Transform AmberDragonCloseUpRig;
        public Transform FourPresetsRowRig;
        public Transform DispersionCloseUpRig;
        public Transform SideProfileRig;

        private void OnEnable()
        {
            ApplySettings();
        }

        private void OnValidate()
        {
            ApplySettings();
        }

        public void SetCameraPreset(GlassLookDevCameraPreset preset)
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
                GlassLookDevCameraPreset.AmberDragonCloseUp => AmberDragonCloseUpRig,
                GlassLookDevCameraPreset.FourPresetsRow => FourPresetsRowRig,
                GlassLookDevCameraPreset.DispersionCloseUp => DispersionCloseUpRig,
                GlassLookDevCameraPreset.SideProfile => SideProfileRig,
                _ => OverviewRig,
            };

            if (target != null)
                StudioCamera.transform.SetPositionAndRotation(target.position, target.rotation);
        }

        [ContextMenu("Apply Glass LookDev Settings")]
        public void ApplySettings()
        {
            ForwardRefractionPass.EnableRefraction = EnableRefraction;
            ForwardRefractionPass.EnableDispersion = EnableDispersion;
            ForwardRefractionPass.EnableBackfaceRefraction = EnableBackfaceRefraction;
            ForwardRefractionPass.DebugMode = (int)DebugMode;

            if (StudioLabelsRoot != null)
                StudioLabelsRoot.SetActive(ShowStudioLabels);

            ApplyCameraPreset();
            UpdateTelemetryText();
        }

        public void UpdateTelemetryText()
        {
            if (StatusTelemetryText == null)
                return;

            string debugLabel = DebugMode switch
            {
                GlassLookDevDebugMode.ThicknessMeters => "DEBUG: THICKNESS",
                GlassLookDevDebugMode.BackfaceNormal => "DEBUG: BACKFACE NORMAL",
                GlassLookDevDebugMode.BeerLambertTransmittance => "DEBUG: BEER-LAMBERT",
                _ => "COMPOSITE",
            };

            StatusTelemetryText.text =
                $"REFRACTION: {(EnableRefraction ? "ON" : "OFF")}  |  BACKFACE SNELL: {(EnableBackfaceRefraction ? "ON" : "OFF")}  |  ABBE DISPERSION: {(EnableDispersion ? "ON" : "OFF")}  |  {debugLabel}\n" +
                "HERO: DRAGON ATTENUATION (AMBER IOR 1.50)  |  01 CLEAR  |  02 FROSTED (MIP BLUR)  |  03 EMERALD (BEER-LAMBERT)  |  04 CRYSTAL (IOR 1.85 DISPERSION)";
        }
    }
}

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
        VolumeSingleScatter = 4,
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

        [Header("Phase 2 Caustics Controls")]
        public bool EnableCaustics = true;
        [Range(0.0f, 3.0f)]
        public float CausticsIntensity = 1.25f;
        [Range(0.0f, 1.0f)]
        public float ColoredShadowStrength = 1.0f;
        public bool ShowCausticsDebugOnly = false;
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

            TransmissiveCausticsPass.EnableCaustics = EnableCaustics;
            TransmissiveCausticsPass.CausticsIntensity = CausticsIntensity;
            TransmissiveCausticsPass.ColoredShadowStrength = ColoredShadowStrength;
            TransmissiveCausticsPass.DebugMode = ShowCausticsDebugOnly ? 1 : 0;

            if (StudioLabelsRoot != null)
                StudioLabelsRoot.SetActive(ShowStudioLabels);

            ApplyCameraPreset();
            UpdateTelemetryText();
        }

        public void UpdateTelemetryText()
        {
            if (StatusTelemetryText == null)
                return;

            string debugLabel = ShowCausticsDebugOnly
                ? "DEBUG: CAUSTICS"
                : DebugMode switch
                {
                    GlassLookDevDebugMode.ThicknessMeters => "DEBUG: THICKNESS",
                    GlassLookDevDebugMode.BackfaceNormal => "DEBUG: BACKFACE NORMAL",
                    GlassLookDevDebugMode.BeerLambertTransmittance => "DEBUG: BEER-LAMBERT",
                    GlassLookDevDebugMode.VolumeSingleScatter => "DEBUG: VOLUME SCATTER",
                    _ => "COMPOSITE",
                };

            StatusTelemetryText.text =
                $"REFRACTION: {(EnableRefraction ? "ON" : "OFF")}  |  CAUSTICS: {(EnableCaustics ? "ON" : "OFF")}  |  DISPERSION: {(EnableDispersion ? "ON" : "OFF")}  |  {debugLabel}\n" +
                "HERO: AMBER DRAGON (BEER-LAMBERT + CAUSTICS + BACKFACE SPEC)  |  01 CLEAR  |  02 FROSTED OPALESCENT (HG SCATTER)  |  03 EMERALD  |  04 CRYSTAL";
        }
    }
}

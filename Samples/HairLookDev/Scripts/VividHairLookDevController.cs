using System;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Samples.HairLookDev
{
    public enum HairLookDevCameraPreset
    {
        Overview = 0,
        BustFrontThreeQuarter = 1,
        BustBacklitRim = 2,
        SphereCloseUp = 3,
        RoughnessComparison = 4,
    }

    public enum HairLookDevRenderMode
    {
        ReferencePathTracingDots = 0,
        RealtimeStrandVisibilityBuffer = 1,
    }

    [ExecuteAlways]
    public sealed class VividHairLookDevController : MonoBehaviour
    {
        [Header("Studio References")]
        public Camera StudioCamera;
        public Volume StudioVolume;
        public HairGroomRenderer SphereGroom;
        public HairGroomRenderer BustGroom;
        public HairGroomRenderer BustMatteGroom;
        public Renderer BustShadowProxyRenderer;
        public Renderer BustMatteShadowProxyRenderer;
        public GameObject StudioLabelsRoot;
        public TMP_Text StatusTelemetryText;

        [Header("LookDev Mode")]
        public HairLookDevRenderMode RenderMode = HairLookDevRenderMode.RealtimeStrandVisibilityBuffer;
        public HairLookDevCameraPreset ActiveCameraPreset = HairLookDevCameraPreset.Overview;
        public bool ShowShadowProxyInRealtime = false;

        [Header("Camera Rig Transforms")]
        public Transform OverviewRig;
        public Transform BustFrontRig;
        public Transform BustBacklitRig;
        public Transform SphereCloseUpRig;
        public Transform RoughnessCompareRig;

        private void OnEnable()
        {
            ApplyModeAndTelemetry();
        }

        private void OnValidate()
        {
            ApplyModeAndTelemetry();
        }

        public void SetCameraPreset(HairLookDevCameraPreset preset)
        {
            ActiveCameraPreset = preset;
            ApplyCameraPreset();
        }

        public void ApplyCameraPreset()
        {
            if (StudioCamera == null)
                return;

            Transform target = ActiveCameraPreset switch
            {
                HairLookDevCameraPreset.BustFrontThreeQuarter => BustFrontRig,
                HairLookDevCameraPreset.BustBacklitRim => BustBacklitRig,
                HairLookDevCameraPreset.SphereCloseUp => SphereCloseUpRig,
                HairLookDevCameraPreset.RoughnessComparison => RoughnessCompareRig,
                _ => OverviewRig,
            };

            if (target != null)
            {
                StudioCamera.transform.SetPositionAndRotation(target.position, target.rotation);
            }
        }

        [ContextMenu("Apply LookDev Mode & Rebuild Proxies")]
        public void ApplyModeAndTelemetry()
        {
            bool enablePtProxy = RenderMode == HairLookDevRenderMode.ReferencePathTracingDots;

            if (SphereGroom != null)
            {
                SphereGroom.EnablePathTracingDotsProxy = enablePtProxy;
                if (enablePtProxy)
                    SphereGroom.RebuildDotsProxyNow();
            }

            if (BustGroom != null)
            {
                BustGroom.EnablePathTracingDotsProxy = enablePtProxy;
                if (enablePtProxy)
                    BustGroom.RebuildDotsProxyNow();
            }

            if (BustMatteGroom != null)
            {
                BustMatteGroom.EnablePathTracingDotsProxy = enablePtProxy;
                if (enablePtProxy)
                    BustMatteGroom.RebuildDotsProxyNow();
            }

            if (BustShadowProxyRenderer != null)
            {
                BustShadowProxyRenderer.enabled = !enablePtProxy && ShowShadowProxyInRealtime;
            }

            if (BustMatteShadowProxyRenderer != null)
            {
                BustMatteShadowProxyRenderer.enabled = !enablePtProxy && ShowShadowProxyInRealtime;
            }

            if (StudioLabelsRoot != null)
            {
                StudioLabelsRoot.SetActive(!enablePtProxy);
            }

            ApplyCameraPreset();
            UpdateTelemetryText();
        }

        public void UpdateTelemetryText()
        {
            if (StatusTelemetryText == null)
                return;

            int sphereStrands = SphereGroom != null && SphereGroom.GroomAsset != null
                ? SphereGroom.GroomAsset.StrandCount
                : 0;
            int sphereSegs = SphereGroom != null && SphereGroom.GroomAsset != null
                ? SphereGroom.GroomAsset.SegmentCount
                : 0;
            int bustStrands = BustGroom != null && BustGroom.GroomAsset != null
                ? BustGroom.GroomAsset.StrandCount
                : 0;
            int bustSegs = BustGroom != null && BustGroom.GroomAsset != null
                ? BustGroom.GroomAsset.SegmentCount
                : 0;

            int matteStrands = BustMatteGroom != null && BustMatteGroom.GroomAsset != null
                ? BustMatteGroom.GroomAsset.StrandCount
                : 0;
            int matteSegs = BustMatteGroom != null && BustMatteGroom.GroomAsset != null
                ? BustMatteGroom.GroomAsset.SegmentCount
                : 0;

            string modeLabel = RenderMode == HairLookDevRenderMode.ReferencePathTracingDots
                ? "REFERENCE PT (DXR DOTS + CHIANG)"
                : "REALTIME STRAND VISBUF (3-LAYER ATLAS)";

            string matteLine = matteStrands > 0
                ? $"\nSTATION 03 MATTE:  {matteStrands:N0} STRANDS / {matteSegs:N0} SEGS (R=0.48)"
                : string.Empty;

            StatusTelemetryText.text =
                $"MODE: {modeLabel}\n"
                + $"STATION 01 SPHERE: {sphereStrands:N0} STRANDS / {sphereSegs:N0} SEGS (R=0.26)\n"
                + $"STATION 02 GLOSSY: {bustStrands:N0} STRANDS / {bustSegs:N0} SEGS (R=0.28)"
                + matteLine;
        }
    }
}

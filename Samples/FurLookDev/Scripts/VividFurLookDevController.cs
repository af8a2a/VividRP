using System;
using System.Globalization;
using System.Text;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Samples.FurLookDev
{
    public enum Yan2017SpeciesPreset
    {
        Human = 0,
        Cat = 1,
        Rabbit = 2,
        Raccoon = 3,
        Deer = 4,
    }

    [ExecuteAlways]
    public sealed class VividFurLookDevController : MonoBehaviour
    {
        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_RoughnessParamsId = Shader.PropertyToID("_RoughnessParams");
        private static readonly int s_MedullaParamsId = Shader.PropertyToID("_MedullaParams");
        private static readonly int s_GonioParamsId = Shader.PropertyToID("_GonioParams");
        private static readonly int s_YanMedullaParamsId = Shader.PropertyToID("_YanMedullaParams");
        private static readonly int s_OutRId = Shader.PropertyToID("_OutR");
        private static readonly int s_OutTTId = Shader.PropertyToID("_OutTT");
        private static readonly int s_OutTRTId = Shader.PropertyToID("_OutTRT");
        private static readonly int s_OutMedullaId = Shader.PropertyToID("_OutMedulla");
        private static readonly int s_OutTotalId = Shader.PropertyToID("_OutTotal");
        private static readonly int s_MetricsOutId = Shader.PropertyToID("_MetricsOut");
        private static readonly int s_FurnaceGridSrvId = Shader.PropertyToID("_FurnaceGridSRV");
        private static readonly int s_UnlitColorMapId = Shader.PropertyToID("_UnlitColorMap");

        [Header("Compute & Studio Volume")]
        public ComputeShader DiagnosticsCompute;
        public Volume StudioVolume;
        [Tooltip("Toggle between Studio LookDev (ACES tonemap, fixed EV100) and Linear White Furnace (Tonemap=None, GI=Off).")]
        public bool WhiteFurnaceLinearMode;

        [Header("Case 01: Single Strand Goniometer")]
        public Color GoniometerBaseColor = new Color(0.62f, 0.34f, 0.16f, 1.0f);
        [Range(0.05f, 1.0f)] public float GoniometerRoughness = 0.28f;
        [Range(0.05f, 1.0f)] public float GoniometerRadialRoughness = 0.36f;
        [Range(0.0f, 10.0f)] public float GoniometerCuticleDeg = 3.0f;
        [Range(-60.0f, 60.0f)] public float GoniometerViewThetaDeg = 0.0f;
        [Range(0.0f, 1.0f)] public float GoniometerMedullaWeight = 0.35f;
        [Range(0.1f, 1.0f)] public float GoniometerMedullaRoughness = 0.65f;

        public Renderer Board01_R;
        public Renderer Board01_TT;
        public Renderer Board01_TRT;
        public Renderer Board01_Medulla;
        public Renderer Board01_Total;
        public TMP_Text Telemetry01_Text;

        [Header("Case 02: White Furnace")]
        public Renderer Board02_SingleScatter;
        public Renderer Board02_FullDualScatter;
        public TMP_Text Telemetry02_Text;

        [Header("Case 03: Medulla Parameter Sweep & Yan 2017 Dataset")]
        public Yan2017SpeciesPreset YanSpecies = Yan2017SpeciesPreset.Rabbit;
        public TextAsset YanHumanData;
        public TextAsset YanCatData;
        public TextAsset YanRabbitData;
        public TextAsset YanRaccoonData;
        public TextAsset YanDeerData;

        public Renderer Board03_SweepCurves;
        public Renderer Board03_YanReference;
        public Renderer Board03_VividMatch;
        public TMP_Text Telemetry03_Text;

        [NonSerialized] public string LastReportSummary = string.Empty;

        private RenderTexture m_RtGonioR;
        private RenderTexture m_RtGonioTT;
        private RenderTexture m_RtGonioTRT;
        private RenderTexture m_RtGonioMedulla;
        private RenderTexture m_RtGonioTotal;
        private RenderTexture m_RtFurnaceSingle;
        private RenderTexture m_RtFurnaceFull;
        private RenderTexture m_RtYanVivid;
        private Texture2D m_TexMedullaSweepPlot;
        private Texture2D m_TexYanReference;
        private MaterialPropertyBlock m_Mpb;

        private void OnEnable()
        {
            if (DiagnosticsCompute != null)
                RunAllDiagnostics();
        }

        private void OnDisable()
        {
            ReleaseResources();
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        private void OnValidate()
        {
            ApplyStudioVolumeMode();
        }

        [ContextMenu("Run All FurLookDev Diagnostics")]
        public string RunAllDiagnostics()
        {
            ApplyStudioVolumeMode();
            if (DiagnosticsCompute == null)
            {
                LastReportSummary = "[FurLookDev] DiagnosticsCompute is not assigned.";
                return LastReportSummary;
            }

            EnsureRenderTargets();
            m_Mpb ??= new MaterialPropertyBlock();

            var report = new StringBuilder(2048);
            RunCase01Goniometer(report);
            RunCase02WhiteFurnace(report);
            RunCase03MedullaSweepAndYan(report);

            LastReportSummary = report.ToString();
            return LastReportSummary;
        }

        public void ApplyStudioVolumeMode()
        {
            if (StudioVolume == null || StudioVolume.sharedProfile == null)
                return;

            VolumeProfile profile = StudioVolume.sharedProfile;
            if (profile.TryGet(out Tonemapping tonemapping))
            {
                tonemapping.mode.overrideState = true;
                tonemapping.mode.value = WhiteFurnaceLinearMode ? TonemappingMode.None : TonemappingMode.ACES;
            }
            if (profile.TryGet(out AutoExposure exposure))
            {
                exposure.active = true;
                exposure.enabled.overrideState = true;
                exposure.enabled.value = true;
                exposure.exposureMode.overrideState = true;
                exposure.exposureMode.value = AutoExposureExposureMode.Fixed;
                exposure.fixedExposure.overrideState = true;
                exposure.fixedExposure.value = WhiteFurnaceLinearMode ? 0.0f : 0.0f;
                exposure.mode.overrideState = true;
                exposure.mode.value = AutoExposureMode.Manual;
                exposure.manualEV100.overrideState = true;
                exposure.manualEV100.value = WhiteFurnaceLinearMode ? 0.0f : 0.0f;
            }
            if (profile.TryGet(out LumenIndirectDiffuse lumen))
            {
                lumen.active = !WhiteFurnaceLinearMode;
            }
        }

        private void RunCase01Goniometer(StringBuilder report)
        {
            Color linBase = GoniometerBaseColor.linear;
            DiagnosticsCompute.SetVector(s_BaseColorId, new Vector4(linBase.r, linBase.g, linBase.b, 0.5f));
            DiagnosticsCompute.SetVector(s_RoughnessParamsId, new Vector4(
                GoniometerRoughness, GoniometerRadialRoughness, GoniometerCuticleDeg, 1.0f));
            DiagnosticsCompute.SetVector(s_MedullaParamsId, new Vector4(
                GoniometerMedullaWeight, GoniometerMedullaRoughness, 0.0f, 1.0f));
            DiagnosticsCompute.SetVector(s_GonioParamsId, new Vector4(
                GoniometerViewThetaDeg, 24.0f, 0.0f, 0.0f));

            int k2d = DiagnosticsCompute.FindKernel("EvaluateGoniometer2D");
            DiagnosticsCompute.SetTexture(k2d, s_OutRId, m_RtGonioR);
            DiagnosticsCompute.SetTexture(k2d, s_OutTTId, m_RtGonioTT);
            DiagnosticsCompute.SetTexture(k2d, s_OutTRTId, m_RtGonioTRT);
            DiagnosticsCompute.SetTexture(k2d, s_OutMedullaId, m_RtGonioMedulla);
            DiagnosticsCompute.SetTexture(k2d, s_OutTotalId, m_RtGonioTotal);
            DiagnosticsCompute.Dispatch(k2d, 256 / 8, 256 / 8, 1);

            BindBoardTexture(Board01_R, m_RtGonioR);
            BindBoardTexture(Board01_TT, m_RtGonioTT);
            BindBoardTexture(Board01_TRT, m_RtGonioTRT);
            BindBoardTexture(Board01_Medulla, m_RtGonioMedulla);
            BindBoardTexture(Board01_Total, m_RtGonioTotal);

            int kStats = DiagnosticsCompute.FindKernel("EvaluateGoniometerStats");
            var statsData = new float4[262];
            using (var buf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, statsData.Length, sizeof(float) * 4))
            {
                DiagnosticsCompute.SetBuffer(kStats, s_MetricsOutId, buf);
                DiagnosticsCompute.Dispatch(kStats, 2, 1, 1);
                buf.GetData(statsData);
            }

            ComputePeakAndFwhm(statsData, 0, 128, v => v.x, out float rPeakDeg, out float rFwhmDeg);
            ComputePeakAndFwhm(statsData, 0, 128, v => v.y, out float trtPeakDeg, out float trtFwhmDeg);
            ComputePeakAndFwhm(statsData, 128, 128, v => v.x, out float ttPeakDeg, out float ttFwhmDeg);
            ComputePeakAndFwhm(statsData, 0, 128, v => v.z, out float medPeakDeg, out float medFwhmDeg);

            float eR = statsData[256].w;
            float eTT = statsData[257].w;
            float eTRT = statsData[258].w;
            float eMed = statsData[259].w;
            float eTot = statsData[260].x * 0.2126f + statsData[260].y * 0.7152f + statsData[260].z * 0.0722f;
            float trtTintRB = statsData[261].x / Mathf.Max(statsData[261].z, 1e-5f);

            string plaque =
                $"CASE 01 — SINGLE STRAND GONIOMETER (alpha={GoniometerCuticleDeg:F1} deg, betaM={GoniometerRoughness:F2}, betaN={GoniometerRadialRoughness:F2})\n" +
                $"  R       Peak: {rPeakDeg:+0.0;-0.0} deg (exp {2f * GoniometerCuticleDeg - GoniometerViewThetaDeg:+0.0;-0.0}) | FWHM: {rFwhmDeg:F1} deg | Energy: {eR:F4}\n" +
                $"  TT      Peak: {ttPeakDeg:+0.0;-0.0} deg (exp {-GoniometerCuticleDeg - GoniometerViewThetaDeg:+0.0;-0.0}) | FWHM: {ttFwhmDeg:F1} deg | Energy: {eTT:F4}\n" +
                $"  TRT     Peak: {trtPeakDeg:+0.0;-0.0} deg (exp {-4f * GoniometerCuticleDeg - GoniometerViewThetaDeg:+0.0;-0.0}) | FWHM: {trtFwhmDeg:F1} deg | Energy: {eTRT:F4} | R/B Tint: {trtTintRB:F2}x\n" +
                $"  Medulla Peak: {medPeakDeg:+0.0;-0.0} deg | FWHM: {medFwhmDeg:F1} deg | Energy: {eMed:F4} | Total E: {eTot:F4}";

            SetTmpText(Telemetry01_Text, plaque);
            report.AppendLine(plaque);
        }

        private void RunCase02WhiteFurnace(StringBuilder report)
        {
            DiagnosticsCompute.SetVector(s_BaseColorId, new Vector4(1f, 1f, 1f, 0.5f));
            DiagnosticsCompute.SetVector(s_RoughnessParamsId, new Vector4(0.35f, 0.45f, 2.5f, 1.0f));
            DiagnosticsCompute.SetVector(s_MedullaParamsId, new Vector4(0.0f, 0.75f, 1.0f, 0.5f));

            int kGrid = DiagnosticsCompute.FindKernel("EvaluateWhiteFurnaceGrid");
            int kBoard = DiagnosticsCompute.FindKernel("RenderWhiteFurnaceBoard");
            var gridData = new float4[256];

            using (var buf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 256, sizeof(float) * 4))
            {
                DiagnosticsCompute.SetBuffer(kGrid, s_MetricsOutId, buf);
                DiagnosticsCompute.Dispatch(kGrid, 16 / 8, 16 / 8, 1);
                buf.GetData(gridData);

                DiagnosticsCompute.SetBuffer(kBoard, s_FurnaceGridSrvId, buf);
                DiagnosticsCompute.SetTexture(kBoard, s_OutTotalId, m_RtFurnaceFull);
                DiagnosticsCompute.SetTexture(kBoard, s_OutRId, m_RtFurnaceSingle);
                DiagnosticsCompute.Dispatch(kBoard, 256 / 8, 256 / 8, 1);
            }

            BindBoardTexture(Board02_SingleScatter, m_RtFurnaceSingle);
            BindBoardTexture(Board02_FullDualScatter, m_RtFurnaceFull);

            float minSingle = float.MaxValue, maxSingle = float.MinValue, sumSingle = 0f;
            float minFull = float.MaxValue, maxFull = float.MinValue, sumFull = 0f;
            int violations = 0, badSamples = 0;

            for (int i = 0; i < 256; i++)
            {
                float full = gridData[i].x;
                float single = gridData[i].y;
                if (gridData[i].w > 0.5f || !float.IsFinite(full) || !float.IsFinite(single))
                    badSamples++;
                if (full > 1.05f || single > 1.05f)
                    violations++;
                minSingle = Mathf.Min(minSingle, single);
                maxSingle = Mathf.Max(maxSingle, single);
                sumSingle += single;
                minFull = Mathf.Min(minFull, full);
                maxFull = Mathf.Max(maxFull, full);
                sumFull += full;
            }

            string status = (violations == 0 && badSamples == 0) ? "PASS (Energy Conserving <= 1.05)" : $"FAIL (Violations={violations}, NaN={badSamples})";
            string plaque =
                $"CASE 02 — WHITE FURNACE (16x16 rough x medulla, 2048 Fibonacci S^2, Albedo=1, No GI/Tonemap)\n" +
                $"  Single-Scatter Integral : Min={minSingle:F3} | Mean={sumSingle / 256f:F3} | Max={maxSingle:F3}\n" +
                $"  Full + Dual-Scatter     : Min={minFull:F3} | Mean={sumFull / 256f:F3} | Max={maxFull:F3}\n" +
                $"  Energy Violations (>1.05): {violations}/256 | NaN/Neg: {badSamples} | Status: {status}";

            SetTmpText(Telemetry02_Text, plaque);
            report.AppendLine(plaque);
        }

        private void RunCase03MedullaSweepAndYan(StringBuilder report)
        {
            Color linBase = GoniometerBaseColor.linear;
            DiagnosticsCompute.SetVector(s_BaseColorId, new Vector4(linBase.r, linBase.g, linBase.b, 0.5f));
            DiagnosticsCompute.SetVector(s_RoughnessParamsId, new Vector4(0.32f, 0.42f, 2.5f, 1.0f));
            DiagnosticsCompute.SetVector(s_MedullaParamsId, new Vector4(0.5f, 0.70f, 0.0f, 1.0f));

            int kSweep = DiagnosticsCompute.FindKernel("EvaluateMedullaSweep");
            var sweepData = new float4[272];
            using (var buf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, sweepData.Length, sizeof(float) * 4))
            {
                DiagnosticsCompute.SetBuffer(kSweep, s_MetricsOutId, buf);
                DiagnosticsCompute.Dispatch(kSweep, 1, 1, 1);
                buf.GetData(sweepData);
            }

            // Analyze Case 03 invariants:
            // 1. kappa == 0 degeneration (medulla term must be exactly 0.0)
            float medAtZeroKappa = sweepData[0].z;
            // 2. R invariance across kappa sweep [0..0.95]
            float maxRDrift = 0f;
            float maxJump = 0f;
            for (int i = 1; i < 64; i++)
            {
                maxRDrift = Mathf.Max(maxRDrift, Mathf.Abs(sweepData[i].x - sweepData[0].x));
                maxJump = Mathf.Max(maxJump, Mathf.Abs(sweepData[i].w - sweepData[i - 1].w));
            }
            float wideAngleGain = sweepData[63].z / Mathf.Max(sweepData[0].w, 1e-4f);
            float cortexAtten = sweepData[63].y / Mathf.Max(sweepData[0].y, 1e-4f);

            int stressPassed = 0;
            for (int i = 0; i < 16; i++)
            {
                if (sweepData[256 + i].w > 0.5f)
                    stressPassed++;
            }

            // Render 4-panel curve plot texture for Bay 03
            DrawMedullaSweepPlotTexture(sweepData);
            BindBoardTexture(Board03_SweepCurves, m_TexMedullaSweepPlot);

            // Yan et al. (2017) dataset parsing + matched VividRP gantry render
            GetYanSpeciesPreset(YanSpecies, out TextAsset yanAsset, out float kappa, out float sigmaS, out float sigmaA, out float gAniso, out Color speciesColor);
            float[,] yanGrid = ParseYanFittedTxt(yanAsset, out int yanPhiCount, out int yanThetaCount);
            RenderYanReferenceTexture(yanGrid, yanPhiCount, yanThetaCount);
            BindBoardTexture(Board03_YanReference, m_TexYanReference);

            Color sLin = speciesColor.linear;
            DiagnosticsCompute.SetVector(s_BaseColorId, new Vector4(sLin.r, sLin.g, sLin.b, 0.5f));
            DiagnosticsCompute.SetVector(s_YanMedullaParamsId, new Vector4(kappa, sigmaS, sigmaA, gAniso));
            DiagnosticsCompute.SetVector(s_GonioParamsId, new Vector4(-30.0f, 18.0f, kappa, 0.0f));

            int kYan = DiagnosticsCompute.FindKernel("RenderYanGantryComparison");
            DiagnosticsCompute.SetTexture(kYan, s_OutTotalId, m_RtYanVivid);
            DiagnosticsCompute.Dispatch(kYan, 256 / 8, 128 / 8, 1);
            BindBoardTexture(Board03_VividMatch, m_RtYanVivid);

            float corr = ComputeYanHeatmapCorrelation(m_TexYanReference, m_RtYanVivid);

            string plaque =
                $"CASE 03 — MEDULLA SWEEP (kappa, sigma_s, sigma_a, g) & YAN 2017 [{YanSpecies}]\n" +
                $"  Degeneration (kappa=0) : Medulla={medAtZeroKappa:F6} | R-Invariance Drift={maxRDrift:F6} | Cortex@0.95={cortexAtten:F2}x\n" +
                $"  Wide-Angle Lift (90d)  : +{wideAngleGain:F2}x | Continuity MaxStep={maxJump:F4} | Stress Corners: {stressPassed}/16 Finite & >=0\n" +
                $"  Yan 2017 Preset        : kappa={kappa:F2}, sigma_s={sigmaS:F1}, sigma_a={sigmaA:F2}, g={gAniso:+0.00;-0.00} | Log-Profile Corr r={corr:F3}";

            SetTmpText(Telemetry03_Text, plaque);
            report.AppendLine(plaque);
        }

        private void GetYanSpeciesPreset(
            Yan2017SpeciesPreset preset,
            out TextAsset data,
            out float kappa,
            out float sigmaS,
            out float sigmaA,
            out float g,
            out Color albedo)
        {
            switch (preset)
            {
                case Yan2017SpeciesPreset.Human:
                    data = YanHumanData; kappa = 0.00f; sigmaS = 0.0f; sigmaA = 0.25f; g = 0.00f; albedo = new Color(0.28f, 0.16f, 0.10f); break;
                case Yan2017SpeciesPreset.Cat:
                    data = YanCatData; kappa = 0.45f; sigmaS = 2.4f; sigmaA = 0.45f; g = 0.15f; albedo = new Color(0.55f, 0.42f, 0.30f); break;
                case Yan2017SpeciesPreset.Raccoon:
                    data = YanRaccoonData; kappa = 0.62f; sigmaS = 3.8f; sigmaA = 0.55f; g = 0.10f; albedo = new Color(0.42f, 0.36f, 0.30f); break;
                case Yan2017SpeciesPreset.Deer:
                    data = YanDeerData; kappa = 0.78f; sigmaS = 5.5f; sigmaA = 0.30f; g = -0.15f; albedo = new Color(0.68f, 0.56f, 0.42f); break;
                default: // Rabbit
                    data = YanRabbitData; kappa = 0.72f; sigmaS = 4.8f; sigmaA = 0.20f; g = 0.05f; albedo = new Color(0.76f, 0.66f, 0.54f); break;
            }
        }

        private static float[,] ParseYanFittedTxt(TextAsset asset, out int phiCount, out int thetaCount)
        {
            // Yan 2017 fitted txt files span phi in [70..290] step 5 (45 values) x theta in [40..80] step 2 (21 values).
            // A few occluded gantry-arm columns are marked '?' and smoothly interpolated from valid neighbors.
            phiCount = 45;
            thetaCount = 21;
            var grid = new float[thetaCount, phiCount];
            var valid = new bool[thetaCount, phiCount];
            if (asset == null || string.IsNullOrEmpty(asset.text))
                return grid;

            string[] lines = asset.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3 || parts[2] == "?") continue;
                if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float phi)) continue;
                if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float theta)) continue;
                if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float val)) continue;

                // Gantry mapping from Yan 2017 page: theta in [40..80] -> [50..10] deg, phi in [70..290] -> [-20..200] deg
                int pi = Mathf.Clamp(Mathf.RoundToInt((phi - 70.0f) / 5.0f), 0, phiCount - 1);
                int ti = Mathf.Clamp(Mathf.RoundToInt((80.0f - theta) * 0.5f), 0, thetaCount - 1); // ti=0 -> 10 deg, ti=20 -> 50 deg
                grid[ti, pi] = Mathf.Max(val, 0.0f);
                valid[ti, pi] = true;
            }

            // Fill occluded '?' gantry columns along phi by linear interpolation
            for (int ti = 0; ti < thetaCount; ti++)
            {
                for (int pi = 0; pi < phiCount; pi++)
                {
                    if (valid[ti, pi]) continue;
                    int left = pi - 1;
                    while (left >= 0 && !valid[ti, left]) left--;
                    int right = pi + 1;
                    while (right < phiCount && !valid[ti, right]) right++;
                    if (left >= 0 && right < phiCount)
                        grid[ti, pi] = Mathf.Lerp(grid[ti, left], grid[ti, right], (pi - left) / (float)(right - left));
                    else if (left >= 0)
                        grid[ti, pi] = grid[ti, left];
                    else if (right < phiCount)
                        grid[ti, pi] = grid[ti, right];
                }
            }
            return grid;
        }

        private void RenderYanReferenceTexture(float[,] grid, int phiCount, int thetaCount)
        {
            const int W = 256, H = 128;
            if (m_TexYanReference == null || m_TexYanReference.width != W || m_TexYanReference.height != H)
            {
                if (m_TexYanReference != null) DestroyImmediate(m_TexYanReference);
                m_TexYanReference = new Texture2D(W, H, TextureFormat.RGBA32, false, false)
                {
                    name = "FurLookDev_Yan2017_Ref",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }

            float maxVal = 1e-4f;
            for (int ty = 0; ty < thetaCount; ty++)
                for (int px = 0; px < phiCount; px++)
                    maxVal = Mathf.Max(maxVal, grid[ty, px]);

            var pixels = new Color[W * H];
            for (int y = 0; y < H; y++)
            {
                float vf = ((y + 0.5f) / H) * (thetaCount - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(vf), 0, thetaCount - 1);
                int y1 = Mathf.Min(y0 + 1, thetaCount - 1);
                float fy = vf - y0;

                for (int x = 0; x < W; x++)
                {
                    float uf = ((x + 0.5f) / W) * (phiCount - 1);
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(uf), 0, phiCount - 1);
                    int x1 = Mathf.Min(x0 + 1, phiCount - 1);
                    float fx = uf - x0;

                    float v00 = grid[y0, x0], v10 = grid[y0, x1];
                    float v01 = grid[y1, x0], v11 = grid[y1, x1];
                    float sample = Mathf.Lerp(Mathf.Lerp(v00, v10, fx), Mathf.Lerp(v01, v11, fx), fy);
                    float norm = Mathf.Clamp01(Mathf.Log(1.0f + (sample / maxVal) * 24.0f, 2.0f) / Mathf.Log(25.0f, 2.0f));
                    pixels[y * W + x] = CpuHeatmap(norm);
                }
            }

            m_TexYanReference.SetPixels(pixels);
            m_TexYanReference.Apply(false, false);
        }

        private static float ComputeYanHeatmapCorrelation(Texture2D refTex, RenderTexture vividRt)
        {
            if (refTex == null || vividRt == null) return 0f;
            var prev = RenderTexture.active;
            RenderTexture.active = vividRt;
            var tmp = new Texture2D(vividRt.width, vividRt.height, TextureFormat.RGBA32, false, false);
            tmp.ReadPixels(new Rect(0, 0, vividRt.width, vividRt.height), 0, 0);
            tmp.Apply();
            RenderTexture.active = prev;

            Color[] a = refTex.GetPixels();
            Color[] b = tmp.GetPixels();
            DestroyImmediate(tmp);

            int n = Mathf.Min(a.Length, b.Length);
            double ma = 0, mb = 0;
            for (int i = 0; i < n; i++)
            {
                ma += a[i].grayscale;
                mb += b[i].grayscale;
            }
            ma /= n; mb /= n;
            double cov = 0, va = 0, vb = 0;
            for (int i = 0; i < n; i++)
            {
                double da = a[i].grayscale - ma;
                double db = b[i].grayscale - mb;
                cov += da * db;
                va += da * da;
                vb += db * db;
            }
            return (va > 1e-9 && vb > 1e-9) ? (float)(cov / Math.Sqrt(va * vb)) : 0f;
        }

        private void DrawMedullaSweepPlotTexture(float4[] sweepData)
        {
            const int W = 512, H = 256;
            if (m_TexMedullaSweepPlot == null || m_TexMedullaSweepPlot.width != W || m_TexMedullaSweepPlot.height != H)
            {
                if (m_TexMedullaSweepPlot != null) DestroyImmediate(m_TexMedullaSweepPlot);
                m_TexMedullaSweepPlot = new Texture2D(W, H, TextureFormat.RGBA32, false, false)
                {
                    name = "FurLookDev_MedullaSweepPlot",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }

            var bg = new Color(0.055f, 0.062f, 0.080f, 1.0f);
            var gridCol = new Color(0.14f, 0.16f, 0.20f, 1.0f);
            var px = new Color[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = bg;

            // 2x2 sub-panels: TL=kappa, TR=sigma_s, BL=sigma_a, BR=anisotropy g
            for (int y = 0; y < H; y++)
            {
                px[y * W + (W / 2)] = gridCol;
                if (y == H / 2)
                    for (int x = 0; x < W; x++) px[y * W + x] = gridCol;
            }

            PlotPanelCurves(px, W, H, 12, H / 2 + 10, W / 2 - 24, H / 2 - 20, sweepData, 0,
                new Color(0.30f, 0.85f, 1.00f), new Color(1.00f, 0.65f, 0.25f), new Color(0.95f, 0.35f, 0.90f), Color.white);
            PlotPanelCurves(px, W, H, W / 2 + 12, H / 2 + 10, W / 2 - 24, H / 2 - 20, sweepData, 64,
                new Color(0.95f, 0.45f, 0.35f), new Color(0.35f, 0.95f, 0.55f), new Color(0.45f, 0.70f, 1.00f), Color.white);
            PlotPanelCurves(px, W, H, 12, 10, W / 2 - 24, H / 2 - 20, sweepData, 128,
                new Color(0.95f, 0.45f, 0.85f), new Color(1.00f, 0.75f, 0.30f), new Color(0.40f, 0.90f, 0.85f), Color.white);
            PlotPanelCurves(px, W, H, W / 2 + 12, 10, W / 2 - 24, H / 2 - 20, sweepData, 192,
                new Color(1.00f, 0.45f, 0.35f), new Color(0.95f, 0.85f, 0.30f), new Color(0.35f, 0.95f, 0.65f), Color.white);

            m_TexMedullaSweepPlot.SetPixels(px);
            m_TexMedullaSweepPlot.Apply(false, false);
        }

        private static void PlotPanelCurves(
            Color[] px, int W, int H, int x0, int y0, int pw, int ph,
            float4[] data, int offset, Color cX, Color cY, Color cZ, Color cW)
        {
            float maxV = 1e-4f;
            for (int i = 0; i < 64; i++)
            {
                float4 v = data[offset + i];
                maxV = Mathf.Max(maxV, Mathf.Max(Mathf.Max(v.x, v.y), Mathf.Max(v.z, v.w)));
            }

            // Frame border
            var border = new Color(0.22f, 0.25f, 0.32f, 1f);
            for (int x = x0; x < x0 + pw; x++)
            {
                px[y0 * W + x] = border;
                px[(y0 + ph - 1) * W + x] = border;
            }
            for (int y = y0; y < y0 + ph; y++)
            {
                px[y * W + x0] = border;
                px[y * W + (x0 + pw - 1)] = border;
            }

            for (int ch = 0; ch < 4; ch++)
            {
                Color col = ch == 0 ? cX : (ch == 1 ? cY : (ch == 2 ? cZ : cW));
                int prevX = -1, prevY = -1;
                for (int i = 0; i < 64; i++)
                {
                    float4 v = data[offset + i];
                    float val = ch == 0 ? v.x : (ch == 1 ? v.y : (ch == 2 ? v.z : v.w));
                    int sx = x0 + 2 + Mathf.RoundToInt((i / 63.0f) * (pw - 5));
                    int sy = y0 + 2 + Mathf.RoundToInt(Mathf.Clamp01(val / maxV) * (ph - 5));
                    if (prevX >= 0)
                        DrawThickLine(px, W, H, prevX, prevY, sx, sy, col);
                    prevX = sx; prevY = sy;
                }
            }
        }

        private static void DrawThickLine(Color[] px, int W, int H, int x0, int y0, int x1, int y1, Color col)
        {
            int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), 1);
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
                int y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
                for (int dy = -1; dy <= 1; dy++)
                {
                    int py = y + dy;
                    if (py < 0 || py >= H) continue;
                    for (int dx = 0; dx <= 1; dx++)
                    {
                        int pxx = x + dx;
                        if (pxx >= 0 && pxx < W)
                            px[py * W + pxx] = col;
                    }
                }
            }
        }

        private static Color CpuHeatmap(float t)
        {
            float s = Mathf.Clamp01(t);
            Color c0 = new Color(0.015f, 0.012f, 0.045f, 1f);
            Color c1 = new Color(0.240f, 0.040f, 0.360f, 1f);
            Color c2 = new Color(0.700f, 0.145f, 0.250f, 1f);
            Color c3 = new Color(0.960f, 0.520f, 0.070f, 1f);
            Color c4 = new Color(0.990f, 0.960f, 0.650f, 1f);
            if (s < 0.25f) return Color.Lerp(c0, c1, s * 4f);
            if (s < 0.50f) return Color.Lerp(c1, c2, (s - 0.25f) * 4f);
            if (s < 0.75f) return Color.Lerp(c2, c3, (s - 0.50f) * 4f);
            return Color.Lerp(c3, c4, (s - 0.75f) * 4f);
        }

        private static void ComputePeakAndFwhm(
            float4[] data, int offset, int count, Func<float4, float> selector,
            out float peakDeg, out float fwhmDeg)
        {
            int peakIdx = 0;
            float peakVal = -1f;
            for (int i = 0; i < count; i++)
            {
                float v = selector(data[offset + i]);
                if (v > peakVal) { peakVal = v; peakIdx = i; }
            }

            peakDeg = Mathf.Lerp(-80f, 80f, peakIdx / (float)(count - 1));
            if (peakVal <= 1e-6f) { fwhmDeg = 0f; return; }

            float half = peakVal * 0.5f;
            int left = peakIdx;
            while (left > 0 && selector(data[offset + left]) >= half) left--;
            int right = peakIdx;
            while (right < count - 1 && selector(data[offset + right]) >= half) right++;
            float degPerStep = 160f / (count - 1);
            fwhmDeg = Mathf.Max((right - left) * degPerStep, degPerStep);
        }

        private void BindBoardTexture(Renderer rend, Texture tex)
        {
            if (rend == null || tex == null) return;
            m_Mpb.Clear();
            m_Mpb.SetTexture(s_UnlitColorMapId, tex);
            rend.SetPropertyBlock(m_Mpb);
        }

        private static void SetTmpText(TMP_Text tmp, string text)
        {
            if (tmp != null)
                tmp.text = text;
        }

        private void EnsureRenderTargets()
        {
            m_RtGonioR = EnsureRt(m_RtGonioR, 256, 256, "FurLookDev_Gonio_R");
            m_RtGonioTT = EnsureRt(m_RtGonioTT, 256, 256, "FurLookDev_Gonio_TT");
            m_RtGonioTRT = EnsureRt(m_RtGonioTRT, 256, 256, "FurLookDev_Gonio_TRT");
            m_RtGonioMedulla = EnsureRt(m_RtGonioMedulla, 256, 256, "FurLookDev_Gonio_Medulla");
            m_RtGonioTotal = EnsureRt(m_RtGonioTotal, 256, 256, "FurLookDev_Gonio_Total");
            m_RtFurnaceSingle = EnsureRt(m_RtFurnaceSingle, 256, 256, "FurLookDev_Furnace_Single");
            m_RtFurnaceFull = EnsureRt(m_RtFurnaceFull, 256, 256, "FurLookDev_Furnace_Full");
            m_RtYanVivid = EnsureRt(m_RtYanVivid, 256, 128, "FurLookDev_Yan_VividMatch");
        }

        private static RenderTexture EnsureRt(RenderTexture rt, int w, int h, string name)
        {
            if (rt != null && rt.IsCreated() && rt.width == w && rt.height == h)
                return rt;
            if (rt != null)
            {
                rt.Release();
                DestroyImmediate(rt);
            }
            var next = new RenderTexture(w, h, 0, GraphicsFormat.R8G8B8A8_UNorm)
            {
                name = name,
                enableRandomWrite = true,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            next.Create();
            return next;
        }

        private void ReleaseResources()
        {
            ReleaseRt(ref m_RtGonioR);
            ReleaseRt(ref m_RtGonioTT);
            ReleaseRt(ref m_RtGonioTRT);
            ReleaseRt(ref m_RtGonioMedulla);
            ReleaseRt(ref m_RtGonioTotal);
            ReleaseRt(ref m_RtFurnaceSingle);
            ReleaseRt(ref m_RtFurnaceFull);
            ReleaseRt(ref m_RtYanVivid);
            if (m_TexMedullaSweepPlot != null) { DestroyImmediate(m_TexMedullaSweepPlot); m_TexMedullaSweepPlot = null; }
            if (m_TexYanReference != null) { DestroyImmediate(m_TexYanReference); m_TexYanReference = null; }
        }

        private static void ReleaseRt(ref RenderTexture rt)
        {
            if (rt != null)
            {
                rt.Release();
                DestroyImmediate(rt);
                rt = null;
            }
        }
    }
}

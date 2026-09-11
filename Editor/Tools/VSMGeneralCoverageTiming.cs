using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;
namespace VividRP.Editor
{
    // Explicit diagnostic; no image readback or shader compilation during timing.
    internal static class VSMGeneralCoverageTiming
    {
        private static VSMBaselineSession session;
        private static double started;
        private static int stage;
        private static string root;
        private static readonly string[] Labels = { "length10_a", "length50_a", "length50_b", "length10_b" };
        [MenuItem("Tools/VividRP/Diagnostics/Run Temporary VSM General Coverage Timing")]
        internal static void Run()
        {
            if (Application.dataPath != "E:/VividRP_Reborn/Assets" || !EditorApplication.isPlaying || session != null)
                throw new InvalidOperationException("Use VividRP_Reborn in Play Mode.");
            string revision = File.ReadAllText("Packages/VividRP/Temp~/smrt-path/timing-revision.txt").Trim();
            root = "Packages/VividRP/Temp~/smrt-path/timing/" + revision + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            Directory.CreateDirectory(root); stage = 0;
            try { Begin(); }
            catch { Finish(); throw; }
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Finish;
        }
        private static void Begin()
        {
            var settings = new VSMBaselineCase { name = Labels[stage], resolution = 4096, firstLevel = 0,
                screenDensity = true, pcf = true, transition = .2f, maxDistance = 150, screenSpaceDenoise = true };
            session = new VSMBaselineSession(Camera.main, new[] { settings }, 4, 4, 120, 4096,
                root + "/" + Labels[stage], "SMRT straight clipmap continuation; focused 1024 pages", false);
            var volume = (CascadedShadowSettingsVolume)typeof(VSMBaselineSession).GetField("m_Settings",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(session);
            volume.virtualShadowMapViewCoverage.Override(true);
            volume.virtualShadowMapPhysicalPageBudget.Override(1024);
            volume.virtualShadowMapCoverageTransition.Override(.05f);
            volume.virtualShadowMapSMRT.Override(true);
            volume.virtualShadowMapSMRTJointSampling.Override(false);
            volume.virtualShadowMapSMRTRayCount.Override(4);
            volume.virtualShadowMapSMRTSamplesPerRay.Override(8);
            volume.virtualShadowMapSMRTMaxRayLength.Override(Labels[stage].StartsWith("length10", StringComparison.Ordinal) ? 10 : 50);
            started = EditorApplication.timeSinceStartup;
        }
        private static void Tick()
        {
            if (session == null) return;
            if (session.Tick())
            {
                session.Finish("completed"); session.Dispose(); session = null;
                if (++stage < Labels.Length) { Begin(); return; }
                File.WriteAllText(root + "/status.txt", "complete"); Finish(); return;
            }
            if (session.IsFinished || EditorApplication.timeSinceStartup - started > 90)
            { File.WriteAllText(root + "/status.txt", "incomplete stage=" + stage); Finish(); }
        }
        private static void Finish()
        {
            EditorApplication.update -= Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= Finish;
            if (session != null) { session.Dispose(); session = null; }
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }
    }
}

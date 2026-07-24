using System;
using UnityEngine;

namespace VividRP.Runtime.Examples
{
    [DisallowMultipleComponent]
    public sealed class VividPerObjectCpuBenchmarkController : MonoBehaviour
    {
        [SerializeField]
        private Renderer[] m_Renderers = Array.Empty<Renderer>();

        [SerializeField]
        private bool m_UseChildRenderersWhenListIsEmpty = true;

        [SerializeField, Min(0)]
        private int m_WarmupFrames = 16;

        [SerializeField, Min(1)]
        private int m_MeasurementFrames = 128;

        [SerializeField]
        private VividPerObjectColorExampleController.PropertyAccessMode m_PerObjectAccessMode =
            VividPerObjectColorExampleController.PropertyAccessMode.CachedHandle;

        [SerializeField, TextArea(10, 30)]
        private string m_LastReport;

        public string LastReport => m_LastReport;

        [ContextMenu("Run MPB vs Per-Object CPU Benchmark")]
        public void RunBenchmark()
        {
            try
            {
                Renderer[] renderers = ResolveRenderers();
                VividPerObjectCpuBenchmarkReport report = VividPerObjectCpuBenchmark.Run(
                    renderers,
                    Mathf.Max(0, m_WarmupFrames),
                    Mathf.Max(1, m_MeasurementFrames),
                    m_PerObjectAccessMode);
                m_LastReport = report.ToString();
                Debug.Log(m_LastReport, this);
            }
            catch (Exception exception)
            {
                m_LastReport = exception.ToString();
                Debug.LogException(exception, this);
            }
        }

        private Renderer[] ResolveRenderers()
        {
            if (m_Renderers != null && m_Renderers.Length > 0)
                return m_Renderers;
            if (m_UseChildRenderersWhenListIsEmpty)
                return GetComponentsInChildren<Renderer>(includeInactive: true);
            return Array.Empty<Renderer>();
        }
    }
}
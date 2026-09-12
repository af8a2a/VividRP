using System;
using System.Globalization;
using System.IO;
using Unity.Profiling;

namespace VividRP.Editor
{
    // Promoted from the VSM capacity/cost experiment. All storage is allocated
    // before warm-up; Observe never formats strings, writes files or reads back GPU resources.
    internal sealed class RenderStageCapture : IDisposable
    {
        internal static readonly string[] VsmMarkers =
        {
            "VSM.LayoutRemap", "VSM.ResetFeedback", "VSM.MarkReceiverPages", "VSM.Allocate",
            "VSM.InvalidateStatic", "VSM.ClearPhysicalPages", "VSM.StaticCasterCull",
            "VSM.DynamicCasterCull", "VSM.PageCull", "VSM.StaticRaster", "VSM.FinalizePages",
            "VSM.DynamicRaster", "VSM.UnityCompatibilityRaster", "VSM.Resolve", "VSM.ResolveTrace",
            "VSM.FilterHorizontal", "VSM.FilterVertical", "VSM.FilterTemporalVertical"
        };
        private readonly string[] m_Markers;
        private readonly ProfilerRecorder[] m_Recorders;
        private readonly long[] m_Nanoseconds, m_SampleCounts;
        private readonly bool[] m_Available;
        private readonly int[] m_Frames;
        private readonly double[] m_Times;
        internal int Count { get; private set; }
        internal int Capacity => m_Frames.Length;

        internal RenderStageCapture(string[] markers, int capacity)
        {
            if (markers == null || markers.Length == 0 || markers.Length > 128 || capacity < 1 || capacity > 8192)
                throw new ArgumentException("Use 1–128 markers and 1–8192 observations.");
            m_Markers = (string[])markers.Clone();
            m_Recorders = new ProfilerRecorder[markers.Length];
            m_Nanoseconds = new long[capacity * markers.Length];
            m_SampleCounts = new long[m_Nanoseconds.Length];
            m_Available = new bool[m_Nanoseconds.Length];
            m_Frames = new int[capacity]; m_Times = new double[capacity];
            try
            {
                for (int i = 0; i < markers.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(markers[i])) throw new ArgumentException("Empty marker name.");
                    m_Recorders[i] = new ProfilerRecorder(ProfilerCategory.Render, markers[i], 1,
                        ProfilerRecorderOptions.WrapAroundWhenCapacityReached |
                        ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.GpuRecorder);
                    m_Recorders[i].Start();
                }
            }
            catch { Dispose(); throw; }
        }

        // Camera observation ID is not the identity of the delayed GPU profiling frame.
        internal void Observe(int cameraFrame, double time)
        {
            // Edit Mode can render many camera callbacks with the same Time.frameCount.
            // Each caller observation is retained; duplicate GPU frames cannot be inferred from that counter.
            if (Count == Capacity) return;
            m_Frames[Count] = cameraFrame; m_Times[Count] = time;
            int offset = Count * m_Markers.Length;
            for (int i = 0; i < m_Recorders.Length; i++)
            {
                if (!m_Recorders[i].Valid || m_Recorders[i].Count == 0) continue;
                ProfilerRecorderSample sample = m_Recorders[i].GetSample(0);
                m_Available[offset + i] = true;
                m_Nanoseconds[offset + i] = sample.Value;
                m_SampleCounts[offset + i] = sample.Count;
            }
            Count++;
        }

        internal void WriteCsv(string path)
        {
            using var writer = new StreamWriter(path, false);
            writer.WriteLine("observation,unity_frame_observed,editor_time_observed,marker,available,gpu_ns,gpu_sample_count");
            for (int row = 0; row < Count; row++)
            for (int marker = 0; marker < m_Markers.Length; marker++)
            {
                int offset = row * m_Markers.Length + marker;
                writer.Write(row); writer.Write(','); writer.Write(m_Frames[row]); writer.Write(',');
                writer.Write(m_Times[row].ToString("R", CultureInfo.InvariantCulture)); writer.Write(',');
                writer.Write(VSMBaselineSamples.Csv(m_Markers[marker])); writer.Write(',');
                writer.Write(m_Available[offset] ? 1 : 0); writer.Write(',');
                writer.Write(m_Nanoseconds[offset]); writer.Write(','); writer.WriteLine(m_SampleCounts[offset]);
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < m_Recorders.Length; i++) m_Recorders[i].Dispose();
        }
    }
}

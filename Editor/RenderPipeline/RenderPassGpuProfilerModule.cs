using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Unity.Profiling.Editor;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine.UIElements;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [Serializable]
    [ProfilerModuleMetadata("VividRP RenderPass GPU")]
    public sealed class RenderPassGpuProfilerModule : ProfilerModule
    {
        public RenderPassGpuProfilerModule() : base(CreateCounters(), ProfilerModuleChartType.StackedTimeArea,
            new[] { RenderPassGpuProfiler.CategoryName, "GPU", Unity.Profiling.ProfilerCategory.Render.Name, Unity.Profiling.ProfilerCategory.Scripts.Name }) { }

        private static ProfilerCounterDescriptor[] CreateCounters()
        {
            var names = RenderPassGpuProfiler.GroupNames;
            var counters = new ProfilerCounterDescriptor[names.Length];
            for (int i = 0; i < names.Length; i++)
                counters[i] = new ProfilerCounterDescriptor(names[i], RenderPassGpuProfiler.CategoryName);
            return counters;
        }

        public override ProfilerModuleViewController CreateDetailsViewController() => new Details(ProfilerWindow);

        internal static string DescribeMissingSamples(GpuProfilingStatisticsAvailabilityStates state)
        {
            if ((state & GpuProfilingStatisticsAvailabilityStates.Enabled) == 0)
                return "GPU sampling was disabled in this captured frame. Record new frames with this module enabled.";
            if ((state & GpuProfilingStatisticsAvailabilityStates.Gathered) != 0)
                return "GPU sampling is active; no completed RenderPass samples in this collection interval (warm-up or no executed pass).";
            if ((state & (GpuProfilingStatisticsAvailabilityStates.NotSupportedWithNativeGfxJobs |
                          GpuProfilingStatisticsAvailabilityStates.NotSupportedWithLegacyGfxJobs)) != 0)
                return "Unity reports GPU profiling unavailable with Graphics Jobs for this frame. Verify GPU Usage support/settings.";
            return "No GPU samples available in this frame. Unity GPU status: " + state;
        }

        // Unity does not expose its timeline renderer publicly. Resolve once per details view,
        // share the ProfilerWindow-owned CPU timeline, and never dispose or reinitialize its state.
        // If Unity changes these internals the GPU list remains usable with a visible explanation.
        private sealed class NativeTimeline
        {
            private delegate void DrawTimeline(int frame, Rect rect, bool fetchData, ref bool live);
            private readonly ProfilerWindow m_Window;
            private DrawTimeline m_Draw;
            private bool m_Live = true;
            private string m_Error;
            internal bool Available => m_Draw != null;

            internal NativeTimeline(ProfilerWindow window)
            {
                m_Window = window;
                try
                {
                    var moduleType = typeof(ProfilerWindow).Assembly.GetType("UnityEditorInternal.Profiling.CPUProfilerModule", true);
                    var getModule = typeof(ProfilerWindow).GetMethod("GetProfilerModuleByType",
                        BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(Type) }, null);
                    var module = getModule?.Invoke(window, new object[] { moduleType });
                    var timeline = moduleType.GetField("m_TimelineGUI", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(module);
                    if (timeline == null) throw new NotSupportedException("The CPU timeline has not been initialized.");
                    var method = timeline.GetType().GetMethod("DoGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(int), typeof(Rect), typeof(bool), typeof(bool).MakeByRefType() }, null);
                    if (method == null) throw new MissingMethodException("ProfilerTimelineGUI.DoGUI");
                    m_Draw = (DrawTimeline)method.CreateDelegate(typeof(DrawTimeline), timeline);
                }
                catch (Exception error)
                {
                    m_Error = "Native Timeline unavailable in this Unity version: " + error.GetBaseException().Message +
                        " Use GPU Passes or the built-in CPU Usage module.";
                }
            }

            internal void Repaint() => m_Window.Repaint();

            internal void Draw(Vector2 size)
            {
                if (size.x < 1 || size.y < 1) return;
                var rect = new Rect(Vector2.zero, size);
                if (m_Draw == null)
                {
                    EditorGUI.HelpBox(rect, m_Error, MessageType.Warning);
                    return;
                }
                int frame = m_Window.selectedFrameIndex < 0 ? ProfilerDriver.lastFrameIndex : (int)m_Window.selectedFrameIndex;
                if (frame < 0)
                {
                    EditorGUI.HelpBox(rect, "Record CPU samples or load a capture to display Unity Timeline.", MessageType.Info);
                    return;
                }
                try { m_Draw(frame, rect, true, ref m_Live); }
                catch (ExitGUIException) { throw; }
                catch (Exception error)
                {
                    m_Error = "Native Timeline could not draw this capture: " + error.GetBaseException().Message +
                        " Switch to GPU Passes; reopen the module to retry.";
                    m_Draw = null;
                    Debug.LogException(error);
                }
            }
        }
        private sealed class Details : ProfilerModuleViewController
        {
            private readonly List<FrameDataView.MarkerInfo> m_Markers = new();
            private readonly List<Row> m_Rows = new();
            private Label m_Status;
            private ListView m_List;
            private NativeTimeline m_Timeline;

            private struct Row
            {
                internal string Name;
                internal long Nanoseconds;
                internal long Samples;
            }

            internal Details(ProfilerWindow window) : base(window) { }

            protected override VisualElement CreateView()
            {
                var root = new VisualElement();
                root.style.flexGrow = 1;
                root.Add(new Label("All cameras • completed GPU samples collected in this frame • excludes subsystem work outside RenderPass.Record"));
                root.Add(new Label("Sum is GPU work, not frame duration: async queues may overlap. Results may arrive late; no sample does not mean zero cost."));
                m_Status = new Label();
                root.Add(m_Status);
                var listHeader = new Label("GPU ms     Samples     Category / Pass (sorted by GPU work)");
                root.Add(listHeader);
                m_List = new ListView(m_Rows, 22, () => new Label(), (element, index) =>
                {
                    var row = m_Rows[index];
                    ((Label)element).text = row.Samples > 0
                        ? $"{row.Nanoseconds / 1000000.0,9:F3}     {row.Samples,7}     {row.Name}"
                        : $"      N/A           —     {row.Name}";
                });
                m_List.style.flexGrow = 1;
                root.Add(m_List);
                m_Timeline = new NativeTimeline(ProfilerWindow);
                var timelineHint = new Label("Native CPU Timeline: Main/Render Thread durations are CPU work, not GPU execution intervals. GPU counters above arrive asynchronously.");
                timelineHint.style.whiteSpace = WhiteSpace.Normal;
                var timelineView = new IMGUIContainer();
                timelineView.style.flexGrow = 1;
                timelineView.style.minHeight = 0;
                timelineView.style.flexShrink = 1;
                timelineView.style.flexBasis = 0;
                timelineView.onGUIHandler = () => m_Timeline.Draw(timelineView.contentRect.size);
                root.Add(timelineHint);
                root.Add(timelineView);
                var view = new DropdownField("Detail view", new List<string> { "GPU Passes", "Unity CPU Timeline" },
                    m_Timeline.Available ? 1 : 0);
                root.Insert(0, view);
                void SelectView(string selected)
                {
                    bool timeline = selected == "Unity CPU Timeline";
                    m_List.style.display = timeline ? DisplayStyle.None : DisplayStyle.Flex;
                    listHeader.style.display = timeline ? DisplayStyle.None : DisplayStyle.Flex;
                    timelineView.style.display = timeline ? DisplayStyle.Flex : DisplayStyle.None;
                    timelineHint.style.display = timeline ? DisplayStyle.Flex : DisplayStyle.None;
                    timelineView.MarkDirtyRepaint();
                }
                view.RegisterValueChangedCallback(evt => SelectView(evt.newValue));
                SelectView(view.value);
                ProfilerWindow.SelectedFrameIndexChanged += Reload;
                Reload(ProfilerWindow.selectedFrameIndex);
                return root;
            }

            private void Reload(long selectedFrame)
            {
                m_Timeline?.Repaint();
                m_Rows.Clear();
                m_Markers.Clear();
                int frame = selectedFrame < 0 ? ProfilerDriver.lastFrameIndex : (int)selectedFrame;
                if (frame < 0)
                {
                    m_Status.text = "Record a capture with this module enabled to collect RenderPass GPU samples.";
                    m_List.RefreshItems();
                    return;
                }
                using var data = ProfilerDriver.GetRawFrameDataView(frame, 0);
                if (!data.valid)
                {
                    m_Status.text = "No frame data available.";
                    m_List.RefreshItems();
                    return;
                }
                // Discover counters from the capture itself, so saved captures survive graph edits/reloads.
                data.GetMarkers(m_Markers);
                long total = 0;
                long samples = 0;
                foreach (var marker in m_Markers)
                {
                    if (!marker.name.StartsWith(RenderPassGpuProfiler.PassPrefix, StringComparison.Ordinal) ||
                        !data.HasCounterValue(marker.id)) continue;
                    string suffix = marker.name.Substring(RenderPassGpuProfiler.PassPrefix.Length);
                    int countId = data.GetMarkerId(RenderPassGpuProfiler.SamplesPrefix + suffix);
                    long count = countId != FrameDataView.invalidMarkerId && data.HasCounterValue(countId)
                        ? data.GetCounterValueAsLong(countId) : 0;
                    long value = count > 0 ? data.GetCounterValueAsLong(marker.id) : 0;
                    m_Rows.Add(new Row { Name = suffix, Nanoseconds = value, Samples = count });
                    total += value;
                    samples += count;
                }
                m_Rows.Sort((a, b) => b.Nanoseconds.CompareTo(a.Nanoseconds));
                m_Status.text = samples > 0
                    ? $"Collected GPU work: {total / 1000000.0:F3} ms • {samples} samples • frame {frame}"
                    : DescribeMissingSamples(ProfilerDriver.GetGpuStatisticsAvailabilityState(frame));
                m_List.RefreshItems();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    ProfilerWindow.SelectedFrameIndexChanged -= Reload;
                    m_Timeline = null;
                }
                base.Dispose(disposing);
            }
        }
    }
}

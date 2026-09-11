# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length50_b — completed_with_warnings
Attempt: 0 | Observations: 200 | Active seconds: 4.0172002976190484 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 47 | Median interval (s, 0=unavailable): 0.018991801303855027
Timing coverage passed: False | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/667/0/0
- Frame intervals over 100 ms retained; investigate before comparing.
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Positive GPU timings below 1 ms retained as diagnostics; no numeric filtering applied.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 200/200 | 19.206899218261242 | 20.363003015518188 | available |
| cpu_frame_ms | 200/200 | 19.26955 | 20.9538 | available |
| gpu_frame_ms | 200/200 | 18.984576 | 20.178432 | available |
| cpu_render_thread_ms | 200/200 | 2.42575 | 2.9861 | available |
| gc_allocated_in_editor_frame_bytes | 200/200 | 14621 | 186943 | available |
| VSM.LayoutRemap_gpu_ms | 200/200 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 200/200 | 4.845952 | 5.2526079999999995 | available |
| VSM.Allocate_gpu_ms | 200/200 | 0.559232 | 0.57087999999999994 | available |
| VSM.InvalidateStatic_gpu_ms | 0/200 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 200/200 | 0.13516799999999998 | 0.46694399999999997 | available |
| VSM.StaticCasterCull_gpu_ms | 200/200 | 0.379776 | 0.947456 | available |
| VSM.DynamicCasterCull_gpu_ms | 200/200 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 200/200 | 0.35763199999999995 | 0.729856 | available |
| VSM.StaticRaster_gpu_ms | 200/200 | 0.007424 | 0.014848 | available |
| VSM.DynamicRaster_gpu_ms | 200/200 | 0.087808 | 0.09344 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/200 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 200/200 | 0.004352 | 0.005888 | available |
| VSM.ResetFeedback_gpu_ms | 200/200 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 200/200 | 5.0223359999999992 | 5.4817279999999995 | available |


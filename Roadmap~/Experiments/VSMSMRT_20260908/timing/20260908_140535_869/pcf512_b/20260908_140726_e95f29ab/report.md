# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## pcf512_b — completed_with_warnings
Attempt: 0 | Observations: 830 | Active seconds: 5.0014613945578219 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 425/421/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 830/830 | 5.9916488826274872 | 6.5578017383813858 | available |
| cpu_frame_ms | 830/830 | 6.0008 | 6.7765 | available |
| gpu_frame_ms | 829/830 | 5.93408 | 6.501888 | available |
| cpu_render_thread_ms | 830/830 | 1.9924499999999998 | 2.4885 | available |
| gc_allocated_in_editor_frame_bytes | 830/830 | 14539 | 20949 | available |
| VSM.LayoutRemap_gpu_ms | 830/830 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 830/830 | 0.358656 | 0.41856 | available |
| VSM.InvalidateStatic_gpu_ms | 0/830 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 830/830 | 0.062464 | 0.075264 | available |
| VSM.StaticCasterCull_gpu_ms | 830/830 | 0.24115199999999998 | 0.62591999999999992 | available |
| VSM.DynamicCasterCull_gpu_ms | 830/830 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 830/830 | 0.22195199999999998 | 0.47359999999999997 | available |
| VSM.StaticRaster_gpu_ms | 830/830 | 0.0076799999999999993 | 0.009984 | available |
| VSM.DynamicRaster_gpu_ms | 830/830 | 0.0384 | 0.041215999999999996 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/830 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 830/830 | 0.004352 | 0.005632 | available |
| VSM.ResetFeedback_gpu_ms | 0/830 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 830/830 | 2.083584 | 2.5210879999999998 | available |


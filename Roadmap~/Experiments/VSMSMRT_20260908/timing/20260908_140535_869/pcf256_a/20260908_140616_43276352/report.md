# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## pcf256_a — completed_with_warnings
Attempt: 0 | Observations: 812 | Active seconds: 5.0062958049886674 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: over_budget_at_last_frame | Last-frame resident/requested/new/overflow: 256/419/1/163
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Physical page requests exceed the budget at the last frame; quality is not validated.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 812/812 | 6.1491993255913258 | 6.8165035918354988 | available |
| cpu_frame_ms | 812/812 | 6.1350999999999996 | 7.1961 | available |
| gpu_frame_ms | 810/812 | 6.074752 | 6.680832 | available |
| cpu_render_thread_ms | 812/812 | 2.0343999999999998 | 2.662 | available |
| gc_allocated_in_editor_frame_bytes | 812/812 | 14539 | 20949 | available |
| VSM.LayoutRemap_gpu_ms | 812/812 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 812/812 | 0.495488 | 0.566016 | available |
| VSM.InvalidateStatic_gpu_ms | 0/812 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 812/812 | 0.036095999999999996 | 0.042752 | available |
| VSM.StaticCasterCull_gpu_ms | 812/812 | 0.173056 | 0.556288 | available |
| VSM.DynamicCasterCull_gpu_ms | 812/812 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 812/812 | 0.153856 | 0.175616 | available |
| VSM.StaticRaster_gpu_ms | 812/812 | 0.024832 | 0.032512 | available |
| VSM.DynamicRaster_gpu_ms | 812/812 | 0.013312 | 0.015616 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/812 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 812/812 | 0.004352 | 0.005632 | available |
| VSM.ResetFeedback_gpu_ms | 0/812 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 812/812 | 1.8648319999999998 | 2.408448 | available |


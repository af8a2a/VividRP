# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length50_a — completed_with_warnings
Attempt: 0 | Observations: 208 | Active seconds: 4.0028548965419546 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 208 | Median interval (s, 0=unavailable): 0.019335898526076534
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/667/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Positive GPU timings below 1 ms retained as diagnostics; no numeric filtering applied.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 208/208 | 19.328598864376545 | 20.391404628753662 | available |
| cpu_frame_ms | 208/208 | 19.29105 | 20.9863 | available |
| gpu_frame_ms | 208/208 | 0.017664 | 0.018944 | available |
| cpu_render_thread_ms | 208/208 | 0.20185 | 0.3384 | available |
| gc_allocated_in_editor_frame_bytes | 208/208 | 20637 | 105127 | available |
| VSM.LayoutRemap_gpu_ms | 208/208 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 208/208 | 4.8733439999999995 | 5.332736 | available |
| VSM.Allocate_gpu_ms | 208/208 | 0.558592 | 0.56831999999999994 | available |
| VSM.InvalidateStatic_gpu_ms | 0/208 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 208/208 | 0.13516799999999998 | 0.386048 | available |
| VSM.StaticCasterCull_gpu_ms | 208/208 | 0.380672 | 0.8448 | available |
| VSM.DynamicCasterCull_gpu_ms | 208/208 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 208/208 | 0.359168 | 0.68736 | available |
| VSM.StaticRaster_gpu_ms | 208/208 | 0.007424 | 0.012799999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 208/208 | 0.087551999999999991 | 0.09472 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/208 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 208/208 | 0.004352 | 0.005632 | available |
| VSM.ResetFeedback_gpu_ms | 208/208 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 208/208 | 4.970496 | 5.5127039999999994 | available |


# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length10_b — completed_with_warnings
Attempt: 0 | Observations: 467 | Active seconds: 4.0067210034013527 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/665/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 467/467 | 8.5458969697356224 | 9.05539933592081 | available |
| cpu_frame_ms | 467/467 | 8.5748 | 9.3146 | available |
| gpu_frame_ms | 467/467 | 8.527872 | 9.030144 | available |
| cpu_render_thread_ms | 467/467 | 2.0861 | 2.5437 | available |
| gc_allocated_in_editor_frame_bytes | 467/467 | 18307 | 21661 | available |
| VSM.LayoutRemap_gpu_ms | 467/467 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 467/467 | 0.99225599999999992 | 1.312768 | available |
| VSM.Allocate_gpu_ms | 467/467 | 0.53248 | 0.550912 | available |
| VSM.InvalidateStatic_gpu_ms | 0/467 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 467/467 | 0.130304 | 0.32383999999999996 | available |
| VSM.StaticCasterCull_gpu_ms | 467/467 | 0.358656 | 0.652032 | available |
| VSM.DynamicCasterCull_gpu_ms | 467/467 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 467/467 | 0.33792 | 0.526848 | available |
| VSM.StaticRaster_gpu_ms | 467/467 | 0.007424 | 0.011519999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 467/467 | 0.087808 | 0.093183999999999989 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/467 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 467/467 | 0.0038399999999999997 | 0.0048639999999999994 | available |
| VSM.ResetFeedback_gpu_ms | 467/467 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 467/467 | 1.885184 | 2.159872 | available |


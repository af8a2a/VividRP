# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length50_a — completed_with_warnings
Attempt: 0 | Observations: 296 | Active seconds: 4.0014205994897942 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/667/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 296/296 | 13.464200310409069 | 14.982801862061024 | available |
| cpu_frame_ms | 296/296 | 13.46245 | 15.4378 | available |
| gpu_frame_ms | 296/296 | 13.445504 | 14.942976 | available |
| cpu_render_thread_ms | 296/296 | 2.6698000000000004 | 3.4255 | available |
| gc_allocated_in_editor_frame_bytes | 296/296 | 14419 | 95538 | available |
| VSM.LayoutRemap_gpu_ms | 296/296 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 296/296 | 4.124544 | 4.589568 | available |
| VSM.Allocate_gpu_ms | 296/296 | 0.547584 | 0.572928 | available |
| VSM.InvalidateStatic_gpu_ms | 0/296 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 296/296 | 0.13055999999999998 | 0.392704 | available |
| VSM.StaticCasterCull_gpu_ms | 296/296 | 0.35571200000000003 | 0.65279999999999994 | available |
| VSM.DynamicCasterCull_gpu_ms | 296/296 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 296/296 | 0.333952 | 0.597248 | available |
| VSM.StaticRaster_gpu_ms | 296/296 | 0.007424 | 0.011776 | available |
| VSM.DynamicRaster_gpu_ms | 296/296 | 0.087808 | 0.11161599999999999 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/296 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 296/296 | 0.003584 | 0.004352 | available |
| VSM.ResetFeedback_gpu_ms | 296/296 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 296/296 | 2.8638719999999998 | 3.4593279999999997 | available |


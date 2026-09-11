# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length10_a — completed_with_warnings
Attempt: 0 | Observations: 430 | Active seconds: 4.0047205002834474 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 47 | Median interval (s, 0=unavailable): 0.011095549886622535
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 668/655/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Positive GPU timings below 1 ms retained as diagnostics; no numeric filtering applied.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 430/430 | 9.0319020673632622 | 10.924900881946087 | available |
| cpu_frame_ms | 430/430 | 9.0628 | 11.7579 | available |
| gpu_frame_ms | 429/430 | 8.890624 | 9.672704 | available |
| cpu_render_thread_ms | 430/430 | 2.36395 | 3.3164 | available |
| gc_allocated_in_editor_frame_bytes | 430/430 | 14419 | 135011 | available |
| VSM.LayoutRemap_gpu_ms | 430/430 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 430/430 | 3.9416320000000002 | 4.2462719999999994 | available |
| VSM.Allocate_gpu_ms | 430/430 | 0.526848 | 0.647936 | available |
| VSM.InvalidateStatic_gpu_ms | 0/430 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 430/430 | 0.121344 | 0.279296 | available |
| VSM.StaticCasterCull_gpu_ms | 430/430 | 0.35148799999999997 | 0.59929599999999994 | available |
| VSM.DynamicCasterCull_gpu_ms | 430/430 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 430/430 | 0.329984 | 0.49407999999999996 | available |
| VSM.StaticRaster_gpu_ms | 430/430 | 0.007424 | 0.011519999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 430/430 | 0.087551999999999991 | 0.093952 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/430 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 430/430 | 0.003072 | 0.004352 | available |
| VSM.ResetFeedback_gpu_ms | 430/430 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 430/430 | 0.828928 | 1.205504 | available |


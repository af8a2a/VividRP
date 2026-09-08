# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## baseline256_b — completed_with_warnings
Attempt: 0 | Observations: 853 | Active seconds: 5.0053369968820931 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: over_budget_at_last_frame | Last-frame resident/requested/new/overflow: 256/421/2/165
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Physical page requests exceed the budget at the last frame; quality is not validated.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 853/853 | 5.8185020461678505 | 6.5457979217171669 | available |
| cpu_frame_ms | 853/853 | 5.8181 | 6.7743 | available |
| gpu_frame_ms | 851/853 | 5.75744 | 6.422528 | available |
| cpu_render_thread_ms | 853/853 | 1.9737 | 2.4386 | available |
| gc_allocated_in_editor_frame_bytes | 853/853 | 14551 | 14753 | available |
| VSM.LayoutRemap_gpu_ms | 853/853 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 853/853 | 0.476416 | 0.518656 | available |
| VSM.InvalidateStatic_gpu_ms | 0/853 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 853/853 | 0.035328 | 0.042752 | available |
| VSM.StaticCasterCull_gpu_ms | 853/853 | 0.172544 | 0.568576 | available |
| VSM.DynamicCasterCull_gpu_ms | 853/853 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 853/853 | 0.153856 | 0.342528 | available |
| VSM.StaticRaster_gpu_ms | 853/853 | 0.025088 | 0.030976 | available |
| VSM.DynamicRaster_gpu_ms | 853/853 | 0.014079999999999999 | 0.015872 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/853 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 853/853 | 0.003328 | 0.004352 | available |
| VSM.ResetFeedback_gpu_ms | 0/853 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 853/853 | 1.8142719999999999 | 2.3452159999999997 | available |


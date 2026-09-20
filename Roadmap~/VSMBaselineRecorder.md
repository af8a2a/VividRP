# VSM Play Mode baseline recorder

Open **Tools > VividRP > VSM Baseline Recorder**, enter Play Mode, assign the
Game camera (blank uses `Camera.main`) and click **Start new recording**. The tool
does not enter Play Mode or modify scene/profile assets. It creates a temporary
global Volume on a layer seen by the camera and removes it on completion,
an explicit **Finish session**, window close, script reload, or exit from Play Mode. Other cameras
using that Volume layer also see the temporary override during the run.

## Cases and duration

The six defaults are 2048/4096/8192 × Hard/PCF, First Level=1, Max Distance=150,
Transition=0.2, Screen Density off and screen-space denoise off. Each case can
also change First Level, distance, transition, Screen Density, target texel
pixels, LOD bias and denoise. Other CSM settings are copied from the selected
camera's effective Volume stack at startup; light bias and camera transforms
are not changed. Enter a source commit in **Revision** for reproducibility.

New presets use **Manual** recorder repaint after the controlled [repaint experiment](../Temp~/VSM/Roadmap~/VSMPhase0Findings.md).
Existing serialized settings can be replaced with **Load baseline preset (manual repaint)**.
Sampling continues independently of GUI repaint; **Refresh progress** requests
an explicit repaint. Manual suppresses periodic recorder repaints during sampling,
warmup, pauses and camera/timing waits. Entering a pause/wait, completion and errors
can repaint once so recovery controls remain visible. FourHz (0.25 seconds) and
OneHz (1 second) remain available for diagnostics. The session temporarily enables
Application.runInBackground and restores it on release so Game rendering can continue
while another application has focus. Keep the Game view open.

Defaults: 5 seconds of warmup, then at least 10 seconds and 300 observations.
Low frame rates extend the measurement until the minimum sample count is met.
Eight additional frames after the measurement-start snapshot keep snapshot
allocations and delayed timing results out of the stable sample window.
**Max Samples Per Case** bounds preallocated memory; hitting it early records
`sample_capacity_reached`, not a full-duration result. Pausing Play Mode pauses
collection. If its camera stops rendering, the session waits and retains samples;
it resumes with a new warmup when that camera renders again.

Use the same camera, viewport, animation state, light, exposure and VT content
for comparisons. Disable receiver/page diagnostic overlays and unrelated camera
views for performance runs. The recorder observes the current scene; it does
not freeze animation, disable other cameras, change VSync, enable the Unity
Profiler, or change Player Settings.

## Matched 4K repaint A/B capture

The existing [controlled experiment](../Temp~/VSM/Roadmap~/VSMPhase0Findings.md) found about 0.25-second
recurrence of sub-1 ms GPU values with FourHz and none in three Manual windows.
That experiment used **1920×1080 Game output**. It is evidence of recorder repaint
influence, not a new 3840×2160 capture or attribution of individual GPU results.

A subsequent [3840×2160 ABBA capture](../Temp~/VSM/Roadmap~/Experiments/RepaintIsolation_20260906/findings.md)
replicated the result: both Manual windows had zero recorder repaints and zero
sub-1 ms readings across 1,373 valid GPU samples; FourHz restored the periodic low
tail. All four cases passed coverage and matched recorded scene/camera/settings.
The report preserves raw data and documents the temporary graph and attribution limits.

1. Fix the Game camera output at **3840×2160** for both measurements, including
   viewport, render scale and any dynamic-resolution setting. Keep the same scene,
   camera pose, animation state, lights, exposure, VT content, VSync/frame cap,
   revision and Player timing settings. Keep Game rendering and hold the Editor
   layout, other open windows and focus state constant. Disable diagnostic overlays,
   quality capture and unrelated camera views before both measurements.
2. With no recording active, click **Load 4K Hard repaint A/B preset (2 cases)**.
   This creates FourHz followed by Manual with identical rendering inputs:
   VSM resolution 4096, Hard shadows, First Level=1, Max Distance=150,
   Transition=0.2, Screen Density off, target texel pixels=1, LOD bias=0 and
   screen-space denoise off. Here **4K in the preset means virtual shadow
   resolution 4096**, not Game output; the preset does not resize the Game view.
   Use the same warmup, duration, minimum samples, capacity and required-timing
   setting for both cases (defaults: 5 s, 10 s, 300, 30000 and enabled).
3. Enter a revision label, select the camera and click **Start new recording**.
   Leave the recorder untouched throughout both steady measurement windows;
   avoid **Refresh progress**, window interactions, resizing and focus changes.
   Manual progress can remain visually stale while collection continues.
   Both cases export into the same run folder. For two separate run folders,
   retain only the FourHz case for A, then reload the preset and retain only
   Manual for B; preserve every other setting and scene input.
4. Verify `renderWidth`/`renderHeight` are **3840×2160** in each `case_NNN.json`.
   Compare camera pixel/scaled dimensions in start/end and segment snapshots,
   with matching camera and VSM settings and no output-size/parameter changes.
   Inspect coverage, missing metrics, long-frame warnings and pauses/segments
   before accepting a match.
   Manual should have zero observed recorder repaints in an untouched stable
   window. Unexpected repaints require investigation and a retained repeat;
   Manual does not suppress repaints requested by Unity or other Editor windows.
5. Repeat the pair in reverse order (Manual then FourHz) to check order/warmup
   effects. **Load 4K Hard repaint sweep (3 rounds)** retains the larger rotating
   FourHz/OneHz/Manual experiment; **Tools > VividRP > Diagnostics > Run Timing
   Source Experiment** still starts that sweep, not the two-case preset.

Compare the full exported distributions; do not delete low GPU readings, clamp
them to 1 ms or recompute a filtered headline median. Run
`python Temp~/VSM/Roadmap~/Experiments/analyze_timing_source.py <run-directory>` on each
folder to produce `timing-source-audit.json` while retaining all raw CSV files.
The audit includes observation/valid counts, low counts and within-segment low
recurrence intervals, observed repaint counts, and whole-frame/Allocate/Resolve
medians. Use `summary.csv` for P95 and per-metric valid sample counts, and
`report.md`/case JSON for coverage and issues. For A and B, compare:

- Total observations, valid GPU count and coverage; sub-1 ms count **and fraction
  of valid GPU samples** (`low_count / gpu_valid`, unavailable when zero).
- Low-reading recurrence, including consecutive clusters and segment boundaries,
  against the FourHz 0.25-second schedule; no low readings means no recurrence
  interval, not an interval of zero.
- Whole-frame GPU median/P95 alongside `VSM.Allocate_gpu_ms` and
  `VSM.ResolveAndFeedback_gpu_ms` median/P95 and coverage. Stable stage costs with
  a changed whole-frame low-value pattern support a timing-source explanation;
  changes in workload, coverage or stages weaken that comparison.
- `observedWindowRepaints` and the raw repaint metadata. These count observed
  recorder GUI repaints; delayed GPU timestamps and camera observation IDs do
  not establish a one-to-one GPU-frame or Present attribution.

Prefer uninterrupted cases (`segments=1`). The case-level repaint total is the
last minus first observed cumulative count, so it includes repaints during gaps
between segments. For resumed captures, inspect count changes within each segment
and compare its snapshot; do not attribute paused UI work to active sampling.

Keep both runs, repeats and partial attempts with their warnings. A failed match
calls for another controlled pair, not selective sample removal. Even a clean
Manual pair retains `editor_frames_unattributed` and `comparable=false`; this
isolates the recorder's periodic repaint contribution without claiming control
of all Editor UI or a certified Game-only GPU timing source.

## Sampling controls and recovery

- **Pause and save — keep session** writes a checkpoint and retains samples,
  recorders and the temporary Volume. It does not finish the run.
- **Resume after warmup** preserves earlier samples and measured active time.
  After warmup and timing settlement, new observations have a new segment number.
  Pause, camera-wait, snapshot I/O and re-warmup time do not count toward the
  measurement duration. Segment-start scene snapshots are exported.
- **Retry current case (archive attempt)** saves the current attempt under
  attempts/case_NNN_attempt_NNN, resets only that case and starts warmup again.
  Previously completed cases remain in the run.
- **Skip current case** exports the current partial attempt as skipped and moves
  to the next case. Skipping cannot produce a successful sampling goal.
- **Finish session and release settings** is the explicit end operation; it
  saves partial results and releases temporary resources. Completion, closing
  the window, script reload or exiting Play Mode also release resources.
  A failed manual save pauses and retains memory for retry; forced window/reload/
  Play Mode teardown must still release Unity resources and logs the save error.

The window shows current case, phase, active seconds, sample count, valid
whole-frame GPU and Resolve sample counts, and every case's status. Buttons remain
above the settings scroll area. Finished reports stay visible; the output folder
and report can be opened from the window.

**Require Frame Timings** defaults on. At startup the recorder waits for an
actual fresh, positive GPU frame timing after warmup, not just IsFeatureEnabled.
If valid GPU frame timings stop arriving for one second, it waits with samples
retained. There is no former 15-second abort/release. Camera absence likewise
waits without releasing. Manual pauses require Resume; camera/timing/Editor-pause
recovery automatically starts a fresh warmup. A sampling error pauses the session
and records the concrete exception.

On an unsupported timing setup, **Continue with missing frame timings** explicitly
opts out of the timing requirement. Missing cells remain blank, and a case with
insufficient coverage cannot be reported as a comparable timing window. With
the requirement enabled, completion also needs the minimum number of valid GPU
frame timings. Other recorded metrics may still be missing; inspect the report.

## Output

Each run gets a unique folder below **`<project>/Logs/VSMBaselines`**, unless an
alternative directory is entered. Existing baseline sheets are not overwritten.

- `run.json` (schemaVersion=3): per-case outcomes, concrete stop/error reason,
  processed/goal-met/comparable counts, required-timing setting, revision label, Unity/API/GPU/driver, GPU memory, VSync/frame cap,
  quality shadow distance, pipeline asset, timing support, case list and initial
  scene snapshot.
- `case_NNN.json`: requested settings, start/end snapshots, actual VSM resolution,
  level count, physical page capacity and render size; selected camera/VSM-active
  frame counts; motion, output-size and parameter-mismatch flags; main light;
  and final allocator counters when a valid selected-camera snapshot exists.
  Counter order is allocated/requested/new assignments/overflow. This one
  synchronous readback occurs **after** measurement ends, before another camera
  takes ownership of the shared page pool; it is not a per-frame average.
- `case_NNN_samples.csv`: raw observations, observation frame/time,
  FrameTiming timestamp, **segment**, frame interval, CPU/GPU frame times, render-thread time,
  Editor-frame GC bytes, and each VSM stage's GPU timing.
- `summary.csv`: one row per case/metric, observation count, valid sample count,
  median, nearest-rank P95, min and max. Timings have `_ms` names; GC uses bytes.
  Checkpoints replace the current case's rows via per-case summary files and
  rebuild this aggregate, so repeated saves do not append duplicate measurements.
- `report.md`: readable per-case progress, missing metrics, validity counts,
  median/P95, warnings and concrete last error; includes not-started cases.
- `events.jsonl`: case start/end, pause/resume, segment start, explicit timing
  opt-out and session finish events with UTC, case/attempt and sample counts.
- `case_NNN_summary.csv`: current case's metric rows used to rebuild summary.
  Case JSON also includes segment-start snapshots, metric availability, long-frame/
  timestamp diagnostics, samplingGoalMet, comparable and issues.

Snapshots list every loaded scene's name/path, active/dirty state, the selected
camera's world transform, non-jittered matrices, lens/clip/viewport/render-size
settings, and active-hierarchy lights with position, rotation, direction, type,
intensity, color, shadow settings and VividRP biases. Unsaved scenes have empty
paths. Engine time/time scale are included. These snapshots describe the run;
they do not capture all dynamic caster or VT state needed to replay it exactly.

Schema 3 separates samplingGoalMet, timingCoveragePassed, frameTimingSource,
residencyStatus and qualityStatus. Coverage checks include at least 95% required
CPU/GPU/stage coverage, stable camera/output/settings and no retained >100 ms
intervals or failed final counter readback. Exact whole-frame camera attribution
is unavailable, so frameTimingSource is editor_frames_unattributed and comparable
remains false even when coverage passes. Quality remains not_validated. Optional
unexecuted stages remain explicitly unavailable.

Run-level processedCases counts cases advanced past; completedCases counts cases
meeting the sampling goal, and comparableCases counts certified comparable cases
(zero while whole-frame sources are unattributed).
The final status is completed_with_warnings if the case list finishes with issues,
or stopped_partial on an early finish. Required stages are LayoutRemap, Allocate,
ClearPhysicalPages, DynamicRaster, FinalizePages and ResolveAndFeedback.

## Timing interpretation

Schema 3 preserves all positive timings, including GPU values below 1 ms. It
reports their count and median recurrence interval without numeric filtering.
Four diagnostic columns follow the metric columns in raw CSV: observed_camera_frame,
observed_camera_serial, window_repaint_count, seconds_since_window_repaint. These
are observation metadata, not a mapping from delayed GPU results to camera frames.
The run also records the project path and the allowlisted D3D12 debug startup flag;
it does not export the full process command line.

Whole-frame timing requires **Player Settings > Frame Timing Stats** on supported
APIs. The tool uses FrameTimingManager's timestamps to reject duplicate results.
Unavailable whole-frame GPU data stays blank. Stage GPU timings use independent
ProfilerRecorders; missing/skipped stages stay blank, with valid counts shown.
Stage medians are therefore conditional on observed executions, not averages
over skipped frames. No GPU time is inferred from CPU time or frame rate.

FrameTiming and GPU recorder results arrive later than the observed player frame;
columns in one raw row are **not** a synchronized GPU-event capture. The tool
discards boundary frames and measures steady windows, without draining the last
pending results. See Unity's [Frame Timing Manager documentation](https://docs.unity.cn/6000.1/Documentation/Manual/frame-timing-manager.html).

GPU stage totals include all cameras using those markers. `VSM.PageCull` is
nested in caster culling and must not be added again to a VSM total. Whole-frame
CPU/GPU and GC measurements include Editor and unrelated rendering work; they
do not certify VSM-only cost or all-thread zero allocation. Capturing timings
itself adds overhead, so record the same settings for each comparison.

## Validation

The repaint fix passes focused Roslyn compilation of Runtime, Editor and
Editor.Tests with no new warnings. A standalone probe using the actual
`OnEditorUpdate` and scheduling methods verifies Manual sampling, paused/waiting
silence, one refresh on attention changes, periodic-mode recovery, completion,
errors and Play Mode exit; 100,000 warmed updates allocated zero managed bytes.
The actual collector/export probe preserves sub-1 ms readings and observation
order in raw CSV and summary statistics, including missing-data blanks.
These probes use Editor/session doubles and do not establish live GPU behavior.
No new 3840×2160 capture or Unity Test Framework run was performed for this fix;
the Editor was running. Run `VSMBaselineRecorderTests` manually, then use the
matched capture procedure above and the Unity Profiler to verify relevant threads.

The current changes pass focused Roslyn compilation of Runtime, Editor and
Editor.Tests. A standalone probe of the actual sample/clock code passes export,
segment and checkpoint checks, with zero managed bytes over 100,512 warmed calls.
A separate probe of the actual window methods passes six control/error branches
using a session test double, including failed saves and forced teardown. These
checks do not verify Unity resource lifetimes or live GPU timing coverage.

Focused regression tests are in `VSMBaselineRecorderTests`: missing-data
statistics, invariant CSV export/escaping, bounded storage, zero managed bytes
in warmed sample collection and complete parameter override/mismatch detection.
Additional cases cover resume time excluding pauses/warmup, checkpoint replacement
without duplicate summary rows, segment export, 95% coverage boundaries and warmed
managed allocation of the four unified profiling scopes.
Run these manually while using an interactive Editor. A Play Mode smoke run
should additionally verify the active scene's timing availability, parameter
switches, pause/save/resume without resource disposal, camera/timing waits, retries,
skip/finish/exit cleanup, save-failure recovery and exported snapshots before
using the resulting measurements to close the P5 performance gates.

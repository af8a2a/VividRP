# P5-A — VSM quality and performance baseline

Status: instrumentation and reproducible protocol implemented, 2026-09-05.
This milestone does not change resolution, layer selection, filtering or budgets.
The six-configuration scene/performance sweep remains to be measured on target hardware.
P5-B adds an opt-in [receiver quality policy](VSMReceiverQuality.md). The capture
and six-configuration P5-A reference below use Screen Density off; record that
policy explicitly when comparing newer results.

## Receiver inspection

Add **VSMReceiverDebugPass** to a diagnostic graph. Connect the same **Depth** and
**GBuffer1** inputs as CSMShadowResolvePass, and connect that pass's completed
**DirectionalShadowTexture** output to the identically named input of the debug
node. Connect **OutputTexture** to an overlay or final debug display. This input
dependency is mandatory: the existing VSMDebugPass before resolve cannot inspect
the current frame's receiver selection.

**DiagnosticData** is an unfiltered RGBA32F texture with the following raw values.
All level indices are zero-based within the uploaded projection array, not the
absolute radius exponent. Preferred means the active policy's pre-residency
selection: legacy P4 coverage by default, or P5-B density constrained by coverage.

| Mode | DiagnosticData RGBA | Display |
| --- | --- | --- |
| PreferredLevel | preferred, sampled, transition level, requested transition weight | preferred-level color |
| SampledLevel | same | sampled-level color |
| FallbackLevels | same; subtract R from G for fallback count | green=direct, toward red over 4 fallback levels |
| TexelFootprint (default) | projected X/Y virtual-texel axis lengths in screen pixels, world texel size, valid=1 | green ≤1 px; red ≥4 px |
| SamplingWork | attempted taps, completed depth comparisons, attempted levels, valid transition sample count (0/1) | blue=cheap, red ≥36 attempted taps |
| Availability | failure bitmask, recomputed VSM shadow, source shadow, absolute difference | green=complete, amber=failed attempt with usable sample, magenta=no usable sample, gray=outside selection/distance |
| QualityPolicy (P5-B) | unclamped density LOD, finest covered level, preferred level, preferred footprint/target | green ≤1× target; red ≥4×; legacy policy unavailable |

Failure bits are ORed across attempted levels/taps: unmapped=1, dirty=2,
ownership/allocation inconsistency=4, out-of-map/invalid projected coverage=8.
This does **not** distinguish allocation-budget misses from newly exposed requests;
correlate with the existing Requested/Evicted/Overflow page views and allocator
counters. An allocated completed zero-depth page is valid lit data, not a miss.
Transition weight is the requested weight; it is not applied when transition
coverage is absent or the primary has already fallen back.

Sky is black with raw (-2,-2,-2,-2). No current camera/frame VSM+resolve snapshot,
unsupported/disabled VSM, unavailable inputs, or a skipped debug camera leaves
magenta and raw (-1,-1,-1,-1). A degenerate receiver plane reports footprint X/Y=-1
and valid=0 rather than an invented finite density. Footprints are local geometric
plane differentials, approximate at silhouettes and thin geometry; the two axis
lengths are not a singular-value bound for every direction.

The debug kernel reuses the production receiver reconstruction, bias, point/PCF
sampling, transitions and missing-page fallback. Diagnostic counters are
thread-local; **MarkVSMReceiverPage is compiled out of this kernel**. It neither
changes feedback nor writes page ownership, metadata or physical depth. It runs
only after the exact camera/frame has completed both raster and receiver resolve.

Remove/disable the node for performance captures. No diagnostic texture or
dispatch is added to ordinary graphs; production kernels compile without the
instrumentation define. At 1080p, the two debug outputs alone cost about 39.6 MiB;
debug replay duplicates receiver work and must not be included in shipping cost.
There are no automatic GPU readbacks or per-pixel global statistic atomics.

## Timing markers

| Marker | Meaning |
| --- | --- |
| VSM.LayoutRemap | projection upload, integer-scroll table/metadata copies and remap |
| VSM.Allocate | allocation/eviction and existing four allocator counters |
| VSM.InvalidateStatic | full or bounds-local static invalidation |
| VSM.ClearPhysicalPages | allocated dynamic clear plus dirty static clear |
| VSM.StaticCasterCull / VSM.DynamicCasterCull | source instance/meshlet culling plus page-request preparation |
| VSM.PageCull | nested subset: page requests, indirect preparation, small/large-page culling |
| VSM.StaticRaster | static raster including its raster-depth clear |
| VSM.FinalizePages | completed static-page flags |
| VSM.DynamicRaster | dynamic raster including its raster-depth clear |
| VSM.UnityCompatibilityRaster | all compatibility levels/tiles, clears and renderer-list submissions |
| VSM.ResetFeedback | ownership/repeated-frame feedback reset, when required |
| VSM.ResolveAndFeedback | full-screen resolve and receiver request atomics in the same dispatch |
| VSMReceiverDebugPass | diagnostic replay only; exclude from performance totals |

Use Unity GPU Profiler / RenderDoc / GPU timing tools to collect these scopes.
CPU command-recording durations are not GPU durations. PageCull is nested: do not
sum it again with the enclosing caster-cull marker. A missing marker can mean
the stage was skipped; record that separately from an unavailable timing sample.
Request collection cannot be independently timed without splitting the current
resolve dispatch, so no separate request-GPU-time claim is made by this milestone.

## Fixed baseline: Sponza wall capture

Reference capture: `E:/vsm-issue.rdc`, 446,814,470 bytes. EID 1400 allocator,
EID 1959 resolve, EID 2343 final blit. This is a correctness/quality baseline,
not a timing measurement. Keep the capture unchanged.

- Output: 1920×1080; camera position (0.647201836, 3.976917982, 5.449532509).
- Camera vertical FOV: approximately 60 degrees; near/far: 0.01 / 1000.
- Camera world basis (right / up / backward):
  (0.999945462,-0.007762608,0.006980523),
  (0.007744990,0.999966800,0.002547351),
  (0.007000066,0.002493148,-0.999972463).
- Light direction toward source: (0.043503992,0.861320138,-0.506196737).
- First Level=1, Max Distance=150, transition=0.2, constant/normal/slope bias=1/1/2.5.
- Effective virtual resolution=2048; PCF off; 128-texel pages; capacity=256.
- Resident/requested/new/overflow = 73/73/0/0.
- Raw receiver ROI x=180..1189, y=525..554: all 30,300 pixels prefer/sample index 2,
  radius 8 m, no fallback and no transition. World texel size=0.0078125 m.
- Around raw pixel (600,540), virtual texel Y projects to approximately 5.09 px.
  Observed tooth height=4–5 px, pitch predominantly 31–34 px.
- Captured-resource CPU reconstruction agreed within 0.01 at 30,299/30,300 pixels;
  this is not bit-exact emulation and does not prove all scene shadows are correct.

Use a saved camera for fresh captures; do not mix Scene/Game snapshots or move
the camera between configurations. Preserve light rotation, animation time,
caster ownership, VT residency, bias, exposure and output resolution. Record the
source revision, API, GPU, driver and actual resolution rather than just Volume=0.

## Six-configuration protocol

Record 2048/4096/8192, each with Hard and PCF. Keep the physical budget at 256 for
this first sweep. Do not assume finer virtual resolution implies finer sampled
data: record preferred/sample levels and fallback distribution alongside quality.
Use `VSMQualityBaseline.csv`; empty cells mean unmeasured, never zero.

For each configuration:

1. Change quality only at a measurement boundary. Allow resource/bootstrap and
   VT/cache warmup to settle; record cold-start/first-frame behavior separately.
2. With receiver diagnostics enabled, capture Levels, Footprint, Work and
   Availability for the fixed wall ROI. Check source/recomputed shadow difference
   (allow R16 output rounding), missed/dirty coverage and layer transitions.
3. Disable the diagnostic node and other overlays. Collect at least 300 stable
   frames of per-stage GPU timings; report median and P95 with memory/GC data.
   Repeat if the OS/GPU is under unrelated load. A RenderDoc replay duration is
   not a substitute for uncaptured live-frame timing.
4. Separately exercise slow/diagonal translation, page/level crossings, FOV zoom,
   a camera cut, moving dynamic casters, alpha/VT changes and over-budget feedback.
   Record miss/recovery frames and invalidation spikes, not only averages.
5. Verify the warmed CPU/render-worker paths in Unity Profiler. Current-thread
   allocation tests cover their measured helper path only, not all threads.

Do not tune bias between configurations to conceal aliasing, or silently increase
capacity. Higher resolution / PCF, adaptive allocation and temporal stabilization
remain P5-B onward. GPU milliseconds and a recommended default quality tier are
deliberately pending the sweep; the existing capture supplies no such measurements.

## Validation and remaining gates

P5-A validation on 2026-09-05 (before P5-B; these results and the DXIL hash are
historical, not validation of the later receiver-policy changes):

- Unity 6000.7.0a6, DX12, NVIDIA GeForce RTX 5070 Ti: **50/50 focused EditMode
  tests passed**, none skipped. Test classes: `VSMReceiverDebugPassTests` and
  `VirtualShadowMapSamplingTests`. Batch tests ran while no interactive Editor
  was open; they are synthetic GPU checks, not the Sponza performance sweep.
  Batch logs also contain package initialization and Editor-shutdown allocation
  diagnostics; passing focused tests is not a clean whole-Editor memory audit.
- Coverage includes generated node registration, input/output ports, exact
  camera/frame snapshot ownership, Hard/PCF diagnostic equivalence, unchanged
  feedback metadata, fallback/dirty/empty/missing cases, transitions and footprint
  scaling. All six modes also execute the production `VSMReceiverDebug` entry,
  checking raw output and the sky sentinel. Existing sampling/allocator/bias
  regression cases pass alongside them.
- Warmed diagnostic sizing/mode/snapshot helpers and cached/literal marker
  recording measured **zero managed bytes** across 256 repetitions on the test
  thread. This does not certify the full render loop or other threads.
- Targeted Roslyn compilation passed for Runtime, Editor and Editor.Tests.
  DXC compiled all **33 entry points** in the resolve and sampling-test compute
  files. Production `CSMShadowResolve` DXIL is byte-identical to the pre-change
  version under the same compilation flags (SHA256
  `0A29C36424DA7453733DEAD3F7681830EECB943ABCAC245154E4E286E8784477`).
  Diagnostic DXIL has no atomic or buffer-store operations.
- GPU test pools now use R32_UInt UAV RenderTextures, matching production.
  Unity 6.7 rejects the previous Texture2D fixture's filtered-Sample format
  validation before dispatch; the test-only upload kernel removes that fixture
  limitation without changing production pool behavior.

Still pending: connect the node in the scene graph and visually inspect the
fixed ROI; perform the six-configuration live timing/quality sweep; verify
movement/cuts, budget pressure, VT and mixed-caster cases in that scene; and
profile warmed allocations across all relevant threads. Other API/device
coverage is not established by the DX12 result. No production graph, Volume
profile, quality default or page budget was changed for this milestone.

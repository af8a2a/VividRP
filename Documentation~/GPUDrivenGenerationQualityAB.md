# Cluster generation quality: moving roof A/B

Date: 2026-10-15. Candidate source: `7103e223`; baseline builder: before that
commit, archived in `Temp~/cluster-lod-quality/harness/`.

## Result

The change improves rendered roof normal and base-color accuracy against the
finest resident LOD, but **does not improve raw UV-coordinate RMSE overall**.
Actual main-view triangles increase 3.15%; this run does not establish a stable
whole-camera GPU time penalty. No new background holes, incomplete LOD cuts,
ancestor/descendant overlap or duplicate draws were found on this route.

This compares the complete generation change, including tighter bounds and
different LOD transition distances. It does not isolate attribute weights from
bounds/error changes, nor compare the two generators at equal triangle counts.

## Controls and capture

- A uses the previous builder: AABB-circumscribed group spheres, fixed minimum
  error increments, and position-only simplification with its sloppy fallback.
- B uses direct sphere merging, outward-rounded additive error, normal weight
  0.1 and source-density-normalized UV multiplier 0.05, without weighted sloppy
  fallback. Both use the same current runtime traversal and packed group table.
- 83 unique assets used by 217 renderers were paired. The old assets were built
  into temporary Play Mode objects. Every finest meshlet's vertex/index data
  matched the current asset; original imported assets were not overwritten.
- Unity 7000.0.0a9, D3D12, RTX 5070 Ti; output 1920x1080, TSR Balanced internal
  buffers 1130x636, FOV 59.999985, near/far 0.3/1000, LOD budget 1 pixel.
- The existing 399-pose route is unchanged: distance 6–100 and back, oscillation
  around 42.295 and 98.353, and four stationary holds. Camera rotation is Euler
  (43.272297, 304.76556, 1.6177362); position is (6.181255, 18.638472, -10.503865)
  plus forward times (12 - distance).
- A1/B1/B2/A2 use identical TSR jitter sequences and 192 stationary warmup
  renders. Captures include final PNG, depth, visibility IDs, UV and geometric
  normal attributes, GBuffer normal/base color, exact GPU main/shadow LOD
  predicates, and actual visible/recovered draw requests and indirect arguments.
- R2 is the finest-LOD reference, using a temporary global Volume with forced
  depth 32. Every frame/view was checked against the expected finest leaf set;
  main selection is constant at 2,288 nodes. The first reference attempt, R1,
  was excluded: the normal Volume overwrote a direct system-field override.
- The failed first pilot's unsupported direct R32G32_UInt readback was replaced
  by an integer structured-buffer copy. Failed/excluded captures are retained
  and are not included in any quality or timing aggregate.

## Attribute quality

Metrics use the same pixels for A and B against R2. Pixels must have the same
visible instance in all three captures and view-depth difference at most
`0.005 * referenceDepth + 0.01` meters; the common mask is eroded one pixel.
Roof metrics include 15,612,961 matched pixel observations. RMSE is pooled over
pixels, not averaged over per-frame RMSEs. Angular error uses atan2 of cross
product magnitude and dot product, so identical decoded normals have zero error.

| Roof metric | Previous A | New B | Change |
| --- | ---: | ---: | ---: |
| Raw UV-coordinate RMSE | 0.0121203 | 0.0127958 | **+5.57%** |
| Geometric normal mean angle | 0.697537 deg | 0.546017 deg | -21.72% |
| GBuffer normal mean angle | 0.697558 deg | 0.546177 deg | -21.70% |
| GBuffer base-color RMSE, 0–1 | 0.0101499 | 0.0091403 | -9.95% |

The complete scene has the same mixed outcome: raw UV RMSE increases from
0.0097278 to 0.0121984, while mean geometric normal error decreases from
0.352043 to 0.248904 degrees and base-color RMSE from 0.0067212 to 0.0059480.
Coordinate distance is not equivalent to texture appearance, especially across
UV seams/repeats; base-color sampling is reported separately. These results do
not justify claiming a universal UV-quality improvement from the default weight.

| Roof distance band | UV RMSE A / B | Geometric normal angle A / B | Poses with higher B UV RMSE |
| --- | ---: | ---: | ---: |
| 6–30 | 0 / 0.0000190 | 0 / 0.000872 deg | 4 / 73 |
| 30–60 | 0.0132877 / 0.0140136 | 1.15110 / 0.96665 deg | 91 / 148 |
| 60–100 | 0.0375739 / 0.0396835 | 5.75138 / 4.29426 deg | 107 / 178 |

Close roof geometry is usually identical to the finest reference. At intermediate
and far distances, normal and sampled base-color gains coexist with occasional
UV seam outliers and changed LOD choices. The earlier synthetic equal-triangle
experiment is not a substitute for these rendered asset results.

## LOD, coverage and temporal behavior

Each version's graph is analyzed independently because its nodes/groups differ.
The effective five runs (A1/B1/B2/A2/R2) contain 342,631,674 finest-to-root
path/view/frame checks, with zero missing cuts, overlapping ancestor selections,
shared-group disagreement, duplicate actual draws or draws from unselected nodes.

- A/B depth differs at all 399 poses because generated geometry/LOD cuts differ.
  Nevertheless, zero pixels covered in A become background in B, including
  zero interior lost-coverage pixels. A1/A2 and B1/B2 depth is byte-identical.
- A supplementary BF run uses B assets with flat main-view traversal. At all
  399 poses it matches B1 depth, main/shadow predicates and actual draw sets.
  Four individual pixels have different visibility IDs at identical depth with
  the same draw set, consistent with equal-depth draw-order ties. This control
  finds no extra visible-geometry rejection from tighter hierarchy bounds.
- Moving node-flag changes increase from 8,664 to 9,510 scene-wide and from
  870 to 972 on the roof. Both same-version repeats reproduce these counts.
  Different cut locations and more levels are not themselves evidence of flicker.
- In the four original stationary holds, B has zero selected-node changes. A
  repeats the existing 45 changes at poses 254–265 (Arch_H001, not roof). These
  holds are at the old thresholds; this is not proof that all possible new
  thresholds are immune to temporal jitter.

| Image comparison | RGB MAE, 8-bit units | Temporal difference-residual MAE |
| --- | ---: | ---: |
| A1 / B1 | 0.044203 | 0.062161 |
| A2 / B2 | 0.044394 | 0.062432 |
| A1 / A2 repeat | 0.023223 | 0.039038 |
| B1 / B2 repeat | 0.023126 | 0.038877 |

Moving A/B differences exceed same-version shading noise, as expected from
different cuts/attributes. The synchronized video/contact sheet retain the
session's existing upper-right debug rectangle and minification/TSR behavior.
No new coverage flicker or cut failure was observed; this does not assert zero
visible LOD approximation changes or eliminate baseline temporal shading effects.

## Geometry and GPU cost

| Work per pose | A mean | B mean | Change |
| --- | ---: | ---: | ---: |
| Actual main-view drawn triangles | 70,172.8 | 72,382.0 | +3.15% |
| Actual roof drawn triangles | 9,876.0 | 9,896.4 | +0.21% |
| Main LOD-selected triangles before culling | 101,690.2 | 105,837.9 | +4.08% |
| Shadow LOD-selected triangles across 17 views, before culling | 2,237,794 | 2,378,794 | +6.30% |

Shadow figures count selection, not actual VSM raster work. Geometry need not
increase at every pose: tighter spheres can also permit earlier coarsening.

Timing is independent of image capture: no screenshots, diagnostic compute or
readbacks. Six BA/AB alternating pairs each use 192 stationary warmup renders,
one 399-pose moving warmup cycle and two measured cycles. All 9,576 observations
are valid with positive values and one sample for each of six GPU markers.
No positive outliers or timing windows were removed. Each mode has 4,788 samples.

| GPU scope | A ms | B ms | B - A | Paired 95% interval |
| --- | ---: | ---: | ---: | ---: |
| Camera total | 2.351084 | 2.355370 | +4.287 us | -19.602 to +28.175 us |
| PreGraph | 1.142210 | 1.140667 | -1.543 us | -4.820 to +1.735 us |
| RenderGraph | 1.207639 | 1.213591 | +5.952 us | -16.964 to +28.868 us |
| Main culling total | 0.033993 | 0.032959 | -1.034 us | -2.068 to -0.00016 us |
| Instance / hierarchy traversal | 0.016178 | 0.016072 | -0.106 us | -0.804 to +0.593 us |
| Cluster final selection | 0.005158 | 0.005365 | +0.207 us | -0.115 to +0.529 us |

Whole-camera mean changes by +0.18%, but the interval crosses zero: a stable
whole-camera cost or speedup is **not established**. The culling interval barely
excludes zero and is insufficient to justify a general performance claim.
Intervals use six paired window means with Student's t (5 degrees of freedom),
not correlated individual frames. GPU clocks are unlocked; endpoint telemetry
and per-window p50/p95/p99/max values are retained. GPU recorder observations are
delayed and are not assigned to an exact CPU pose. Scope excludes Editor UI.

## Evidence and restoration

Local artifacts under `Temp~/generation-quality-ab/`:

- `report/report.html`, `roof-generation-ab.mp4`, `contact-sheet.png`, plots.
- `report/summary.json`, `image-summary.json`, `geometry-summary.json`,
  `flat-control.json`, `timing-summary.json`, `timing-windows.csv`.
- A1/B1/B2/A2, valid R2 reference, BF control, and twelve TA/TB timing windows.
- Excluded pilotA/R1, route definitions, 83-asset finest-data verification,
  archived instrumentation/analysis scripts, and source hashes.

Original asset bindings, camera transform, Play Mode, timing settings and
screenshot preference were restored. The pre-existing dirty scene was not saved.
Temporary reference Volume/assets and Editor scripts were removed; four
instrumented production files were restored byte-for-byte. Unity Test Framework
was not run in the interactive Editor. This task changes documentation only;
it does not retune production weights based on the benchmark.

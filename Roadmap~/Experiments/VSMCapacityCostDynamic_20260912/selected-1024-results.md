# Selected 1024-ray dynamic reference comparison

This table supersedes the 256-ray response-tail interpretation for the six selected scenarios. All 18 original production outputs were independently compared with the matching 1024-ray reference capture: complete world/normal arrays are bitwise identical, device-depth maximum delta is zero, and camera/light/fixture pose, phase, geometry hash, and fixture backend match. No unmatched subset was silently treated as a complete pair.

The original production signals remain fixed. The higher-ray capture contributes only reference visibility; its own shadow output and history never enter the comparison. The reference remains finite fixed quadrature with .001/.01 m normal-bias sensitivity checks, forced-opaque alpha, and sampled opaque-floor ROI. It is not exact whole-scene visibility truth.

## Floor visibility error

| Scenario | Fixed 4 MAE | Fixed 8 MAE | Adaptive MAE |
|---|---:|---:|---:|
| static | 0.015244 | 0.014788 | 0.015248 |
| thin_sweep | 0.015553 | 0.015006 | 0.015580 |
| deform | 0.010036 | 0.009788 | 0.010043 |
| receiver_lift | 0.009560 | 0.009263 | 0.009563 |
| sun_rotate | 0.015154 | 0.014336 | 0.015154 |
| sun_step | 0.012775 | 0.012339 | 0.012777 |

MAE and its positive (under-occlusion) / negative (over-occlusion) components use the .001 m reference. Both bias versions and sensitive-sample fractions are retained in `selected-1024-quality-jump01.json` and `selected-1024-three-modes.json`.

## Response at the common .1 visibility-jump threshold

Detection is a sustained two-frame crossing of the event midpoint, within delay 0–8. P95/P99 condition on detected events; the undetected denominator is detected plus fully observed, never-detected events. Reference reversal, changed surface, and capture-end censoring are reported separately. Different numbers of detected events must not be read as different geometric-event populations.

| Scenario / mode | Detected | Not detected through delay 8 | Undetected fraction | Conditional P95 | Conditional P99 |
|---|---:|---:|---:|---:|---:|
| thin_sweep / fixed4 | 1551 | 0 | 0.0000% | 0 | 2 |
| thin_sweep / fixed8 | 1621 | 1 | 0.0617% | 0 | 1 |
| thin_sweep / adaptive | 1535 | 0 | 0.0000% | 1 | 2 |
| sun_step / fixed4 | 1871 | 31 | 1.6299% | 0 | 3 |
| sun_step / fixed8 | 1874 | 30 | 1.5756% | 0 | 1 |
| sun_step / adaptive | 1871 | 31 | 1.6299% | 0 | 3 |

| Scenario / mode | Eligible geometric events | Already on new side | Capture-end censoring | Reference reversal / surface loss |
|---|---:|---:|---:|---:|
| thin_sweep / fixed4 | 2850 | 178 | 4 | 1117 |
| thin_sweep / fixed8 | 2850 | 138 | 2 | 1088 |
| thin_sweep / adaptive | 2850 | 190 | 4 | 1121 |
| sun_step / fixed4 | 2165 | 263 | 0 | 0 |
| sun_step / fixed8 | 2165 | 261 | 0 | 0 |
| sun_step / adaptive | 2165 | 263 | 0 | 0 |

At .5, thin_sweep has 312 eligible events in each mode: 3 were already on the new side, 158 were detected with conditional P95/P99 both zero, and 151 lost the reference-direction/surface condition. There were no fully observed undetected events. Sun_step never reaches .5. This strong-jump result does not establish zero delay for all 312 events or all dynamic behavior.

The 1024 reference changes the thin_sweep conclusion from its 256-ray estimate: adaptive P95 becomes 1 rather than 0, and its two apparent fully observed misses disappear. The thin event-set Jaccard is 97.57% at .1. Sun_step is more stable: adaptive P99 remains 3, with 31 fully observed undetected events. Fixed 8 improves conditional P99 to 1 in both measured scenarios, but the miss counts do not support a universal “fewer misses” claim.

## Sampled residual beyond the filter envelope

| Scenario / mode | Eligible event observations | Old-side residual observations | Maximum old-side error |
|---|---:|---:|---:|
| thin_sweep / fixed4 | 3364 | 4 | 0.112793 |
| thin_sweep / fixed8 | 3492 | 0 | not measured |
| thin_sweep / adaptive | 3307 | 4 | 0.112793 |
| sun_step / fixed4 | 801 | 4 | 0.117188 |
| sun_step / fixed8 | 711 | 0 | not measured |
| sun_step / adaptive | 801 | 4 | 0.117188 |

These are stride-4 sampled event observations, not unique pixels or full-resolution proof. The conservative sampled envelope may miss unsampled geometry, and residuals can include estimator or geometry bias; they are not all established temporal ghosting. Zero counted residuals is limited to that sampled coverage.

## Moving receiver and remaining coverage limits

| Receiver top / mode | Samples | MAE | Positive error | Negative error | Error > .25 | Median history age |
|---|---:|---:|---:|---:|---:|---:|
| fixed4 | 137240 | 0.025574 | 0.018293 | 0.007280 | 0.03279% | 1 |
| fixed8 | 137240 | 0.025031 | 0.017971 | 0.007060 | 0.03425% | 1 |
| adaptive | 137240 | 0.025575 | 0.018294 | 0.007281 | 0.03279% | 1 |

The receiver-lift top meets the normal contract for 99.983% of height-and-footprint candidates over all 32 frames. Its history-age median drops from 4 while stationary to 1 during motion, then recovers when motion stops. This supports active history rejection on the sampled top; screen-grid captures still do not establish material-point tracking latency.

Deform and continuous sun rotation have real reference changes, but neither supplies .1/.5 tracked-floor events; they support along-trajectory error only. The deform backend is a prebaked meshlet frame sequence, not a live skinned or arbitrary vertex-deformation implementation. Camera slide/turn and receiver_slide have no 1024 capture, so their 256-ray per-frame results remain separate and do not inherit this convergence validation. The camera samples also lack motion-corresponded response events.

## Reproduce

From the package root (restore the archived raw captures first if the Temp directories are absent):

```powershell
python Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/analyze-reference-convergence.py Temp~/vsm-baseline/dynamic/20260912_075412_506 Temp~/vsm-baseline/dynamic/20260912_075856_113 --all-base-modes --output Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/selected-1024-three-modes.json
python Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/summarize-selected-reference.py Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/selected-1024-three-modes.json --base-report Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/final-dynamic-quality-jump01.json --output-directory Roadmap~/Experiments/VSMCapacityCostDynamic_20260912
```

# Initial dynamic capture: fixture validation failure retained

The 256-ray capture `Temp~/vsm-baseline/dynamic/20260912_073537_090` completed all 24 cases, but this is **not a completed dynamic-quality acceptance baseline**. The companion 1024-ray capture is `Temp~/vsm-baseline/dynamic/20260912_073931_711`. Both remain useful for diagnosing the harness; raw capture completion is distinct from valid fixture evidence.

`dynamic-quality-jump05.json`, `dynamic-quality-jump01.json`, and `reference-convergence.json` explicitly carry `status: invalid-dynamic-fixtures`. Cases `thin_sweep`, `deform`, `receiver_slide`, and `receiver_lift` are excluded from quality conclusions after independent runtime checks found fixture geometry/normal-contract problems. Static, camera-motion, and sun-rotation cases remain usable within the limitations below. A corrected fixture capture must supersede the excluded results.

## What the offline validation establishes

- All 24 initial cases have 32 consecutive receiver frames, phase aligned to step, and camera/light/fixture pose matching the declared scenario plan. Every same-scenario mode pair has matching metadata, fixture phase/hash, world positions, and reference values. Pairing alone cannot validate whether the intended fixture was actually visible to the production GBuffer and reference RTAS.
- The receiver-lift candidate top is geometrically visible: height plus footprint selects about 3,582–4,695 samples per frame. The decoded normal Y is almost always zero, so the required `normalY > .9` correctly rejects them. Only eight samples over all 32 frames pass the full ROI in each mode. Those eight samples cannot establish a moving-receiver accuracy or history-rejection result.
- The normal mismatch is not inferred from a missing object or a flipped reconstructed position: candidate world heights match the lifted cube. The first captured color frame also shows its top. The geometry-only mask is recorded only as a normal-contract diagnostic, never promoted to a valid accuracy ROI.

## Valid evidence and response-coverage limits

Floor visibility MAE against the finite 256-ray, .001 m normal-bias reference:

| Scenario | Fixed 4 | Fixed 8 | Adaptive |
|---|---:|---:|---:|
| Static | 0.015308 | 0.014853 | 0.015308 |
| Camera slide | 0.014939 | 0.014516 | 0.014938 |
| Camera turn | 0.014782 | 0.014322 | 0.014782 |
| Sun rotate | 0.015218 | 0.014403 | 0.015218 |

These are sampled opaque-floor errors, not whole-scene truth. The reference forces alpha opaque and is finite fixed quadrature. Positive/negative components, .01 m bias results, and bias-sensitive fractions are retained in the JSON.

Both event thresholds, **.5 and .1**, were evaluated uniformly for every mode. No case supplies eligible tracked-floor events from which to estimate first-detection P95/P99:

- Camera slide has 178 / 3,615 raw events at .5 / .1; camera turn has 128 / 3,036. After bias filtering, all remaining events change world position by more than the .02 m correspondence tolerance. A fixed screen pixel is not a tracked receiver during camera motion.
- The sun does move, but its 256-ray maximum adjacent-frame floor-reference change is only .03515625. The 1024-ray maximum is .0234375. Neither crosses .1.
- Static has no dynamic transition by construction. Invalid fixture cases cannot supply response evidence.

Consequently, delay percentiles and undetected fractions are `null` with `insufficient_detectable_events`, and sampled filter-support residual has no eligible observations. None of these are zero-lag or zero-ghosting passes. No final dynamic acceptance plot is generated from the invalid fixture data.

## 256 to 1024 reference convergence

The five adaptive cases have exactly identical phase, pose, fixture hash, world arrays, validity, and depth across captures. Production output from the original 256-ray run is held fixed while replacing only the reference field. The invalid fixture cases remain excluded despite successful pairing.

| Usable case, .001 m bias | Mean absolute field difference | Field P99 | Field maximum | Adjacent-delta error P99 | Original-signal MAE against 256 / 1024 |
|---|---:|---:|---:|---:|---:|
| Static | 0.001759 | 0.013672 | 0.025391 | 0 | 0.015308 / 0.015239 |
| Sun rotate | 0.001733 | 0.013672 | 0.027344 | 0.011719 | 0.015218 / 0.015147 |

The .01 m bias shows similar field differences (static 0.001752; sun 0.001727). Neither reference creates .1 or .5 eligible floor events. There is no 1024-ray camera capture, so camera-motion reference convergence is not established. Agreement between two finite quadratures is a convergence check, not proof of exact visibility.

## Reproduction

Run from the package root with Python and NumPy. The original Temp paths are encoded in JSON; when raw files are archived, restore their relative capture directories first.

```powershell
python Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/analyze-dynamic.py Temp~/vsm-baseline/dynamic/20260912_073537_090 --jump-threshold .5 --invalid-fixture-scenarios thin_sweep deform receiver_slide receiver_lift --output Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/dynamic-quality-jump05.json
python Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/analyze-dynamic.py Temp~/vsm-baseline/dynamic/20260912_073537_090 --jump-threshold .1 --invalid-fixture-scenarios thin_sweep deform receiver_slide receiver_lift --output Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/dynamic-quality-jump01.json
python Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/analyze-reference-convergence.py Temp~/vsm-baseline/dynamic/20260912_073537_090 Temp~/vsm-baseline/dynamic/20260912_073931_711 --invalid-fixture-scenarios thin_sweep deform receiver_slide receiver_lift --output Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/reference-convergence.json
```

The two Python self-test entry points cover event detection, censoring, rejection of changed receivers, bias filtering, support masks, pairing exceptions, fixture hashes, and event-set comparison. They do not replace independent GPU fixture validation.

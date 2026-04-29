# Local Exposure

`LocalExposurePass` ports the bilateral Local Exposure path used by Unreal's histogram and local-exposure post-processing flow into VividRP's explicit RenderGraph model.

## Graph Order

Add the pass explicitly in graph assets. A typical post-processing chain is:

```text
AutoExposurePass -> LocalExposurePass -> BloomPass -> ColorGradingPass -> FinalBlitPass
```

The pass reads `source` and writes `LocalExposureOutput`. It implements the existing post-process source override contract so later source-based passes can consume the adjusted scene color through the normal chaining path.

## Scope

This implementation includes the bilateral local exposure path only. It does not include Unreal's experimental Fusion path or the Local Exposure visualization mode.

The pass uses:

- 64x64 bilateral-grid tiles.
- 32 luminance slices.
- An `R32G32_SFloat` 3D grid storing weighted log-luminance sum and weight.
- The current VividRP auto-exposure buffers for exposure scale and pre-exposure.
- A bounded separable Gaussian approximation for blurred luminance.

## Controls

Use the `Local Exposure` volume component to enable the pass and tune highlight/shadow contrast, optional highlight/shadow contrast curves, detail strength, blurred luminance blend/kernel percent, threshold controls, and middle grey bias.

No existing `.vrdg` asset is edited automatically. Insert `LocalExposurePass` where local exposure should run in the post-processing chain.

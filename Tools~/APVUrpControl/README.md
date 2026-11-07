# Independent URP diffuse GI control

Use an independent **Unity 7000.0.0a16 URP Blank** project, with official built-in Core/URP **17.7.0**, rather than changing the VividRP project's pipeline. The verified project is `E:/APV_URP_Control_7000a16`.

Copy `APVControlHarness.cs` into its `Assets/Editor` and `APVControlSample.compute` into `Assets/APVControl` (create the folder first). `APVControlHarness.Create()` builds seven native MeshRenderer boxes, a black environment, one Mixed point light, Baked Indirect, and an SH L2 Baking Set with 0.5 m spacing. It refuses to overwrite an existing fixture.

The blank template disables URP exposure globally and includes SSAO. The harness enables exposure and disables renderer features, so only APV diffuse GI is compared. Use fixed exposure and ACES for readability; **light intensity, EV100 and final pixel values are not required to match VividRP**. Keep APV weight at 1 while diagnosing data and sampling, and compare red/green bounce, occlusion and spatial transitions on white surfaces. Do not use AO or local exposure to compensate for missing bounce.

For a connected Editor, use Unity CLI `script eval` with these calls:

```csharp
APVControlHarness.Create(); // once; afterwards reopen Assets/APVControl/Control.unity
APVControlHarness.Bake();   // asynchronous; poll Status() until Completed
APVControlHarness.SetExposure(5); // choose independently for this pipeline
APVControlHarness.SetDisplay(false, 1); // real-time point light off, APV on
APVControlHarness.Capture("C:/absolute/path/urp-diffuse.png");
APVControlHarness.SampleProbes(); // asynchronous; Status() reports four floor samples
// Restore the enabled Mixed light before saving the scene or rebaking:
APVControlHarness.SetDisplay(true, 1);
```

`Capture` explicitly prepares the official SRP in a batch Editor and uses `RenderPipeline.StandardRequest`. A fallback based only on `Camera.Render()` can return a misleading image without initializing SRP/APV. VividRP currently uses its real Game View render target for captures (`capture game_view --source screen`), not this URP-specific harness.

`Watch-UnityProject.ps1` creates or runs only the explicitly named diagnostic project, records memory/handle counts and stops that project if the configured limits are exceeded. The verified 16 GiB / 20000-handle limits were chosen against the original Editor's measured approximately 10.2 GiB / 5100-handle startup; remeasure before using different projects or machines. It never prints process command lines.

After a native bake, compare the **postprocessed** `Temp/PostProcessingOutput/APVBake/probeRequest0/irradiance.shl2` using `MeasureDiffuseGI.py`. The raw `radianceIndirect*.shl2` layout is different. Colored L0 is a diagnostic of chromatic transport, not a lighting quality score or a requirement for arbitrary scenes.

`InspectBakeInput.csx` inspects the serialized input produced by a **Compute** bake. Check the input files' modification times against that bake: Progressive baking need not rewrite them, so older files can be stale. The script only reads and does not patch inputs.

See `Documentation~/MeshletAPVBaking.md` for the observed Meta-pass matrix bug, fix and validation boundaries.

# Meshlet APV bake validation

Environment: Unity 7000.0.0a16, local CoreRP 17.7.0, D3D12.

## Workflow

1. Save the current scenes. Use **VividRP > Samples > Open Meshlet APV Validation Scene** to open the saved fixture and select its separate pipeline asset. **Create Meshlet APV Validation Scene** creates it only when it does not already exist.
2. Use **VividRP > Lighting > Bake Meshlet APV**. All scenes in the active Baking Set must be loaded. The command uses the project's selected native baking backend.
3. Wait for the completion message. Temporary MeshRenderers are removed automatically. Save the scene and assets to retain native APV references and data.
4. Reopen the scene to verify that the bake loads without any native MeshRenderer geometry.

The fixture is stored under `Samples/MeshletAPV`: seven static MeshletRenderer boxes, original source mesh/materials, a black environment, one Mixed point light (1500 cd), Baked Indirect, fixed EV100 7, ACES, SH L2, 0.5 m minimum probe spacing, and native Virtual Offset defaults. It is an integration fixture rather than a finished lighting preset.

## Bake geometry ownership

`MeshletAPVBaking` exports active, enabled, static `ContributeGI` MeshletRenderers in the active Baking Set. It uses their original mesh, materials and exact transform hierarchy, independently of meshlet culling and runtime LOD. Skinned sources and geometry already represented by an enabled native MeshRenderer are skipped. Missing source data or Meta passes fail explicitly and roll back temporary geometry.

The proxies exist before native placement, Virtual Offset and input extraction. A dedicated ownership marker permits cleanup on completion, the supplied Cancel command, scene closing, script reload and recovery after interruption. They are not runtime receivers: after cleanup the scene renders with MeshletRenderer and samples the native APV data by world position. No custom APV binary format is introduced.

## Hybrid GBuffer ordering

The five GBufferPass outputs feed the matching `m_GBuffer0_In` through `m_GBuffer4_In` override inputs of VisibilityBufferGBufferResolvePass. Downstream consumers read the Resolve outputs. The compiled graph has:

`PreDepth -> VisibilityBuffer -> GBufferPass -> VisibilityBufferGBufferResolvePass -> MaterialClassification / DeferredLighting`

Native pre-depth and meshlet visibility share the depth chain. Resolve discards invalid visibility pixels, preserving native GBuffer pixels. RenderGraph clears the color textures on their first use each frame; GBufferPass no longer clears all MRTs unconditionally in Record. Serialized output port names are preserved.

## Verification boundaries

The fixture generated 8 cells and 6207 unique probe positions. Completion and the supplied Cancel command both removed all 7 proxies. Native input extraction saw 7 instances. An initial bake using native MeshRenderers on the original objects produced exactly the same `irradiance.shl2` SHA-256 as the initial proxy bake, before the Meta-pass fix described below:

`846EC514EF0E2FE166A8893C4B9CAA705FFC38D139399C204CAE0C0DF67FBAB8`

The scene was completely unloaded (`DataHasBeenLoaded == false`) and reopened. Afterwards APV data loaded from disk, `_EnableProbeVolumes == 1`, 7 MeshletRenderers were present and no MeshRenderer remained. At the same camera/exposure, APV weight 1 versus 0 changed 487767 of 691200 pixels by more than two 8-bit RGB levels. Weight 0 uses the pipeline's ambient fallback; it is not a zero-light reference. A capture with the real-time point light disabled separately confirms the baked contribution.

A temporary native foreground cube was moved from left to right. It occluded meshlet geometry and the old location recovered in the inspected frames. This is a depth/merge/clear check with anti-aliasing disabled, not acceptance of native material shading or a temporal-AA ghosting stress test. The temporary cube and material were removed before saving.

## Independent URP diffuse-GI comparison (2026-11-07)

The independent project `E:/APV_URP_Control_7000a16` uses the URP Blank template, Unity 7000.0.0a16, official built-in Core/URP 17.7.0, D3D12 and native MeshRenderers. It has the same seven-box geometry, black environment, Baked Indirect mixed-light mode and 6207-probe placement. Relevant local CoreRP path-tracing, unified-ray-tracing and APV baking sources match the official package after normalizing line endings.

The comparison targets diffuse bounce, color bleeding, occlusion and spatial transitions on white surfaces. It does not require matching lamp, exposure or postprocessing numbers. Real-time point lighting is disabled for the captures, APV weight is 1, and AO, local exposure, Bloom and SSR are absent/disabled. The URP template's SSAO was explicitly disabled, and its global exposure switch explicitly enabled. The final readability settings are URP EV100 5 and VividRP EV100 7, both with ACES. Differences in the tone curves and baked light falloff remain; the images are not pixel-equivalence tests.

### Cause and fix

The original achromatic bake was a **Meta-pass rasterization bug**. `ShaderVariablesGlobal.hlsl` remapped `unity_MatrixVP` to `_VividMatrixVP` for every pass. Unity's native lightmapper renders albedo/emission into UV space using its own matrix, outside VividRP's camera render loop. The Meta vertex shader therefore projected with the runtime camera instead of the baker's UV-space camera.

The true serialized Compute-bake albedo images had only about 0.97–1.20% mean alpha coverage, despite containing some correctly colored pixels. A successful manual material extraction or the presence of colored pixels did not prove correct UV-space coverage. After keeping the native `unity_MatrixVP` binding in Meta passes, coverage is about 88–90%, and both baking backends recover color bounce. The condition covers StandardLit/Layered/experimental Meta, Unlit Meta and Terrain Meta; the complete visual bake fixture exercises StandardLit.

| Bake | Probes | Chromatic L0 probes (`max(R,G,B)-min(R,G,B) > 0.01`) |
| --- | ---: | ---: |
| VividRP before fix, Progressive / Compute | 6207 | 0 |
| Official URP, original inverse-square extraction | 6207 | 2435 |
| URP with default Legacy light extraction and VividRP feature declarations | 6207 | 2442 |
| VividRP after Meta fix, Progressive | 6207 | 2442 |
| VividRP after Meta fix, Compute | 6207 | 2449 |

These counts diagnose chromatic transport in this deliberately colored fixture; they are not a quality score. The Legacy falloff and supported-feature declaration experiments did not remove URP color bleeding, so neither explains the missing VividRP color. VividRP's current native light extraction still uses Legacy falloff; this is a remaining lighting-contract difference, not a reason to rescale APV SH or force URP's exposure numbers onto VividRP.

The corrected Meshlet image shows red bounce on the left of the floor/white blocks and green bounce on the right, with the same broad spatial behavior as the URP control. Keep APV intensity at 1 and tune the pipeline's exposure for the desired presentation. Re-bake existing data affected by the Meta bug; exposure or runtime GI multiplication cannot repair that data.

After the corrected Compute bake, the fixture was saved, fully unloaded and reopened: data loaded from disk, `_EnableProbeVolumes == 1`, 7 MeshletRenderers, 0 MeshRenderers and 0 temporary proxies. The reloaded Game View retained color bleeding. The independent URP Editor was saved and closed; the original Sponza Noon scenes and shared pipeline were restored.

The reproducible control harness, watchdog and SH inspection script are under `Tools~/APVUrpControl`. Diagnostic captures and pre-fix backups are under `C:/Users/11252/.codex/visualizations/2026/11/06/01a1b0d2-8c18-7171-823c-fd53dbc0b370/urp-control`.

Live Editor baking, native input extraction, GPU sampling and scene reload are checked separately from compilation. The added Editor tests cover export ownership/filtering/rollback and hybrid graph wiring/idempotence. Unity Test Framework is not run while the interactive Editor is open; run the focused APV, GBufferPass and RenderGraph migration tests with the Editor closed.

Shader reimport also reported D3D11 diagnostics in DeferredLit (OpenPBR struct forward declarations) and HZBGenerate (wave intrinsics), separate from the documented 8-UAV exception. These were recorded, not repaired as part of this D3D12 GI task. The actual D3D12 DeferredLit kernel reported supported and rendered the corrected reloaded fixture. D3D11 compatibility is not validated.

This fixture does not validate Player builds, large-world streaming, lighting scenarios, dynamic occluders, or production lighting quality. The noon Sponza lighting is not baked by this workflow.

# Unity 7000.0.0a9 compatibility

Back to index: [Documentation~ Index](README.md)

Updated on 2026-10-14 against Unity `7000.0.0a9` revision `69e891f60f9a`.

## Package baseline

- VividRP now declares `unity: 7000.0` and `unityRelease: 0a9`.
- The containing project already selected this Editor version.
- Embedded `Packages/CoreRP` was updated from the installed a7 source to the
  installed a9 `BuiltInPackages/com.unity.render-pipelines.core` source: 47
  modified files and four added files, including upstream-provided metadata.
- CoreRP and Shader Graph retain version `17.7.0`; the CoreRP version string
  alone does not identify the a9 source revision.
- Dependency declarations were checked against a9's built-in packages and the
  Unity registry on 2026-10-14: Collections `6.7.0`, Test Framework `1.9.0`,
  Timeline `6.7.0`, and Profile Analyzer `1.4.2`. Other declared versions were
  retained. The containing project's Profile Analyzer pin was also updated to
  `1.4.2`, and Unity regenerated its package lock file.
- All files outside that upstream delta were preserved byte-for-byte, including
  VividRP's CoreRP customizations and existing local changes. No merge conflicts
  occurred. Existing metadata and GUIDs were preserved.

The upstream delta includes upscaler API/editor changes, RenderGraph fixes,
probe-volume and XR changes, and unified ray-tracing vertex attribute decoding.
CoreRP is a separate repository and its changes must be retained alongside the
VividRP package update.

## Verification

- Recompilation in the running a9 Editor completed with no C# compiler errors.
- `Tools~/Validate-SimpleSlab.ps1` passed all 43 DXC entrypoints/variants using
  Vulkan SDK 1.4.350.0 DXC and the updated CoreRP includes (HLSL 2018,
  `SHADER_API_D3D11`). This is standalone compilation, not GPU execution.
- Read-only reflection in a9 verified all 18 structs and 144 field offsets in
  `VividGPUDrivenStructs.cs.hlsl`, including strides, sequential layout, fixed
  arrays, and padding. This did not invoke Unity Test Framework.
- VividRP's whitespace check passed. CoreRP's check only reported pre-existing
  whitespace in `Runtime/Debugging/White1px.png.meta`, which was preserved.

The import log also reported DX12 `COLOR` fragment-output semantic errors in
the project's old `Assets/TextMesh Pro/Shaders/TMP_Bitmap-Mobile.shader`,
`TMP_SDF-Surface.shader`, and `TMP_SDF-Surface-Mobile.shader`. These assets are
outside VividRP/CoreRP and were not modified. Existing 14-UAV versus 8-limit
diagnostics were also observed. These diagnostics must not be confused with
successful runtime rendering validation.

Unity Test Framework tests were not run because an interactive Editor session
was active. Run the GPU-driven buffer layout tests manually, then check the
target scene's geometry, culling counters, indirect draw arguments, and visual
output. Visual correctness and performance have not been verified by this update.

Local backup, exact upstream file manifest, compilation status, layout results,
and validation logs are under `Temp~/Unity7000a9Upgrade/` (untracked).

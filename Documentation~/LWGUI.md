# LWGUI Integration

VividRP vendors [LWGUI](https://github.com/JasonMa0012/LWGUI) under `ThirdParty/LWGUI` so the project can modify the shader GUI source in place.

## Bundled upstream revision

- Branch: `1.x`
- Commit: `0acba799ec7d582ebb667ea6db0aefd32fd8c2ef`
- Package version: `1.41.0`

## Included content

The embedded copy keeps the source required for normal use inside VividRP:

- `ThirdParty/LWGUI/Editor`
- `ThirdParty/LWGUI/Runtime`
- `ThirdParty/LWGUI/UnityEditorExtension`
- `ThirdParty/LWGUI/LICENSE`
- `ThirdParty/LWGUI/README.md`
- `ThirdParty/LWGUI/README_CN.md`

Tests, package metadata, documentation assets, and Unity `.meta` files are intentionally not copied.

## VividRP-specific adaptation

Upstream LWGUI loads several icons through fixed asset GUIDs. That does not survive vendoring without the original `.meta` files, so VividRP replaces those lookups with relative-path loading via `ThirdParty/LWGUI/Editor/Helper/LwguiAssetPathUtility.cs`.

Starting with the bundled 1.41.0 revision, LWGUI no longer embeds `fxc.exe`; its shader performance monitor locates FXC from an installed Windows SDK.

VividRP also adds a DXC backend under `ThirdParty/LWGUI/Editor/PerformanceMonitor/ShaderCompiler/Dxc`. It preprocesses the selected Unity D3D12 shader variant to standalone HLSL, compiles Shader Model 6 DXIL, and reports IR-level ALU cost, texture operations, instruction count, and SSA temporary values. The SSA count is only a pressure indicator; physical register allocation remains driver and GPU dependent.

When updating LWGUI from upstream, review these files before replacing the embedded copy:

- `ThirdParty/LWGUI/Editor/Helper/LwguiAssetPathUtility.cs`
- `ThirdParty/LWGUI/Editor/Helper/Helper.cs`
- `ThirdParty/LWGUI/Editor/Helper/RevertableHelper.cs`
- `ThirdParty/LWGUI/Editor/Helper/ToolbarHelper.cs`
- `ThirdParty/LWGUI/Editor/Helper/IOHelper.cs`
- `ThirdParty/LWGUI/Editor/PerformanceMonitor/ShaderCompiler/Dxc/ShaderCompilerDxc.cs`

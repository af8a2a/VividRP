# LWGUI Integration

VividRP vendors [LWGUI](https://github.com/JasonMa0012/LWGUI) under `ThirdParty/LWGUI` so the project can modify the shader GUI source in place.

## Bundled upstream revision

- Branch: `1.x`
- Commit: `881ca3b5e3b72a73fdaefe9257818c8766a1e374`
- Package version: `1.35.0`

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

Upstream LWGUI loads several icons and `fxc.exe` through fixed asset GUIDs. That does not survive vendoring without the original `.meta` files, so VividRP replaces those lookups with relative-path loading via `ThirdParty/LWGUI/Editor/Helper/LwguiAssetPathUtility.cs`.

When updating LWGUI from upstream, review these files before replacing the embedded copy:

- `ThirdParty/LWGUI/Editor/Helper/LwguiAssetPathUtility.cs`
- `ThirdParty/LWGUI/Editor/Helper/Helper.cs`
- `ThirdParty/LWGUI/Editor/Helper/RevertableHelper.cs`
- `ThirdParty/LWGUI/Editor/Helper/ToolbarHelper.cs`
- `ThirdParty/LWGUI/Editor/PerformanceMonitor/ShaderCompiler/Fxc/ShaderCompilerDefaultFxc.cs`

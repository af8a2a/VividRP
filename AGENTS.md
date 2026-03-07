# Repository Guidelines

## Project Structure & Module Organization
- `Runtime/RenderPipeline/` contains the SRP entry points and settings objects (`VividRenderPipeline`, `VividRenderPipelineAsset`, `VividRenderPipelineGlobalSettings`).
- `Runtime/RenderGraph/` contains the reflection-driven pass model, pass recorder, frame context types, preview registry, and resource descriptors used at runtime.
- `Runtime/RenderPass/` contains concrete passes, currently grouped under `Core/` and `Example/`; new passes typically derive from `RasterPass`, `UnsafePass`, or `ComputePass`.
- `Runtime/Utility/PipelineResource/` plus `Runtime/Resources/PipelineResources.asset` implement package resource lookup based on `[PipelineResource]` and `[ResourcePath]` attributes.
- `Editor/RenderGraph/` contains the GraphToolkit-based RenderGraph editor, validators, importers, node data types, drawers, and pass-node registry generation.
- `Editor/RenderGraph/GeneratedRenderPassNodes.g.cs` is generated code. Update the generator, registry builder, or runtime pass types instead of editing this file by hand.
- `Editor/PipelineResource/` and `Editor/RenderPipeline/` contain editor automation such as resource syncing and global settings hooks.
- `Shaders/` is a top-level package folder with package shaders and the `VividRP.Shaders` assembly; shader assets are not stored under `Runtime/Shaders/`.
- `Documentation/` contains the current package notes for RenderGraph editor usage, resource descriptors, and acceleration-structure support; keep higher-level workflow docs there.
- `Tests/Editor/` currently contains all committed tests through the `VividRP.Editor.Tests` assembly. Add `Tests/Runtime/` only when runtime-specific coverage is needed.
- Do not manually create or edit `*.meta` files; let Unity generate and maintain them automatically.
- Unity `.meta` files, generated assets, and package-relative paths must stay in sync when moving or renaming files. If the package path or package name changes, update both `Editor/PipelineResource/PipelineResourceUpdater.cs` and `Editor/RenderGraph/RenderPassNodeRegistryGenerator.cs`.

## Build, Test, and Development Commands
- Open the package through the Unity project root `E:\VividRP_Reborn` using Unity `6000.5.0a7` or a compatible `6000.5` build.
- Run the current EditMode suite with Unity Test Framework:
  `Unity.exe -batchmode -projectPath "E:\VividRP_Reborn" -runTests -testPlatform EditMode -testResults Logs/editmode-results.xml -quit -logFile Logs/editmode.log`
- There are no committed PlayMode tests yet. Add the relevant test assembly before documenting or relying on a PlayMode batch command.
- Quick pass/resource search: `rg "IRenderPass|RenderGraphResource|PipelineResource|ResourcePath" Runtime Editor Tests`
- Quick editor/codegen search: `rg "GeneratedRenderPassNodes|BuildRegistrations|RegisteredPassTypeName" Editor Runtime Tests`
- Quick package path audit: `rg "Packages/VividRP|Packages/com.af8a2a.vividrp|com.af8a2a.vividrp" Runtime Editor Tests package.json`

## Coding Style & Naming Conventions
- Use 4-space indentation, braces on new lines, and small focused methods.
- Match namespaces to area, for example `VividRP.Runtime`, `VividRP.Runtime.RenderPass.Core`, `VividRP.Editor.RenderGraph`, and `VividRP.Editor.Tests`.
- Preserve reflection-driven contracts: runtime pass resource fields are discovered via `[RenderGraphResource]`, and editor port generation plus preview lookup depend on those field names, access flags, and field types.
- Keep GraphToolkit data model naming consistent: node model classes end with `NodeData`, generated files use the `.g.cs` suffix, and tests end with `Tests.cs`.
- Serialized fields and long-lived backing fields often use the Unity-style `m_` prefix, but some files already follow local alternatives. Match the style of the file you are editing instead of mass-renaming existing members.
- Use `Undo.RecordObject(...)` before mutating user-facing serialized assets in editor tooling. When following the existing sync/generation patterns, also persist changes with `EditorUtility.SetDirty(...)` and `AssetDatabase.SaveAssetIfDirty(...)`.
- Prefer minimal, assembly-appropriate visibility (`internal`, `internal sealed`, etc.) for editor helpers and node data types, matching the current codebase.
- Do not hand-edit generated or synchronized artifacts such as `Editor/RenderGraph/GeneratedRenderPassNodes.g.cs` or `Runtime/Resources/PipelineResources.asset` unless you are intentionally fixing their generator/sync pipeline.

## Testing Guidelines
- Use Unity Test Framework with NUnit under `Tests/Editor/` for current package coverage.
- Follow the existing test naming pattern: `MethodName_ExpectedBehavior_WhenCondition`.
- Add focused EditMode tests with each fix or feature, especially around pass-port generation, descriptor drawers, preview metadata, registry generation, and reflection-based pass/resource behavior.
- If a change introduces runtime-only behavior that cannot be validated meaningfully in current EditMode tests, add the appropriate `Tests/Runtime/` or PlayMode coverage in the same change.
- Prefer self-contained tests that use dummy pass types or temporary ScriptableObjects over manual project setup.

## Commit & Pull Request Guidelines
- Recent history is mixed, so prefer short imperative commit titles. Use scoped Conventional Commit prefixes such as `feat:`, `fix:`, `test:`, or `refactor:` when practical.
- When source changes affect generated or synchronized outputs, include those artifacts in the same review context: `Editor/RenderGraph/GeneratedRenderPassNodes.g.cs`, `Runtime/Resources/PipelineResources.asset`, and related `.meta` files.
- PRs should summarize purpose, key changes, package-path assumptions, and EditMode test evidence.
- Include screenshots or GIFs for RenderGraph editor UI changes and note shader-visible behavior changes when touching passes, shaders, or pipeline resources.

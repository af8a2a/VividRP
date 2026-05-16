# Repository Guidelines

## Project Structure & Module Organization
- `Runtime/ComponentData/` contains camera/light companion data (`VividAdditionalCameraData`, `VividAdditionalLightData`) and should stay aligned with editor inspectors in `Editor/ComponentEditor/`.
- `Runtime/Extension/CoreRP/` contains Core RP extension glue plus the `VividRP.CoreRP.Runtime.asmref`; keep assembly references intact when moving these files.
- `Runtime/RenderPipeline/` contains the SRP entry points and settings objects (`VividRenderPipeline`, `VividRenderPipelineAsset`, `VividRenderPipelineGlobalSettings`).
- `Runtime/RenderGraph/` contains the reflection-driven pass model, pass recorder, frame context types, preview/history registries, and resource descriptors used at runtime.
- `Runtime/RenderPass/` contains concrete passes, currently grouped under `Core/` and `Example/`; new passes typically derive from `RasterPass`, `UnsafePass`, or `ComputePass`.
- `Runtime/Utility/PipelineResource/` plus `Runtime/Resources/PipelineResources.asset` implement package resource lookup based on `[PipelineResource]` and `[ResourcePath]` attributes.
- `Editor/RenderGraph/` contains the GraphToolkit-based RenderGraph editor, validators, importers, pass-compilation utilities, node data types, drawers, navigation helpers, and pass-node registry generation.
- `Editor/RenderGraph/GeneratedRenderPassNodes.g.cs` is generated code. Update the generator, registry builder, or runtime pass types instead of editing this file by hand.
- RenderGraph authoring assets use the `.vrdg` extension and are imported into `RenderGraphData` assets by `Editor/RenderGraph/RenderGraphImporter.cs`; change importer/compiler behavior carefully and keep import-time tests current.
- `Editor/PipelineResource/` and `Editor/RenderPipeline/` contain editor automation such as resource syncing and global settings hooks.
- `Editor/ComponentEditor/`, `Editor/Material/`, `Editor/Shader/`, and `Editor/VolumeEditor/` contain custom inspectors, shader GUIs, and editor-only shader assets; keep runtime/editor boundaries clean.
- `Shaders/` is a top-level package folder with package shaders and the `VividRP.Shaders` assembly; shader assets are not stored under `Runtime/Shaders/`.
- `Documentation/` contains the current package notes for RenderGraph editor usage, resource descriptors, and acceleration-structure support; keep higher-level workflow docs there.
- `Tests/Editor/` contains the current committed suite through the `VividRP.Editor.Tests` assembly, including pass, node, drawer, importer, history, component, and editor coverage. Add `Tests/Runtime/` only when runtime-specific coverage is needed.
- Do not manually create or edit `*.meta` files; let Unity generate and maintain them automatically.
- Unity `.meta` files, generated assets, and package-relative paths must stay in sync when moving or renaming files. If the package path or package name changes, update both `Editor/PipelineResource/PipelineResourceUpdater.cs` and `Editor/RenderGraph/RenderPassNodeRegistryGenerator.cs`.
- The repository currently uses both `Packages/com.af8a2a.vividrp/...` and `Packages/VividRP/...` path constants; do not “fix” only one side during refactors—audit all package-relative paths together.

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
- Preserve serialized field names in authoring/runtime data models unless you also add an explicit migration path. `RenderGraphData`, `RenderGraphPassDefinition`, and `PipelineResourcesContainer` are serialized assets that survive importer/editor updates.
- Put `[RenderGraphResource]` on fields, not properties. The collector reflects instance fields across the inheritance chain and ignores null resource values.
- Initialize pass resource descriptors before `Initialize()` runs; a null `RenderGraphTexture`, `RenderGraphBuffer`, or `RenderGraphRenderList` field is skipped and will not get ports or runtime setup.
- For `[PipelineResource]` classes, expose resource bindings as `public` instance fields with `[ResourcePath]`; the updater does not populate private fields.
- Keep GraphToolkit data model naming consistent: node model classes end with `NodeData`, generated files use the `.g.cs` suffix, and tests end with `Tests.cs`.
- Preserve existing resource field names when changing pass APIs. Read/write ports, preview keys, compiled graph bindings, and some tests rely on those exact names.
- For read/write resources, keep the generated port naming convention in mind: input uses `<FieldName>_In`, output uses `<FieldName>_Out`.
- Preview nodes are texture-focused. If you add new preview behavior for buffers, render lists, or history resources, update both validator/drawer logic and tests in the same change.
- Serialized fields and long-lived backing fields often use the Unity-style `m_` prefix, but some files already follow local alternatives. Match the style of the file you are editing instead of mass-renaming existing members.
- Use `Undo.RecordObject(...)` before mutating user-facing serialized assets in editor tooling. When following the existing sync/generation patterns, also persist changes with `EditorUtility.SetDirty(...)` and `AssetDatabase.SaveAssetIfDirty(...)`.
- Prefer minimal, assembly-appropriate visibility (`internal`, `internal sealed`, etc.) for editor helpers and node data types, matching the current codebase.
- Do not hand-edit generated or synchronized artifacts such as `Editor/RenderGraph/GeneratedRenderPassNodes.g.cs` or `Runtime/Resources/PipelineResources.asset` unless you are intentionally fixing their generator/sync pipeline.

## RenderGraph Rules
- `.vrdg` files are the source of truth for graph authoring. Do not manually maintain derived `RenderGraphData` contents; let the importer/compiler regenerate them.
- New passes should implement `Create()`, `Prepare(...)`, `Record(...)`, and `Dispose()` coherently; `Prepare(...)` is where per-frame descriptor sizing/imports should happen.
- Use the appropriate resource wrapper type for graph integration: `RenderGraphTexture`, `RenderGraphBuffer`, `RenderGraphRenderList`, and `RenderGraphAccelerationStructureDesc` where supported by the runtime/editor flow.
- History resource nodes expose `PrevOut` and `CurrOut`; if you change history binding semantics, update importer, compilation ordering, runtime history handling, and tests together.
- Pass ordering is compiler-driven. When changing binding semantics or connection rules, verify `RenderGraphPassCompilationUtility` still derives the right dependencies and cycle fallback behavior.
- If a pass changes its exposed resource layout dynamically, keep `IDynamicPassResourceLayout` behavior and the related editor/runtime tests in sync.
- If a pass supports async compute or global state modification, express that through the existing marker interfaces instead of ad hoc flags.
- Reserve `IRenderGraphRecordingPass` for the antialiasing pass only. New runtime passes should use the standard `ComputePass`, `UnsafePass`, or `RasterPass` recorder path instead of bypassing the recorder for post-processing, history, resource, or culling behavior.
- For passes whose work must run even without same-frame resource consumers, implement `IRenderGraphSideEffectPass` so `RenderGraphPassCullingUtility` treats the pass as live. Use it for history updates, readbacks, imported-resource updates, or persistent side effects, and pair behavior changes with focused culling utility tests.
- When adding a runtime pass type, verify the generated node registry, navigation helpers, compilation utility, and pass-node tests still reflect the new pass correctly.

## Resource Workflow
- Use `PipelineResourceUpdater` or the custom inspector on `PipelineResourcesContainer` to recollect engine resources; do not hand-maintain `Runtime/Resources/PipelineResources.asset` entry-by-entry.
- Resource container entries are normalized and sorted during recollection. If you change resource-key generation, preserve deterministic ordering and update related tests.
- Keep `ResourceEntry` serialization compatibility in mind when renaming fields; `ResourceObject` already carries a migration attribute from the older `Asset` name.

## Testing Guidelines
- Use Unity Test Framework with NUnit under `Tests/Editor/` for current package coverage.
- Follow the existing test naming pattern: `MethodName_ExpectedBehavior_WhenCondition`.
- Add focused EditMode tests with each fix or feature, especially around pass-port generation, descriptor drawers, preview metadata, registry generation, reflection-based pass/resource behavior, render-list/history resources, and custom editor utilities.
- If a change introduces runtime-only behavior that cannot be validated meaningfully in current EditMode tests, add the appropriate `Tests/Runtime/` or PlayMode coverage in the same change.
- Prefer self-contained tests that use dummy pass types or temporary ScriptableObjects over manual project setup.
- Do not add or keep tests that only inspect implementation text, such as `File.ReadAllText(...)` with `Assert.That(source, Does.Contain(...))`, when the behavior can be validated through runtime/editor APIs.
- Do not add or keep tests that only validate `[ResourcePath]` declarations or expected path strings, including `VividRPCoreResources_Declares*` reflection tests. Prefer resource recollection, pass initialization, or runtime binding coverage when the resource path matters.
- For GPU-driven culling/debug counters, keep CPU mirror validation and GPU shader counting semantics in sync. If CPU/GPU counts diverge, fix the shared decision rules or counter increments rather than suppressing the mismatch.

## Commit & Pull Request Guidelines
- Recent history is mixed, so prefer short imperative commit titles. Use scoped Conventional Commit prefixes such as `feat:`, `fix:`, `test:`, or `refactor:` when practical.
- When source changes affect generated or synchronized outputs, include those artifacts in the same review context: `Editor/RenderGraph/GeneratedRenderPassNodes.g.cs`, `Runtime/Resources/PipelineResources.asset`, and related `.meta` files.
- PRs should summarize purpose, key changes, package-path assumptions, and EditMode test evidence.
- Include screenshots or GIFs for RenderGraph editor UI changes and note shader-visible behavior changes when touching passes, shaders, or pipeline resources.

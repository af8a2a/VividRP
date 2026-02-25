# Repository Guidelines

## Project Structure & Module Organization
- `Runtime/` contains SRP runtime code (`VividRenderPipeline*`) and RenderGraph execution/data model (`Runtime/RenderGraph/**`).
- `Editor/` contains editor-only GraphView tooling (`RenderGraphEditorWindow`, node views, search window, USS styles).
- `Runtime/Shaders/` stores package shaders used by render passes.
- `Documentation/` is for package docs; keep high-level usage notes here.
- `*.asmdef` files split assemblies into `VividRP.Runtime` and `VividRP.Editor`; preserve this separation.
- Unity `.meta` files are part of source control and must stay in sync with moved/renamed assets.

## Build, Test, and Development Commands
- Open and iterate in Unity (required): launch Unity 6000.5+ with project root `E:\VividRP_Reborn`.
- Run EditMode tests (CLI example):
  `Unity.exe -batchmode -projectPath "E:\VividRP_Reborn" -runTests -testPlatform EditMode -quit -logFile Logs/editmode.log`
- Run PlayMode tests (CLI example):
  `Unity.exe -batchmode -projectPath "E:\VividRP_Reborn" -runTests -testPlatform PlayMode -quit -logFile Logs/playmode.log`
- Quick code search: `rg "RenderGraph|NodeData" Runtime Editor`

## Coding Style & Naming Conventions
- C# style: 4-space indentation, braces on new lines, clear single-responsibility methods.
- Namespaces: `VividRP.Runtime` for runtime, `VividRP.Editor` for editor-only code.
- Private serialized/internal fields use Unity-style `m_` prefix (for example `m_Asset`, `m_SearchWindow`).
- Keep naming consistent: data classes end with `NodeData`, editor views end with `NodeView`.
- Use `Undo.RecordObject(...)` before mutating serialized assets in editor code.

## Testing Guidelines
- Current package has no committed test suite; add tests with each feature/fix.
- Place tests in `Tests/Editor` and `Tests/Runtime` using Unity Test Framework (`com.unity.test-framework`).
- Test files should end with `Tests.cs`; name tests by behavior (for example `Validate_ReturnsCycleError_WhenLoopExists`).
- Prioritize coverage for graph validation, edge compatibility, and pass execution ordering.

## Commit & Pull Request Guidelines
- Follow Conventional Commits seen in history: `feat:`, `fix:`, `refactor:`, etc.
- Keep commit titles imperative and scoped (for example `feat: add texture node port validation`).
- PRs should include: purpose, key changes, test evidence (Test Runner output/logs), and linked issue/task.
- Include screenshots/GIFs for GraphView or shader-visible changes.

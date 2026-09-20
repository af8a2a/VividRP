# VividRP Agent Guide

## 1. Project Context

VividRP is a Unity Scriptable Render Pipeline package. Work from this package directory; the containing Unity project supplies the Editor environment. Read `package.json` for package dependencies and the containing project's `ProjectSettings/ProjectVersion.txt` for its actual Editor version before choosing tools or APIs.

- `Runtime/`: pipeline, render graph, subsystems, and runtime resources.
- `Editor/`: graph tooling, resource synchronization, and Editor integrations.
- `Shaders/`: shader code and includes; check the matching C# bindings when changing GPU interfaces.
- `Tests/Editor/`: Unity Editor tests.
- `SourceGenerators~/`: independent Roslyn generator and .NET tests; see its `README.md` for build/deployment details.
- `Tools~/` and `Documentation~/`: consult the relevant tool or feature documentation as needed.

## 2. Execution and Decisions

Complete authorized work through implementation and appropriate verification. For routine, reversible choices, follow nearby code and proceed; state assumptions only when they affect behavior or scope. Ask a focused question when missing information materially changes correctness, compatibility, data ownership, or an irreversible action. Continue independent work while waiting.

Start with the relevant instructions, working-tree status, implementation, callers, and existing tests. Search narrowly with `rg` and expand when the evidence requires it. For substantial work, give a short plan with observable completion criteria; skip formal planning for simple edits.

Follow the user's current request and accepted decisions. Apply skills and repository guidance within that scope; do not invent additional approval gates from general advice. If an applicable instruction blocks progress, identify its source and exact constraint, explain the unresolved step, and finish unaffected work.

Use subagents only when requested or explicitly authorized by applicable instructions. When using them, assign independent, bounded tasks with clear file ownership and validation expectations; review their results before integration.

## 3. Change Boundaries

Implement the smallest complete solution consistent with the existing architecture. Preserve local style, public contracts, serialized data, and CPU/GPU layout agreement. Add abstractions, dependencies, or fallback behavior only when the task needs them.

Keep edits tied to the request. Preserve pre-existing and concurrent changes; do not reset, overwrite, or clean unrelated work. Remove only dead code introduced by your own changes. If overlapping edits prevent a reliable merge, resolve the overlap with the user rather than guessing intent.

Fix the cause supported by evidence. Inspect ownership, lifetime, invalidation, and call sites before changing resource behavior. Do not hide failures by weakening assertions, disabling checks, or catching errors without handling them.

## 4. Rendering Performance and Correctness

Stable rendering paths must allocate zero managed memory after warm-up. This includes frame preparation, subsystem updates, pass preparation/recording, scene building, culling, history, virtual-texture scheduling, and helpers called per camera or instance.

- Reuse owned scratch collections, arrays, and descriptors. Copy into existing storage without aliasing mutable shared state.
- Cache names and recurring delegates. Keep string formatting, diagnostic dumps, LINQ, boxed enumeration, and `params` arrays out of stable paths.
- Cache layouts and validation by their actual inputs; invalidate when those inputs change. Recreate GPU resources only when their effective descriptors change.
- Preserve required initialization, defensive copies, disposal, and synchronization. Allocation reduction must not break resource ownership or rendering correctness.

For hot-path changes, add or update a focused allocation regression check where feasible: warm up, prepare inputs/delegates/assertion messages outside measurement, then measure repeated stable calls with `GC.GetAllocatedBytesForCurrentThread()`. Use profiler evidence for claims about other threads or whole-frame behavior. Respect the Editor-session restrictions below and report any measurement that remains unverified.

## 5. Verification and Handoff

Choose checks that address the changed behavior. Prefer a focused compilation or regression test; add broader coverage only for affected dependencies, failures, or unresolved risks. Documentation-only changes need content and diff checks, not a Unity build. Stop repeating successful checks unless new changes justify another run.

For generator changes, the standalone test command is:

```powershell
dotnet test SourceGenerators~/VividRP.RenderPassNodeGenerator.Tests/VividRP.RenderPassNodeGenerator.Tests.csproj
```

When the task requires rebuilding and deploying the generator:

```powershell
dotnet build SourceGenerators~/VividRP.RenderPassNodeGenerator/VividRP.RenderPassNodeGenerator.csproj -c Release -t:DeployToUnity
```

These .NET tests are separate from Unity Test Framework tests. For other changes, use the applicable checks in Important Notes. Verify available tools, references, shader entry points, and defines before invoking them; do not invent a package-wide build command or treat an isolated compilation as proof of runtime rendering correctness.

Before handoff, inspect the final diff for scope, accidental generated changes, and whitespace errors. Distinguish checks that passed, checks that failed, and checks not run. If verification is blocked, give the specific reason and the smallest remaining manual check.

Respond in the user's language with concise progress updates and a final explanation of the result, relevant file links, validation, and material limitations. Preserve the original objective when the user adds a correction or asks a status question. Do not claim completion or performance improvements without supporting evidence.

## 6. Important Notes
- Validate C# and shader changes with focused, non-Unity-test checks whenever possible. Use C# Roslyn or .NET assembly compilation for C# code, MCP-based Unity console inspection, DXC shader compilation, or equivalent targeted checks to confirm the result of a code change.
- Run Unity Test Framework unit tests only when Unity Editor is not running. If an open Unity Editor prevents `-batchmode` tests from running, treat that as an active interactive user session: do not use computer-use, UI automation, or similar means to start Unity tests proactively; instead, state in the final task handoff that the user should run the relevant Unity tests manually.
- Unity `.meta` files are auto-generated; do not manually create or edit them
- Do not hand-edit generated or synchronized artifacts such as `Editor/SourceGenerators/VividRP.RenderPassNodeGenerator.dll` or `Runtime/Resources/PipelineResources.asset`; rebuild the former from `SourceGenerators~/VividRP.RenderPassNodeGenerator` and update the latter through its sync pipeline
- Unity `.meta` files, generated assets, and package-relative paths must stay in sync when moving or renaming files
- The repository currently uses both `Packages/com.af8a2a.vividrp/...` and `Packages/VividRP/...` path constants; do not “fix” only one side during refactors — audit all package-relative paths together
- Package path changes require an audit of `Editor/PipelineResource/PipelineResourceUpdater.cs` and all package-relative constants found by the package path search below
- Quick searches:
  - Pass/resource search: `rg "IRenderPass|RenderGraphResource|PipelineResource|ResourcePath" Runtime Editor Tests`
  - Editor/codegen search: `rg "GeneratedRenderPassNodeRegistry|RenderPassNodeSourceGenerator|GetRegisteredPassType" Editor Runtime Tests SourceGenerators~`
  - Package path audit: `rg "Packages/VividRP|Packages/com.af8a2a.vividrp|com.af8a2a.vividrp" Runtime Editor Tests package.json`



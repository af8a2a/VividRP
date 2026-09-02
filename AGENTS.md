# Repository Guidelines

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

## 5. Render-Loop Managed Allocation Rules

Treat render-loop code as zero-managed-allocation after warm-up. This includes pipeline/frame preparation, subsystem `OnUpdate`/`Update`, render-pass `Prepare`/`Record`, GPU-driven scene building and validation, culling, history preparation, and virtual-texture scheduling/upload collection. Constructors and validation helpers also count as hot paths when they can be reached once per renderer, material, camera, or instance.

The following patterns have caused recurring `GC.Alloc` regressions in this repository:

- Do not build strings in a stable hot path. Avoid interpolation, concatenation, `string.Format`, numeric `ToString`, and `StringBuilder.ToString` for resource names, debug dumps, validation text, or logging. Precompute bounded names in static tables, cache names with the owning resource, and construct diagnostic dumps lazily only when they are actually requested. On Unity/Mono, even `StringBuilder.Append(int)` may allocate through numeric formatting.
- Do not call `Array.Clone()` or descriptor `Clone()` every frame. Allocate pass-owned arrays and descriptors once, then restore or copy fields into the existing destination. Use `Array.Copy`/`CopyTo` for arrays and the relevant non-allocating descriptor `Copy` helper for render-graph descriptors. Preserve ownership: do not alias a mutable source descriptor or shared static array when downstream code can modify it.
- Do not create recurring delegates at call sites. Capturing lambdas allocate closures, and inline non-capturing lambdas or method-group conversions are not guaranteed allocation-free across all Unity compiler/runtime combinations. Cache recurring callbacks, comparers, allocators, and render functions in `static readonly` delegate fields.
- Do not pass concrete hot-path collections through `IEnumerable<T>` when their struct enumerator will be boxed. Prefer a concrete collection or concrete `Dictionary<TKey, TValue>.KeyCollection`/`ValueCollection` parameter, or use an indexed loop over `List<T>`/arrays. Avoid LINQ and iterator methods in render-loop code unless a measured implementation proves zero allocation.
- Do not use `params` overloads in a hot path; the compiler creates an array for the arguments. Use fixed-arity overloads or explicit pairwise operations, for example nested two-argument `Mathf.Max` calls instead of a multi-value `params` call.
- Reuse scratch storage. Keep arrays, lists, dictionaries, hash sets, and builders on the owning object, call `Clear()`, and grow capacity only when needed. Do not create a new temporary collection per frame or per camera.
- Cache immutable canonical layouts, compiled metadata, and validation results by their real inputs. Do not reconstruct layouts or eagerly regenerate debug representations while checking renderability for every instance. Invalidation must follow the data/version that can actually change.
- Resource recreation is allowed only when the resource's effective descriptor changes. Stable `Prepare` calls should update an existing descriptor and reuse handles and names; put unavoidable allocations behind the configuration-change/recreation branch.
- Watch for other hidden boxing: passing value types through `object` or non-generic interfaces, enum formatting/logging, and interface-based comparisons. A source line without an explicit `new` is not evidence that it is allocation-free.

When fixing or adding hot-path code:

1. Warm the path before measuring so initialization and static caches are excluded.
2. Add a focused regression test using `GC.GetAllocatedBytesForCurrentThread()`: warm up, call the stable path repeatedly, and assert zero bytes. Create delegates, reflection data, test inputs, and assertion messages before the measured region.
3. Use the Unity Profiler to verify all relevant threads; the current-thread API only covers the thread executing the test. Follow the first managed allocator in the call stack rather than assuming the marker's top-level method is the direct cause.
4. Keep cold-path defensive copies and real resource creation when required for correctness. Move them out of the stable frame path instead of removing ownership boundaries.

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



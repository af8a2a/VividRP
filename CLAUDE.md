# CLAUDE.md

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

## 5. Important Notes

- Unity `.meta` files are auto-generated; do not manually create or edit them
- Do not hand-edit generated or synchronized artifacts such as `Editor/SourceGenerators/VividRP.RenderPassNodeGenerator.dll` or `Runtime/Resources/PipelineResources.asset`; rebuild the former from `SourceGenerators~/VividRP.RenderPassNodeGenerator` and update the latter through its sync pipeline
- Unity `.meta` files, generated assets, and package-relative paths must stay in sync when moving or renaming files
- The repository currently uses both `Packages/com.af8a2a.vividrp/...` and `Packages/VividRP/...` path constants; do not “fix” only one side during refactors — audit all package-relative paths together
- Package path changes require an audit of `Editor/PipelineResource/PipelineResourceUpdater.cs` and all package-relative constants found by the package path search below
- Quick searches:
  - Pass/resource search: `rg "IRenderPass|RenderGraphResource|PipelineResource|ResourcePath" Runtime Editor Tests`
  - Editor/codegen search: `rg "GeneratedRenderPassNodeRegistry|RenderPassNodeSourceGenerator|GetRegisteredPassType" Editor Runtime Tests SourceGenerators~`
  - Package path audit: `rg "Packages/VividRP|Packages/com.af8a2a.vividrp|com.af8a2a.vividrp" Runtime Editor Tests package.json`

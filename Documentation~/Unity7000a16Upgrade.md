# Unity 7000.0.0a16 compatibility

Updated on 2026-11-06 against Editor revision `0745a91df592`.

- VividRP now requires `7000.0.0a16`; the containing project already selected it.
- Collections is updated from `6.7.0` to the a16 built-in `7.0.0`.
- Pipeline is updated from `0.8.0-exp.1` to registry version `0.10.0-exp.1` in
  both VividRP and the containing project's manifest. Unity regenerated the lock.
- Other VividRP dependencies match the a16 built-in packages or current registry
  versions checked on this date. CoreRP and Shader Graph remain `17.7.0`.

Embedded CoreRP was merged against a16's installed BuiltInPackages source using
the a9 baseline reconstructed from the previous upgrade's backup and upstream
merge inputs. The update applies 95 files and removes ten obsolete upstream
SurfaceCache test sources/assembly definitions with their metadata. Existing
metadata is preserved; new metadata comes from upstream. CHANGELOG.md is preserved.
The only merge conflict, Runtime/AssemblyInfo.cs, retains both the upstream
friend assembly and VividRP friend assemblies. Existing VividRP customizations
in command buffers, Blitter, and CoreUtils were merged. CoreRP is a separate
repository; retain its changes together with this package update.

Validation:

- a16 batch-mode import and C# compilation exited successfully.
- Five focused EditMode tests passed: GPU data strides, legacy MeshLOD offsets,
  shared MeshLOD offsets, instance offsets, and meshlet vertex offsets.
- An additional culling/LOD context field-offset test passed, including fixed
  arrays and padding (six passing tests in total). Both test batches saved passing
  XML results but stalled during Editor shutdown; their owned processes were
  terminated after results were saved. The ordinary import batch exited normally.
- Dependency declarations match the resolved lock, with CoreRP still embedded.
- All 43 focused DXC entrypoints/variants passed using
  `Tools~/Validate-SimpleSlab.ps1` and the currently installed Vulkan SDK DXC.
  This checks standalone compilation, not GPU execution.
- VividRP and CoreRP whitespace checks passed.
- Scene visual correctness and performance remain unverified.

Backup, merge inputs/report, import log, test XML/log, and shader validation log
are retained under `Temp~/Unity7000a16Upgrade/` (untracked).

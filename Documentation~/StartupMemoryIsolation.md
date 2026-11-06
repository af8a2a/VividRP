# Startup memory isolation

For an Editor-only minimal SRP diagnostic, set `VIVIDRP_STARTUP_SRP=minimal`
in the environment of the **new Editor process**. Clear it for normal rendering.
No pipeline asset or project setting is changed.

```powershell
$env:VIVIDRP_STARTUP_SRP = 'minimal'
# Launch the Editor using an external memory/handle watchdog.
# Restore this shell's previous environment value afterwards.
```

The pipeline keeps its declared `VividRenderPipeline` type, creates one reusable
command buffer, clears camera targets and submits. It skips global-settings
setup, RenderGraph, runtime subsystem initialization and normal subsystem
teardown. Player builds do not contain this diagnostic branch. Native plugin
preloading, Editor initialization callbacks, scene components and Unity's asset
indexer remain active, so a stable minimal run alone does not identify a plugin.

Compare process private bytes and handle counts over equivalent observation
windows. Startup caches and graphics initialization can legitimately exceed
4 GiB in this project; distinguish a plateau from continuing growth. Stop the
Editor immediately when abnormal growth or the diagnostic budget is reached.

## Recursive package aliases (2026-11-06)

The observed runaway directory handles were traced to
`UnityEditor.Search.AssetIndexer.GetDependencies()` calling a recursive
`System.IO.Enumeration.FileSystemEnumerator`. Two old shader-capture include
trees under `Temp~/reference-pt-20261009/includes/Packages` and
`Temp~/openpbr-deferred-20261012/includes/Packages` contained junctions pointing
back to VividRP. Traversing either alias re-entered `Temp~` and repeated the tree.
The four temporary VividRP/CoreRP aliases were removed non-recursively; their
target directories and files were retained.

Unity's asset-import exclusion for `~` directories does not establish that every
filesystem-based indexer excludes them. Keep package-root include aliases outside
the project/package tree, and remove temporary junctions in `finally` blocks.

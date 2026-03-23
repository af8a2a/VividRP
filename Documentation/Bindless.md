# Bindless Native Plugin Setup

## Why this exists

VividRP's bindless path relies on `UnityBindless.dll` hooking D3D12 descriptor heap creation early enough to reserve a plugin-owned descriptor range.

On current Unity 6000.5 editor builds, native plugins loaded from `Packages/...` can arrive too late for this hook point. When that happens, the plugin can still see the active shader-visible CBV/SRV/UAV heap through `SetDescriptorHeaps`, but it misses the original `CreateDescriptorHeap` call and never learns the plugin-owned range. The visible symptom is:

- `GetSRVDescriptorHeapCount()` returns a non-zero heap size
- `GetBindlessDescriptorCount()` stays `0`
- bindless descriptors can never be created reliably

This behavior matches Unity issue `UUM-134389`, where Unity's current workaround is to bypass Package Manager for this kind of early native plugin hook.

## Why the DLL lives in `BindlessNative~`

The package stores the native payload under `BindlessNative~`. Unity ignores folders suffixed with `~` during import, so the DLL stays versioned in the package without being loaded from the package itself.

That lets the package keep a single source of truth while forcing the actual early-loaded copy to come from the project.

## Why the Setup script copies into `Assets/Plugins`

`Assets/Plugins/...` participates in Unity's normal project plugin discovery path. A project-local copy placed there and marked as preloaded can be loaded before the D3D12 device and descriptor heap setup is finished.

That earlier load timing is the difference between:

- successfully capturing the plugin-owned bindless descriptor range
- only observing Unity's already-created default shader-visible heap

## How to use it

From the project root, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Packages\VividRP\Setup-Bindless.ps1
```

The script:

- creates `Assets/Plugins/VividRP/x86_64` if it does not exist
- copies `UnityBindless.dll`
- copies `UnityBindless.pdb` when present
- creates a preloaded plugin `.meta` file on first install
- removes known stale copies from older locations

After the script finishes, restart Unity.

## Generated files

The setup script installs the project-local runtime copy here:

- `Assets/Plugins/VividRP/x86_64/UnityBindless.dll`
- `Assets/Plugins/VividRP/x86_64/UnityBindless.pdb`
- `Assets/Plugins/VividRP/x86_64/UnityBindless.dll.meta`

These generated files are intentional. The package-local copy in `BindlessNative~` is only the source payload.

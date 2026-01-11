# DLSS Preset Migration Guide

## Overview

DLSS preset configuration has been migrated from **asset-level** (UniversalRenderPipelineAsset) to **per-camera** (UniversalCameraData) for better flexibility and per-camera customization.

## What Changed

### Before (Deprecated)
```csharp
// Asset-level DLSS presets (DEPRECATED)
UniversalRenderPipeline.asset.m_DLSSPreset.DLSSRenderPresetForQuality
UniversalRenderPipeline.asset.m_DLSSPreset.DLSSRenderPresetForBalanced
// ...etc
```

### After (Current)
```csharp
// Per-camera DLSS presets
UniversalCameraData.dlssPreset.DLSSRenderPresetForQuality
UniversalCameraData.dlssPreset.DLSSRenderPresetForBalanced
// ...etc

// Get effective preset with automatic fallback
DLSSPreset effectivePreset = cameraData.GetEffectiveDLSSPreset();
```

## Migration Strategy

### Backward Compatibility

The migration is **fully backward compatible**:

1. **Asset-level settings preserved**: `UniversalRenderPipelineAsset.m_DLSSPreset` still exists but is marked `[Obsolete]`
2. **Automatic fallback**: If a camera doesn't have a custom preset, it automatically falls back to the asset-level preset
3. **Gradual migration**: Projects can migrate camera-by-camera without breaking existing functionality

### Fallback Hierarchy

```
Camera DLSS Preset Resolution:
1. Check UniversalCameraData.dlssPreset (per-camera, highest priority)
   ↓ if null
2. Fall back to UniversalRenderPipelineAsset.m_DLSSPreset (asset-level, deprecated)
   ↓ if null
3. Use default DLSSPreset() (all presets = 0)
```

## Code Changes

### Files Modified

1. **UniversalCameraData.Upscaler.cs**
   - Added `internal DLSSPreset dlssPreset = null;`
   - Added `internal DLSSPreset GetEffectiveDLSSPreset()` method

2. **UniversalRenderPipelineAsset.Upscaler.cs**
   - Marked `m_DLSSPreset` as `[Obsolete]` with migration message

3. **DLSSWarpPass.cs**
   - Changed from `UniversalRenderPipeline.asset.m_DLSSPreset`
   - To: `cameraData.GetEffectiveDLSSPreset()`

4. **VividRenderPipelineAssetUI.Drawers.cs**
   - Added deprecation warning HelpBox in UI

### Key Implementation

```csharp
// UniversalCameraData.Upscaler.cs
internal DLSSPreset GetEffectiveDLSSPreset()
{
    if (dlssPreset != null)
        return dlssPreset;

    // Fallback to asset-level preset for backward compatibility
    #pragma warning disable CS0618 // Type or member is obsolete
    if (UniversalRenderPipeline.asset != null && UniversalRenderPipeline.asset.m_DLSSPreset != null)
        return UniversalRenderPipeline.asset.m_DLSSPreset;
    #pragma warning restore CS0618

    // Return default preset if both are null
    return new DLSSPreset();
}
```

## For Developers

### Setting Per-Camera DLSS Presets

```csharp
// Get camera data
var camera = GetComponent<Camera>();
var additionalCameraData = camera.GetUniversalAdditionalCameraData();

// Create custom DLSS preset
var customPreset = new DLSSPreset
{
    DLSSRenderPresetForQuality = 11,      // Preset K (transformer-based)
    DLSSRenderPresetForBalanced = 10,     // Preset J
    DLSSRenderPresetForPerformance = 13,  // Preset M
    DLSSRenderPresetForUltraPerformance = 12, // Preset L
    DLSSRenderPresetForDLAA = 11          // Preset K
};

// Set to camera data (requires access to internal field)
// Note: This is internal API, typically set via custom editor or initialization script
```

### Accessing in Rendering Code

```csharp
// In render passes
DLSSPreset preset = cameraData.GetEffectiveDLSSPreset();

// Pass to DLSSPass.Parameters
parameters.dlssPreset = preset;
```

## TODO: Editor UI for Per-Camera Presets

**Location**: `VividCameraEditor.cs` or `UniversalAdditionalCameraDataEditor.cs`

To fully support per-camera configuration, add UI in the camera inspector:

```csharp
// Pseudocode for camera editor
public override void OnInspectorGUI()
{
    // ... existing camera properties ...

    // DLSS Preset section
    EditorGUILayout.Space();
    EditorGUILayout.LabelField("DLSS Presets", EditorStyles.boldLabel);

    var useCameraPreset = EditorGUILayout.Toggle("Override Asset Preset",
        m_CameraData.dlssPreset != null);

    if (useCameraPreset)
    {
        if (m_CameraData.dlssPreset == null)
            m_CameraData.dlssPreset = new DLSSPreset();

        // Draw preset dropdowns for each quality mode
        DrawPresetDropdown("Quality", ref m_CameraData.dlssPreset.DLSSRenderPresetForQuality);
        DrawPresetDropdown("Balanced", ref m_CameraData.dlssPreset.DLSSRenderPresetForBalanced);
        DrawPresetDropdown("Performance", ref m_CameraData.dlssPreset.DLSSRenderPresetForPerformance);
        DrawPresetDropdown("Ultra Performance", ref m_CameraData.dlssPreset.DLSSRenderPresetForUltraPerformance);
        DrawPresetDropdown("DLAA", ref m_CameraData.dlssPreset.DLSSRenderPresetForDLAA);
    }
    else
    {
        m_CameraData.dlssPreset = null;
        EditorGUILayout.HelpBox("Using asset-level DLSS presets", MessageType.Info);
    }
}
```

## Migration Checklist

For existing projects:

- [ ] **No action required** - Asset-level presets continue to work automatically
- [ ] *(Optional)* Suppress obsolete warnings by migrating to per-camera presets
- [ ] *(Optional)* Remove asset-level preset assignments if all cameras have custom presets
- [ ] *(Optional)* Implement custom camera editor UI for per-camera preset configuration

For new projects:

- [ ] Configure DLSS presets per-camera via custom editor or initialization scripts
- [ ] Leave asset-level preset as default fallback
- [ ] Test preset fallback hierarchy works as expected

## Benefits of Per-Camera Presets

1. **Multi-camera setups**: Different cameras can use different DLSS presets
2. **Scene-specific optimization**: Cutscenes vs gameplay can have different quality settings
3. **Performance scaling**: High-priority cameras get better presets, background cameras use faster presets
4. **A/B testing**: Compare DLSS presets side-by-side with dual cameras
5. **VR/XR flexibility**: Per-eye configuration if needed

## Technical Notes

### Why Per-Camera?

- **Flexibility**: Some cameras need higher quality (main camera) while others can use faster presets (minimap, rear-view)
- **Scalability**: Easier to manage quality settings for complex multi-camera setups
- **Modern architecture**: Aligns with per-camera rendering settings pattern in URP
- **Future-proof**: Prepares for potential camera-specific DLSS features

### Preset Values Reference

DLSS SR Presets (uint values):
- `0` = Default
- `6` = F (Deprecated)
- `7` = G (Reverts to default)
- `10` = J (Less ghosting, more flickering)
- `11` = K (Best quality, transformer-based)
- `12` = L (Default for Ultra Performance)
- `13` = M (Default for Performance)

## Timeline

- **Current**: Deprecation warnings in editor UI
- **Next release**: Obsolete attribute on asset-level field
- **Future release (TBD)**: Remove asset-level field entirely (breaking change)

Projects should migrate to per-camera presets at their convenience before the final removal.

---

## Questions or Issues?

For migration support or questions about per-camera DLSS presets, please refer to:
- VividRP documentation
- DLSS integration guide
- DLSSPass.cs implementation comments

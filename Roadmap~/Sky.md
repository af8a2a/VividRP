# Sky System Implementation Plan

## Current State Summary

VividRP has a minimal HDRI sky consisting of:

| Component | File | Role |
|---|---|---|
| `HDRISkyVolume` | `Runtime/RenderPipeline/HDRISkyVolume.cs` | Volume params: cubemap, tint, exposure, rotation |
| `HDRISkyPass` | `Runtime/RenderPass/Core/HDRISkyPass.cs` | Fullscreen raster pass — sample cubemap to color target |
| `HDRISky.shader` | `Shaders/Core/Private/HDRISky.shader` | View-dir -> rotate -> sample cubemap |
| `DeferredLightingPass` | `Runtime/RenderPass/Core/DeferredLightingPass.cs` | Imports sky cubemap as `SkyIBLCubemap`, binds tint/exposure/rotation for specular IBL |

### What exists

- Specular IBL: `DeferredLightingPass` imports the raw HDRI cubemap via `PrepareSkyIblState()` (line 612) and binds `_VividSkyIBLCubemap` / `_VividSkyIBLTint` / `_VividSkyIBLParams` to the compute shader.
- Shader sampling: `HdrpLitLighting.hlsl` `VividSampleSkyIBL()` (line 147) does roughness-based mip sampling with rotation.
- Diffuse fallback: `EvaluateVividBakedDiffuseLighting()` (line 307) falls back to Unity built-in `SampleSH(normalWS)` from `UnityPerDraw` cbuffer when no baked GI — this uses `unity_SHAr/Ag/Ab/Br/Bg/Bb/C`, **not** a VividRP-controlled SH.

### What is missing

- No `SkyManager` — passes read `HDRISkyVolume` directly
- No sky type abstraction — cannot plug in alternative sky models
- No sky-derived diffuse SH — ambient probe comes from Unity built-in, not from the sky cubemap
- No sky update strategy — every frame re-reads volume, re-imports cubemap
- No prefiltered specular env — raw cubemap mips used directly
- No physically based sky model

---

## Target Architecture

```
Volume Layer               Runtime Layer              Shader Layer
─────────────             ─────────────              ────────────
SkySettingsVolume ──┐
                    ├──► SkyManager ──► VividSkyData ──► ShaderVariablesGlobal (SH)
HDRISkyVolume ──────┤        │                            DeferredLitCompute (IBL cubemap)
                    │        │
PBSkyVolume ────────┘    ISkyRenderer                  HDRISky.shader (background)
                         ├─ HDRISkyRenderer             PBSky.shader  (background)
                         └─ PBSkyRenderer
```

---

## Phase 0 — Sky Framework

**Goal**: Extract sky logic from passes into a pluggable framework. No visual change.

### 0.1 `SkyType` enum

**New file**: `Runtime/RenderPipeline/Sky/SkyType.cs`

```csharp
namespace VividRP.Runtime
{
    public enum SkyType
    {
        None = 0,
        HDRI = 1,
        PhysicallyBased = 2
    }
}
```

### 0.2 `SkyUpdateMode` enum

**New file**: `Runtime/RenderPipeline/Sky/SkyUpdateMode.cs`

```csharp
namespace VividRP.Runtime
{
    public enum SkyUpdateMode
    {
        OnChanged = 0,
        OnDemand = 1,
        Realtime = 2
    }
}
```

### 0.3 `SkySettingsVolume`

**New file**: `Runtime/RenderPipeline/Sky/SkySettingsVolume.cs`

```csharp
namespace VividRP.Runtime
{
    [Serializable]
    [VolumeComponentMenu("VividRP/Sky Settings")]
    public sealed class SkySettingsVolume : VolumeComponent
    {
        public VolumeParameter<SkyType> skyType = new(SkyType.HDRI);
        public VolumeParameter<SkyUpdateMode> updateMode = new(SkyUpdateMode.OnChanged);
        public ClampedFloatParameter updatePeriod = new(0f, 0f, 10f);
    }
}
```

### 0.4 `ISkyRenderer` interface

**New file**: `Runtime/RenderPipeline/Sky/ISkyRenderer.cs`

```csharp
namespace VividRP.Runtime
{
    internal interface ISkyRenderer : IDisposable
    {
        SkyType Type { get; }
        bool IsActive();
        int GetSkyHash();
        void Build(VividRPCoreResources resources);
        SphericalHarmonicsL2 EvaluateDiffuseSH();
        Cubemap GetSpecularCubemap();
        Color GetTint();
        float GetExposure();
        float GetRotation();
    }
}
```

### 0.5 `HDRISkyRenderer`

**New file**: `Runtime/RenderPipeline/Sky/HDRISkyRenderer.cs`

Wraps current logic from `DeferredLightingPass.PrepareSkyIblState()` and `HDRISkyPass.Record()`:

```csharp
namespace VividRP.Runtime
{
    internal sealed class HDRISkyRenderer : ISkyRenderer
    {
        public SkyType Type => SkyType.HDRI;

        public bool IsActive()
        {
            var sky = VolumeManager.instance.stack?.GetComponent<HDRISkyVolume>();
            return sky != null && sky.HasSkyCubemap();
        }

        public int GetSkyHash()
        {
            // Hash cubemap instance ID + tint + exposure + rotation
        }

        public SphericalHarmonicsL2 EvaluateDiffuseSH()
        {
            // Phase 1 will implement; return default for now
            return default;
        }

        public Cubemap GetSpecularCubemap()
        {
            var sky = VolumeManager.instance.stack?.GetComponent<HDRISkyVolume>();
            return sky?.GetSkyCubemapOrDefault();
        }

        public Color GetTint() { /* read HDRISkyVolume.tint */ }
        public float GetExposure() { /* read HDRISkyVolume.exposure */ }
        public float GetRotation() { /* read HDRISkyVolume.rotation */ }

        public void Build(VividRPCoreResources resources) { }
        public void Dispose() { }
    }
}
```

### 0.6 `VividSkyData` (ContextItem)

**New file**: `Runtime/RenderGraph/FrameContext/VividSkyData.cs`

Carries per-frame sky outputs through the frame context so passes never touch volumes directly:

```csharp
namespace VividRP.Runtime
{
    public class VividSkyData : ContextItem
    {
        public SkyType activeSkyType;
        public bool hasDiffuseSH;
        public SphericalHarmonicsL2 diffuseSH;
        public Cubemap specularCubemap;
        public Color tint;
        public float exposure;
        public float rotation;
        public int skyHash;

        public override void Reset()
        {
            activeSkyType = SkyType.None;
            hasDiffuseSH = false;
            diffuseSH = default;
            specularCubemap = null;
            tint = Color.white;
            exposure = 1f;
            rotation = 0f;
            skyHash = 0;
        }
    }
}
```

### 0.7 `SkyManager`

**New file**: `Runtime/RenderPipeline/Sky/SkyManager.cs`

Static manager following the same pattern as `VividVolumeManagerUtility`:

```csharp
namespace VividRP.Runtime
{
    internal static class SkyManager
    {
        private static readonly Dictionary<SkyType, ISkyRenderer> s_Renderers = new();
        private static int s_LastSkyHash;
        private static float s_LastUpdateTime;
        private static bool s_Initialized;
        private static SphericalHarmonicsL2 s_CachedDiffuseSH;
        private static bool s_HasCachedDiffuseSH;

        internal static void Initialize()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            RegisterRenderer(new HDRISkyRenderer(), resources);
            s_Initialized = true;
        }

        internal static void Deinitialize()
        {
            foreach (var renderer in s_Renderers.Values)
                renderer.Dispose();
            s_Renderers.Clear();
            s_Initialized = false;
        }

        internal static void Update(ContextContainer frameData)
        {
            var skyData = frameData.GetOrCreate<VividSkyData>();
            var settings = VolumeManager.instance.stack?.GetComponent<SkySettingsVolume>();
            var skyType = settings?.skyType.value ?? SkyType.HDRI;
            var updateMode = settings?.updateMode.value ?? SkyUpdateMode.OnChanged;

            if (!s_Renderers.TryGetValue(skyType, out var renderer) || !renderer.IsActive())
            {
                skyData.Reset();
                return;
            }

            skyData.activeSkyType = skyType;
            skyData.specularCubemap = renderer.GetSpecularCubemap();
            skyData.tint = renderer.GetTint();
            skyData.exposure = renderer.GetExposure();
            skyData.rotation = renderer.GetRotation();

            var currentHash = renderer.GetSkyHash();
            skyData.skyHash = currentHash;

            if (NeedsUpdate(updateMode, currentHash, settings?.updatePeriod.value ?? 0f))
            {
                s_CachedDiffuseSH = renderer.EvaluateDiffuseSH();
                s_HasCachedDiffuseSH = true;
                s_LastSkyHash = currentHash;
                s_LastUpdateTime = Time.time;
            }

            skyData.hasDiffuseSH = s_HasCachedDiffuseSH;
            skyData.diffuseSH = s_CachedDiffuseSH;
        }

        private static bool NeedsUpdate(SkyUpdateMode mode, int currentHash, float period)
        {
            return mode switch
            {
                SkyUpdateMode.OnChanged => currentHash != s_LastSkyHash,
                SkyUpdateMode.Realtime => period <= 0f || Time.time - s_LastUpdateTime >= period,
                SkyUpdateMode.OnDemand => false,
                _ => false
            };
        }

        internal static void RequestUpdate() => s_LastSkyHash = 0;

        private static void RegisterRenderer(ISkyRenderer renderer, VividRPCoreResources resources)
        {
            renderer.Build(resources);
            s_Renderers[renderer.Type] = renderer;
        }
    }
}
```

### 0.8 Integration points — changes to existing files

#### `VividRenderPipeline.cs`

```
Constructor (after line 31):
+   SkyManager.Initialize();

Dispose (after line 212):
+   SkyManager.Deinitialize();
```

#### `VividRenderPipeline.RenderCamera()` (line 69-73)

```
    VividVolumeManagerUtility.Update(camera);
+   // SkyManager.Update is called inside PassRecorder.InitializeContext
    PassRecorder.InitializeContext(context, camera, cullingResults);
```

#### `PassRecorder.Execution.cs` — `InitializeContext()`

Add at end of method:

```csharp
    SkyManager.Update(s_FrameData);
```

#### `HDRISkyPass.Record()` — read from `VividSkyData` instead of `HDRISkyVolume`

Replace lines 57-62:

```csharp
// Before:
var skySettings = VolumeManager.instance.stack?.GetComponent<HDRISkyVolume>();
var cubemap = skySettings?.GetSkyCubemapOrDefault();
var tint = skySettings?.tint.value ?? Color.white;
var exposure = skySettings?.exposure.value ?? 1f;
var rotation = skySettings?.rotation.value ?? 0f;

// After:
// HDRISkyPass.Prepare() caches sky data from VividSkyData
```

Move cubemap / tint / exposure / rotation reads into `Prepare()` from `VividSkyData`:

```csharp
public override void Prepare(ContextContainer frameData)
{
    var cameraData = frameData.Get<VividCameraData>();
    var skyData = frameData.Get<VividSkyData>();
    m_PixelCoordToViewDirMatrix = cameraData.GetPixelCoordToViewDirWSMatrix();
    m_Cubemap = skyData.specularCubemap;
    m_Tint = skyData.tint;
    m_Exposure = skyData.exposure;
    m_Rotation = skyData.rotation;
    // ... resize textures ...
}
```

#### `DeferredLightingPass.PrepareSkyIblState()` — read from `VividSkyData`

Replace lines 612-626. Change method signature to accept `VividSkyData`:

```csharp
private void PrepareSkyIblState(VividSkyData skyData)
{
    var skyCubemap = skyData?.specularCubemap ?? m_FallbackSkyIBLCubemap;
    EnsureSkyIblCubemapImported(skyCubemap);
    m_SkyIBLTint = skyData?.tint ?? Color.white;
    var skyExposure = skyData?.exposure ?? 1f;
    var skyRotation = skyData?.rotation ?? 0f;
    m_SkyIBLParams = BuildSkyIblParams(skyCubemap, skyExposure, skyRotation);
}
```

Update call site in `Prepare()` (line 272):

```csharp
var skyData = frameData.GetOrCreate<VividSkyData>();
PrepareSkyIblState(skyData);
```

#### `VividVolumeManagerUtility.cs`

Add helper:

```csharp
internal static SkySettingsVolume GetSkySettingsVolume()
{
    return VolumeManager.instance.stack?.GetComponent<SkySettingsVolume>();
}
```

### 0.9 New files summary

```
Runtime/RenderPipeline/Sky/
    SkyType.cs
    SkyUpdateMode.cs
    SkySettingsVolume.cs
    ISkyRenderer.cs
    HDRISkyRenderer.cs
    SkyManager.cs
Runtime/RenderGraph/FrameContext/
    VividSkyData.cs
```

### 0.10 Tests

**New file**: `Tests/Editor/SkyManagerTests.cs`

- `SkyManager_ReturnsHDRISkyData_WhenSkyTypeIsHDRI`
- `SkyManager_ReturnsNone_WhenSkyTypeIsNone`
- `SkyManager_SkipsUpdate_WhenUpdateModeIsOnDemandAndNoRequest`
- `SkyManager_UpdatesSH_WhenHashChangesInOnChangedMode`

**New file**: `Tests/Editor/HDRISkyRendererTests.cs`

- `HDRISkyRenderer_IsActive_WhenCubemapIsAssigned`
- `HDRISkyRenderer_GetSkyHash_ChangesWithExposure`
- `HDRISkyRenderer_GetSpecularCubemap_ReturnsFallback_WhenNoVolumeActive`

**Modify**: `Tests/Editor/HDRISkyPassTests.cs`

- Update tests to provide `VividSkyData` in frame context instead of relying on `VolumeManager`

**Modify**: `Tests/Editor/DeferredDirectionalLightingPassTests.cs`

- Update `Prepare` tests to populate `VividSkyData` in frame context

---

## Phase 1 — Diffuse SH from Sky

**Goal**: Generate `SphericalHarmonicsL2` from HDRI cubemap, feed it to lighting shaders.

### 1.1 `SkyDiffuseSHUtility`

**New file**: `Runtime/RenderPipeline/Sky/SkyDiffuseSHUtility.cs`

CPU-side SH projection from cubemap:

```csharp
namespace VividRP.Runtime
{
    internal static class SkyDiffuseSHUtility
    {
        internal static SphericalHarmonicsL2 ProjectCubemapToSH(
            Cubemap cubemap,
            Color tint,
            float exposure,
            float rotation)
        {
            // 1. Read cubemap pixels per face (GetPixels at mip 0 or a low mip)
            // 2. For each texel, compute direction from face + UV
            // 3. Apply rotation around Y
            // 4. Apply tint * exposure
            // 5. Accumulate into SH L2 coefficients using standard basis functions
            // 6. Apply normalization (4*PI / sampleCount)
            // Return SphericalHarmonicsL2
        }
    }
}
```

**Implementation notes**:

- Use the lowest reasonable mip (e.g. 4x4 or 8x8 per face) — diffuse SH does not need high-res input
- Unity's `SphericalHarmonicsL2` has 27 coefficients (3 channels x 9 basis)
- Can use `cubemap.GetPixels(face, mipLevel)` for CPU readback
- Standard SH basis: `Y_00, Y_1-1, Y_10, Y_11, Y_2-2, Y_2-1, Y_20, Y_21, Y_22`

### 1.2 Implement `HDRISkyRenderer.EvaluateDiffuseSH()`

```csharp
public SphericalHarmonicsL2 EvaluateDiffuseSH()
{
    var sky = VolumeManager.instance.stack?.GetComponent<HDRISkyVolume>();
    var cubemap = sky?.GetSkyCubemapOrDefault();
    if (cubemap == null)
        return default;

    return SkyDiffuseSHUtility.ProjectCubemapToSH(
        cubemap,
        sky.tint.value,
        sky.exposure.value,
        sky.rotation.value);
}
```

### 1.3 Add SH coefficients to `ShaderVariablesGlobal`

#### `Runtime/RenderGraph/FrameContext/ShaderVariablesGlobal.cs`

Add after `_VividShadowColor` (line 57):

```csharp
public Vector4 _VividSHAr;
public Vector4 _VividSHAg;
public Vector4 _VividSHAb;
public Vector4 _VividSHBr;
public Vector4 _VividSHBg;
public Vector4 _VividSHBb;
public Vector4 _VividSHC;
```

In `Create()` method, add after line 122:

```csharp
_VividSHAr = Vector4.zero,
_VividSHAg = Vector4.zero,
_VividSHAb = Vector4.zero,
_VividSHBr = Vector4.zero,
_VividSHBg = Vector4.zero,
_VividSHBb = Vector4.zero,
_VividSHC = Vector4.zero,
```

#### `Shaders/Core/Public/ShaderVariablesGlobal.hlsl`

Add before `CBUFFER_END` (line 52):

```hlsl
float4 _VividSHAr;
float4 _VividSHAg;
float4 _VividSHAb;
float4 _VividSHBr;
float4 _VividSHBg;
float4 _VividSHBb;
float4 _VividSHC;
```

### 1.4 Populate SH in frame setup

**Modify**: `FrameContextSystem.cs` or `ShaderVariablesGlobal.Create()`

Add a new overload or extend `Create()` to accept `VividSkyData`:

```csharp
internal static ShaderVariablesGlobal Create(
    VividCameraData.ShaderVariables shaderVariables,
    CameraTemporalData temporalData,
    VividSkyData skyData)
{
    var result = Create(shaderVariables, temporalData);

    if (skyData != null && skyData.hasDiffuseSH)
    {
        SphericalHarmonicsToVectors(skyData.diffuseSH, out result._VividSHAr, ...);
    }

    return result;
}
```

**New helper** in `ShaderVariablesGlobal.cs`:

```csharp
private static void SphericalHarmonicsToVectors(
    SphericalHarmonicsL2 sh,
    out Vector4 shar, out Vector4 shag, out Vector4 shab,
    out Vector4 shbr, out Vector4 shbg, out Vector4 shbb,
    out Vector4 shc)
{
    // Pack SH coefficients into the 7-vector format Unity shaders expect
    // L0 + L1: SHAr = (r[1], r[2], r[3], r[0])  etc.
    // L2:      SHBr = (r[4], r[5], r[6], r[7])   etc.
    // SHC      = (r[8], g[8], b[8], 1)
    //
    // Use SphericalHarmonicsL2 indexer: sh[channel, coefficient]
}
```

### 1.5 Consume SH in shader

#### `Shaders/Core/Public/HdrpLitLighting.hlsl`

Add a new function:

```hlsl
float3 VividSampleSkySH(float3 normalWS)
{
    // Evaluate L0+L1
    float4 normal4 = float4(normalWS, 1.0);
    float3 res;
    res.r = dot(_VividSHAr, normal4);
    res.g = dot(_VividSHAg, normal4);
    res.b = dot(_VividSHAb, normal4);

    // Evaluate L2
    float4 normalSq = normalWS.xyzz * normalWS.yzzx;
    res.r += dot(_VividSHBr, normalSq);
    res.g += dot(_VividSHBg, normalSq);
    res.b += dot(_VividSHBb, normalSq);

    float vC = normalWS.x * normalWS.x - normalWS.y * normalWS.y;
    res += _VividSHC.rgb * vC;

    return max(0.0, res);
}
```

Modify `EvaluateVividBakedDiffuseLighting()` (line 307):

```hlsl
float3 EvaluateVividBakedDiffuseLighting(VividGBufferSurfaceData surfaceData)
{
    if (surfaceData.hasBakedGI > 0.5)
        return surfaceData.bakedGI;

    // Use VividRP sky SH when available, fallback to Unity built-in
    float3 skySH = VividSampleSkySH(surfaceData.normalWS);
    return any(skySH > 0.0) ? skySH : SampleSH(surfaceData.normalWS);
}
```

### 1.6 Tests

**New file**: `Tests/Editor/SkyDiffuseSHUtilityTests.cs`

- `ProjectCubemapToSH_ReturnsNonZero_WhenCubemapHasColor`
- `ProjectCubemapToSH_RespectsExposure_WhenExposureIsTwo`
- `ProjectCubemapToSH_RespectsRotation_WhenRotatedBy180`
- `ProjectCubemapToSH_ReturnsZero_WhenCubemapIsBlack`

**New file**: `Tests/Editor/ShaderVariablesGlobalSHTests.cs`

- `Create_PopulatesSHVectors_WhenSkyDataHasDiffuseSH`
- `Create_LeavesSHZero_WhenSkyDataHasNoDiffuseSH`
- `SphericalHarmonicsToVectors_MatchesUnityFormat_WhenGivenKnownSH`

### 1.7 New files summary

```
Runtime/RenderPipeline/Sky/
    SkyDiffuseSHUtility.cs
Tests/Editor/
    SkyDiffuseSHUtilityTests.cs
    ShaderVariablesGlobalSHTests.cs
```

### 1.8 Changed files summary

```
Runtime/RenderGraph/FrameContext/ShaderVariablesGlobal.cs   — add 7 SH fields + packing helper
Shaders/Core/Public/ShaderVariablesGlobal.hlsl              — add 7 SH float4 uniforms
Shaders/Core/Public/HdrpLitLighting.hlsl                    — add VividSampleSkySH(), modify EvaluateVividBakedDiffuseLighting()
Runtime/RenderPipeline/Sky/HDRISkyRenderer.cs               — implement EvaluateDiffuseSH()
```

---

## Phase 2 — Specular Environment Formalization

**Goal**: Separate raw sky cubemap from lighting cubemap; add runtime cache and prefilter path.

### 2.1 `SkySpecularCache`

**New file**: `Runtime/RenderPipeline/Sky/SkySpecularCache.cs`

```csharp
namespace VividRP.Runtime
{
    internal sealed class SkySpecularCache : IDisposable
    {
        private RTHandle m_CachedCubemap;
        private int m_CachedSkyHash;

        internal bool IsValid => m_CachedCubemap != null;
        internal RTHandle Cubemap => m_CachedCubemap;
        internal int SkyHash => m_CachedSkyHash;

        internal void Update(Cubemap source, int skyHash)
        {
            if (source == null) return;
            if (skyHash == m_CachedSkyHash && m_CachedCubemap != null) return;

            m_CachedCubemap?.Release();
            m_CachedCubemap = RTHandles.Alloc(source);
            m_CachedSkyHash = skyHash;
        }

        public void Dispose()
        {
            m_CachedCubemap?.Release();
            m_CachedCubemap = null;
            m_CachedSkyHash = 0;
        }
    }
}
```

### 2.2 Move cubemap import from `DeferredLightingPass` to `SkyManager`

Currently `DeferredLightingPass` owns `ImportedSkyCubemapState` and calls `RTHandles.Alloc` / `PassRecorder.ImportTexture` directly (lines 628-641).

Move this responsibility to `SkyManager`:

```csharp
// In SkyManager:
private static SkySpecularCache s_SpecularCache;

internal static RTHandle GetSpecularCubemapHandle()
{
    return s_SpecularCache?.Cubemap;
}
```

`DeferredLightingPass.PrepareSkyIblState()` becomes:

```csharp
private void PrepareSkyIblState(VividSkyData skyData)
{
    var handle = SkyManager.GetSpecularCubemapHandle();
    if (handle != null && PassRecorder.IsPassTextureImportActive)
        PassRecorder.ImportTexture(m_SkyIBLCubemap, handle);

    m_SkyIBLTint = skyData?.tint ?? Color.white;
    m_SkyIBLParams = BuildSkyIblParams(
        skyData?.specularCubemap,
        skyData?.exposure ?? 1f,
        skyData?.rotation ?? 0f);
}
```

This removes `ImportedSkyCubemapState`, `m_SkyIBLCubemapState`, `EnsureSkyIblCubemapImported()`, `ReleaseSkyIblCubemapState()`, `CreateFallbackSkyIBLCubemap()` from `DeferredLightingPass`.

### 2.3 Future: GGX prefilter compute pass

Not in first implementation. Design slot:

```csharp
// Future: SkySpecularPrefilterPass extends ComputePass
// Input: raw sky cubemap
// Output: prefiltered cubemap with GGX-convolved mips
// Triggered by SkyManager when sky hash changes
```

### 2.4 Tests

**Modify**: `Tests/Editor/DeferredDirectionalLightingPassTests.cs`

- Update `Prepare_ResizesInputAndOutputTextures` to provide `VividSkyData` in frame context
- Verify pass no longer owns cubemap lifecycle

**New file**: `Tests/Editor/SkySpecularCacheTests.cs`

- `Update_AllocatesRTHandle_WhenSourceIsNew`
- `Update_ReusesHandle_WhenSkyHashUnchanged`
- `Dispose_ReleasesHandle`

---

## Phase 3 — Physically Based Sky V1

**Goal**: Rayleigh/Mie single-scattering sky dome driven by main directional light.

### 3.1 `PhysicallyBasedSkyVolume`

**New file**: `Runtime/RenderPipeline/Sky/PhysicallyBasedSkyVolume.cs`

```csharp
namespace VividRP.Runtime
{
    [Serializable]
    [VolumeComponentMenu("VividRP/Physically Based Sky")]
    public sealed class PhysicallyBasedSkyVolume : VolumeComponent, IPostProcessComponent
    {
        [Header("Planet")]
        public MinFloatParameter planetRadius = new(6371000f, 100f);
        public MinFloatParameter atmosphereThickness = new(100000f, 1000f);
        public ColorParameter groundAlbedo = new(new Color(0.3f, 0.3f, 0.3f));

        [Header("Air (Rayleigh)")]
        public MinFloatParameter airDensity = new(1f, 0f);
        public ColorParameter airScatteringColor = new(new Color(0.175f, 0.409f, 1f));

        [Header("Aerosol (Mie)")]
        public MinFloatParameter aerosolDensity = new(0.5f, 0f);
        public ClampedFloatParameter aerosolAnisotropy = new(0.8f, -1f, 1f);

        [Header("Rendering")]
        public MinFloatParameter exposure = new(1f, 0f);
        public BoolParameter renderSunDisk = new(true);
        public MinFloatParameter sunDiskSize = new(1f, 0f);

        public bool IsActive() => airDensity.value > 0f || aerosolDensity.value > 0f;
    }
}
```

### 3.2 `PhysicallyBasedSkyRenderer`

**New file**: `Runtime/RenderPipeline/Sky/PhysicallyBasedSkyRenderer.cs`

```csharp
namespace VividRP.Runtime
{
    internal sealed class PhysicallyBasedSkyRenderer : ISkyRenderer
    {
        public SkyType Type => SkyType.PhysicallyBased;

        public bool IsActive()
        {
            var vol = VolumeManager.instance.stack?.GetComponent<PhysicallyBasedSkyVolume>();
            return vol != null && vol.IsActive();
        }

        public int GetSkyHash()
        {
            // Hash volume params + main light direction
        }

        public SphericalHarmonicsL2 EvaluateDiffuseSH()
        {
            // Generate SH from atmosphere model
            // Sample sky radiance in ~64 directions using the atmosphere model
            // Project into SH L2
        }

        public Cubemap GetSpecularCubemap()
        {
            // Phase 3 V1: render to a small runtime cubemap (64x64 per face)
            // using the atmosphere shader
            return m_RuntimeSkyCubemap;
        }

        // ...
    }
}
```

### 3.3 `PhysicallyBasedSkyPass`

**New file**: `Runtime/RenderPass/Core/PhysicallyBasedSkyPass.cs`

```csharp
namespace VividRP.Runtime.RenderPass.Core
{
    public class PhysicallyBasedSkyPass : RasterPass
    {
        [RenderGraphResource(Name = "Color", Access = AccessFlags.ReadWrite, AttachmentIndex = 0)]
        private RenderGraphTexture m_ColorTarget;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.ReadWrite, IsDepthAttachment = true)]
        private RenderGraphTexture m_DepthTexture;

        private Material m_Material;

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_Material = CoreUtils.CreateEngineMaterial(resources.PhysicallyBasedSkyShader);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var skyData = frameData.Get<VividSkyData>();
            if (skyData.activeSkyType != SkyType.PhysicallyBased) return;
            // Read PhysicallyBasedSkyVolume params
            // Read main directional light direction from VividLightData
            // Cache as material properties
        }

        public override void Record(RasterGraphContext context)
        {
            // Bind atmosphere params
            // Bind sun direction from main directional light
            // Fullscreen draw
        }

        public override void Dispose() { /* destroy material */ }
    }
}
```

### 3.4 Atmosphere shader

**New file**: `Shaders/Core/Private/PhysicallyBasedSky.shader`

Single-pass fullscreen shader:

```
- Vert: fullscreen triangle, output positionCS
- Frag:
    1. Compute view direction from PixelCoordToViewDirWS
    2. Ray-sphere intersection with atmosphere boundary
    3. March along view ray (16-32 steps)
    4. At each step:
       - Compute Rayleigh + Mie optical depth
       - Compute light transmittance to sun (secondary ray, 4-8 steps)
       - Accumulate in-scattered light
    5. Apply exposure and tint
    6. Optional: render sun disk via smoothstep around sun direction
```

### 3.5 Register in SkyManager

In `SkyManager.Initialize()`:

```csharp
RegisterRenderer(new HDRISkyRenderer(), resources);
RegisterRenderer(new PhysicallyBasedSkyRenderer(), resources);
```

### 3.6 Register shader resource

In `VividRPCoreResources`:

```csharp
[ResourcePath("Shaders/Core/Private/PhysicallyBasedSky")]
public Shader PhysicallyBasedSkyShader;
```

### 3.7 RenderGraph editor node

After adding `PhysicallyBasedSkyPass`, regenerate `GeneratedRenderPassNodes.g.cs` via the existing `RenderPassNodeRegistryGenerator`.

### 3.8 Tests

- `PhysicallyBasedSkyVolumeTests` — `IsActive` behavior
- `PhysicallyBasedSkyRendererTests` — hash, active state, SH generation
- `PhysicallyBasedSkyPassTests` — resource layout, prepare behavior
- `PhysicallyBasedSkyPassNodeTests` — port definitions

### 3.9 New files summary

```
Runtime/RenderPipeline/Sky/
    PhysicallyBasedSkyVolume.cs
    PhysicallyBasedSkyRenderer.cs
Runtime/RenderPass/Core/
    PhysicallyBasedSkyPass.cs
Shaders/Core/Private/
    PhysicallyBasedSky.shader
Tests/Editor/
    PhysicallyBasedSkyVolumeTests.cs
    PhysicallyBasedSkyRendererTests.cs
    PhysicallyBasedSkyPassTests.cs
    PhysicallyBasedSkyPassNodeTests.cs
```

---

## Phase 4 — LUT-based Atmosphere & Aerial Perspective

**Goal**: Precompute atmosphere LUTs for performance; add aerial perspective for scene objects.

### 4.1 Atmosphere LUTs

**New file**: `Runtime/RenderPass/Core/AtmosphereLUTPass.cs` (ComputePass)

Precompute three 2D LUTs each frame (or on sky change):

| LUT | Dimensions | Content |
|---|---|---|
| Transmittance | 256 x 64 | Optical depth integral from any altitude/angle to atmosphere edge |
| Multi-scattering | 32 x 32 | Second-order scattering contribution |
| Sky-view | 192 x 108 | Final sky radiance parameterized by view direction |

**New file**: `Shaders/Core/Private/AtmosphereLUT.compute`

Three kernels:

```hlsl
#pragma kernel TransmittanceLUT
#pragma kernel MultiScatteringLUT
#pragma kernel SkyViewLUT
```

### 4.2 Use LUTs in `PhysicallyBasedSkyPass`

Replace per-pixel raymarching with sky-view LUT lookup:

```hlsl
// In PhysicallyBasedSky.shader Frag:
float3 skyColor = SAMPLE_TEXTURE2D(_SkyViewLUT, sampler_SkyViewLUT, SkyViewUV(viewDir)).rgb;
```

### 4.3 Aerial Perspective

**New file**: `Runtime/RenderPass/Core/AerialPerspectivePass.cs` (ComputePass or RasterPass)

**New file**: `Shaders/Core/Private/AerialPerspective.compute`

- Input: scene depth buffer, transmittance LUT, multi-scattering LUT
- Output: aerial perspective volume texture (or per-pixel)
- Applied after deferred lighting, before post-processing

Two approaches (choose based on quality/perf):

1. **Per-pixel**: Sample transmittance LUT at scene depth, blend fog color
2. **Froxel volume**: 3D texture (e.g. 160x90x64), sample in lighting pass

### 4.4 Height Fog integration

Extend `PhysicallyBasedSkyVolume` with:

```csharp
[Header("Height Fog")]
public BoolParameter enableHeightFog = new(false);
public MinFloatParameter fogBaseHeight = new(0f, -10000f);
public MinFloatParameter fogDensity = new(0.01f, 0f);
public MinFloatParameter fogMaxDistance = new(5000f, 0f);
```

Or introduce a separate `AtmosphericFogVolume`.

### 4.5 New files summary

```
Runtime/RenderPass/Core/
    AtmosphereLUTPass.cs
    AerialPerspectivePass.cs
Shaders/Core/Private/
    AtmosphereLUT.compute
    AerialPerspective.compute (or .shader)
Tests/Editor/
    AtmosphereLUTPassTests.cs
    AerialPerspectivePassTests.cs
```

---

## File Change Matrix

| Phase | New Files | Modified Files |
|---|---|---|
| 0 | 7 runtime + 1 context + 2 test | `VividRenderPipeline.cs`, `PassRecorder.Execution.cs`, `HDRISkyPass.cs`, `DeferredLightingPass.cs`, `VividVolumeManagerUtility.cs`, existing tests |
| 1 | 1 runtime + 2 test | `ShaderVariablesGlobal.cs`, `ShaderVariablesGlobal.hlsl`, `HdrpLitLighting.hlsl`, `HDRISkyRenderer.cs` |
| 2 | 1 runtime + 1 test | `DeferredLightingPass.cs`, `SkyManager.cs` |
| 3 | 3 runtime + 1 shader + 4 test | `VividRPCoreResources.cs`, `SkyManager.cs`, `GeneratedRenderPassNodes.g.cs` (regen) |
| 4 | 2 runtime + 2 shader + 2 test | `PhysicallyBasedSkyPass.cs`, `PhysicallyBasedSkyVolume.cs` |

---

## Recommended Execution Order

```
Phase 0.1-0.6  SkyType + SkySettingsVolume + ISkyRenderer + HDRISkyRenderer + VividSkyData
Phase 0.7      SkyManager
Phase 0.8      Wire into pipeline + passes
Phase 0.9-0.10 Tests → verify no visual regression
──────────────────────────────────────────────────────────
Phase 1.1-1.2  SkyDiffuseSHUtility + HDRISkyRenderer.EvaluateDiffuseSH
Phase 1.3-1.4  ShaderVariablesGlobal SH fields (C# + HLSL)
Phase 1.5      Shader consumption (VividSampleSkySH)
Phase 1.6      Tests
──────────────────────────────────────────────────────────
Phase 2.1-2.2  SkySpecularCache + DeferredLightingPass cleanup
Phase 2.3      (Future) GGX prefilter slot
Phase 2.4      Tests
──────────────────────────────────────────────────────────
Phase 3.1-3.3  PB Sky volume + renderer + pass
Phase 3.4      Atmosphere shader (single-pass raymarch)
Phase 3.5-3.8  Registration + tests
──────────────────────────────────────────────────────────
Phase 4.1-4.2  LUT precompute + sky-view lookup
Phase 4.3      Aerial perspective
Phase 4.4      Height fog integration
Phase 4.5      Tests
```

---

## Key Design Decisions

### Why `VividSkyData` as ContextItem instead of passes reading volumes directly

- Passes should not know which sky type is active
- Sky update frequency is decoupled from frame frequency
- Multiple passes (HDRISkyPass, DeferredLightingPass, future forward pass) consume the same data without redundant volume queries

### Why CPU-side SH first instead of GPU compute

- HDRI cubemap is already on CPU (imported asset)
- SH projection at 8x8 per face = 384 samples — trivial CPU cost
- No async readback complexity
- Easy to test with NUnit
- GPU path can be added later for PBR sky where cubemap is GPU-generated

### Why not use `RenderSettings.ambientProbe`

- `RenderSettings.ambientProbe` is a global singleton; it would conflict with multi-camera setups or editor previews
- VividRP should own its environment state via `VividSkyData` + `ShaderVariablesGlobal`
- Optional: sync `RenderSettings.ambientProbe = skyData.diffuseSH` for compatibility with third-party shaders, but do not depend on it as input

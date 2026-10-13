# Conservative meshlet culling

`GPUMeshletCulling.compute` uses the same affine-box support predicate as instance
culling. Meshlet metadata supplies a local sphere, so the default box is its
enclosing local cube. VSM geometry-bound variants use their tighter auxiliary
local AABB. Neither path approximates a sheared instance by the largest transformed
radius axis. Existing buffer layouts, draw counters, material-facing conventions,
VSM page queries and the first-pass/recovery queue contracts are unchanged.

The reference is the local UE checkout:

- `E:/UnrealEngine/Engine/Shaders/Private/Nanite/NaniteClusterCulling.usf`:
  `FBoxCull` and cluster `FrustumHZB`.
- `NaniteCullingCommon.ush`: previous/current view HZB and near-plane protection.
- `NaniteHZBCull.ush`: projected box bounds and inclusive reversed-Z depth tests.

This adapts those geometric rules to VividRP's existing two-stage occlusion pass;
it does not port Nanite's resource layout or streaming system. The local-space
normal cone remains a meshoptimizer-specific stage.

## Precision and rejection rules

The target guarantees reversed Z and the D3D/Vulkan clip-depth interval [0, W].
There is no forward-Z branch or runtime depth-convention parameter. The pyramid
reduces with `min`, padding uses clear depth zero, and equality remains visible.
Projection and depth use FP32 throughout.

The projected rectangle bounds all eight corners of the affine box. Projection
error bounds include magnitudes before model/view translation cancellation; the
rectangle and closest depth expand toward visibility, with two additional pixels
of guard. Queries touching a clip-depth boundary, crossing W=0 or extending outside
the historical viewport remain visible. The latter cannot safely clamp to the
border of the previous frame's depth evidence. Mip selection covers the rectangle
with at most four loads; missing mips and truncated odd-dimension tails also retain
geometry. A large rectangle can be tested when a covering mip exists.

Frustum support uses strict `float16_t`/`float16_t3` arithmetic on normalized axes
and bounded plane normals, with the shared 1/32 outward error allowance. World
coordinates, matrix transforms, plane distances and offsets remain FP32.

The normal cone and viewing rays stay in object space under nonuniform scale,
shear and reflection. The material's original culling side is used because raster
renderer-list selection already compensates mirrored winding. The perspective
test uses meshoptimizer's sufficient sphere-cone condition, enlarged for camera
transform error. Only the normalized direction/axis dot uses FP16, with a 1/128
absolute margin covering half conversion/arithmetic and denormal flushing.
Invalid/degenerate cones and ambiguous boundaries remain visible. Double-sided
materials skip cone rejection. These allowances can retain extra geometry.

## Validation

From the package directory:

```powershell
./Tools~/Validate-MeshletCulling.ps1
./Tools~/Validate-InstanceFrustumCulling.ps1
```

The meshlet command compiles all 12 production kernels (including VSM variants
and depth reduction), checks production DXIL for native `f16` dot instructions,
and executes the shared predicates on D3D12. It requires DXC, clang-cl, Windows
SDK/linker, Python 3, and a GPU with native 16-bit operations. It resolves Editor
includes through Bee, or accepts `-UnityCgIncludes`. Outputs default to
`Temp~/meshlet-culling`; include junctions created by the script are removed
without traversing their targets.

24,640 deterministic cases compare GPU readback to independent double-precision
corner projection and angular cone oracles starting from the same FP32 inputs.
They cover all frustum planes, shear/reflection, extreme scales, large translation
cancellation, perspective/orthographic views, near-plane intersections, historical
viewport edges, equality/clear/invalid depth, odd mip tails and invalid metadata.
Controls require both rejection and retention, so an always-visible predicate
cannot pass. The FP32 diagnostic variant keeps the same outward margins.

Both native FP16 and diagnostic FP32 passed on RTX 5070 Ti with no false frustum,
cone or occlusion rejections and no rectangle/depth containment failures. The
shared instance predicate is separately regressed by its existing 41,024 cases.
The command also compiles six LOD-build kernels and checks 16,448 parent/child
LOD selection cases; see `GPUDrivenClusterLOD.md` for the Camera_1 roof gap fix
and its live D3D12 GameView verification. These checks do not establish general
temporal occlusion/VSM integration or frame-time improvement; no performance
improvement is inferred from the presence of FP16 instructions.

# Conservative instance frustum culling

`GPUInstanceCulling.compute` tests the affine image of each instance's local
AABB against the six inward world-space planes in `VividGPUCullingContext`.
The implementation follows the box-extrema approach used by
`E:/UnrealEngine/Engine/Shaders/Private/Nanite/NaniteHZBCull.ush`
(`BoxCullFrustumOrtho` / `BoxCullFrustumPerspective`) and Nanite's `FBoxCull`.
VividRP only needs a visibility decision at this stage, so it uses the equivalent
plane-support test without constructing a projected screen rectangle or dividing
by clip W. HZB, VSM page filtering, pass masks and LOD job emission retain their
existing responsibilities.

Given world-space center C and transformed half-extent axes X, Y, Z, the maximum
signed distance of the box to plane (N, D) is:

```
dot(N, C) + D + abs(dot(N, X)) + abs(dot(N, Y)) + abs(dot(N, Z))
```

Reject only when an outward error bound on this maximum is strictly negative.
This covers mirrored transforms, nonuniform scales and shear without the old
maximum-column-length sphere approximation. Contact is retained. Perspective,
orthographic, asymmetric and reversed-Z views use the existing CPU-extracted
planes; a zero near plane continues to mean disabled near clipping. Invalid
bounds, non-affine transforms and invalid planes are conservatively retained.

## Strict FP16 arithmetic

The support calculation uses `float16_t` and `float16_t3`, with
`#pragma use_dxc` and `#pragma require native16bit`. Production compilation
requires native 16-bit shader arithmetic (DXC SM 6.2 with
`-enable-16bit-types` for standalone D3D12 checks). There is no minimum-precision
type or hardware fallback variant. This requirement follows Unity's
[native 16-bit shader guidance](https://docs.unity3d.com/cn/current/Manual/UpgradeGuideUnity6.html).

Bounds, matrices, positions, translations and plane offsets stay FP32. Only
half-extent axes normalized by their largest absolute component and bounded
plane normals enter FP16. Thus even large worlds never put world positions into
FP16's limited exponent range. Custom planes with normal components exceeding
one use the FP32 support calculation.

The normalized support is expanded by 1/32 before scaling back to world units.
For round-to-nearest FP16, with unit roundoff u = 2^-11, nine bounded products
(including two operand conversions), six within-dot additions and two final
additions contribute less than 57u to absolute error at first order. Higher-order
terms and half denormal flushing fit inside the remaining margin. An additional
FP32 allowance is `4e-6 * (dot(abs(N), worldMagnitude) + abs(D)) + 1e-30`, where
worldMagnitude includes the magnitudes before transform/translation cancellation.
Ambiguous boundary cases remain visible. This deliberately trades some extra
work near boundaries for conservative rejection.

Light-sphere tests and older sphere-based VSM page tests receive a conservative
sphere enclosing the transformed box. Its radius uses scaled lengths to avoid
squared-length overflow/underflow. Main-view culling skips constructing this
sphere when no light-sphere test is requested. GPU buffer fields, offsets,
strides and draw/job counter contracts are unchanged.

## Validation

From the package directory:

```powershell
./Tools~/Validate-InstanceFrustumCulling.ps1
```

Requires DXC, clang-cl, Windows SDK/linker, Python 3 and a D3D12 GPU with native
16-bit operations. Editor includes are resolved from the containing project's
Bee references, or supplied using `-UnityCgIncludes`. Results go to
`Temp~/instance-frustum` by default. Temporary include links point to the actual
package sources; the script removes only links it created.

The script compiles all eight production kernels with their declared defines,
checks production DXIL for strict `f16` dot products, and runs the exact shared
predicate in a standalone D3D12 compute dispatch. An independent Python oracle
transforms all eight corners using double precision from the same FP32 inputs.
41,024 cases cover all six planes, contact, disabled near clipping, degenerate
and invalid bounds, mirrors, shear, very small/large scales, large translations,
and perspective off-center views. Both native FP16 and an FP32 diagnostic variant
must retain every oracle-visible box and reject clearly outside control cases.
The light/page sphere must also contain every transformed corner.

On the RTX 5070 Ti, both variants passed with zero false rejections and zero
sphere-containment failures. The boundary-heavy corpus intentionally contains
many uncertain cases retained by the error allowance. This is correctness and
compiled-instruction evidence, not a scene frame-time measurement. Unity import,
camera/CSM/VSM visual checks and production GPU timing remain separate checks.

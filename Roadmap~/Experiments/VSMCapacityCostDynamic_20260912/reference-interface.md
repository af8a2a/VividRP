# Independent geometric reference interface

`VSMBaselineReference.compute.txt` is an archived diagnostic shader, not a runtime import. Its two kernels compile with DXC `cs_6_5`, `SHADER_API_D3D11=1`, `UNITY_COMPILER_DXC=1`, `-enable-16bit-types`, the package include aliases and Unity 6000.7.0a6 `Data/Resources/CGIncludes`. Both kernel compilations passed on 2026-09-12. Dispatch validation and measured scene results belong in the capture report; compilation alone does not validate geometry.

The ordered 64-bucket retention and overflow-lower-bound algorithm also passed 800 deterministic CPU permutation/duplicate cases against independent full `sorted(set(...))` results (seed 20260912, candidate set sizes 0/1/15/16/17/63/64/65/100/200). This checks the bounded-set algorithm, not GPU triangle intersection accuracy.

## Common RTAS contract

Bind `_ReferenceAcceleration` to both kernels. Include every enabled shadow caster in scope, including off-camera geometry. Do not reuse receiver-camera culling for reference geometry. Match camera and main-light layer eligibility. Disable triangle face culling for the initial opaque reference and explicitly exclude alpha silhouettes from accuracy assertions: these kernels treat every included triangle as opaque and skip procedural primitives. They do not evaluate alpha tests, material WPO/PDO, terrain, or skin deformation automatically. Dynamic reference geometry must contain the actual deformed mesh of the captured frame.

Use manual RTAS management with per-instance masks:

- `1`: static VSM pool.
- `2`: dynamic VSM pool.

**Observed Unity 6000.7.0a6 API trap:** `AddInstance(renderer, null, false, false, 2)` returned one instance in the diagnostic but produced no triangle hit. Do not pass null submesh flags or treat `GetInstanceCount()` as evidence of usable geometry. Supply an array of exactly `mesh.subMeshCount`, each eligible submesh set to `RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly`. The explicit-flags Renderer route passed with both InternalErrorShader and StandardLit, ruling out the fixture material as the cause of that empty-reference result. This is an observed behavior of the tested editor version, not a claim about all Unity versions.

The corrected `reference-update-gpu-command.json` and `reference-update-gpu-result.txt` contain 18 actual GPU checks: explicit-flags Renderer with each of two materials and remove/re-add `RayTracingMeshInstanceConfig`, each using transform motion and mesh deformation, each requiring blocked → clear → blocked. All 18 passed. The config route specifies submesh flags and `DynamicGeometryManualUpdate`, removes the previous instance handle, adds the current mesh/current transform, calls `UpdateInstanceGeometry(handle)`, builds and then traces. It is a verified diagnostic fallback when direct Renderer tracking is unsuitable. These tests instantiate temporary current-scene objects and destroy them in `finally`; they do not create, switch or save scenes.

For eligible `MeshletRenderer`, static means `r.gameObject.isStatic && !r.sourceWasSkinned`. This follows `VividMeshletRendererDatabase.cs:771,776` and `VividPrimitiveSceneAdapter.cs:284`. Production draw-set eligibility must also be matched; inactive, invalid and shadow-disabled primitives must be excluded. Plain Unity Renderer compatibility draws belong in the dynamic pool. Avoid double-adding the source Renderer when an enabled MeshletRenderer takes it over. Store and update each RTAS instance transform every changed capture frame, rebuild/update deforming BLAS geometry as needed, then enqueue the TLAS build before the reference dispatch in the same command buffer. A static TLAS from the first frame invalidates moving-camera/caster/light comparisons when the geometry itself changes.

Receiver visibility is a separate requirement from RTAS inclusion: the active render graph must actually write the fixture's depth **and** GBuffer normal before VSM resolve. In this project's tested graph, the compatibility PreDepth pass exists but the ordinary GBuffer pass is absent; only the meshlet VisibilityBufferGBufferResolve path writes the relevant normal. A plain Renderer can therefore produce depth while its normal remains clear/invalid. Do not accept such a fixture for moving-receiver quality metrics. Validate actual world position, normal, fixture coverage and a known shadow transition before the full trajectory capture.

The receiver kernel forces opaque traversal and accepts the first committed triangle. The capacity kernel instead forces **non-opaque traversal solely to enumerate all triangle intersections**: it never commits a candidate. It still treats all candidate triangles as real opaque geometry for counting, without evaluating material opacity. Do not interpret the different traversal flag as alpha-tested ground truth.

## `ReferenceReceivers`

Dispatch `(ceil(gridWidth/8), ceil(gridHeight/8), 1)` once per captured production frame. Output is `gridWidth × gridHeight`, random-write `R32G32B32A32_SFloat`.

| Binding | Meaning |
| --- | --- |
| `_ReferenceDepthTexture` | Production frame's device depth, same texture passed to receiver resolve |
| `_ReferenceGBuffer1` | Production normal buffer, normal decoded from `.xy` |
| `_ReferenceVisibility` | Output `(softBias0, softBias1, centerHardBias0, centerHardBias1)` |
| `_BaselineShadowTexture` | Actual production filtered directional shadow at this captured frame; copied without rerunning SMRT |
| `_BaselineHistory` | Actual current written RGBA16F history texture; bind a fallback even when disabled |
| `_BaselineHasHistory` | 1 when the bound history is this frame's valid output, otherwise 0 |
| `_BaselineSignal` | Additional grid-sized RGBA32F output `(actualFilteredVisibility, currentHistoryAgeOr0, deviceDepth, valid)` |
| `_BaselineWorld` | Additional grid-sized RGBA32F output `(worldX, worldY, worldZ, decodedNormalY)` for opaque/floor ROI selection |
| `_ReferenceInvViewProjection` | `cameraData.GetGPUViewProjectionMatrix(true).inverse` for this frame |
| `_ReferenceScreenWidth`, `_ReferenceScreenHeight` | Full production depth dimensions |
| `_ReferenceROIOriginX`, `_ReferenceROIOriginY` | First sampled **integer source pixel**, no hidden offset |
| `_ReferenceGridWidth`, `_ReferenceGridHeight` | Output dimensions |
| `_ReferencePixelStride` | Source pixel stride, at least 1; quarter-grid uses 4 |
| `_ReferenceRayCount` | Complete angular sample count per frame, normally 256 or 1024 |
| `_ReferenceNormalBias` | Vector with world normal offsets in `.x,.y`; recommended 0.001, 0.01 metres |
| `_ReferenceLightDirection` | Normalized direction toward the directional light, `-light.transform.forward` |
| `_ReferenceSunBasisX`, `_ReferenceSunBasisY` | Orthogonal unit basis perpendicular to light direction; same basis constructor as the existing independent ray-traced directional shadow |
| `_ReferenceTanAngularRadius` | `tan(angularDiameterDegrees * Deg2Rad * 0.5)` |
| `_ReferenceRayMaxDistance` | Geometric search length; 1000 metres for the current enclosed Sponza reference; report this finite bound |

Example to reproduce the previous quarter-grid convention: origin `(2,2)`, stride `4`, grid `(screenWidth/4, screenHeight/4)`. Sampled pixel is `origin + outputPixel * stride`, reconstructed from `(pixel + 0.5)/screenSize`. `ComputeWorldSpacePosition` matches production; **do not add a manual Y flip**. Sky or out-of-range pixels output reference `(-1,-1,-1,-1)`, signal validity 0, world 0 and must be excluded from metrics. Dispatch after production temporal/filter completion so shadow and history refer to the same frame. All three output textures are mandatory bindings for this kernel.

The sequence is a complete fixed Hammersley disk each frame, independent of the production stochastic sequence, SMRT rules, page storage and temporal history. The 256-point and 1024-point disks are different complete point sets because radial stratification uses total count. Capture both on representative frames to report reference convergence; do not call 256 samples exact ground truth. Both normal offsets are sensitivity probes: pixels where their answers disagree materially should be reported separately rather than silently choosing the more favorable reference. Shading normal, rather than a mesh geometric normal, sets this reference origin offset.

For dynamic comparisons freeze each requested trajectory state through production render and reference dispatch, then advance exactly once after capture. Record camera/light/object transforms and production frame index. Compare fixed 4, fixed 8 and adaptive at identical trajectory states and warm-up lengths; maintain separate histories per run. Receiver reference is raw geometric visibility, while production filtered visibility includes spatial filtering. Report raw-vs-reference and filtered-vs-reference separately, and do not attribute every filtered edge difference to ray accuracy.

## `ReferenceCapacity`

Let `S = _CapacitySamplesPerPageAxis`, `P = physicalPageCapacity`, and `N = P*S*S`. Dispatch `(ceil(S/8), ceil(S/8), P)`. Recommended starting `S=8` uniformly samples 64 texel centers per allocated physical slot. The shader verifies owner/page-table reciprocity and skips unowned slots. Sampling is deterministic and **does not prove that unsampled texels fit 16 layers**.

Bind the production resources after their frame's raster/clear/resolve sequence:

- `_VSMProjections`, `_VSMProjectionCount`.
- `_VSMPrototypePhysicalPageOwners`, `_VSMPrototypePageTable`.
- `_VSMPrototypeStaticPhysicalPage`, `_VSMPrototypeDynamicPhysicalPage` (the 16-slice uint textures).
- `_VSMPrototypePageSize`, `_VSMPrototypePagesPerAxis`, `_VSMPrototypeVirtualResolution`, `_VSMPrototypePhysicalPagesPerRow`, `_VSMPrototypePhysicalPageCapacity`, `_VSMPrototypePageTableEntryCount`.
- `_CapacitySamplesPerPageAxis = S`, `_CapacityStaticMask = 1`, `_CapacityDynamicMask = 2`.
- `_CapacityUniqueDepthToleranceWorld = 0.0001` metres, `_CapacityProductionMatchToleranceWorld = 0.001` metres initially. Record both and assess tolerances with the actual clipmap span and raster bias.

Output buffers:

| Buffer | Count × stride | Layout per element |
| --- | --- | --- |
| `_CapacityCounts` | `2*N × 16` bytes | `uint4(triangleCandidates, uniqueDepthLowerBound, productionOccupied, productionMatched)` |
| `_CapacityDiagnostics` | `2*N × 16` bytes | `float4(maxNearestWorldError, meanNearestWorldError, missingReferenceNear16, flags)` |
| `_CapacityReferenceDepths` | `2*N*64 × 4` bytes | 64 sorted geometric distances from the near plane per pool/sample, unused slots `-1` |

For `P=256, S=8`, these buffers total 9 MiB: 0.5 MiB counts, 0.5 MiB diagnostics and 8 MiB geometric depths. Index `sampleIndex=(physicalSlot*S+sampleY)*S+sampleX`; pool element `outputIndex=sampleIndex*2+pool`, with static pool 0 and dynamic pool 1. Reference depth at `outputIndex*64+layer`.

Local texel is `floor((sampleXY+0.5)*pageSize/S)`. Owner encodes virtual page index plus one; virtual page/level and physical atlas coordinates can be reconstructed from the archived owner buffer and captured layout. The diagnostic ray follows that texel's light axis from projection near to far plane. This uses the existing directional projection's affine, orthogonal world-to-shadow rows and reversed-depth convention on D3D12, rather than SMRT intersection code.

### Unique-depth counting and limitations

`triangleCandidates` counts actual triangle intersections emitted by inline ray query, including coincident/duplicate triangles. It is **not** a unique-layer count. The shader quantizes ray distance to `floor(t/epsilon+0.5)` and keeps the nearest 64 distinct integer buckets in sorted order, storing the minimum actual distance within each bucket. Effective epsilon is:

`max(requestedEpsilon, 1e-7, projectionDepthSpan/16777214)` metres.

The span-dependent term keeps bucket indices in exactly representable float-integer range. This can exceed 0.1 mm for large depth ranges; calculate and archive actual epsilon per projection. Bucket membership is independent of candidate traversal order. Surfaces in one bucket merge; surfaces closer than epsilon can fall on opposite bucket boundaries. Thus this is an explicit numerical definition, not topology reconstruction or a general geometric thickness guarantee.

Counts `0..64` are exact numbers of observed distinct buckets if traversal finished without a 65th bucket. When any additional distinct bucket appears, the shader reports `uniqueDepthLowerBound=65` and retains the nearest 64 buckets; it never counts later unretained duplicates again as distinct surfaces. A count above 16 is capacity evidence **for that particular pool**. Combining static and dynamic counts and testing against 16 would be wrong: each production pool independently stores 16 depths.

`productionOccupied` counts nonzero production slices; `productionMatched` counts production depths whose nearest retained geometric depth is within the configured world tolerance. `missingReferenceNear16` counts the closest up-to-16 reference depths without a production depth within tolerance. These proximity matches are not one-to-one assignment. Depths are decoded from production normalized depth back to near-plane world distance. Mismatches can arise from raster footprint/face culling, alpha treatment, caster eligibility, bias, stale geometry/cache, or quantization; they are not automatically proof of overflow. Use opaque interior samples and inspect archived depths before assigning cause.

`flags` is a float-encoded integer bitmask: bit 0 valid owned sample, bit 1 at least 65 unique buckets, bit 2 production nonempty/reference empty, bit 3 reference nonempty/production empty. Max/mean nearest error are `-1` when either set is empty. Unowned slots have zero counts, flags 0 and `-1` reference depths.

Report occupancy (actual production full texels), sampled geometric capacity (`uniqueDepthLowerBound>16` by pool), truncated-reference frequency, missing-near-surface diagnostics and finite sampling coverage separately. Geometry reference does not observe discarded atomic fragments directly and cannot replace production overflow instrumentation. Run capacity/reference captures separately from production timing: RTAS builds, hundreds of geometric rays, output buffers and readbacks substantially perturb GPU work.

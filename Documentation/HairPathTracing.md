# Chiang Hair Path Tracing

VividRP Hair uses RTXCR's Chiang near-field BCSDF material implementation with a VividRP-owned DOTS geometry backend. Static strands form the V1 baseline; the same mesh contract can now carry previous-frame centerlines for deforming-strand motion vectors.

## Supported Path

- Shader: `VividRP/Material/Hair`
- Geometry: DOTS mesh, four triangles per line segment, with optional dynamic frame history
- Reference material pass: `ReferencedPathtracingDXR`
- Ray-tracing GBuffer pass: `RaytracingGBufferDXR`
- Lighting: unified analytic-light and environment NEE, BSDF sampling, multi-bounce transport and MIS
- Denoising: existing ReBLUR and DLSS Ray Reconstruction inputs

Hair energy is classified as specular in V1. Chiang TT/TRT lobes can cross the radial normal, but they do not enter VividRP's closed-solid material medium stack.

## Creating Validation Assets

Use `VividRP > Hair > Create Chiang Validation Assets` in the Unity Editor. The command creates:

- `Assets/VividRPValidation/Hair/ChiangHairValidationDots.asset`
- `Assets/VividRPValidation/Hair/ChiangHairValidation.mat`

Assign both assets to a `MeshFilter` and `MeshRenderer` in a scene rendered by the Reference Path Tracing graph. The Hair shader intentionally contains only ray-tracing passes in V1, so it is not a raster hair preview shader.

The validation asset is a small curved, tapered bundle. It is intended for geometry, lobe, self-intersection and denoiser-guide checks, not performance measurements representative of a production groom.

## DOTS Mesh Contract

`HairDotsMeshBuilder` consumes independent line segments with endpoint position, radius and UV. Each segment writes twelve independent vertices:

- `POSITION`: expanded strip surface position
- `NORMAL`: signed centerline offset axis
- `TANGENT.xyz`: centerline tangent
- `TEXCOORD0.xy`: strand UV
- `TEXCOORD1.x`: volume-compensated radius
- `TEXCOORD1.y`: endpoint coordinate, 0 at the start and 1 at the end
- `TEXCOORD2.xyz`: previous-frame endpoint centerline position in object space
- `TEXCOORD2.w`: previous-frame volume-compensated radius

Radius scale is applied while building the mesh. Changing only a material property cannot resize DOTS correctly because expanded positions and stored radii must remain synchronized.

The ray-tracing hit shader reconstructs the tapered segment centerline, intersects the analytic tapered body near the committed triangle hit, and outputs corrected position, radial normal, tangent and radius. The next-event and continuation ray origin helpers use the radius to skip the second orthogonal strip when crossing the fiber.

Static builds write the current centerline and radius into `TEXCOORD2`, so
they retain camera and object-transform motion without a separate asset path.
Deforming integrations call `HairDotsMeshBuilder.BuildDynamic(current,
previous, target)` after simulation and before RTAS construction. Current and
previous arrays must have identical segment ordering and topology. On a
history reset, teleport, or groom swap, pass current data for both frames.

## Material Parameters

The dedicated Hair material inspector divides the ShaderLab contract into
Absorption, Scattering, Fiber Interface, and Emission sections. The serialized
property names remain stable so materials created before the inspector was
introduced continue to load without migration.

- Absorption Model: Color, Physical Melanin, or Normalized Melanin
- Absorption Color, shown for the Color model
- Melanin Concentration and Melanin Redness, shown for both physical models
- Longitudinal roughness `beta_m` and azimuthal roughness `beta_n`
- Cuticle angle in degrees
- Fiber IOR and analytical/Schlick Fresnel selection
- Optional HDR emission

The defaults match the RTXCR sample's Chiang defaults. Color parameters are consumed in scene-linear space by the BCSDF adapter.

`HairInput.hlsl` loads and sanitizes ShaderLab values into a single
`VividHairMaterialData` value. The Chiang adapter, Reference Path Tracing
closest-hit, and Raytracing GBuffer closest-hit all consume that same value.
The editor material adapter applies the same ranges, repairs non-finite legacy
values, rounds enum/toggle fields, tracks the material contract version, and
synchronizes the emissive global-illumination flag.

## DLSS Ray Reconstruction Guides

The Hair GBuffer closest-hit writes corrected fiber position, radial normal, longitudinal roughness, base color and dielectric F0.

For motion, the hit shader reconstructs the previous centerline at the current
`segmentU`, transports the radial surface coordinate into the previous strand
frame, applies the previous radius, and transforms the result with
`UNITY_PREV_MATRIX_M`. The GBuffer payload uses that material-supplied position
for previous clip-space and view-depth calculations. Camera motion, object
transform, centerline deformation/rotation, and radius changes therefore share
one motion-vector path.

Animated meshes must participate in RTAS as dynamic geometry, and their vertex
update must complete before `RTASBuildPass`. The package currently provides the
frame/mesh contract, not a groom simulation scheduler.

## Current Limitations

- Dynamic DOTS requires caller-managed current/previous frames and topology reset
- No glTF or groom importer
- No raster lighting or raster shadow pass
- No far-field Hair BCSDF
- No built-in groom simulation scheduler
- No procedural AABB or native LSS backend
- NRD strand material ID/thickness specialization is not enabled yet

Raw Reference PT accumulation remains the correctness baseline. DLSS-RR and ReBLUR outputs are previews and must be checked against the raw result when tuning Hair guide semantics.

## Source Notice

The vendored RTXCR material files individually carry an MIT permission notice and retain their original headers. Their source commit is recorded in `Shaders/Material/Hair/Vendor/RTXCR/NOTICE.md`. RTXCR geometry and sample glue are not vendored.

#ifndef VIVID_SDF_SURFACE_CACHE_LIGHTING_INCLUDED
#define VIVID_SDF_SURFACE_CACHE_LIGHTING_INCLUDED

uint ResolveSdfSkyMiss(float3 origin, float3 direction, float tracedDistance, bool incomplete, uint screenState,
    VividMeshSdfReceiver receiver)
{
    // A depth hit without readable history still blocks the sky on this ray.
    if (screenState != 0u)
        return RayMiss | RaySkyOccluded;
    if (incomplete)
        return RayMiss | RaySkyUnresolved;

    VividMeshSdfNode root = _MeshSdfNodes[0];
    float entry = min(tracedDistance, _SdfDiffuseSkyTraceDistance);
    float exit = FLT_MAX;
    if (!VividMeshSdfClipRay(origin, direction, root.boundsMin, root.boundsMax, entry, exit))
        return RaySkyVisible;

    float limit = min(exit, _SdfDiffuseSkyTraceDistance);
    if (entry <= limit)
    {
        VividMeshSdfHit unused;
        bool advancedReceiver;
        if (tracedDistance > 0.0)
            receiver = (VividMeshSdfReceiver)0;
        if (VividTraceMeshSdfSceneInternal(origin, direction, entry, limit, _SdfDiffuseLayerMask,
            0.0, true, receiver, unused, advancedReceiver, incomplete))
            return RayMiss | RaySkyOccluded;
    }
    return !incomplete && exit <= _SdfDiffuseSkyTraceDistance ? RaySkyVisible : (RayMiss | RaySkyUnresolved);
}

float ResolveSunShadowBias(VividMeshSdfField field, VividMeshSdfInstance instance, float3 normalWS)
{
    if (field.signMode != 2u)
        return _SdfDiffuseRayBias;

    // The hit is already on the outside of the baked shell. A shorter normal
    // offset keeps the sun ray near the real surface at contact and shadow edges.
    float localUnits = length(float3(dot(instance.worldToLocal0.xyz, normalWS),
        dot(instance.worldToLocal1.xyz, normalWS), dot(instance.worldToLocal2.xyz, normalWS)));
    float minBias = 0.25 * field.originAndVoxelSize.w / max(localUnits, 1e-6);
    return min(_SdfDiffuseRayBias, max(0.4 * _SdfDiffuseRayBias, minBias));
}

#if defined(VIVID_SDF_SUN_UPDATE) || defined(VIVID_SDF_RADIOSITY_UPDATE)
bool ProjectCardPoint(VividMeshSdfInstance instance, VividMeshSdfField field, float3 local, float3 normalLS,
    float3 normalWS, out float3 position, out float3 geometryNormal)
{
    if (field.signMode == 2u)
        local += normalLS * (0.5 * field.shellThickness);

    // Invert the linear part of the world-to-local transform without uploading a second transform.
    float3 r0 = instance.worldToLocal0.xyz;
    float3 r1 = instance.worldToLocal1.xyz;
    float3 r2 = instance.worldToLocal2.xyz;
    float3 c0 = cross(r1, r2);
    float3 c1 = cross(r2, r0);
    float3 c2 = cross(r0, r1);
    float3 delta = local - float3(instance.worldToLocal0.w, instance.worldToLocal1.w, instance.worldToLocal2.w);
    position = (c0 * delta.x + c1 * delta.y + c2 * delta.z) / dot(r0, c0);
    geometryNormal = 0.0;
    if (!all(isfinite(position)))
        return false;

    // The captured mesh and the SDF isosurface can disagree by a fraction of a voxel.
    float localUnits = length(float3(dot(r0, normalWS), dot(r1, normalWS), dot(r2, normalWS)));
    float reach = 1.75 * field.originAndVoxelSize.w / max(localUnits, 1e-6);
    float3 start = position + normalWS * reach;
    float distance;
    float3 gridPosition;
    if (!VividMeshSdfTraceInstance(field, instance, start, -normalWS, 0.0, 2.0 * reach, distance, gridPosition))
        return false;
    float3 gradient = VividMeshSdfGradient(field, gridPosition);
    geometryNormal = SafeNormalize(gradient.x * r0 + gradient.y * r1 + gradient.z * r2);
    position = start - normalWS * distance;
    return dot(geometryNormal, normalWS) >= 0.95;
}
#endif

#if defined(VIVID_SDF_CACHED_SUN)
Texture2D<float> _SdfSunVisibility;
RWTexture2D<float4> _SdfSunCacheHits;
uint _SdfSunReadyCards;

float LoadCachedSunVisibility(DiffuseSurface surface, uint3 cardTexel, float surfaceError,
    VividMeshSdfHit hit, float3 lightingNormal)
{
    uint card = ((uint)surface.cardExtent.w - 1u) * 6u + cardTexel.z;
    uint2 pixel = cardTexel.xy;
    if (cardTexel.z >= VividSdfBaseCardCount || card >= _SdfSunReadyCards || any(pixel == 0u) || any(pixel + 1u >= uint2(_SdfSunLayout.xy))
        || dot(hit.normalWS, lightingNormal) < 0.95
        || dot(hit.normalWS, _SdfDiffuseLightPositionRange.xyz) <= 0.0)
        return -1.0;
    VividMeshSdfField field = _MeshSdfFields[_MeshSdfInstances[hit.instanceIndex].fieldIndex];
    if (surfaceError > 0.5 * (field.originAndVoxelSize.w + field.shellThickness))
        return -1.0;

    int2 uv = int2(VividSdfSunAtlasPixel(card, pixel));
    float visibility = _SdfSunVisibility.Load(int3(uv, 0));
    // Keep the exact hit shadow test at shadow transitions and card gaps.
    if (visibility < 0.0
        || _SdfSunVisibility.Load(int3(uv + int2(-1, 0), 0)) != visibility
        || _SdfSunVisibility.Load(int3(uv + int2(1, 0), 0)) != visibility
        || _SdfSunVisibility.Load(int3(uv + int2(0, -1), 0)) != visibility
        || _SdfSunVisibility.Load(int3(uv + int2(0, 1), 0)) != visibility)
        return -1.0;
    return visibility;
}
#endif

float3 TraceHitLighting(float3 position, float3 normal, VividMeshSdfHit hit, DiffuseSurface surface,
    float cachedVisibility)
{
    float3 radiance = max(surface.emission.rgb, 0.0);
    float range = _SdfDiffuseLightPositionRange.w;
    VividMeshSdfInstance instance = _MeshSdfInstances[hit.instanceIndex];
    if (range == 0.0 || (instance.layerMask & _SdfDiffuseLightLayerMask) == 0u)
        return radiance;

    bool directional = range < 0.0;
    float3 direction = _SdfDiffuseLightPositionRange.xyz;
    float attenuation = 1.0;
    if (!directional)
    {
        float3 toLight = direction - position;
        float distanceSquared = dot(toLight, toLight);
        if (distanceSquared <= 1e-6 || distanceSquared >= range * range)
            return radiance;
        direction = toLight * rsqrt(distanceSquared);
        float relativeDistance = distanceSquared / (range * range);
        float window = saturate(1.0 - relativeDistance * relativeDistance);
        attenuation = window * window / max(distanceSquared, 0.01);
    }
    float nDotL = dot(normal, direction);
    if (nDotL <= 0.0)
        return radiance;
    if (dot(hit.normalWS, direction) <= 0.0)
        return radiance;

    float shadow = 1.0;
    float strength = _SdfDiffuseLightColorShadow.w;
    if (cachedVisibility >= 0.0)
        shadow -= strength * (1.0 - cachedVisibility);
    else if (strength > 0.0)
    {
        float bias = _SdfDiffuseRayBias;
        if (directional && hit.signMode == 2u)
            bias = ResolveSunShadowBias(_MeshSdfFields[instance.fieldIndex], instance, hit.normalWS);
        float3 origin = position + hit.normalWS * bias;
        float3 shadowDirection = direction;
        float shadowDistance = -range;
        if (!directional)
        {
            float3 shadowRay = _SdfDiffuseLightPositionRange.xyz - origin;
            shadowDistance = length(shadowRay);
            shadowDirection = shadowRay / max(shadowDistance, 1e-6);
        }
        if (shadowDistance > _SdfDiffuseRayBias
            && VividOccludeMeshSdfScene(origin, shadowDirection, shadowDistance, _SdfDiffuseLightLayerMask))
            shadow -= strength;
    }

    // Incident directional illuminance is unexposed. Lambertian outgoing radiance
    // is albedo * E / PI; the gather's cosine PDF cancels its receiver BRDF / PI.
    return radiance + saturate(surface.albedoAndValid.rgb) * max(_SdfDiffuseLightColorShadow.rgb, 0.0)
        * (INV_PI * nDotL * attenuation * shadow);
}

#if defined(VIVID_SDF_SURFACE_CACHE)
// Keep the atlas dimensions in sync with SdfDiffuseGatherPass.
static const uint RadiosityCardSize = 8u;
static const uint RadiosityColumns = 16u;
static const uint RadiosityRays = 16u;
StructuredBuffer<uint> _SdfRadiosityWork;
Texture2D<float4> _SdfRadiosityInput;
uint _SdfRadiosityLayout; // Number of published cards, or zero until a sweep is complete.

uint2 RadiosityAtlasPixel(uint card, uint2 pixel)
{
    return uint2(card % RadiosityColumns, card / RadiosityColumns) * RadiosityCardSize + pixel;
}

uint2 RadiosityCardPixel(uint2 pixel, uint2 size)
{
    return min((2u * pixel + 1u) * size / (2u * RadiosityCardSize), size - 1u);
}

bool TryLoadCardIndirect(DiffuseSurface surface, VividMeshSdfHit hit, uint3 cardTexel, out float3 radiance)
{
    radiance = 0.0;
    [branch]
    if (_SdfRadiosityLayout == 0u)
        return false;
    uint first = _SdfRadiosityWork[(uint)surface.cardExtent.w];
    if (first >= _SdfRadiosityLayout || cardTexel.z >= _SdfRadiosityLayout - first)
        return false;

    uint card = first + cardTexel.z;
    uint slice, axis;
    float3 boundsMin, extent;
    VividGetSdfCardProjection(surface, cardTexel.z, slice, axis, boundsMin, extent);
    uint2 size;
    uint slices;
    _SdfSurfaceAlbedoDepth.GetDimensions(size.x, size.y, slices);
    float depth = _SdfSurfaceAlbedoDepth.Load(int4(cardTexel.xy, slice, 0)).a;
    float3 normal = SafeNormalize(_SdfSurfaceNormalCoverage.Load(int4(cardTexel.xy, slice, 0)).xyz * 2.0 - 1.0);
    VividMeshSdfField field = _MeshSdfFields[_MeshSdfInstances[hit.instanceIndex].fieldIndex];
    float depthTolerance = max(1.5 * field.originAndVoxelSize.w, extent[axis] / 512.0);
    float2 p = (float2(cardTexel.xy) + 0.5) * (RadiosityCardSize / float2(size)) - 0.5;
    int2 lower = int2(floor(p));
    float2 fraction = frac(p);
    float3 sum = 0.0;
    float total = 0.0;
    [unroll]
    for (uint tap = 0u; tap < 4u; tap++)
    {
        uint2 offset = uint2(tap & 1u, tap >> 1u);
        uint2 cell = uint2(clamp(lower + int2(offset), 0, int(RadiosityCardSize) - 1));
        float4 sample = _SdfRadiosityInput.Load(int3(RadiosityAtlasPixel(card, cell), 0));
        if (sample.a == 0.0 || abs(sample.a - 1.0 - depth) * extent[axis] > depthTolerance)
            continue;
        uint2 representative = RadiosityCardPixel(cell, size);
        float3 sampleNormal = SafeNormalize(_SdfSurfaceNormalCoverage.Load(int4(representative, slice, 0)).xyz * 2.0 - 1.0);
        if (dot(normal, sampleNormal) < 0.9)
            continue;
        float2 w = lerp(1.0 - fraction, fraction, float2(offset));
        float weight = w.x * w.y;
        sum += sample.rgb * weight;
        total += weight;
    }

    // Cached E / PI becomes outgoing radiance after exactly one albedo multiply.
    // Bounce sweeps are finite; do not darken high-reflectance channels to force
    // contraction or apply another / PI to an already cosine-integrated cache.
    radiance = saturate(surface.albedoAndValid.rgb) * (sum / max(total, 1e-5));
    return total > 0.0;
}

float3 LoadCardIndirect(DiffuseSurface surface, VividMeshSdfHit hit, uint3 cardTexel)
{
    float3 radiance;
    TryLoadCardIndirect(surface, hit, cardTexel, radiance);
    return radiance;
}

#if defined(VIVID_SDF_RECEIVER)
bool TryLoadScreenCardIndirect(uint2 pixel, float3 position, float3 normal, float3 direction,
    out float3 radiance, inout VividMeshSdfHit matchedHit)
{
    radiance = 0.0;
    uint index = _SdfReceiverVisibility.Load(int3(pixel, 0)).x - VIVID_VISIBILITY_BUFFER_INSTANCE_ID_BIAS;
    if (index >= _SdfReceiverCount)
        return false;
    uint3 source = _SdfReceiverEntities[index].xyz;
    if (source.z == 0xffffffffu)
        return false;

    VividMeshSdfInstance instance = _MeshSdfInstances[source.z];
    if (any(instance.entityId != source.xy) || (instance.layerMask & _SdfDiffuseLayerMask) == 0u)
        return false;
    VividMeshSdfField field = _MeshSdfFields[instance.fieldIndex];
    DiffuseSurface surface = _SdfDiffuseSurfaces[source.z];
    float3 r0 = instance.worldToLocal0.xyz;
    float3 r1 = instance.worldToLocal1.xyz;
    float3 r2 = instance.worldToLocal2.xyz;
    float3 c0 = cross(r1, r2);
    float3 c1 = cross(r2, r0);
    float3 c2 = cross(r0, r1);
    VividMeshSdfHit hit = (VividMeshSdfHit)0;
    hit.instanceIndex = source.z;
    hit.entityId = source.xy;
    hit.signMode = field.signMode;
    hit.normalWS = normal;
    // Transform the raster normal by the inverse transpose of world-to-local.
    hit.normalLS = SafeNormalize(float3(dot(c0, normal), dot(c1, normal), dot(c2, normal)) * sign(dot(r0, c0)));

    float3 cardNormal;
    uint3 cardTexel;
    float surfaceError;
    if (!VividTrySampleSdfSurface(hit, position, surface, cardNormal, cardTexel, surfaceError)
        || surfaceError > 0.75 * field.originAndVoxelSize.w
        || dot(cardNormal, normal) < 0.75 || dot(cardNormal, direction) >= -0.05
        || !TryLoadCardIndirect(surface, hit, cardTexel, radiance))
        return false;
    matchedHit = hit;
    return true;
}
#endif
#endif

#endif

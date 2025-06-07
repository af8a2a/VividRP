#ifndef UNITY_RAYTRACING_FRAGINPUTS_INCLUDED
#define UNITY_RAYTRACING_FRAGINPUTS_INCLUDED

#ifdef FRAG_INPUTS_ENABLE_STRIPPING
    #error "FragInputs stripping not supported and not needed for ray tracing"
#endif

struct FragInputs
{
    // Contain value return by SV_POSITION (That is name positionCS in PackedVarying).
    // xy: unormalized screen position (offset by 0.5), z: device depth, w: depth in view space
    // Note: SV_POSITION is the result of the clip space position provide to the vertex shaders that is transform by the viewport
    float4 positionSS; // In case depth offset is use, positionRWS.w is equal to depth offset
    float3 positionRWS; // Relative camera space position
    float3 positionPredisplacementRWS; // Relative camera space position
    float2 positionPixel;              // Pixel position (VPOS)

    float4 texCoord0;
    float4 texCoord1;
    float4 texCoord2;
    float4 texCoord3;

    #ifdef FRAG_INPUTS_USE_INSTANCEID
        uint instanceID;
    #endif

    #ifdef FRAG_INPUTS_USE_SIX_WAY_DIFFUSE_GI_DATA
        float4 diffuseGIData[3];
    #endif

    float4 color; // vertex color

    // TODO: confirm with Morten following statement
    // Our TBN is orthogonal but is maybe not orthonormal in order to be compliant with external bakers (Like xnormal that use mikktspace).
    // (xnormal for example take into account the interpolation when baking the normal and normalizing the tangent basis could cause distortion).
    // When using tangentToWorld with surface gradient, it doesn't normalize the tangent/bitangent vector (We instead use exact same scale as applied to interpolated vertex normal to avoid breaking compliance).
    // this mean that any usage of tangentToWorld[1] or tangentToWorld[2] outside of the context of normal map (like for POM) must normalize the TBN (TCHECK if this make any difference ?)
    // When not using surface gradient, each vector of tangentToWorld are normalize (TODO: Maybe they should not even in case of no surface gradient ? Ask Morten)
    float3x3 tangentToWorld;

    uint primitiveID; // Only with fullscreen pass debug currently - not supported on all platforms

    // For two sided lighting
    bool isFrontFace;

    // append a substruct for custom interpolators to be copied correctly into SDI from Varyings.
    // #if defined(USE_CUSTOMINTERP_SUBSTRUCT)
    //     CustomInterpolators customInterpolators;
    // #endif

    // Append an additional substruct for VFX interpolators. Eventually, we should merge this with custom interpolators.
    // #if defined(HAVE_VFX_MODIFICATION)
    //     FragInputsVFX vfx;
    // #endif
};

void AdjustFragInputsToOffScreenRendering(inout FragInputs input, bool offScreenRenderingEnabled, float offScreenRenderingFactor)
{
    // We need to readapt the SS position as our screen space positions are for a low res buffer, but we try to access a full res buffer.
    input.positionSS.xy = offScreenRenderingEnabled ? (uint2)round(input.positionSS.xy * offScreenRenderingFactor) : input.positionSS.xy;
    input.positionPixel = offScreenRenderingEnabled ? (uint2)round(input.positionPixel * offScreenRenderingFactor) : input.positionPixel;
}


void BuildFragInputsFromIntersection(IntersectionVertex currentVertex, out FragInputs outFragInputs)
{
    float3 rayDirection = WorldRayDirection();
    outFragInputs.positionSS = float4(0.0, 0.0, 0.0, 0.0);
    outFragInputs.positionPixel = float2(0.0, 0.0);
    outFragInputs.positionRWS = WorldRayOrigin() + rayDirection * RayTCurrent();
    outFragInputs.texCoord0 = currentVertex.texCoord0;
    outFragInputs.texCoord1 = currentVertex.texCoord1;
    outFragInputs.texCoord2 = currentVertex.texCoord2;
    outFragInputs.texCoord3 = currentVertex.texCoord3;
    outFragInputs.color = currentVertex.color;

#ifdef FRAG_INPUTS_USE_INSTANCEID
    #if UNITY_ANY_INSTANCING_ENABLED
        const int localBaseInstanceId = unity_BaseInstanceID;
    #else
        const int localBaseInstanceId = 0;
    #endif
    outFragInputs.instanceID = InstanceIndex() - localBaseInstanceId;
#endif

    // Compute the world space normal
    float3 normalWS = normalize(mul(currentVertex.normalOS, (float3x3)WorldToObject3x4()));
    float3 tangentWS = normalize(mul(currentVertex.tangentOS.xyz, (float3x3)WorldToObject3x4()));
    outFragInputs.tangentToWorld = CreateTangentToWorld(normalWS, tangentWS, sign(currentVertex.tangentOS.w));

    outFragInputs.isFrontFace = dot(rayDirection, outFragInputs.tangentToWorld[2]) < 0.0f;
}

uint GetCurrentVertexAndBuildFragInputs(AttributeData attributeData, out IntersectionVertex currentVertex, out FragInputs outFragInputs)
{
    uint currentFrameIndex = 0; //Used for VFX

    #ifdef HAVE_VFX_MODIFICATION
    ZERO_INITIALIZE(IntersectionVertex, currentVertex);
    BuildFragInputsFromVFXIntersection(attributeData, outFragInputs, currentFrameIndex);
    #else
    GetCurrentIntersectionVertex(attributeData, currentVertex);
    // Build the Frag inputs from the intersection vertice
    BuildFragInputsFromIntersection(currentVertex, outFragInputs);
    #endif

    return currentFrameIndex;
}

#if defined(SURFACE_GRADIENT) || defined(DECAL_NORMAL_BLENDING)
void GetNormalWS_SG(FragInputs input, float3 normalTS, out float3 normalWS, float3 doubleSidedConstants)
{
#ifdef _DOUBLESIDED_ON
    // Flip the displacements (the entire surface gradient) in the 'flip normal' mode.
    float flipSign = input.isFrontFace ? 1.0 : doubleSidedConstants.x;
    normalTS *= flipSign;
#endif

    normalWS = SurfaceGradientResolveNormal(input.tangentToWorld[2], normalTS);
}
#endif

// This function convert the tangent space normal/tangent to world space and orthonormalize it + apply a correction of the normal if it is not pointing towards the near plane
void GetNormalWS(FragInputs input, float3 normalTS, out float3 normalWS, float3 doubleSidedConstants)
{
#if defined(SURFACE_GRADIENT)
    GetNormalWS_SG(input, normalTS, normalWS, doubleSidedConstants);
#else

    #ifdef _DOUBLESIDED_ON
    float flipSign = input.isFrontFace ? 1.0 : doubleSidedConstants.x;
    normalTS.xy *= flipSign;
    #endif // _DOUBLESIDED_ON

    // We need to normalize as we use mikkt tangent space and this is expected (tangent space is not normalized)
    normalWS = SafeNormalize(TransformTangentToWorld(normalTS, input.tangentToWorld));
#endif
}


// This function takes a world space src normal + applies a correction to the normal if it is not pointing towards the near plane.
void GetNormalWS_SrcWS(FragInputs input, float3 srcNormalWS, out float3 normalWS, float3 doubleSidedConstants)
{
#ifdef _DOUBLESIDED_ON
    srcNormalWS = (!input.isFrontFace && doubleSidedConstants.z < 0) ? srcNormalWS + 2 * input.tangentToWorld[2] * max(0, -dot(input.tangentToWorld[2], srcNormalWS)) : srcNormalWS;
    normalWS = (!input.isFrontFace && doubleSidedConstants.x < 0) ? reflect(-srcNormalWS, input.tangentToWorld[2]) : srcNormalWS;
#else
    normalWS = srcNormalWS;
#endif
}

// This function converts an object space normal to world space + applies a correction to the normal if it is not pointing towards the near plane.
void GetNormalWS_SrcOS(FragInputs input, float3 srcNormalOS, out float3 normalWS, float3 doubleSidedConstants)
{
    float3 srcNormalWS = TransformObjectToWorldNormal(srcNormalOS);
    GetNormalWS_SrcWS(input, srcNormalWS, normalWS, doubleSidedConstants);
}


#endif
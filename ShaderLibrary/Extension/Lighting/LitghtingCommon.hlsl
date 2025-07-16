


#define DEFERRED_LIGHTING_TILE_SIZE (16)
#define DEFERRED_LIGHTING_GROUP_SIZE (DEFERRED_LIGHTING_TILE_SIZE / 2)
#define DEFERRED_LIGHTING_THREADS   (64)
#define HasShadingModel(stencilVal) ((stencilVal >> SHADINGMODELS_USER_MASK_BITS) > 0)
#define StencilToShadingModel(stencilVal) (stencilVal & SHADINGMODELS_MODELS_MASK)



struct DeferredLightingOutput
{
    float3 diffuseLighting;
    float3 specularLighting;

};


//--------------------------------------------------------------------------------------------------
// Implementation Shading Models: Lit
//--------------------------------------------------------------------------------------------------

// Shading data decode from gbuffer
struct ShadingData
{
    float3 normalWS;

    float3 albedo;
    float metallic;
    float occlusion;
    float smoothness;
    uint materialFlags;

    float perceptualRoughness;
    float roughness;
    float roughness2;

    float3 diffuseColor;
    float3 fresnel0;

    #ifdef _LIGHT_LAYERS
    uint meshRenderingLayers;
    #endif
};

ShadingData DecodeShadingDataFromGBuffer(PositionInputs posInput)
{
    ShadingData shadingData;
    ZERO_INITIALIZE(ShadingData, shadingData);

    float4 gbuffer0 = LOAD_TEXTURE2D_X(_GBuffer0, posInput.positionSS);
    float4 gbuffer1 = LOAD_TEXTURE2D_X(_GBuffer1, posInput.positionSS);
    float4 gbuffer2 = LOAD_TEXTURE2D_X(_GBuffer2, posInput.positionSS);

    // Unpack GBuffer informations. Init datas.
    // See UnityGBuffer for more information.
    // GBuffer0: diffuse           diffuse         diffuse         materialFlags   (sRGB rendertarget)
    // GBuffer1: metallic/specular specular        specular        occlusion
    // GBuffer2: encoded-normal    encoded-normal  encoded-normal  smoothness
    shadingData.normalWS = normalize(UnpackNormal(gbuffer2.xyz));

    shadingData.albedo = gbuffer0.rgb;
    shadingData.metallic = MetallicFromReflectivity(gbuffer1.r); // TODO: handle with Specular Metallic and setup.
    shadingData.occlusion = gbuffer1.a;
    shadingData.smoothness = gbuffer2.a;
    shadingData.materialFlags = UnpackMaterialFlags(gbuffer0.a);

    shadingData.perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(shadingData.smoothness);
    shadingData.roughness = PerceptualRoughnessToRoughness(shadingData.perceptualRoughness);
    // We need to max this with Angular Diameter, which result in minRoughness.
    shadingData.roughness2 = max(shadingData.roughness * shadingData.roughness, FLT_MIN);

    shadingData.diffuseColor = ComputeDiffuseColor(shadingData.albedo, shadingData.metallic);
    shadingData.fresnel0 = ComputeFresnel0(shadingData.albedo, shadingData.metallic, DEFAULT_SPECULAR_VALUE);

    // #ifdef _LIGHT_LAYERS
    // float4 renderingLayers = LOAD_TEXTURE2D_X(MERGE_NAME(_, GBUFFER_LIGHT_LAYERS), posInput.positionSS);
    // shadingData.meshRenderingLayers = DecodeMeshRenderingLayer(renderingLayers.r);
    // #endif

    return shadingData;
}




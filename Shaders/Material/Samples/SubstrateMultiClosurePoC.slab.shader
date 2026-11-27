// @vivid-base "Packages/com.vivid.render-pipelines/Shaders/Material/Templates/SubstrateOpaque.slab.template"
// @vivid-graph "Packages/com.vivid.render-pipelines/Runtime/Resources/MaterialGraphs/OpenPBROpaque.vmatg"
// @vivid-wpo-padding 0.025

Shader "VividRP/Material/Samples/SubstrateMultiClosurePoC"
{
    Properties
    {
        VIVID_SLAB_GRAPH_PROPERTIES
    }

    // @vivid-vertex-begin
    // Programmable World Position Offset runs identically in PreDepth, ShadowCaster,
    // Raster GBuffer, GPUDriven VividVisibilityRaster, and GPUDriven Resolve barycentrics.
    #define VIVID_SLAB_HAS_VERTEX_OFFSET 1
    float3 VividSlabEvaluateVertexOffset(const VividSlabVertexContext ctx)
    {
        float wave = sin(ctx.positionWS.x * 3.0f + _Time.y * 2.0f) * 0.02f;
        return ctx.normalWS * wave;
    }
    // @vivid-vertex-end

    // @vivid-coverage-begin
    // Programmable Coverage/AlphaClip runs identically in PreDepth, ShadowCaster,
    // Raster GBuffer, and GPUDriven VividVisibilityRaster (automatically omits [earlydepthstencil]).
    #define VIVID_SLAB_HAS_CUSTOM_COVERAGE 1
    void VividSlabEvaluateCoverage(
        const VividSlabEvalContext ctx,
        out float alpha,
        out float cutoff)
    {
        float2 centered = abs(frac(ctx.uv0 * 4.0f) - 0.5f);
        alpha = 1.0f - max(centered.x, centered.y);
        cutoff = 0.55f;
    }
    // @vivid-coverage-end

    // @vivid-surface-begin
    // Custom OpenPBR V2 post-hook runs identically in Raster GBuffer and GPUDriven Compute.
    #define VIVID_SLAB_HAS_CUSTOM_POST_SURFACE 1
    void VividSlabPostEvaluateSurface(
        const VividSlabEvalContext ctx,
        inout VividOpenPBRV2Inputs v2,
        inout float ambientOcclusion)
    {
        v2.coatWeight = 0.5f;
        v2.coatRoughness = 0.08f;
        v2.coatIor = 1.5f;
        v2.coatColor = float3(1.0f, 1.0f, 1.0f);
        v2.fuzzWeight = 0.2f;
        v2.fuzzRoughness = 0.5f;
        v2.fuzzColor = float3(0.9f, 0.92f, 1.0f);
    }
    // @vivid-surface-end

    VIVID_SLAB_USE_BASE_SUBSHADER
}

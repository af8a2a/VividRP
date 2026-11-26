// @vivid-base "Packages/com.vivid.render-pipelines/Shaders/Material/Templates/SubstrateOpaque.slab.template"
// @vivid-graph "Packages/com.vivid.render-pipelines/Runtime/Resources/MaterialGraphs/OpenPBROpaque.vmatg"

Shader "VividRP/Material/Samples/SubstratePoC"
{
    Properties
    {
        VIVID_SLAB_GRAPH_PROPERTIES
    }

    // @vivid-surface-begin
    // Custom OpenPBR V2 post-hook runs identically in BOTH Raster GBuffer and GPUDriven Compute.
    #define VIVID_SLAB_HAS_CUSTOM_POST_SURFACE 1
    void VividSlabPostEvaluateSurface(
        const VividSlabEvalContext ctx,
        inout VividOpenPBRV2Inputs v2,
        inout float ambientOcclusion)
    {
        v2.coatWeight = 0.35f;
        v2.coatRoughness = 0.05f;
        v2.coatIor = 1.5f;
        v2.coatColor = float3(1.0f, 1.0f, 1.0f);
    }
    // @vivid-surface-end

    VIVID_SLAB_USE_BASE_SUBSHADER
}

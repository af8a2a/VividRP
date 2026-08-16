#ifndef VIVIDRP_EXPERIMENTAL_STANDARD_LIT_CLOSURE_BUFFER_PASS_INCLUDED
#define VIVIDRP_EXPERIMENTAL_STANDARD_LIT_CLOSURE_BUFFER_PASS_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/StandardLit/ExperimentalStandardLitInput.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/Closure/ExperimentalClosureBuffer.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/VividVertMesh.hlsl"

VividPackedVaryingsMesh Vert(VividAttributesMesh input)
{
    return PackVividVaryingsMesh(VividVertMesh(input));
}

VividExperimentalClosureBufferOutput FragClosureBuffer(
    VividPackedVaryingsMesh packedInput)
{
    UNITY_SETUP_INSTANCE_ID(packedInput);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(packedInput);

    FragInputs input = UnpackVividVaryingsMeshToFragInputs(packedInput);
    VividExperimentalStandardSurface surface =
        BuildExperimentalStandardLitSurface(input);
    VividExperimentalClosureMaterial material =
        VividCompileExperimentalStandardSurface(surface);
    return VividPackExperimentalClosureBuffer(surface, material);
}

#endif

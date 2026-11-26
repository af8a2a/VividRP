#ifndef VIVIDRP_STANDARD_LIT_GBUFFER_PASS_INCLUDED
#define VIVIDRP_STANDARD_LIT_GBUFFER_PASS_INCLUDED

#if defined(VIVIDRP_SHADERPASS_GBUFFER) && !defined(VIVID_VT_ENABLE_FEEDBACK_RW)
#define VIVIDRP_SUBSTRATE_GBUFFER_OUTPUT 1
#endif

#include "Packages/com.vivid.render-pipelines/Shaders/Material/StandardLit/StandardLitInput.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/VividShaderPassGBuffer.hlsl"

#endif

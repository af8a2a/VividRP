#ifndef VIVIDRP_EXPERIMENTAL_RAYTRACING_GBUFFER_PASS_INCLUDED
#define VIVIDRP_EXPERIMENTAL_RAYTRACING_GBUFFER_PASS_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/RaytracingGBufferCommon.hlsl"

#define VIVIDRP_INDIRECT_DIFFUSE_DEFINE_RAYTRACING_SHADERS 0
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/IndirectDiffuse.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/StandardLit/ExperimentalStandardLitOpenPBRAdapter.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/RaytracingGBuffer.hlsl"

#endif

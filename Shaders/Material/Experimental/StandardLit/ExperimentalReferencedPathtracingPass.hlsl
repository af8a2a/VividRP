#ifndef VIVIDRP_EXPERIMENTAL_REFERENCED_PATH_TRACING_PASS_INCLUDED
#define VIVIDRP_EXPERIMENTAL_REFERENCED_PATH_TRACING_PASS_INCLUDED

// Establish the dependencies required by the adapter before including the
// existing reference integrator. Include guards keep the second inclusion in
// ReferencedPathtracing.hlsl inert while its closest-hit implementation uses
// the experimental resolver supplied here.
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingCommon.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingLightList.hlsl"

#define VIVIDRP_REFERENCED_PATH_TRACING_USE_RTXTF 1
#define VIVIDRP_INDIRECT_DIFFUSE_DEFINE_RAYTRACING_SHADERS 0
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/IndirectDiffuse.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/ReferencedPathtracingRTXTF.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/StandardLit/ExperimentalStandardLitOpenPBRAdapter.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/ReferencedPathtracing.hlsl"

#endif

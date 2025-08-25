#ifndef SHADER_VARIABLES_RAYTRACING_INCLUDED
#define SHADER_VARIABLES_RAYTRACING_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/ShaderVariablesGlobal.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/RayTracingSystem.cs.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/ShaderVariablesRaytracing.cs.hlsl"


// The target acceleration acceleration structure should only be defined for non compute shaders
#ifndef SHADER_STAGE_COMPUTE
GLOBAL_RESOURCE(RaytracingAccelerationStructure, _RaytracingAccelerationStructure, RAY_TRACING_ACCELERATION_STRUCTURE_REGISTER);
#endif

RW_TEXTURE2D_ARRAY(uint, _RayCountTexture);

#endif /* SHADER_VARIABLES_RAYTRACING_INCLUDED */
#ifndef SHADER_RAYTRACING_COMMON_INCLUDED
#define SHADER_RAYTRACING_COMMON_INCLUDED

#if defined(SHADER_STAGE_RAY_TRACING)
float EvaluateRayTracingBias(float3 positionRWS)
{
    float distanceToCamera = length(positionRWS);
    float blend = saturate((distanceToCamera - _ProjectionParams.y) / (_ProjectionParams.z - _ProjectionParams.y));
    return lerp(_RayTracingRayBias, _RayTracingDistantRayBias, blend);
}
#endif

// Using this function instead of accessing the constant directly allows for overrides, in particular
// in Path Tracing where we want to change the sidedness behaviour based on the transparency mode.
float3 GetDoubleSidedConstants()
{
#ifdef _DOUBLESIDED_ON

    #if (SHADERPASS == SHADERPASS_PATH_TRACING)

        #if defined(_SURFACE_TYPE_TRANSPARENT) && (defined(_REFRACTION_PLANE) || defined(_REFRACTION_SPHERE))
            return 1.0; // Force to 'None'
        #else
            return _DoubleSidedConstants.z > 0.0 ? -1.0 : _DoubleSidedConstants.xyz; // Force to 'Flip' or 'Mirror'
        #endif

    #else // SHADERPASS_PATH_TRACING

        return _DoubleSidedConstants.xyz;

    #endif // SHADERPASS_PATH_TRACING

#else // _DOUBLESIDED_ON

    return 1.0;

#endif // _DOUBLESIDED_ON
}

// Heuristic mapping from roughness (GGX in particular) to ray spread angle
float roughnessToSpreadAngle(float roughness)
{
    // FIXME: The mapping will most likely need adjustment...
    return roughness * PI/8;
}

#endif /* SHADER_RAYTRACING_COMMON_INCLUDED */
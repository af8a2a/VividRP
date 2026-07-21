#ifndef VIVIDRP_OPENPBR_UNITY_HLSL_INTEROP_INCLUDED
#define VIVIDRP_OPENPBR_UNITY_HLSL_INTEROP_INCLUDED

// Adobe's Slang interop assumes GLSL-style vector splat constructors and
// user-defined struct constructors. Unity compiles this pass as HLSL through
// DXC, so keep the HLSL-specific compatibility surface renderer-owned.
#define OPENPBR_USE_CUSTOM_INTEROP 1
#define OPENPBR_ADDRESS_SPACE_THREAD
#define OPENPBR_OUT(type) out type
#define OPENPBR_INOUT(type) inout type
#define OPENPBR_CONST_REF(type) const type
#define OPENPBR_CONSTEXPR_LOCAL const
#define OPENPBR_CONSTEXPR_GLOBAL static const
#define OPENPBR_GENERAL_CONSTEXPR_FUNCTION
#define OPENPBR_LIMITED_CONSTEXPR_FUNCTION
#define OPENPBR_INLINE_FUNCTION

typedef float2 vec2;
typedef float3 vec3;
typedef float4 vec4;

#define OPENPBR_MAKE_VEC2_SPLAT(value) ((float2)(value))
#define OPENPBR_MAKE_VEC3_SPLAT(value) ((float3)(value))
#define OPENPBR_MAKE_VEC4_SPLAT(value) ((float4)(value))

// Unity currently compiles ray-tracing ShaderLab passes with legacy HLSL
// overload rules. A wrapper containing only one struct is structurally
// equivalent to that inner struct, which makes lobe overloads ambiguous.
#define OPENPBR_LEGACY_HLSL_WRAPPER_TYPE_TAG uint _openpbr_legacy_hlsl_wrapper_type_tag;

#define OPENPBR_UINT32 uint
#define OPENPBR_UINT16 uint
#define OPENPBR_ENERGY_TABLES_USE_UINT16 0

#ifndef mix
#define mix lerp
#endif
#ifndef equal
#define equal(a, b) ((a) == (b))
#endif
#ifndef notEqual
#define notEqual(a, b) ((a) != (b))
#endif
#ifndef greaterThan
#define greaterThan(a, b) ((a) > (b))
#endif
#ifndef greaterThanEqual
#define greaterThanEqual(a, b) ((a) >= (b))
#endif

#define OPENPBR_SWIZZLE(value, suffix) (value).suffix
#define OPENPBR_ASSERT(expression, message)
#define OPENPBR_ASSERT_UNREACHABLE(message)
#define OPENPBR_STATIC_ASSERT(expression, message)

// HLSL has no user-defined struct constructors. Forward declarations let the
// vendor call sites use typed factories whose definitions follow openpbr.h.
struct OpenPBR_Basis;
struct OpenPBR_AllCoefficients;
struct OpenPBR_AllCoefficientsAndProbabilities;
struct OpenPBR_ConstantReflectionCoefficient;
struct OpenPBR_IorReflectionCoefficient;
struct OpenPBR_AnisotropicGGXSmithVNDFMicrofacetDistribution;
struct OpenPBR_ComprehensiveReflectionTransmissionCoefficient;
struct OpenPBR_MinimalMicrofacetReflectionLobe_AnisotropicGGXSmithVNDFMicrofacetDistribution_IorReflectionCoefficient;
struct OpenPBR_MinimalMicrofacetReflectionLobe_AnisotropicGGXSmithVNDFMicrofacetDistribution_ConstantReflectionCoefficient;
struct OpenPBR_ComprehensiveMicrofacetReflectionTransmissionLobe;

OpenPBR_Basis VividOpenPBRMakeOpenPBR_Basis(vec3 tangent, vec3 bitangent, vec3 normal);
OpenPBR_AllCoefficients VividOpenPBRMakeOpenPBR_AllCoefficients(vec3 reflection, vec3 transmission);
OpenPBR_AllCoefficientsAndProbabilities VividOpenPBRMakeOpenPBR_AllCoefficientsAndProbabilities(
    vec3 reflection,
    vec3 transmission,
    float reflectionProbability,
    float transmissionProbability);
OpenPBR_ConstantReflectionCoefficient VividOpenPBRMakeOpenPBR_ConstantReflectionCoefficient(vec3 color);
OpenPBR_IorReflectionCoefficient VividOpenPBRMakeOpenPBR_IorReflectionCoefficient(float relativeIor);
OpenPBR_AnisotropicGGXSmithVNDFMicrofacetDistribution
VividOpenPBRMakeOpenPBR_AnisotropicGGXSmithVNDFMicrofacetDistribution(
    vec2 alpha,
    OpenPBR_Basis basis,
    float isotropicAlpha);
OpenPBR_ComprehensiveReflectionTransmissionCoefficient
VividOpenPBRMakeOpenPBR_ComprehensiveReflectionTransmissionCoefficient(
    vec3 etaTransparent,
    vec3 etaOpaque,
    vec3 transparentReflectionScale,
    vec3 opaqueReflectionScale,
    vec3 transmission,
    vec3 metalF0,
    vec3 metalF82Tint,
    float metalAmount,
    float thinFilmWeight,
    float thinFilmThicknessNm,
    float thinFilmExteriorIor,
    float thinFilmIor,
    vec3 thinFilmInteriorIor,
    vec3 rgbWavelengthsNm,
    vec3 thinWallReflectionAlbedo);
OpenPBR_MinimalMicrofacetReflectionLobe_AnisotropicGGXSmithVNDFMicrofacetDistribution_IorReflectionCoefficient
VividOpenPBRMakeOpenPBR_MinimalMicrofacetReflectionLobe_AnisotropicGGXSmithVNDFMicrofacetDistribution_IorReflectionCoefficient(
    vec3 normal,
    OpenPBR_AnisotropicGGXSmithVNDFMicrofacetDistribution distribution,
    OpenPBR_IorReflectionCoefficient coefficient);
OpenPBR_MinimalMicrofacetReflectionLobe_AnisotropicGGXSmithVNDFMicrofacetDistribution_ConstantReflectionCoefficient
VividOpenPBRMakeOpenPBR_MinimalMicrofacetReflectionLobe_AnisotropicGGXSmithVNDFMicrofacetDistribution_ConstantReflectionCoefficient(
    vec3 normal,
    OpenPBR_AnisotropicGGXSmithVNDFMicrofacetDistribution distribution,
    OpenPBR_ConstantReflectionCoefficient coefficient);
OpenPBR_ComprehensiveMicrofacetReflectionTransmissionLobe
VividOpenPBRMakeOpenPBR_ComprehensiveMicrofacetReflectionTransmissionLobe(
    vec3 normal,
    OpenPBR_AnisotropicGGXSmithVNDFMicrofacetDistribution distribution,
    OpenPBR_ComprehensiveReflectionTransmissionCoefficient coefficient,
    vec3 relativeIor,
    vec3 pathThroughput);

#define VIVIDRP_OPENPBR_STRUCT_FACTORY_IMPL(type) VividOpenPBRMake##type
#define VIVIDRP_OPENPBR_STRUCT_FACTORY(type) VIVIDRP_OPENPBR_STRUCT_FACTORY_IMPL(type)
#define OPENPBR_MAKE_STRUCT_1(type, arg1) VIVIDRP_OPENPBR_STRUCT_FACTORY(type)(arg1)
#define OPENPBR_MAKE_STRUCT_2(type, arg1, arg2) VIVIDRP_OPENPBR_STRUCT_FACTORY(type)(arg1, arg2)
#define OPENPBR_MAKE_STRUCT_3(type, arg1, arg2, arg3) VIVIDRP_OPENPBR_STRUCT_FACTORY(type)(arg1, arg2, arg3)
#define OPENPBR_MAKE_STRUCT_4(type, arg1, arg2, arg3, arg4) VIVIDRP_OPENPBR_STRUCT_FACTORY(type)(arg1, arg2, arg3, arg4)
#define OPENPBR_MAKE_STRUCT_5(type, arg1, arg2, arg3, arg4, arg5) VIVIDRP_OPENPBR_STRUCT_FACTORY(type)(arg1, arg2, arg3, arg4, arg5)
#define OPENPBR_MAKE_STRUCT_6(type, arg1, arg2, arg3, arg4, arg5, arg6) VIVIDRP_OPENPBR_STRUCT_FACTORY(type)(arg1, arg2, arg3, arg4, arg5, arg6)
#define OPENPBR_MAKE_STRUCT_7(type, arg1, arg2, arg3, arg4, arg5, arg6, arg7) VIVIDRP_OPENPBR_STRUCT_FACTORY(type)(arg1, arg2, arg3, arg4, arg5, arg6, arg7)
#define OPENPBR_MAKE_STRUCT_8(type, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8) VIVIDRP_OPENPBR_STRUCT_FACTORY(type)(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8)
#define OPENPBR_MAKE_STRUCT_9(type, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9) VIVIDRP_OPENPBR_STRUCT_FACTORY(type)(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9)
#define OPENPBR_MAKE_STRUCT_10(type, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10) VIVIDRP_OPENPBR_STRUCT_FACTORY(type)(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10)
#define OPENPBR_MAKE_STRUCT_11(type, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11) VIVIDRP_OPENPBR_STRUCT_FACTORY(type)(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11)
#define OPENPBR_MAKE_STRUCT_12(type, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12) VIVIDRP_OPENPBR_STRUCT_FACTORY(type)(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12)
#define OPENPBR_MAKE_STRUCT_13(type, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13) VIVIDRP_OPENPBR_STRUCT_FACTORY(type)(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13)
#define OPENPBR_MAKE_STRUCT_14(type, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14) VIVIDRP_OPENPBR_STRUCT_FACTORY(type)(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14)
#define OPENPBR_MAKE_STRUCT_15(type, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15) VIVIDRP_OPENPBR_STRUCT_FACTORY(type)(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15)

#endif

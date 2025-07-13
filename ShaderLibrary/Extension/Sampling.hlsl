


#ifndef GGX_BOUNDED_VNDF_SAMPLING
// "Bounded VNDF Sampling for Smith–GGX Reflections"
// Kenta Eto & Yusuke Tokuyoshi - Siggraph Asia 2023
// This paper further improves the method of Dupuy and Benyoub in the case where the microfacet normal is used for reflection only.
// It allows many fewer rays to be rejected at high roughness which improves quality. This is particularly noticeable
// in multi-bounce reflections of rough metals because longer paths can be followed without early termination.
#define GGX_BOUNDED_VNDF_SAMPLING 1
#endif

float VisibleGGXPDF(float3 V, float3 H, float a2, bool bLimitVDNFToReflection = true)
{
    float NoV = V.z;
    float NoH = H.z;
    float VoH = dot(V, H);

    float d = (NoH * a2 - NoH) * NoH + 1;
    float D = a2 / (PI*d*d);
    float k = 1.0;
    #if GGX_BOUNDED_VNDF_SAMPLING
    if (bLimitVDNFToReflection)
    {
        float s = 1.0f + length(V.xy);
        float s2 = s * s;
        k = (s2 - a2 * s2) / (s2 + a2 * V.z * V.z); // Eq. 5
    }
    #endif
    float PDF = 2 * VoH * D / (k * NoV + sqrt(NoV * (NoV - NoV * a2) + a2));
    return PDF;
}

float VisibleGGXPDF_aniso(float3 V, float3 H, float2 Alpha, bool bLimitVDNFToReflection = true)
{
    float NoV = V.z;
    float NoH = H.z;
    float VoH = dot(V, H);
    float a2 = Alpha.x * Alpha.y;
    float3 Hs = float3(Alpha.y * H.x, Alpha.x * H.y, a2 * NoH);
    float S = dot(Hs, Hs);
    float D = (1.0f / PI) * a2 * Sq(a2 / S);
    float LenV = length(float3(V.x * Alpha.x, V.y * Alpha.y, NoV));
    float k = 1.0;
    #if GGX_BOUNDED_VNDF_SAMPLING
    if (bLimitVDNFToReflection)
    {
        float a = saturate(min(Alpha.x, Alpha.y));
        float s = 1.0f + length(V.xy);
        float ka2 = a * a, s2 = s * s;
        k = (s2 - ka2 * s2) / (s2 + ka2 * V.z * V.z); // Eq. 5
    }
    #endif
    float Pdf = (2 * D * VoH) / (k * NoV + LenV);
    return Pdf;
}


// "Sampling Visible GGX Normals with Spherical Caps"
// Jonathan Dupuy & Anis Benyoub - High Performance Graphics 2023
void SampleAnisoGGXVisibleNormalSphericalCaps(float2 u,
                                 float3 V,
                                 float3x3 localToWorld,
                                 float roughnessX,
                                 float roughnessY,
                             out float3 localV,
                             out float3 localH,
                             out float  VdotH,
                                 bool bLimitVDNFToReflection)
{
    localV = mul(V, transpose(localToWorld));

    // Stretch view direction, Hemisphere Space
    float3 Vh = normalize(float3(roughnessX * localV.x, roughnessY * localV.y, localV.z));
    float Phi = (2 * PI) * u.x;
    float k = 1.0;

    #if GGX_BOUNDED_VNDF_SAMPLING
    if (bLimitVDNFToReflection)
    {
        // If we know we will be reflecting the view vector around the sampled micronormal, we can
        // tweak the range a bit more to eliminate some of the vectors that will point below the horizon
        float a = saturate(min(roughnessX, roughnessY));
        float s = 1.0 + length(V.xy);
        float a2 = a * a, s2 = s * s;
        k = (s2 - a2 * s2) / (s2 + a2 * V.z * V.z);
    }
    #endif

    float Z = lerp(1.0, -k * Vh.z, u.y);
    float SinTheta = sqrt(saturate(1 - Z * Z));
    float X = SinTheta * cos(Phi);
    float Y = SinTheta * sin(Phi);
    float3 H = float3(X, Y, Z) + Vh;

    // Transform the normal back to the Ellipsoid Space
    localH = normalize(float3(roughnessX * H.x, roughnessY * H.y, max(0.0, H.z)));

    VdotH = saturate(dot(localV, localH));
}


// GGX vsible normal Spherical Caps sampling, isotropic variant
void SampleGGXVisibleNormalSphericalCaps(float2 u,
                                         float3 V,
                                         float3x3 localToWorld,
                                         float roughness,
                                     out float3 localV,
                                     out float3 localH,
                                     out float  VdotH,
                                         bool bLimitVDNFToReflection = true)
{
    SampleAnisoGGXVisibleNormalSphericalCaps(u, V, localToWorld, roughness, roughness, localV, localH, VdotH, bLimitVDNFToReflection);
}


#define A_GPU
#define A_HLSL

#include "Packages/com.unity.render-pipelines.core/Runtime/PostProcessing/Shaders/ffx/ffx_a.hlsl"


// D65 xy coordinates.
#define lpmColD65 AF2(0.3127, 0.3290)
//------------------------------------------------------------------------------------------------------------------------------
// Rec709 xy coordinates, (D65 white point).
#define lpmCol709R  AF2(0.64, 0.33)
#define lpmCol709G  AF2(0.30, 0.60)
#define lpmCol709B  AF2(0.15, 0.06)
//------------------------------------------------------------------------------------------------------------------------------
// DCI-P3 xy coordinates, (D65 white point).
#define lpmColP3R  AF2(0.680, 0.320)
#define lpmColP3G  AF2(0.265, 0.690)
#define lpmColP3B  AF2(0.150, 0.060)
//-----------------------------------------------------------------------------------------------------------------------------
// Rec2020 xy coordinates, (D65 white point).
#define lpmCol2020R AF2(0.708, 0.292)
#define lpmCol2020G AF2(0.170, 0.797)
#define lpmCol2020B AF2(0.131, 0.046)


// HDR10 VARIABLES
// ===============
// hdr10S ... Use LpmHdr10<Raw|Scrgb>Scalar() to compute this value
//==============================================================================================================================
// CON   SOFT  CON2  CLIP  SCALEONLY
#define LPM_CONFIG_FS2RAW_709 false,false,true, true, false
#define LPM_COLORS_FS2RAW_709 lpmCol709R,lpmCol709G,lpmCol709B,lpmColD65,\
                               fs2R,fs2G,fs2B,fs2W,\
                               fs2R,fs2G,fs2B,fs2W,1.0
//------------------------------------------------------------------------------------------------------------------------------
// FreeSync2 min-spec is larger than sRGB, so using 709 primaries all the way through as an optimization.
// CON   SOFT  CON2  CLIP  SCALEONLY
#define LPM_CONFIG_FS2SCRGB_709 false,false,false,false,true
#define LPM_COLORS_FS2SCRGB_709 lpmCol709R,lpmCol709G,lpmCol709B,lpmColD65,\
                                 lpmCol709R,lpmCol709G,lpmCol709B,lpmColD65,\
                                 lpmCol709R,lpmCol709G,lpmCol709B,lpmColD65,fs2S
//------------------------------------------------------------------------------------------------------------------------------
// CON   SOFT  CON2  CLIP  SCALEONLY
#define LPM_CONFIG_HDR10RAW_709 false,false,true, true, false
#define LPM_COLORS_HDR10RAW_709 lpmCol709R,lpmCol709G,lpmCol709B,lpmColD65,\
                                 lpmCol709R,lpmCol709G,lpmCol709B,lpmColD65,\
                                 lpmCol2020R,lpmCol2020G,lpmCol2020B,lpmColD65,hdr10S
//------------------------------------------------------------------------------------------------------------------------------
// CON   SOFT  CON2  CLIP  SCALEONLY
#define LPM_CONFIG_HDR10SCRGB_709 false,false,false,false,true
#define LPM_COLORS_HDR10SCRGB_709 lpmCol709R,lpmCol709G,lpmCol709B,lpmColD65,\
                                   lpmCol709R,lpmCol709G,lpmCol709B,lpmColD65,\
                                   lpmCol709R,lpmCol709G,lpmCol709B,lpmColD65,hdr10S
//------------------------------------------------------------------------------------------------------------------------------
// CON   SOFT  CON2  CLIP  SCALEONLY
#define LPM_CONFIG_709_709 false,false,false,false,false
#define LPM_COLORS_709_709 lpmCol709R,lpmCol709G,lpmCol709B,lpmColD65,\
                            lpmCol709R,lpmCol709G,lpmCol709B,lpmColD65,\
                            lpmCol709R,lpmCol709G,lpmCol709B,lpmColD65,1.0
//==============================================================================================================================
// CON   SOFT  CON2  CLIP  SCALEONLY
#define LPM_CONFIG_FS2RAW_P3 true, true, false,false,false
#define LPM_COLORS_FS2RAW_P3 lpmColP3R,lpmColP3G,lpmColP3B,lpmColD65,\
                              fs2R,fs2G,fs2B,fs2W,\
                              fs2R,fs2G,fs2B,fs2W,1.0
//------------------------------------------------------------------------------------------------------------------------------
// FreeSync2 gamut can be smaller than P3.
// CON   SOFT  CON2  CLIP  SCALEONLY
#define LPM_CONFIG_FS2SCRGB_P3 true, true, true, false,false
#define LPM_COLORS_FS2SCRGB_P3 lpmColP3R,lpmColP3G,lpmColP3B,lpmColD65,\
                                fs2R,fs2G,fs2B,fs2W,\
                                lpmCol709R,lpmCol709G,lpmCol709B,lpmColD65,fs2S
//------------------------------------------------------------------------------------------------------------------------------
// CON   SOFT  CON2  CLIP  SCALEONLY
#define LPM_CONFIG_HDR10RAW_P3 false,false,true, true, false
#define LPM_COLORS_HDR10RAW_P3 lpmColP3R,lpmColP3G,lpmColP3B,lpmColD65,\
                                lpmColP3R,lpmColP3G,lpmColP3B,lpmColD65,\
                                lpmCol2020R,lpmCol2020G,lpmCol2020B,lpmColD65,hdr10S
//------------------------------------------------------------------------------------------------------------------------------
// CON   SOFT  CON2  CLIP  SCALEONLY
#define LPM_CONFIG_HDR10SCRGB_P3 false,false,true, false,false
#define LPM_COLORS_HDR10SCRGB_P3 lpmColP3R,lpmColP3G,lpmColP3B,lpmColD65,\
                                  lpmCol2020R,lpmCol2020G,lpmCol2020B,lpmColD65,\
                                  lpmCol709R,lpmCol709G,lpmCol709B,lpmColD65,hdr10S
//------------------------------------------------------------------------------------------------------------------------------
// CON   SOFT  CON2  CLIP  SCALEONLY
#define LPM_CONFIG_709_P3 true, true, false,false,false
#define LPM_COLORS_709_P3 lpmColP3R,lpmColP3G,lpmColP3B,lpmColD65,\
                           lpmCol709R,lpmCol709G,lpmCol709B,lpmColD65,\
                           lpmCol709R,lpmCol709G,lpmCol709B,lpmColD65,1.0
//==============================================================================================================================
// CON   SOFT  CON2  CLIP  SCALEONLY
#define LPM_CONFIG_FS2RAW_2020 true, true, false,false,false
#define LPM_COLORS_FS2RAW_2020 lpmCol2020R,lpmCol2020G,lpmCol2020B,lpmColD65,\
                                fs2R,fs2G,fs2B,fs2W,\
                                fs2R,fs2G,fs2B,fs2W,1.0
//------------------------------------------------------------------------------------------------------------------------------
// CON   SOFT  CON2  CLIP  SCALEONLY
#define LPM_CONFIG_FS2SCRGB_2020 true, true, true, false,false
#define LPM_COLORS_FS2SCRGB_2020 lpmCol2020R,lpmCol2020G,lpmCol2020B,lpmColD65,\
                                  fs2R,fs2G,fs2B,fs2W,\
                                  lpmCol709R,lpmCol709G,lpmCol709B,lpmColD65,fs2S
//------------------------------------------------------------------------------------------------------------------------------
// CON   SOFT  CON2  CLIP  SCALEONLY
#define LPM_CONFIG_HDR10RAW_2020 false,false,false,false,false
#define LPM_COLORS_HDR10RAW_2020 lpmCol2020R,lpmCol2020G,lpmCol2020B,lpmColD65,\
                                  lpmCol2020R,lpmCol2020G,lpmCol2020B,lpmColD65,\
                                  lpmCol2020R,lpmCol2020G,lpmCol2020B,lpmColD65,hdr10S
//------------------------------------------------------------------------------------------------------------------------------
// CON   SOFT  CON2  CLIP  SCALEONLY
#define LPM_CONFIG_HDR10SCRGB_2020 false,false,true, false,false
#define LPM_COLORS_HDR10SCRGB_2020 lpmCol2020R,lpmCol2020G,lpmCol2020B,lpmColD65,\
                                    lpmCol2020R,lpmCol2020G,lpmCol2020B,lpmColD65,\
                                    lpmCol709R,lpmCol709G,lpmCol709B,lpmColD65,hdr10S
//------------------------------------------------------------------------------------------------------------------------------
// CON   SOFT  CON2  CLIP  SCALEONLY
#define LPM_CONFIG_709_2020 true, true, false,false,false
#define LPM_COLORS_709_2020 lpmCol2020R,lpmCol2020G,lpmCol2020B,lpmColD65,\
                             lpmCol709R,lpmCol709G,lpmCol709B,lpmColD65,\
                             lpmCol709R,lpmCol709G,lpmCol709B,lpmColD65,1.0

#include "ffx_lpm.hlsl"

float3 Apply_LPM(float3 color)
{
    AU4 map0, map1, map2, map3, map4, map5, map6, map7;
    AU4 map8, map9, mapA, mapB, mapC, mapD, mapE, mapF;
    AU4 mapG = 0, mapH = 0, mapI = 0, mapJ = 0, mapK = 0, mapL = 0, mapM = 0, mapN = 0;
    #if defined(DISPLAYMODE_HDR10_SCRGB)
    float hdr10S = LpmHdr10ScrgbScalar(_DisplayMinMaxLuminance.y);
    #elif defined(DISPLAYMODE_HDR10_2084)
    float hdr10S = LpmHdr10RawScalar(_DisplayMinMaxLuminance.y);
    #endif
    LpmSetup(map0, map1, map2, map3, map4, map5, map6, map7,
             map8, map9, mapA, mapB, mapC, mapD, mapE, mapF,
             mapG, mapH, mapI, mapJ, mapK, mapL, mapM, mapN,
             true,
             #if defined(DISPLAYMODE_HDR10_SCRGB)
             LPM_CONFIG_HDR10SCRGB_709,
             LPM_COLORS_HDR10SCRGB_709,
             #elif defined(DISPLAYMODE_HDR10_2084)
             LPM_CONFIG_HDR10RAW_709,
             LPM_COLORS_HDR10RAW_709,
             #else
             LPM_CONFIG_709_709,
             LPM_COLORS_709_709,
             #endif
             _SoftGap,
             _HdrMax,
             _LPMExposure,
             _Contrast,
             _ShoulderContrast,
             _Saturation,
             _Crosstalk
    );

    LpmFilter(color.r, color.g, color.b,
              true,
              #if defined(DISPLAYMODE_HDR10_SCRGB)
              LPM_CONFIG_HDR10SCRGB_709,
              #elif defined(DISPLAYMODE_HDR10_2084)
              LPM_CONFIG_HDR10RAW_709,
              #else
              LPM_CONFIG_709_709,
              #endif
               map0, map1, map2, map3, map4, map5, map6, map7,
              map8, map9, mapA, mapB, mapC, mapD, mapE, mapF,
              mapG, mapH, mapI, mapJ, mapK, mapL, mapM, mapN
    );
    
    return color;
}

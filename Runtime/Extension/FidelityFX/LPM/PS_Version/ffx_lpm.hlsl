
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//_____________________________________________________________/\_______________________________________________________________
//==============================================================================================================================
//
//                                                             GPU
//
//==============================================================================================================================
#ifdef A_GPU
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//_____________________________________________________________/\_______________________________________________________________
//==============================================================================================================================
//                                                         HELPER CODE
//------------------------------------------------------------------------------------------------------------------------------
// Used by LpmSetup() to build constants for the GPU setup path.
//------------------------------------------------------------------------------------------------------------------------------
// Color math references,
//  - http://www.brucelindbloom.com/index.html?Eqn_RGB_XYZ_Matrix.html
//  - https://en.wikipedia.org/wiki/SRGB#The_sRGB_transfer_function_.28.22gamma.22.29
//  - http://www.ryanjuckett.com/programming/rgb-color-space-conversion/
//==============================================================================================================================
 // Low-precision solution.
 void LpmMatInv3x3(out AF3 ox,out AF3 oy,out AF3 oz,in AF3 ix,in AF3 iy,in AF3 iz){
  AF1 i=1.0/(ix.x*(iy.y*iz.z-iz.y*iy.z)-ix.y*(iy.x*iz.z-iy.z*iz.x)+ix.z*(iy.x*iz.y-iy.y*iz.x));
  ox=AF3((iy.y*iz.z-iz.y*iy.z)*i,(ix.z*iz.y-ix.y*iz.z)*i,(ix.y*iy.z-ix.z*iy.y)*i);
  oy=AF3((iy.z*iz.x-iy.x*iz.z)*i,(ix.x*iz.z-ix.z*iz.x)*i,(iy.x*ix.z-ix.x*iy.z)*i);
  oz=AF3((iy.x*iz.y-iz.x*iy.y)*i,(iz.x*ix.y-ix.x*iz.y)*i,(ix.x*iy.y-iy.x*ix.y)*i);}
//------------------------------------------------------------------------------------------------------------------------------
 // Transpose.
 void LpmMatTrn3x3(out AF3 ox,out AF3 oy,out AF3 oz,in AF3 ix,in AF3 iy,in AF3 iz){
  ox=AF3(ix.x,iy.x,iz.x);oy=AF3(ix.y,iy.y,iz.y);oz=AF3(ix.z,iy.z,iz.z);}
//------------------------------------------------------------------------------------------------------------------------------
 void LpmMatMul3x3(out AF3 ox,out AF3 oy,out AF3 oz,in AF3 ax,in AF3 ay,in AF3 az,in AF3 bx,in AF3 by,in AF3 bz){
  AF3 bx2,by2,bz2;LpmMatTrn3x3(bx2,by2,bz2,bx,by,bz);
  ox=AF3(dot(ax,bx2),dot(ax,by2),dot(ax,bz2));
  oy=AF3(dot(ay,bx2),dot(ay,by2),dot(ay,bz2));
  oz=AF3(dot(az,bx2),dot(az,by2),dot(az,bz2));}
//------------------------------------------------------------------------------------------------------------------------------

//------------------------------------------------------------------------------------------------------------------------------
 // Computes z from xy, returns xyz.
 AF3 LpmColXyToZ(AF2 a){return AF3(a.x,a.y,1.0-(a.x+a.y));}
//------------------------------------------------------------------------------------------------------------------------------
 // Returns conversion matrix, rgbw inputs are xy chroma coordinates.
 void LpmColRgbToXyz(out AF3 ox,out AF3 oy,out AF3 oz,AF2 r,AF2 g,AF2 b,AF2 w){
  // Expand from xy to xyz.
  AF3 r3,g3,b3;LpmMatTrn3x3(r3,g3,b3,LpmColXyToZ(r),LpmColXyToZ(g),LpmColXyToZ(b));
  // Convert white xyz to XYZ.
  AF3 w3=LpmColXyToZ(w)*(1.0/w.y);
  // Compute xyz to XYZ scalars for primaries.
  AF3 rv,gv,bv;LpmMatInv3x3(rv,gv,bv,r3,g3,b3);
  AF3 s=AF3(dot(rv,w3),dot(gv,w3),dot(bv,w3));
  // Scale.
  ox=r3*s;oy=g3*s;oz=b3*s;}
//==============================================================================================================================
 // Visualize difference between two values, by bits of precision.
 // This is useful when doing approximation to reference comparisons.
 AP1 LpmD(AF1 a,AF1 b){return abs(a-b)<1.0;}
//------------------------------------------------------------------------------------------------------------------------------
 AF1 LpmC(AF1 a,AF1 b){AF1 c=1.0; // 6-bits or less (the color)
  if(LpmD(a* 127.0,b* 127.0))c=0.875; // 7-bits
  if(LpmD(a* 255.0,b* 255.0))c=0.5; // 8-bits
  if(LpmD(a* 512.0,b* 512.0))c=0.125; // 9-bits
  if(LpmD(a*1024.0,b*1024.0))c=0.0; // 10-bits or better (black)
  return c;}
//------------------------------------------------------------------------------------------------------------------------------
 AF3 LpmViewDiff(AF3 a,AF3 b){return AF3(LpmC(a.r,b.r),LpmC(a.g,b.g),LpmC(a.b,b.b));}
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//_____________________________________________________________/\_______________________________________________________________
//==============================================================================================================================
//                                                 HDR10 RANGE LIMITING SCALAR
//------------------------------------------------------------------------------------------------------------------------------
// As of 2019, HDR10 supporting TVs typically have PQ tonal curves with near clipping long before getting to the peak 10K nits.
// Unfortunately this clipping point changes per TV (requires some amount of user calibration).
// Some examples,
//  https://youtu.be/M7OsbpU4oCQ?t=875
//  https://youtu.be/8mlTElC2z2A?t=1159
//  https://youtu.be/B5V5hCVXBAI?t=975
// For this reason it can be useful to manually limit peak HDR10 output to some point before the clipping point.
// The following functions are useful to compute the scaling factor 'hdr10S' to use with LpmSetup() to manually limit peak.
//==============================================================================================================================
 // Compute 'hdr10S' for raw HDR10 output, pass in peak nits (typically somewhere around 1000.0 to 2000.0).
 AF1 LpmHdr10RawScalar(AF1 peakNits){return peakNits*(1.0/10000.0);}
//------------------------------------------------------------------------------------------------------------------------------
 // Compute 'hdr10S' for scRGB based HDR10 output, pass in peak nits (typically somewhere around 1000.0 to 2000.0).
 AF1 LpmHdr10ScrgbScalar(AF1 peakNits){return peakNits*(1.0/10000.0)*(10000.0/80.0);}
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//_____________________________________________________________/\_______________________________________________________________
//==============================================================================================================================
//                                                   FREESYNC2 SCRGB SCALAR
//------------------------------------------------------------------------------------------------------------------------------
// The more expensive scRGB mode for FreeSync2 requires a complex scale factor based on display properties.
//==============================================================================================================================
 // TODO: Validate this is correct!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
 // This computes the 'fs2S' factor used in LpmSetup().
 AF1 LpmFs2ScrgbScalar(
 bool localDimming, // Is local dimming on?
 AF1 minLuma,AF1 medLuma){ // Queried display properties.
  if(localDimming)return 0.0; // TODO!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
  return ((medLuma-minLuma)+minLuma)*(1.0/80.0);}
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//_____________________________________________________________/\_______________________________________________________________
//==============================================================================================================================
//                                                    SETUP CONTROL BLOCK
//------------------------------------------------------------------------------------------------------------------------------
// This is used to control LpmFilter*() functions.
//------------------------------------------------------------------------------------------------------------------------------
// CONTROL BLOCK
// =============
// LPM has an optimized constant|literal control block of 384 bytes.
// This control block should be 128-byte aligned (future-proof in case constant cache lines end up at 128-bytes/line).
// Much of this block is reserved for future usage, and to establish good alignment.
// Compile will dead-code remove things not used (no extra overhead).
// Content ordered and grouped for best performance in the common cases.
// Control block has both 32-bit and 16-bit values so that optimizations are possible on platforms supporting faster 16-bit.
//------------------------------------------------------------------------------------------------------------------------------
// 32-BIT PART
//       _______R________  _______G________  _______B________  _______A________
// map0  saturation.r      saturation.g      saturation.b      contrast
// map1  toneScaleBias.x   toneScaleBias.y   lumaT.r           lumaT.g
// map2  lumaT.b           crosstalk.r       crosstalk.g       crosstalk.b
// map3  rcpLumaT.r        rcpLumaT.g        rcpLumaT.b        con2R.r
// --
// map4  con2R.g           con2R.b           con2G.r           con2G.g
// map5  con2G.b           con2B.r           con2B.g           con2B.b
// map6  shoulderContrast  lumaW.r           lumaW.g           lumaW.b
// map7  softGap.x         softGap.y         conR.r            conR.g
// --
// map8  conR.b            conG.r            conG.g            conG.b
// map9  conB.r            conB.g            conB.b            (reserved)
// mapA  (reserved)        (reserved)        (reserved)        (reserved)
// mapB  (reserved)        (reserved)        (reserved)        (reserved)
// --
// mapC  (reserved)        (reserved)        (reserved)        (reserved)
// mapD  (reserved)        (reserved)        (reserved)        (reserved)
// mapE  (reserved)        (reserved)        (reserved)        (reserved)
// mapF  (reserved)        (reserved)        (reserved)        (reserved)
// --
// PACKED 16-BIT PART
//          _______X________  _______Y________  _______X________  _______Y________
// mapG.rg  saturation.r      saturation.g      saturation.b      contrast
// mapG.ba  toneScaleBias.x   toneScaleBias.y   lumaT.r           lumaT.g
// mapH.rg  lumaT.b           crosstalk.r       crosstalk.g       crosstalk.b
// mapH.ba  rcpLumaT.r        rcpLumaT.g        rcpLumaT.b        con2R.r
// mapI.rg  con2R.g           con2R.b           con2G.r           con2G.g
// mapI.ba  con2G.b           con2B.r           con2B.g           con2B.b
// mapJ.rg  shoulderContrast  lumaW.r           lumaW.g           lumaW.b
// mapJ.ba  softGap.x         softGap.y         conR.r            conR.g
// --
// mapK.rg  conR.b            conG.r            conG.g            conG.b
// mapK.ba  conB.r            conB.g            conB.b            (reserved)
// mapL.rb  (reserved)        (reserved)        (reserved)        (reserved)
// mapL.ba  (reserved)        (reserved)        (reserved)        (reserved)
// mapM.rb  (reserved)        (reserved)        (reserved)        (reserved)
// mapM.ba  (reserved)        (reserved)        (reserved)        (reserved)
// mapN.rb  (reserved)        (reserved)        (reserved)        (reserved)
// mapN.ba  (reserved)        (reserved)        (reserved)        (reserved)
//------------------------------------------------------------------------------------------------------------------------------
// IDEAS
// =====
//  - Some of this might benefit from double precision on the GPU.
//  - Can scaling factor in con2 be used to improve FP16 precision?
//  - Verify lumaW stuff!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
//==============================================================================================================================
 void LpmSetup(
 // Control block output.
 out AU4 map0,out AU4 map1,out AU4 map2,out AU4 map3,out AU4 map4,out AU4 map5,out AU4 map6,out AU4 map7,
 out AU4 map8,out AU4 map9,out AU4 mapA,out AU4 mapB,out AU4 mapC,out AU4 mapD,out AU4 mapE,out AU4 mapF,
 out AU4 mapG,out AU4 mapH,out AU4 mapI,out AU4 mapJ,out AU4 mapK,out AU4 mapL,out AU4 mapM,out AU4 mapN,
 // Path control.
 AP1 shoulder, // Use optional extra shoulderContrast tuning (set to false if shoulderContrast is 1.0).
 // Prefab start, "LPM_CONFIG_".
 AP1 con, // Use first RGB conversion matrix, if 'soft' then 'con' must be true also.
 AP1 soft, // Use soft gamut mapping.
 AP1 con2, // Use last RGB conversion matrix.
 AP1 clip, // Use clipping in last conversion matrix.
 AP1 scaleOnly, // Scale only for last conversion matrix (used for 709 HDR to scRGB).
 // Gamut control, "LPM_COLORS_".
 AF2 xyRedW,AF2 xyGreenW,AF2 xyBlueW,AF2 xyWhiteW, // Chroma coordinates for working color space.
 AF2 xyRedO,AF2 xyGreenO,AF2 xyBlueO,AF2 xyWhiteO, // For the output color space.
 AF2 xyRedC,AF2 xyGreenC,AF2 xyBlueC,AF2 xyWhiteC,AF1 scaleC, // For the output container color space (if con2).
 // Prefab end.
 AF1 softGap, // Range of 0 to a little over zero, controls how much feather region in out-of-gamut mapping, 0=clip.
 // Tonemapping control.
 AF1 hdrMax, // Maximum input value.
 AF1 exposure, // Number of stops between 'hdrMax' and 18% mid-level on input.
 AF1 contrast, // Input range {0.0 (no extra contrast) to 1.0 (maximum contrast)}.
 AF1 shoulderContrast, // Shoulder shaping, 1.0 = no change (fast path).
 AF3 saturation, // A per channel adjustment, use <0 decrease, 0=no change, >0 increase.
 AF3 crosstalk){ // One channel must be 1.0, the rest can be <= 1.0 but not zero.
//-----------------------------------------------------------------------------------------------------------------------------
  // Contrast needs to be 1.0 based for no contrast.
  contrast+=1.0;
  // Saturation is based on contrast.
  saturation+=contrast;
//-----------------------------------------------------------------------------------------------------------------------------
  AF1 midIn=hdrMax*0.18/exp2(exposure);
  AF1 midOut=0.18;
//-----------------------------------------------------------------------------------------------------------------------------
  AF2 toneScaleBias;
  AF1 cs=contrast*shoulderContrast;
  AF1 z0=-pow(midIn,contrast);
  AF1 z1=pow(hdrMax,cs)*pow(midIn,contrast);
  AF1 z2=pow(hdrMax,contrast)*pow(midIn,cs)*midOut;
  AF1 z3=pow(hdrMax,cs)*midOut;
  AF1 z4=pow(midIn,cs)*midOut;
  toneScaleBias.x=-((z0+(midOut*(z1-z2))/(z3-z4))/z4);
//-----------------------------------------------------------------------------------------------------------------------------
  AF1 w0=pow(hdrMax,cs)*pow(midIn,contrast);
  AF1 w1=pow(hdrMax,contrast)*pow(midIn,cs)*midOut;
  AF1 w2=pow(hdrMax,cs)*midOut;
  AF1 w3=pow(midIn,cs)*midOut;
  toneScaleBias.y=(w0-w1)/(w2-w3);
//-----------------------------------------------------------------------------------------------------------------------------
  AF3 lumaW;AF3 rgbToXyzXW;AF3 rgbToXyzYW;AF3 rgbToXyzZW;
  LpmColRgbToXyz(rgbToXyzXW,rgbToXyzYW,rgbToXyzZW,xyRedW,xyGreenW,xyBlueW,xyWhiteW);
  // Use the Y vector of the matrix for the associated luma coef.
  // For safety, make sure the vector sums to 1.0.
  lumaW=rgbToXyzYW;
  lumaW*=ARcpF1(lumaW.r+lumaW.g+lumaW.b);
//-----------------------------------------------------------------------------------------------------------------------------
  // The 'lumaT' for crosstalk mapping is always based on the output color space, unless soft conversion is not used.
  AF3 lumaT;AF3 rgbToXyzXO;AF3 rgbToXyzYO;AF3 rgbToXyzZO;
  LpmColRgbToXyz(rgbToXyzXO,rgbToXyzYO,rgbToXyzZO,xyRedO,xyGreenO,xyBlueO,xyWhiteO);
  if(soft)lumaT=rgbToXyzYO;else lumaT=rgbToXyzYW;
  lumaT*=ARcpF1(lumaT.r+lumaT.g+lumaT.b);
  AF3 rcpLumaT=ARcpF3(lumaT);
//-----------------------------------------------------------------------------------------------------------------------------
  AF2 softGap2;
  if(soft)softGap2=AF2(softGap,(1.0-softGap)/(softGap*0.693147180559));
  #ifdef A_HLSL
   else softGap2=AF2_(0.0);
  #endif
//-----------------------------------------------------------------------------------------------------------------------------
  // First conversion is always working to output.
  AF3 conR,conG,conB;
  if(con){AF3 xyzToRgbRO;AF3 xyzToRgbGO;AF3 xyzToRgbBO;
   LpmMatInv3x3(xyzToRgbRO,xyzToRgbGO,xyzToRgbBO,rgbToXyzXO,rgbToXyzYO,rgbToXyzZO);
   LpmMatMul3x3(conR,conG,conB,xyzToRgbRO,xyzToRgbGO,xyzToRgbBO,rgbToXyzXW,rgbToXyzYW,rgbToXyzZW);}
  #ifdef A_HLSL
   else{conR=conG=conB=AF3_(0.0);}
  #endif
//-----------------------------------------------------------------------------------------------------------------------------
  // The last conversion is always output to container.
  AF3 con2R,con2G,con2B;
  if(con2){AF3 rgbToXyzXC;AF3 rgbToXyzYC;AF3 rgbToXyzZC;
   LpmColRgbToXyz(rgbToXyzXC,rgbToXyzYC,rgbToXyzZC,xyRedC,xyGreenC,xyBlueC,xyWhiteC);
   AF3 xyzToRgbRC;AF3 xyzToRgbGC;AF3 xyzToRgbBC;
   LpmMatInv3x3(xyzToRgbRC,xyzToRgbGC,xyzToRgbBC,rgbToXyzXC,rgbToXyzYC,rgbToXyzZC);
   LpmMatMul3x3(con2R,con2G,con2B,xyzToRgbRC,xyzToRgbGC,xyzToRgbBC,rgbToXyzXO,rgbToXyzYO,rgbToXyzZO);
   con2R*=scaleC;con2G*=scaleC;con2B*=scaleC;}
  #ifdef A_HLSL
   else{con2R=con2G=con2B=AF3_(0.0);}
  #endif
  if(scaleOnly)con2R.r=scaleC;
//-----------------------------------------------------------------------------------------------------------------------------
  // Debug force 16-bit precision for the 32-bit inputs.
  #ifdef LPM_DEBUG_FORCE_16BIT_PRECISION
   saturation=AF3(AH3(saturation));
   contrast=AF1(AH1(contrast));
   toneScaleBias=AF2(AH2(toneScaleBias));
   lumaT=AF3(AH3(lumaT));
   crosstalk=AF3(AH3(crosstalk));
   rcpLumaT=AF3(AH3(rcpLumaT));
   con2R=AF3(AH3(con2R));
   con2G=AF3(AH3(con2G));
   con2B=AF3(AH3(con2B));
   shoulderContrast=AF1(AH1(shoulderContrast));
   lumaW=AF3(AH3(lumaW));
   softGap2=AF2(AH2(softGap2));
   conR=AF3(AH3(conR));
   conG=AF3(AH3(conG));
   conB=AF3(AH3(conB));
  #endif
//-----------------------------------------------------------------------------------------------------------------------------
  // Pack into control block.
  map0.rgb=AU3_AF3(saturation);map0.a=AU1_AF1(contrast);
  map1.rg=AU2_AF2(toneScaleBias);map1.ba=AU2_AF2(lumaT.rg);
  map2.r=AU1_AF1(lumaT.b);map2.gba=AU3_AF3(crosstalk);
  map3.rgb=AU3_AF3(rcpLumaT);map3.a=AU1_AF1(con2R.r);
  map4.rg=AU2_AF2(con2R.gb);map4.ba=AU2_AF2(con2G.rg);
  map5.r=AU1_AF1(con2R.b);map5.gba=AU3_AF3(con2B);
  map6.r=AU1_AF1(shoulderContrast);map6.gba=AU3_AF3(lumaW);
  map7.rg=AU2_AF2(softGap2);map7.ba=AU2_AF2(conR.rg);
  map8.r=AU1_AF1(conR.b);map8.gba=AU3_AF3(conG);
  map9.rgb=AU3_AF3(conB);
  #ifdef A_HLSL
   map9.a=0.0;mapA=mapB=mapC=mapD=mapE=mapF=AU4_(0);
  #endif
  mapG=0, mapH=0, mapI=0, mapJ=0, mapK=0, mapL=0, mapM=0, mapN=0;
//-----------------------------------------------------------------------------------------------------------------------------
 }
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//_____________________________________________________________/\_______________________________________________________________
//==============================================================================================================================
//                                                           MAPPER
//------------------------------------------------------------------------------------------------------------------------------
// Do not call this directly, instead call the LpmFilter*() functions.
// This gets reconfigured based on inputs for all the various usage cases.
// Some of this has been explicitly ordered to increase precision.
//------------------------------------------------------------------------------------------------------------------------------
// IDEAS
// =====
//  - Use med3() for soft falloff and for [A] color conversions.
//  - Retry FP16 PQ conversion with different input range.
//  - Possibly skip some work if entire wave is in gamut.
//==============================================================================================================================
 // Use LpmFilter() instead of this.
 void LpmMap(inout AF1 colorR,inout AF1 colorG,inout AF1 colorB,// Input and output color.
 AF3 lumaW, // Luma coef for RGB working space.
 AF3 lumaT, // Luma coef for crosstalk mapping (can be working or output color-space depending on usage case).
 AF3 rcpLumaT, // 1/lumaT.
 AF3 saturation, // Saturation powers.
 AF1 contrast, // Contrast power.
 AP1 shoulder, // Using shoulder tuning (should be a compile-time immediate).
 AF1 shoulderContrast, // Shoulder power.
 AF2 toneScaleBias, // Other tonemapping parameters.
 AF3 crosstalk, // Crosstalk scaling for over-exposure color shaping.
 AP1 con, // Use first RGB conversion matrix (should be a compile-time immediate), if 'soft' then 'con' must be true also.
 AF3 conR,AF3 conG,AF3 conB, // RGB conversion matrix (working to output space conversion).
 AP1 soft, // Use soft gamut mapping (should be a compile-time immediate).
 AF2 softGap, // {x,(1-x)/(x*0.693147180559)}, where 'x' is gamut mapping soft fall-off amount.
 AP1 con2, // Use last RGB conversion matrix (should be a compile-time immediate).
 AP1 clip, // Use clipping on last conversion matrix.
 AP1 scaleOnly, // Do scaling only (special case for 709 HDR to scRGB).
 AF3 con2R,AF3 con2G,AF3 con2B){ // Secondary RGB conversion matrix.
//------------------------------------------------------------------------------------------------------------------------------
  // Grab original RGB ratio (RCP, 3x MUL, MAX3).
  AF1 rcpMax=ARcpF1(AMax3F1(colorR,colorG,colorB));AF1 ratioR=colorR*rcpMax;AF1 ratioG=colorG*rcpMax;AF1 ratioB=colorB*rcpMax;
  // Apply saturation, ratio must be max 1.0 for this to work right (3x EXP2, 3x LOG2, 3x MUL).
  ratioR=pow(ratioR,AF1_(saturation.r));ratioG=pow(ratioG,AF1_(saturation.g));ratioB=pow(ratioB,AF1_(saturation.b));
//------------------------------------------------------------------------------------------------------------------------------
  // Tonemap luma, note this uses the original color, so saturation is luma preserving.
  // If not using 'con' this uses the output space luma directly to avoid needing extra constants.
  // Note 'soft' should be a compile-time immediate (so no branch) (3x MAD).
  AF1 luma;if(soft)luma=colorG*AF1_(lumaW.g)+(colorR*AF1_(lumaW.r)+(colorB*AF1_(lumaW.b)));
  else             luma=colorG*AF1_(lumaT.g)+(colorR*AF1_(lumaT.r)+(colorB*AF1_(lumaT.b)));
  luma=pow(luma,AF1_(contrast)); // (EXP2, LOG2, MUL).
  AF1 lumaShoulder=shoulder?pow(luma,AF1_(shoulderContrast)):luma; // Optional (EXP2, LOG2, MUL).
  luma=luma*ARcpF1(lumaShoulder*AF1_(toneScaleBias.x)+AF1_(toneScaleBias.y)); // (MAD, MUL, RCP).
//------------------------------------------------------------------------------------------------------------------------------
  // If running soft clipping (this should be a compile-time immediate so branch will not exist).
  if(soft){
   // The 'con' should be a compile-time immediate so branch will not exist.
   // Use of 'con' is implied if soft-falloff is enabled, but using the check here to make finding bugs easy.
   if(con){
    // Converting ratio instead of color. Change of primaries (9x MAD).
    colorR=ratioR;colorG=ratioG;colorB=ratioB;
    ratioR=colorR*AF1_(conR.r)+(colorG*AF1_(conR.g)+(colorB*AF1_(conR.b)));
    ratioG=colorG*AF1_(conG.g)+(colorR*AF1_(conG.r)+(colorB*AF1_(conG.b)));
    ratioB=colorB*AF1_(conB.b)+(colorG*AF1_(conB.g)+(colorR*AF1_(conB.r)));
    // Convert ratio to max 1 again (RCP, 3x MUL, MAX3).
    rcpMax=ARcpF1(AMax3F1(ratioR,ratioG,ratioB));ratioR*=rcpMax;ratioG*=rcpMax;ratioB*=rcpMax;}
//------------------------------------------------------------------------------------------------------------------------------
   // Absolute gamut mapping converted to soft falloff (maintains max 1 property).
   //  g = gap {0 to g} used for {-inf to 0} input range
   //          {g to 1} used for {0 to 1} input range
   //  x >= 0 := y = x * (1-g) + g
   //  x < 0  := g * 2^(x*h)
   //  Where h=(1-g)/(g*log(2)) --- where log() is the natural log
   // The {g,h} above is passed in as softGap.
   // Soft falloff (3x MIN, 3x MAX, 9x MAD, 3x EXP2).
   ratioR=min(max(AF1_(softGap.x),ASatF1(ratioR*AF1_(-softGap.x)+ratioR)),
    ASatF1(AF1_(softGap.x)*exp2(ratioR*AF1_(softGap.y))));
   ratioG=min(max(AF1_(softGap.x),ASatF1(ratioG*AF1_(-softGap.x)+ratioG)),
    ASatF1(AF1_(softGap.x)*exp2(ratioG*AF1_(softGap.y))));
   ratioB=min(max(AF1_(softGap.x),ASatF1(ratioB*AF1_(-softGap.x)+ratioB)),
    ASatF1(AF1_(softGap.x)*exp2(ratioB*AF1_(softGap.y))));}
//------------------------------------------------------------------------------------------------------------------------------
  // Compute ratio scaler required to hit target luma (4x MAD, 1 RCP).
  AF1 lumaRatio=ratioR*AF1_(lumaT.r)+ratioG*AF1_(lumaT.g)+ratioB*AF1_(lumaT.b);
  // This is limited to not clip.
  AF1 ratioScale=ASatF1(luma*ARcpF1(lumaRatio));
  // Assume in gamut, compute output color (3x MAD).
  colorR=ASatF1(ratioR*ratioScale);colorG=ASatF1(ratioG*ratioScale);colorB=ASatF1(ratioB*ratioScale);
  // Capability per channel to increase value (3x MAD).
  // This factors in crosstalk factor to avoid multiplies later.
  //  '(1.0-ratio)*crosstalk' optimized to '-crosstalk*ratio+crosstalk'
  AF1 capR=AF1_(-crosstalk.r)*colorR+AF1_(crosstalk.r);
  AF1 capG=AF1_(-crosstalk.g)*colorG+AF1_(crosstalk.g);
  AF1 capB=AF1_(-crosstalk.b)*colorB+AF1_(crosstalk.b);
  // Compute amount of luma needed to add to non-clipped channels to make up for clipping (3x MAD).
  AF1 lumaAdd=ASatF1((-colorB)*AF1_(lumaT.b)+((-colorR)*AF1_(lumaT.r)+((-colorG)*AF1_(lumaT.g)+luma)));
  // Amount to increase keeping over-exposure ratios constant and possibly exceeding clipping point (4x MAD, 1 RCP).
  AF1 t=lumaAdd*ARcpF1(capG*AF1_(lumaT.g)+(capR*AF1_(lumaT.r)+(capB*AF1_(lumaT.b))));
  // Add amounts to base color but clip (3x MAD).
  colorR=ASatF1(t*capR+colorR);colorG=ASatF1(t*capG+colorG);colorB=ASatF1(t*capB+colorB);
  // Compute amount of luma needed to add to non-clipped channel to make up for clipping (3x MAD).
  lumaAdd=ASatF1((-colorB)*AF1_(lumaT.b)+((-colorR)*AF1_(lumaT.r)+((-colorG)*AF1_(lumaT.g)+luma)));
  // Add to last channel (3x MAD).
  colorR=ASatF1(lumaAdd*AF1_(rcpLumaT.r)+colorR);
  colorG=ASatF1(lumaAdd*AF1_(rcpLumaT.g)+colorG);
  colorB=ASatF1(lumaAdd*AF1_(rcpLumaT.b)+colorB);
//------------------------------------------------------------------------------------------------------------------------------
  // The 'con2' should be a compile-time immediate so branch will not exist.
  // Last optional place to convert from smaller to larger gamut (or do clipped conversion).
  // For the non-soft-falloff case, doing this after all other mapping saves intermediate re-scaling ratio to max 1.0.
  if(con2){
   // Change of primaries (9x MAD).
   ratioR=colorR;ratioG=colorG;ratioB=colorB;
   if(clip){
    colorR=ASatF1(ratioR*AF1_(con2R.r)+(ratioG*AF1_(con2R.g)+(ratioB*AF1_(con2R.b))));
    colorG=ASatF1(ratioG*AF1_(con2G.g)+(ratioR*AF1_(con2G.r)+(ratioB*AF1_(con2G.b))));
    colorB=ASatF1(ratioB*AF1_(con2B.b)+(ratioG*AF1_(con2B.g)+(ratioR*AF1_(con2B.r))));}
   else{
    colorR=ratioR*AF1_(con2R.r)+(ratioG*AF1_(con2R.g)+(ratioB*AF1_(con2R.b)));
    colorG=ratioG*AF1_(con2G.g)+(ratioR*AF1_(con2G.r)+(ratioB*AF1_(con2G.b)));
    colorB=ratioB*AF1_(con2B.b)+(ratioG*AF1_(con2B.g)+(ratioR*AF1_(con2B.r)));}}
//------------------------------------------------------------------------------------------------------------------------------
  if(scaleOnly){colorR*=AF1_(con2R.r);colorG*=AF1_(con2R.r);colorB*=AF1_(con2R.r);}}


////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//_____________________________________________________________/\_______________________________________________________________
//==============================================================================================================================
//                                                           FILTER
//------------------------------------------------------------------------------------------------------------------------------
// Entry point for per-pixel color tone+gamut mapping.
// Input is linear color {0 to hdrMax} ranged.
// Output is linear color {0 to 1} ranged, except for scRGB where outputs can end up negative and larger than one.
//==============================================================================================================================
 // 32-bit entry point.
 void LpmFilter(
 // Input and output color.
 inout AF1 colorR,inout AF1 colorG,inout AF1 colorB,
 // Path control should all be compile-time immediates.
 AP1 shoulder, // Using shoulder tuning.
 // Prefab "LPM_CONFIG_" start, use the same as used for LpmSetup().
 AP1 con, // Use first RGB conversion matrix, if 'soft' then 'con' must be true also.
 AP1 soft, // Use soft gamut mapping.
 AP1 con2, // Use last RGB conversion matrix.
 AP1 clip, // Use clipping in last conversion matrix.
 AP1 scaleOnly, // Scale only for last conversion matrix (used for 709 HDR to scRGB).
 // Prefab end.
 // Control block.
 AU4 map0,AU4 map1,AU4 map2,AU4 map3,AU4 map4,AU4 map5,AU4 map6,AU4 map7,
 AU4 map8,AU4 map9,AU4 mapA,AU4 mapB,AU4 mapC,AU4 mapD,AU4 mapE,AU4 mapF,
 AU4 mapG,AU4 mapH,AU4 mapI,AU4 mapJ,AU4 mapK,AU4 mapL,AU4 mapM,AU4 mapN){
  LpmMap(colorR,colorG,colorB,
   AF3(AF4_AU4(map6).g,AF4_AU4(map6).b,AF4_AU4(map6).a), // lumaW
   AF3(AF4_AU4(map1).b,AF4_AU4(map1).a,AF4_AU4(map2).r), // lumaT
   AF3(AF4_AU4(map3).r,AF4_AU4(map3).g,AF4_AU4(map3).b), // rcpLumaT
   AF3(AF4_AU4(map0).r,AF4_AU4(map0).g,AF4_AU4(map0).b), // saturation
   AF4_AU4(map0).a, // contrast
   shoulder,
   AF4_AU4(map6).r, // shoulderContrast
   AF2(AF4_AU4(map1).r,AF4_AU4(map1).g), // toneScaleBias
   AF3(AF4_AU4(map2).g,AF4_AU4(map2).b,AF4_AU4(map2).a),// crosstalk
   con,
   AF3(AF4_AU4(map7).b,AF4_AU4(map7).a,AF4_AU4(map8).r), // conR
   AF3(AF4_AU4(map8).g,AF4_AU4(map8).b,AF4_AU4(map8).a), // conG
   AF3(AF4_AU4(map9).r,AF4_AU4(map9).g,AF4_AU4(map9).b), // conB
   soft,
   AF2(AF4_AU4(map7).r,AF4_AU4(map7).g), // softGap
   con2,clip,scaleOnly,
   AF3(AF4_AU4(map3).a,AF4_AU4(map4).r,AF4_AU4(map4).g), // con2R
   AF3(AF4_AU4(map4).b,AF4_AU4(map4).a,AF4_AU4(map5).r), // con2G
   AF3(AF4_AU4(map5).g,AF4_AU4(map5).b,AF4_AU4(map5).a));} // con2B
 //------------------------------------------------------------------------------------------------------------------------------
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//_____________________________________________________________/\_______________________________________________________________
//==============================================================================================================================
//                                                       END OF GPU CODE
//==============================================================================================================================
#endif

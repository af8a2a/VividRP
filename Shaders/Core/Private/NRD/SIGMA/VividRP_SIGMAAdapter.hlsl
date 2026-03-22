// VividRP SIGMA adapter — included after Common.hlsl in each SIGMA compute shader.
// gIn_ViewZ now receives linear eye depth from GenerateViewZPass (R32_SFloat, positive values).
// UnpackViewZ(z) = abs(z * gViewZScale) with gViewZScale=1 is correct as-is.
// No override needed.

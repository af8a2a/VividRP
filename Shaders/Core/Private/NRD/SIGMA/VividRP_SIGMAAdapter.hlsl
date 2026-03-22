// VividRP SIGMA adapter — included after Common.hlsl in each SIGMA compute shader.
// Overrides UnpackViewZ to linearize raw reversed-Z hardware depth using the
// projection matrix already present in the SIGMA constant buffer.
//
// For Unity reversed-Z perspective:
//   proj[2][2] = near / (near - far)   (≈ 0 for large far)
//   proj[2][3] = near * far / (far - near)
//   linearZ    = proj[2][3] / (rawDepth - proj[2][2])

#undef UnpackViewZ
#define UnpackViewZ(z) (gViewToClip[2][3] / ((z) - gViewToClip[2][2]))

TEXTURE2D(_ExposureTexture);
TEXTURE2D(_PrevExposureTexture);

float GetCurrentExposureMultiplier()
{
    // _ProbeExposureScale is a scale used to perform range compression to avoid saturation of the content of the probes. It is 1.0 if we are not rendering probes.
    return LOAD_TEXTURE2D(_ExposureTexture, int2(0, 0)).x ;
}

float GetPreviousExposureMultiplier()
{
    // _ProbeExposureScale is a scale used to perform range compression to avoid saturation of the content of the probes. It is 1.0 if we are not rendering probes.
    return LOAD_TEXTURE2D(_PrevExposureTexture, int2(0, 0)).x ;
}
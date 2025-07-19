TEXTURE2D(_ExposureTexture);
TEXTURE2D(_PrevExposureTexture);
float _ProbeExposureScale;


float GetCurrentExposureMultiplier()
{
    #if SHADEROPTIONS_PRE_EXPOSITION
    // _ProbeExposureScale is a scale used to perform range compression to avoid saturation of the content of the probes. It is 1.0 if we are not rendering probes.
    return LOAD_TEXTURE2D(_ExposureTexture, int2(0, 0)).x * _ProbeExposureScale;
    #else
    return _ProbeExposureScale;
    #endif
}

float GetPreviousExposureMultiplier()
{
    #if SHADEROPTIONS_PRE_EXPOSITION
    // _ProbeExposureScale is a scale used to perform range compression to avoid saturation of the content of the probes. It is 1.0 if we are not rendering probes.
    return LOAD_TEXTURE2D(_PrevExposureTexture, int2(0, 0)).x * _ProbeExposureScale;
    #else
    return _ProbeExposureScale;
    #endif
}
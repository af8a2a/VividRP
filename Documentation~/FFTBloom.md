# FFT Convolution Bloom

`BloomPass` supports two Volume modes:

- `Scattering` keeps the existing mip-pyramid bloom and is the default fast path.
- `Convolution FFT` convolves the thresholded HDR image with a user-supplied kernel.

## Setup

1. Add **Post-processing > Bloom** to a Volume Profile.
2. Set **Mode** to `Convolution FFT`.
3. Assign a linear HDR or grayscale texture to **Convolution Kernel**.
4. Set **Convolution Center** to the kernel's brightest point. A centered kernel uses `(0.5, 0.5)`.
5. Adjust **Convolution Size** and **Intensity**.

The kernel should use a black background and non-negative RGB values. Colored kernels are supported. The implementation clamps the bright center before convolution so the source highlight remains in the scene while the surrounding scattering lobe becomes bloom.

## Implementation

The implementation follows the same domain construction used by Unreal's FFT bloom:

1. Downsample and threshold the scene input.
2. Reserve zero padding from the kernel radius to limit circular wraparound.
3. Round both axes up to powers of two, capped at `4096`.
4. Wrap the centered spatial kernel around the FFT origin.
5. Reduce its RGB energy and cache its frequency-domain representation.
6. Execute unitary radix-2 forward transforms, complex multiplication, and inverse transforms.
7. Normalize by kernel energy and pass the result through the existing bloom tint, intensity, lens-dirt, lens-flare, and final-composite bindings.

The spectral kernel cache is invalidated when the texture contents, kernel size, center, clamp, or FFT domain changes.

## Performance and limits

- **Convolution Resolution Scale** defaults to `0.25`. FFT memory and work grow with the next power-of-two domain, so small resolution changes can double an axis.
- **Convolution Buffer Scale** limits padding. Lower values save memory but can introduce bloom wrapping between opposite screen edges.
- Frequency buffers use `R16G16B16A16_SFloat`. Extremely bright, broadly distributed pre-exposed values can exceed half-precision range.
- If the convolution kernel is missing or FFT kernels are unavailable, `BloomPass` falls back to the scattering path.
- The current FFT implementation is a portable multi-dispatch radix-2 path. It does not yet use wave or group-shared mixed-radix stages.

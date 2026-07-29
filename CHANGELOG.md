# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- FidelityFX CACAO as a selectable alternative to the existing GTAO implementation.
- FFT convolution bloom with user-authored kernels, padded power-of-two domains, kernel-spectrum caching, energy normalization, a Wave32/Wave64 primary path, and a 4096-point LDS fallback.
- Scalar stochastic geometry coverage for StandardLit reference path tracing, separated from OpenPBR material transmission.
- OpenPBR solid-surface transmission for StandardLit reference path tracing, including partial dielectric transmission, rough refraction, four-level nested IOR tracking, and Beer-Lambert absorption controlled by transmission color and depth.
- An R-channel transmission texture for spatially varying StandardLit OpenPBR transmission.

### Changed

- The Bloom Volume inspector now shows only the settings used by the selected Scattering or Convolution FFT path.
- DLSS Ray Reconstruction reference-path inputs now emit finite, normalized ray guides and encode secondary misses with the FP16 maximum hit-distance sentinel.
- Removed StandardLit `_OpacityColor`; colored transparency is now authored exclusively through OpenPBR transmission controls.

- Renamed the `GTAO` Volume component type to `AmbientOcclusion` and added an implementation-aware custom inspector.

## [0.1.0] - 2026-02-24

### This is the first release of *\<VividRP\>*.

*Short description of this release*

"""Orthogonal spatial/temporal seam-noise diagnostics for Screen Probe GI & TSR.

Separates three previously conflated effects near geometric seams:
1. Full period-P (default 8-frame TSR/probe cycle, harmonics k = m * T/P including
   k = 24 and Nyquist k = 32) vs non-periodic stochastic frame-difference noise.
2. Seam-parallel 1D band breathing (k_x = 0, row-mean oscillation) vs within-row
   2D spatial blotch noise (k_x != 0), satisfying exact row-wise Pythagoras.
3. Surface-isolated 2D 8-32 px bandpass via symmetric mirror extension across the
   last/first stable row of each surface, eliminating cross-seam FFT ringing from
   subpixel jittering seam rows (d = +-0.5 px) and opposite-surface noise.
"""
from __future__ import annotations

import numpy as np
from scipy import fft


def radial_bandpass_transfer(
    shape: tuple[int, int],
    low_freq: float = 1.0 / 32.0,
    high_freq: float = 1.0 / 8.0,
    taper: float = 0.20,
) -> np.ndarray:
    """Tapered cosine radial bandpass filter in 2D rfft frequency space."""
    ys, xs = shape
    radius = np.hypot(fft.fftfreq(ys)[:, None], fft.rfftfreq(xs)[None, :])
    low = np.clip((radius - low_freq) / (low_freq * taper), 0.0, 1.0)
    high = np.clip((high_freq - radius) / (high_freq * taper), 0.0, 1.0)
    return (0.25 * (1.0 - np.cos(np.pi * low)) * (1.0 - np.cos(np.pi * high))).astype(np.float32)


def apply_bandpass_2d(
    frames: np.ndarray,
    transfer: np.ndarray | None = None,
    pad: int = 32,
) -> np.ndarray:
    """Apply the 2D radial bandpass frame-by-frame with reflect boundary padding."""
    arr = np.asarray(frames, dtype=np.float32)
    single = arr.ndim == 2
    if single:
        arr = arr[None, ...]
    if arr.ndim != 3:
        raise ValueError(f"Expected 2D or 3D array, got shape {arr.shape}")

    _, height, width = arr.shape
    padded_shape = (height + 2 * pad, width + 2 * pad)
    if transfer is None:
        transfer = radial_bandpass_transfer(padded_shape)
    elif transfer.shape != (padded_shape[0], padded_shape[1] // 2 + 1):
        raise ValueError(f"Transfer shape {transfer.shape} does not match padded shape {padded_shape}")

    padded = np.pad(arr, ((0, 0), (pad, pad), (pad, pad)), mode="reflect")
    filtered = fft.irfft2(fft.rfft2(padded, axes=(-2, -1)) * transfer, s=padded_shape, axes=(-2, -1))
    cropped = filtered[:, pad:-pad, pad:-pad]
    return cropped[0] if single else cropped


def find_seam_stable_boundary_row(
    surface_mask: np.ndarray,
    stable_mask: np.ndarray,
    x_mask: np.ndarray,
    seam_local_y: float,
    side: str,
) -> int:
    """Locate the Stable row closest to seam_local_y on the requested surface side."""
    if surface_mask.shape != stable_mask.shape or surface_mask.ndim != 2:
        raise ValueError("surface_mask and stable_mask must share 2D shape (H, W)")
    if x_mask.shape != (surface_mask.shape[1],) or not np.any(x_mask):
        raise ValueError("x_mask must be a non-empty 1D boolean mask of length W")

    row_valid = (surface_mask & stable_mask)[:, x_mask].all(axis=1)
    rows = np.arange(surface_mask.shape[0])
    if side == "upper":
        candidates = np.flatnonzero(row_valid & (rows < seam_local_y))
        if candidates.size == 0:
            raise ValueError("No fully stable upper-surface row found above seam")
        return int(candidates[-1])
    if side == "lower":
        candidates = np.flatnonzero(row_valid & (rows > seam_local_y))
        if candidates.size == 0:
            raise ValueError("No fully stable lower-surface row found below seam")
        return int(candidates[0])
    raise ValueError(f"Unsupported side '{side}'; expected 'upper' or 'lower'")


def mirror_extend_surface(
    frames: np.ndarray,
    boundary_row: int,
    side: str,
) -> np.ndarray:
    """Half-sample symmetric mirror extension across a horizontal surface boundary.

    For 'upper', rows <= boundary_row are preserved and rows > boundary_row are
    filled by symmetric reflection across boundary_row + 0.5.
    For 'lower', rows >= boundary_row are preserved and rows < boundary_row are
    filled by symmetric reflection across boundary_row - 0.5.
    """
    arr = np.asarray(frames)
    height = arr.shape[-2]
    if not (0 <= boundary_row < height):
        raise ValueError(f"boundary_row {boundary_row} out of range [0, {height})")

    row_indices = np.arange(height, dtype=np.int64)
    if side == "upper":
        span = boundary_row + 1
        over = row_indices > boundary_row
        mod = row_indices[over] % (2 * span)
        reflected = np.where(mod < span, mod, 2 * span - 1 - mod)
        row_indices[over] = reflected
    elif side == "lower":
        span = height - boundary_row
        under = row_indices < boundary_row
        k = boundary_row - 1 - row_indices[under]
        mod = k % (2 * span)
        offset = np.where(mod < span, mod, 2 * span - 1 - mod)
        row_indices[under] = boundary_row + offset
    else:
        raise ValueError(f"Unsupported side '{side}'; expected 'upper' or 'lower'")

    return np.take(arr, row_indices, axis=-2)


def compute_surface_isolated_bandpass(
    frames: np.ndarray,
    ceiling_mask: np.ndarray,
    wall_mask: np.ndarray,
    stable_mask: np.ndarray,
    x_mask: np.ndarray,
    seam_local_y: float,
    transfer: np.ndarray | None = None,
    pad: int = 32,
) -> tuple[np.ndarray, dict[str, int]]:
    """Compute 2D bandpass with ceiling and wall strictly isolated across the seam."""
    r_ceil = find_seam_stable_boundary_row(ceiling_mask, stable_mask, x_mask, seam_local_y, "upper")
    r_wall = find_seam_stable_boundary_row(wall_mask, stable_mask, x_mask, seam_local_y, "lower")
    if not (r_ceil < seam_local_y < r_wall):
        raise ValueError(f"Invalid seam boundary ordering: r_ceil={r_ceil}, seam={seam_local_y}, r_wall={r_wall}")

    ceil_ext = mirror_extend_surface(frames, r_ceil, "upper")
    wall_ext = mirror_extend_surface(frames, r_wall, "lower")
    bp_ceil = apply_bandpass_2d(ceil_ext, transfer=transfer, pad=pad)
    bp_wall = apply_bandpass_2d(wall_ext, transfer=transfer, pad=pad)

    isolated = np.zeros_like(bp_ceil)
    isolated = np.where(ceiling_mask[None, ...], bp_ceil, isolated)
    isolated = np.where(wall_mask[None, ...], bp_wall, isolated)
    return isolated, {"ceiling_last_stable_row": r_ceil, "wall_first_stable_row": r_wall}


def decompose_period(bp: np.ndarray, period: int = 8) -> dict[str, np.ndarray]:
    """Decompose (T, H, W) sequence into period-P waveform and aperiodic residual.

    Linear drift is estimated on cycle means so that:
    1. A pure linear ramp has zero periodic component (per8_wave == 0).
    2. A pure period-P oscillation has zero linear slope and zero stochastic residual.
    3. per8_seq + stoch_seq == bp identically at every frame and pixel.
    """
    arr = np.asarray(bp, dtype=np.float64)
    if arr.ndim != 3:
        raise ValueError(f"Expected 3D sequence (T, H, W), got {arr.shape}")
    num_frames = arr.shape[0]
    if period < 2 or num_frames % period != 0 or num_frames < 2 * period:
        raise ValueError(f"num_frames={num_frames} must be an integer multiple >= 2*period ({period})")

    num_cycles = num_frames // period
    cycle_means = arr.reshape(num_cycles, period, *arr.shape[1:]).mean(axis=1)
    if num_cycles >= 2:
        cycle_clock = (np.arange(num_cycles, dtype=np.float64) - 0.5 * (num_cycles - 1)) * period
        slope = np.tensordot(cycle_clock, cycle_means - cycle_means.mean(axis=0), axes=(0, 0)) / np.dot(
            cycle_clock, cycle_clock
        )
    else:
        slope = np.zeros(arr.shape[1:], dtype=np.float64)

    frame_clock = np.arange(num_frames, dtype=np.float64) - 0.5 * (num_frames - 1)
    detrended = arr - arr.mean(axis=0) - frame_clock[:, None, None] * slope

    per8_wave = detrended.reshape(num_cycles, period, *arr.shape[1:]).mean(axis=0)
    per8_wave -= per8_wave.mean(axis=0)
    per8_seq = np.tile(per8_wave, (num_cycles, 1, 1))
    stoch_seq = arr - per8_seq

    # Exact Parseval harmonic decomposition of the period-P template and its cyclic frame diff.
    wave_spec = fft.rfft(per8_wave, axis=0)
    num_harmonics = period // 2
    seq_harmonic_power = np.zeros((num_harmonics, *arr.shape[1:]), dtype=np.float64)
    diff_harmonic_power = np.zeros((num_harmonics, *arr.shape[1:]), dtype=np.float64)
    for m in range(1, num_harmonics + 1):
        weight = 1.0 if (period % 2 == 0 and m == period // 2) else 2.0
        p_seq = weight * (np.abs(wave_spec[m]) / period) ** 2
        diff_gain = 4.0 * (np.sin(np.pi * m / period) ** 2)
        seq_harmonic_power[m - 1] = p_seq
        diff_harmonic_power[m - 1] = diff_gain * p_seq

    return {
        "detrended": detrended,
        "per8_wave": per8_wave,
        "per8_seq": per8_seq,
        "stoch_seq": stoch_seq,
        "seq_harmonic_power": seq_harmonic_power,
        "diff_harmonic_power": diff_harmonic_power,
    }


def decompose_kx_over_mask(values: np.ndarray, mask: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Orthogonally split (..., H, W) on 2D mask into row-mean (kx=0) and residual (kx!=0).

    Returns 1D flattened views over `mask` of shape (..., N_masked_pixels) whose
    inner product along the masked-pixel axis is identically zero on every frame.
    """
    arr = np.asarray(values, dtype=np.float64)
    if mask.ndim != 2 or arr.shape[-2:] != mask.shape:
        raise ValueError(f"Shape mismatch between values {arr.shape} and mask {mask.shape}")

    kx0_full = np.zeros_like(arr, dtype=np.float64)
    for row in np.flatnonzero(mask.any(axis=1)):
        cols = mask[row]
        row_mean = arr[..., row, cols].mean(axis=-1, keepdims=True)
        kx0_full[..., row, cols] = row_mean

    masked_total = arr[..., mask]
    masked_kx0 = kx0_full[..., mask]
    masked_kx_nonzero = masked_total - masked_kx0
    return masked_kx0, masked_kx_nonzero


def _legacy_detrended_spectrum_metrics(bp: np.ndarray, mask: np.ndarray, mean_value: float, period: int = 8) -> dict[str, float]:
    """Exact match to legacy summarize-seam.py OLS detrend + k = T/period bin."""
    num_frames = bp.shape[0]
    bin_fund = num_frames // period
    clock = np.arange(num_frames, dtype=np.float32) - 0.5 * (num_frames - 1)
    centered = bp - bp.mean(axis=0)
    slope = np.tensordot(clock, centered, axes=(0, 0)) / np.dot(clock, clock)
    centered = centered - clock[:, None, None] * slope
    spectrum = fft.rfft(centered, axis=0)
    power_fund = 2.0 * (np.abs(spectrum[bin_fund]) / num_frames) ** 2
    temporal_energy = np.mean(centered * centered, axis=0, dtype=np.float64)

    rms_fund = float(np.sqrt(power_fund[mask].mean(dtype=np.float64)))
    denom = float(temporal_energy[mask].sum(dtype=np.float64))
    pct_scale = 100.0 / mean_value if mean_value != 0.0 else 0.0
    return {
        "detrended_8_frame_rms_pct_mean": rms_fund * pct_scale,
        "detrended_8_frame_power_pct": 100.0 * float(power_fund[mask].sum(dtype=np.float64)) / denom if denom > 0.0 else 0.0,
    }


def _compute_band_decomposition(
    bp: np.ndarray,
    mask: np.ndarray,
    mean_value: float,
    period: int = 8,
    temporal_parts: dict[str, np.ndarray] | None = None,
) -> dict[str, float | dict[str, float]]:
    """Compute orthogonal temporal (per8/stoch) x spatial (kx=0/kx!=0) diff metrics."""
    if temporal_parts is None:
        temporal_parts = decompose_period(bp, period=period)

    num_frames = bp.shape[0]
    bin_fund = num_frames // period
    pct_scale = 100.0 / mean_value if mean_value != 0.0 else 0.0

    # Cycle-mean-detrended sequence energy (immune to periodic/sawtooth slope bias).
    detrended = temporal_parts["detrended"]
    seq_per8_power = temporal_parts["seq_harmonic_power"].sum(axis=0, dtype=np.float64)
    seq_total_power = np.mean(detrended * detrended, axis=0, dtype=np.float64)
    seq_per8_rms = float(np.sqrt(seq_per8_power[mask].mean(dtype=np.float64)))
    seq_denom = float(seq_total_power[mask].sum(dtype=np.float64))
    detrended_per8_power_pct = 100.0 * float(seq_per8_power[mask].sum(dtype=np.float64)) / seq_denom if seq_denom > 0.0 else 0.0

    diffs_total = np.diff(bp.astype(np.float64), axis=0)
    diffs_per8 = np.diff(temporal_parts["per8_seq"], axis=0)
    diffs_stoch = np.diff(temporal_parts["stoch_seq"], axis=0)

    var_total = float(np.mean(diffs_total[:, mask] ** 2, dtype=np.float64))
    var_per8 = float(np.mean(diffs_per8[:, mask] ** 2, dtype=np.float64))
    var_stoch = float(np.mean(diffs_stoch[:, mask] ** 2, dtype=np.float64))
    var_temporal_sum = var_per8 + var_stoch

    rms_total = float(np.sqrt(var_total))
    rms_per8 = float(np.sqrt(var_per8))
    rms_stoch = float(np.sqrt(var_stoch))
    per8_power_pct = 100.0 * var_per8 / var_temporal_sum if var_temporal_sum > 0.0 else 0.0
    stoch_power_pct = 100.0 * var_stoch / var_temporal_sum if var_temporal_sum > 0.0 else 0.0

    # Harmonic split of the period-P diff energy (scaled to sum to per8_power_pct).
    harm_powers = temporal_parts["diff_harmonic_power"][:, mask].mean(axis=-1, dtype=np.float64)
    harm_sum = float(harm_powers.sum(dtype=np.float64))
    harmonics_pct: dict[str, float] = {}
    for m in range(1, period // 2 + 1):
        k = m * bin_fund
        share = float(harm_powers[m - 1]) / harm_sum if harm_sum > 0.0 else 0.0
        harmonics_pct[f"k{k}"] = per8_power_pct * share

    # Exact row-wise spatial split (kx=0 vs kx!=0) on total, per8, and stoch diffs.
    kx0_total, kxn_total = decompose_kx_over_mask(diffs_total, mask)
    _, kxn_per8 = decompose_kx_over_mask(diffs_per8, mask)
    kx0_stoch, kxn_stoch = decompose_kx_over_mask(diffs_stoch, mask)

    var_kx0 = float(np.mean(kx0_total ** 2, dtype=np.float64))
    var_kxn = float(np.mean(kxn_total ** 2, dtype=np.float64))
    rms_kx0 = float(np.sqrt(var_kx0))
    rms_kxn = float(np.sqrt(var_kxn))

    rms_kx0_per8 = float(np.sqrt(max(var_per8 - float(np.mean(kxn_per8 ** 2, dtype=np.float64)), 0.0)))
    rms_kxn_per8 = float(np.sqrt(np.mean(kxn_per8 ** 2, dtype=np.float64)))
    rms_kx0_stoch = float(np.sqrt(np.mean(kx0_stoch ** 2, dtype=np.float64)))
    rms_kxn_stoch = float(np.sqrt(np.mean(kxn_stoch ** 2, dtype=np.float64)))

    return {
        "band_frame_difference_rms": rms_total,
        "band_frame_difference_rms_pct_mean": rms_total * pct_scale,
        "detrended_per8_rms_pct_mean": seq_per8_rms * pct_scale,
        "detrended_per8_power_pct": detrended_per8_power_pct,
        "per8_frame_difference_rms": rms_per8,
        "per8_frame_difference_rms_pct_mean": rms_per8 * pct_scale,
        "per8_frame_difference_power_pct": per8_power_pct,
        "per8_harmonic_diff_power_pct": harmonics_pct,
        "stoch_frame_difference_rms": rms_stoch,
        "stoch_frame_difference_rms_pct_mean": rms_stoch * pct_scale,
        "stoch_frame_difference_power_pct": stoch_power_pct,
        "kx0_frame_difference_rms": rms_kx0,
        "kx0_frame_difference_rms_pct_mean": rms_kx0 * pct_scale,
        "kx0_frame_difference_power_pct": 100.0 * var_kx0 / var_total if var_total > 0.0 else 0.0,
        "kx_nonzero_frame_difference_rms": rms_kxn,
        "kx_nonzero_frame_difference_rms_pct_mean": rms_kxn * pct_scale,
        "kx_nonzero_frame_difference_power_pct": 100.0 * var_kxn / var_total if var_total > 0.0 else 0.0,
        "kx0_per8_frame_difference_rms": rms_kx0_per8,
        "kx0_per8_frame_difference_rms_pct_mean": rms_kx0_per8 * pct_scale,
        "kx0_stoch_frame_difference_rms": rms_kx0_stoch,
        "kx0_stoch_frame_difference_rms_pct_mean": rms_kx0_stoch * pct_scale,
        "kx_nonzero_per8_frame_difference_rms": rms_kxn_per8,
        "kx_nonzero_per8_frame_difference_rms_pct_mean": rms_kxn_per8 * pct_scale,
        "kx_nonzero_stoch_frame_difference_rms": rms_kxn_stoch,
        "kx_nonzero_stoch_frame_difference_rms_pct_mean": rms_kxn_stoch * pct_scale,
    }


def summarize_stage_regions(
    raw: np.ndarray,
    bp: np.ndarray,
    bp_isolated: np.ndarray,
    areas: dict[str, np.ndarray],
    period: int = 8,
) -> dict[str, dict]:
    """Compute legacy + P0 orthogonal metrics for every named region mask."""
    bp_parts = decompose_period(bp, period=period)
    iso_parts = decompose_period(bp_isolated, period=period)

    results: dict[str, dict] = {}
    for name, mask in areas.items():
        mu = float(raw[:, mask].mean(dtype=np.float64))
        legacy_spec = _legacy_detrended_spectrum_metrics(bp, mask, mu, period=period)
        raw_decomp = _compute_band_decomposition(bp, mask, mu, period=period, temporal_parts=bp_parts)
        iso_decomp = _compute_band_decomposition(bp_isolated, mask, mu, period=period, temporal_parts=iso_parts)

        results[name] = {
            "pixels": int(mask.sum()),
            "mean": mu,
            "band_frame_difference_rms": raw_decomp["band_frame_difference_rms"],
            "band_frame_difference_rms_pct_mean": raw_decomp["band_frame_difference_rms_pct_mean"],
            "detrended_8_frame_rms_pct_mean": legacy_spec["detrended_8_frame_rms_pct_mean"],
            "detrended_8_frame_power_pct": legacy_spec["detrended_8_frame_power_pct"],
            **{k: v for k, v in raw_decomp.items() if not k.startswith("band_frame_difference_rms")},
            "surface_isolated": iso_decomp,
        }
    return results


def compute_row_profiles(
    bp: np.ndarray,
    bp_isolated: np.ndarray,
    band_area: np.ndarray,
    period: int = 8,
) -> dict[str, np.ndarray]:
    """Compute 1D vertical profiles versus seam distance for legacy & P0 components."""
    height = band_area.shape[0]
    keys = (
        "legacy_total",
        "isolated_total",
        "isolated_kx0",
        "isolated_kx_nonzero",
        "isolated_per8",
        "isolated_stoch",
        "isolated_kx_nonzero_stoch",
    )
    profiles = {k: np.full(height, np.nan, dtype=np.float64) for k in keys}

    diffs_legacy = np.diff(bp.astype(np.float64), axis=0)
    iso_parts = decompose_period(bp_isolated, period=period)
    diffs_iso = np.diff(bp_isolated.astype(np.float64), axis=0)
    diffs_iso_per8 = np.diff(iso_parts["per8_seq"], axis=0)
    diffs_iso_stoch = np.diff(iso_parts["stoch_seq"], axis=0)

    for row in np.flatnonzero(band_area.any(axis=1)):
        cols = band_area[row]
        profiles["legacy_total"][row] = np.sqrt(np.mean(diffs_legacy[:, row, cols] ** 2, dtype=np.float64))

        row_iso = diffs_iso[:, row, cols]
        row_iso_mean = row_iso.mean(axis=-1, keepdims=True)
        row_iso_kxn = row_iso - row_iso_mean
        profiles["isolated_total"][row] = np.sqrt(np.mean(row_iso ** 2, dtype=np.float64))
        profiles["isolated_kx0"][row] = np.sqrt(np.mean(row_iso_mean ** 2, dtype=np.float64))
        profiles["isolated_kx_nonzero"][row] = np.sqrt(np.mean(row_iso_kxn ** 2, dtype=np.float64))

        profiles["isolated_per8"][row] = np.sqrt(np.mean(diffs_iso_per8[:, row, cols] ** 2, dtype=np.float64))
        row_stoch = diffs_iso_stoch[:, row, cols]
        row_stoch_kxn = row_stoch - row_stoch.mean(axis=-1, keepdims=True)
        profiles["isolated_stoch"][row] = np.sqrt(np.mean(row_stoch ** 2, dtype=np.float64))
        profiles["isolated_kx_nonzero_stoch"][row] = np.sqrt(np.mean(row_stoch_kxn ** 2, dtype=np.float64))

    return profiles

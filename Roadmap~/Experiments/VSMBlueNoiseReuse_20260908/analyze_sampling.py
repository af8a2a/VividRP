"""Read existing textures and evaluate sampling only; no Unity/SMRT/TSR execution.

Constant disk half-plane integrals have independent analytic references. These
measure distribution/convergence, not scene shadow correctness or GPU cost.
"""
from pathlib import Path
import hashlib
import json
import math

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(__file__).resolve().parent
N = 128
FRAMES = 256


def texture(name):
    # Unity texture (0,0) is the PNG's bottom-left. RGB conversion resolves the
    # indexed/1-bit PNG assets before red-channel UNorm decoding.
    return np.asarray(Image.open(ROOT / 'Texture/BlueNoise' / (name + '.png')).convert('RGB'))[::-1, :, 0].copy()


def hash32(x):
    x = np.asarray(x, dtype=np.uint32)
    x = x ^ (x >> 16)
    x = np.multiply(x, np.uint32(0x7feb352d), dtype=np.uint32)
    x = x ^ (x >> 15)
    x = np.multiply(x, np.uint32(0x846ca68b), dtype=np.uint32)
    return x ^ (x >> 16)


def current_disk(rays):
    yy, xx = np.mgrid[:N, :N].astype(np.uint32)
    seed = hash32(xx ^ hash32(yy + np.uint32(0x9e3779b9)) ^ hash32(np.uint32(0x68bc21eb)))
    f = np.arange(FRAMES, dtype=np.float32)[:, None, None, None]
    ray = np.arange(rays, dtype=np.float32)[None, :, None, None]
    rotation0 = (seed >> 8).astype(np.float32)[None, None] / 16777216.0
    rotation1 = (hash32(seed) >> 8).astype(np.float32)[None, None] / 16777216.0
    phase0 = np.mod(rotation0 + f * np.float32(0.754877666), 1)
    phase1 = np.mod(rotation1 + f * np.float32(0.569840296), 1)
    radius = np.sqrt((ray + phase0) / rays)
    angle = np.float32(2 * math.pi) * np.mod(ray * np.float32(0.618033989) + phase1, 1)
    return radius * np.cos(angle), radius * np.sin(angle)


def bnd_scalar(kind, indices, dimension):
    yy, xx = np.mgrid[:N, :N]
    address = (xx + yy * N) * 8 + (dimension & 7)
    scramble = texture('ScramblingTile' + kind + 'SPP').reshape(-1)[address].astype(np.float32) / 255
    ranking = texture('RankingTile' + kind + 'SPP').reshape(-1)[address].astype(np.float32) / 255
    mask = 7 if kind == '8' else 255
    rank = np.clip((ranking * 256).astype(np.uint32), 0, mask)
    index = (indices[:, :, None, None] & mask) ^ rank[None, None]
    sobol = texture('SobolOwenScrambled256').astype(np.float32) / 255
    value = np.clip((sobol[index, dimension & 255] * 256).astype(np.uint32), 0, 255)
    scramble = np.minimum(scramble, np.float32(.999))
    value ^= (scramble[None, None] * 256).astype(np.uint32)
    return np.minimum((np.maximum(.001, scramble[None, None]) + value) / 256, .99999994).astype(np.float32)


def bnd_disk(kind, rays):
    indices = np.arange(FRAMES * rays, dtype=np.uint32).reshape(FRAMES, rays)
    u = bnd_scalar(kind, indices, 0)
    v = bnd_scalar(kind, indices, 1)
    radius = np.sqrt(u)
    angle = np.float32(2 * math.pi) * v
    return radius * np.cos(angle), radius * np.sin(angle)


def bnd_phase_disk(kind, rays):
    indices = np.arange(FRAMES, dtype=np.uint32)[:, None]
    phase0 = bnd_scalar(kind, indices, 0)
    phase1 = bnd_scalar(kind, indices, 1)
    ray = np.arange(rays, dtype=np.float32)[None, :, None, None]
    radius = np.sqrt((ray + phase0) / rays)
    angle = np.float32(2 * math.pi) * np.mod(ray * np.float32(0.618033989) + phase1, 1)
    return radius * np.cos(angle), radius * np.sin(angle)


def summarize(values):
    return {'median': float(np.median(values)), 'min': float(np.min(values)), 'max': float(np.max(values))}


def tent3(a):
    horizontal = (np.roll(a, 1, axis=-1) + 2*a + np.roll(a, -1, axis=-1)) * .25
    return (np.roll(horizontal, 1, axis=-2) + 2*horizontal + np.roll(horizontal, -1, axis=-2)) * .25


def main():
    assets = []
    for p in sorted((ROOT / 'Texture/BlueNoise').glob('*.png')):
        im = Image.open(p)
        a = np.asarray(im.convert('RGB'))[..., 0]
        assets.append({'name': p.name, 'size': list(im.size), 'red_min': int(a.min()), 'red_max': int(a.max()),
                       'sha256': hashlib.sha256(p.read_bytes()).hexdigest()})
    report = {'scope': __doc__, 'frames': FRAMES, 'grid': [N, N], 'assets': assets,
              'source_shader_sha256': hashlib.sha256((ROOT / 'Shaders/Core/Public/BlueNoise.hlsl').read_bytes()).hexdigest(),
              'note': 'CPU float32 approximation of texture loads/math; not bitwise GPU validation. No receiver-origin jitter, depth data, history rejection, or TSR. Direct BND256 and BND1Temporal use (frame*rays+ray)&255. BND8 uses the existing &7 mask. Phase variants use frame&255 for a shared per-pixel phase, preserving current radial strata and golden-angle spacing.',
              'integrands': [], 'results': {}}
    cases = [(a, h) for a in (0., .37, .91, 1.43) for h in (-.6, -.2, .2, .6)]
    report['integrands'] = [{'normal_angle': a, 'threshold': h, 'expected': (math.acos(h) - h*math.sqrt(1-h*h))/math.pi} for a,h in cases]
    freq = np.fft.fftfreq(N)
    radius = np.sqrt(freq[:, None]**2 + freq[None, :]**2)
    low = (radius > 0) & (radius <= .125)
    all_nonzero = radius > 0
    for rays in (4, 8):
        for label, kind in [('current', None), ('bnd256', '256'), ('bnd1_temporal', '1'), ('bnd8_masked', '8'), ('bnd256_phase', '256'), ('bnd1_phase', '1')]:
            phase_mode = label.endswith('_phase')
            x, y = current_disk(rays) if kind is None else bnd_phase_disk(kind, rays) if phase_mode else bnd_disk(kind, rays)
            period = None if kind is None else 256 if phase_mode else (8 if kind == '8' else 256) // math.gcd(rays, 8 if kind == '8' else 256)
            result = {'rays': rays, 'angular_sample_period_frames': period,
                      'first_frame_rmse': [], 'low_spatial_energy_fraction': [], 'low_spatial_band_rmse': [], 'box_average_rmse': {str(k): [] for k in (4, 8, 16, 32)}, 'ema_096_rmse_last16': [], 'ema_096_tent3_rmse_last16': []}
            for a, h in cases:
                expected = (math.acos(h) - h * math.sqrt(1-h*h)) / math.pi
                visibility = (x * math.cos(a) + y * math.sin(a) > h).mean(axis=1, dtype=np.float32)
                error = visibility - expected
                result['first_frame_rmse'].append(float(np.sqrt(np.mean(error[0]**2))))
                fft = np.fft.fft2(error[:16], axes=(-2, -1))
                power = np.abs(fft)**2
                result['low_spatial_energy_fraction'].append(float(power[:, low].sum() / max(power[:, all_nonzero].sum(), 1e-30)))
                result['low_spatial_band_rmse'].append(float(np.sqrt(power[:, low].sum() / (16*N**4))))
                for k in (4, 8, 16, 32):
                    blocks = error.reshape(FRAMES//k, k, N, N).mean(axis=1)
                    result['box_average_rmse'][str(k)].append(float(np.sqrt(np.mean(blocks**2))))
                # Keep only the final 16 of 256 EMA frames, suppressing startup
                # error. This omits actual TSR nonlinear clipping/rejection.
                history = visibility[0].copy()
                errors = []
                filtered_errors = []
                for frame in range(1, FRAMES):
                    history = history * .96 + visibility[frame] * .04
                    if frame >= FRAMES-16:
                        errors.append(float(np.mean((history-expected)**2)))
                        filtered_errors.append(float(np.mean((tent3(history)-expected)**2)))
                result['ema_096_rmse_last16'].append(float(np.sqrt(np.mean(errors))))
                result['ema_096_tent3_rmse_last16'].append(float(np.sqrt(np.mean(filtered_errors))))
            result = {key: ({n:summarize(v) for n,v in value.items()} if key == 'box_average_rmse' else summarize(value) if isinstance(value,list) else value) for key,value in result.items()}
            report['results'][f'{label}_{rays}'] = result
            print(label, rays, 'period', result['angular_sample_period_frames'], 'single', round(result['first_frame_rmse']['median'],5), 'box16',round(result['box_average_rmse']['16']['median'],5), 'lowfreq',round(result['low_spatial_energy_fraction']['median'],5),flush=True)
    (OUT / 'sampling-analysis.json').write_text(json.dumps(report, indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()

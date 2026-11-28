#!/usr/bin/env python3
"""
Houdini 22 hython baker for VividRP FurSlab DCC assets (Phase D2).

Builds a real Houdini SOP network (/obj/vivid_fur_baker) with procedural 3D
groom curves, evaluates slice intersections and tangent-space flow fields via
Houdini geometry (hou.Geometry), writes linear RGBA PNGs conforming strictly to
VividFurSurface.hlsl, and optionally saves an editable .hipnc/.hip file.

Usage (inside Houdini 22 hython):
    hython Tools~/Houdini/Bake-VividFurPatterns.py ^
        --out Editor/SceneTemplates/Outdoors/Textures/Fur ^
        --save-hip Tools~/Houdini/VividFur_PatternBaker.hipnc ^
        --resolution 512 --flipbook-resolution 1024 --grid 4 4

Offline contract self-test (no Houdini license required):
    python Tools~/Houdini/Bake-VividFurPatterns.py --self-test
"""

from __future__ import annotations

import argparse
import math
import os
import random
import struct
import sys
import zlib
from typing import List, Tuple

try:
    import numpy as np
except ImportError:
    np = None  # type: ignore[assignment]


# ---------------------------------------------------------------------------
# Minimal zero-dependency lossless linear RGBA PNG writer (8-bit or 16-bit)
# Expects image arrays indexed with row 0 = bottom (Unity UV v=0), and flips
# vertically on write so Unity's bottom-up UV mapping samples row 0 at v=0.
# ---------------------------------------------------------------------------

def _png_chunk(chunk_type: bytes, data: bytes) -> bytes:
    crc = zlib.crc32(chunk_type + data) & 0xFFFFFFFF
    return struct.pack(">I", len(data)) + chunk_type + data + struct.pack(">I", crc)


def write_linear_rgba_png(path: str, rgba_bottom_up: "np.ndarray", bit_depth: int = 8) -> None:
    if np is None:
        raise RuntimeError("numpy is required to write PNG images.")
    if rgba_bottom_up.ndim != 3 or rgba_bottom_up.shape[2] != 4:
        raise ValueError(f"Expected (H, W, 4) array, got {rgba_bottom_up.shape}")
    if bit_depth not in (8, 16):
        raise ValueError(f"Unsupported PNG bit depth: {bit_depth}")

    height, width, _ = rgba_bottom_up.shape
    # Flip vertically so bottom-up UV (v=0 at row 0) maps to PNG top-down scanlines.
    top_down = np.flipud(np.clip(rgba_bottom_up, 0.0, 1.0))

    if bit_depth == 8:
        quantized = np.rint(top_down * 255.0).astype(np.uint8)
        raw_rows = [b"\x00" + quantized[y].tobytes() for y in range(height)]
    else:
        quantized = np.rint(top_down * 65535.0).astype(">u2")
        raw_rows = [b"\x00" + quantized[y].tobytes() for y in range(height)]

    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    ihdr = struct.pack(">IIBBBBB", width, height, bit_depth, 6, 0, 0, 0)
    idat = zlib.compress(b"".join(raw_rows), level=6)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(_png_chunk(b"IHDR", ihdr))
        # Explicit gAMA = 1.0 (100000) to mark linear data for external viewers.
        f.write(_png_chunk(b"gAMA", struct.pack(">I", 100000)))
        f.write(_png_chunk(b"IDAT", idat))
        f.write(_png_chunk(b"IEND", b""))


def compute_flipbook_slice_uv(
    u: float,
    v: float,
    tiling_u: float,
    tiling_v: float,
    cols: int,
    rows: int,
    slice_index: int,
) -> Tuple[float, float]:
    """Python mirror of VividFurComputeFlipbookSliceUV in VividFurSurface.hlsl."""
    max_slice = max(cols * rows - 1, 0)
    clamped = min(max(slice_index, 0), max_slice)
    col = clamped % cols
    row = clamped // cols
    tiled_u = (u * tiling_u) % 1.0
    tiled_v = (v * tiling_v) % 1.0
    safe_u = 0.004 + (0.996 - 0.004) * tiled_u
    safe_v = 0.004 + (0.996 - 0.004) * tiled_v
    return (col + safe_u) / float(cols), (row + safe_v) / float(rows)


# ---------------------------------------------------------------------------
# Houdini 22 SOP Network Builders & Geometry Samplers
# ---------------------------------------------------------------------------

def _require_houdini():
    try:
        import hou  # type: ignore
    except ImportError as exc:
        raise RuntimeError(
            "This baking mode must be executed via Houdini's hython.exe "
            "(e.g. 'E:\\Houdini 22.0.368\\bin\\hython.exe Tools~/Houdini/Bake-VividFurPatterns.py')."
        ) from exc
    return hou


def _build_flow_whorl_sop(hou, parent_geo, resolution: int):
    """Builds a Houdini SOP UV grid + Attribute Wrangle generating periodic swirl FlowMap."""
    grid = parent_geo.createNode("grid", "flow_uv_grid")
    grid.parm("orient").set(0)  # XY plane
    grid.parm("sizex").set(1.0)
    grid.parm("sizey").set(1.0)
    grid.parm("tx").set(0.5)
    grid.parm("ty").set(0.5)
    grid.parm("rows").set(resolution)
    grid.parm("cols").set(resolution)

    wrangle = parent_geo.createNode("attribwrangle", "flow_whorl_wrangle")
    wrangle.setInput(0, grid)
    wrangle.parm("snippet").set(
        """
// VividRP _FurFlowMap (Linear RGBA):
// R,G = tangent-space comb flow in [0,1] -> [-1,1]
// B   = loft override (0 = combed flat, 1 = upright)
// A   = local length scale multiplier
float u = clamp(@P.x, 0.0, 1.0);
float v = clamp(@P.y, 0.0, 1.0);

float sx = sin(u * 6.28318530718);
float sy = sin((v - 0.5) * 3.14159265359);
float angle = atan2(sy, sx) + 1.15;
float fx = cos(angle);
float fy = sin(angle);
float r = length(set(sx, sy)) * 0.70710678;

float loft = lerp(0.85, 0.15, clamp(r * 1.4, 0.0, 1.0));
float lenScale = lerp(0.45, 1.0, smooth(0.05, 0.45, r));

v@vivid_flow_rgba = set(fx * 0.5 + 0.5, fy * 0.5 + 0.5, loft);
f@vivid_flow_a = lenScale;
"""
    )

    out_null = parent_geo.createNode("null", "OUT_FLOW_MAP")
    out_null.setInput(0, wrangle)
    return out_null


def _build_braided_2d_pattern_sop(hou, parent_geo, resolution: int):
    """Builds a Houdini SOP braided plush diamond weave field via VEX Attribute Wrangle."""
    grid = parent_geo.createNode("grid", "pattern2d_uv_grid")
    grid.parm("orient").set(0)
    grid.parm("sizex").set(1.0)
    grid.parm("sizey").set(1.0)
    grid.parm("tx").set(0.5)
    grid.parm("ty").set(0.5)
    grid.parm("rows").set(resolution)
    grid.parm("cols").set(resolution)

    wrangle = parent_geo.createNode("attribwrangle", "braided_plush_wrangle")
    wrangle.setInput(0, grid)
    wrangle.parm("snippet").set(
        """
// VividRP _FurPatternMap Mode 1 (2D Pattern Map, Linear RGBA):
// R   = strand height field [0,1]
// G,B = tangent-space micro-normal / strand offset [0,1] -> [-1,1]
// A   = per-strand deterministic variation ID [0,1]
float u = clamp(@P.x, 0.0, 1.0);
float v = clamp(@P.y, 0.0, 1.0);
float pi = 3.14159265359;

float d1 = abs(sin((u + v) * pi * 2.0));
float d2 = abs(sin((u - v) * pi * 2.0));
float ridge = max(d1, d2);
float tuft = 0.5 + 0.5 * sin(u * pi * 24.0) * sin(v * pi * 24.0);
float h = clamp(pow(ridge, 0.65) * (0.72 + 0.28 * tuft), 0.0, 1.0);

// Analytical partial derivatives of the dominant braided cord
float ox = cos((u + v) * pi * 4.0) * 0.5 + 0.5;
float oy = sin((u - v) * pi * 4.0) * 0.5 + 0.5;

// Tileable cellular ID hash on 16x16 tuft lattice
vector cell = floor(set(u * 16.0, v * 16.0, 0.0));
float idVal = clamp(0.25 + 0.75 * rand(cell + set(13.0, 29.0, 0.0)), 0.0, 1.0);

v@vivid_pat2d_rgb = set(h, ox, oy);
f@vivid_pat2d_a = idVal;
"""
    )

    out_null = parent_geo.createNode("null", "OUT_PATTERN_2D")
    out_null.setInput(0, wrangle)
    return out_null


def _build_curly_wool_curves_sop(hou, parent_geo, strand_count: int = 25, points_per_strand: int = 24):
    """
    Builds real 3D helical lambskin wool curves in Houdini SOPs (Python SOP + PolyFrame)
    on a tileable [0,1]x[0,1] UV patch with Y in [0,1] representing normalized fur height.
    Artists can swap this node with a Houdini GuideGroom / HairGen node directly.
    """
    py_sop = parent_geo.createNode("python", "curly_wool_groom_curves")
    py_sop.parm("python").set(
        f"""
import math, random, hou

node = hou.pwd()
geo = node.geometry()
geo.addAttrib(hou.attribType.Point, "width", 0.05)
geo.addAttrib(hou.attribType.Point, "strand_id", 0.5)
geo.addAttrib(hou.attribType.Point, "tangent_uv", (0.0, 0.0, 0.0))

rng = random.Random(42)
grid_n = int(round(math.sqrt({strand_count})))
pts_per = {points_per_strand}

for idx in range(grid_n * grid_n):
    gx = idx % grid_n
    gy = idx // grid_n
    root_u = (gx + 0.18 + 0.64 * rng.random()) / float(grid_n)
    root_v = (gy + 0.18 + 0.64 * rng.random()) / float(grid_n)
    phase = rng.random() * math.tau
    max_len = 0.68 + 0.32 * rng.random()
    base_rad = 0.058 + 0.026 * rng.random()
    turns = 1.35 + 0.45 * rng.random()
    helix_r = 0.072 + 0.022 * rng.random()
    sid = rng.random()

    poly = geo.createPolygon()
    poly.setIsClosed(False)
    for k in range(pts_per):
        t = k / float(pts_per - 1)
        h = t * max_len
        theta = phase + h * math.tau * turns
        amp = helix_r * math.sin(min(t * 1.15, 1.0) * math.pi * 0.85)
        u = root_u + math.cos(theta) * amp
        v = root_v + math.sin(theta) * amp
        rad = base_rad * (1.0 - 0.62 * t * t)
        tu = -math.sin(theta)
        tv = math.cos(theta)

        pt = geo.createPoint()
        pt.setPosition((u, h, v))
        pt.setAttribValue("width", rad)
        pt.setAttribValue("strand_id", sid)
        pt.setAttribValue("tangent_uv", (tu, tv, 0.0))
        poly.addVertex(pt)
"""
    )

    out_null = parent_geo.createNode("null", "OUT_GROOM_CURVES")
    out_null.setInput(0, py_sop)
    return out_null


def _read_grid_rgba_from_sop(sop_node, resolution: int, rgb_attrib: str, a_attrib: str) -> "np.ndarray":
    geo = sop_node.geometry()
    rgb = np.array(geo.pointFloatAttribValues(rgb_attrib), dtype=np.float32).reshape(resolution, resolution, 3)
    alpha = np.array(geo.pointFloatAttribValues(a_attrib), dtype=np.float32).reshape(resolution, resolution, 1)
    return np.concatenate([rgb, alpha], axis=2)


def _bake_flipbook_from_houdini_curves(
    curves_sop,
    atlas_size: int,
    cols: int,
    rows: int,
) -> "np.ndarray":
    """
    Slices Houdini 3D groom curves (X=U, Y=Height in [0,1], Z=V) across cols*rows
    height planes and packs them into a row-major linear RGBA Flipbook Atlas
    matching VividFurComputeFlipbookSliceUV.
    """
    if atlas_size % cols != 0 or atlas_size % rows != 0:
        raise ValueError(f"atlas_size ({atlas_size}) must be divisible by grid ({cols}x{rows}).")

    cell_w = atlas_size // cols
    cell_h = atlas_size // rows
    total_slices = cols * rows

    geo = curves_sop.geometry()
    curves_data: List[Tuple["np.ndarray", "np.ndarray", "np.ndarray", float]] = []
    for prim in geo.prims():
        pts = prim.points()
        if len(pts) < 2:
            continue
        pos = np.array([p.position() for p in pts], dtype=np.float32)  # (N, 3): u, h, v
        rad = np.array([p.attribValue("width") for p in pts], dtype=np.float32)
        tuv = np.array([p.attribValue("tangent_uv")[:2] for p in pts], dtype=np.float32)
        sid = float(pts[0].attribValue("strand_id"))
        curves_data.append((pos, rad, tuv, sid))

    if not curves_data:
        raise RuntimeError("Houdini groom SOP produced zero curves.")

    # Pixel centers inside a single tile cell [0, 1]^2
    us = (np.arange(cell_w, dtype=np.float32) + 0.5) / float(cell_w)
    vs = (np.arange(cell_h, dtype=np.float32) + 0.5) / float(cell_h)
    uu, vv = np.meshgrid(us, vs)  # (cell_h, cell_w)

    atlas = np.zeros((atlas_size, atlas_size, 4), dtype=np.float32)
    atlas[:, :, 1] = 0.5
    atlas[:, :, 2] = 0.5
    atlas[:, :, 3] = 0.5

    for slice_idx in range(total_slices):
        h_target = slice_idx / float(max(total_slices - 1, 1))
        best_alpha = np.zeros((cell_h, cell_w), dtype=np.float32)
        best_ox = np.zeros((cell_h, cell_w), dtype=np.float32)
        best_oy = np.zeros((cell_h, cell_w), dtype=np.float32)
        best_id = np.full((cell_h, cell_w), 0.5, dtype=np.float32)

        for pos, rad, tuv, sid in curves_data:
            hs = pos[:, 1]
            if h_target < hs[0] - 1e-5 or h_target > hs[-1] + 1e-5:
                continue
            # Interpolate curve intersection at height plane h_target
            seg = int(np.searchsorted(hs, h_target, side="right")) - 1
            seg = max(0, min(seg, len(hs) - 2))
            h0, h1 = float(hs[seg]), float(hs[seg + 1])
            frac = 0.0 if abs(h1 - h0) < 1e-6 else max(0.0, min(1.0, (h_target - h0) / (h1 - h0)))

            cu = float(pos[seg, 0] + (pos[seg + 1, 0] - pos[seg, 0]) * frac)
            cv = float(pos[seg, 2] + (pos[seg + 1, 2] - pos[seg, 2]) * frac)
            cr = float(rad[seg] + (rad[seg + 1] - rad[seg]) * frac)
            tx = float(tuv[seg, 0] + (tuv[seg + 1, 0] - tuv[seg, 0]) * frac)
            ty = float(tuv[seg, 1] + (tuv[seg + 1, 1] - tuv[seg, 1]) * frac)

            # Periodic wrap distance on [0,1]^2 for seamless tiling
            dx = uu - (cu % 1.0)
            dx -= np.round(dx)
            dy = vv - (cv % 1.0)
            dy -= np.round(dy)
            dist = np.sqrt(dx * dx + dy * dy)

            feather = max(cr * 0.45, 0.008)
            alpha = np.clip((cr - dist) / feather, 0.0, 1.0)
            mask = alpha > best_alpha
            if np.any(mask):
                inv_r = 1.0 / max(cr, 1e-3)
                ox = np.clip(tx * 0.65 + (dx * inv_r) * 0.35, -1.0, 1.0)
                oy = np.clip(ty * 0.65 + (dy * inv_r) * 0.35, -1.0, 1.0)
                best_alpha[mask] = alpha[mask]
                best_ox[mask] = ox[mask]
                best_oy[mask] = oy[mask]
                best_id[mask] = sid

        col = slice_idx % cols
        row = slice_idx // cols
        y0 = row * cell_h
        x0 = col * cell_w
        atlas[y0 : y0 + cell_h, x0 : x0 + cell_w, 0] = best_alpha
        atlas[y0 : y0 + cell_h, x0 : x0 + cell_w, 1] = best_ox * 0.5 + 0.5
        atlas[y0 : y0 + cell_h, x0 : x0 + cell_w, 2] = best_oy * 0.5 + 0.5
        atlas[y0 : y0 + cell_h, x0 : x0 + cell_w, 3] = best_id

    return atlas


def run_houdini_bake(args: argparse.Namespace) -> None:
    if np is None:
        raise RuntimeError("numpy is unavailable in this Python environment.")
    hou = _require_houdini()
    hou.hipFile.clear(suppress_save_prompt=True)

    obj = hou.node("/obj")
    geo = obj.createNode("geo", "vivid_fur_baker")

    flow_out = _build_flow_whorl_sop(hou, geo, args.resolution)
    pat2d_out = _build_braided_2d_pattern_sop(hou, geo, args.resolution)
    curves_out = _build_curly_wool_curves_sop(hou, geo, strand_count=args.strands)
    geo.layoutChildren()

    flow_img = _read_grid_rgba_from_sop(flow_out, args.resolution, "vivid_flow_rgba", "vivid_flow_a")
    pat2d_img = _read_grid_rgba_from_sop(pat2d_out, args.resolution, "vivid_pat2d_rgb", "vivid_pat2d_a")
    flipbook_img = _bake_flipbook_from_houdini_curves(
        curves_out,
        atlas_size=args.flipbook_resolution,
        cols=args.grid[0],
        rows=args.grid[1],
    )

    out_dir = os.path.abspath(args.out)
    os.makedirs(out_dir, exist_ok=True)
    flow_path = os.path.join(out_dir, "VividFur_Flow_Swirl.png")
    pat2d_path = os.path.join(out_dir, "VividFur_Pattern2D_BraidedPlush.png")
    flipbook_path = os.path.join(out_dir, f"VividFur_Flipbook{args.grid[0]}x{args.grid[1]}_CurlyWool.png")

    write_linear_rgba_png(flow_path, flow_img, bit_depth=args.bit_depth)
    write_linear_rgba_png(pat2d_path, pat2d_img, bit_depth=args.bit_depth)
    write_linear_rgba_png(flipbook_path, flipbook_img, bit_depth=args.bit_depth)

    print(f"[Houdini {hou.applicationVersionString()} | {hou.licenseCategory()}]")
    print(f"  Baked FlowMap      -> {flow_path} ({args.resolution}x{args.resolution}, {args.bit_depth}-bit linear)")
    print(f"  Baked 2D Pattern   -> {pat2d_path} ({args.resolution}x{args.resolution}, {args.bit_depth}-bit linear)")
    print(f"  Baked 3D Flipbook  -> {flipbook_path} ({args.flipbook_resolution}x{args.flipbook_resolution}, {args.grid[0]}x{args.grid[1]} slices)")

    if args.save_hip:
        hip_path = os.path.abspath(args.save_hip)
        os.makedirs(os.path.dirname(hip_path), exist_ok=True)
        hou.hipFile.save(hip_path)
        print(f"  Saved Houdini scene -> {hip_path}")


# ---------------------------------------------------------------------------
# Offline contract self-test (no Houdini license required)
# ---------------------------------------------------------------------------

def run_self_test() -> None:
    import tempfile

    # 1. Verify flipbook slice UV contract matches VividFurComputeFlipbookSliceUV
    u0, v0 = compute_flipbook_slice_uv(0.5, 0.5, 1.0, 1.0, 4, 4, 0)
    assert abs(u0 - 0.125) < 1e-6 and abs(v0 - 0.125) < 1e-6, (u0, v0)
    u15, v15 = compute_flipbook_slice_uv(0.5, 0.5, 1.0, 1.0, 4, 4, 15)
    assert abs(u15 - 0.875) < 1e-6 and abs(v15 - 0.875) < 1e-6, (u15, v15)
    # Margin guard at wrap boundary (u=0 -> 0.004/4 = 0.001)
    ub, vb = compute_flipbook_slice_uv(0.0, 0.0, 1.0, 1.0, 4, 4, 0)
    assert abs(ub - 0.001) < 1e-6 and abs(vb - 0.001) < 1e-6, (ub, vb)

    # 2. Verify linear RGBA 8-bit and 16-bit PNG writer signatures and chunks
    if np is not None:
        sample = np.zeros((16, 16, 4), dtype=np.float32)
        sample[0, 0] = [1.0, 0.5, 0.25, 0.75]  # bottom-left (v=0)
        with tempfile.TemporaryDirectory() as td:
            for depth in (8, 16):
                p = os.path.join(td, f"test_{depth}.png")
                write_linear_rgba_png(p, sample, bit_depth=depth)
                data = open(p, "rb").read()
                assert data[:8] == b"\x89PNG\r\n\x1a\n"
                assert b"gAMA" in data and b"IHDR" in data and b"IDAT" in data and b"IEND" in data
                w, h, bd, ct = struct.unpack(">IIBB", data[16:26])
                assert (w, h, bd, ct) == (16, 16, depth, 6)
    print("[PASS] Bake-VividFurPatterns.py contract self-test passed (flipbook UV + 8/16-bit linear PNG).")


def parse_args(argv: List[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Houdini 22 hython baker for VividRP FurSlab DCC textures.")
    parser.add_argument("--out", default="OutFurTextures", help="Output directory for baked linear PNGs.")
    parser.add_argument("--save-hip", default="", help="Optional path to save the Houdini .hip/.hipnc scene.")
    parser.add_argument("--resolution", type=int, default=512, help="Resolution for FlowMap and 2D Pattern.")
    parser.add_argument("--flipbook-resolution", type=int, default=1024, help="Atlas resolution for 3D Flipbook.")
    parser.add_argument("--grid", type=int, nargs=2, default=[4, 4], metavar=("COLS", "ROWS"), help="Flipbook grid.")
    parser.add_argument("--strands", type=int, default=25, help="Number of 3D curly wool curves per tile.")
    parser.add_argument("--bit-depth", type=int, choices=[8, 16], default=8, help="PNG bit depth (8 or 16).")
    parser.add_argument("--self-test", action="store_true", help="Run offline contract self-test without Houdini.")
    return parser.parse_args(argv)


def main(argv: List[str]) -> int:
    args = parse_args(argv)
    if args.self_test:
        run_self_test()
        return 0
    run_houdini_bake(args)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

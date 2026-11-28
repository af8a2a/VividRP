#!/usr/bin/env python3
"""
Houdini 22 (hython) Strand-Based Hair Groom Exporter for VividRP (.vividhair v1).

Generates two LookDev grooms using Houdini SOPs + VEX:
  1) HairSphere_16k.vividhair  (16,384 strands x 8 segments = 131,072 segments)
  2) HairBust_48k.vividhair    (49,152 strands x 7 segments = 344,064 segments)
     + HairBust_Head.obj        (mannequin head mesh aligned to HairBust_48k)
     + HairBust_ShadowProxy.obj (inverted-face hair silhouette shadow proxy)
  + VividHairGrooms.hipnc      (inspectable Houdini 22 scene)

Binary contract matches:
  - Runtime/Core/Hair/HairGroomData.cs
  - Shaders/Core/Public/Hair/VividHairGroomTypes.hlsl

Coordinate convention:
  - Houdini is right-handed Y-up; Unity is left-handed Y-up.
  - Unity's OBJ importer negates X automatically (x_unity = -x_obj).
  - Therefore .vividhair also negates X on positions and tangents so grooms
    register identically with exported OBJ head/proxy meshes in Unity.
"""

import argparse
import math
import os
import random
import struct
import sys
import time

import hou


VDHR_MAGIC = 0x52484456
VDHR_VERSION = 1
FLAG_STRANDS_RANDOMIZED = 1 << 0
FLAG_HAS_BAKED_L1_SH = 1 << 1
FLAG_HAS_CLUMP_DATA = 1 << 2


def float_to_half_bits(val: float) -> int:
    """IEEE 754 float32 -> float16 uint16 bits (matching Unity math.f32tof16)."""
    packed = struct.pack("<f", float(val))
    bits = struct.unpack("<I", packed)[0]
    sign = (bits >> 16) & 0x8000
    exp = ((bits >> 23) & 0xFF) - 127 + 15
    mant = bits & 0x7FFFFF

    if exp <= 0:
        if exp < -10:
            return sign
        mant = (mant | 0x800000) >> (1 - exp)
        return sign | ((mant + 0x1000) >> 13)
    elif exp >= 31:
        return sign | 0x7C00
    return sign | (exp << 10) | ((mant + 0x1000) >> 13)


def pack_half2(u: float, v: float) -> int:
    return (float_to_half_bits(u) & 0xFFFF) | ((float_to_half_bits(v) & 0xFFFF) << 16)


def pack_unorm16(val: float) -> int:
    c = 0.0 if val < 0.0 else (1.0 if val > 1.0 else val)
    return int(round(c * 65535.0)) & 0xFFFF


def pack_unorm16_pair(low: float, high: float) -> int:
    return pack_unorm16(low) | (pack_unorm16(high) << 16)


def pack_clump_and_seed(clump_id: int, seed: float) -> int:
    return (clump_id & 0xFFFF) | (pack_unorm16(seed) << 16)


def pack_segment_strand_local(strand_index: int, local_seg_index: int) -> int:
    return (strand_index & 0xFFFFF) | ((local_seg_index & 0xFFF) << 20)


def build_hair_sphere_node(obj_root, strand_count: int, verts_per_strand: int):
    """Builds a 16k-strand parted, clumped lookdev hair sphere in Houdini SOPs."""
    geo = obj_root.createNode("geo", "HairSphere_16k")

    # Base emitter sphere (radius 0.16m, matches FurLookDev / HairLookDev scale)
    sphere = geo.createNode("sphere", "base_sphere")
    sphere.parm("type").set("poly")
    sphere.parm("freq").set(5)
    sphere.parmTuple("rad").set((0.16, 0.16, 0.16))

    # Scatter hair roots uniformly on the upper 82% of the sphere
    wrangle_mask = geo.createNode("attribwrangle", "scalp_density")
    wrangle_mask.setInput(0, sphere)
    wrangle_mask.parm("class").set("point")
    wrangle_mask.parm("snippet").set(
        """
        vector n = normalize(@P);
        @density = smooth(-0.62, -0.35, n.y);
        @N = n;
        @uv = set(atan2(n.z, n.x) / (2.0 * 3.14159265) + 0.5, acos(clamp(n.y, -1.0, 1.0)) / 3.14159265, 0.0);
        """
    )

    scatter = geo.createNode("scatter", "scatter_roots")
    scatter.setInput(0, wrangle_mask)
    scatter.parm("npts").set(strand_count)
    scatter.parm("usedensityattrib").set(1)
    scatter.parm("densityattrib").set("density")
    scatter.parm("relaxpoints").set(0)
    scatter.parm("seed").set(2025)

    # Scatter 64 clump centers for coherent lock formation
    clump_scatter = geo.createNode("scatter", "clump_centers")
    clump_scatter.setInput(0, wrangle_mask)
    clump_scatter.parm("npts").set(64)
    clump_scatter.parm("usedensityattrib").set(1)
    clump_scatter.parm("densityattrib").set("density")
    clump_scatter.parm("seed").set(77)

    # Generate full multi-vertex hair polylines in VEX with parting, gravity drape,
    # clump attraction, gentle wave/curl, and micro-frizz.
    groom = geo.createNode("attribwrangle", "generate_strands")
    groom.setInput(0, scatter)
    groom.setInput(1, clump_scatter)
    groom.parm("class").set("point")
    groom.parm("snippet").set(
        f"""
        int nVerts = {verts_per_strand};
        float sphereR = 0.16;
        vector rootP = @P;
        vector rootN = normalize(rootP);

        float rnd0 = rand(@ptnum * 17.13 + 3.1);
        float rnd1 = rand(@ptnum * 53.71 + 9.7);
        float rnd2 = rand(@ptnum * 91.37 + 21.4);

        // Find nearest clump center
        int clumpPt = nearpoint(1, rootP);
        vector clumpP = point(1, "P", clumpPt);
        vector clumpN = normalize(clumpP);

        // Natural side-parted meridian flow + slight spiral swirl
        float partSide = (rootP.x >= -0.025) ? 1.0 : -1.0;
        vector downDir = normalize(set(partSide * 0.28, -1.0, -0.18));
        vector flowDir = normalize(downDir - rootN * dot(downDir, rootN));
        if (length(flowDir) < 1e-4) flowDir = set(partSide, 0.0, 0.0);
        vector bitan = normalize(cross(rootN, flowDir));

        // Clump target flow direction
        vector clumpFlow = normalize(downDir - clumpN * dot(downDir, clumpN));
        if (length(clumpFlow) < 1e-4) clumpFlow = flowDir;
        float clumpNoise = rand(clumpPt * 31.7 + 5.3);

        // Strand length varies smoothly from crown to nape
        float strandLen = fit(rootN.y, -0.5, 1.0, 0.055, 0.135) * lerp(0.86, 1.14, rnd0);
        float rootRad = lerp(0.00052, 0.00068, rnd1);
        float tipRad  = rootRad * 0.32;
        float clumpPullStrength = lerp(0.32, 0.62, rnd2);
        float wavePhase = clumpNoise * 6.2831853 + rnd1 * 0.9;
        float waveAmp = 0.0045 * lerp(0.5, 1.2, clumpNoise);

        int prim = addprim(0, "polyline");
        setprimattrib(0, "clump_id", prim, clumpPt, "set");
        setprimattrib(0, "strand_seed", prim, rnd0, "set");
        setprimattrib(0, "root_u", prim, @uv.x, "set");
        setprimattrib(0, "root_v", prim, @uv.y, "set");

        vector curP = rootP;
        float stepLen = strandLen / float(nVerts - 1);
        for (int i = 0; i < nVerts; i++)
        {{
            float u = float(i) / float(nVerts - 1);
            if (i > 0)
            {{
                // Lift near root, drape smoothly with gravity toward tip
                float lift = pow(1.0 - u, 2.2) * 0.42;
                float grav = u * u * 0.85;
                vector dir = normalize(flowDir * (0.75 + 0.35 * u) + rootN * lift + set(0, -grav, 0));

                // Pull gently toward clump trajectory
                vector clumpTarget = clumpP + normalize(clumpFlow + clumpN * lift + set(0, -grav, 0)) * (u * strandLen);
                vector toClump = clumpTarget - curP;
                float clumpWeight = smooth(0.15, 0.85, u) * clumpPullStrength * 0.18;

                // Subtle lock wave + micro flyaway frizz
                float wave = sin(u * 5.2 + wavePhase) * waveAmp * u;
                vector frizz = bitan * ((rnd1 - 0.5) * 0.006 * u * u) + rootN * ((rnd2 - 0.5) * 0.004 * u);

                curP += dir * stepLen + toClump * clumpWeight + bitan * (wave * 0.25) + frizz * 0.15;

                // Keep outside the inner sphere surface (collision push-out)
                float dist = length(curP);
                float minR = sphereR + 0.0012;
                if (dist < minR)
                    curP = normalize(curP) * minR;
            }}

            int pt = (i == 0) ? @ptnum : addpoint(0, curP);
            if (i == 0) setattrib(0, "point", "P", pt, 0, curP, "set");
            float rad = lerp(rootRad, tipRad, pow(u, 1.35));
            setpointattrib(0, "radius", pt, rad, "set");
            setpointattrib(0, "strand_u", pt, u, "set");
            setpointattrib(0, "strand_rand", pt, rnd0, "set");
            addvertex(0, prim, pt);
        }}
        """
    )

    # Output null
    out_node = geo.createNode("null", "OUT_HAIR_SPHERE")
    out_node.setInput(0, groom)
    out_node.setDisplayFlag(True)
    out_node.setRenderFlag(True)
    geo.layoutChildren()
    return out_node


def build_hair_bust_nodes(obj_root, strand_count: int, verts_per_strand: int):
    """
    Builds a realistic mannequin head + sculpted inverted-face shadow proxy +
    48k-strand Indiana-Jones-inspired side-parted short-to-medium hair groom with bangs.
    """
    geo = obj_root.createNode("geo", "HairBust_48k")

    # 1. Sculpted stylized anatomical mannequin head + neck in VEX (centered around Y=0)
    head_base = geo.createNode("sphere", "head_base")
    head_base.parm("type").set("poly")
    head_base.parm("freq").set(5)
    head_base.parmTuple("rad").set((0.105, 0.128, 0.118))

    head_sculpt = geo.createNode("attribwrangle", "sculpt_head")
    head_sculpt.setInput(0, head_base)
    head_sculpt.parm("class").set("point")
    head_sculpt.parm("snippet").set(
        """
        // Right-handed Houdini space (+Z = face front, +Y = up)
        vector p = @P;
        float yn = p.y / 0.128;
        float xn = p.x / 0.105;
        float zn = p.z / 0.118;

        // Taper jaw/chin below eyes, broaden cranium above
        float jawTaper = smooth(-0.95, 0.05, yn);
        p.x *= lerp(0.68, 1.03, jawTaper);
        if (yn < -0.1 && zn > 0.0)
        {
            // Chin protrusion and jaw contour
            float chinMask = exp(-pow(xn * 2.2, 2.0)) * smooth(0.0, -0.85, yn);
            p.z += 0.014 * chinMask;
            p.y -= 0.012 * chinMask;
        }
        // Subtle brow ridge and eye sockets on +Z face
        if (zn > 0.35)
        {
            float brow = exp(-pow((yn - 0.14) * 8.0, 2.0)) * exp(-pow(xn * 1.4, 2.0));
            p.z += 0.008 * brow;
            float noseBridge = exp(-pow(xn * 7.5, 2.0)) * exp(-pow((yn + 0.08) * 3.8, 2.0));
            p.z += 0.022 * noseBridge;
            float eyeL = exp(-pow((xn - 0.32) * 5.2, 2.0) - pow((yn - 0.02) * 6.5, 2.0));
            float eyeR = exp(-pow((xn + 0.32) * 5.2, 2.0) - pow((yn - 0.02) * 6.5, 2.0));
            p.z -= 0.009 * (eyeL + eyeR);
        }
        // Occipital fullness at back of skull (-Z)
        if (zn < -0.2)
        {
            float backSkull = exp(-pow((yn - 0.08) * 2.4, 2.0));
            p.z -= 0.012 * backSkull;
        }
        @P = p;
        """
    )

    head_normals = geo.createNode("normal", "head_normals")
    head_normals.setInput(0, head_sculpt)

    # Neck cylinder merged cleanly with head for bust silhouette
    neck = geo.createNode("tube", "neck")
    neck.parm("type").set("poly")
    neck.parm("cols").set(32)
    neck.parm("rows").set(8)
    neck.parm("rad1").set(0.056)
    neck.parm("rad2").set(0.068)
    neck.parm("height").set(0.14)
    neck.parmTuple("t").set((0.0, -0.145, -0.018))

    neck_normals = geo.createNode("normal", "neck_normals")
    neck_normals.setInput(0, neck)

    head_merge = geo.createNode("merge", "merge_bust_head")
    head_merge.setInput(0, head_normals)
    head_merge.setInput(1, neck_normals)

    out_head = geo.createNode("null", "OUT_BUST_HEAD")
    out_head.setInput(0, head_merge)

    # 2. Scalp hair mask on sculpted cranium (excludes face, ears, lower jaw)
    scalp_mask = geo.createNode("attribwrangle", "scalp_mask")
    scalp_mask.setInput(0, head_normals)
    scalp_mask.parm("class").set("point")
    scalp_mask.parm("snippet").set(
        """
        vector p = @P;
        vector n = normalize(@N);
        float xn = p.x / 0.105;
        float yn = p.y / 0.128;
        float zn = p.z / 0.118;

        // Hairline boundary: higher at forehead (+Z), dips at temples, wraps down to nape (-Z)
        float hairlineY = lerp(-0.52, 0.34, smooth(-0.35, 0.65, zn));
        // Temple recession
        float templeDip = 0.14 * exp(-pow((abs(xn) - 0.72) * 3.5, 2.0)) * smooth(0.0, 0.55, zn);
        hairlineY -= templeDip;
        // Ear cutout on left/right sides
        float earCut = exp(-pow((abs(xn) - 0.92) * 4.2, 2.0) - pow((yn + 0.08) * 4.5, 2.0) - pow((zn + 0.05) * 4.0, 2.0));

        float mask = smooth(hairlineY - 0.06, hairlineY + 0.06, yn) * (1.0 - smooth(0.25, 0.65, earCut));
        @density = clamp(mask, 0.0, 1.0);
        @uv = set(atan2(n.z, n.x) / (2.0 * 3.14159265) + 0.5, clamp(yn * 0.5 + 0.5, 0.0, 1.0), 0.0);
        """
    )

    # 3. Inverted-face shadow proxy mesh (IJGC Slide 68: prevents double-shadowing on forehead skin)
    proxy_blast = geo.createNode("blast", "proxy_scalp_only")
    proxy_blast.setInput(0, scalp_mask)
    proxy_blast.parm("grouptype").set("points")
    proxy_blast.parm("group").set("@density<0.2")

    proxy_inflate = geo.createNode("attribwrangle", "inflate_shadow_proxy")
    proxy_inflate.setInput(0, proxy_blast)
    proxy_inflate.parm("class").set("point")
    proxy_inflate.parm("snippet").set(
        """
        float vol = lerp(0.010, 0.028, smooth(-0.2, 0.85, @P.y / 0.128));
        @P += normalize(@N) * vol;
        """
    )

    proxy_reverse = geo.createNode("reverse", "invert_proxy_faces")
    proxy_reverse.setInput(0, proxy_inflate)

    out_proxy = geo.createNode("null", "OUT_SHADOW_PROXY")
    out_proxy.setInput(0, proxy_reverse)

    # 4. Scatter 48k roots + 96 clump centers across the scalp
    scatter_roots = geo.createNode("scatter", "scatter_bust_roots")
    scatter_roots.setInput(0, scalp_mask)
    scatter_roots.parm("npts").set(strand_count)
    scatter_roots.parm("usedensityattrib").set(1)
    scatter_roots.parm("densityattrib").set("density")
    scatter_roots.parm("relaxpoints").set(0)
    scatter_roots.parm("seed").set(1937)

    clump_centers = geo.createNode("scatter", "bust_clump_centers")
    clump_centers.setInput(0, scalp_mask)
    clump_centers.parm("npts").set(96)
    clump_centers.parm("usedensityattrib").set(1)
    clump_centers.parm("densityattrib").set("density")
    clump_centers.parm("seed").set(42)

    # 5. Groom side-parted hero hairstyle with swept bangs, layered sides, and tapered nape
    groom_bust = geo.createNode("attribwrangle", "generate_bust_strands")
    groom_bust.setInput(0, scatter_roots)
    groom_bust.setInput(1, clump_centers)
    groom_bust.setInput(2, head_normals)
    groom_bust.parm("class").set("point")
    groom_bust.parm("snippet").set(
        f"""
        int nVerts = {verts_per_strand};
        vector rootP = @P;
        vector rootN = normalize(@N);

        float rnd0 = rand(@ptnum * 19.31 + 1.7);
        float rnd1 = rand(@ptnum * 47.89 + 11.3);
        float rnd2 = rand(@ptnum * 83.41 + 29.1);

        int clumpPt = nearpoint(1, rootP);
        vector clumpP = point(1, "P", clumpPt);
        vector clumpN = normalize(point(1, "N", clumpPt));
        float clumpRnd = rand(clumpPt * 23.17 + 7.9);

        // Classic left-side part (x = 0.032 in Houdini -> right side in Unity after -X)
        float partX = 0.032;
        float sideSign = (rootP.x >= partX) ? 1.0 : -1.0;

        // Top crown/bangs sweep diagonally across forehead; sides/back comb smoothly down & back
        float isFrontTop = smooth(0.02, 0.095, rootP.y) * smooth(-0.02, 0.085, rootP.z);
        float isBack     = smooth(0.01, -0.06, rootP.z);

        vector sweepTop  = normalize(set(sideSign * 0.82, -0.25, -0.35 + 0.55 * isFrontTop));
        vector combSide  = normalize(set(sideSign * 0.22, -0.88, -0.42));
        vector combBack  = normalize(set(rootP.x * 1.4, -0.95, -0.18));

        vector rawFlow = lerp(combSide, sweepTop, isFrontTop);
        rawFlow = lerp(rawFlow, combBack, isBack * (1.0 - isFrontTop));
        vector flowDir = normalize(rawFlow - rootN * dot(rawFlow, rootN));
        if (length(flowDir) < 1e-4) flowDir = set(0.0, -1.0, 0.0);
        vector bitan = normalize(cross(rootN, flowDir));

        // Realistic length grading: longer on crown/bangs (8-12 cm), shorter at sides/nape (2.5-5 cm)
        float crownFactor = smooth(-0.05, 0.10, rootP.y);
        float strandLen = lerp(0.028, 0.115, crownFactor) * lerp(0.82, 1.18, rnd0);
        float rootRad = lerp(0.00042, 0.00056, rnd1);
        float tipRad  = rootRad * 0.28;
        float clumpStrength = lerp(0.35, 0.68, rnd2);
        float wavePhase = clumpRnd * 6.2831853 + rnd1 * 1.1;

        int prim = addprim(0, "polyline");
        setprimattrib(0, "clump_id", prim, clumpPt, "set");
        setprimattrib(0, "strand_seed", prim, rnd0, "set");
        setprimattrib(0, "root_u", prim, @uv.x, "set");
        setprimattrib(0, "root_v", prim, @uv.y, "set");

        vector curP = rootP;
        float stepLen = strandLen / float(nVerts - 1);
        for (int i = 0; i < nVerts; i++)
        {{
            float u = float(i) / float(nVerts - 1);
            if (i > 0)
            {{
                // Root lift for natural hair volume + gravity bend along tip
                float lift = pow(1.0 - u, 1.9) * lerp(0.28, 0.65, crownFactor);
                float grav = u * u * lerp(0.55, 0.95, crownFactor);
                vector dir = normalize(flowDir * (0.85 + 0.25 * u) + rootN * lift + set(0, -grav, 0));

                vector clumpTarget = clumpP + normalize(rawFlow + clumpN * lift + set(0, -grav, 0)) * (u * strandLen);
                vector toClump = clumpTarget - curP;
                float cWeight = smooth(0.12, 0.88, u) * clumpStrength * 0.16;

                float wave = sin(u * 4.8 + wavePhase) * 0.0038 * u;
                vector flyaway = bitan * ((rnd1 - 0.5) * 0.005 * u * u) + rootN * ((rnd2 - 0.5) * 0.0035 * u);

                curP += dir * stepLen + toClump * cWeight + bitan * (wave * 0.3) + flyaway * 0.2;

                // Push outside sculpted head mesh so strands never clip inside skull/forehead
                int hitPrim;
                vector hitUV;
                xyzdist(2, curP, hitPrim, hitUV);
                vector surfP = primuv(2, "P", hitPrim, hitUV);
                vector surfN = normalize(primuv(2, "N", hitPrim, hitUV));
                float signedD = dot(curP - surfP, surfN);
                float minClearance = 0.0018;
                if (signedD < minClearance)
                    curP += surfN * (minClearance - signedD);
            }}

            int pt = (i == 0) ? @ptnum : addpoint(0, curP);
            if (i == 0) setattrib(0, "point", "P", pt, 0, curP, "set");
            float rad = lerp(rootRad, tipRad, pow(u, 1.3));
            setpointattrib(0, "radius", pt, rad, "set");
            setpointattrib(0, "strand_u", pt, u, "set");
            setpointattrib(0, "strand_rand", pt, rnd0, "set");
            addvertex(0, prim, pt);
        }}
        """
    )

    out_hair = geo.createNode("null", "OUT_HAIR_BUST")
    out_hair.setInput(0, groom_bust)
    out_hair.setDisplayFlag(True)
    out_hair.setRenderFlag(True)
    geo.layoutChildren()
    return out_hair, out_head, out_proxy


def export_geometry_to_vividhair(sop_node, output_path: str, random_seed: int):
    """Exports a Houdini polyline SOP to .vividhair v1 with randomized strand order."""
    t0 = time.time()
    geometry = sop_node.geometry()
    prims = list(geometry.prims())
    if not prims:
        raise RuntimeError(f"SOP {sop_node.path()} produced 0 primitives.")

    # Pre-randomize strand order (IJGC / Taillandier20 continuous LOD contract)
    rng = random.Random(random_seed)
    rng.shuffle(prims)

    # Read attributes in bulk using Python tuples
    strand_records = bytearray()
    vertex_records = bytearray()
    segment_records = bytearray()

    bmin = [1e30, 1e30, 1e30]
    bmax = [-1e30, -1e30, -1e30]
    radius_sum = 0.0
    total_verts = 0
    total_segs = 0
    valid_strands = 0

    for prim in prims:
        pts = [v.point() for v in prim.vertices()]
        n = len(pts)
        if n < 2:
            continue

        # Convert Houdini right-handed (x, y, z) -> Unity left-handed (-x, y, z)
        positions = []
        radii = []
        strand_us = []
        strand_rands = []
        for pt in pts:
            px, py, pz = pt.position()
            ux, uy, uz = -px, py, pz
            positions.append((ux, uy, uz))
            r = float(pt.attribValue("radius"))
            r = max(r, 1e-5)
            radii.append(r)
            strand_us.append(float(pt.attribValue("strand_u")))
            strand_rands.append(float(pt.attribValue("strand_rand")))

            if ux < bmin[0]: bmin[0] = ux
            if uy < bmin[1]: bmin[1] = uy
            if uz < bmin[2]: bmin[2] = uz
            if ux > bmax[0]: bmax[0] = ux
            if uy > bmax[1]: bmax[1] = uy
            if uz > bmax[2]: bmax[2] = uz
            radius_sum += r

        clump_id = int(prim.attribValue("clump_id")) & 0xFFFF
        strand_seed = float(prim.attribValue("strand_seed"))
        root_u = float(prim.attribValue("root_u"))
        root_v = float(prim.attribValue("root_v"))

        first_vertex = total_verts
        strand_idx = valid_strands
        strand_records += struct.pack(
            "<IIII",
            first_vertex,
            n,
            pack_half2(root_u, root_v),
            pack_clump_and_seed(clump_id, strand_seed),
        )

        # Compute smooth finite-differenced unit tangents
        for i in range(n):
            p_prev = positions[i - 1] if i > 0 else positions[0]
            p_next = positions[i + 1] if i + 1 < n else positions[n - 1]
            tx = p_next[0] - p_prev[0]
            ty = p_next[1] - p_prev[1]
            tz = p_next[2] - p_prev[2]
            tlen = math.sqrt(tx * tx + ty * ty + tz * tz)
            if tlen > 1e-8:
                inv = 1.0 / tlen
                tx, ty, tz = tx * inv, ty * inv, tz * inv
            else:
                tx, ty, tz = 0.0, -1.0, 0.0

            ux, uy, uz = positions[i]
            packed_attrs = pack_unorm16_pair(strand_us[i], strand_rands[i])
            vertex_records += struct.pack(
                "<fffffffI",
                ux, uy, uz,
                radii[i],
                tx, ty, tz,
                packed_attrs,
            )

        for k in range(n - 1):
            segment_records += struct.pack(
                "<II",
                first_vertex + k,
                pack_segment_strand_local(strand_idx, k),
            )

        total_verts += n
        total_segs += (n - 1)
        valid_strands += 1

    default_radius = (radius_sum / max(total_verts, 1)) if total_verts > 0 else 0.0005
    flags = FLAG_STRANDS_RANDOMIZED | FLAG_HAS_CLUMP_DATA

    header = struct.pack(
        "<IIIIIIfffffffIII",
        VDHR_MAGIC,
        VDHR_VERSION,
        flags,
        valid_strands,
        total_verts,
        total_segs,
        bmin[0], bmin[1], bmin[2],
        bmax[0], bmax[1], bmax[2],
        default_radius,
        0, 0, 0,
    )
    assert len(header) == 64

    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    with open(output_path, "wb") as f:
        f.write(header)
        f.write(strand_records)
        f.write(vertex_records)
        f.write(segment_records)

    elapsed = time.time() - t0
    size_mb = os.path.getsize(output_path) / (1024.0 * 1024.0)
    print(
        f"[Export-VividHairGroom] Wrote {output_path}: "
        f"{valid_strands} strands, {total_verts} verts, {total_segs} segs, "
        f"{size_mb:.2f} MB in {elapsed:.2f}s"
    )


def export_sop_to_obj(sop_node, output_path: str):
    """Exports a Houdini polygon SOP to standard OBJ (Unity auto-flips X on import)."""
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    sop_node.geometry().saveToFile(output_path)
    size_kb = os.path.getsize(output_path) / 1024.0
    print(f"[Export-VividHairGroom] Wrote OBJ {output_path} ({size_kb:.1f} KB)")


def main():
    parser = argparse.ArgumentParser(description="Generate Houdini 22 VividRP strand hair grooms.")
    parser.add_argument(
        "--out-dir",
        default=os.path.abspath(
            os.path.join(os.path.dirname(__file__), "..", "..", "Samples", "HairLookDev", "Grooms")
        ),
        help="Target output directory for .vividhair and .obj assets.",
    )
    parser.add_argument("--sphere-strands", type=int, default=16384)
    parser.add_argument("--sphere-verts", type=int, default=9)
    parser.add_argument("--bust-strands", type=int, default=49152)
    parser.add_argument("--bust-verts", type=int, default=8)
    args = parser.parse_args()

    hou.hipFile.clear(suppress_save_prompt=True)
    obj_root = hou.node("/obj")

    print("[Export-VividHairGroom] Building HairSphere_16k in Houdini 22...")
    sphere_hair = build_hair_sphere_node(obj_root, args.sphere_strands, args.sphere_verts)
    export_geometry_to_vividhair(
        sphere_hair,
        os.path.join(args.out_dir, "HairSphere_16k.vividhair"),
        random_seed=202501,
    )

    print("[Export-VividHairGroom] Building HairBust_48k + Head + ShadowProxy in Houdini 22...")
    bust_hair, bust_head, bust_proxy = build_hair_bust_nodes(
        obj_root, args.bust_strands, args.bust_verts
    )
    export_geometry_to_vividhair(
        bust_hair,
        os.path.join(args.out_dir, "HairBust_48k.vividhair"),
        random_seed=202502,
    )
    export_sop_to_obj(bust_head, os.path.join(args.out_dir, "HairBust_Head.obj"))
    export_sop_to_obj(bust_proxy, os.path.join(args.out_dir, "HairBust_ShadowProxy.obj"))

    hip_path = os.path.join(args.out_dir, "VividHairGrooms.hipnc")
    hou.hipFile.save(hip_path)
    print(f"[Export-VividHairGroom] Saved Houdini project {hip_path}")


if __name__ == "__main__":
    main()

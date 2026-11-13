"""Normalize actual Unity/Lumen GPU outputs; never derive GI from an LDR image.

Dependencies: numpy, scipy, OpenEXR. D3D12 Cornell fixture, fixed monocular POV.
The common field is consumed E/pi before the receiving material, not outgoing Lo.
"""
import argparse
import csv
import hashlib
import json
from pathlib import Path

import numpy as np
import OpenEXR
from scipy.ndimage import minimum_filter, maximum_filter

FIXTURE = Path(__file__).parents[1] / "UnrealCornellReference/unity_fixture.json"
SCHEMA = "vivid.linear_gi.v1"


def json_sequence(path):
    text = path.read_text(encoding="utf-8-sig")
    decoder, offset = json.JSONDecoder(), 0
    while offset < len(text):
        while offset < len(text) and text[offset].isspace():
            offset += 1
        if offset == len(text):
            return
        value, offset = decoder.raw_decode(text, offset)
        yield value


def exr_write(path, array):
    array = np.ascontiguousarray(array, dtype=np.float32)
    if not np.isfinite(array).all():
        raise ValueError("Non-finite output: " + str(path))
    key = "Y" if array.ndim == 2 else "RGB" if array.shape[-1] == 3 else "RGBA"
    OpenEXR.File({"compression": OpenEXR.ZIP_COMPRESSION}, {key: array}).write(str(path))
    recovered = OpenEXR.File(str(path)).channels()[key].pixels
    if not np.array_equal(array, recovered):
        raise ValueError("Float EXR round-trip changed values: " + str(path))


def exr_read(path):
    channels = OpenEXR.File(str(path)).channels()
    return next(iter(channels.values())).pixels


def expected_geometry(fixture, width, height):
    """Analytic triangle-equivalent boxes/one-sided quad, used only for masks/QA."""
    camera = fixture["camera"]
    if max(abs(v) for v in camera["euler"]) > 1e-4:
        raise ValueError("This controlled fixture requires the exported identity camera rotation.")
    tangent = np.tan(np.deg2rad(camera["verticalFov"]) * 0.5)
    x = ((np.arange(width, dtype=np.float32) + 0.5) * 2 / width - 1) * tangent * camera["aspect"]
    y = (1 - (np.arange(height, dtype=np.float32) + 0.5) * 2 / height) * tangent
    rays = np.stack(np.broadcast_arrays(x[None, :], y[:, None], np.ones((height, width), np.float32)), -1)
    nearest = np.full((height, width), np.inf, np.float32)
    labels = np.zeros((height, width), np.uint8)
    normal = np.zeros((height, width, 3), np.float32)
    albedo = np.zeros_like(normal)
    origin = np.asarray(camera["position"], dtype=np.float32)
    emitter_id = None
    for index, item in enumerate(fixture["geometry"], 1):
        if abs(item["rotation"][0]) + abs(item["rotation"][2]) > 1e-4:
            raise ValueError("Unsupported reference object rotation.")
        angle = np.deg2rad(item["rotation"][1])
        c, s = np.cos(angle), np.sin(angle)
        rotation = np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]], dtype=np.float32)
        o = (origin - np.asarray(item["position"], np.float32)) @ rotation
        d = rays @ rotation
        half = np.asarray(item["meshBounds"], np.float32) * np.asarray(item["scale"], np.float32) * 0.5
        if half[1] == 0:
            emitter_id = index
            t = np.divide(-o[1], d[..., 1], out=np.full_like(nearest, np.inf), where=d[..., 1] > 1e-8)
            hit = o + d * t[..., None]
            valid = (t > 0) & (np.abs(hit[..., 0]) <= half[0]) & (np.abs(hit[..., 2]) <= half[2])
            n = np.broadcast_to(np.array([0, -1, 0], np.float32), normal.shape)
        else:
            parallel = np.abs(d) < 1e-8
            with np.errstate(divide="ignore", invalid="ignore"):
                a, b = (-half - o) / d, (half - o) / d
            lo, hi = np.minimum(a, b), np.maximum(a, b)
            lo = np.where(parallel, np.where(np.abs(o) <= half, -np.inf, np.inf), lo)
            hi = np.where(parallel, np.where(np.abs(o) <= half, np.inf, -np.inf), hi)
            t = lo.max(-1)
            valid = (t > 0) & (t <= hi.min(-1))
            axis = lo.argmax(-1)
            local_normal = np.zeros_like(normal)
            sign = -np.sign(np.take_along_axis(d, axis[..., None], -1)[..., 0])
            np.put_along_axis(local_normal, axis[..., None], sign[..., None], -1)
            n = local_normal @ rotation.T
        update = valid & (t < nearest)
        nearest[update], labels[update] = t[update], index
        normal[update] = n[update]
        albedo[update] = item["baseColorLinear"]
    stable = (labels > 0) & (minimum_filter(labels, 5) == maximum_filter(labels, 5))
    receiver = (labels > 0) & (labels != emitter_id)
    nearest[labels == 0] = 0
    return labels, normal, nearest, albedo, stable, receiver, emitter_id


def srgb_to_linear(value):
    return np.where(value <= 0.04045, value / 12.92, ((value + 0.055) / 1.055) ** 2.4)


def unpack_unsigned_float(bits, mantissa):
    exponent = (bits >> mantissa).astype(np.int32)
    fraction = (bits & ((1 << mantissa) - 1)).astype(np.float32)
    with np.errstate(over="ignore", invalid="ignore"):
        value = np.where(exponent == 0, np.ldexp(fraction, 1 - 15 - mantissa),
                         np.ldexp(1 + fraction / (1 << mantissa), exponent - 15))
    return np.where(exponent == 31, np.where(fraction == 0, np.inf, np.nan), value).astype(np.float32)


def unreal_resource(dump, descriptor, resource_id):
    # Resource ids contain literal '[0]'; glob would treat this as a character class.
    prefix = resource_id + ".v00000000000000000000.bin"
    path = next(p for p in (dump / "Resources").iterdir() if p.name == prefix)
    h, w = descriptor["ExtentY"], descriptor["ExtentX"]
    fmt = descriptor["Format"]
    if fmt == "PF_FloatRGBA":
        return np.fromfile(path, "<f2").reshape(h, w, 4).astype(np.float32)
    if fmt in ("PF_DepthStencil", "PF_R32_FLOAT"):
        return np.fromfile(path, "<f4").reshape(h, w)
    if fmt == "PF_B8G8R8A8":
        value = np.fromfile(path, np.uint8).reshape(h, w, 4)[..., [2, 1, 0]].astype(np.float32) / 255
        return srgb_to_linear(value) if "SRGB" in descriptor["Flags"] else value
    raw = np.fromfile(path, "<u4").reshape(h, w)
    if fmt == "PF_A2B10G10R10":
        return np.stack([raw & 1023, (raw >> 10) & 1023, (raw >> 20) & 1023], -1).astype(np.float32) / 1023
    if fmt == "PF_FloatR11G11B10":
        return np.stack([unpack_unsigned_float(raw & 2047, 6),
                         unpack_unsigned_float((raw >> 11) & 2047, 6), unpack_unsigned_float(raw >> 22, 5)], -1)
    raise ValueError("Unsupported actual GPU format: " + fmt)


def read_unreal(root):
    request = json.loads((root / "capture-request.json").read_text(encoding="utf-8-sig"))
    dump = next((root / "dump").iterdir())
    if (dump / "Base/Status.txt").read_text().strip() != "ok":
        raise ValueError("Incomplete GPU dump.")
    passes = list(json_sequence(dump / "Base/Passes.json"))
    # Read only the rendering controls needed by this export contract.
    console_text = (dump / "Base/ConsoleVariables.csv").read_text(encoding="utf-8-sig")
    rendering_controls = {}
    diagnostic_controls = dict(command.split(maxsplit=1) for command in request.get("diagnostic_commands", []))
    for row in csv.reader(console_text.splitlines()):
        if row and (row[0] in diagnostic_controls or row[0] in ("r.EyeAdaptation.PreExposureOverride", "r.ScreenPercentage",
                              "r.Lumen.ScreenProbeGather.ShortRangeAO", "r.Lumen.HardwareRayTracing",
                              "r.Lumen.ScreenProbeGather.MaxRayIntensity", "r.LumenScene.Radiosity.MaxRayIntensity",
                              "r.Lumen.Reflections.MaxRayIntensity")):
            rendering_controls[row[0]] = {"type": row[1], "set_by": row[2], "value": row[3]}
    for key, expected in diagnostic_controls.items():
        if key not in rendering_controls or float(rendering_controls[key]["value"]) != float(expected):
            raise ValueError("Actual diagnostic control mismatch: " + key)
    for key, expected in {"r.EyeAdaptation.PreExposureOverride": request["pre_exposure"],
                          "r.ScreenPercentage": 100, "r.Lumen.ScreenProbeGather.ShortRangeAO": 0,
                          "r.Lumen.HardwareRayTracing": 0}.items():
        if key not in rendering_controls or float(rendering_controls[key]["value"]) != expected:
            raise ValueError("Actual GPU dump rendering control mismatch: " + key)
    for key, field in [("r.Lumen.ScreenProbeGather.MaxRayIntensity", "probe_max_ray_intensity"),
                       ("r.LumenScene.Radiosity.MaxRayIntensity", "radiosity_max_ray_intensity"),
                       ("r.Lumen.Reflections.MaxRayIntensity", "reflection_max_ray_intensity")]:
        if field not in request or float(rendering_controls[key]["value"]) != request[field]:
            raise ValueError("Missing/mismatched exposure-dependent clamp control: " + key)
    selected = [p for p in passes if p["EventName"].startswith("DiffuseIndirectComposite")]
    if len(selected) != 1:
        raise ValueError("Ambiguous composite pass/view.")
    descriptors = {d["UniqueResourceName"]: d for d in json_sequence(dump / "Base/ResourceDescs.json")}

    def get(name):
        for resource_id in selected[0]["InputResources"]:
            desc = descriptors[resource_id.split(".")[0]]
            if desc["Name"] == name:
                return unreal_resource(dump, desc, resource_id)
        raise ValueError("Missing native input " + name)

    native_gi = get("Lumen.ScreenProbeGather.DiffuseIndirect")
    albedo = get("GBufferC")
    normal = (get("GBufferA") * 2 - 1)[..., [1, 2, 0]]  # UE -> Unity world axes
    normal /= np.maximum(np.linalg.norm(normal, axis=-1, keepdims=True), 1e-6)
    device_z = get("SceneDepthZ")
    valid = device_z > 0
    depth = np.divide(request["near_clip_cm"] / 100, device_z, out=np.zeros_like(device_z), where=valid)
    info = json.loads((dump / "Base/Infos.json").read_text())
    version = ".".join(str(info[n]) for n in ["EngineMajorVersion", "EngineMinorVersion", "EnginePatchVersion"])
    return {"engine": "Unreal Lumen " + version, "gi": native_gi / request["pre_exposure"],
            "conditional": None, "coverage": valid.astype(np.float32), "valid": valid,
            "albedo": albedo, "normal": normal, "depth": depth,
            "raw": request, "pre_exposure": request["pre_exposure"], "row_origin": "top_left",
            "coverage_source": "geometry validity; native fractional trace coverage is not exposed",
            "native_pass": selected[0]["EventName"], "dump": str(dump), "rendering_controls": rendering_controls}


def read_unity(root):
    raw = json.loads((root / "raw-metadata.json").read_text())
    w, h = raw["width"], raw["height"]
    values = {n: np.fromfile(root / (n + ".f32"), "<f4").reshape(h, w, 4)[::-1].copy()
              for n in ["transport", "conditional", "albedo", "geometry"]}
    return {"engine": "VividRP", "gi": values["transport"][..., :3],
            "conditional": values["conditional"][..., :3], "coverage": values["transport"][..., 3],
            "valid": values["albedo"][..., 3] > 0.5, "albedo": values["albedo"][..., :3],
            "normal": values["geometry"][..., :3], "depth": values["geometry"][..., 3],
            "raw": raw, "pre_exposure": 1.0, "row_origin": "bottom_left -> top_left",
            "coverage_source": "measured GI alpha, already multiplied into consumed E/pi"}


def export(root, destination):
    if destination.exists():
        raise ValueError("Refusing to overwrite " + str(destination))
    data = read_unity(root) if (root / "raw-metadata.json").exists() else read_unreal(root)
    h, w = data["gi"].shape[:2]
    if (w, h) != (1920, 1080):
        raise ValueError(f"Unexpected actual GI size {w}x{h}; no resizing is allowed.")
    fixture = json.loads(FIXTURE.read_text(encoding="utf-8-sig"))
    labels, expected_normal, expected_depth, expected_albedo, stable, receiver, emitter = expected_geometry(fixture, w, h)
    stable &= data["valid"]
    receiver &= data["valid"]
    alignment = {
        "geometry_validity_disagreement_fraction": float(np.mean((labels > 0) != data["valid"])),
        "depth_relative_error_p99": float(np.quantile(np.abs(data["depth"][stable] / expected_depth[stable] - 1), 0.99)),
        "normal_dot_mean": float(np.einsum("ij,ij->i", data["normal"][stable], expected_normal[stable]).mean()),
        "albedo_abs_error_p99": float(np.quantile(np.abs(data["albedo"][stable] - expected_albedo[stable]), 0.99)),
    }
    if alignment["depth_relative_error_p99"] > 0.01 or alignment["normal_dot_mean"] < 0.99 or alignment["albedo_abs_error_p99"] > 0.01:
        raise ValueError("Camera/orientation/GBuffer contract failed: " + repr(alignment))
    # Do not infer PreExposure from SceneColor / emissive color: the emitter also
    # receives reflected lighting. Use the recorded renderer override and the
    # independent paired-normalization check with a fixed physical clamp instead.
    gi = np.where(receiver[..., None], data["gi"], 0)
    albedo = np.where(receiver[..., None], data["albedo"], 0)
    outputs = {"e_over_pi": gi, "irradiance": gi * np.float32(np.pi),
               "lambert_radiance": gi * albedo, "albedo": albedo,
               "coverage": np.where(receiver, data["coverage"], 0), "receiver_mask": receiver.astype(np.float32),
               "normal": np.where(receiver[..., None], data["normal"], 0),
               "depth_m": np.where(receiver, data["depth"], 0), "object_id": labels.astype(np.float32)}
    if data["conditional"] is not None:
        outputs["conditional_e_over_pi"] = np.where(receiver[..., None], data["conditional"], 0)
    destination.mkdir(parents=True)
    for name, value in outputs.items():
        exr_write(destination / (name + ".exr"), value)
    metadata = {"schema": SCHEMA, "engine": data["engine"], "width": w, "height": h,
                "quantity": "irradiance_over_pi", "rgb_space": "linear_sRGB_D65", "row_origin": "top_left",
                "pre_exposure_removed": True, "native_pre_exposure": data["pre_exposure"],
                "receiving_albedo_in_e_over_pi": False, "receiving_brdf_in_e_over_pi": False,
                "display_transform_applied": False, "coverage_applied_to_e_over_pi": True,
                "coverage_source": data["coverage_source"], "emissive_surface_excluded": True,
                "lambert_radiance_definition": "derived albedo * consumed E/pi; not the native OpenPBR/DefaultLit material response",
                "irradiance_definition": "pi * consumed E/pi", "normal_axes": "Unity world xyz",
                "fixture_sha256": hashlib.sha256(FIXTURE.read_bytes()).hexdigest(),
                "camera": fixture["camera"], "objects": {i: item["name"] for i, item in enumerate(fixture["geometry"], 1)},
                "source": str(root), "raw_row_conversion": data["row_origin"], "alignment_validation": alignment,
                "raw_metadata": data["raw"], "files": {n: n + ".exr" for n in outputs}}
    if data["engine"].startswith("Unreal"):
        metadata["native_pass"] = data["native_pass"]
        metadata["rendering_controls"] = data["rendering_controls"]
        metadata["native_storage"] = "PF_FloatR11G11B10, unpacked without display conversion"
    (destination / "metadata.json").write_text(json.dumps(metadata, indent=2))
    print(json.dumps({"export": str(destination), "validation": alignment}, indent=2))


def compare(a_root, b_root, report):
    ma, mb = [json.loads((p / "metadata.json").read_text()) for p in [a_root, b_root]]
    for field in ["schema", "quantity", "rgb_space", "row_origin", "pre_exposure_removed", "fixture_sha256"]:
        if ma[field] != mb[field]:
            raise ValueError("Incompatible datasets: " + field)
    for metadata in (ma, mb):
        if metadata["schema"] != SCHEMA or metadata["quantity"] != "irradiance_over_pi" or not metadata["pre_exposure_removed"]:
            raise ValueError("Input is not normalized linear E/pi.")
        if metadata["display_transform_applied"] or metadata["receiving_albedo_in_e_over_pi"] or metadata["receiving_brdf_in_e_over_pi"]:
            raise ValueError("Input already contains display/material response.")
        if not metadata["coverage_applied_to_e_over_pi"]:
            raise ValueError("Conditional GI must not be compared to consumed GI.")
    if ma["camera"] != mb["camera"]:
        raise ValueError("Camera mismatch.")
    a, b = [exr_read(p / "e_over_pi.exr") for p in [a_root, b_root]]
    mask = (exr_read(a_root / "receiver_mask.exr") > 0.5) & (exr_read(b_root / "receiver_mask.exr") > 0.5)
    ids = exr_read(a_root / "object_id.exr")
    mask &= minimum_filter(ids, 5) == maximum_filter(ids, 5)
    y_a, y_b = [x @ np.array([0.2126, 0.7152, 0.0722], np.float32) for x in [a, b]]
    result = {"a": str(a_root), "b": str(b_root), "quantity": "consumed E/pi, scene linear; no gain fitting",
              "sample_scope": "one exported frame per input, not a temporal/converged-error benchmark", "regions": {}}
    for label, selection in [("all", mask)] + [(name, mask & (ids == int(i))) for i, name in ma["objects"].items()]:
        if not selection.any():
            continue
        aa, bb = y_a[selection], y_b[selection]
        result["regions"][label] = {"pixels": int(selection.sum()), "mean_a": float(aa.mean()), "mean_b": float(bb.mean()),
                                     "mean_change_percent": float(100 * (bb.mean() / max(aa.mean(), 1e-8) - 1)),
                                     "rmse": float(np.sqrt(np.mean((aa - bb) ** 2)))}
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(json.dumps(result, indent=2))
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("export"); p.add_argument("source", type=Path); p.add_argument("destination", type=Path)
    p = sub.add_parser("compare"); p.add_argument("a", type=Path); p.add_argument("b", type=Path); p.add_argument("report", type=Path)
    args = parser.parse_args()
    if args.command == "export":
        export(args.source, args.destination)
    else:
        compare(args.a, args.b, args.report)

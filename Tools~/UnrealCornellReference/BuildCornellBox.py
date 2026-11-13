"""Execute with full UE 5.7 Editor -ExecutePythonScript in the isolated project.

Geometry/material inputs are exported from the live Unity fixture, not estimated
from a screenshot. Existing completed maps are never overwritten.
"""
import json
import math
from pathlib import Path

import unreal

PROJECT = Path(unreal.Paths.project_dir())
SOURCE = PROJECT / "Scripts/unity_fixture.json"
ROOT = "/Game/VividReference"
MAP = ROOT + "/Maps/CornellBoxEmissiveCeiling"
STATE = PROJECT / "Saved/Diagnostics/build-state.json"
TAG = "VividEmissiveCornellGenerated"


def xyz_cm(v):
    return [100.0 * v[2], 100.0 * v[0], 100.0 * v[1]]


def safe_name(name):
    return "".join(c if c.isalnum() else "_" for c in name).strip("_")


def create_material(item, tools, assets):
    path = ROOT + "/Materials/M_" + safe_name(item["material"])
    if assets.does_asset_exist(path):
        return assets.load_asset(path)
    material = tools.create_asset(path.rsplit("/", 1)[1], path.rsplit("/", 1)[0],
                                  unreal.Material, unreal.MaterialFactoryNew())
    material.set_editor_property("two_sided", False)
    material.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_DEFAULT_LIT)
    library = unreal.MaterialEditingLibrary

    def vector(values, prop, y):
        node = library.create_material_expression(material, unreal.MaterialExpressionConstant3Vector, -320, y)
        node.set_editor_property("constant", unreal.LinearColor(*values, 1.0))
        assert library.connect_material_property(node, "", prop)

    def scalar(value, prop, y):
        node = library.create_material_expression(material, unreal.MaterialExpressionConstant, -320, y)
        node.set_editor_property("r", value)
        assert library.connect_material_property(node, "", prop)

    vector(item["baseColorLinear"], unreal.MaterialProperty.MP_BASE_COLOR, 0)
    if max(item["emissionLinear"]) > 0:
        radiance = library.create_material_expression(material, unreal.MaterialExpressionConstant3Vector, -560, 300)
        radiance.set_editor_property("constant", unreal.LinearColor(*item["emissionLinear"], 1))
        scale = library.create_material_expression(material, unreal.MaterialExpressionScalarParameter, -560, 440)
        scale.set_editor_property("parameter_name", "EmissionScale")
        scale.set_editor_property("default_value", 1.0)
        multiply = library.create_material_expression(material, unreal.MaterialExpressionMultiply, -320, 300)
        assert library.connect_material_expressions(radiance, "", multiply, "A")
        assert library.connect_material_expressions(scale, "", multiply, "B")
        assert library.connect_material_property(multiply, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR)
    else:
        vector(item["emissionLinear"], unreal.MaterialProperty.MP_EMISSIVE_COLOR, 260)
    scalar(item["roughness"], unreal.MaterialProperty.MP_ROUGHNESS, 100)
    scalar(0.0, unreal.MaterialProperty.MP_METALLIC, 160)
    scalar(0.5, unreal.MaterialProperty.MP_SPECULAR, 210)
    library.recompile_material(material)
    assert assets.save_loaded_asset(material, False)
    return material


def create_mesh(item, assets, mesh_editor):
    path = ROOT + "/Meshes/SM_" + safe_name(item["name"])
    if assets.does_asset_exist(path):
        mesh = assets.load_asset(path)
    else:
        mesh = None
    emitter = max(item["emissionLinear"]) > 0
    if mesh is None:
        dims = xyz_cm([a * b for a, b in zip(item["meshBounds"], item["scale"])])
        dynamic = unreal.DynamicMesh()
        options = unreal.GeometryScriptPrimitiveOptions()
        if emitter:
            options.set_editor_property("flip_orientation", True)
            unreal.GeometryScript_Primitives.append_rectangle_xy(dynamic, options, unreal.Transform(), dims[0], dims[1])
        else:
            unreal.GeometryScript_Primitives.append_box(dynamic, options, unreal.Transform(), *dims,
                                                        origin=unreal.GeometryScriptPrimitiveOriginMode.CENTER)
        creation = unreal.GeometryScriptCreateNewStaticMeshAssetOptions()
        creation.set_editor_property("enable_nanite", True)
        creation.set_editor_property("enable_collision", False)
        result = unreal.GeometryScript_NewAssetUtils.create_new_static_mesh_asset_from_mesh(dynamic, path, creation)
        mesh = result[0] if isinstance(result, tuple) else result
        if not isinstance(mesh, unreal.StaticMesh):
            raise RuntimeError("Static mesh creation failed: " + repr(result))
    build = mesh_editor.get_lod_build_settings(mesh, 0)
    build.set_editor_property("distance_field_resolution_scale", 4.0 if emitter else 2.0)
    build.set_editor_property("generate_distance_field_as_if_two_sided", emitter)
    build.set_editor_property("max_lumen_mesh_cards", 24)
    mesh_editor.set_lod_build_settings(mesh, 0, build)
    assert assets.save_loaded_asset(mesh, False)
    return mesh


def main():
    state = json.loads(STATE.read_text()) if STATE.exists() else {}
    assets = unreal.get_editor_subsystem(unreal.EditorAssetSubsystem)
    if assets.does_asset_exist(MAP) and state.get("complete"):
        raise RuntimeError("Completed fixture exists; refusing to overwrite a reviewable experiment.")
    fixture = json.loads(SOURCE.read_text(encoding="utf-8-sig"))
    tools = unreal.AssetToolsHelpers.get_asset_tools()
    mesh_editor = unreal.get_editor_subsystem(unreal.StaticMeshEditorSubsystem)
    if mesh_editor is None:
        raise RuntimeError("Run with full UnrealEditor -ExecutePythonScript, not the PythonScript commandlet; mesh editor subsystem is required.")
    materials, meshes = {}, {}
    for item in fixture["geometry"]:
        if item["material"] not in materials:
            materials[item["material"]] = create_material(item, tools, assets)
        meshes[item["name"]] = create_mesh(item, assets, mesh_editor)
        meshes[item["name"]].set_material(0, materials[item["material"]])
        assert assets.save_loaded_asset(meshes[item["name"]], False)

    emitter_parent = materials["Ceiling Emitter"]
    emitter_instance_path = ROOT + "/Materials/MI_CeilingEmitter"
    emitter_instance = assets.load_asset(emitter_instance_path) if assets.does_asset_exist(emitter_instance_path) else tools.create_asset(
        "MI_CeilingEmitter", ROOT + "/Materials", unreal.MaterialInstanceConstant, unreal.MaterialInstanceConstantFactoryNew())
    unreal.MaterialEditingLibrary.set_material_instance_parent(emitter_instance, emitter_parent)
    unreal.MaterialEditingLibrary.set_material_instance_scalar_parameter_value(emitter_instance, "EmissionScale", 1.0)
    assert assets.save_loaded_asset(emitter_instance, False)
    materials["Ceiling Emitter"] = emitter_instance

    STATE.write_text(json.dumps({"complete": False, "map": MAP}, indent=2))
    levels = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    if assets.does_asset_exist(MAP):
        assert levels.load_level(MAP)
        for actor in actors.get_all_level_actors():
            if TAG in [str(tag) for tag in actor.tags]:
                assert actors.destroy_actor(actor)
    else:
        assert levels.new_level(MAP)

    manifest = []
    for item in fixture["geometry"]:
        location = unreal.Vector(*xyz_cm(item["position"]))
        rotation = unreal.Rotator(pitch=0, yaw=item["rotation"][1], roll=0)
        actor = actors.spawn_actor_from_class(unreal.StaticMeshActor, location, rotation)
        actor.set_actor_label(item["name"])
        actor.tags = [TAG]
        component = actor.static_mesh_component
        component.set_static_mesh(meshes[item["name"]])
        component.set_material(0, materials[item["material"]])
        if max(item["emissionLinear"]) > 0:
            component.set_emissive_light_source(True)
        manifest.append({"name": item["name"], "position_cm": xyz_cm(item["position"]),
                         "yaw": item["rotation"][1], "mesh": meshes[item["name"]].get_path_name(),
                         "material": materials[item["material"]].get_path_name()})

    volume = actors.spawn_actor_from_class(unreal.PostProcessVolume, unreal.Vector())
    volume.set_actor_label("Reference - Manual EV4 - Lumen")
    volume.tags = [TAG]
    volume.set_editor_property("unbound", True)
    settings = volume.get_editor_property("settings")

    def pp(name, value):
        settings.set_editor_property("override_" + name, True)
        settings.set_editor_property(name, value)

    pp("dynamic_global_illumination_method", unreal.DynamicGlobalIlluminationMethod.LUMEN)
    pp("reflection_method", unreal.ReflectionMethod.LUMEN)
    pp("auto_exposure_method", unreal.AutoExposureMethod.AEM_MANUAL)
    pp("auto_exposure_apply_physical_camera_exposure", True)
    pp("camera_iso", 100.0)
    pp("camera_shutter_speed", 1.0)
    pp("depth_of_field_fstop", 4.0)
    pp("auto_exposure_bias", 0.0)
    pp("bloom_intensity", 0.0)
    pp("motion_blur_amount", 0.0)
    pp("vignette_intensity", 0.0)
    pp("ambient_occlusion_intensity", 0.0)
    pp("local_exposure_highlight_contrast_scale", 1.0)
    pp("local_exposure_shadow_contrast_scale", 1.0)
    volume.set_editor_property("settings", settings)

    camera = actors.spawn_actor_from_class(unreal.CameraActor, unreal.Vector(*xyz_cm(fixture["camera"]["position"])))
    camera.set_actor_label("Cornell Comparison Camera - Unity Match")
    camera.tags = [TAG]
    camera_component = camera.camera_component
    aspect = fixture["camera"]["aspect"]
    horizontal_fov = math.degrees(2 * math.atan(math.tan(math.radians(fixture["camera"]["verticalFov"]) / 2) * aspect))
    camera_component.set_editor_property("field_of_view", horizontal_fov)
    camera_component.set_editor_property("aspect_ratio", aspect)
    camera_component.set_editor_property("constrain_aspect_ratio", True)
    camera_component.set_editor_property("post_process_blend_weight", 0.0)
    camera.set_editor_property("auto_activate_for_player", unreal.AutoReceiveInput.PLAYER0)
    world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
    world.get_world_settings().set_editor_property("force_no_precomputed_lighting", True)
    assert not any(isinstance(actor, unreal.Light) for actor in actors.get_all_level_actors())
    assert levels.save_current_level()
    state = {"complete": True, "map": MAP, "engine": unreal.SystemLibrary.get_engine_version(),
             "unity_source": fixture["source"], "geometry": manifest, "horizontal_fov": horizontal_fov,
             "vertical_fov": fixture["camera"]["verticalFov"], "aspect": aspect,
             "ev100": 4, "light_count": 0, "gi": "Lumen Software Ray Tracing / Epic baseline",
             "coordinate_mapping": "Unity (x,y,z) metres -> Unreal (z,x,y) centimetres; unity yaw -> unreal yaw",
             "materials_linear": {item["material"]: {"base": item["baseColorLinear"], "emission": item["emissionLinear"],
                                                        "roughness": item["roughness"]} for item in fixture["geometry"]}}
    STATE.write_text(json.dumps(state, indent=2))
    unreal.log("VIVID_CORNELL_BUILD_OK " + MAP)


main()

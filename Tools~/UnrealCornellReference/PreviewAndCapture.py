"""Open/pilot the matching camera, then capture emission on/off/restored.

Runs inside the full Editor. Tick callbacks allow real Lumen frames to converge.
No analytic light is created, and the saved material instance remains switched on.
"""
import json
import time
from pathlib import Path

import unreal

project = Path(unreal.Paths.project_dir())
output = project / "Saved/Diagnostics"
levels = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
assert levels.load_level("/Game/VividReference/Maps/CornellBoxEmissiveCeiling")
world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
camera = next(a for a in actors.get_all_level_actors() if isinstance(a, unreal.CameraActor))
emitter = unreal.load_asset("/Game/VividReference/Materials/MI_CeilingEmitter")
for command in ["sg.ViewDistanceQuality 3", "sg.AntiAliasingQuality 3", "sg.ShadowQuality 3",
                "sg.GlobalIlluminationQuality 3", "sg.ReflectionQuality 3", "sg.PostProcessQuality 3",
                "sg.TextureQuality 3", "sg.EffectsQuality 3", "sg.FoliageQuality 3", "sg.ShadingQuality 3",
                "r.Lumen.HardwareRayTracing 0", "r.Lumen.ScreenProbeGather.ScreenTraces 1",
                "r.ScreenPercentage 100", "r.DynamicRes.OperationMode 0", "r.HighResScreenshotDelay 64", "t.MaxFPS 60"]:
    unreal.SystemLibrary.execute_console_command(world, command)
levels.pilot_level_actor(camera)
levels.set_exact_camera_view(True)
levels.editor_set_viewport_realtime(True)
levels.editor_set_game_view(True)
unreal.get_editor_subsystem(unreal.LevelEditorSubsystem).editor_invalidate_viewports()
unreal.AutomationLibrary.finish_loading_before_screenshot()

_vivid_capture = {"arm": 0, "frames": 0, "task": None, "start": time.time(), "results": []}
_vivid_arms = [("lumen-emission-on", 1.0, 512), ("lumen-emission-off", 0.0, 256),
               ("lumen-emission-restored", 1.0, 512)]
unreal.MaterialEditingLibrary.set_material_instance_scalar_parameter_value(emitter, "EmissionScale", 1.0)


def _vivid_tick(delta):
    try:
        state = _vivid_capture
        arm, level, warmup = _vivid_arms[state["arm"]]
        if state["task"] is not None:
            if not state["task"].is_task_done():
                levels.editor_invalidate_viewports()
                return
            state["results"].append({"arm": arm, "scale": level, "warmup_frames": state["frames"]})
            state["arm"] += 1
            if state["arm"] == len(_vivid_arms):
                unreal.MaterialEditingLibrary.set_material_instance_scalar_parameter_value(emitter, "EmissionScale", 1.0)
                unreal.get_editor_subsystem(unreal.EditorAssetSubsystem).save_loaded_asset(emitter, False)
                levels.pilot_level_actor(camera)
                levels.set_exact_camera_view(True)
                levels.save_current_level()
                report = {"complete": True, "engine": unreal.SystemLibrary.get_engine_version(),
                          "seconds": time.time() - state["start"], "results": state["results"],
                          "light_count": sum(isinstance(a, unreal.Light) for a in actors.get_all_level_actors()),
                          "hardware_ray_tracing": unreal.SystemLibrary.get_console_variable_int_value("r.Lumen.HardwareRayTracing"),
                          "gi_quality": unreal.SystemLibrary.get_console_variable_int_value("sg.GlobalIlluminationQuality")}
                (output / "capture.json").write_text(json.dumps(report, indent=2))
                unreal.unregister_slate_post_tick_callback(_vivid_tick_handle)
                unreal.log("VIVID_CORNELL_CAPTURE_OK")
                return
            state["frames"] = 0
            state["task"] = None
            unreal.MaterialEditingLibrary.set_material_instance_scalar_parameter_value(emitter, "EmissionScale", _vivid_arms[state["arm"]][1])
            return
        state["frames"] += 1
        if state["frames"] % 64 == 0:
            (output / "progress.json").write_text(json.dumps({"arm": arm, "frames": state["frames"], "target": warmup}))
        if state["frames"] >= warmup:
            levels.pilot_level_actor(camera)
            levels.set_exact_camera_view(True)
            state["task"] = unreal.AutomationLibrary.take_high_res_screenshot(
                1920, 1080, str(output / (arm + ".png")), camera=camera, delay=0.0)
            if state["task"] is None or not state["task"].is_valid_task():
                raise RuntimeError("Screenshot request failed: " + arm)
    except Exception as error:
        unreal.MaterialEditingLibrary.set_material_instance_scalar_parameter_value(emitter, "EmissionScale", 1.0)
        (output / "capture-error.txt").write_text(repr(error))
        unreal.unregister_slate_post_tick_callback(_vivid_tick_handle)
        unreal.log_error("VIVID_CORNELL_CAPTURE_FAILED " + repr(error))


_vivid_tick_handle = unreal.register_slate_post_tick_callback(_vivid_tick)

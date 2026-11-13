"""Pilot the reference camera without modifying or rebuilding saved assets."""
import unreal

levels = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
map_path = "/Game/VividReference/Maps/CornellBoxEmissiveCeiling"
if world is None or world.get_path_name().split(".")[0] != map_path:
    if unreal.EditorLoadingAndSavingUtils.get_dirty_map_packages():
        raise RuntimeError("Save the current dirty map before switching to the reference.")
    assert levels.load_level(map_path)
actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
camera = next(a for a in actors.get_all_level_actors() if isinstance(a, unreal.CameraActor))
levels.pilot_level_actor(camera)
levels.set_exact_camera_view(True)
levels.editor_set_viewport_realtime(True)
levels.editor_set_game_view(True)
levels.editor_invalidate_viewports()

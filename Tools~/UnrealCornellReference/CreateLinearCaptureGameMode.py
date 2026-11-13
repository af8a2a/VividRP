"""Run once with full Editor -nullrhi -ExecutePythonScript. Does not edit maps."""
import unreal

root = "/Game/VividReference/Capture"
path = root + "/BP_LinearGICaptureGameMode"
assets = unreal.get_editor_subsystem(unreal.EditorAssetSubsystem)
if assets.does_asset_exist(path):
    blueprint = assets.load_asset(path)
else:
    factory = unreal.BlueprintFactory()
    factory.set_editor_property("parent_class", unreal.GameModeBase)
    blueprint = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
        "BP_LinearGICaptureGameMode", root, unreal.Blueprint, factory)
default = unreal.get_default_object(blueprint.generated_class())
default.set_editor_property("default_pawn_class", None)
default.set_editor_property("spectator_class", None)
default.set_editor_property("hud_class", None)
assert assets.save_loaded_asset(blueprint, False)

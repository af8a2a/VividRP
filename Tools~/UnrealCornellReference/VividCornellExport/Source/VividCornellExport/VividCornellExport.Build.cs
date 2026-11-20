using System.IO;
using UnrealBuildTool;

public class VividCornellExport : ModuleRules
{
    public VividCornellExport(ReadOnlyTargetRules Target) : base(Target)
    {
        PrivateDependencyModuleNames.AddRange(new[] { "Core", "CoreUObject", "Engine", "Projects", "RenderCore", "Renderer", "RHI" });
        PrivateIncludePaths.Add(Path.Combine(GetModuleDirectory("Renderer"), "Private"));
        PrivateIncludePaths.Add(Path.Combine(GetModuleDirectory("Renderer"), "Internal"));
    }
}

#include "Interfaces/IPluginManager.h"
#include "Modules/ModuleManager.h"
#include "Misc/CommandLine.h"
#include "SceneViewExtension.h"
#include "ShaderCore.h"

void ExportCardMaterialPages(FRDGBuilder& Graph, const FSceneView& View);

class FVividMaterialExportView final : public FSceneViewExtensionBase
{
public:
    explicit FVividMaterialExportView(const FAutoRegister& Register) : FSceneViewExtensionBase(Register) {}
    void PrePostProcessPass_RenderThread(FRDGBuilder& Graph, const FSceneView& View, const FPostProcessingInputs& Inputs) override
    {
        ExportCardMaterialPages(Graph, View);
    }
};

class FVividCornellExportModule final : public IModuleInterface
{
    TSharedPtr<FVividMaterialExportView, ESPMode::ThreadSafe> Extension;
    void Install()
    {
        Extension = FSceneViewExtensions::NewExtension<FVividMaterialExportView>();
    }
public:
    void StartupModule() override
    {
        const auto Plugin = IPluginManager::Get().FindPlugin(TEXT("VividCornellExport"));
        AddShaderSourceDirectoryMapping(TEXT("/VividCornellExport"), Plugin->GetBaseDir() / TEXT("Shaders"));
        FString Output;
        if (FParse::Value(FCommandLine::Get(), TEXT("VividMaterialPages="), Output) && !Output.IsEmpty())
            FCoreDelegates::OnPostEngineInit.AddRaw(this, &FVividCornellExportModule::Install);
    }
    void ShutdownModule() override
    {
        FCoreDelegates::OnPostEngineInit.RemoveAll(this);
        Extension.Reset();
    }
};
IMPLEMENT_MODULE(FVividCornellExportModule,VividCornellExport)

// Call ExportCardMaterialPages from a scene view extension after Lumen Scene rendering.
#include "ScenePrivate.h"
#include "Lumen/LumenSceneData.h"
#include "RenderGraphUtils.h"
#include "GlobalShader.h"
#include "RHIGPUReadback.h"
#include "Misc/FileHelper.h"
#include "Serialization/MemoryWriter.h"

class FExportCardMaterialPagesCS : public FGlobalShader
{
    DECLARE_GLOBAL_SHADER(FExportCardMaterialPagesCS);
    SHADER_USE_PARAMETER_STRUCT(FExportCardMaterialPagesCS,FGlobalShader);
    BEGIN_SHADER_PARAMETER_STRUCT(FParameters,)
        SHADER_PARAMETER_RDG_TEXTURE_UAV(RWTexture2D<uint4>, AlbedoBlocks)
        SHADER_PARAMETER_RDG_TEXTURE_UAV(RWTexture2D<uint4>, NormalBlocks)
        SHADER_PARAMETER_RDG_TEXTURE_UAV(RWTexture2D<uint4>, EmissiveBlocks)
        SHADER_PARAMETER_RDG_TEXTURE(Texture2D<float>, PageDepth)
        SHADER_PARAMETER_RDG_TEXTURE(Texture2D<float>, PageOpacity)
        SHADER_PARAMETER_RDG_BUFFER_SRV(StructuredBuffer<uint2>, BlockCoords)
        SHADER_PARAMETER_RDG_BUFFER_UAV(RWStructuredBuffer<uint4>, Records)
        SHADER_PARAMETER(uint32, NumBlocks)
    END_SHADER_PARAMETER_STRUCT()
};
IMPLEMENT_GLOBAL_SHADER(FExportCardMaterialPagesCS,"/VividCornellExport/ExportCardMaterialPages.usf","Export",SF_Compute);

void ExportCardMaterialPages(FRDGBuilder& Graph, const FSceneView& Base)
{
    static FString Output = [](){ FString S; FParse::Value(FCommandLine::Get(), TEXT("VividMaterialPages="), S); return S; }();
    static bool Finished = false;
    if (Output.IsEmpty() || Finished || !Base.bIsViewInfo || Base.Family->FrameNumber < 639) return;
    const auto& View = static_cast<const FViewInfo&>(Base);
    const FLumenSceneData* L = View.ViewLumenSceneData;
    static TUniquePtr<FRHIGPUBufferReadback> Reads[5];
    static TArray<uint8> Package;
    static uint32 Sizes[5];
    if (Reads[0])
    {
        for (const auto& Read : Reads) if (!Read->IsReady()) return;
        for (int i = 0; i < 5; ++i)
        {
            const uint8* Ptr = static_cast<const uint8*>(Reads[i]->Lock(Sizes[i]));
            Package.Append(Ptr, Sizes[i]);
            Reads[i]->Unlock();
            Reads[i].Reset();
        }
        Finished = true;
        if (!FFileHelper::SaveArrayToFile(Package,*Output)) UE_LOG(LogTemp,Error,TEXT("Failed to save material pages: %s"),*Output);
        return;
    }
    if (!L || !L->AlbedoAtlas || !L->NormalAtlas || !L->EmissiveAtlas || !L->DepthAtlas || !L->OpacityAtlas
        || !L->CardBuffer || !L->MeshCardsBuffer || !L->CardPageBuffer || !L->PageTableBuffer
        || L->PageTable.Num() == 0) return;
    auto* A = Graph.RegisterExternalTexture(L->AlbedoAtlas);
    auto* N = Graph.RegisterExternalTexture(L->NormalAtlas);
    auto* E = Graph.RegisterExternalTexture(L->EmissiveAtlas);
    if (A->Desc.Format != PF_BC7 || N->Desc.Format != PF_BC5 || E->Desc.Format != PF_BC6H
        || A->Desc.UAVFormat != PF_R32G32B32A32_UINT || N->Desc.UAVFormat != A->Desc.UAVFormat || E->Desc.UAVFormat != A->Desc.UAVFormat)
    {
        UE_LOG(LogTemp, Error, TEXT("Expected native BC7/BC5/BC6H Surface Cache with integer UAV views."));
        Finished = true;
        return;
    }
    TArray<FUintVector2> Blocks;
    TArray<uint32> PageRecords;
    for (int i = 0; i < L->PageTable.Num(); ++i)
    {
        if (!L->PageTable.IsAllocated(i)) continue;
        const auto& Pg = L->PageTable[i];
        if (!Pg.IsMapped()) continue;
        const auto& Rect = Pg.PhysicalAtlasRect;
        PageRecords.Append({uint32(i),uint32(Pg.ResLevel),uint32(Rect.Min.X),uint32(Rect.Min.Y),uint32(Rect.Width()),uint32(Rect.Height())});
        for (int y=Rect.Min.Y; y<Rect.Max.Y; y+=4)
            for (int x=Rect.Min.X; x<Rect.Max.X; x+=4)
                Blocks.Emplace(x/4,y/4);
    }
    if (Blocks.IsEmpty()) return;
    uint32 Magic = 0x50434d56, Version = 1, Width = A->Desc.Extent.X, Height = A->Desc.Extent.Y;
    uint32 Cards = L->Cards.Num(), Meshes = L->MeshCards.Num(), Table = L->PageTable.Num();
    uint32 Pages = PageRecords.Num()/6, NumBlocks = Blocks.Num();
    FMemoryWriter Writer(Package);
    Writer << Magic << Version << Width << Height << Cards << Meshes << Table << Pages << NumBlocks;
    Writer.Serialize(PageRecords.GetData(), PageRecords.Num()*sizeof(uint32));
    auto* P = Graph.AllocParameters<FExportCardMaterialPagesCS::FParameters>();
    P->AlbedoBlocks = Graph.CreateUAV(A);
    P->NormalBlocks = Graph.CreateUAV(N);
    P->EmissiveBlocks = Graph.CreateUAV(E);
    P->PageDepth = Graph.RegisterExternalTexture(L->DepthAtlas);
    P->PageOpacity = Graph.RegisterExternalTexture(L->OpacityAtlas);
    P->BlockCoords = Graph.CreateSRV(CreateStructuredBuffer(Graph,TEXT("Export.MaterialPageCoords"),sizeof(FUintVector2),NumBlocks,Blocks.GetData(),Blocks.Num()*sizeof(FUintVector2)));
    auto* Data = Graph.CreateBuffer(FRDGBufferDesc::CreateStructuredDesc(16, NumBlocks*6),TEXT("Export.MaterialPageBlocks"));
    P->Records = Graph.CreateUAV(Data);
    P->NumBlocks = NumBlocks;
    auto Shader = GetGlobalShaderMap(Base.GetFeatureLevel())->GetShader<FExportCardMaterialPagesCS>();
    FComputeShaderUtils::AddPass(Graph,RDG_EVENT_NAME("Export resident material blocks"),Shader,P,FIntVector(FMath::DivideAndRoundUp(NumBlocks,64u),1,1));
    FRDGBufferRef Buffers[] = {Graph.RegisterExternalBuffer(L->CardBuffer),Graph.RegisterExternalBuffer(L->MeshCardsBuffer),
        Graph.RegisterExternalBuffer(L->PageTableBuffer),Graph.RegisterExternalBuffer(L->CardPageBuffer),Data};
    Sizes[0]=Cards*160; Sizes[1]=Meshes*96; Sizes[2]=Table*8; Sizes[3]=Table*80; Sizes[4]=NumBlocks*96;
    for (int i=0; i<5; ++i)
    {
        Reads[i]=MakeUnique<FRHIGPUBufferReadback>(TEXT("Material page export"));
        AddEnqueueCopyPass(Graph,Reads[i].Get(),Buffers[i],Sizes[i]);
    }
}

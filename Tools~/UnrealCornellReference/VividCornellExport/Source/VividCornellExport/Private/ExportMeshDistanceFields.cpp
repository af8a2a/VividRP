// Build in an Editor module with Core, CoreUObject and Engine dependencies.
// Vivid.ExportDistanceField <static mesh object path> <absolute output filename>
#include "AssetCompilingManager.h"
#include "DistanceFieldAtlas.h"
#include "Engine/StaticMesh.h"
#include "HAL/IConsoleManager.h"
#include "Misc/FileHelper.h"
#include "Serialization/MemoryWriter.h"
#include "StaticMeshResources.h"

static void ExportMeshDistanceField(const TArray<FString>& Args)
{
    if (Args.Num() != 2)
    {
        UE_LOG(LogTemp, Error, TEXT("Expected Vivid.ExportDistanceField <object path> <output>"));
        return;
    }
    UStaticMesh* Mesh = LoadObject<UStaticMesh>(nullptr, *Args[0]);
    FAssetCompilingManager::Get().FinishAllCompilation();
    const FStaticMeshRenderData* RenderData = Mesh ? Mesh->GetRenderData() : nullptr;
    FDistanceFieldVolumeData* Field = RenderData && !RenderData->LODResources.IsEmpty()
        ? RenderData->LODResources[0].DistanceFieldData : nullptr;
    if (!Field || Field->AlwaysLoadedMip.IsEmpty() || Field->StreamableMips.GetBulkDataSize() <= 0)
    {
        UE_LOG(LogTemp, Error, TEXT("Missing sparse distance field on %s"), *Args[0]);
        return;
    }

    TArray<uint8> Bytes;
    FMemoryWriter Writer(Bytes);
    uint32 Magic = 0x46445356, Version = 1, NumMips = DistanceField::NumMips;
    uint32 TwoSided = Field->bMostlyTwoSided ? 1 : 0;
    FTCHARToUTF8 Name(*Mesh->GetPathName());
    uint32 NameSize = Name.Length();
    Writer << Magic << Version << NumMips << TwoSided << NameSize;
    Writer.Serialize(const_cast<ANSICHAR*>(Name.Get()), NameSize);
    FBox3f Geometry(Mesh->GetBoundingBox());
    Writer << Geometry.Min << Geometry.Max << Field->LocalSpaceMeshBounds.Min << Field->LocalSpaceMeshBounds.Max;
    const int64 StreamSize = Field->StreamableMips.GetBulkDataSize();
    const uint8* Stream = static_cast<const uint8*>(Field->StreamableMips.LockReadOnly());
    bool Valid = Stream != nullptr;
    for (uint32 Index = 0; Valid && Index < NumMips; ++Index)
    {
        FSparseDistanceFieldMip& Mip = Field->Mips[Index];
        Writer << Mip.IndirectionDimensions << Mip.NumDistanceFieldBricks << Mip.VolumeToVirtualUVScale
            << Mip.VolumeToVirtualUVAdd << Mip.DistanceFieldToVolumeScaleBias;
        const bool Last = Index == NumMips - 1;
        uint32 Size = Last ? Field->AlwaysLoadedMip.Num() : Mip.BulkSize;
        int64 Expected = int64(Mip.IndirectionDimensions.X) * Mip.IndirectionDimensions.Y * Mip.IndirectionDimensions.Z * 4
            + int64(Mip.NumDistanceFieldBricks) * DistanceField::BrickSize * DistanceField::BrickSize * DistanceField::BrickSize;
        Valid = Size == Expected && (Last || int64(Mip.BulkOffset) + Size <= StreamSize);
        if (Valid)
        {
            Writer << Size;
            const uint8* Data = Last ? Field->AlwaysLoadedMip.GetData() : Stream + Mip.BulkOffset;
            Writer.Serialize(const_cast<uint8*>(Data), Size);
        }
    }
    Field->StreamableMips.Unlock();
    if (!Valid || Writer.IsError() || !FFileHelper::SaveArrayToFile(Bytes, *Args[1]))
    {
        UE_LOG(LogTemp, Error, TEXT("Failed to export complete distance field %s"), *Args[0]);
        return;
    }
    UE_LOG(LogTemp, Display, TEXT("Exported %s (%d bytes) to %s"), *Args[0], Bytes.Num(), *Args[1]);
}

static FAutoConsoleCommand GExportMeshDistanceField(TEXT("Vivid.ExportDistanceField"),
    TEXT("Write all baked sparse distance field mips for one static mesh."),
    FConsoleCommandWithArgsDelegate::CreateStatic(&ExportMeshDistanceField));

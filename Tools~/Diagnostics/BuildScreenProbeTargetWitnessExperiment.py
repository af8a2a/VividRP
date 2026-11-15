"""Instrument one failing temporal invocation without adding GPU resources."""
import argparse
import json
from pathlib import Path

from BuildScreenProbeUnusedBinWitnessExperiment import build as unused_build, instrument_shader as unused_instrument


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise ValueError(f"Expected one original fragment: {old[:100]!r}")
    return source.replace(old, new, 1)


def instrument_shader(source):
    source = unused_instrument(source)
    helper = """
// Temporary target witness. Slots 9..15 have one writer: tile (74,47), bin 10.
bool TargetWitnessStorageAvailable()
{
    return _SdfDiffuseScreenSize.x == 1920.0 && _SdfDiffuseScreenSize.y == 1080.0
        && _SdfDiffuseAdaptiveProbes != 0u && _SdfDiffuseGeometry[uint2(0u, 68u)].w < 0.0;
}

void StoreTargetWitness(uint slot, uint4 words)
{
    uint2 address = uint2(slot, 68u);
    _SdfDirectionalTemporalOutput[address] = f16tof32(0x400u + (words & 8191u));
    _SdfDirectionalTemporalOutput[address + uint2(0u, 136u)] = f16tof32(0x400u + ((words >> 13u) & 8191u));
    _SdfDirectionalTemporalOutput[address + uint2(0u, 272u)] = f16tof32(0x400u + (words >> 26u));
}

"""
    entry = "[numthreads(16, 1, 1)]\nvoid ScreenProbeTemporallyAccumulateTraceRadianceCS"
    source = replace_once(source, entry, helper + entry)
    start = source.index("void ScreenProbeTemporallyAccumulateTraceRadianceCS")
    stop = source.index("void ScreenProbeFilterGatherTracesCS", start)
    kernel = source[start:stop]
    kernel = replace_once(kernel, "    float4 temporalPlane = plane;", """    float4 temporalPlane = plane;
    float4 witnessCurrent = current;
    float4 witnessClip = 0.0;
    float4 witnessStats = 0.0;""")
    kernel = replace_once(kernel, "        float2 uv = ScreenUv(clip);", "        witnessClip = clip;\n        float2 uv = ScreenUv(clip);")
    kernel = replace_once(kernel, "            if (total > 1e-5)", "            witnessStats = float4(total, geometryTotal, ageSum, bestWeight);\n            if (total > 1e-5)")
    kernel = replace_once(kernel, "    if (robust && (_SdfGatherHistoryDiagnostics & 2048u)", "    float4 witnessBeforeBorrow = current;\n    if (robust && (_SdfGatherHistoryDiagnostics & 2048u)")
    kernel = replace_once(kernel, "    _SdfDirectionalTemporalOutput[address] = RoundSdfHistory(current);", """    // Prevent the unused storage invocation from racing the selected valid invocation.
    if (all(tile == uint2(0u, 68u)) && bin >= 9u && TargetWitnessStorageAvailable())
        return;
    _SdfDirectionalTemporalOutput[address] = RoundSdfHistory(current);""")
    kernel = replace_once(kernel, "    RecordUnusedBinTemporalWitness(tile, bin, geometry.w);", """    RecordUnusedBinTemporalWitness(tile, bin, geometry.w);
    if (all(tile == uint2(74u, 47u)) && bin == 10u && TargetWitnessStorageAvailable())
    {
        StoreTargetWitness(9u, asuint(geometry));
        StoreTargetWitness(10u, asuint(witnessCurrent));
        StoreTargetWitness(11u, asuint(hit));
        StoreTargetWitness(12u, asuint(plane));
        StoreTargetWitness(13u, asuint(witnessClip));
        StoreTargetWitness(14u, asuint(witnessStats));
        StoreTargetWitness(15u, asuint(witnessBeforeBorrow));
    }""")
    return source[:start] + kernel + source[stop:]


def build(template, output):
    source = unused_build(template, output, True)
    source = replace_once(source, "byte[] zeroPacketRow=new byte[72];", "byte[] zeroPacketRow=new byte[128];\n    var rawWitnessPackets=new List<byte[]>();\n    object rawWitnessArchive=null;")
    source = replace_once(source, "var words=new uint[36];", "var words=new uint[64];")
    source = replace_once(source, "bin<9;++bin", "bin<16;++bin")
    source = replace_once(source, "witnesses.Add(new {arm=name,frame=f,words});", """witnesses.Add(new {arm=name,frame=f,words});
                    var packet=new byte[384];
                    for(int plane=0;plane<3;++plane) Buffer.BlockCopy(bytes,(68+plane*136)*1920*8,packet,plane*128,128);
                    rawWitnessPackets.Add(packet);""")
    source = replace_once(source, '            System.IO.File.WriteAllText(output+"/capture.json"', """            // Persist original encoded GPU words, including partial-run evidence, after rendering stops.
            string packetPath=output+"/witness-packets.bin";
            using(var file=System.IO.File.Create(packetPath)) foreach(var packet in rawWitnessPackets) file.Write(packet,0,packet.Length);
            using(var hash=System.Security.Cryptography.SHA256.Create()) using(var file=System.IO.File.OpenRead(packetPath))
                rawWitnessArchive=new {path=packetPath,count=rawWitnessPackets.Count,packetBytes=384,
                    sha256=BitConverter.ToString(hash.ComputeHash(file)).Replace("-","").ToLowerInvariant()};
            System.IO.File.WriteAllText(output+"/capture.json\"""")
    source = replace_once(source, "expectWitness,witnesses,signalRecords,", "expectWitness,witnesses,signalRecords,rawWitnessArchive,")
    return source.replace('schema="screen-probe-unused-bin-witness.v1"', 'schema="screen-probe-target-witness.v1"')


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    template = Path(__file__).with_name("ScreenProbeAnchorGpuTimingCapture.cs").read_text(encoding="utf-8-sig")
    source = build(template, args.output.resolve())
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / "capture-script.cs").write_text(source, encoding="utf-8")
    (args.output / "protocol.json").write_text(json.dumps(dict(schema="screen-probe-target-witness-protocol.v1",
        arms=8, frames=64, target=[74,47,10], storage=[0,68], bins=16, packetBytes=384,
        shaderNewResources=0, textureSizeChanged=False, ownedPayloadBudget=3*1024**3,
        savedPayloadBudget=192*1024**2, rawWitnessArchiveBudget=512*384,
        attributionGate="Instrumented run must reproduce matching-input failure; inspect actual target inputs and pre-borrow output"), indent=2)+"\n", encoding="utf-8")
    print(json.dumps({"root":str(args.output.resolve())}))

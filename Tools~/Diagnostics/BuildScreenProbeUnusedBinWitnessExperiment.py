"""Prepare masked signal comparisons and an existing-atlas temporal CB witness."""
import argparse
import json
from pathlib import Path

from BuildScreenProbeDirectionalBindingExperiment import build as binding_build


WITNESS_HLSL = """
// Temporary witness: no new resource or atlas row. Never overwrite valid geometry.
void RecordUnusedBinTemporalWitness(uint2 tile, uint bin, float geometryValidity)
{
    if (_SdfDiffuseScreenSize.x != 1920.0 || _SdfDiffuseScreenSize.y != 1080.0
        || _SdfDiffuseAdaptiveProbes == 0u || any(tile != uint2(0u, 68u))
        || !(geometryValidity < 0.0) || bin >= 9u)
        return;
    uint4 words;
    if (bin < 4u)
        words = asuint(_SdfDiffusePreviousVP[bin]);
    else if (bin == 4u)
        words = asuint(_SdfDiffuseScreenSize);
    else if (bin == 5u)
        words = asuint(_SdfProbeCameraForward);
    else if (bin == 6u)
        words = asuint(_SdfHistoryLightScale);
    else if (bin == 7u)
        words = uint4(_SdfDirectionalHasHistory, _SdfRestirDense, _SdfDiffuseAdaptiveProbes, _SdfGatherHistoryDiagnostics);
    else
        words = uint4(0x12345678u, 0x89abcdefu, 0x31415926u, 0xfacefeedu);
    uint2 address = uint2(bin, 68u);
    _SdfDirectionalTemporalOutput[address] = f16tof32(0x400u + (words & 8191u));
    _SdfDirectionalTemporalOutput[address + uint2(0u, 136u)] = f16tof32(0x400u + ((words >> 13u) & 8191u));
    _SdfDirectionalTemporalOutput[address + uint2(0u, 272u)] = f16tof32(0x400u + (words >> 26u));
}

"""


def instrument_shader(source: str) -> str:
    entry = "[numthreads(16, 1, 1)]\nvoid ScreenProbeTemporallyAccumulateTraceRadianceCS"
    tail = "    _SdfDirectionalTemporalOutput[address + 2u * offset] = RoundSdfHistory(temporalPlane);\n"
    if source.count(entry) != 1 or source.count(tail) != 1:
        raise ValueError("Unexpected original temporal kernel structure")
    return source.replace(entry, WITNESS_HLSL + entry, 1).replace(
        tail, tail + "    RecordUnusedBinTemporalWitness(tile, bin, geometry.w);\n", 1)


def build(template: str, output: Path, witness: bool) -> str:
    source = binding_build(template, output)

    def replace(old: str, new: str) -> None:
        nonlocal source
        if source.count(old) != 1:
            raise ValueError(f"Expected one fragment: {old[:100]!r}")
        source = source.replace(old, new, 1)

    replace("// Original-shader short history binding capture. No GPU instrumentation or performance comparison.",
            "// Existing-atlas GPU witness capture. No performance comparison." if witness
            else "// Original-shader capture with the same invalid-cell signal mask as the witness.")
    replace("    const long retainedPayloadBudget=3L*1024*1024*1024;", """    const long retainedPayloadBudget=3L*1024*1024*1024;
    const bool expectWitness=EXPECT_WITNESS;
    var witnesses=new List<object>();var signalRecords=new List<object>();
    var referenceSignals=new Dictionary<string,string>();
    byte[] zeroPacketRow=new byte[72];
    bool Masked(string tag) => tag=="directional-temporal" || tag=="previous-directional"
        || tag=="directional-filtered" || tag=="previous-directional-filtered";
    string SignalHash(byte[] bytes,string tag) {
        int height=(tag=="directional-filtered" || tag=="previous-directional-filtered")?136:408;
        if(bytes.Length!=1920*height*8) throw new InvalidOperationException("Unexpected masked atlas size: "+tag);
        using var hash=System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        int cursor=0;
        for(int plane=0;plane<(height==408?3:1);++plane) {
            int start=((68+plane*136)*1920)*8;
            hash.AppendData(bytes,cursor,start-cursor);hash.AppendData(zeroPacketRow);
            cursor=start+zeroPacketRow.Length;
        }
        hash.AppendData(bytes,cursor,bytes.Length-cursor);
        return BitConverter.ToString(hash.GetHashAndReset()).Replace("-","").ToLowerInvariant();
    }
    uint[] DecodeWitness(byte[] bytes) {
        var words=new uint[36];
        for(int bin=0;bin<9;++bin) for(int channel=0;channel<4;++channel) {
            uint value=0;
            for(int plane=0;plane<3;++plane) {
                int at=(((68+plane*136)*1920+bin)*4+channel)*2;
                uint half=BitConverter.ToUInt16(bytes,at);
                uint limit=plane==2?63u:8191u;
                if(half<0x400u || half-0x400u>limit) throw new InvalidOperationException("Invalid witness encoding.");
                value|=(half-0x400u)<<(plane*13);
            }
            words[bin*4+channel]=value;
        }
        if(words[32]!=0x12345678u || words[33]!=0x89abcdefu || words[34]!=0x31415926u || words[35]!=0xfacefeedu)
            throw new InvalidOperationException("Missing existing-bin witness magic.");
        return words;
    }
    void CompareMaskedSignals() {
        bool first=arm.StartsWith("00-",StringComparison.Ordinal);
        var payloads=first?baselinePayloads:currentPayloads;
        foreach(var entry in payloads) {
            int slash=entry.Key.IndexOf('/');string tag=entry.Key.Substring(slash+1);
            if(!Masked(tag))continue;
            int f=int.Parse(entry.Key.Substring(0,slash));string signalSha256=SignalHash(entry.Value,tag);
            signalRecords.Add(new {arm,frame=f,tag,signalSha256});
            if(first) referenceSignals.Add(entry.Key,signalSha256);
            else if(signalSha256!=referenceSignals[entry.Key] && (tag=="directional-temporal" || tag=="directional-filtered"))
                functionalDifferences.Add((arm,f,tag));
        }
    }""".replace("EXPECT_WITNESS", "true" if witness else "false"))
    replace('|| tag=="rays" || tag=="proposal";', '|| tag=="directional-filtered" || tag=="previous-directional-filtered";')
    start = source.index("    void CompareMaskedSignals() {")
    end = source.index("    float[] V", start)
    compare_function = source[start:end]
    replace(compare_function, "")
    declaration = "    var functionalDifferences=new List<(string arm,int frame,string tag)>();\n"
    replace(declaration, declaration + compare_function)
    replace('if(mismatch && (tag=="raw" || tag=="composite" || tag=="directional-temporal" || tag=="directional-filtered"))',
            'if(mismatch && (tag=="raw" || tag=="composite"))')
    replace('                var row=new {arm=name,frame=f,tag,width,height,format,sha256};', """                if(tag=="geometry" && !(BitConverter.ToSingle(bytes,(68*120)*16+12)<0.0f))
                    throw new InvalidOperationException("The witness cell is not unused; no valid geometry may be masked.");
                if(tag=="directional-temporal" && expectWitness) {
                    uint[] words=DecodeWitness(bytes);witnesses.Add(new {arm=name,frame=f,words});
                }
                var row=new {arm=name,frame=f,tag,width,height,format,sha256};""")
    replace("            SaveFirstBundle();", "            CompareMaskedSignals();\n            SaveFirstBundle();")
    replace('schema="screen-probe-directional-binding.v1"', 'schema="screen-probe-unused-bin-witness.v1"')
    replace("firstBundle,savedPayloads,", "firstBundle,savedPayloads,expectWitness,witnesses,signalRecords,")
    return source


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--witness", action="store_true")
    args = parser.parse_args()
    template = Path(__file__).with_name("ScreenProbeAnchorGpuTimingCapture.cs")
    source = build(template.read_text(encoding="utf-8-sig"), args.output.resolve(), args.witness)
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / "capture-script.cs").write_text(source, encoding="utf-8")
    (args.output / "protocol.json").write_text(json.dumps(dict(schema="screen-probe-unused-bin-witness-protocol.v1",
        witness=args.witness, arms=8, frames=64, shaderNewResources=0, textureSizeChanged=False,
        cell=[0,68], bins=9, planes=[68,204,340], maskedRowBytes=72,
        ownedPayloadBudget=3*1024**3, savedPayloadBudget=192*1024**2,
        attributionGate="Witness-bearing run must reproduce a valid-signal divergence with matching relevant inputs"),indent=2)+"\n",encoding="utf-8")
    print(json.dumps({"root":str(args.output.resolve())}))

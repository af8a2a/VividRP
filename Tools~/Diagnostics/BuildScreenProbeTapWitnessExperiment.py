"""Record the four actual history taps of the selected temporal invocation."""
import argparse
import json
from pathlib import Path

from BuildScreenProbeTargetWitnessExperiment import build as target_build, instrument_shader as target_instrument, replace_once


def instrument_shader(source):
    # Reuse only the checked storage encoder, not the other variant's instrumentation.
    target = target_instrument(source)
    helper = target[target.index("// Temporary target witness."):target.index("[numthreads(16, 1, 1)]\nvoid ScreenProbeTemporally", target.index("// Temporary target witness."))]
    entry = "[numthreads(16, 1, 1)]\nvoid ScreenProbeTemporallyAccumulateTraceRadianceCS"
    source = replace_once(source, entry, helper + entry)
    start = source.index("void ScreenProbeTemporallyAccumulateTraceRadianceCS")
    stop = source.index("void ScreenProbeFilterGatherTracesCS", start)
    k = source[start:stop]
    k = replace_once(k, "    float4 temporalPlane = plane;", """    float4 temporalPlane = plane;
    float4 witnessGeometry[4], witnessLighting[4], witnessState[4];
    [unroll] for (uint i = 0u; i < 4u; ++i) { witnessGeometry[i] = 0.0; witnessLighting[i] = 0.0; witnessState[i] = 0.0; }
    float4 witnessClip = 0.0, witnessGrid = 0.0, witnessStats = 0.0;""")
    k = replace_once(k, "        float2 uv = ScreenUv(clip);", "        witnessClip = clip;\n        float2 uv = ScreenUv(clip);")
    k = replace_once(k, "            float2 fraction = frac(probe);", "            float2 fraction = frac(probe);\n            witnessGrid = float4(probe, fraction);")
    k = replace_once(k, "                float3 delta = oldGeometry.xyz - geometry.xyz;", """                witnessGeometry[tap] = oldGeometry;
                witnessState[tap].x = weight;
                float3 delta = oldGeometry.xyz - geometry.xyz;""")
    k = replace_once(k, "                uint2 oldAddress = DirectionalGatherAddress(oldTile, oldBin);", """                uint2 oldAddress = DirectionalGatherAddress(oldTile, oldBin);
                witnessState[tap].zw = asfloat(oldAddress);""")
    k = replace_once(k, "                oldLighting.rgb *= _SdfHistoryLightScale.rgb;", "                oldLighting.rgb *= _SdfHistoryLightScale.rgb;\n                witnessLighting[tap] = oldLighting;")
    k = replace_once(k, "                history += oldLighting.rgb * weight;", "                witnessState[tap].y = weight;\n                history += oldLighting.rgb * weight;")
    k = replace_once(k, "            if (total > 1e-5)", "            witnessStats = float4(total, geometryTotal, ageSum, bestWeight);\n            if (total > 1e-5)")
    k = replace_once(k, "    _SdfDirectionalTemporalOutput[address] = RoundSdfHistory(current);", """    if (all(tile == uint2(0u, 68u)) && TargetWitnessStorageAvailable())
        return;
    _SdfDirectionalTemporalOutput[address] = RoundSdfHistory(current);""")
    tail = "    _SdfDirectionalTemporalOutput[address + 2u * offset] = RoundSdfHistory(temporalPlane);"
    k = replace_once(k, tail, tail + """
    if (all(tile == uint2(74u, 47u)) && bin == 10u && TargetWitnessStorageAvailable())
    {
        [unroll] for (uint tap = 0u; tap < 4u; ++tap)
        {
            StoreTargetWitness(3u * tap, asuint(witnessGeometry[tap]));
            StoreTargetWitness(3u * tap + 1u, asuint(witnessLighting[tap]));
            StoreTargetWitness(3u * tap + 2u, asuint(witnessState[tap]));
        }
        StoreTargetWitness(12u, asuint(witnessClip));
        StoreTargetWitness(13u, asuint(witnessGrid));
        StoreTargetWitness(14u, asuint(witnessStats));
        StoreTargetWitness(15u, uint4(0x12345678u, 0x89abcdefu, 0x31415926u, 0xfacefeedu));
    }""")
    return source[:start] + k + source[stop:]


def build(template, output):
    s = target_build(template, output)
    s = replace_once(s, "words[32]!=0x12345678u || words[33]!=0x89abcdefu || words[34]!=0x31415926u || words[35]!=0xfacefeedu",
                     "words[60]!=0x12345678u || words[61]!=0x89abcdefu || words[62]!=0x31415926u || words[63]!=0xfacefeedu")
    return s.replace('schema="screen-probe-target-witness.v1"', 'schema="screen-probe-tap-witness.v1"')


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    template = Path(__file__).with_name("ScreenProbeAnchorGpuTimingCapture.cs").read_text(encoding="utf-8-sig")
    source = build(template, args.output.resolve())
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / "capture-script.cs").write_text(source, encoding="utf-8")
    (args.output / "protocol.json").write_text(json.dumps(dict(schema="screen-probe-tap-witness-protocol.v1",
        arms=8, frames=64, target=[74,47,10], storage=[0,68], bins=16, packetBytes=384,
        shaderNewResources=0, textureSizeChanged=False, ownedPayloadBudget=3*1024**3,
        savedPayloadBudget=192*1024**2, rawWitnessArchiveBudget=512*384,
        attributionGate="Compare actual per-tap geometry, selected addresses, lighting and weights at matched-input temporal divergence"), indent=2)+"\n", encoding="utf-8")
    print(json.dumps({"root":str(args.output.resolve())}))

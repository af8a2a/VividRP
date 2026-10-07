using System;
using Unity.Mathematics;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.GPUDriven
{
    // GraphToolkit-independent authoring defaults. A graph uses its current
    // geometry normal; the geometry-free contract default remains world +Z.
    internal static class MaterialOpenPBROpaqueAuthoring
    {
        internal static string GetPortName(OpenPBROpaqueFieldSemantic semantic)
        {
            switch (semantic)
            {
                case OpenPBROpaqueFieldSemantic.BaseWeight: return "BaseWeight";
                case OpenPBROpaqueFieldSemantic.BaseColor: return "BaseColor";
                case OpenPBROpaqueFieldSemantic.BaseDiffuseRoughness: return "BaseDiffuseRoughness";
                case OpenPBROpaqueFieldSemantic.BaseMetalness: return "BaseMetalness";
                case OpenPBROpaqueFieldSemantic.SpecularWeight: return "SpecularWeight";
                case OpenPBROpaqueFieldSemantic.SpecularColor: return "SpecularColor";
                case OpenPBROpaqueFieldSemantic.SpecularRoughness: return "SpecularRoughness";
                case OpenPBROpaqueFieldSemantic.SpecularIor: return "SpecularIor";
                case OpenPBROpaqueFieldSemantic.NormalWS: return "NormalWS";
                case OpenPBROpaqueFieldSemantic.EmissionLuminance: return "EmissionLuminance";
                case OpenPBROpaqueFieldSemantic.EmissionColor: return "EmissionColor";
                default: throw new ArgumentOutOfRangeException(nameof(semantic));
            }
        }

        internal static MaterialGraphValue CreateDefaultInput(
            MaterialGraph graph, string nodeId, OpenPBROpaqueFieldSemantic semantic)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));
            string defaultId = nodeId + ".Default." + GetPortName(semantic);
            switch (semantic)
            {
                case OpenPBROpaqueFieldSemantic.BaseWeight:
                    return graph.Constant(defaultId, OpenPBROpaqueContract.DefaultBaseWeight);
                case OpenPBROpaqueFieldSemantic.BaseColor:
                    return graph.Constant(defaultId, new float3(OpenPBROpaqueContract.DefaultBaseColor));
                case OpenPBROpaqueFieldSemantic.BaseDiffuseRoughness:
                    return graph.Constant(defaultId, OpenPBROpaqueContract.DefaultBaseDiffuseRoughness);
                case OpenPBROpaqueFieldSemantic.BaseMetalness:
                    return graph.Constant(defaultId, OpenPBROpaqueContract.DefaultBaseMetalness);
                case OpenPBROpaqueFieldSemantic.SpecularWeight:
                    return graph.Constant(defaultId, OpenPBROpaqueContract.DefaultSpecularWeight);
                case OpenPBROpaqueFieldSemantic.SpecularColor:
                    return graph.Constant(defaultId, new float3(OpenPBROpaqueContract.DefaultSpecularColor));
                case OpenPBROpaqueFieldSemantic.SpecularRoughness:
                    return graph.Constant(defaultId, OpenPBROpaqueContract.DefaultSpecularRoughness);
                case OpenPBROpaqueFieldSemantic.SpecularIor:
                    return graph.Constant(defaultId, OpenPBROpaqueContract.DefaultSpecularIor);
                case OpenPBROpaqueFieldSemantic.NormalWS:
                    return graph.ExternalInput(defaultId, MaterialExternalInput.GeometryNormalWS);
                case OpenPBROpaqueFieldSemantic.EmissionLuminance:
                    return graph.Constant(defaultId, OpenPBROpaqueContract.DefaultEmissionLuminance);
                case OpenPBROpaqueFieldSemantic.EmissionColor:
                    return graph.Constant(defaultId, new float3(OpenPBROpaqueContract.DefaultEmissionColor));
                default: throw new ArgumentOutOfRangeException(nameof(semantic));
            }
        }
    }
}

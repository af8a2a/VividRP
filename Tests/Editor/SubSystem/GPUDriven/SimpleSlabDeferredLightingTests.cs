using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class SimpleSlabDeferredLightingTests
    {
        [Test]
        public void FastAndDualSlab_OwnLightingWithoutLegacyBsdfOrFgd()
        {
            string source = Read("Shaders/Core/Public/VividDeferredLighting.hlsl");
            int start = source.IndexOf("VividSimpleSlabDeferredLighting EvaluateDeferredFastSlabLighting(", StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            int end = source.IndexOf("float3 VividDeferredFresnelSchlick(", start, StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThan(start));
            string fastPath = source.Substring(start, end - start);
            foreach (string legacy in new[] { "VividGBufferSurfaceData", "VividLitBSDFData", "GetVividPreLightData",
                         "ApplyVividSlabEnergyToLegacyPreLight", "PostEvaluateBSDF", "EvaluateBSDF_Env", "EvaluateBSDF_Area" })
                StringAssert.DoesNotContain(legacy, source);
            StringAssert.Contains("VividComposeSimpleSlabDeferredLighting", fastPath);
            StringAssert.Contains("lightLoopOutput = EvaluateDeferredFastSlabLighting(", source);
            StringAssert.Contains("lightLoopOutput = EvaluateDeferredDualSlabLighting(", source);
            StringAssert.DoesNotContain("HdrpLitLighting.hlsl", source);
            StringAssert.DoesNotContain("VividPreLightData", source);
            StringAssert.DoesNotContain("PreIntegratedFGD", source);
            StringAssert.Contains("baseEnergy.singleScatterSpecularAlbedo * baseEnvironmentWeight", source);
            StringAssert.Contains("topEnergy.singleScatterSpecularAlbedo * topWeight", source);
            string header = Read("Shaders/Core/Public/VividSimpleSlabDeferredLighting.hlsl");
            StringAssert.Contains($"#define VIVID_SIMPLE_SLAB_DEFERRED_LIGHTING_VERSION {MaterialProgramContract.SimpleSlabDeferredLightingVersion}u", header);
            foreach (string legacy in new[] { "HdrpLitLighting.hlsl", "PreIntegratedFGD.hlsl", "VividLitBSDFData", "PostEvaluateBSDF" })
                StringAssert.DoesNotContain(legacy, header);
            StringAssert.Contains("result.screenSpaceReflectionFGD = energy.singleScatterSpecularAlbedo;", header);
            StringAssert.Contains("result.screenSpaceReplaceableSpecularLighting = indirectLighting.singleScatterSpecular;", header);
            StringAssert.Contains("preExposedReplaceableSpecular * reflectionWeight", source);
        }

        [Test]
        public void DeferredEntryPoints_ShareNativeSurfacePixelContract()
        {
            foreach (string path in new[]
            {
                "Shaders/Material/DeferredLit.compute",
                "Shaders/Material/ShaderPass/SimpleDeferredLitPass.hlsl",
                "Shaders/Material/DeferredDirectionalLightingIndirectPass.hlsl"
            })
            {
                string source = Read(path);
                StringAssert.Contains("VividDeferredLighting.hlsl", source, path);
                StringAssert.Contains("VividEvaluateDeferredSurfacePixel(pixelCoord, deviceDepth, debugLighting)", source, path);
                foreach (string legacy in new[] { "HdrpLitLighting.hlsl", "UnpackVividGBufferSurfaceData",
                    "EvaluateBSDF_", "_GBuffer4", "_MainLightColor", "_AmbientColor" })
                    StringAssert.DoesNotContain(legacy, source, path);
            }
        }

        // Focused shader test: packed GBuffer -> production classification/indirect args ->
        // production Clear/Deferred. Not a substitute for the existing full SRP pixel tests.
        [TestCase(true, 0.0f, true, false)]
        [TestCase(false, 1.0f, true, false)]
        [TestCase(true, 0.4f, true, false)]
        [TestCase(false, 0.0f, false, false)]
        [TestCase(true, 0.4f, true, true)]
        public void ProductionFastSlab_MatchesLightingAndCompositingContract(
            bool directLights, float ssrWeight, bool lutReady, bool mixedTile)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and GPU readback required.");
            RTHandles.Initialize(1, 1);
            using var fixture = new PixelFixture();
            fixture.Run(directLights, ssrWeight, lutReady, mixedTile);
        }

        // 1: mip-colored sky, 2: partial probes + sky, 3: overlapping probes
        // consume all weight, 4: invalid first probe, 5: different N/R face fades.
        [TestCase(1, 0.0f, true)]
        [TestCase(1, 1.0f, true)]
        [TestCase(2, 0.0f, true)]
        [TestCase(2, 0.4f, true)]
        [TestCase(2, 1.0f, true)]
        [TestCase(2, 1.0f, false)]
        [TestCase(3, 0.0f, true)]
        [TestCase(3, 1.0f, true)]
        [TestCase(4, 0.0f, true)]
        [TestCase(5, 0.0f, true)]
        [TestCase(5, 1.0f, true)]
        public void ProductionFastSlab_SeparatesEnvironmentLobesAndSsr(
            int environment, float ssrWeight, bool ssrEnabled)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and GPU readback required.");
            RTHandles.Initialize(1, 1);
            using var fixture = new PixelFixture();
            fixture.Run(false, ssrWeight, true, false, environment, ssrEnabled);
        }

        [TestCase(1)] // Tube
        [TestCase(2)] // Backfacing rectangle
        [TestCase(3)] // Outside range
        [TestCase(4)] // Barn doors
        public void ProductionFastSlab_AreaLobesRetainLightGeometry(int areaCase)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and GPU readback required.");
            RTHandles.Initialize(1, 1);
            using var fixture = new PixelFixture();
            fixture.Run(true, 0, true, false, areaCase: areaCase);
        }

        [Test]
        public void AreaLight_ViewHemisphereGatesDiffuseAndSpecular()
        {
            if (!SystemInfo.supportsComputeShaders)
                Assert.Ignore("Compute shaders required.");
            RTHandles.Initialize(1, 1);
            using var fixture = new PixelFixture();
            fixture.AssertAreaViewHemisphere();
        }

        // Every tile also contains FastSlab, Unlit, AO-zero and no-SSR pixels.
        // Mode 3 forces an invalid Sidecar, which must remain diagnostic.
        [TestCase(1, 0.0f, 0.0f, false, false)]
        [TestCase(1, 1.0f / 255.0f, 0.0f, true, false)]
        [TestCase(1, 0.4f, 0.0f, true, false)]
        [TestCase(1, 0.4f, 0.4f, true, false)]
        [TestCase(1, 0.4f, 1.0f, false, false)]
        [TestCase(1, 1.0f, 1.0f, true, false)]
        [TestCase(2, 0.0f, 1.0f, false, false)]
        [TestCase(2, 1.0f / 255.0f, 0.0f, true, false)]
        [TestCase(2, 0.4f, 0.0f, true, false)]
        [TestCase(2, 0.4f, 0.4f, true, false)]
        [TestCase(2, 0.4f, 1.0f, false, false)]
        [TestCase(2, 1.0f, 1.0f, true, false)]
        [TestCase(2, 0.4f, 1.0f, true, true)]
        [TestCase(3, 0.4f, 1.0f, false, true)]
        public void ProductionDualSlab_MatchesLayerWeightsAndSingleScatterSsr(
            int dualMode, float layerWeight, float ssrWeight, bool directLights, bool mixedTile)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and GPU readback required.");
            RTHandles.Initialize(1, 1);
            using var fixture = new PixelFixture();
            fixture.Run(directLights, ssrWeight, true, mixedTile, environment: 5,
                dualMode: dualMode, layerWeight: layerWeight);
        }

        [TestCase(1, 1, true)]  // Horizontal + tube
        [TestCase(2, 4, true)]  // Vertical + barn doors
        [TestCase(2, 0, false)] // Missing Slab LUT
        public void ProductionDualSlab_RetainsAreaGeometryAndLutDiagnostics(int dualMode, int areaCase, bool lutReady)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and GPU readback required.");
            RTHandles.Initialize(1, 1);
            using var fixture = new PixelFixture();
            fixture.Run(true, 0.4f, lutReady, false, environment: 2, areaCase: areaCase, dualMode: dualMode);
        }

        [TestCase(1, 0.4f)]
        [TestCase(1, 1.0f)]
        [TestCase(2, 0.4f)]
        [TestCase(2, 1.0f)]
        public void ProductionDualSlab_WhiteEnvironmentPreservesLayerEnergyBudget(int dualMode, float layerWeight)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and GPU readback required.");
            RTHandles.Initialize(1, 1);
            using var fixture = new PixelFixture();
            fixture.Run(false, 0, true, false, environment: 6, dualMode: dualMode, layerWeight: layerWeight);
        }

        // Both raster entrypoints use the same independent reference as compute.
        // The non-sky pixels include metal/dielectric, AO-zero, Unlit and SSR opt-out.
        [TestCase(0, true, 0.0f, true, false)]
        [TestCase(0, false, 1.0f, true, false)]
        [TestCase(0, true, 0.4f, false, false)]
        [TestCase(1, true, 0.4f, true, true)]
        [TestCase(2, true, 0.4f, true, true)]
        [TestCase(2, false, 1.0f, true, false)]
        [TestCase(2, true, 1.0f, false, false)]
        [TestCase(3, false, 1.0f, true, true)]
        public void RasterDeferredEntryPoints_MatchNativeComputeContract(
            int dualMode, bool directLights, float ssrWeight, bool lutReady, bool mixedTile)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and GPU readback required.");
            RTHandles.Initialize(1, 1);
            using var fixture = new PixelFixture();
            fixture.Run(directLights, ssrWeight, lutReady, mixedTile, environment: 5,
                dualMode: dualMode, verifyRaster: true);
        }

        [TestCase(3, true)]  // Reserved GeneralSlab, dispatched through variant 1
        [TestCase(3, false)] // Unlit emission survives missing LUT in the same tile
        [TestCase(5, true)]  // Reserved Subsurface
        [TestCase(14, true)] // Reserved CatchAll material class (not the tile variant)
        [TestCase(6, true)]  // Unknown class sanitized to Error
        public void UnsupportedDeferredClass_IsDiagnosticAcrossComputeAndRaster(int exportClass, bool lutReady)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and GPU readback required.");
            RTHandles.Initialize(1, 1);
            using var fixture = new PixelFixture();
            fixture.Run(true, 0.4f, lutReady, false, verifyRaster: true, overrideClass: exportClass);
        }

        private sealed class PixelFixture : IDisposable
        {
            private readonly List<Object> m_Objects = new();
            private readonly List<ComputeBuffer> m_Buffers = new();
            private readonly VividSlabLut m_Lut = new();
            private readonly CommandBuffer m_Cmd = new();
            private readonly MaterialPropertyBlock m_RasterProperties = new();
            private ComputeShader m_Production;

            internal void AssertAreaViewHemisphere()
            {
                ComputeShader shader = Load("Tests/Editor/SubSystem/GPUDriven/SimpleSlabDeferredLightingTests.compute");
                Assert.That(m_Lut.Create(Load("Shaders/Core/Private/VividSlabLut.compute")), Is.True);
                int kernel = shader.FindKernel("EvaluateAreaViewHemisphere");
                var ltc = Track(new Texture2DArray(1, 1, 3, TextureFormat.RGBAFloat, false, true));
                for (int slice = 0; slice < 3; ++slice)
                    ltc.SetPixels(new[] { new Color(2, 0, 1.3f, 0) }, slice);
                ltc.Apply(false, false);
                var values = new Vector4[20]; // Rectangle/tube x five views x diffuse/specular.
                ComputeBuffer output = Buffer(values);
                Texture(shader, kernel, "_VividSlabLut", m_Lut.Texture.rt);
                Texture(shader, kernel, "_LtcData", ltc);
                Bind(shader, kernel, "_Expected", output);
                m_Cmd.DispatchCompute(shader, kernel, 10, 1, 1);
                Graphics.ExecuteCommandBuffer(m_Cmd);
                output.GetData(values);
                for (int i = 0; i < values.Length; ++i)
                {
                    int viewCase = (i / 2) % 5;
                    Assert.That(values[i].w, Is.EqualTo(1), $"Output {i} was not written.");
                    for (int channel = 0; channel < 3; ++channel)
                    {
                        float value = values[i][channel];
                        string label = $"shape {i / 10}, view {viewCase}, lobe {i % 2}, channel {channel}";
                        Assert.That(float.IsNaN(value) || float.IsInfinity(value), Is.False, label);
                        if (viewCase < 2)
                            Assert.That(value, Is.GreaterThan(0), label);
                        else
                            Assert.That(value, Is.Zero, label);
                    }
                }
            }

            internal void Run(bool directLights, float ssrWeight, bool lutReady, bool mixedTile,
                int environment = 0, bool ssrEnabled = true, int areaCase = 0,
                int dualMode = 0, float layerWeight = 0.4f, bool verifyRaster = false, int overrideClass = 0)
            {
                ComputeShader production = Load("Shaders/Material/DeferredLit.compute");
                m_Production = production;
                ComputeShader control = Load("Tests/Editor/SubSystem/GPUDriven/SimpleSlabDeferredLightingTests.compute");
                ComputeShader classifier = Load("Shaders/Material/MaterialClassification.compute");
                Assert.That(m_Lut.Create(Load("Shaders/Core/Private/VividSlabLut.compute")), Is.True);
                int prepare = control.FindKernel("PrepareInputs");
                int reference = control.FindKernel("ReferenceLighting");
                int clear = production.FindKernel("ClearDeferredLit");
                int variant = mixedTile ? 3 : (dualMode > 0 && layerWeight > 0 ? 2 : 0);
                if (overrideClass != 0)
                    variant = Mathf.Max(variant, overrideClass == 3 ? 1 : 3);
                int shade = production.FindKernel("DeferredLit_Variant" + variant);
                var gbuffers = new RenderTexture[5];
                for (int i = 0; i < gbuffers.Length; ++i)
                {
                    gbuffers[i] = Target(GraphicsFormat.R32G32B32A32_SFloat);
                    Texture(control, prepare, "_TestGBuffer" + i, gbuffers[i]);
                    string name = i == 4 ? "_DiffuseIrradiance" : "_GBuffer" + i;
                    Texture(production, clear, name, gbuffers[i]);
                    Texture(production, shade, name, gbuffers[i]);
                }
                RenderTexture depth = Target(GraphicsFormat.R32_SFloat);
                RenderTexture lighting = Target(GraphicsFormat.R32G32B32A32_SFloat);
                RenderTexture debug = Target(GraphicsFormat.R32G32B32A32_SFloat);
                Texture(control, prepare, "_TestDepth", depth);
                RenderTexture layerAux0 = Target(GraphicsFormat.R32G32B32A32_SFloat);
                RenderTexture layerAux1 = Target(GraphicsFormat.R32G32B32A32_SFloat);
                Texture(control, prepare, "_TestLayerAux0", layerAux0);
                Texture(control, prepare, "_TestLayerAux1", layerAux1);
                var sky = Track(new Cubemap(2, TextureFormat.RGBAFloat, true) { filterMode = FilterMode.Trilinear });
                for (int face = 0; face < 6; ++face)
                {
                    sky.SetPixels(Pixels(environment == 6 ? Color.white : new Color(0.2f, 0.4f, 0.6f, 1), 4), (CubemapFace)face, 0);
                    sky.SetPixels(Pixels(environment == 6 ? Color.white : new Color(0.7f, 0.15f, 0.05f, 1), 1), (CubemapFace)face, 1);
                }
                sky.Apply(false, false);
                Texture2D shadow = Solid(new Color(0.25f, 0, 0, 0));
                // These inputs use pixel-coordinate Load(), not normalized-UV sampling.
                Texture2D ao = Solid(new Color(0.5f, 0, 0, 0), 8);
                Texture2D ssr = Solid(new Color(0.8f, 0.1f, 0.3f, ssrWeight), 8);
                var ltc = Track(new Texture2DArray(1, 1, 3, TextureFormat.RGBAFloat, false, true));
                for (int slice = 0; slice < 3; ++slice)
                    ltc.SetPixels(new[] { new Color(2, 0, 1.3f, 0) }, slice);
                ltc.Apply(false, false);
                var atlas = Track(new Texture2DArray(2, 2, 2, TextureFormat.RGBAFloat, true, true)
                { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp });
                atlas.SetPixels(Pixels(new Color(0.8f, 0.2f, 0.1f, 1), 4), 0, 0);
                atlas.SetPixels(Pixels(new Color(0.1f, 0.6f, 0.3f, 1), 1), 0, 1);
                atlas.SetPixels(Pixels(new Color(0.1f, 0.2f, 0.9f, 1), 4), 1, 0);
                atlas.SetPixels(Pixels(new Color(0.6f, 0.15f, 0.8f, 1), 1), 1, 1);
                atlas.Apply(false, false);

                ComputeBuffer directional = Buffer(new[] { new VividLightData.DirectionalLightData
                {
                    directionWS = new Vector3(0, 0, 1), shadowStrength = 1,
                    color = new Vector3(2, 0.5f, 1)
                } });
                ComputeBuffer punctual = Buffer(new[] { new VividLightData.PunctualLightData
                {
                    positionWS = new Vector3(0, 0, 3), color = new Vector3(0.2f, 0.7f, 0.4f),
                    angleOffset = 1, rangeAttenuationBias = 1
                } });
                ComputeBuffer area = Buffer(new[] { new VividLightData.AreaLightData
                {
                    positionWS = new Vector3(0.3f, 0.2f, 2), color = new Vector3(0.7f, 0.3f, 0.2f),
                    lightType = areaCase == 1 ? 0u : 1u,
                    // A proper rotated frame: cross(right, up) == forward.
                    forwardWS = areaCase == 2 ? Vector3.forward : Vector3.back,
                    rightWS = areaCase == 2 ? Vector3.right : Vector3.left,
                    upWS = Vector3.up, width = 1, height = 0.8f,
                    rangeAttenuationBias = areaCase == 3 ? 0 : 1,
                    cosBarnDoorAngle = areaCase == 4 ? 0.9f : 0,
                    barnDoorLength = areaCase == 4 ? 0.5f : 0
                } });
                var probeData = new VividLightData.ReflectionProbeData[2];
                for (int i = 0; i < probeData.Length; ++i)
                {
                    probeData[i] = new VividLightData.ReflectionProbeData
                    {
                        extents = Vector3.one * 10, rightWS = Vector3.right, upWS = Vector3.up,
                        forwardWS = Vector3.forward, weight = environment == 3 ? 0.75f : (i == 0 ? 0.25f : 0.5f),
                        multiplier = i == 0 ? 1.4f : 0.7f,
                        atlasScaleOffset = new Vector4(1, 1, 0, 0),
                        atlasIndexAndSlice = new Vector4(environment == 4 && i == 0 ? -1 : i, i, 0, 0),
                        boxSideFadePositive = environment == 5 && i == 0 ? Vector3.zero : Vector3.one,
                        boxSideFadeNegative = environment == 5 && i == 0 ? Vector3.right : Vector3.one
                    };
                }
                ComputeBuffer probes = Buffer(probeData);
                ComputeBuffer indices = Buffer(new uint[1]);
                ComputeBuffer flags = Buffer(new uint[1]);
                ComputeBuffer tiles = Buffer(new uint[4]);
                ComputeBuffer args = Buffer(new uint[16], ComputeBufferType.Structured | ComputeBufferType.IndirectArguments);
                ComputeBuffer scalars = Buffer(new float[1]);
                ComputeBuffer ambient = Buffer(new Vector4[7]);
                ComputeBuffer exposure = Buffer(new[] { new Vector4(2.5f, 0, 0, 0) });
                ComputeBuffer expected = Buffer(new Vector4[64]);

                foreach (ComputeShader shader in new[] { production, control })
                {
                    Vector(shader, "_VividScreenSize", new Vector4(8, 8, 0.125f, 0.125f));
                    Matrix(shader, "_VividGlstateMatrixProjection", Matrix4x4.identity);
                    Matrix4x4 view = Matrix4x4.identity;
                    if (environment == 5)
                    {
                        view.SetRow(0, new Vector4(0.6f, 0, -0.8f, 0));
                        view.SetRow(2, new Vector4(0.8f, 0, 0.6f, 0));
                    }
                    Matrix(shader, "_VividMatrixV", view);
                    Matrix(shader, "_VividMatrixInvVP", Matrix4x4.identity);
                    Matrix(shader, "_PixelCoordToViewDirWS", Matrix4x4.identity);
                    Vector(shader, "_SkyTextureTint", Vector4.one);
                    Vector(shader, "_SkyTextureParams", new Vector4(1, 0, environment == 0 ? 0 : 1, 1));
                    Vector(shader, "_ReflectionAtlasCubeData", Vector4.zero);
                    Int(shader, "_DirectionalLightCount", directLights ? 1 : 0);
                    Int(shader, "_PunctualLightCount", directLights ? 1 : 0);
                    Int(shader, "_AreaLightCount", directLights ? 1 : 0);
                    Int(shader, "_ReflectionProbeCount", environment >= 2 && environment <= 5 ? 2 : 0);
                    Int(shader, "_ReflectionAtlasMipCount", 2);
                    Int(shader, "_ReflectionAtlasSliceCount", 2);
                    Int(shader, "_EnableProbeVolumes", 0);
                    Int(shader, "_MainDirectionalLightIndex", 0);
                    Int(shader, "_ClusteredPunctualLightGridEnabled", 0);
                    Int(shader, "_ClusteredAreaLightGridEnabled", 0);
                    Int(shader, "_ClusteredReflectionProbeGridEnabled", 0);
                    Int(shader, "_ClusteredDecalGridEnabled", 0);
                    Int(shader, "_ScreenSpaceReflectionEnabled", ssrEnabled ? 1 : 0);
                    Int(shader, "_MaterialTileCountX", 1);
                    Int(shader, "_MaterialFeatureTileListOffset", variant);
                    Int(shader, "_VividSlabLutReady", lutReady ? 1 : 0);
                }
                foreach (int kernel in new[] { clear, shade })
                {
                    Texture(production, kernel, "_DepthTexture", depth);
                    Texture(production, kernel, "_LightingTexture", lighting);
                    Texture(production, kernel, "_LightingDebugTexture", debug);
                    Texture(production, kernel, "_SkyTexture", sky);
                    Bind(production, kernel, "_VividAutoExposurePreExposureBuffer", exposure);
                }
                foreach (var pair in new[] { (shader: production, kernel: shade), (shader: control, kernel: reference) })
                {
                    Texture(pair.shader, pair.kernel, "_VividSlabLut", m_Lut.Texture.rt);
                    Texture(pair.shader, pair.kernel, "_LtcData", ltc);
                    Bind(pair.shader, pair.kernel, "_DirectionalLights", directional);
                    Bind(pair.shader, pair.kernel, "_PunctualLights", punctual);
                    Bind(pair.shader, pair.kernel, "_AreaLights", area);
                }
                Texture(production, shade, "_LayerAux0", layerAux0);
                Texture(production, shade, "_LayerAux1", layerAux1);
                Texture(production, shade, "_GTAOTexture", ao);
                Texture(production, shade, "_DirectionalShadowTexture", shadow);
                Texture(production, shade, "_ScreenSpaceReflectionTexture", ssr);
                Texture(production, shade, "_ReflectionAtlas", atlas);
                Bind(production, shade, "_ReflectionProbes", probes);
                Bind(production, shade, "_VividAmbientProbeData", ambient);
                Bind(production, shade, "g_LayeredOffset", indices);
                Bind(production, shade, "g_vLayeredLightList", indices);
                Bind(production, shade, "g_logBaseBuffer", scalars);
                Bind(production, shade, "_MaterialTileFeatureFlags", flags);
                Bind(production, shade, "_MaterialFeatureTileList", tiles);
                Bind(control, reference, "_Expected", expected);
                m_Cmd.SetComputeFloatParam(control, "_TestSSRWeight", ssrWeight);
                m_Cmd.SetComputeFloatParam(control, "_TestExposure", 2.5f);
                Int(control, "_TestLutReady", lutReady ? 1 : 0);
                Int(control, "_TestMixedTile", mixedTile ? 1 : 0);
                Int(control, "_TestEnvironment", environment);
                Int(control, "_TestSSREnabled", ssrEnabled ? 1 : 0);
                Int(control, "_TestDualMode", dualMode);
                Int(control, "_TestOverrideClass", overrideClass);
                m_Cmd.SetComputeFloatParam(control, "_TestLayerWeight", layerWeight);
                m_Cmd.DispatchCompute(control, prepare, 1, 1, 1);
                Int(classifier, "_ClassificationWidth", 8);
                Int(classifier, "_ClassificationHeight", 8);
                Int(classifier, "_MaterialTileCount", 1);
                Int(classifier, "_MaterialTileCountX", 1);
                foreach (string entry in new[] { "ClearDeferredVariantArgs", "ClassifyDeferredExports", "BuildDeferredVariantIndirectArgs" })
                {
                    int kernel = classifier.FindKernel(entry);
                    Texture(classifier, kernel, "_GBuffer0", gbuffers[0]);
                    Texture(classifier, kernel, "_GBuffer1", gbuffers[1]);
                    Texture(classifier, kernel, "_DepthTexture", depth);
                    Bind(classifier, kernel, "_MaterialTileFeatureFlags", flags);
                    Bind(classifier, kernel, "_MaterialFeatureTileList", tiles);
                    Bind(classifier, kernel, "_MaterialFeatureIndirectArgs", args);
                    m_Cmd.DispatchCompute(classifier, kernel, 1, 1, 1);
                }
                m_Cmd.DispatchCompute(control, reference, 1, 1, 1);
                m_Cmd.DispatchCompute(production, clear, 1, 1, 1);
                m_Cmd.DispatchCompute(production, shade, args, (uint)(variant * 4 * sizeof(uint)));
                Graphics.ExecuteCommandBuffer(m_Cmd);
                var referencePixels = new Vector4[64];
                expected.GetData(referencePixels);
                var dispatchArgs = new uint[16];
                args.GetData(dispatchArgs);
                for (int i = 0; i < 4; ++i)
                    Assert.That(dispatchArgs[i * 4], Is.EqualTo(i == variant ? 1u : 0u));
                AssertPixels(lighting, referencePixels, environment == 6, dualMode == 2);

                // Neither Fast nor Dual requires any legacy FGD binding. Repeat
                // the dispatch to check that accumulation is cleared each time.
                Graphics.ExecuteCommandBuffer(m_Cmd);
                AssertPixels(lighting, referencePixels, environment == 6, dualMode == 2);

                if (verifyRaster)
                    AssertRasterPixels(referencePixels);
            }

            private void AssertRasterPixels(Vector4[] referencePixels)
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(VividPackagePathUtility.GetPreferredAssetPath(
                    "Tests/Editor/SubSystem/GPUDriven/SimpleSlabRasterDeferredTests.shader"));
                Assert.That(shader, Is.Not.Null);
                foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader))
                    Assert.That(message.severity.ToString(), Is.Not.EqualTo("Error"), message.message);
                Material material = Track(new Material(shader));
                material.DisableKeyword("PROBE_VOLUMES_L1");
                material.DisableKeyword("PROBE_VOLUMES_L2");
                RenderTexture target = Target(GraphicsFormat.R32G32B32A32_SFloat);
                // Raster entrypoints explicitly leave sky/background to their caller.
                for (int i = 7; i < referencePixels.Length; i += 8)
                    referencePixels[i] = new Vector4(0, 0, 0, 1);
                var pixelIndices = new uint[64];
                for (uint i = 0; i < pixelIndices.Length; ++i)
                    pixelIndices[i] = 63u - i;
                m_RasterProperties.SetBuffer("_MaterialPixelIndices", Buffer(pixelIndices));
                m_RasterProperties.SetInt("_LightingWidth", 8);
                m_RasterProperties.SetInt("_LightingHeight", 8);
                // Isolate camera globals from an Editor render's globally-bound
                // constant buffer; do not mutate the interactive frame's state.
                var globals = new ShaderVariablesGlobal
                {
                    _VividScreenSize = m_RasterProperties.GetVector("_VividScreenSize"),
                    _VividGlstateMatrixProjection = m_RasterProperties.GetMatrix("_VividGlstateMatrixProjection"),
                    _VividMatrixV = m_RasterProperties.GetMatrix("_VividMatrixV"),
                    _VividMatrixInvVP = m_RasterProperties.GetMatrix("_VividMatrixInvVP")
                };
                m_RasterProperties.SetConstantBuffer(ShaderVariablesGlobal.ConstantBufferShaderId,
                    Buffer(new[] { globals }, ComputeBufferType.Constant), 0, Marshal.SizeOf<ShaderVariablesGlobal>());

                for (int pass = 0; pass < 2; ++pass)
                {
                    m_Cmd.Clear();
                    m_Cmd.SetRenderTarget(target);
                    m_Cmd.SetViewport(new Rect(0, 0, 8, 8));
                    m_Cmd.ClearRenderTarget(false, true, Color.clear);
                    m_Cmd.DrawProcedural(Matrix4x4.identity, material, pass,
                        pass == 0 ? MeshTopology.Triangles : MeshTopology.Points,
                        pass == 0 ? 3 : 1, pass == 0 ? 1 : 64, m_RasterProperties);
                    Graphics.ExecuteCommandBuffer(m_Cmd);
                    AssertPixels(target, referencePixels, false, false);
                    Graphics.ExecuteCommandBuffer(m_Cmd);
                    AssertPixels(target, referencePixels, false, false);
                }
            }

            private static void AssertPixels(RenderTexture target, Vector4[] expected, bool whiteEnvironment, bool verticalLayer)
            {
                AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBAFloat);
                request.WaitForCompletion();
                Assert.That(request.hasError, Is.False);
                var actual = request.GetData<Vector4>();
                for (int i = 0; i < expected.Length; ++i)
                    for (int c = 0; c < 4; ++c)
                    {
                        Assert.That(actual[i][c], Is.EqualTo(expected[i][c]).Within(0.003f), $"pixel {i}, channel {c}");
                        if (whiteEnvironment && c < 3 && i % 8 != 7)
                            Assert.That(actual[i][c], Is.InRange(0.0f, 1.253f), "unit environment times AO 0.5 and exposure 2.5");
                        // White base and top opacity == green diffuse albedo:
                        // vertical composition preserves unit green energy.
                        if (whiteEnvironment && verticalLayer && i % 8 == 0 && c == 1)
                            Assert.That(actual[i][c], Is.EqualTo(1.25f).Within(0.003f));
                    }
            }

            private T Track<T>(T value) where T : Object { m_Objects.Add(value); return value; }
            private ComputeShader Load(string path)
            {
                ComputeShader asset = AssetDatabase.LoadAssetAtPath<ComputeShader>(VividPackagePathUtility.GetPreferredAssetPath(path));
                Assert.That(asset, Is.Not.Null, path);
                foreach (ShaderMessage message in ShaderUtil.GetComputeShaderMessages(asset))
                    Assert.That(message.severity.ToString(), Is.Not.EqualTo("Error"), message.message);
                ComputeShader shader = Track(Object.Instantiate(asset));
                shader.DisableKeyword("PROBE_VOLUMES_L1");
                shader.DisableKeyword("PROBE_VOLUMES_L2");
                return shader;
            }
            private RenderTexture Target(GraphicsFormat format)
            {
                var texture = Track(new RenderTexture(new RenderTextureDescriptor(8, 8)
                { graphicsFormat = format, depthBufferBits = 0, msaaSamples = 1, enableRandomWrite = true }));
                texture.Create();
                return texture;
            }
            private Texture2D Solid(Color color, int size = 1)
            {
                var texture = Track(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
                texture.SetPixels(Pixels(color, size * size));
                texture.Apply();
                return texture;
            }
            private static Color[] Pixels(Color color, int count)
            {
                var pixels = new Color[count];
                for (int i = 0; i < count; ++i) pixels[i] = color;
                return pixels;
            }
            private ComputeBuffer Buffer<T>(T[] data, ComputeBufferType type = ComputeBufferType.Default) where T : struct
            {
                var buffer = new ComputeBuffer(data.Length, Marshal.SizeOf<T>(), type);
                m_Buffers.Add(buffer);
                buffer.SetData(data);
                return buffer;
            }
            private void Texture(ComputeShader shader, int kernel, string name, Texture texture)
            {
                m_Cmd.SetComputeTextureParam(shader, kernel, Shader.PropertyToID(name), texture);
                if (shader == m_Production) m_RasterProperties.SetTexture(name, texture);
            }
            private void Bind(ComputeShader shader, int kernel, string name, ComputeBuffer buffer)
            {
                m_Cmd.SetComputeBufferParam(shader, kernel, Shader.PropertyToID(name), buffer);
                if (shader == m_Production) m_RasterProperties.SetBuffer(name, buffer);
            }
            private void Int(ComputeShader shader, string name, int value)
            {
                m_Cmd.SetComputeIntParam(shader, name, value);
                if (shader == m_Production) m_RasterProperties.SetInt(name, value);
            }
            private void Vector(ComputeShader shader, string name, Vector4 value)
            {
                m_Cmd.SetComputeVectorParam(shader, name, value);
                if (shader == m_Production) m_RasterProperties.SetVector(name, value);
            }
            private void Matrix(ComputeShader shader, string name, Matrix4x4 value)
            {
                m_Cmd.SetComputeMatrixParam(shader, name, value);
                if (shader == m_Production) m_RasterProperties.SetMatrix(name, value);
            }
            public void Dispose()
            {
                m_Cmd.Dispose();
                m_Lut.Dispose();
                foreach (ComputeBuffer buffer in m_Buffers) buffer.Dispose();
                for (int i = m_Objects.Count - 1; i >= 0; --i) Object.DestroyImmediate(m_Objects[i]);
            }
        }

        private static string Read(string relative) => File.ReadAllText(VividPackagePathUtility.GetPreferredAssetPath(relative));
    }
}

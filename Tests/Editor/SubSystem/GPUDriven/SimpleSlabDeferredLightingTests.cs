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
        public void FastSlab_OwnsItsLightingDataAndComposition()
        {
            string source = Read("Shaders/Material/DeferredLit.compute");
            int start = source.IndexOf("VividSimpleSlabDeferredLighting EvaluateDeferredFastSlabLighting(", StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            int end = source.IndexOf("VividDirectLighting ScaleVividDeferredDirectLighting(", start, StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThan(start));
            string fastPath = source.Substring(start, end - start);
            foreach (string legacy in new[] { "VividGBufferSurfaceData", "VividLitBSDFData", "GetVividPreLightData",
                         "ApplyVividSlabEnergyToLegacyPreLight", "PostEvaluateBSDF", "EvaluateBSDF_Env", "EvaluateBSDF_Area" })
                StringAssert.DoesNotContain(legacy, fastPath);
            StringAssert.Contains("VividComposeSimpleSlabDeferredLighting", fastPath);
            StringAssert.Contains("lightLoopOutput = EvaluateDeferredFastSlabLighting(", source);
            StringAssert.Contains("legacyDualOutput = EvaluateDeferredDualSlabLighting(", source);
            string header = Read("Shaders/Core/Public/VividSimpleSlabDeferredLighting.hlsl");
            StringAssert.Contains($"#define VIVID_SIMPLE_SLAB_DEFERRED_LIGHTING_VERSION {MaterialProgramContract.SimpleSlabDeferredLightingVersion}u", header);
            foreach (string legacy in new[] { "HdrpLitLighting.hlsl", "PreIntegratedFGD.hlsl", "VividLitBSDFData", "PostEvaluateBSDF" })
                StringAssert.DoesNotContain(legacy, header);
            StringAssert.Contains("result.screenSpaceReflectionFGD = energy.singleScatterSpecularAlbedo;", header);
            StringAssert.Contains("result.screenSpaceReplaceableSpecularLighting = indirectLighting.singleScatterSpecular;", header);
            StringAssert.Contains("preExposedReplaceableSpecular * reflectionWeight", source);
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

        private sealed class PixelFixture : IDisposable
        {
            private readonly List<Object> m_Objects = new();
            private readonly List<ComputeBuffer> m_Buffers = new();
            private readonly VividSlabLut m_Lut = new();
            private readonly CommandBuffer m_Cmd = new();

            internal void Run(bool directLights, float ssrWeight, bool lutReady, bool mixedTile,
                int environment = 0, bool ssrEnabled = true, int areaCase = 0)
            {
                ComputeShader production = Load("Shaders/Material/DeferredLit.compute");
                ComputeShader control = Load("Tests/Editor/SubSystem/GPUDriven/SimpleSlabDeferredLightingTests.compute");
                ComputeShader classifier = Load("Shaders/Material/MaterialClassification.compute");
                Assert.That(m_Lut.Create(Load("Shaders/Core/Private/VividSlabLut.compute")), Is.True);
                int prepare = control.FindKernel("PrepareInputs");
                int reference = control.FindKernel("ReferenceLighting");
                int clear = production.FindKernel("ClearDeferredLit");
                int variant = mixedTile ? 3 : 0;
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
                var sky = Track(new Cubemap(2, TextureFormat.RGBAFloat, true) { filterMode = FilterMode.Trilinear });
                for (int face = 0; face < 6; ++face)
                {
                    sky.SetPixels(Pixels(new Color(0.2f, 0.4f, 0.6f, 1), 4), (CubemapFace)face, 0);
                    sky.SetPixels(Pixels(new Color(0.7f, 0.15f, 0.05f, 1), 1), (CubemapFace)face, 1);
                }
                sky.Apply(false, false);
                Texture2D legacyFgd = Solid(new Color(0.93f, 0.17f, 0.66f, 0.5f));
                Texture2D black = Solid(Color.clear);
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
                    m_Cmd.SetComputeVectorParam(shader, "_VividScreenSize", new Vector4(8, 8, 0.125f, 0.125f));
                    m_Cmd.SetComputeMatrixParam(shader, "_VividGlstateMatrixProjection", Matrix4x4.identity);
                    Matrix4x4 view = Matrix4x4.identity;
                    if (environment == 5)
                    {
                        view.SetRow(0, new Vector4(0.6f, 0, -0.8f, 0));
                        view.SetRow(2, new Vector4(0.8f, 0, 0.6f, 0));
                    }
                    m_Cmd.SetComputeMatrixParam(shader, "_VividMatrixV", view);
                    m_Cmd.SetComputeMatrixParam(shader, "_VividMatrixInvVP", Matrix4x4.identity);
                    m_Cmd.SetComputeMatrixParam(shader, "_PixelCoordToViewDirWS", Matrix4x4.identity);
                    m_Cmd.SetComputeVectorParam(shader, "_SkyTextureTint", Vector4.one);
                    m_Cmd.SetComputeVectorParam(shader, "_SkyTextureParams", new Vector4(1, 0, environment == 0 ? 0 : 1, 1));
                    m_Cmd.SetComputeVectorParam(shader, "_ReflectionAtlasCubeData", Vector4.zero);
                    Int(shader, "_DirectionalLightCount", directLights ? 1 : 0);
                    Int(shader, "_PunctualLightCount", directLights ? 1 : 0);
                    Int(shader, "_AreaLightCount", directLights ? 1 : 0);
                    Int(shader, "_ReflectionProbeCount", environment >= 2 ? 2 : 0);
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
                Texture(production, shade, "_PreIntegratedFGD_GGXDisneyDiffuse", legacyFgd);
                Texture(production, shade, "_PreIntegratedFGD_CharlieAndFabric", black);
                Texture(production, shade, "_LayerAux0", black);
                Texture(production, shade, "_LayerAux1", black);
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
                AssertPixels(lighting, referencePixels);

                // FastSlab must be independent of BOTH legacy FGD contents and repeated dispatch state.
                legacyFgd.SetPixel(0, 0, Color.white);
                legacyFgd.Apply();
                Graphics.ExecuteCommandBuffer(m_Cmd);
                AssertPixels(lighting, referencePixels);
            }

            private static void AssertPixels(RenderTexture target, Vector4[] expected)
            {
                AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBAFloat);
                request.WaitForCompletion();
                Assert.That(request.hasError, Is.False);
                var actual = request.GetData<Vector4>();
                for (int i = 0; i < expected.Length; ++i)
                    for (int c = 0; c < 3; ++c)
                        Assert.That(actual[i][c], Is.EqualTo(expected[i][c]).Within(0.003f), $"pixel {i}, channel {c}");
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
            private void Texture(ComputeShader shader, int kernel, string name, Texture texture) =>
                m_Cmd.SetComputeTextureParam(shader, kernel, Shader.PropertyToID(name), texture);
            private void Bind(ComputeShader shader, int kernel, string name, ComputeBuffer buffer) =>
                m_Cmd.SetComputeBufferParam(shader, kernel, Shader.PropertyToID(name), buffer);
            private void Int(ComputeShader shader, string name, int value) => m_Cmd.SetComputeIntParam(shader, name, value);
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

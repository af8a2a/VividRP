using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using ResourcePathAttribute = VividRP.Runtime.ResourcePathAttribute;

namespace VividRP.Editor.Tests
{
    public class AtmosphereLUTPassTests
    {
        [Test]
        public void Initialize_RegistersLutsAndHiddenSkyViewHistoryResources()
        {
            IRenderPass renderPass = new AtmosphereLUTPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();
            var bufferEntries = resources.Buffers.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "MultiScatteringLUT",
                "SkyViewLUT",
                "SkyViewHistoryLayersCurrent",
                "SkyViewHistoryLayersPrevious",
                "TransmittanceLUT"
            }));
            Assert.That(textureEntries.Single(entry => entry.Name == "TransmittanceLUT").Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(textureEntries.Single(entry => entry.Name == "MultiScatteringLUT").Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(textureEntries.Single(entry => entry.Name == "SkyViewLUT").Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(textureEntries.Single(entry => entry.Name == "SkyViewHistoryLayersPrevious").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(textureEntries.Single(entry => entry.Name == "SkyViewHistoryLayersCurrent").Access, Is.EqualTo(AccessFlags.Write));

            Assert.That(bufferEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "SkyViewHistoryMetaCurrent",
                "SkyViewHistoryMetaPrevious",
                "SkyViewHistorySelection"
            }));
            Assert.That(bufferEntries.Single(entry => entry.Name == "SkyViewHistoryMetaPrevious").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(bufferEntries.Single(entry => entry.Name == "SkyViewHistoryMetaCurrent").Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(bufferEntries.Single(entry => entry.Name == "SkyViewHistorySelection").Access, Is.EqualTo(AccessFlags.ReadWrite));
        }

        [Test]
        public void AtmosphereLUTPass_InheritsFromComputePass()
        {
            Assert.That(typeof(ComputePass).IsAssignableFrom(typeof(AtmosphereLUTPass)), Is.True);
        }

        [Test]
        public void Prepare_ConfiguresFixedLutDimensionsAndRandomWrite()
        {
            var pass = new AtmosphereLUTPass();

            pass.Prepare(new ContextContainer());

            AssertTexture(pass, "m_TransmittanceLUT", AtmosphereLUTPass.TransmittanceWidth, AtmosphereLUTPass.TransmittanceHeight);
            AssertTexture(pass, "m_MultiScatteringLUT", AtmosphereLUTPass.MultiScatteringWidth, AtmosphereLUTPass.MultiScatteringHeight);
            AssertTexture(pass, "m_SkyViewLUT", AtmosphereLUTPass.SkyViewWidth, AtmosphereLUTPass.SkyViewHeight);
            AssertArrayTexture(pass, "m_SkyViewHistoryLayersPrevious", AtmosphereLUTPass.SkyViewWidth, AtmosphereLUTPass.SkyViewHeight, AtmosphereLUTPass.SkyViewHistoryLayerCount);
            AssertArrayTexture(pass, "m_SkyViewHistoryLayersCurrent", AtmosphereLUTPass.SkyViewWidth, AtmosphereLUTPass.SkyViewHeight, AtmosphereLUTPass.SkyViewHistoryLayerCount);
            AssertStructuredBuffer(pass, "m_SkyViewHistoryMetaPrevious", AtmosphereLUTPass.SkyViewHistoryLayerCount);
            AssertStructuredBuffer(pass, "m_SkyViewHistoryMetaCurrent", AtmosphereLUTPass.SkyViewHistoryLayerCount);
            AssertStructuredBuffer(pass, "m_SkyViewHistorySelection", 1);
        }

        [Test]
        public void VividRPCoreResources_DeclaresAtmosphereLUTCompute()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.AtmosphereLUTCompute));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Shaders/Core/Private/AtmosphereLUT.compute"));
        }

        [Test]
        public void Source_UsesCommonSkyParametersAndDispatchesAllKernels()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "AtmosphereLUTPass.cs"));

            Assert.That(source, Does.Contain("public sealed class AtmosphereLUTPass : ComputePass"));
            Assert.That(source, Does.Contain("m_ComputeShader = resources?.AtmosphereLUTCompute;"));
            Assert.That(source, Does.Contain("m_TransmittanceKernel = m_ComputeShader.FindKernel(TransmittanceKernelName);"));
            Assert.That(source, Does.Contain("m_MultiScatteringKernel = m_ComputeShader.FindKernel(MultiScatteringKernelName);"));
            Assert.That(source, Does.Contain("m_SkyViewKernel = m_ComputeShader.FindKernel(SkyViewKernelName);"));
            Assert.That(source, Does.Contain("m_SkyViewSelectHistoryKernel = m_ComputeShader.FindKernel(SkyViewSelectHistoryKernelName);"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyShaderParameterBuilder.TryBuild(frameData, out m_Parameters)"));
            Assert.That(source, Does.Contain("PassRecorder.ImportTexture(m_TransmittanceLUT, m_CachedTransmittanceHandle);"));
            Assert.That(source, Does.Contain("PassRecorder.ImportTexture(m_MultiScatteringLUT, m_CachedMultiScatteringHandle);"));
            Assert.That(source, Does.Contain("PassRecorder.ImportTexture(m_SkyViewLUT, m_CachedSkyViewHandle);"));
            Assert.That(source, Does.Contain("AllocHistoryTexture("));
            Assert.That(source, Does.Contain("SkyViewHistoryTextureKey,"));
            Assert.That(source, Does.Contain("m_SkyViewHistoryLayersPrevious,"));
            Assert.That(source, Does.Contain("m_SkyViewHistoryLayersCurrent,"));
            Assert.That(source, Does.Contain("AllocHistoryBuffer("));
            Assert.That(source, Does.Contain("SkyViewHistoryMetaKey,"));
            Assert.That(source, Does.Contain("m_SkyViewHistoryMetaPrevious,"));
            Assert.That(source, Does.Contain("m_SkyViewHistoryMetaCurrent,"));
            Assert.That(source, Does.Contain("ComputeTransmittanceHash(m_Parameters)"));
            Assert.That(source, Does.Contain("ComputeMultiScatteringHash(m_Parameters, transmittanceHash)"));
            Assert.That(source, Does.Contain("ComputeSkyViewDependencyHash(multiScatteringHash)"));
            Assert.That(source, Does.Contain("ComputeSkyViewParametersHash(m_Parameters)"));
            Assert.That(source, Does.Contain("ComputeSkyViewCameraHash(m_Parameters)"));
            Assert.That(source, Does.Contain("SelectSkyViewHistoryLayer(cmd);"));
            Assert.That(source, Does.Contain("ResolveRebuildReason(m_TransmittanceCacheRecreated, m_CachedTransmittanceHash, transmittanceHash)"));
            Assert.That(source, Does.Contain("ResolveRebuildReason(m_MultiScatteringCacheRecreated, m_CachedMultiScatteringHash, multiScatteringHash)"));
            Assert.That(source, Does.Contain("ResolveSkyViewRebuildReason("));
            Assert.That(source, Does.Contain("cmd.SetComputeTextureParam(m_ComputeShader, m_TransmittanceKernel, TransmittanceLutOutputId, m_TransmittanceLUT.innerHandle);"));
            Assert.That(source, Does.Contain("cmd.SetComputeTextureParam(m_ComputeShader, m_MultiScatteringKernel, TransmittanceLutId, m_TransmittanceLUT.innerHandle);"));
            Assert.That(source, Does.Contain("cmd.SetComputeTextureParam(m_ComputeShader, m_SkyViewKernel, SkyViewLutOutputId, m_SkyViewLUT.innerHandle);"));
        }

        [Test]
        public void Source_ProfilesRebuildReasonsAndReleasesPersistentResources()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "AtmosphereLUTPass.cs"));

            Assert.That(source, Does.Contain("AtmosphereLUTPass.RebuildTransmittance (MissingTexture)"));
            Assert.That(source, Does.Contain("AtmosphereLUTPass.RebuildTransmittance (ParametersChanged)"));
            Assert.That(source, Does.Contain("AtmosphereLUTPass.RebuildMultiScattering (MissingTexture)"));
            Assert.That(source, Does.Contain("AtmosphereLUTPass.RebuildMultiScattering (ParametersChanged)"));
            Assert.That(source, Does.Contain("AtmosphereLUTPass.RebuildSkyView (MissingTexture)"));
            Assert.That(source, Does.Contain("AtmosphereLUTPass.RebuildSkyView (DependenciesChanged)"));
            Assert.That(source, Does.Contain("AtmosphereLUTPass.RebuildSkyView (CameraChanged)"));
            Assert.That(source, Does.Contain("AtmosphereLUTPass.RebuildSkyView (ParametersChanged)"));
            Assert.That(source, Does.Contain("ReleaseCachedLutResources();"));
            Assert.That(source, Does.Contain("ReleaseLutResource(ref m_CachedTransmittanceTexture, ref m_CachedTransmittanceHandle);"));
            Assert.That(source, Does.Contain("ReleaseLutResource(ref m_CachedMultiScatteringTexture, ref m_CachedMultiScatteringHandle);"));
            Assert.That(source, Does.Contain("ReleaseLutResource(ref m_CachedSkyViewTexture, ref m_CachedSkyViewHandle);"));
            Assert.That(source, Does.Contain("ConfigureSkyViewHistoryTextureDescriptor(m_SkyViewHistoryLayersPrevious, \"SkyViewHistoryLayersPrevious\");"));
            Assert.That(source, Does.Contain("ConfigureSkyViewHistoryMetaDescriptor(m_SkyViewHistoryMetaPrevious, \"SkyViewHistoryMetaPrevious\");"));
            Assert.That(source, Does.Contain("ConfigureSkyViewHistorySelectionDescriptor(m_SkyViewHistorySelection, \"SkyViewHistorySelection\");"));
        }

        [Test]
        public void Source_SplitsSkyViewHashesBetweenDependenciesParametersAndCamera()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "AtmosphereLUTPass.cs"));

            Assert.That(source, Does.Contain("private int m_CachedSkyViewDependencyHash;"));
            Assert.That(source, Does.Contain("private int m_CachedSkyViewParametersHash;"));
            Assert.That(source, Does.Contain("private int m_CachedSkyViewCameraHash;"));
            Assert.That(source, Does.Contain("return SkyLutRebuildReason.DependenciesChanged;"));
            Assert.That(source, Does.Contain("return SkyLutRebuildReason.ParametersChanged;"));
            Assert.That(source, Does.Contain("return cachedCameraHash != nextCameraHash"));
            Assert.That(source, Does.Contain("? SkyLutRebuildReason.CameraChanged"));
            Assert.That(source, Does.Contain("hash = AppendHash(hash, multiScatteringHash);"));
            Assert.That(source, Does.Contain("hash = AppendHash(hash, parameters.skyCameraPositionPS.x);"));
            Assert.That(source, Does.Contain("hash = AppendHash(hash, parameters.skyCameraPositionPS.y);"));
            Assert.That(source, Does.Contain("hash = AppendHash(hash, parameters.skyCameraPositionPS.z);"));
            Assert.That(source, Does.Contain("hash = AppendHash(hash, parameters.skySunDirection.x);"));
            Assert.That(source, Does.Contain("hash = AppendHash(hash, parameters.skySunDirection.y);"));
            Assert.That(source, Does.Contain("hash = AppendHash(hash, parameters.skySunDirection.z);"));
        }

        [Test]
        public void Source_DeclaresGpuSideSkyViewHistorySelectionPath()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "AtmosphereLUTPass.cs"));

            Assert.That(source, Does.Contain("using System.Runtime.InteropServices;"));
            Assert.That(source, Does.Contain("private const string SkyViewSelectHistoryKernelName = \"SkyViewLUTSelectHistoryLayer\";"));
            Assert.That(source, Does.Contain("private const string SkyViewStoreHistoryKernelName = \"SkyViewLUTStoreHistory\";"));
            Assert.That(source, Does.Contain("[StructLayout(LayoutKind.Sequential)]"));
            Assert.That(source, Does.Contain("private struct SkyViewHistorySelectionEntry"));
            Assert.That(source, Does.Contain("private readonly RenderGraphBuffer m_SkyViewHistorySelection = new();"));
            Assert.That(source, Does.Contain("m_SkyViewSelectHistoryKernel = m_ComputeShader.FindKernel(SkyViewSelectHistoryKernelName);"));
            Assert.That(source, Does.Contain("m_SkyViewStoreHistoryKernel = m_ComputeShader.FindKernel(SkyViewStoreHistoryKernelName);"));
            Assert.That(source, Does.Contain("m_SkyViewHistoryFrameIndex = unchecked((uint)Time.frameCount);"));
            Assert.That(source, Does.Contain("SelectSkyViewHistoryLayer(cmd);"));
            Assert.That(source, Does.Contain("StoreSkyViewHistory(cmd);"));
            Assert.That(source, Does.Contain("cmd.SetComputeBufferParam(m_ComputeShader, m_SkyViewSelectHistoryKernel, SkyViewHistoryMetaPreviousId, m_SkyViewHistoryMetaPrevious.innerHandle);"));
            Assert.That(source, Does.Contain("cmd.SetComputeBufferParam(m_ComputeShader, m_SkyViewSelectHistoryKernel, SkyViewHistorySelectionId, m_SkyViewHistorySelection.innerHandle);"));
            Assert.That(source, Does.Contain("cmd.SetComputeIntParam(m_ComputeShader, SkyViewHistoryFrameIndexId, unchecked((int)m_SkyViewHistoryFrameIndex));"));
            Assert.That(source, Does.Contain("cmd.DispatchCompute(m_ComputeShader, m_SkyViewSelectHistoryKernel, 1, 1, 1);"));
            Assert.That(source, Does.Contain("cmd.SetComputeTextureParam(m_ComputeShader, m_SkyViewKernel, SkyViewHistoryLayersPreviousId, m_SkyViewHistoryLayersPrevious.innerHandle);"));
            Assert.That(source, Does.Contain("cmd.SetComputeBufferParam(m_ComputeShader, m_SkyViewKernel, SkyViewHistoryMetaPreviousId, m_SkyViewHistoryMetaPrevious.innerHandle);"));
            Assert.That(source, Does.Contain("cmd.SetComputeBufferParam(m_ComputeShader, m_SkyViewKernel, SkyViewHistorySelectionId, m_SkyViewHistorySelection.innerHandle);"));
            Assert.That(source, Does.Contain("cmd.SetComputeIntParam(m_ComputeShader, SkyViewHistoryHasValidHistoryId, m_HasValidSkyViewHistoryLayers && m_HasValidSkyViewHistoryMeta ? 1 : 0);"));
            Assert.That(source, Does.Contain("cmd.SetComputeIntParam(m_ComputeShader, SkyViewHistoryDependencyHashId, m_SkyViewHistoryDependencyHash);"));
            Assert.That(source, Does.Contain("cmd.SetComputeIntParam(m_ComputeShader, SkyViewHistoryParameterHashId, m_SkyViewHistoryParameterHash);"));
            Assert.That(source, Does.Contain("cmd.SetComputeIntParam(m_ComputeShader, SkyViewHistoryFrameIndexId, unchecked((int)m_SkyViewHistoryFrameIndex));"));
            Assert.That(source, Does.Contain("cmd.SetComputeTextureParam(m_ComputeShader, m_SkyViewStoreHistoryKernel, SkyViewLutSourceId, m_SkyViewLUT.innerHandle);"));
            Assert.That(source, Does.Contain("cmd.SetComputeBufferParam(m_ComputeShader, m_SkyViewStoreHistoryKernel, SkyViewHistoryMetaPreviousId, m_SkyViewHistoryMetaPrevious.innerHandle);"));
            Assert.That(source, Does.Contain("cmd.SetComputeBufferParam(m_ComputeShader, m_SkyViewStoreHistoryKernel, SkyViewHistoryMetaCurrentId, m_SkyViewHistoryMetaCurrent.innerHandle);"));
            Assert.That(source, Does.Contain("cmd.SetComputeBufferParam(m_ComputeShader, m_SkyViewStoreHistoryKernel, SkyViewHistorySelectionId, m_SkyViewHistorySelection.innerHandle);"));
            Assert.That(source, Does.Not.Contain("ImportedGraphicsBuffer"));
            Assert.That(source, Does.Not.Contain("GetData("));
            Assert.That(source, Does.Not.Contain("ResolveSkyViewHistoryTargetLayer("));
            Assert.That(source, Does.Not.Contain("m_SkyViewHistoryTargetLayer"));
            Assert.That(source, Does.Contain("cmd.DispatchCompute("));
            Assert.That(source, Does.Contain("m_SkyViewStoreHistoryKernel,"));
            Assert.That(source, Does.Contain("SkyViewHistoryLayerCount);"));
        }

        [Test]
        public void Shader_Source_DeclaresRequiredKernelsAndSkyViewEvaluation()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "AtmosphereLUT.compute"));

            Assert.That(source, Does.Contain("#pragma kernel TransmittanceLUT"));
            Assert.That(source, Does.Contain("#pragma kernel MultiScatteringLUT"));
            Assert.That(source, Does.Contain("#pragma kernel SkyViewLUT"));
            Assert.That(source, Does.Contain("RWTexture2D<float4> _TransmittanceLUTOutput;"));
            Assert.That(source, Does.Contain("RWTexture2D<float4> _MultiScatteringLUTOutput;"));
            Assert.That(source, Does.Contain("RWTexture2D<float4> _SkyViewLUTOutput;"));
            Assert.That(source, Does.Contain("SampleTransmittanceLut"));
            Assert.That(source, Does.Contain("SampleMultiScatteringLut"));
            Assert.That(source, Does.Contain("float3 SanitizeSkyRadiance(float3 color)"));
            Assert.That(source, Does.Contain("float3 DecodeSkyViewDirection(float2 uv)"));
            Assert.That(source, Does.Contain("float3 cameraPosition = _SkyCameraPositionPS.xyz;"));
            Assert.That(source, Does.Contain("float3 samplePosition = cameraPosition + normalizedDirection * sampleDistance;"));
            Assert.That(source, Does.Contain("skyColor += SampleMultiScatteringLut(cameraHeight, sunAtCamera, planetRadius, atmosphereRadius) * 0.5f;"));
            Assert.That(source, Does.Contain("EvaluateSkyView"));
        }

        [Test]
        public void Shader_Source_DeclaresSkyViewHistoryStoreKernel()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "AtmosphereLUT.compute"));

            Assert.That(source, Does.Contain("#pragma kernel SkyViewLUTSelectHistoryLayer"));
            Assert.That(source, Does.Contain("#pragma kernel SkyViewLUTStoreHistory"));
            Assert.That(source, Does.Contain("Texture2D<float4> _SkyViewLUTSource;"));
            Assert.That(source, Does.Contain("Texture2DArray<float4> _SkyViewHistoryLayersPrevious;"));
            Assert.That(source, Does.Contain("RWTexture2DArray<float4> _SkyViewHistoryLayersCurrent;"));
            Assert.That(source, Does.Contain("struct SkyViewHistoryMetaEntry"));
            Assert.That(source, Does.Contain("struct SkyViewHistorySelectionEntry"));
            Assert.That(source, Does.Contain("StructuredBuffer<SkyViewHistoryMetaEntry> _SkyViewHistoryMetaPrevious;"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<SkyViewHistoryMetaEntry> _SkyViewHistoryMetaCurrent;"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<SkyViewHistorySelectionEntry> _SkyViewHistorySelection;"));
            Assert.That(source, Does.Contain("int _SkyViewHistoryFrameIndex;"));
            Assert.That(source, Does.Contain("SkyViewHistorySelectionEntry ResolveSkyViewHistorySelection()"));
            Assert.That(source, Does.Contain("float3 SafeNormalizeSkyVector(float3 value, float3 fallback)"));
            Assert.That(source, Does.Contain("void BuildSkyReprojectionFrame(float3 cameraPositionPS, float3 sunDirection, out float3 right, out float3 up, out float3 forward)"));
            Assert.That(source, Does.Contain("float ComputeSkyViewHistorySunGuardWeight(float3 directionWS)"));
            Assert.That(source, Does.Contain("float3 RotateDirectionBetweenSkyUps(float3 directionWS, float3 fromUp, float3 toUp)"));
            Assert.That(source, Does.Contain("float ResolveSkyViewLocalYAcrossHorizon(float currentLocalY, float currentHorizonCos, float historyHorizonCos)"));
            Assert.That(source, Does.Contain("float2 EncodeSkyViewUv(float3 directionWS)"));
            Assert.That(source, Does.Contain("float3 ReprojectSkyViewDirection(float3 directionWS, SkyViewHistoryMetaEntry meta)"));
            Assert.That(source, Does.Contain("float3 SampleSkyViewHistory(uint sourceLayer, float3 historyDirectionWS)"));
            Assert.That(source, Does.Contain("float ComputeSkyViewHistoryBlendWeight(float confidence)"));
            Assert.That(source, Does.Contain("float ComputeSkyViewHistoryConfidence(float3 directionWS, float3 historyDirectionWS, SkyViewHistoryMetaEntry meta)"));
            Assert.That(source, Does.Contain("if (sunGuardWeight <= 0.0f)"));
            Assert.That(source, Does.Contain("void SkyViewLUTSelectHistoryLayer(uint3 tid : SV_DispatchThreadID)"));
            Assert.That(source, Does.Contain("_SkyViewHistorySelection[0] = ResolveSkyViewHistorySelection();"));
            Assert.That(source, Does.Contain("void SkyViewLUTStoreHistory(uint3 tid : SV_DispatchThreadID)"));
            Assert.That(source, Does.Contain("SkyViewHistorySelectionEntry selection = _SkyViewHistorySelection[0];"));
            Assert.That(source, Does.Contain("bool needsCurrentEvaluation = true;"));
            Assert.That(source, Does.Contain("if (selection.hasHistoryResources != 0u && selection.hasMatchingLayer != 0u)"));
            Assert.That(source, Does.Contain("float3 historyDirectionWS = ReprojectSkyViewDirection(directionWS, historyMeta);"));
            Assert.That(source, Does.Contain("float3 historyColor = SampleSkyViewHistory(sourceLayer, historyDirectionWS);"));
            Assert.That(source, Does.Contain("float historyWeight = ComputeSkyViewHistoryBlendWeight(confidence);"));
            Assert.That(source, Does.Contain("if (confidence >= 0.85f)"));
            Assert.That(source, Does.Contain("if (needsCurrentEvaluation)"));
            Assert.That(source, Does.Contain("_SkyViewHistoryLayersCurrent[tid] = color;"));
            Assert.That(source, Does.Contain("_SkyViewHistoryMetaCurrent[layerIndex] = meta;"));
            Assert.That(source, Does.Not.Contain("int _SkyViewHistoryTargetLayer;"));
        }

        private static void AssertTexture(AtmosphereLUTPass pass, string fieldName, int expectedWidth, int expectedHeight)
        {
            var texture = GetFieldValue<RenderGraphTexture>(pass, fieldName);

            Assert.That(texture.desc.Width, Is.EqualTo(expectedWidth));
            Assert.That(texture.desc.Height, Is.EqualTo(expectedHeight));
            Assert.That(texture.desc.EnableRandomWrite, Is.True);
            Assert.That(texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        private static void AssertArrayTexture(AtmosphereLUTPass pass, string fieldName, int expectedWidth, int expectedHeight, int expectedSlices)
        {
            var texture = GetFieldValue<RenderGraphTexture>(pass, fieldName);

            Assert.That(texture.desc.Width, Is.EqualTo(expectedWidth));
            Assert.That(texture.desc.Height, Is.EqualTo(expectedHeight));
            Assert.That(texture.desc.Slices, Is.EqualTo(expectedSlices));
            Assert.That(texture.desc.Dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(texture.desc.EnableRandomWrite, Is.True);
            Assert.That(texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        private static void AssertStructuredBuffer(AtmosphereLUTPass pass, string fieldName, int expectedCount)
        {
            var buffer = GetFieldValue<RenderGraphBuffer>(pass, fieldName);

            Assert.That(buffer.desc.Count, Is.EqualTo(expectedCount));
            Assert.That(buffer.desc.Target, Is.EqualTo(GraphicsBuffer.Target.Structured));
        }

        private static T GetFieldValue<T>(AtmosphereLUTPass pass, string fieldName)
        {
            var field = typeof(AtmosphereLUTPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(pass);
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packageRoots = new[]
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }
    }
}

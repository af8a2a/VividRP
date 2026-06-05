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

namespace VividRP.Editor.Tests
{
    public class TemporalAAPassTests
    {
        [Test]
        public void Initialize_RegistersExpectedTextureResources()
        {
            IRenderPass renderPass = new TemporalAAPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "CameraDepth",
                "Color",
                "MotionVectors",
                "TAAHistoryColor",
                "TAAHistoryColorCurrent",
                "TAAOutput",
            }));
        }

        [Test]
        public void Initialize_ColorInput_IsReadOnly()
        {
            IRenderPass renderPass = new TemporalAAPass();

            var resources = renderPass.Initialize();
            var colorEntry = resources.Textures.First(e => e.Name == "Color");

            Assert.That(colorEntry.Access, Is.EqualTo(AccessFlags.Read));
        }

        [Test]
        public void Initialize_MotionVectors_IsReadOnly()
        {
            IRenderPass renderPass = new TemporalAAPass();

            var resources = renderPass.Initialize();
            var motionVectorEntry = resources.Textures.First(e => e.Name == "MotionVectors");

            Assert.That(motionVectorEntry.Access, Is.EqualTo(AccessFlags.Read));
        }

        [Test]
        public void Initialize_TAAOutput_IsWriteOnly()
        {
            IRenderPass renderPass = new TemporalAAPass();

            var resources = renderPass.Initialize();
            var outputEntry = resources.Textures.First(e => e.Name == "TAAOutput");

            Assert.That(outputEntry.Access, Is.EqualTo(AccessFlags.Write));
        }

        [Test]
        public void Initialize_RegistersNoBufferResources()
        {
            IRenderPass renderPass = new TemporalAAPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Buffers, Is.Empty);
            Assert.That(resources.RenderLists, Is.Empty);
        }

        [Test]
        public void TemporalAAPass_InheritsFromComputePass()
        {
            Assert.That(typeof(ComputePass).IsAssignableFrom(typeof(TemporalAAPass)), Is.True);
        }

        [Test]
        public void Prepare_ConfiguresOutputDimensionsFromCameraData()
        {
            var pass = new TemporalAAPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;
            frameData.GetOrCreate<VividTemporalData>();

            pass.Prepare(frameData);

            var outputTexture = GetTextureField(pass, "m_OutputTexture");
            Assert.That(outputTexture.desc.Width, Is.EqualTo(1920));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(1080));
            Assert.That(outputTexture.desc.EnableRandomWrite, Is.True);
        }

        [Test]
        public void Prepare_ConfiguresHistoryCurrentDimensionsFromCameraData()
        {
            var pass = new TemporalAAPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1280;
            cameraData.actualHeight = 720;
            frameData.GetOrCreate<VividTemporalData>();

            pass.Prepare(frameData);

            var historyCurrentTexture = GetTextureField(pass, "m_HistoryColorCurrent");
            Assert.That(historyCurrentTexture.desc.Width, Is.EqualTo(1280));
            Assert.That(historyCurrentTexture.desc.Height, Is.EqualTo(720));
            Assert.That(historyCurrentTexture.desc.EnableRandomWrite, Is.True);
        }

        [Test]
        public void CreateHistoryDescriptor_ReusesDescriptorInstance()
        {
            var pass = new TemporalAAPass();
            var createHistoryDescriptor = typeof(TemporalAAPass).GetMethod("CreateHistoryDescriptor", BindingFlags.Instance | BindingFlags.NonPublic);
            var widthField = typeof(TemporalAAPass).GetField("m_Width", BindingFlags.Instance | BindingFlags.NonPublic);
            var heightField = typeof(TemporalAAPass).GetField("m_Height", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(createHistoryDescriptor, Is.Not.Null);
            Assert.That(widthField, Is.Not.Null);
            Assert.That(heightField, Is.Not.Null);

            widthField.SetValue(pass, 640);
            heightField.SetValue(pass, 360);
            var firstDescriptor = (RenderGraphTextureDesc)createHistoryDescriptor.Invoke(pass, global::System.Array.Empty<object>());

            widthField.SetValue(pass, 1280);
            heightField.SetValue(pass, 720);
            var secondDescriptor = (RenderGraphTextureDesc)createHistoryDescriptor.Invoke(pass, global::System.Array.Empty<object>());

            Assert.That(secondDescriptor, Is.SameAs(firstDescriptor));
            Assert.That(secondDescriptor.Width, Is.EqualTo(1280));
            Assert.That(secondDescriptor.Height, Is.EqualTo(720));
            Assert.That(secondDescriptor.Name, Is.EqualTo("TAAHistoryColorCurrent"));
            Assert.That(secondDescriptor.EnableRandomWrite, Is.True);
        }

        [Test]
        public void Prepare_DoesNotAllocate_ForRepeatedDisabledTemporalAA()
        {
            var pass = new TemporalAAPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;
            frameData.GetOrCreate<VividTemporalData>();

            pass.Prepare(frameData);

            var allocatedBefore = global::System.GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 32; index++)
            {
                pass.Prepare(frameData);
            }

            var allocatedBytes = global::System.GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Assert.That(allocatedBytes, Is.Zero);
        }

        [Test]
        public void Prepare_FallsBackToPixelDimensions_WhenActualDimensionsAreZero()
        {
            var pass = new TemporalAAPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 0;
            cameraData.actualHeight = 0;
            cameraData.pixelWidth = 800;
            cameraData.pixelHeight = 600;
            frameData.GetOrCreate<VividTemporalData>();

            pass.Prepare(frameData);

            var outputTexture = GetTextureField(pass, "m_OutputTexture");
            Assert.That(outputTexture.desc.Width, Is.EqualTo(800));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(600));
        }

        [Test]
        public void Constructor_OutputFormat_IsR16G16B16A16_SFloat()
        {
            var pass = new TemporalAAPass();

            var outputTexture = GetTextureField(pass, "m_OutputTexture");
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        [Test]
        public void PipelineResourcesAsset_ReferencesTemporalAAComputeShader()
        {
            var pipelineResourcesSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "Resources", "PipelineResources.asset"));

            Assert.That(
                pipelineResourcesSource,
                Does.Contain("- ResourceName: Shaders/Core/Private/TemporalAA"));
            Assert.That(
                pipelineResourcesSource,
                Does.Contain("ResourceObject: {fileID: 7200000, guid: e30d483061855394891ca96699b7d9ba, type: 3}"));
        }

        [Test]
        public void TemporalAACompute_ReprojectsHistoryWithJitterDelta()
        {
            var computeSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "TemporalAA.compute"));

            Assert.That(computeSource, Does.Contain("float2 jitterDelta = (_Jitter.zw - _Jitter.xy) * 0.5;"));
            Assert.That(computeSource, Does.Contain("float2 historyUV = uv - motionVector + jitterDelta;"));
        }

        private static RenderGraphTexture GetTextureField(TemporalAAPass pass, string fieldName)
        {
            var field = typeof(TemporalAAPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on TemporalAAPass");
            return (RenderGraphTexture)field.GetValue(pass);
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

    public class CMAA2PassTests
    {
        [Test]
        public void Initialize_RegistersExpectedTextureResources()
        {
            IRenderPass renderPass = new CMAA2Pass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "CMAA2DeferredBlendItemListHeads",
                "CMAA2Edges",
                "CMAA2Output",
                "Color",
            }));
        }

        [Test]
        public void Initialize_RegistersExpectedBufferResources()
        {
            IRenderPass renderPass = new CMAA2Pass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Buffers.Select(entry => entry.Name).OrderBy(name => name), Is.EqualTo(new[]
            {
                "CMAA2ControlBuffer",
                "CMAA2DeferredBlendItemList",
                "CMAA2DeferredBlendLocationList",
                "CMAA2ExecuteIndirectBuffer",
                "CMAA2ShapeCandidates",
            }));
            Assert.That(resources.RenderLists, Is.Empty);
        }

        [Test]
        public void CMAA2Pass_InheritsFromComputePass()
        {
            Assert.That(typeof(ComputePass).IsAssignableFrom(typeof(CMAA2Pass)), Is.True);
        }

        [Test]
        public void CMAA2Pass_UsesStablePassResourceLayout()
        {
            Assert.That(typeof(IStablePassResourceLayout).IsAssignableFrom(typeof(CMAA2Pass)), Is.True);
        }

        [Test]
        public void Prepare_ConfiguresOutputDimensionsFromCameraData()
        {
            var pass = new CMAA2Pass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            pass.Prepare(frameData);

            var outputTexture = GetCmaa2TextureField(pass, "m_OutputTexture");
            Assert.That(outputTexture.desc.Width, Is.EqualTo(1920));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(1080));
            Assert.That(outputTexture.desc.EnableRandomWrite, Is.True);
        }

        [Test]
        public void Prepare_ConfiguresWorkingResourcesFromCameraData()
        {
            var pass = new CMAA2Pass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1280;
            cameraData.actualHeight = 720;

            pass.Prepare(frameData);

            var edgesTexture = GetCmaa2TextureField(pass, "m_CmaaEdgesTexture");
            var headsTexture = GetCmaa2TextureField(pass, "m_CmaaDeferredBlendItemListHeadsTexture");
            var candidatesBuffer = GetCmaa2BufferField(pass, "m_CmaaShapeCandidatesBuffer");
            var applyBuffer = GetCmaa2BufferField(pass, "m_CmaaDeferredBlendItemListBuffer");
            var locationBuffer = GetCmaa2BufferField(pass, "m_CmaaDeferredBlendLocationListBuffer");

            Assert.That(edgesTexture.desc.Width, Is.EqualTo(640));
            Assert.That(edgesTexture.desc.Height, Is.EqualTo(720));
            Assert.That(headsTexture.desc.Width, Is.EqualTo(640));
            Assert.That(headsTexture.desc.Height, Is.EqualTo(360));
            Assert.That(candidatesBuffer.desc.Count, Is.EqualTo(230400));
            Assert.That(applyBuffer.desc.Count, Is.EqualTo(460800));
            Assert.That(locationBuffer.desc.Count, Is.EqualTo(153600));
        }

        [Test]
        public void SetInput_MarksPassResourceLayoutDirty()
        {
            var pass = new CMAA2Pass();
            var method = typeof(CMAA2Pass).GetMethod("SetInput", BindingFlags.Instance | BindingFlags.NonPublic);
            var field = typeof(CMAA2Pass).GetField("m_ColorInput", BindingFlags.Instance | BindingFlags.NonPublic);
            var input = RenderGraphTexture.CreateInput("InjectedColor", GraphicsFormat.R16G16B16A16_SFloat);

            Assert.That(method, Is.Not.Null);
            Assert.That(field, Is.Not.Null);

            method.Invoke(pass, new object[] { input });

            Assert.That(pass.IsPassResourceLayoutDirty, Is.True);
            Assert.That(field.GetValue(pass), Is.SameAs(input));

            pass.ClearPassResourceLayoutDirty();
            Assert.That(pass.IsPassResourceLayoutDirty, Is.False);
        }

        [Test]
        public void TemporalAACompute_DeclaresCmaa2Kernels_AndIncludesCmaa2ShaderBody()
        {
            var computeSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "TemporalAA.compute"));
            var cmaaSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "CMAA2.hlsl"));

            Assert.That(computeSource, Does.Contain("#pragma kernel ComputeDispatchArgsCS"));
            Assert.That(computeSource, Does.Contain("#pragma kernel EdgesColor2x2CS"));
            Assert.That(computeSource, Does.Contain("#pragma kernel ProcessCandidatesCS"));
            Assert.That(computeSource, Does.Contain("#pragma kernel DeferredColorApply2x2CS"));
            Assert.That(computeSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Private/CMAA2.hlsl\""));
            Assert.That(cmaaSource, Does.Contain("Conservative Morphological Anti-Aliasing, version: 2.3"));
            Assert.That(cmaaSource, Does.Contain("void ComputeDispatchArgsCS"));
            Assert.That(cmaaSource, Does.Contain("void DeferredColorApply2x2CS"));
        }

        private static RenderGraphTexture GetCmaa2TextureField(CMAA2Pass pass, string fieldName)
        {
            var field = typeof(CMAA2Pass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on CMAA2Pass");
            return (RenderGraphTexture)field.GetValue(pass);
        }

        private static RenderGraphBuffer GetCmaa2BufferField(CMAA2Pass pass, string fieldName)
        {
            var field = typeof(CMAA2Pass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on CMAA2Pass");
            return (RenderGraphBuffer)field.GetValue(pass);
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

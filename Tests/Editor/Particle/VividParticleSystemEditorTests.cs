using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime.Particle;

namespace VividRP.Editor.Tests
{
    public sealed class VividParticleSystemEditorTests
    {
        private GameObject m_GameObject;
        private VividParticleSystemAsset m_Asset;
        private UnityEditor.Editor m_Editor;

        [SetUp]
        public void SetUp()
        {
            VividParticleSystemManager.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            VividParticleSystemManager.ClearForTests();

            if (m_Editor != null)
                Object.DestroyImmediate(m_Editor);

            if (m_Asset != null)
                Object.DestroyImmediate(m_Asset);

            if (m_GameObject != null)
                Object.DestroyImmediate(m_GameObject);
        }

        [Test]
        public void CreateEditor_UsesVividParticleSystemEditor_ForComponent()
        {
            VividParticleSystem system = CreateSystem();

            m_Editor = UnityEditor.Editor.CreateEditor(system);

            Assert.That(m_Editor, Is.TypeOf<VividParticleSystemEditor>());
        }

        [Test]
        public void CreateEditor_UsesVividParticleSystemAssetEditor_ForAsset()
        {
            m_Asset = ScriptableObject.CreateInstance<VividParticleSystemAsset>();

            m_Editor = UnityEditor.Editor.CreateEditor(m_Asset);

            Assert.That(m_Editor, Is.TypeOf<VividParticleSystemAssetEditor>());
        }

        [Test]
        public void MenuItems_CreateVividParticleSystemGameObject_AddsComponentAndParents()
        {
            var parent = new GameObject("Vivid Particle Menu Parent");
            GameObject created = null;
            try
            {
                created = VividParticleSystemMenuItems.CreateVividParticleSystemGameObject(parent);

                Assert.That(VividParticleSystemMenuItems.CreateVividParticleSystemMenuPath, Is.EqualTo("GameObject/Rendering/Vivid Particle System"));
                Assert.That(created, Is.Not.Null);
                Assert.That(created.name, Is.EqualTo("Vivid Particle System"));
                Assert.That(created.transform.parent, Is.EqualTo(parent.transform));
                Assert.That(created.GetComponent<VividParticleSystem>(), Is.Not.Null);
                Assert.That(Selection.activeGameObject, Is.EqualTo(created));
            }
            finally
            {
                if (created != null)
                    Object.DestroyImmediate(created);

                Object.DestroyImmediate(parent);
                VividParticleSystemManager.ClearForTests();
            }
        }

        [Test]
        public void InspectorUtility_FindsModuleRoots_ForComponentAndAsset()
        {
            VividParticleSystem system = CreateSystem();
            m_Asset = ScriptableObject.CreateInstance<VividParticleSystemAsset>();

            using var systemObject = new SerializedObject(system);
            using var assetObject = new SerializedObject(m_Asset);

            Assert.That(
                VividParticleSystemEditorUtility.TryFindModuleRoots(
                    systemObject,
                    out SerializedProperty systemMain,
                    out SerializedProperty systemEmission,
                    out SerializedProperty systemShape,
                    out SerializedProperty systemRenderer),
                Is.True);
            Assert.That(systemMain, Is.Not.Null);
            Assert.That(systemEmission, Is.Not.Null);
            Assert.That(systemShape, Is.Not.Null);
            Assert.That(systemRenderer, Is.Not.Null);
            Assert.That(VividParticleSystemEditorUtility.FindAssetProperty(systemObject), Is.Not.Null);

            Assert.That(
                VividParticleSystemEditorUtility.TryFindModuleRoots(
                    assetObject,
                    out SerializedProperty assetMain,
                    out SerializedProperty assetEmission,
                    out SerializedProperty assetShape,
                    out SerializedProperty assetRenderer),
                Is.True);
            Assert.That(assetMain, Is.Not.Null);
            Assert.That(assetEmission, Is.Not.Null);
            Assert.That(assetShape, Is.Not.Null);
            Assert.That(assetRenderer, Is.Not.Null);
        }

        [Test]
        public void InspectorUtility_ApplyAssetTemplate_CopiesModulesThroughPublicProperty()
        {
            VividParticleSystem system = CreateSystem();
            m_Asset = ScriptableObject.CreateInstance<VividParticleSystemAsset>();
            m_Asset.main.startLifetime = 12.0f;
            m_Asset.emission.rateOverTime = 4.0f;
            m_Asset.shape.shapeType = VividParticleShapeType.Sphere;
            m_Asset.shape.radius = 3.0f;
            m_Asset.rendererModule.color = Color.cyan;
            system.main.startLifetime = 1.0f;
            system.emission.rateOverTime = 0.0f;
            system.shape.shapeType = VividParticleShapeType.Point;
            system.rendererModule.color = Color.red;

            bool applied = VividParticleSystemEditorUtility.ApplyAssetTemplate(system, m_Asset, force: false);

            Assert.That(applied, Is.True);
            Assert.That(system.asset, Is.SameAs(m_Asset));
            Assert.That(system.main.startLifetime, Is.EqualTo(12.0f));
            Assert.That(system.emission.rateOverTime, Is.EqualTo(4.0f));
            Assert.That(system.shape.shapeType, Is.EqualTo(VividParticleShapeType.Sphere));
            Assert.That(system.shape.radius, Is.EqualTo(3.0f));
            Assert.That(system.rendererModule.color, Is.EqualTo(Color.cyan));
        }

        [Test]
        public void InspectorUtility_ApplyAssetTemplate_CanForceReapplySameAsset()
        {
            VividParticleSystem system = CreateSystem();
            m_Asset = ScriptableObject.CreateInstance<VividParticleSystemAsset>();
            m_Asset.main.startSize = 2.0f;
            VividParticleSystemEditorUtility.ApplyAssetTemplate(system, m_Asset, force: false);

            system.main.startSize = 8.0f;
            m_Asset.main.startSize = 3.0f;
            bool appliedWithoutForce = VividParticleSystemEditorUtility.ApplyAssetTemplate(system, m_Asset, force: false);
            bool appliedWithForce = VividParticleSystemEditorUtility.ApplyAssetTemplate(system, m_Asset, force: true);

            Assert.That(appliedWithoutForce, Is.False);
            Assert.That(appliedWithForce, Is.True);
            Assert.That(system.asset, Is.SameAs(m_Asset));
            Assert.That(system.main.startSize, Is.EqualTo(3.0f));
        }

        [Test]
        public void InspectorUtility_CopyComponentSettingsToAsset_CopiesModulesWithoutLinking()
        {
            VividParticleSystem system = CreateSystem();
            system.main.startLifetime = 9.0f;
            system.main.startColor = Color.green;
            system.emission.rateOverTime = 3.0f;
            system.emission.bursts = new[]
            {
                new VividParticleBurst(0.25f, 4),
            };
            system.shape.shapeType = VividParticleShapeType.Box;
            system.shape.boxSize = new Vector3(1.0f, 2.0f, 3.0f);
            system.rendererModule.renderMode = VividParticleRenderMode.Stretch;
            system.rendererModule.color = Color.yellow;
            system.rendererModule.stretchLengthScale = 5.0f;

            m_Asset = ScriptableObject.CreateInstance<VividParticleSystemAsset>();
            bool copied = VividParticleSystemEditorUtility.CopyComponentSettingsToAsset(system, m_Asset);

            Assert.That(copied, Is.True);
            Assert.That(m_Asset.main.startLifetime, Is.EqualTo(9.0f));
            Assert.That(m_Asset.main.startColor, Is.EqualTo(Color.green));
            Assert.That(m_Asset.emission.rateOverTime, Is.EqualTo(3.0f));
            Assert.That(m_Asset.emission.bursts, Has.Length.EqualTo(1));
            Assert.That(m_Asset.emission.bursts[0].time, Is.EqualTo(0.25f));
            Assert.That(m_Asset.emission.bursts[0].count, Is.EqualTo(4));
            Assert.That(m_Asset.shape.shapeType, Is.EqualTo(VividParticleShapeType.Box));
            Assert.That(m_Asset.shape.boxSize, Is.EqualTo(new Vector3(1.0f, 2.0f, 3.0f)));
            Assert.That(m_Asset.rendererModule.renderMode, Is.EqualTo(VividParticleRenderMode.Stretch));
            Assert.That(m_Asset.rendererModule.color, Is.EqualTo(Color.yellow));
            Assert.That(m_Asset.rendererModule.stretchLengthScale, Is.EqualTo(5.0f));

            system.emission.bursts[0] = new VividParticleBurst(0.75f, 9);
            system.rendererModule.color = Color.red;

            Assert.That(m_Asset.emission.bursts[0].time, Is.EqualTo(0.25f));
            Assert.That(m_Asset.emission.bursts[0].count, Is.EqualTo(4));
            Assert.That(m_Asset.rendererModule.color, Is.EqualTo(Color.yellow));
        }

        [Test]
        public void ShapeSceneData_ReadsAndWritesSerializedShape()
        {
            VividParticleSystem system = CreateSystem();
            system.shape.shapeType = VividParticleShapeType.Box;
            system.shape.radius = 2.0f;
            system.shape.boxSize = new Vector3(1.0f, 2.0f, 3.0f);
            system.shape.angle = 35.0f;

            using var serializedSystem = new SerializedObject(system);
            SerializedProperty shape = serializedSystem.FindProperty("m_Shape");

            Assert.That(
                VividParticleSystemEditorUtility.TryReadShapeSceneData(
                    shape,
                    out VividParticleShapeSceneData data),
                Is.True);
            Assert.That(data.Enabled, Is.True);
            Assert.That(data.ShapeType, Is.EqualTo(VividParticleShapeType.Box));
            Assert.That(data.Radius, Is.EqualTo(2.0f));
            Assert.That(data.BoxSize, Is.EqualTo(new Vector3(1.0f, 2.0f, 3.0f)));
            Assert.That(data.Angle, Is.EqualTo(35.0f));

            data.Radius = -10.0f;
            data.BoxSize = new Vector3(-1.0f, 4.0f, -5.0f);
            data.Angle = 120.0f;
            VividParticleSystemEditorUtility.WriteShapeSceneData(shape, data);
            serializedSystem.ApplyModifiedProperties();

            Assert.That(system.shape.radius, Is.EqualTo(VividParticleShapeModule.MinimumRadius));
            Assert.That(system.shape.boxSize, Is.EqualTo(new Vector3(0.0f, 4.0f, 0.0f)));
            Assert.That(system.shape.angle, Is.EqualTo(89.0f));
        }

        [Test]
        public void ShapeSceneData_CalculatesStableConePreviewLength()
        {
            float narrow = VividParticleSystemEditorUtility.GetConePreviewLength(2.0f, 10.0f);
            float wide = VividParticleSystemEditorUtility.GetConePreviewLength(2.0f, 80.0f);

            Assert.That(narrow, Is.GreaterThan(wide));
            Assert.That(narrow, Is.LessThanOrEqualTo(8.0f));
            Assert.That(wide, Is.GreaterThanOrEqualTo(0.25f));
        }

        [Test]
        public void ShapeSceneData_ConvertsConeAngleHandlePosition()
        {
            Vector3 handlePosition = VividParticleSystemEditorUtility.GetConeAngleHandlePosition(2.0f, 35.0f);
            float angle = VividParticleSystemEditorUtility.GetConeAngleFromHandlePosition(handlePosition);

            Assert.That(angle, Is.EqualTo(35.0f).Within(0.0001f));
            Assert.That(
                VividParticleSystemEditorUtility.GetConeAngleFromHandlePosition(new Vector3(1000.0f, 0.0f, 0.00001f)),
                Is.EqualTo(89.0f));
        }

        [Test]
        public void EmissionBurstUtility_FindsAndInitializesSerializedBurst()
        {
            VividParticleSystem system = CreateSystem();

            using var serializedSystem = new SerializedObject(system);

            Assert.That(
                VividParticleSystemEditorUtility.TryFindEmissionBurstsProperty(
                    serializedSystem,
                    out SerializedProperty bursts),
                Is.True);
            Assert.That(bursts, Is.Not.Null);

            bursts.arraySize = 1;
            SerializedProperty burst = bursts.GetArrayElementAtIndex(0);
            Assert.That(
                VividParticleSystemEditorUtility.TryWriteBurstElement(
                    burst,
                    time: -1.0f,
                    count: -3),
                Is.True);
            serializedSystem.ApplyModifiedProperties();

            Assert.That(system.emission.bursts, Has.Length.EqualTo(1));
            Assert.That(system.emission.bursts[0].time, Is.EqualTo(0.0f));
            Assert.That(system.emission.bursts[0].count, Is.EqualTo(0));

            serializedSystem.Update();
            Assert.That(
                VividParticleSystemEditorUtility.TryWriteBurstElement(
                    bursts.GetArrayElementAtIndex(0),
                    time: 0.25f,
                    count: 4),
                Is.True);
            serializedSystem.ApplyModifiedProperties();

            Assert.That(system.emission.bursts[0].time, Is.EqualTo(0.25f));
            Assert.That(system.emission.bursts[0].count, Is.EqualTo(4));
        }

        [Test]
        public void GpuLayoutPreview_CreatesDescriptorFromSerializedRendererModule()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.renderMode = VividParticleRenderMode.Stretch;
            system.rendererModule.colorDataMode = VividParticleGpuDataMode.Shared;
            system.rendererModule.rotationDataMode = VividParticleGpuDataMode.Shared;
            system.rendererModule.velocityDataMode = VividParticleGpuDataMode.Shared;
            system.rendererModule.sizeDataMode = VividParticleGpuDataMode.Shared;
            system.rendererModule.uvDataEnabled = true;
            system.rendererModule.customData1Enabled = true;
            system.rendererModule.meshIndexDataEnabled = true;

            using var serializedSystem = new SerializedObject(system);
            SerializedProperty renderer = serializedSystem.FindProperty("m_Renderer");

            Assert.That(
                VividParticleSystemEditorUtility.TryCreateGpuDataLayoutDescriptor(
                    renderer,
                    out VividParticleSystemManager.VividParticleGpuDataLayoutDescriptor descriptor),
                Is.True);
            Assert.That(descriptor.RenderMode, Is.EqualTo(VividParticleRenderMode.Stretch));
            Assert.That(descriptor.IncludeUV, Is.True);
            Assert.That(descriptor.IncludeCustomData1, Is.True);
            Assert.That(descriptor.IncludeCustomData2, Is.False);
            Assert.That(descriptor.IncludeMeshIndex, Is.True);

            VividParticleSystemManager.VividParticleGpuDataLayout layout =
                VividParticleSystemManager.VividParticleGpuDataLayout.Create(descriptor);
            Assert.That(
                layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.BaseColor, out var colorInfo),
                Is.True);
            Assert.That(colorInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(
                layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.Rotation, out var rotationInfo),
                Is.True);
            Assert.That(rotationInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(
                layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.VelocityStretch, out var velocityInfo),
                Is.True);
            Assert.That(velocityInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(
                layout.PerInstanceElementByteSize,
                Is.EqualTo(VividParticleSystemManager.SizeOfFloat4 * 5));
            Assert.That(
                layout.PerInstanceUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask
                    | VividParticleSystemManager.UploadColumnVelocityStretchMask
                    | VividParticleSystemManager.UploadColumnUVMask
                    | VividParticleSystemManager.UploadColumnCustomData1Mask
                    | VividParticleSystemManager.UploadColumnMeshIndexMask));
        }

        [Test]
        public void GpuLayoutPreview_ReturnsFalse_WhenRendererModuleIsMissing()
        {
            Assert.That(
                VividParticleSystemEditorUtility.TryCreateGpuDataLayoutDescriptor(
                    null,
                    out _),
                Is.False);
        }

        [Test]
        public void GpuLayoutPreview_FormatsUploadColumnMaskWithNames()
        {
            Assert.That(
                VividParticleSystemEditorUtility.FormatUploadColumnMask(0),
                Is.EqualTo("None (0x0)"));

            int columnMask = VividParticleSystemManager.UploadColumnPositionSizeMask
                | VividParticleSystemManager.UploadColumnBaseColorMask
                | VividParticleSystemManager.UploadColumnScaleMask;
            Assert.That(
                VividParticleSystemEditorUtility.FormatUploadColumnMask(columnMask),
                Is.EqualTo("PositionSize | BaseColor | Scale (0x13)"));

            Assert.That(
                VividParticleSystemEditorUtility.FormatUploadColumnMask(1 << 30),
                Is.EqualTo("Unknown(0x40000000) (0x40000000)"));
        }

        [Test]
        public void GpuLayoutPreview_FormatsPerSharpBitsWithNames()
        {
            Assert.That(
                VividParticleSystemEditorUtility.FormatGpuDataBits(0u),
                Is.EqualTo("None (0x0)"));

            uint bits = VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.SharedData)
                | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.BaseColor)
                | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.Rotation)
                | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.VelocityStretch)
                | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.Scale);
            Assert.That(
                VividParticleSystemEditorUtility.FormatGpuDataBits(bits),
                Is.EqualTo("SharedData | BaseColor | Rotation | VelocityStretch | Scale (0x79)"));

            Assert.That(
                VividParticleSystemEditorUtility.FormatGpuDataBits(1u << 31),
                Is.EqualTo("Unknown(0x80000000) (0x80000000)"));
        }

        [Test]
        public void GpuLayoutPreview_FormatsRenderJobModuleFlagsWithNames()
        {
            Assert.That(
                VividParticleSystemEditorUtility.FormatRenderJobModuleFlags(0u),
                Is.EqualTo("None (0x0)"));

            uint flags = VividParticleSystemManager.RenderJobTransformUploadFlag
                | VividParticleSystemManager.RenderJobVelocityStretchUploadFlag
                | VividParticleSystemManager.RenderJobSharedDataFlag;
            Assert.That(
                VividParticleSystemEditorUtility.FormatRenderJobModuleFlags(flags),
                Is.EqualTo("Transform | VelocityStretch | SharedData (0x15)"));

            Assert.That(
                VividParticleSystemEditorUtility.FormatRenderJobModuleFlags(1u << 31),
                Is.EqualTo("Unknown(0x80000000) (0x80000000)"));
        }

        [Test]
        public void GpuLayoutPreview_FormatsDataInfoWithCopyMask()
        {
            VividParticleSystemManager.VividParticleGpuDataLayout layout =
                VividParticleSystemManager.VividParticleGpuDataLayout.Create(VividParticleRenderMode.Billboard);

            Assert.That(
                VividParticleSystemEditorUtility.FormatGpuDataInfo(layout[0]),
                Is.EqualTo($"PerSharp / ZeroBlock / {VividParticleSystemManager.SharedDataByteSize} B / Copy None (0x0)"));
            Assert.That(
                VividParticleSystemEditorUtility.FormatGpuDataInfo(layout[2]),
                Is.EqualTo($"PerInstance / PositionSize / {VividParticleSystemManager.SizeOfFloat4} B / Copy PositionSize (0x1)"));
            Assert.That(
                VividParticleSystemEditorUtility.FormatGpuDataInfo(layout[5]),
                Is.EqualTo($"PerSharp / Rotation / {VividParticleSystemManager.SizeOfFloat4} B / Copy Rotation (0x4)"));
        }

        [Test]
        public void RendererInspectorNotices_ReturnNone_ForDefaultBillboardRenderer()
        {
            VividParticleSystem system = CreateSystem();

            using var serializedSystem = new SerializedObject(system);
            SerializedProperty renderer = serializedSystem.FindProperty("m_Renderer");

            Assert.That(
                VividParticleSystemEditorUtility.GetRendererInspectorNotices(renderer),
                Is.EqualTo(VividParticleRendererInspectorNotice.None));
        }

        [Test]
        public void RendererInspectorNotices_ReportNonRenderingModes()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.enabled = false;
            system.rendererModule.renderMode = VividParticleRenderMode.None;

            using var serializedSystem = new SerializedObject(system);
            SerializedProperty renderer = serializedSystem.FindProperty("m_Renderer");

            VividParticleRendererInspectorNotice notices =
                VividParticleSystemEditorUtility.GetRendererInspectorNotices(renderer);
            Assert.That(
                (notices & VividParticleRendererInspectorNotice.RendererDisabled) != 0,
                Is.True);
            Assert.That(
                (notices & VividParticleRendererInspectorNotice.RenderModeNone) != 0,
                Is.True);
            Assert.That(
                (notices & VividParticleRendererInspectorNotice.MeshMissing) == 0,
                Is.True);
        }

        [Test]
        public void RendererInspectorNotices_ReportMeshModeWithoutMesh()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.renderMode = VividParticleRenderMode.Mesh;
            system.rendererModule.mesh = null;

            using var serializedSystem = new SerializedObject(system);
            SerializedProperty renderer = serializedSystem.FindProperty("m_Renderer");

            VividParticleRendererInspectorNotice notices =
                VividParticleSystemEditorUtility.GetRendererInspectorNotices(renderer);
            Assert.That(
                (notices & VividParticleRendererInspectorNotice.MeshMissing) != 0,
                Is.True);
        }

        [Test]
        public void RendererInspectorNotices_UseMeshList_WhenPrimaryMeshIsMissing()
        {
            Mesh mesh = new Mesh { name = "Vivid Particle Editor Mesh List Test" };
            try
            {
                VividParticleSystem system = CreateSystem();
                system.rendererModule.renderMode = VividParticleRenderMode.Mesh;
                system.rendererModule.mesh = null;
                system.rendererModule.meshes = new[] { mesh };

                using var serializedSystem = new SerializedObject(system);
                SerializedProperty renderer = serializedSystem.FindProperty("m_Renderer");

                VividParticleRendererInspectorNotice notices =
                    VividParticleSystemEditorUtility.GetRendererInspectorNotices(renderer);
                Assert.That(
                    (notices & VividParticleRendererInspectorNotice.MeshMissing) == 0,
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void RendererInspectorNotices_ReportMultiMeshDrawCommandHint()
        {
            Mesh firstMesh = new Mesh { name = "Vivid Particle Editor Multi Mesh First" };
            Mesh secondMesh = new Mesh { name = "Vivid Particle Editor Multi Mesh Second" };
            try
            {
                VividParticleSystem system = CreateSystem();
                system.rendererModule.renderMode = VividParticleRenderMode.Mesh;
                system.rendererModule.SetMeshes(new[] { firstMesh, secondMesh });

                using var serializedSystem = new SerializedObject(system);
                SerializedProperty renderer = serializedSystem.FindProperty("m_Renderer");

                VividParticleRendererInspectorNotice notices =
                    VividParticleSystemEditorUtility.GetRendererInspectorNotices(renderer);
                Assert.That(
                    (notices & VividParticleRendererInspectorNotice.MultiMeshSplitsDrawCommands) != 0,
                    Is.True);
                Assert.That(
                    (notices & VividParticleRendererInspectorNotice.MeshMissing) == 0,
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(firstMesh);
                Object.DestroyImmediate(secondMesh);
            }
        }

        [Test]
        public void RendererInspectorNotices_ReportUploadAndSortingCostHints()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.renderMode = VividParticleRenderMode.Stretch;
            system.rendererModule.colorDataMode = VividParticleGpuDataMode.PerParticle;
            system.rendererModule.customData1Enabled = true;
            system.rendererModule.meshIndexDataEnabled = true;
            system.rendererModule.sortMode = VividParticleSortMode.ByDistance;

            using var serializedSystem = new SerializedObject(system);
            SerializedProperty renderer = serializedSystem.FindProperty("m_Renderer");

            VividParticleRendererInspectorNotice notices =
                VividParticleSystemEditorUtility.GetRendererInspectorNotices(renderer);
            Assert.That(
                (notices & VividParticleRendererInspectorNotice.StretchUsesPerParticleVelocity) != 0,
                Is.True);
            Assert.That(
                (notices & VividParticleRendererInspectorNotice.MeshIndexRequiresCustomShader) != 0,
                Is.True);
            Assert.That(
                (notices & VividParticleRendererInspectorNotice.PerParticleGpuDataIncreasesUpload) != 0,
                Is.True);
            Assert.That(
                (notices & VividParticleRendererInspectorNotice.SortingAllocatesPositions) != 0,
                Is.True);
        }

        [Test]
        public void RendererInspectorNotices_ReturnNone_WhenRendererModuleIsMissing()
        {
            Assert.That(
                VividParticleSystemEditorUtility.GetRendererInspectorNotices(null),
                Is.EqualTo(VividParticleRendererInspectorNotice.None));
        }

        private VividParticleSystem CreateSystem()
        {
            m_GameObject = new GameObject("Vivid Particle System Editor Test");
            return m_GameObject.AddComponent<VividParticleSystem>();
        }
    }
}

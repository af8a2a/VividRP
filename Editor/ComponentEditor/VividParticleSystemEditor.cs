using System;
using System.Globalization;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.Particle;

namespace VividRP.Editor
{
    internal struct VividParticleShapeSceneData
    {
        public bool Enabled;
        public VividParticleShapeType ShapeType;
        public float Radius;
        public Vector3 BoxSize;
        public float Angle;
    }

    [Flags]
    internal enum VividParticleRendererInspectorNotice
    {
        None = 0,
        RendererDisabled = 1 << 0,
        RenderModeNone = 1 << 1,
        MeshMissing = 1 << 2,
        StretchUsesPerParticleVelocity = 1 << 3,
        MeshIndexRequiresCustomShader = 1 << 4,
        SortingAllocatesPositions = 1 << 5,
        PerParticleGpuDataIncreasesUpload = 1 << 6,
        MultiMeshSplitsDrawCommands = 1 << 7,
    }

    internal readonly struct VividParticleGpuLayoutFootprint
    {
        public VividParticleGpuLayoutFootprint(
            int instanceCapacity,
            int sharpCapacity,
            int spanCapacity,
            int totalByteSize)
        {
            InstanceCapacity = instanceCapacity;
            SharpCapacity = sharpCapacity;
            SpanCapacity = spanCapacity;
            TotalByteSize = totalByteSize;
        }

        public int InstanceCapacity { get; }

        public int SharpCapacity { get; }

        public int SpanCapacity { get; }

        public int TotalByteSize { get; }
    }

    internal static class VividParticleSystemMenuItems
    {
        internal const string CreateVividParticleSystemMenuPath = "GameObject/Rendering/Vivid Particle System";

        [MenuItem(CreateVividParticleSystemMenuPath, priority = 11)]
        private static void CreateVividParticleSystem(MenuCommand menuCommand)
        {
            CreateVividParticleSystemGameObject(menuCommand.context as GameObject);
        }

        internal static GameObject CreateVividParticleSystemGameObject(GameObject parent)
        {
            var gameObject = new GameObject("Vivid Particle System");
            GameObjectUtility.SetParentAndAlign(gameObject, parent);
            gameObject.AddComponent<VividParticleSystem>();

            Undo.RegisterCreatedObjectUndo(gameObject, "Create Vivid Particle System");
            Selection.activeGameObject = gameObject;
            return gameObject;
        }
    }

    internal static class VividParticleSystemEditorUtility
    {
        private static readonly UploadColumnFormatEntry[] s_UploadColumnFormatEntries =
        {
            new(VividParticleSystemManager.UploadColumnPositionSizeMask, "PositionSize"),
            new(VividParticleSystemManager.UploadColumnBaseColorMask, "BaseColor"),
            new(VividParticleSystemManager.UploadColumnRotationMask, "Rotation"),
            new(VividParticleSystemManager.UploadColumnVelocityStretchMask, "VelocityStretch"),
            new(VividParticleSystemManager.UploadColumnScaleMask, "Scale"),
            new(VividParticleSystemManager.UploadColumnUVMask, "UV"),
            new(VividParticleSystemManager.UploadColumnCustomData1Mask, "CustomData1"),
            new(VividParticleSystemManager.UploadColumnCustomData2Mask, "CustomData2"),
            new(VividParticleSystemManager.UploadColumnMeshIndexMask, "MeshIndex"),
        };

        private static readonly GpuDataBitFormatEntry[] s_GpuDataBitFormatEntries =
        {
            new(VividParticleSystemManager.VividParticleGpuDataId.SharedData, "SharedData"),
            new(VividParticleSystemManager.VividParticleGpuDataId.SpanSharedData, "SpanSharedData"),
            new(VividParticleSystemManager.VividParticleGpuDataId.PositionSize, "PositionSize"),
            new(VividParticleSystemManager.VividParticleGpuDataId.BaseColor, "BaseColor"),
            new(VividParticleSystemManager.VividParticleGpuDataId.Rotation, "Rotation"),
            new(VividParticleSystemManager.VividParticleGpuDataId.VelocityStretch, "VelocityStretch"),
            new(VividParticleSystemManager.VividParticleGpuDataId.Scale, "Scale"),
            new(VividParticleSystemManager.VividParticleGpuDataId.UV, "UV"),
            new(VividParticleSystemManager.VividParticleGpuDataId.CustomData1, "CustomData1"),
            new(VividParticleSystemManager.VividParticleGpuDataId.CustomData2, "CustomData2"),
            new(VividParticleSystemManager.VividParticleGpuDataId.MeshIndex, "MeshIndex"),
        };

        private static readonly RenderJobFlagFormatEntry[] s_RenderJobFlagFormatEntries =
        {
            new(VividParticleSystemManager.RenderJobTransformUploadFlag, "Transform"),
            new(VividParticleSystemManager.RenderJobColorUploadFlag, "Color"),
            new(VividParticleSystemManager.RenderJobVelocityStretchUploadFlag, "VelocityStretch"),
            new(VividParticleSystemManager.RenderJobExtraDataUploadFlag, "ExtraData"),
            new(VividParticleSystemManager.RenderJobSharedDataFlag, "SharedData"),
        };

        internal static bool TryFindModuleRoots(
            SerializedObject serializedObject,
            out SerializedProperty main,
            out SerializedProperty emission,
            out SerializedProperty shape,
            out SerializedProperty renderer)
        {
            main = serializedObject?.FindProperty("m_Main");
            emission = serializedObject?.FindProperty("m_Emission");
            shape = serializedObject?.FindProperty("m_Shape");
            renderer = serializedObject?.FindProperty("m_Renderer");
            return main != null && emission != null && shape != null && renderer != null;
        }

        internal static SerializedProperty FindAssetProperty(SerializedObject serializedObject)
        {
            return serializedObject?.FindProperty("m_Asset");
        }

        internal static bool TryFindEmissionBurstsProperty(
            SerializedObject serializedObject,
            out SerializedProperty bursts)
        {
            bursts = serializedObject?.FindProperty("m_Emission.m_Bursts");
            return bursts != null;
        }

        internal static SerializedProperty FindRelative(SerializedProperty property, string path)
        {
            return property?.FindPropertyRelative(path);
        }

        internal static bool TryWriteBurstElement(SerializedProperty burst, float time, int count)
        {
            if (burst == null)
                return false;

            SerializedProperty burstTime = FindRelative(burst, "m_Time");
            SerializedProperty burstCount = FindRelative(burst, "m_Count");
            if (burstTime == null || burstCount == null)
                return false;

            burstTime.floatValue = Mathf.Max(0.0f, time);
            burstCount.intValue = Mathf.Max(0, count);
            return true;
        }

        internal static bool ApplyAssetTemplate(
            VividParticleSystem system,
            VividParticleSystemAsset asset,
            string undoName,
            bool force)
        {
            if (system == null)
                return false;

            if (!force && system.asset == asset)
                return false;

            if (!string.IsNullOrEmpty(undoName))
                Undo.RecordObject(system, undoName);

            if (force && system.asset == asset)
                system.asset = null;

            system.asset = asset;
            EditorUtility.SetDirty(system);
            return true;
        }

        internal static bool ApplyAssetTemplate(
            VividParticleSystem system,
            VividParticleSystemAsset asset,
            bool force)
        {
            return ApplyAssetTemplate(system, asset, "Apply Vivid Particle Template", force);
        }

        internal static bool CopyComponentSettingsToAsset(
            VividParticleSystem system,
            VividParticleSystemAsset asset,
            string undoName)
        {
            if (system == null || asset == null)
                return false;

            if (!string.IsNullOrEmpty(undoName))
                Undo.RecordObject(asset, undoName);

            asset.main.CopyFrom(system.main);
            asset.emission.CopyFrom(system.emission);
            asset.shape.CopyFrom(system.shape);
            asset.rendererModule.CopyFrom(system.rendererModule);
            asset.Validate();
            EditorUtility.SetDirty(asset);
            return true;
        }

        internal static bool CopyComponentSettingsToAsset(
            VividParticleSystem system,
            VividParticleSystemAsset asset)
        {
            return CopyComponentSettingsToAsset(system, asset, "Copy Vivid Particle Settings");
        }

        internal static VividParticleSystemAsset CreateAssetTemplateFromComponent(
            VividParticleSystem system,
            string assetPath,
            bool assignToSystem)
        {
            if (system == null || string.IsNullOrEmpty(assetPath))
                return null;

            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            var asset = ScriptableObject.CreateInstance<VividParticleSystemAsset>();
            CopyComponentSettingsToAsset(system, asset, undoName: null);
            AssetDatabase.CreateAsset(asset, uniquePath);
            Undo.RegisterCreatedObjectUndo(asset, "Create Vivid Particle Template");
            AssetDatabase.SaveAssetIfDirty(asset);

            if (assignToSystem)
                ApplyAssetTemplate(system, asset, "Assign Vivid Particle Template", force: false);

            return asset;
        }

        internal static bool TryReadShapeSceneData(
            SerializedProperty shape,
            out VividParticleShapeSceneData data)
        {
            data = default;
            if (shape == null)
                return false;

            SerializedProperty enabled = FindRelative(shape, "m_Enabled");
            SerializedProperty shapeType = FindRelative(shape, "m_ShapeType");
            SerializedProperty radius = FindRelative(shape, "m_Radius");
            SerializedProperty boxSize = FindRelative(shape, "m_BoxSize");
            SerializedProperty angle = FindRelative(shape, "m_Angle");
            if (enabled == null || shapeType == null || radius == null || boxSize == null || angle == null)
                return false;

            data = new VividParticleShapeSceneData
            {
                Enabled = enabled.boolValue,
                ShapeType = (VividParticleShapeType)shapeType.enumValueIndex,
                Radius = ClampShapeRadius(radius.floatValue),
                BoxSize = ClampShapeBoxSize(boxSize.vector3Value),
                Angle = ClampShapeAngle(angle.floatValue),
            };
            return true;
        }

        internal static void WriteShapeSceneData(
            SerializedProperty shape,
            VividParticleShapeSceneData data)
        {
            if (shape == null)
                return;

            SerializedProperty radius = FindRelative(shape, "m_Radius");
            SerializedProperty boxSize = FindRelative(shape, "m_BoxSize");
            SerializedProperty angle = FindRelative(shape, "m_Angle");
            if (radius != null)
                radius.floatValue = ClampShapeRadius(data.Radius);

            if (boxSize != null)
                boxSize.vector3Value = ClampShapeBoxSize(data.BoxSize);

            if (angle != null)
                angle.floatValue = ClampShapeAngle(data.Angle);
        }

        internal static float ClampShapeRadius(float radius)
        {
            return Mathf.Max(VividParticleShapeModule.MinimumRadius, radius);
        }

        internal static Vector3 ClampShapeBoxSize(Vector3 size)
        {
            return new Vector3(
                Mathf.Max(VividParticleShapeModule.MinimumBoxExtent, size.x),
                Mathf.Max(VividParticleShapeModule.MinimumBoxExtent, size.y),
                Mathf.Max(VividParticleShapeModule.MinimumBoxExtent, size.z));
        }

        internal static float ClampShapeAngle(float angle)
        {
            return Mathf.Clamp(angle, 0.0f, 89.0f);
        }

        internal static float GetConePreviewLength(float radius, float angle)
        {
            float clampedRadius = Mathf.Max(0.25f, ClampShapeRadius(radius));
            float tangent = Mathf.Tan(Mathf.Deg2Rad * Mathf.Clamp(angle, 1.0f, 89.0f));
            if (tangent <= 0.0001f)
                return clampedRadius;

            return Mathf.Clamp(clampedRadius / tangent, 0.25f, clampedRadius * 4.0f);
        }

        internal static Vector3 GetConeAngleHandlePosition(float radius, float angle)
        {
            float length = GetConePreviewLength(radius, angle);
            float endRadius = Mathf.Tan(Mathf.Deg2Rad * ClampShapeAngle(angle)) * length;
            return new Vector3(endRadius, 0.0f, length);
        }

        internal static float GetConeAngleFromHandlePosition(Vector3 handlePosition)
        {
            float length = Mathf.Max(0.0001f, Mathf.Abs(handlePosition.z));
            float radius = Mathf.Max(0.0f, Mathf.Abs(handlePosition.x));
            return ClampShapeAngle(Mathf.Atan2(radius, length) * Mathf.Rad2Deg);
        }

        internal static bool TryCreateGpuDataLayoutDescriptor(
            SerializedProperty renderer,
            out VividParticleSystemManager.VividParticleGpuDataLayoutDescriptor descriptor)
        {
            descriptor = default;
            if (renderer == null)
                return false;

            SerializedProperty renderMode = FindRelative(renderer, "m_RenderMode");
            SerializedProperty colorDataMode = FindRelative(renderer, "m_ColorDataMode");
            SerializedProperty rotationDataMode = FindRelative(renderer, "m_RotationDataMode");
            SerializedProperty velocityDataMode = FindRelative(renderer, "m_VelocityDataMode");
            SerializedProperty sizeDataMode = FindRelative(renderer, "m_SizeDataMode");
            SerializedProperty uvDataEnabled = FindRelative(renderer, "m_UVDataEnabled");
            SerializedProperty customData1Enabled = FindRelative(renderer, "m_CustomData1Enabled");
            SerializedProperty customData2Enabled = FindRelative(renderer, "m_CustomData2Enabled");
            SerializedProperty meshIndexDataEnabled = FindRelative(renderer, "m_MeshIndexDataEnabled");
            if (HasMissingOrMixedValue(
                    renderMode,
                    colorDataMode,
                    rotationDataMode,
                    velocityDataMode,
                    sizeDataMode,
                    uvDataEnabled,
                    customData1Enabled,
                    customData2Enabled,
                    meshIndexDataEnabled))
            {
                return false;
            }

            descriptor = new VividParticleSystemManager.VividParticleGpuDataLayoutDescriptor(
                (VividParticleRenderMode)renderMode.enumValueIndex,
                (VividParticleGpuDataMode)colorDataMode.enumValueIndex,
                (VividParticleGpuDataMode)rotationDataMode.enumValueIndex,
                (VividParticleGpuDataMode)velocityDataMode.enumValueIndex,
                (VividParticleGpuDataMode)sizeDataMode.enumValueIndex,
                uvDataEnabled.boolValue,
                customData1Enabled.boolValue,
                customData2Enabled.boolValue,
                meshIndexDataEnabled.boolValue);
            return true;
        }

        internal static bool TryCreateGpuLayoutFootprint(
            SerializedProperty renderer,
            out VividParticleGpuLayoutFootprint footprint)
        {
            footprint = default;
            if (!TryCreateGpuDataLayoutDescriptor(
                    renderer,
                    out VividParticleSystemManager.VividParticleGpuDataLayoutDescriptor descriptor))
            {
                return false;
            }

            SerializedProperty maxParticles = renderer.serializedObject?.FindProperty("m_Main.m_MaxParticles");
            if (maxParticles == null || maxParticles.hasMultipleDifferentValues)
                return false;

            int instanceCapacity = Mathf.Max(VividParticleMainModule.MinimumMaxParticles, maxParticles.intValue);
            int sharpCapacity = 1;
            int spanCapacity = Mathf.Max(
                1,
                VividParticleSystemManager.GetVisibleInstanceCount(descriptor.RenderMode, instanceCapacity));
            VividParticleSystemManager.VividParticleGpuDataLayout layout =
                VividParticleSystemManager.VividParticleGpuDataLayout.Create(descriptor);
            footprint = new VividParticleGpuLayoutFootprint(
                instanceCapacity,
                sharpCapacity,
                spanCapacity,
                layout.CalculateByteSize(instanceCapacity, sharpCapacity, spanCapacity));
            return true;
        }

        internal static string FormatUploadColumnMask(int columnMask)
        {
            if (columnMask == 0)
                return "None (0x0)";

            var builder = new System.Text.StringBuilder();
            int remainingMask = columnMask;
            for (int index = 0; index < s_UploadColumnFormatEntries.Length; index++)
            {
                UploadColumnFormatEntry entry = s_UploadColumnFormatEntries[index];
                if ((columnMask & entry.Mask) == 0)
                    continue;

                if (builder.Length > 0)
                    builder.Append(" | ");

                builder.Append(entry.Name);
                remainingMask &= ~entry.Mask;
            }

            if (remainingMask != 0)
            {
                if (builder.Length > 0)
                    builder.Append(" | ");

                builder.Append("Unknown(0x");
                builder.Append(remainingMask.ToString("X", CultureInfo.InvariantCulture));
                builder.Append(')');
            }

            builder.Append(" (0x");
            builder.Append(columnMask.ToString("X", CultureInfo.InvariantCulture));
            builder.Append(')');
            return builder.ToString();
        }

        internal static string FormatGpuDataBits(uint bits)
        {
            if (bits == 0u)
                return "None (0x0)";

            var builder = new System.Text.StringBuilder();
            uint remainingBits = bits;
            for (int index = 0; index < s_GpuDataBitFormatEntries.Length; index++)
            {
                GpuDataBitFormatEntry entry = s_GpuDataBitFormatEntries[index];
                if ((bits & entry.Bit) == 0u)
                    continue;

                if (builder.Length > 0)
                    builder.Append(" | ");

                builder.Append(entry.Name);
                remainingBits &= ~entry.Bit;
            }

            if (remainingBits != 0u)
            {
                if (builder.Length > 0)
                    builder.Append(" | ");

                builder.Append("Unknown(0x");
                builder.Append(remainingBits.ToString("X", CultureInfo.InvariantCulture));
                builder.Append(')');
            }

            builder.Append(" (0x");
            builder.Append(bits.ToString("X", CultureInfo.InvariantCulture));
            builder.Append(')');
            return builder.ToString();
        }

        internal static string FormatRenderJobModuleFlags(uint flags)
        {
            if (flags == 0u)
                return "None (0x0)";

            var builder = new System.Text.StringBuilder();
            uint remainingFlags = flags;
            for (int index = 0; index < s_RenderJobFlagFormatEntries.Length; index++)
            {
                RenderJobFlagFormatEntry entry = s_RenderJobFlagFormatEntries[index];
                if ((flags & entry.Flag) == 0u)
                    continue;

                if (builder.Length > 0)
                    builder.Append(" | ");

                builder.Append(entry.Name);
                remainingFlags &= ~entry.Flag;
            }

            if (remainingFlags != 0u)
            {
                if (builder.Length > 0)
                    builder.Append(" | ");

                builder.Append("Unknown(0x");
                builder.Append(remainingFlags.ToString("X", CultureInfo.InvariantCulture));
                builder.Append(')');
            }

            builder.Append(" (0x");
            builder.Append(flags.ToString("X", CultureInfo.InvariantCulture));
            builder.Append(')');
            return builder.ToString();
        }

        internal static string FormatGpuDataInfo(VividParticleSystemManager.VividParticleGpuDataInfo dataInfo)
        {
            string copyMask = dataInfo.HasUploadSegment
                ? FormatUploadColumnMask(dataInfo.UploadColumnMask)
                : "None (0x0)";
            string renderJobs = dataInfo.RenderJobFlagMask != 0u
                ? FormatRenderJobModuleFlags(dataInfo.RenderJobFlagMask)
                : "None (0x0)";
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} / {1} / {2} / {3} B / Bit {4} / Copy {5} / Job {6}",
                dataInfo.Role,
                dataInfo.Frequency,
                dataInfo.UploadSegment,
                dataInfo.ElementSize,
                FormatGpuDataBits(dataInfo.DataBit),
                copyMask,
                renderJobs);
        }

        internal static string FormatByteSize(int byteSize)
        {
            int clampedByteSize = Mathf.Max(0, byteSize);
            if (clampedByteSize >= 1024 * 1024)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:0.##} MiB ({1} B)",
                    clampedByteSize / (1024.0f * 1024.0f),
                    clampedByteSize);
            }

            if (clampedByteSize >= 1024)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:0.##} KiB ({1} B)",
                    clampedByteSize / 1024.0f,
                    clampedByteSize);
            }

            return string.Format(CultureInfo.InvariantCulture, "{0} B", clampedByteSize);
        }

        internal static VividParticleRendererInspectorNotice GetRendererInspectorNotices(SerializedProperty renderer)
        {
            if (renderer == null)
                return VividParticleRendererInspectorNotice.None;

            VividParticleRendererInspectorNotice notices = VividParticleRendererInspectorNotice.None;
            SerializedProperty enabled = FindRelative(renderer, "m_Enabled");
            SerializedProperty renderMode = FindRelative(renderer, "m_RenderMode");
            SerializedProperty mesh = FindRelative(renderer, "m_Mesh");
            SerializedProperty meshes = FindRelative(renderer, "m_Meshes");
            SerializedProperty colorDataMode = FindRelative(renderer, "m_ColorDataMode");
            SerializedProperty rotationDataMode = FindRelative(renderer, "m_RotationDataMode");
            SerializedProperty velocityDataMode = FindRelative(renderer, "m_VelocityDataMode");
            SerializedProperty sizeDataMode = FindRelative(renderer, "m_SizeDataMode");
            SerializedProperty uvDataEnabled = FindRelative(renderer, "m_UVDataEnabled");
            SerializedProperty customData1Enabled = FindRelative(renderer, "m_CustomData1Enabled");
            SerializedProperty customData2Enabled = FindRelative(renderer, "m_CustomData2Enabled");
            SerializedProperty meshIndexDataEnabled = FindRelative(renderer, "m_MeshIndexDataEnabled");
            SerializedProperty sortMode = FindRelative(renderer, "m_SortMode");

            if (enabled != null && !enabled.hasMultipleDifferentValues && !enabled.boolValue)
                notices |= VividParticleRendererInspectorNotice.RendererDisabled;

            if (renderMode != null && !renderMode.hasMultipleDifferentValues)
            {
                VividParticleRenderMode resolvedMode = (VividParticleRenderMode)renderMode.enumValueIndex;
                if (resolvedMode == VividParticleRenderMode.None)
                    notices |= VividParticleRendererInspectorNotice.RenderModeNone;

                if (resolvedMode == VividParticleRenderMode.Mesh
                    && !HasAnyMeshReference(mesh, meshes))
                {
                    notices |= VividParticleRendererInspectorNotice.MeshMissing;
                }

                if (resolvedMode == VividParticleRenderMode.Mesh
                    && HasMultipleMeshReferences(mesh, meshes))
                {
                    notices |= VividParticleRendererInspectorNotice.MultiMeshSplitsDrawCommands;
                }

                if (resolvedMode == VividParticleRenderMode.Stretch)
                    notices |= VividParticleRendererInspectorNotice.StretchUsesPerParticleVelocity;
            }

            if (meshIndexDataEnabled != null
                && !meshIndexDataEnabled.hasMultipleDifferentValues
                && meshIndexDataEnabled.boolValue)
            {
                notices |= VividParticleRendererInspectorNotice.MeshIndexRequiresCustomShader;
            }

            if (UsesExplicitPerParticleGpuData(
                    colorDataMode,
                    rotationDataMode,
                    velocityDataMode,
                    sizeDataMode,
                    uvDataEnabled,
                    customData1Enabled,
                    customData2Enabled))
            {
                notices |= VividParticleRendererInspectorNotice.PerParticleGpuDataIncreasesUpload;
            }

            if (sortMode != null
                && !sortMode.hasMultipleDifferentValues
                && (VividParticleSortMode)sortMode.enumValueIndex != VividParticleSortMode.None)
            {
                notices |= VividParticleRendererInspectorNotice.SortingAllocatesPositions;
            }

            return notices;
        }

        private static bool HasAnyMeshReference(SerializedProperty mesh, SerializedProperty meshes)
        {
            if (mesh != null)
            {
                if (mesh.hasMultipleDifferentValues)
                    return true;

                if (mesh.objectReferenceValue != null)
                    return true;
            }

            if (meshes == null)
                return false;

            if (meshes.hasMultipleDifferentValues)
                return true;

            for (int index = 0; index < meshes.arraySize; index++)
            {
                SerializedProperty element = meshes.GetArrayElementAtIndex(index);
                if (element == null)
                    continue;

                if (element.hasMultipleDifferentValues || element.objectReferenceValue != null)
                    return true;
            }

            return false;
        }

        private static bool HasMultipleMeshReferences(SerializedProperty mesh, SerializedProperty meshes)
        {
            int count = 0;
            if (mesh != null)
            {
                if (mesh.hasMultipleDifferentValues)
                    return false;

                if (mesh.objectReferenceValue != null)
                    count++;
            }

            if (meshes == null || meshes.hasMultipleDifferentValues)
                return false;

            for (int index = 0; index < meshes.arraySize; index++)
            {
                SerializedProperty element = meshes.GetArrayElementAtIndex(index);
                if (element == null || element.hasMultipleDifferentValues || element.objectReferenceValue == null)
                    continue;

                count++;
                if (count > 1)
                    return true;
            }

            return false;
        }

        private static bool UsesExplicitPerParticleGpuData(params SerializedProperty[] properties)
        {
            for (int index = 0; index < properties.Length; index++)
            {
                SerializedProperty property = properties[index];
                if (property == null || property.hasMultipleDifferentValues)
                    continue;

                if (property.propertyType == SerializedPropertyType.Enum
                    && (VividParticleGpuDataMode)property.enumValueIndex == VividParticleGpuDataMode.PerParticle)
                {
                    return true;
                }

                if (property.propertyType == SerializedPropertyType.Boolean && property.boolValue)
                    return true;
            }

            return false;
        }

        private readonly struct UploadColumnFormatEntry
        {
            public UploadColumnFormatEntry(int mask, string name)
            {
                Mask = mask;
                Name = name;
            }

            public readonly int Mask;
            public readonly string Name;
        }

        private readonly struct GpuDataBitFormatEntry
        {
            public GpuDataBitFormatEntry(VividParticleSystemManager.VividParticleGpuDataId dataId, string name)
            {
                Bit = VividParticleSystemManager.GetGpuDataBit(dataId);
                Name = name;
            }

            public readonly uint Bit;
            public readonly string Name;
        }

        private readonly struct RenderJobFlagFormatEntry
        {
            public RenderJobFlagFormatEntry(uint flag, string name)
            {
                Flag = flag;
                Name = name;
            }

            public readonly uint Flag;
            public readonly string Name;
        }

        private static bool HasMissingOrMixedValue(params SerializedProperty[] properties)
        {
            for (int index = 0; index < properties.Length; index++)
            {
                if (properties[index] == null || properties[index].hasMultipleDifferentValues)
                    return true;
            }

            return false;
        }
    }

    internal abstract class VividParticleSystemEditorBase : UnityEditor.Editor
    {
        private static readonly GUIContent s_MainLabel = EditorGUIUtility.TrTextContent("Main");
        private static readonly GUIContent s_EmissionLabel = EditorGUIUtility.TrTextContent("Emission");
        private static readonly GUIContent s_ShapeLabel = EditorGUIUtility.TrTextContent("Shape");
        private static readonly GUIContent s_RendererLabel = EditorGUIUtility.TrTextContent("Renderer");
        private static readonly GUIContent s_DebugLabel = EditorGUIUtility.TrTextContent("Debug");
        private static readonly GUIContent s_DataLayoutLabel = EditorGUIUtility.TrTextContent("GPU Data Layout");
        private static readonly GUIContent s_PlayLabel = EditorGUIUtility.TrTextContent("Play");
        private static readonly GUIContent s_PauseLabel = EditorGUIUtility.TrTextContent("Pause");
        private static readonly GUIContent s_StopLabel = EditorGUIUtility.TrTextContent("Stop");
        private static readonly GUIContent s_ClearLabel = EditorGUIUtility.TrTextContent("Clear");
        private static readonly GUIContent s_EmitLabel = EditorGUIUtility.TrTextContent("Emit");
        private static readonly GUIContent s_EmitCountLabel = EditorGUIUtility.TrTextContent("Emit Count");
        private static readonly GUIContent s_BurstsLabel = EditorGUIUtility.TrTextContent("Bursts");
        private static readonly GUIContent s_BurstTimeLabel = EditorGUIUtility.TrTextContent("Time");
        private static readonly GUIContent s_BurstCountLabel = EditorGUIUtility.TrTextContent("Count");
        private static readonly GUIContent s_ParticleCountLabel = EditorGUIUtility.TrTextContent("Particle Count");
        private static readonly GUIContent s_TimeLabel = EditorGUIUtility.TrTextContent("Time");
        private static readonly GUIContent s_PageSizeLabel = EditorGUIUtility.TrTextContent("Page Size");
        private static readonly GUIContent s_StorageCapacityLabel = EditorGUIUtility.TrTextContent("Storage Capacity");
        private static readonly GUIContent s_StoragePageCountLabel = EditorGUIUtility.TrTextContent("Storage Pages");
        private static readonly GUIContent s_PendingSimulationLabel = EditorGUIUtility.TrTextContent("Pending Simulation");
        private static readonly GUIContent s_RenderRecordsLabel = EditorGUIUtility.TrTextContent("Render Records");
        private static readonly GUIContent s_LineGroupsLabel = EditorGUIUtility.TrTextContent("Line Groups");
        private static readonly GUIContent s_EcsLineGroupsLabel = EditorGUIUtility.TrTextContent("ECS Line Groups");
        private static readonly GUIContent s_EcsLinesLabel = EditorGUIUtility.TrTextContent("ECS Lines");
        private static readonly GUIContent s_EcsMatchedLinesLabel = EditorGUIUtility.TrTextContent("ECS Matched Lines");
        private static readonly GUIContent s_EcsSkippedLinesLabel = EditorGUIUtility.TrTextContent("ECS Skipped Lines");
        private static readonly GUIContent s_DrawBatchesLabel = EditorGUIUtility.TrTextContent("Draw Batches");
        private static readonly GUIContent s_CullingRecordsLabel = EditorGUIUtility.TrTextContent("Culling Records");
        private static readonly GUIContent s_DrawCommandsLabel = EditorGUIUtility.TrTextContent("Draw Commands");
        private static readonly GUIContent s_DrawRangesLabel = EditorGUIUtility.TrTextContent("Draw Ranges");
        private static readonly GUIContent s_VisibleCapacityLabel = EditorGUIUtility.TrTextContent("Visible Capacity");
        private static readonly GUIContent s_SortingCapacityLabel = EditorGUIUtility.TrTextContent("Sorting Capacity");
        private static readonly GUIContent s_LightCommandsLabel = EditorGUIUtility.TrTextContent("Light Commands");
        private static readonly GUIContent s_LightRangesLabel = EditorGUIUtility.TrTextContent("Light Ranges");
        private static readonly GUIContent s_LightVisibleCapacityLabel =
            EditorGUIUtility.TrTextContent("Light Visible Capacity");
        private static readonly GUIContent s_PickingCommandsLabel = EditorGUIUtility.TrTextContent("Picking Commands");
        private static readonly GUIContent s_PickingRangesLabel = EditorGUIUtility.TrTextContent("Picking Ranges");
        private static readonly GUIContent s_PickingVisibleCapacityLabel =
            EditorGUIUtility.TrTextContent("Picking Visible Capacity");
        private static readonly GUIContent s_SelectionCommandsLabel = EditorGUIUtility.TrTextContent("Selection Commands");
        private static readonly GUIContent s_SelectionRangesLabel = EditorGUIUtility.TrTextContent("Selection Ranges");
        private static readonly GUIContent s_SelectionVisibleCapacityLabel =
            EditorGUIUtility.TrTextContent("Selection Visible Capacity");
        private static readonly GUIContent s_BoundsPageWorksLabel = EditorGUIUtility.TrTextContent("Bounds Page Works");
        private static readonly GUIContent s_CullingSingleMeshCachesLabel =
            EditorGUIUtility.TrTextContent("Single Mesh Cache Records");
        private static readonly GUIContent s_CullingMultiMeshCachesLabel =
            EditorGUIUtility.TrTextContent("Multi Mesh Cache Records");
        private static readonly GUIContent s_CullingMeshFallbacksLabel =
            EditorGUIUtility.TrTextContent("Mesh Fallback Records");
        private static readonly GUIContent s_CullingRecordCacheEntriesLabel =
            EditorGUIUtility.TrTextContent("Record Cache Entries");
        private static readonly GUIContent s_CullingBatchCacheEntriesLabel =
            EditorGUIUtility.TrTextContent("Batch Cache Entries");
        private static readonly GUIContent s_MeshVisibleWorksLabel = EditorGUIUtility.TrTextContent("Mesh Visible Works");
        private static readonly GUIContent s_MeshVisibleOutputsLabel = EditorGUIUtility.TrTextContent("Mesh Visible Outputs");
        private static readonly GUIContent s_LastUploadLabel = EditorGUIUtility.TrTextContent("Last Upload");
        private static readonly GUIContent s_DirtyUploadQueueLabel = EditorGUIUtility.TrTextContent("Dirty Upload Queue");
        private static readonly GUIContent s_InvalidDirtyUploadQueueLabel =
            EditorGUIUtility.TrTextContent("Invalid Dirty Uploads");
        private static readonly GUIContent s_DirtyUploadBatchQueueLabel =
            EditorGUIUtility.TrTextContent("Dirty Batch Queue");
        private static readonly GUIContent s_InvalidDirtyUploadBatchQueueLabel =
            EditorGUIUtility.TrTextContent("Invalid Dirty Batches");
        private static readonly GUIContent s_UploadRecordWorksLabel = EditorGUIUtility.TrTextContent("Upload Record Works");
        private static readonly GUIContent s_UploadBatchWorksLabel = EditorGUIUtility.TrTextContent("Upload Batch Works");
        private static readonly GUIContent s_TransformUploadPageWorksLabel =
            EditorGUIUtility.TrTextContent("Transform Page Works");
        private static readonly GUIContent s_ColorUploadPageWorksLabel =
            EditorGUIUtility.TrTextContent("Color Page Works");
        private static readonly GUIContent s_VelocityUploadPageWorksLabel =
            EditorGUIUtility.TrTextContent("Velocity Page Works");
        private static readonly GUIContent s_ExtraUploadPageWorksLabel =
            EditorGUIUtility.TrTextContent("Extra Page Works");
        private static readonly GUIContent s_LastUploadCopyWorksLabel = EditorGUIUtility.TrTextContent("Upload Copy Works");
        private static readonly GUIContent s_MergedUploadCopyWorksLabel =
            EditorGUIUtility.TrTextContent("Merged Copy Works");
        private static readonly GUIContent s_RenderJobFlagsLabel = EditorGUIUtility.TrTextContent("Render Job Flags");
        private static readonly GUIContent s_RenderPageJobModulesLabel =
            EditorGUIUtility.TrTextContent("Render Page Job Modules");
        private static readonly GUIContent s_LastUploadColumnMaskLabel = EditorGUIUtility.TrTextContent("Upload Column Mask");
        private static readonly GUIContent s_LastUploadDataBitsLabel = EditorGUIUtility.TrTextContent("Upload Data Bits");
        private static readonly GUIContent s_LayoutHashLabel = EditorGUIUtility.TrTextContent("Layout Hash");
        private static readonly GUIContent s_DataPerSharpBitsLabel = EditorGUIUtility.TrTextContent("Per-Sharp Bits");
        private static readonly GUIContent s_LayoutColumnCountLabel = EditorGUIUtility.TrTextContent("Column Count");
        private static readonly GUIContent s_PerInstanceUploadBytesLabel = EditorGUIUtility.TrTextContent("Per-Particle Upload Bytes");
        private static readonly GUIContent s_PerInstanceUploadMaskLabel = EditorGUIUtility.TrTextContent("Per-Particle Upload Mask");
        private static readonly GUIContent s_PerInstanceRenderJobsLabel = EditorGUIUtility.TrTextContent("Per-Particle Render Jobs");
        private static readonly GUIContent s_TransformUploadMaskLabel =
            EditorGUIUtility.TrTextContent("Transform Upload Mask");
        private static readonly GUIContent s_ColorUploadMaskLabel =
            EditorGUIUtility.TrTextContent("Color Upload Mask");
        private static readonly GUIContent s_VelocityUploadMaskLabel =
            EditorGUIUtility.TrTextContent("Velocity Upload Mask");
        private static readonly GUIContent s_ExtraUploadMaskLabel =
            EditorGUIUtility.TrTextContent("Extra Upload Mask");
        private static readonly GUIContent s_UVUploadMaskLabel =
            EditorGUIUtility.TrTextContent("UV Upload Mask");
        private static readonly GUIContent s_CustomDataUploadMaskLabel =
            EditorGUIUtility.TrTextContent("Custom Data Upload Mask");
        private static readonly GUIContent s_MeshIndexUploadMaskLabel =
            EditorGUIUtility.TrTextContent("Mesh Index Upload Mask");
        private static readonly GUIContent s_InstanceCapacityLabel = EditorGUIUtility.TrTextContent("Instance Capacity");
        private static readonly GUIContent s_SharpCapacityLabel = EditorGUIUtility.TrTextContent("Sharp Capacity");
        private static readonly GUIContent s_SpanCapacityLabel = EditorGUIUtility.TrTextContent("Span Capacity");
        private static readonly GUIContent s_EstimatedBufferBytesLabel =
            EditorGUIUtility.TrTextContent("Estimated Buffer Bytes");
        private const string RendererDisabledNotice =
            "Renderer is disabled. Particles can still simulate, but this module will not produce BRG draw records.";
        private const string RenderModeNoneNotice =
            "Render Mode None keeps simulation active without initializing particle rendering.";
        private const string MeshMissingNotice =
            "Mesh render mode needs at least one Mesh asset before particles can render.";
        private const string StretchVelocityNotice =
            "Stretch mode keeps velocity stretch as per-particle GPU data so each particle can stretch independently.";
        private const string MeshIndexNotice =
            "Mesh Index GPU data is uploaded for custom shaders; built-in rendering remaps Mesh mode particles into mesh-specific draw commands.";
        private const string MultiMeshNotice =
            "Multiple Meshes split Mesh mode into mesh-specific BRG draw commands; invalid mesh indices render with the first mesh.";
        private const string SortingPositionsNotice =
            "Non-default Sort Mode allocates sorting positions for camera views.";
        private const string PerParticleGpuDataNotice =
            "Per-particle GPU data adds upload columns and increases particle buffer bandwidth.";

        private SerializedProperty m_Main;
        private SerializedProperty m_Emission;
        private SerializedProperty m_Shape;
        private SerializedProperty m_Renderer;
        private bool m_MainExpanded = true;
        private bool m_EmissionExpanded = true;
        private bool m_ShapeExpanded = true;
        private bool m_RendererExpanded = true;
        private bool m_DebugExpanded = true;
        private int m_EmitCount = 1;
        private ReorderableList m_BurstList;
        private SerializedObject m_BurstListSerializedObject;
        private string m_BurstListPropertyPath;

        protected virtual void OnEnable()
        {
            VividParticleSystemEditorUtility.TryFindModuleRoots(
                serializedObject,
                out m_Main,
                out m_Emission,
                out m_Shape,
                out m_Renderer);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawHeaderInspector();
            DrawModule(s_MainLabel, ref m_MainExpanded, () => DrawMainModule(m_Main));
            DrawModule(s_EmissionLabel, ref m_EmissionExpanded, () => DrawEmissionModule(m_Emission));
            DrawModule(s_ShapeLabel, ref m_ShapeExpanded, () => DrawShapeModule(m_Shape));
            DrawModule(s_RendererLabel, ref m_RendererExpanded, () => DrawRendererModule(m_Renderer));
            DrawFooterInspector();

            serializedObject.ApplyModifiedProperties();
        }

        protected virtual void DrawHeaderInspector()
        {
        }

        protected virtual void DrawFooterInspector()
        {
        }

        protected void DrawRuntimeControlsAndStats()
        {
            if (targets.Length != 1 || target is not VividParticleSystem system)
                return;

            EditorGUILayout.Space();
            m_DebugExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(m_DebugExpanded, s_DebugLabel);
            if (m_DebugExpanded)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawPlaybackControls();
                    DrawRuntimeStats(system);
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawPlaybackControls()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(s_PlayLabel))
                    ExecuteOnTargets("Play Vivid Particle System", system => system.Play(withChildren: false));

                if (GUILayout.Button(s_PauseLabel))
                    ExecuteOnTargets("Pause Vivid Particle System", system => system.Pause(withChildren: false));

                if (GUILayout.Button(s_StopLabel))
                    ExecuteOnTargets(
                        "Stop Vivid Particle System",
                        system => system.Stop(withChildren: false, VividParticleSystemStopBehavior.StopEmitting));

                if (GUILayout.Button(s_ClearLabel))
                    ExecuteOnTargets(
                        "Clear Vivid Particle System",
                        system => system.Stop(withChildren: false, VividParticleSystemStopBehavior.StopEmittingAndClear));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                m_EmitCount = Mathf.Max(1, EditorGUILayout.IntField(s_EmitCountLabel, m_EmitCount));
                if (GUILayout.Button(s_EmitLabel, GUILayout.Width(80.0f)))
                    ExecuteOnTargets("Emit Vivid Particles", system => system.Emit(m_EmitCount));
            }
        }

        private static void DrawRuntimeStats(VividParticleSystem system)
        {
            VividParticleSystemManager.TryGetRuntimeStats(
                system,
                out VividParticleSystemManager.VividParticleSystemRuntimeStats runtimeStats);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField(s_ParticleCountLabel, runtimeStats.ParticleCount);
                EditorGUILayout.FloatField(s_TimeLabel, runtimeStats.Time);
                EditorGUILayout.IntField(s_PageSizeLabel, runtimeStats.PageSize);
                EditorGUILayout.IntField(s_StorageCapacityLabel, runtimeStats.StorageCapacity);
                EditorGUILayout.IntField(s_StoragePageCountLabel, runtimeStats.StoragePageCount);
                EditorGUILayout.Toggle(s_PendingSimulationLabel, runtimeStats.HasPendingSimulation);
            }

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStats();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField(s_RenderRecordsLabel, rendererStats.RenderRecordCount);
                EditorGUILayout.IntField(s_LineGroupsLabel, rendererStats.LineGroupCount);
                EditorGUILayout.IntField(s_EcsLineGroupsLabel, rendererStats.EcsLineGroupCount);
                EditorGUILayout.IntField(s_EcsLinesLabel, rendererStats.EcsLineCount);
                EditorGUILayout.IntField(s_EcsMatchedLinesLabel, rendererStats.EcsMatchedLineCount);
                EditorGUILayout.IntField(s_EcsSkippedLinesLabel, rendererStats.EcsSkippedLineCount);
                EditorGUILayout.IntField(s_DrawBatchesLabel, rendererStats.DrawBatchCount);
                EditorGUILayout.IntField(s_CullingRecordsLabel, rendererStats.CullingRecordCount);
                EditorGUILayout.IntField(s_DrawCommandsLabel, rendererStats.DrawCommandCount);
                EditorGUILayout.IntField(s_DrawRangesLabel, rendererStats.DrawRangeCount);
                EditorGUILayout.IntField(s_VisibleCapacityLabel, rendererStats.VisibleInstanceCapacity);
                EditorGUILayout.IntField(s_SortingCapacityLabel, rendererStats.SortingPositionCapacity);
                EditorGUILayout.IntField(s_LightCommandsLabel, rendererStats.LightDrawCommandCount);
                EditorGUILayout.IntField(s_LightRangesLabel, rendererStats.LightDrawRangeCount);
                EditorGUILayout.IntField(s_LightVisibleCapacityLabel, rendererStats.LightVisibleInstanceCapacity);
                EditorGUILayout.IntField(s_PickingCommandsLabel, rendererStats.PickingDrawCommandCount);
                EditorGUILayout.IntField(s_PickingRangesLabel, rendererStats.PickingDrawRangeCount);
                EditorGUILayout.IntField(s_PickingVisibleCapacityLabel, rendererStats.PickingVisibleInstanceCapacity);
                EditorGUILayout.IntField(s_SelectionCommandsLabel, rendererStats.SelectionDrawCommandCount);
                EditorGUILayout.IntField(s_SelectionRangesLabel, rendererStats.SelectionDrawRangeCount);
                EditorGUILayout.IntField(s_SelectionVisibleCapacityLabel, rendererStats.SelectionVisibleInstanceCapacity);
                EditorGUILayout.IntField(s_BoundsPageWorksLabel, rendererStats.LastBoundsPageWorkCount);
                EditorGUILayout.IntField(
                    s_CullingSingleMeshCachesLabel,
                    rendererStats.LastCullingSingleMeshCacheRecordCount);
                EditorGUILayout.IntField(
                    s_CullingMultiMeshCachesLabel,
                    rendererStats.LastCullingMultiMeshCacheRecordCount);
                EditorGUILayout.IntField(
                    s_CullingMeshFallbacksLabel,
                    rendererStats.LastCullingMeshFallbackRecordCount);
                EditorGUILayout.IntField(
                    s_CullingRecordCacheEntriesLabel,
                    rendererStats.LastCullingRecordVisibleCacheEntryCount);
                EditorGUILayout.IntField(
                    s_CullingBatchCacheEntriesLabel,
                    rendererStats.LastCullingBatchVisibleCacheEntryCount);
                EditorGUILayout.IntField(s_MeshVisibleWorksLabel, rendererStats.MeshVisibleCountWorkCount);
                EditorGUILayout.IntField(s_MeshVisibleOutputsLabel, rendererStats.MeshVisibleCountOutputCount);
                EditorGUILayout.TextField(
                    s_LastUploadLabel,
                    VividParticleSystemEditorUtility.FormatByteSize(rendererStats.LastCopyByteCount));
                EditorGUILayout.IntField(s_DirtyUploadQueueLabel, rendererStats.LastDirtyUploadQueueCount);
                EditorGUILayout.IntField(s_InvalidDirtyUploadQueueLabel, rendererStats.LastInvalidDirtyUploadQueueCount);
                EditorGUILayout.IntField(s_DirtyUploadBatchQueueLabel, rendererStats.LastDirtyUploadBatchQueueCount);
                EditorGUILayout.IntField(
                    s_InvalidDirtyUploadBatchQueueLabel,
                    rendererStats.LastInvalidDirtyUploadBatchQueueCount);
                EditorGUILayout.IntField(s_UploadRecordWorksLabel, rendererStats.LastUploadRecordWorkCount);
                EditorGUILayout.IntField(s_UploadBatchWorksLabel, rendererStats.LastUploadBatchWorkCount);
                EditorGUILayout.IntField(s_TransformUploadPageWorksLabel, rendererStats.LastTransformUploadPageWorkCount);
                EditorGUILayout.IntField(s_ColorUploadPageWorksLabel, rendererStats.LastColorUploadPageWorkCount);
                EditorGUILayout.IntField(s_VelocityUploadPageWorksLabel, rendererStats.LastVelocityStretchUploadPageWorkCount);
                EditorGUILayout.IntField(s_ExtraUploadPageWorksLabel, rendererStats.LastExtraDataUploadPageWorkCount);
                EditorGUILayout.IntField(s_LastUploadCopyWorksLabel, rendererStats.LastUploadCopyWorkCount);
                EditorGUILayout.IntField(s_MergedUploadCopyWorksLabel, rendererStats.LastMergedUploadCopyWorkCount);
                EditorGUILayout.TextField(
                    s_RenderJobFlagsLabel,
                    VividParticleSystemEditorUtility.FormatRenderJobModuleFlags(rendererStats.LastRenderJobModuleFlags));
                EditorGUILayout.IntField(s_RenderPageJobModulesLabel, rendererStats.LastRenderPageJobModuleCount);
                EditorGUILayout.TextField(
                    s_LastUploadColumnMaskLabel,
                    VividParticleSystemEditorUtility.FormatUploadColumnMask(rendererStats.LastUploadColumnMask));
                EditorGUILayout.TextField(
                    s_LastUploadDataBitsLabel,
                    VividParticleSystemEditorUtility.FormatGpuDataBits(rendererStats.LastUploadDataBits));
            }
        }

        private void ExecuteOnTargets(string undoName, Action<VividParticleSystem> action)
        {
            for (int index = 0; index < targets.Length; index++)
            {
                if (targets[index] is not VividParticleSystem system)
                    continue;

                Undo.RecordObject(system, undoName);
                action(system);
                EditorUtility.SetDirty(system);
            }
        }

        private static void DrawModule(GUIContent label, ref bool expanded, Action draw)
        {
            EditorGUILayout.Space();
            expanded = EditorGUILayout.BeginFoldoutHeaderGroup(expanded, label);
            if (expanded)
            {
                using (new EditorGUI.IndentLevelScope())
                    draw();
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static void DrawMainModule(SerializedProperty module)
        {
            if (module == null)
                return;

            DrawRelative(module, "m_Duration");
            DrawRelative(module, "m_Loop");
            DrawRelative(module, "m_PlayOnAwake");
            DrawRelative(module, "m_StartLifetime");
            DrawRelative(module, "m_StartSpeed");
            DrawRelative(module, "m_StartSize");
            DrawRelative(module, "m_StartColor");
            DrawRelative(module, "m_GravityModifier");
            DrawRelative(module, "m_SimulationSpace");
            DrawRelative(module, "m_MaxParticles");

            SerializedProperty useAutoRandomSeed = VividParticleSystemEditorUtility.FindRelative(module, "m_UseAutoRandomSeed");
            EditorGUILayout.PropertyField(useAutoRandomSeed);
            if (useAutoRandomSeed == null || useAutoRandomSeed.hasMultipleDifferentValues || !useAutoRandomSeed.boolValue)
                DrawRelative(module, "m_RandomSeed");
        }

        private void DrawEmissionModule(SerializedProperty module)
        {
            if (module == null)
                return;

            SerializedProperty enabled = VividParticleSystemEditorUtility.FindRelative(module, "m_Enabled");
            EditorGUILayout.PropertyField(enabled);
            using (new EditorGUI.DisabledScope(enabled != null && !enabled.hasMultipleDifferentValues && !enabled.boolValue))
            {
                DrawRelative(module, "m_RateOverTime");
                DrawBurstList(VividParticleSystemEditorUtility.FindRelative(module, "m_Bursts"));
            }
        }

        private void DrawBurstList(SerializedProperty bursts)
        {
            if (bursts == null)
                return;

            GetBurstList(bursts).DoLayoutList();
        }

        private ReorderableList GetBurstList(SerializedProperty bursts)
        {
            if (m_BurstList != null
                && ReferenceEquals(m_BurstListSerializedObject, bursts.serializedObject)
                && m_BurstListPropertyPath == bursts.propertyPath)
            {
                m_BurstList.serializedProperty = bursts;
                return m_BurstList;
            }

            var list = new ReorderableList(
                bursts.serializedObject,
                bursts,
                draggable: true,
                displayHeader: true,
                displayAddButton: true,
                displayRemoveButton: true)
            {
                elementHeight = EditorGUIUtility.singleLineHeight + 4.0f,
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, s_BurstsLabel),
            };
            list.drawElementCallback = (rect, index, _, _) => DrawBurstElement(list.serializedProperty, rect, index);
            list.onAddCallback = AddBurstElement;

            m_BurstList = list;
            m_BurstListSerializedObject = bursts.serializedObject;
            m_BurstListPropertyPath = bursts.propertyPath;
            return m_BurstList;
        }

        private static void DrawBurstElement(SerializedProperty bursts, Rect rect, int index)
        {
            if (bursts == null || index < 0 || index >= bursts.arraySize)
                return;

            SerializedProperty burst = bursts.GetArrayElementAtIndex(index);
            SerializedProperty time = VividParticleSystemEditorUtility.FindRelative(burst, "m_Time");
            SerializedProperty count = VividParticleSystemEditorUtility.FindRelative(burst, "m_Count");
            if (time == null || count == null)
                return;

            rect.y += 2.0f;
            rect.height = EditorGUIUtility.singleLineHeight;
            float spacing = 6.0f;
            float width = (rect.width - spacing) * 0.5f;
            var timeRect = new Rect(rect.x, rect.y, width, rect.height);
            var countRect = new Rect(rect.x + width + spacing, rect.y, width, rect.height);
            EditorGUI.PropertyField(timeRect, time, s_BurstTimeLabel);
            EditorGUI.PropertyField(countRect, count, s_BurstCountLabel);
        }

        private static void AddBurstElement(ReorderableList list)
        {
            SerializedProperty bursts = list.serializedProperty;
            int index = bursts.arraySize;
            bursts.InsertArrayElementAtIndex(index);
            SerializedProperty burst = bursts.GetArrayElementAtIndex(index);
            VividParticleSystemEditorUtility.TryWriteBurstElement(burst, time: 0.0f, count: 1);
            bursts.serializedObject.ApplyModifiedProperties();
            list.index = index;
        }

        private static void DrawShapeModule(SerializedProperty module)
        {
            if (module == null)
                return;

            SerializedProperty enabled = VividParticleSystemEditorUtility.FindRelative(module, "m_Enabled");
            EditorGUILayout.PropertyField(enabled);
            using (new EditorGUI.DisabledScope(enabled != null && !enabled.hasMultipleDifferentValues && !enabled.boolValue))
            {
                SerializedProperty shapeType = VividParticleSystemEditorUtility.FindRelative(module, "m_ShapeType");
                EditorGUILayout.PropertyField(shapeType);
                VividParticleShapeType resolvedShape = shapeType != null && !shapeType.hasMultipleDifferentValues
                    ? (VividParticleShapeType)shapeType.enumValueIndex
                    : VividParticleShapeType.Cone;

                if (resolvedShape is VividParticleShapeType.Sphere or VividParticleShapeType.Cone)
                    DrawRelative(module, "m_Radius");

                if (resolvedShape == VividParticleShapeType.Box)
                    DrawRelative(module, "m_BoxSize");

                if (resolvedShape == VividParticleShapeType.Cone)
                    DrawRelative(module, "m_Angle");
            }
        }

        private static void DrawRendererModule(SerializedProperty module)
        {
            if (module == null)
                return;

            SerializedProperty enabled = VividParticleSystemEditorUtility.FindRelative(module, "m_Enabled");
            EditorGUILayout.PropertyField(enabled);
            using (new EditorGUI.DisabledScope(enabled != null && !enabled.hasMultipleDifferentValues && !enabled.boolValue))
            {
                SerializedProperty renderMode = VividParticleSystemEditorUtility.FindRelative(module, "m_RenderMode");
                EditorGUILayout.PropertyField(renderMode);
                VividParticleRenderMode resolvedMode = renderMode != null && !renderMode.hasMultipleDifferentValues
                    ? (VividParticleRenderMode)renderMode.enumValueIndex
                    : VividParticleRenderMode.Billboard;

                DrawRelative(module, "m_Material");
                if (resolvedMode == VividParticleRenderMode.Mesh)
                {
                    DrawRelative(module, "m_Mesh");
                    DrawRelative(module, "m_Meshes");
                }

                DrawRelative(module, "m_Color");
                DrawRelative(module, "m_SizeScale");
                DrawRelative(module, "m_Pivot");
                DrawRelative(module, "m_MinParticleSize");
                DrawRelative(module, "m_MaxParticleSize");
                DrawRelative(module, "m_Flip");
                if (resolvedMode == VividParticleRenderMode.Stretch)
                {
                    DrawRelative(module, "m_StretchLengthScale");
                    DrawRelative(module, "m_StretchSpeedScale");
                }

                DrawRelative(module, "m_RenderQueueOffset");
                DrawRelative(module, "m_ShadowCastingMode");
                DrawRelative(module, "m_ReceiveShadows");
                DrawRelative(module, "m_RenderingLayerMask");
                DrawRelative(module, "m_SortMode");

                EditorGUILayout.Space(2.0f);
                EditorGUILayout.LabelField(s_DataLayoutLabel, EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawRelative(module, "m_ColorDataMode");
                    DrawRelative(module, "m_RotationDataMode");
                    DrawRelative(module, "m_VelocityDataMode");
                    DrawRelative(module, "m_SizeDataMode");
                    DrawRelative(module, "m_UVDataEnabled");
                    DrawRelative(module, "m_CustomData1Enabled");
                    DrawRelative(module, "m_CustomData2Enabled");
                    DrawRelative(module, "m_MeshIndexDataEnabled");
                }

                DrawGpuDataLayoutPreview(module);
            }

            DrawRendererNotices(module);
        }

        private static void DrawRendererNotices(SerializedProperty module)
        {
            VividParticleRendererInspectorNotice notices =
                VividParticleSystemEditorUtility.GetRendererInspectorNotices(module);
            if (notices == VividParticleRendererInspectorNotice.None)
                return;

            EditorGUILayout.Space(2.0f);
            if ((notices & VividParticleRendererInspectorNotice.RendererDisabled) != 0)
                EditorGUILayout.HelpBox(RendererDisabledNotice, MessageType.Info);

            if ((notices & VividParticleRendererInspectorNotice.RenderModeNone) != 0)
                EditorGUILayout.HelpBox(RenderModeNoneNotice, MessageType.Info);

            if ((notices & VividParticleRendererInspectorNotice.MeshMissing) != 0)
                EditorGUILayout.HelpBox(MeshMissingNotice, MessageType.Warning);

            if ((notices & VividParticleRendererInspectorNotice.StretchUsesPerParticleVelocity) != 0)
                EditorGUILayout.HelpBox(StretchVelocityNotice, MessageType.Info);

            if ((notices & VividParticleRendererInspectorNotice.MeshIndexRequiresCustomShader) != 0)
                EditorGUILayout.HelpBox(MeshIndexNotice, MessageType.Info);

            if ((notices & VividParticleRendererInspectorNotice.MultiMeshSplitsDrawCommands) != 0)
                EditorGUILayout.HelpBox(MultiMeshNotice, MessageType.Info);

            if ((notices & VividParticleRendererInspectorNotice.PerParticleGpuDataIncreasesUpload) != 0)
                EditorGUILayout.HelpBox(PerParticleGpuDataNotice, MessageType.Info);

            if ((notices & VividParticleRendererInspectorNotice.SortingAllocatesPositions) != 0)
                EditorGUILayout.HelpBox(SortingPositionsNotice, MessageType.Info);
        }

        private static void DrawGpuDataLayoutPreview(SerializedProperty rendererModule)
        {
            EditorGUILayout.Space(2.0f);
            if (!VividParticleSystemEditorUtility.TryCreateGpuDataLayoutDescriptor(
                    rendererModule,
                    out VividParticleSystemManager.VividParticleGpuDataLayoutDescriptor descriptor))
            {
                EditorGUILayout.HelpBox("GPU layout preview is unavailable for mixed renderer values.", MessageType.Info);
                return;
            }

            VividParticleSystemManager.VividParticleGpuDataLayout layout =
                VividParticleSystemManager.VividParticleGpuDataLayout.Create(descriptor);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField(s_LayoutHashLabel, layout.Hash);
                EditorGUILayout.TextField(
                    s_DataPerSharpBitsLabel,
                    VividParticleSystemEditorUtility.FormatGpuDataBits(layout.DataPerSharpBits));
                EditorGUILayout.IntField(s_LayoutColumnCountLabel, layout.Count);
                EditorGUILayout.IntField(s_PerInstanceUploadBytesLabel, layout.PerInstanceElementByteSize);
                EditorGUILayout.TextField(
                    s_PerInstanceUploadMaskLabel,
                    VividParticleSystemEditorUtility.FormatUploadColumnMask(layout.PerInstanceUploadColumnMask));
                EditorGUILayout.TextField(
                    s_PerInstanceRenderJobsLabel,
                    VividParticleSystemEditorUtility.FormatRenderJobModuleFlags(layout.PerInstanceRenderJobFlagMask));
                EditorGUILayout.TextField(
                    s_TransformUploadMaskLabel,
                    VividParticleSystemEditorUtility.FormatUploadColumnMask(layout.TransformRenderJobUploadColumnMask));
                EditorGUILayout.TextField(
                    s_ColorUploadMaskLabel,
                    VividParticleSystemEditorUtility.FormatUploadColumnMask(layout.ColorRenderJobUploadColumnMask));
                EditorGUILayout.TextField(
                    s_VelocityUploadMaskLabel,
                    VividParticleSystemEditorUtility.FormatUploadColumnMask(layout.VelocityStretchRenderJobUploadColumnMask));
                EditorGUILayout.TextField(
                    s_ExtraUploadMaskLabel,
                    VividParticleSystemEditorUtility.FormatUploadColumnMask(layout.ExtraDataRenderJobUploadColumnMask));
                EditorGUILayout.TextField(
                    s_UVUploadMaskLabel,
                    VividParticleSystemEditorUtility.FormatUploadColumnMask(layout.UVRenderJobUploadColumnMask));
                EditorGUILayout.TextField(
                    s_CustomDataUploadMaskLabel,
                    VividParticleSystemEditorUtility.FormatUploadColumnMask(layout.CustomDataRenderJobUploadColumnMask));
                EditorGUILayout.TextField(
                    s_MeshIndexUploadMaskLabel,
                    VividParticleSystemEditorUtility.FormatUploadColumnMask(layout.MeshIndexRenderJobUploadColumnMask));
                if (VividParticleSystemEditorUtility.TryCreateGpuLayoutFootprint(
                        rendererModule,
                        out VividParticleGpuLayoutFootprint footprint))
                {
                    EditorGUILayout.IntField(s_InstanceCapacityLabel, footprint.InstanceCapacity);
                    EditorGUILayout.IntField(s_SharpCapacityLabel, footprint.SharpCapacity);
                    EditorGUILayout.IntField(s_SpanCapacityLabel, footprint.SpanCapacity);
                    EditorGUILayout.TextField(
                        s_EstimatedBufferBytesLabel,
                        VividParticleSystemEditorUtility.FormatByteSize(footprint.TotalByteSize));
                }

                for (int index = 0; index < layout.Count; index++)
                {
                    VividParticleSystemManager.VividParticleGpuDataInfo dataInfo = layout[index];
                    EditorGUILayout.LabelField(
                        dataInfo.DataId.ToString(),
                        VividParticleSystemEditorUtility.FormatGpuDataInfo(dataInfo));
                }
            }
        }

        private static void DrawRelative(
            SerializedProperty module,
            string relativePath,
            bool includeChildren = false)
        {
            SerializedProperty property = VividParticleSystemEditorUtility.FindRelative(module, relativePath);
            if (property != null)
                EditorGUILayout.PropertyField(property, includeChildren);
        }
    }

    [CustomEditor(typeof(VividParticleSystem))]
    [CanEditMultipleObjects]
    internal sealed class VividParticleSystemEditor : VividParticleSystemEditorBase
    {
        private static readonly GUIContent s_AssetLabel = EditorGUIUtility.TrTextContent("Asset");
        private static readonly GUIContent s_ApplyTemplateLabel = EditorGUIUtility.TrTextContent("Apply Template");
        private static readonly GUIContent s_SaveTemplateLabel = EditorGUIUtility.TrTextContent("Save Template...");
        private static readonly Color s_ShapeHandleColor = new(0.24f, 0.72f, 1.0f, 0.9f);
        private static readonly Color s_ShapeHandleColorBehind = new(0.24f, 0.72f, 1.0f, 0.25f);
        private SerializedProperty m_Asset;
        private SerializedProperty m_Shape;

        protected override void OnEnable()
        {
            base.OnEnable();
            m_Asset = VividParticleSystemEditorUtility.FindAssetProperty(serializedObject);
            m_Shape = serializedObject.FindProperty("m_Shape");
        }

        protected override void DrawHeaderInspector()
        {
            DrawAssetTemplateField();
        }

        protected override void DrawFooterInspector()
        {
            DrawRuntimeControlsAndStats();
        }

        private void DrawAssetTemplateField()
        {
            if (m_Asset == null)
                return;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = m_Asset.hasMultipleDifferentValues;
                var nextAsset = (VividParticleSystemAsset)EditorGUILayout.ObjectField(
                    s_AssetLabel,
                    m_Asset.objectReferenceValue,
                    typeof(VividParticleSystemAsset),
                    allowSceneObjects: false);
                EditorGUI.showMixedValue = false;
                if (EditorGUI.EndChangeCheck())
                {
                    ApplyAssetTemplateToTargets(nextAsset, force: false);
                    serializedObject.Update();
                    m_Asset = VividParticleSystemEditorUtility.FindAssetProperty(serializedObject);
                }

                using (new EditorGUI.DisabledScope(m_Asset.hasMultipleDifferentValues || m_Asset.objectReferenceValue == null))
                {
                    if (GUILayout.Button(s_ApplyTemplateLabel, GUILayout.Width(110.0f)))
                    {
                        ApplyAssetTemplateToTargets((VividParticleSystemAsset)m_Asset.objectReferenceValue, force: true);
                        serializedObject.Update();
                        m_Asset = VividParticleSystemEditorUtility.FindAssetProperty(serializedObject);
                    }
                }

                using (new EditorGUI.DisabledScope(targets.Length != 1 || target is not VividParticleSystem))
                {
                    if (GUILayout.Button(s_SaveTemplateLabel, GUILayout.Width(112.0f)))
                        SaveTemplateFromTarget();
                }
            }
        }

        private void SaveTemplateFromTarget()
        {
            if (target is not VividParticleSystem system || system == null)
                return;

            string path = EditorUtility.SaveFilePanelInProject(
                "Save Vivid Particle Template",
                "New Vivid Particle System",
                "asset",
                "Save current Vivid Particle System settings as a template asset.");
            if (string.IsNullOrEmpty(path))
                return;

            VividParticleSystemAsset asset =
                VividParticleSystemEditorUtility.CreateAssetTemplateFromComponent(
                    system,
                    path,
                    assignToSystem: true);
            if (asset == null)
                return;

            EditorGUIUtility.PingObject(asset);
            serializedObject.Update();
            m_Asset = VividParticleSystemEditorUtility.FindAssetProperty(serializedObject);
        }

        private void ApplyAssetTemplateToTargets(
            VividParticleSystemAsset asset,
            bool force)
        {
            for (int index = 0; index < targets.Length; index++)
            {
                if (targets[index] is VividParticleSystem system)
                {
                    VividParticleSystemEditorUtility.ApplyAssetTemplate(
                        system,
                        asset,
                        "Apply Vivid Particle Template",
                        force);
                }
            }
        }

        private void OnSceneGUI()
        {
            if (targets.Length != 1 || target is not VividParticleSystem system || system == null)
                return;

            serializedObject.Update();
            if (!VividParticleSystemEditorUtility.TryReadShapeSceneData(m_Shape, out VividParticleShapeSceneData data)
                || !data.Enabled
                || data.ShapeType == VividParticleShapeType.Point)
            {
                return;
            }

            VividParticleShapeSceneData editedData = data;
            EditorGUI.BeginChangeCheck();
            DrawShapeSceneHandles(system.transform, ref editedData);
            if (!EditorGUI.EndChangeCheck())
                return;

            Undo.RecordObject(system, "Edit Vivid Particle Shape");
            VividParticleSystemEditorUtility.WriteShapeSceneData(m_Shape, editedData);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(system);
        }

        private static void DrawShapeSceneHandles(
            Transform ownerTransform,
            ref VividParticleShapeSceneData data)
        {
            Matrix4x4 matrix = ownerTransform != null
                ? ownerTransform.localToWorldMatrix
                : Matrix4x4.identity;

            CompareFunction previousZTest = Handles.zTest;
            Color previousColor = Handles.color;
            using (new Handles.DrawingScope(s_ShapeHandleColor, matrix))
            {
                Handles.zTest = CompareFunction.Greater;
                Handles.color = s_ShapeHandleColorBehind;
                DrawShapeWire(data);

                Handles.zTest = CompareFunction.LessEqual;
                Handles.color = s_ShapeHandleColor;
                DrawShapeWire(data);
                DrawShapeEditHandles(ref data);
            }

            Handles.color = previousColor;
            Handles.zTest = previousZTest;
        }

        private static void DrawShapeWire(VividParticleShapeSceneData data)
        {
            switch (data.ShapeType)
            {
                case VividParticleShapeType.Sphere:
                    Handles.DrawWireDisc(Vector3.zero, Vector3.right, data.Radius);
                    Handles.DrawWireDisc(Vector3.zero, Vector3.up, data.Radius);
                    Handles.DrawWireDisc(Vector3.zero, Vector3.forward, data.Radius);
                    break;
                case VividParticleShapeType.Box:
                    Handles.DrawWireCube(Vector3.zero, data.BoxSize);
                    break;
                case VividParticleShapeType.Cone:
                    DrawConeWire(data.Radius, data.Angle);
                    break;
            }
        }

        private static void DrawShapeEditHandles(ref VividParticleShapeSceneData data)
        {
            switch (data.ShapeType)
            {
                case VividParticleShapeType.Sphere:
                    data.Radius = VividParticleSystemEditorUtility.ClampShapeRadius(
                        Handles.RadiusHandle(Quaternion.identity, Vector3.zero, data.Radius));
                    break;
                case VividParticleShapeType.Box:
                    float handleSize = HandleUtility.GetHandleSize(Vector3.zero);
                    data.BoxSize = VividParticleSystemEditorUtility.ClampShapeBoxSize(
                        Handles.ScaleHandle(data.BoxSize, Vector3.zero, Quaternion.identity, handleSize));
                    break;
                case VividParticleShapeType.Cone:
                    data.Radius = VividParticleSystemEditorUtility.ClampShapeRadius(
                        Handles.RadiusHandle(Quaternion.identity, Vector3.zero, data.Radius));
                    Vector3 angleHandlePosition = VividParticleSystemEditorUtility.GetConeAngleHandlePosition(
                        data.Radius,
                        data.Angle);
                    float coneHandleSize = HandleUtility.GetHandleSize(angleHandlePosition) * 0.08f;
                    Vector3 movedAngleHandlePosition = Handles.Slider2D(
                        angleHandlePosition,
                        Vector3.forward,
                        Vector3.right,
                        Vector3.forward,
                        coneHandleSize,
                        Handles.DotHandleCap,
                        Vector2.zero);
                    Handles.DrawLine(Vector3.zero, movedAngleHandlePosition);
                    data.Angle = VividParticleSystemEditorUtility.GetConeAngleFromHandlePosition(movedAngleHandlePosition);
                    break;
            }
        }

        private static void DrawConeWire(float radius, float angle)
        {
            radius = VividParticleSystemEditorUtility.ClampShapeRadius(radius);
            float length = VividParticleSystemEditorUtility.GetConePreviewLength(radius, angle);
            float endRadius = Mathf.Tan(Mathf.Deg2Rad * VividParticleSystemEditorUtility.ClampShapeAngle(angle)) * length;
            Handles.DrawWireDisc(Vector3.zero, Vector3.forward, radius);
            Handles.DrawWireDisc(Vector3.forward * length, Vector3.forward, endRadius);

            Vector3[] directions =
            {
                Vector3.right,
                Vector3.left,
                Vector3.up,
                Vector3.down,
            };

            for (int index = 0; index < directions.Length; index++)
                Handles.DrawLine(Vector3.zero, Vector3.forward * length + directions[index] * endRadius);
        }
    }

    [CustomEditor(typeof(VividParticleSystemAsset))]
    [CanEditMultipleObjects]
    internal sealed class VividParticleSystemAssetEditor : VividParticleSystemEditorBase
    {
    }
}

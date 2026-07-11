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
        ShadowsOnlySkipsRegularViews = 1 << 8,
        MotionVectorsAffectDrawOutput = 1 << 9,
        StaticShadowCasterAffectsDrawOutput = 1 << 10,
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
            new(VividParticleSystemManager.RenderJobUVUploadFlag, "UV"),
            new(VividParticleSystemManager.RenderJobCustomDataUploadFlag, "CustomData"),
            new(VividParticleSystemManager.RenderJobMeshIndexUploadFlag, "MeshIndex"),
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

        internal static SerializedProperty FindForceOverLifetimeProperty(SerializedObject serializedObject)
        {
            return serializedObject?.FindProperty("m_ForceOverLifetime");
        }

        internal static SerializedProperty FindColorOverLifetimeProperty(SerializedObject serializedObject)
        {
            return serializedObject?.FindProperty("m_ColorOverLifetime");
        }

        internal static SerializedProperty FindColorBySpeedProperty(SerializedObject serializedObject)
        {
            return serializedObject?.FindProperty("m_ColorBySpeed");
        }

        internal static SerializedProperty FindSizeOverLifetimeProperty(SerializedObject serializedObject)
        {
            return serializedObject?.FindProperty("m_SizeOverLifetime");
        }

        internal static SerializedProperty FindSizeBySpeedProperty(SerializedObject serializedObject)
        {
            return serializedObject?.FindProperty("m_SizeBySpeed");
        }

        internal static SerializedProperty FindRotationOverLifetimeProperty(SerializedObject serializedObject)
        {
            return serializedObject?.FindProperty("m_RotationOverLifetime");
        }

        internal static SerializedProperty FindRotationBySpeedProperty(SerializedObject serializedObject)
        {
            return serializedObject?.FindProperty("m_RotationBySpeed");
        }

        internal static SerializedProperty FindNoiseProperty(SerializedObject serializedObject)
        {
            return serializedObject?.FindProperty("m_Noise");
        }

        internal static SerializedProperty FindCustomDataProperty(SerializedObject serializedObject)
        {
            return serializedObject?.FindProperty("m_CustomData");
        }

        internal static SerializedProperty FindVelocityOverLifetimeProperty(SerializedObject serializedObject)
        {
            return serializedObject?.FindProperty("m_VelocityOverLifetime");
        }

        internal static SerializedProperty FindInheritVelocityProperty(SerializedObject serializedObject)
        {
            return serializedObject?.FindProperty("m_InheritVelocity");
        }

        internal static SerializedProperty FindLimitVelocityOverLifetimeProperty(SerializedObject serializedObject)
        {
            return serializedObject?.FindProperty("m_LimitVelocityOverLifetime");
        }

        internal static SerializedProperty FindTextureSheetAnimationProperty(SerializedObject serializedObject)
        {
            return serializedObject?.FindProperty("m_TextureSheetAnimation");
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

        internal static bool ScrubPreview(VividParticleSystem system, float time)
        {
            if (system == null)
                return false;

            system.Pause(withChildren: false);
            system.Simulate(
                Mathf.Max(0.0f, time),
                withChildren: false,
                restart: true,
                fixedTimeStep: true);
            return true;
        }

        internal static bool RestartPreview(VividParticleSystem system, bool play)
        {
            if (system == null)
                return false;

            system.Stop(
                withChildren: false,
                VividParticleSystemStopBehavior.StopEmittingAndClear);
            if (play)
                system.Play(withChildren: false);

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
            asset.forceOverLifetime.CopyFrom(system.forceOverLifetime);
            asset.velocityOverLifetime.CopyFrom(system.velocityOverLifetime);
            asset.inheritVelocity.CopyFrom(system.inheritVelocity);
            asset.limitVelocityOverLifetime.CopyFrom(system.limitVelocityOverLifetime);
            asset.colorOverLifetime.CopyFrom(system.colorOverLifetime);
            asset.colorBySpeed.CopyFrom(system.colorBySpeed);
            asset.sizeOverLifetime.CopyFrom(system.sizeOverLifetime);
            asset.sizeBySpeed.CopyFrom(system.sizeBySpeed);
            asset.rotationOverLifetime.CopyFrom(system.rotationOverLifetime);
            asset.rotationBySpeed.CopyFrom(system.rotationBySpeed);
            asset.noise.CopyFrom(system.noise);
            asset.customData.CopyFrom(system.customData);
            asset.textureSheetAnimation.CopyFrom(system.textureSheetAnimation);
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
            SerializedProperty colorOverLifetimeEnabled = renderer.serializedObject?
                .FindProperty("m_ColorOverLifetime.m_Enabled");
            SerializedProperty colorBySpeedEnabled = renderer.serializedObject?
                .FindProperty("m_ColorBySpeed.m_Enabled");
            SerializedProperty sizeOverLifetimeEnabled = renderer.serializedObject?
                .FindProperty("m_SizeOverLifetime.m_Enabled");
            SerializedProperty sizeBySpeedEnabled = renderer.serializedObject?
                .FindProperty("m_SizeBySpeed.m_Enabled");
            SerializedProperty rotationOverLifetimeEnabled = renderer.serializedObject?
                .FindProperty("m_RotationOverLifetime.m_Enabled");
            SerializedProperty rotationBySpeedEnabled = renderer.serializedObject?
                .FindProperty("m_RotationBySpeed.m_Enabled");
            SerializedProperty velocityOverLifetimeEnabled = renderer.serializedObject?
                .FindProperty("m_VelocityOverLifetime.m_Enabled");
            SerializedProperty inheritVelocityEnabled = renderer.serializedObject?
                .FindProperty("m_InheritVelocity.m_Enabled");
            SerializedProperty textureSheetAnimationEnabled = renderer.serializedObject?
                .FindProperty("m_TextureSheetAnimation.m_Enabled");
            SerializedProperty customDataMode1 = renderer.serializedObject?
                .FindProperty("m_CustomData.m_Mode1");
            SerializedProperty customDataMode2 = renderer.serializedObject?
                .FindProperty("m_CustomData.m_Mode2");
            if (HasMissingOrMixedValue(
                    renderMode,
                    colorDataMode,
                    rotationDataMode,
                    velocityDataMode,
                    sizeDataMode,
                    uvDataEnabled,
                    customData1Enabled,
                    customData2Enabled,
                    meshIndexDataEnabled,
                    colorOverLifetimeEnabled,
                    colorBySpeedEnabled,
                    sizeOverLifetimeEnabled,
                    sizeBySpeedEnabled,
                    rotationOverLifetimeEnabled,
                    rotationBySpeedEnabled,
                    velocityOverLifetimeEnabled,
                    inheritVelocityEnabled,
                    textureSheetAnimationEnabled,
                    customDataMode1,
                    customDataMode2))
            {
                return false;
            }

            descriptor = new VividParticleSystemManager.VividParticleGpuDataLayoutDescriptor(
                (VividParticleRenderMode)renderMode.enumValueIndex,
                colorOverLifetimeEnabled.boolValue || colorBySpeedEnabled.boolValue
                    ? VividParticleGpuDataMode.PerParticle
                    : (VividParticleGpuDataMode)colorDataMode.enumValueIndex,
                rotationOverLifetimeEnabled.boolValue || rotationBySpeedEnabled.boolValue
                    ? VividParticleGpuDataMode.PerParticle
                    : (VividParticleGpuDataMode)rotationDataMode.enumValueIndex,
                velocityOverLifetimeEnabled.boolValue || inheritVelocityEnabled.boolValue
                    ? VividParticleGpuDataMode.PerParticle
                    : (VividParticleGpuDataMode)velocityDataMode.enumValueIndex,
                sizeOverLifetimeEnabled.boolValue || sizeBySpeedEnabled.boolValue
                    ? VividParticleGpuDataMode.PerParticle
                    : (VividParticleGpuDataMode)sizeDataMode.enumValueIndex,
                textureSheetAnimationEnabled.boolValue
                    ? VividParticleGpuDataMode.PerParticle
                    : VividParticleGpuDataMode.Shared,
                uvDataEnabled.boolValue || textureSheetAnimationEnabled.boolValue,
                customData1Enabled.boolValue
                    || customDataMode1.enumValueIndex != (int)VividParticleCustomDataMode.Disabled,
                customData2Enabled.boolValue
                    || customDataMode2.enumValueIndex != (int)VividParticleCustomDataMode.Disabled,
                meshIndexDataEnabled.boolValue,
                customDataMode1.enumValueIndex != (int)VividParticleCustomDataMode.Disabled
                    ? VividParticleGpuDataMode.PerParticle
                    : VividParticleGpuDataMode.Shared,
                customDataMode2.enumValueIndex != (int)VividParticleCustomDataMode.Disabled
                    ? VividParticleGpuDataMode.PerParticle
                    : VividParticleGpuDataMode.Shared);
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

        internal static string FormatPerformanceSummary(
            VividParticleSystemManager.VividParticleSystemRuntimeStats runtimeStats,
            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Particles {0}, Storage {1}/{2} pages, PendingSim {3}, EmitInit {4} (inline {5}, job {6}), Upload {7}, CopyOps {8}/{9}, Sorts {10}, RenderJobs {11}, Draw {12}/{13}, Cull {14}/{15}->{16}/{17}, Bounds {18}/{19}, CullingBuild {20}/{21}, MeshCount {22}/{23}, Reduce {24}/{25}, PickBuild {26}, BatchBuild {27}, Filter {28}/{29}, Scratch {30}/{31} ({32}/{33})",
                runtimeStats.ParticleCount,
                runtimeStats.StorageCapacity,
                runtimeStats.StoragePageCount,
                runtimeStats.HasPendingSimulation ? 1 : 0,
                runtimeStats.LastEmissionInitializeWorkCount,
                runtimeStats.LastEmissionInitializeInlineWorkCount,
                runtimeStats.LastEmissionInitializeScheduledWorkCount,
                FormatByteSize(rendererStats.LastCopyByteCount),
                rendererStats.LastCopyOperationCount,
                rendererStats.LastUploadCopyWorkCount,
                rendererStats.LastUploadCopySortCount,
                rendererStats.LastRenderPageJobModuleCount,
                rendererStats.DrawCommandCount,
                rendererStats.VisibleInstanceCapacity,
                rendererStats.LastCullingSourceDrawCommandCount,
                rendererStats.LastCullingSourceVisibleInstanceCount,
                rendererStats.LastCullingFilteredDrawCommandCount,
                rendererStats.LastCullingFilteredVisibleInstanceCount,
                rendererStats.LastBoundsPageWorkCount,
                rendererStats.CullingPageBoundsCapacity,
                rendererStats.LastCullingRecordBuildWorkCount,
                rendererStats.HasPendingCullingRecordBuild ? 1 : 0,
                rendererStats.LastMeshVisibleCountInlineWorkCount,
                rendererStats.LastMeshVisibleCountScheduledWorkCount,
                rendererStats.LastMeshVisibleBatchReduceWorkCount,
                rendererStats.MeshBatchVisibleCountOutputCount,
                rendererStats.LastPickingDrawBuildWorkCount,
                rendererStats.LastBatchDrawBuildWorkCount,
                rendererStats.LastCullingFilterPassCount,
                rendererStats.LastCullingFilterCommandCount,
                rendererStats.CullingScratchSlotCount,
                rendererStats.ActiveCullingScratchSlotCount,
                rendererStats.LastCullingScratchSplitCount,
                rendererStats.LastCullingScratchPacketCount);
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
            SerializedProperty meshIndexDataEnabled = FindRelative(renderer, "m_MeshIndexDataEnabled");
            SerializedProperty sortMode = FindRelative(renderer, "m_SortMode");
            SerializedProperty shadowCastingMode = FindRelative(renderer, "m_ShadowCastingMode");
            SerializedProperty motionVectorGenerationMode =
                FindRelative(renderer, "m_MotionVectorGenerationMode");
            SerializedProperty staticShadowCaster = FindRelative(renderer, "m_StaticShadowCaster");

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
                    meshIndexDataEnabled))
            {
                notices |= VividParticleRendererInspectorNotice.PerParticleGpuDataIncreasesUpload;
            }

            if (sortMode != null
                && !sortMode.hasMultipleDifferentValues
                && (VividParticleSortMode)sortMode.enumValueIndex != VividParticleSortMode.None)
            {
                notices |= VividParticleRendererInspectorNotice.SortingAllocatesPositions;
            }

            if (shadowCastingMode != null
                && !shadowCastingMode.hasMultipleDifferentValues
                && (ShadowCastingMode)shadowCastingMode.enumValueIndex == ShadowCastingMode.ShadowsOnly)
            {
                notices |= VividParticleRendererInspectorNotice.ShadowsOnlySkipsRegularViews;
            }

            if (motionVectorGenerationMode != null
                && !motionVectorGenerationMode.hasMultipleDifferentValues
                && (MotionVectorGenerationMode)motionVectorGenerationMode.enumValueIndex
                    != MotionVectorGenerationMode.ForceNoMotion)
            {
                notices |= VividParticleRendererInspectorNotice.MotionVectorsAffectDrawOutput;
            }

            if (staticShadowCaster != null
                && !staticShadowCaster.hasMultipleDifferentValues
                && staticShadowCaster.boolValue)
            {
                notices |= VividParticleRendererInspectorNotice.StaticShadowCasterAffectsDrawOutput;
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
        private static readonly GUIContent s_ForceOverLifetimeLabel =
            EditorGUIUtility.TrTextContent("Force over Lifetime");
        private static readonly GUIContent s_ColorOverLifetimeLabel =
            EditorGUIUtility.TrTextContent("Color over Lifetime");
        private static readonly GUIContent s_ColorBySpeedLabel =
            EditorGUIUtility.TrTextContent("Color by Speed");
        private static readonly GUIContent s_SizeOverLifetimeLabel =
            EditorGUIUtility.TrTextContent("Size over Lifetime");
        private static readonly GUIContent s_SizeBySpeedLabel =
            EditorGUIUtility.TrTextContent("Size by Speed");
        private static readonly GUIContent s_RotationOverLifetimeLabel =
            EditorGUIUtility.TrTextContent("Rotation over Lifetime");
        private static readonly GUIContent s_RotationBySpeedLabel =
            EditorGUIUtility.TrTextContent("Rotation by Speed");
        private static readonly GUIContent s_NoiseLabel = EditorGUIUtility.TrTextContent("Noise");
        private static readonly GUIContent s_VelocityOverLifetimeLabel =
            EditorGUIUtility.TrTextContent("Velocity over Lifetime");
        private static readonly GUIContent s_InheritVelocityLabel =
            EditorGUIUtility.TrTextContent("Inherit Velocity");
        private static readonly GUIContent s_LimitVelocityOverLifetimeLabel =
            EditorGUIUtility.TrTextContent("Limit Velocity over Lifetime");
        private static readonly GUIContent s_TextureSheetAnimationLabel =
            EditorGUIUtility.TrTextContent("Texture Sheet Animation");
        private static readonly GUIContent s_CustomDataLabel =
            EditorGUIUtility.TrTextContent("Custom Data");
        private static readonly GUIContent s_RendererLabel = EditorGUIUtility.TrTextContent("Renderer");
        private static readonly GUIContent s_DebugLabel = EditorGUIUtility.TrTextContent("Debug");
        private static readonly GUIContent s_DataLayoutLabel = EditorGUIUtility.TrTextContent("GPU Data Layout");
        private static readonly GUIContent s_PlayLabel = EditorGUIUtility.TrTextContent("Play");
        private static readonly GUIContent s_PauseLabel = EditorGUIUtility.TrTextContent("Pause");
        private static readonly GUIContent s_StopLabel = EditorGUIUtility.TrTextContent("Stop");
        private static readonly GUIContent s_ClearLabel = EditorGUIUtility.TrTextContent("Clear");
        private static readonly GUIContent s_RestartLabel = EditorGUIUtility.TrTextContent("Restart");
        private static readonly GUIContent s_EmitLabel = EditorGUIUtility.TrTextContent("Emit");
        private static readonly GUIContent s_EmitCountLabel = EditorGUIUtility.TrTextContent("Emit Count");
        private static readonly GUIContent s_PreviewTimeLabel = EditorGUIUtility.TrTextContent("Preview Time");
        private static readonly GUIContent s_BurstsLabel = EditorGUIUtility.TrTextContent("Bursts");
        private static readonly GUIContent s_BurstTimeLabel = EditorGUIUtility.TrTextContent("Time");
        private static readonly GUIContent s_BurstCountLabel = EditorGUIUtility.TrTextContent("Count");
        private static readonly GUIContent s_PerformanceSummaryLabel =
            EditorGUIUtility.TrTextContent("Perf Summary");
        private static readonly GUIContent s_ParticleCountLabel = EditorGUIUtility.TrTextContent("Particle Count");
        private static readonly GUIContent s_TimeLabel = EditorGUIUtility.TrTextContent("Time");
        private static readonly GUIContent s_PageSizeLabel = EditorGUIUtility.TrTextContent("Page Size");
        private static readonly GUIContent s_StorageCapacityLabel = EditorGUIUtility.TrTextContent("Storage Capacity");
        private static readonly GUIContent s_StoragePageCountLabel = EditorGUIUtility.TrTextContent("Storage Pages");
        private static readonly GUIContent s_PendingSimulationLabel = EditorGUIUtility.TrTextContent("Pending Simulation");
        private static readonly GUIContent s_ActiveSimulationQueryLinesLabel =
            EditorGUIUtility.TrTextContent("Active Simulation Query Lines");
        private static readonly GUIContent s_InvalidActiveSimulationQueryLinesLabel =
            EditorGUIUtility.TrTextContent("Invalid Simulation Query Lines");
        private static readonly GUIContent s_SimulationModuleGroupsLabel =
            EditorGUIUtility.TrTextContent("Simulation Module Groups");
        private static readonly GUIContent s_SimulationModuleGroupCacheBuildsLabel =
            EditorGUIUtility.TrTextContent("Module Group Cache Builds");
        private static readonly GUIContent s_SimulationModuleGroupCacheHitsLabel =
            EditorGUIUtility.TrTextContent("Module Group Cache Hits");
        private static readonly GUIContent s_SimulationModuleGroupSourceScansLabel =
            EditorGUIUtility.TrTextContent("Module Group Source Scans");
        private static readonly GUIContent s_BaseSimulationPageWorksLabel =
            EditorGUIUtility.TrTextContent("Base Simulation Page Works");
        private static readonly GUIContent s_VelocitySimulationPageWorksLabel =
            EditorGUIUtility.TrTextContent("Velocity Simulation Page Works");
        private static readonly GUIContent s_PendingSimulationSystemsLabel =
            EditorGUIUtility.TrTextContent("Pending Simulation Systems");
        private static readonly GUIContent s_NativeSimulationConfigsLabel =
            EditorGUIUtility.TrTextContent("Native Simulation Configs");
        private static readonly GUIContent s_NativeRenderModuleConfigsLabel =
            EditorGUIUtility.TrTextContent("Native Render Module Configs");
        private static readonly GUIContent s_NativeSimulationBurstsLabel =
            EditorGUIUtility.TrTextContent("Packed Simulation Bursts");
        private static readonly GUIContent s_SimulationPrepareInlineLabel =
            EditorGUIUtility.TrTextContent("Simulation Prepare Inline");
        private static readonly GUIContent s_SimulationPrepareScheduledLabel =
            EditorGUIUtility.TrTextContent("Simulation Prepare Scheduled");
        private static readonly GUIContent s_NativeSimulationConfigUpdatesLabel =
            EditorGUIUtility.TrTextContent("Simulation Config Updates");
        private static readonly GUIContent s_NativeSimulationBurstRebuildsLabel =
            EditorGUIUtility.TrTextContent("Simulation Burst Rebuilds");
        private static readonly GUIContent s_EmissionPlanWorksLabel =
            EditorGUIUtility.TrTextContent("Emission Plan Works");
        private static readonly GUIContent s_EmissionPlanFallbacksLabel =
            EditorGUIUtility.TrTextContent("Emission Plan Fallbacks");
        private static readonly GUIContent s_EmissionPlanReservationsLabel =
            EditorGUIUtility.TrTextContent("Native Emission Reservations");
        private static readonly GUIContent s_EmissionPlanReservedParticlesLabel =
            EditorGUIUtility.TrTextContent("Reserved Emission Particles");
        private static readonly GUIContent s_EmissionInitializeWorksLabel =
            EditorGUIUtility.TrTextContent("Emission Init Works");
        private static readonly GUIContent s_EmissionInitializeInlineWorksLabel =
            EditorGUIUtility.TrTextContent("Emission Init Inline Works");
        private static readonly GUIContent s_EmissionInitializeScheduledWorksLabel =
            EditorGUIUtility.TrTextContent("Emission Init Scheduled Works");
        private static readonly GUIContent s_EmissionInitializePageWorksLabel =
            EditorGUIUtility.TrTextContent("Emission Init Page Works");
        private static readonly GUIContent s_RenderRecordsLabel = EditorGUIUtility.TrTextContent("Render Records");
        private static readonly GUIContent s_RenderRecordPoolLabel = EditorGUIUtility.TrTextContent("Render Record Pool");
        private static readonly GUIContent s_ReusedRenderRecordsLabel = EditorGUIUtility.TrTextContent("Reused Render Records");
        private static readonly GUIContent s_CreatedRenderRecordsLabel = EditorGUIUtility.TrTextContent("Created Render Records");
        private static readonly GUIContent s_LineGroupsLabel = EditorGUIUtility.TrTextContent("Line Groups");
        private static readonly GUIContent s_LineGroupPoolLabel = EditorGUIUtility.TrTextContent("Line Group Pool");
        private static readonly GUIContent s_ReusedLineGroupsLabel = EditorGUIUtility.TrTextContent("Reused Line Groups");
        private static readonly GUIContent s_CreatedLineGroupsLabel = EditorGUIUtility.TrTextContent("Created Line Groups");
        private static readonly GUIContent s_EcsRendererQueryCreatedLabel =
            EditorGUIUtility.TrTextContent("ECS Renderer Query Created");
        private static readonly GUIContent s_EcsRendererQueryReusedLabel =
            EditorGUIUtility.TrTextContent("ECS Renderer Query Reused");
        private static readonly GUIContent s_EcsRendererQueryCacheBuildsLabel =
            EditorGUIUtility.TrTextContent("ECS Query Cache Builds");
        private static readonly GUIContent s_EcsRendererQueryCacheHitsLabel =
            EditorGUIUtility.TrTextContent("ECS Query Cache Hits");
        private static readonly GUIContent s_EcsRendererQuerySourceScansLabel =
            EditorGUIUtility.TrTextContent("ECS Query Source Scans");
        private static readonly GUIContent s_EcsRendererQueryCachedLinesLabel =
            EditorGUIUtility.TrTextContent("ECS Query Cached Lines");
        private static readonly GUIContent s_EcsRendererLineGroupCacheBuildsLabel =
            EditorGUIUtility.TrTextContent("ECS Line Group Cache Builds");
        private static readonly GUIContent s_EcsRendererLineGroupCacheHitsLabel =
            EditorGUIUtility.TrTextContent("ECS Line Group Cache Hits");
        private static readonly GUIContent s_EcsRendererLineGroupSourceScansLabel =
            EditorGUIUtility.TrTextContent("ECS Line Group Source Scans");
        private static readonly GUIContent s_EcsLineGroupsLabel = EditorGUIUtility.TrTextContent("ECS Line Groups");
        private static readonly GUIContent s_EcsLinesLabel =
            EditorGUIUtility.TrTextContent("ECS Active Renderer Lines");
        private static readonly GUIContent s_EcsMatchedLinesLabel = EditorGUIUtility.TrTextContent("ECS Matched Lines");
        private static readonly GUIContent s_EcsSkippedLinesLabel = EditorGUIUtility.TrTextContent("ECS Skipped Lines");
        private static readonly GUIContent s_ActiveRendererQueryLinesLabel =
            EditorGUIUtility.TrTextContent("Active Renderer Query Lines");
        private static readonly GUIContent s_InvalidActiveRendererQueryLinesLabel =
            EditorGUIUtility.TrTextContent("Invalid Active Renderer Lines");
        private static readonly GUIContent s_RendererRecordRefsLabel =
            EditorGUIUtility.TrTextContent("Native Renderer Record Refs");
        private static readonly GUIContent s_InvalidRendererRecordRefsLabel =
            EditorGUIUtility.TrTextContent("Invalid Renderer Record Refs");
        private static readonly GUIContent s_DrawBatchesLabel = EditorGUIUtility.TrTextContent("Draw Batches");
        private static readonly GUIContent s_DrawBatchPoolLabel = EditorGUIUtility.TrTextContent("Draw Batch Pool");
        private static readonly GUIContent s_ReusedDrawBatchesLabel = EditorGUIUtility.TrTextContent("Reused Draw Batches");
        private static readonly GUIContent s_CreatedDrawBatchesLabel = EditorGUIUtility.TrTextContent("Created Draw Batches");
        private static readonly GUIContent s_CullingRecordsLabel = EditorGUIUtility.TrTextContent("Culling Records");
        private static readonly GUIContent s_CullingPageBoundsCapacityLabel =
            EditorGUIUtility.TrTextContent("Culling Page Bounds Capacity");
        private static readonly GUIContent s_CullingRecordBuildWorksLabel =
            EditorGUIUtility.TrTextContent("Culling Record Build Works");
        private static readonly GUIContent s_PendingCullingRecordBuildLabel =
            EditorGUIUtility.TrTextContent("Pending Culling Record Build");
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
        private static readonly GUIContent s_LastCullingViewTypeLabel =
            EditorGUIUtility.TrTextContent("Last Culling View");
        private static readonly GUIContent s_LastCullingSourceCommandsLabel =
            EditorGUIUtility.TrTextContent("Last Source Commands");
        private static readonly GUIContent s_LastCullingSourceRangesLabel =
            EditorGUIUtility.TrTextContent("Last Source Ranges");
        private static readonly GUIContent s_LastCullingSourceVisibleLabel =
            EditorGUIUtility.TrTextContent("Last Source Visible");
        private static readonly GUIContent s_LastCullingSourceSortingLabel =
            EditorGUIUtility.TrTextContent("Last Source Sorting");
        private static readonly GUIContent s_LastCullingFilteredCommandsLabel =
            EditorGUIUtility.TrTextContent("Last Filtered Commands");
        private static readonly GUIContent s_LastCullingFilteredRangesLabel =
            EditorGUIUtility.TrTextContent("Last Filtered Ranges");
        private static readonly GUIContent s_LastCullingFilteredVisibleLabel =
            EditorGUIUtility.TrTextContent("Last Filtered Visible");
        private static readonly GUIContent s_LastCullingFilteredSortingLabel =
            EditorGUIUtility.TrTextContent("Last Filtered Sorting");
        private static readonly GUIContent s_LastCullingUsedFilteredLayoutLabel =
            EditorGUIUtility.TrTextContent("Used Filtered Layout");
        private static readonly GUIContent s_LastCullingUsedPickingFilterLabel =
            EditorGUIUtility.TrTextContent("Used Picking Filter");
        private static readonly GUIContent s_LastCullingFilterPassesLabel =
            EditorGUIUtility.TrTextContent("Filter Compact Passes");
        private static readonly GUIContent s_LastCullingFilterCommandsLabel =
            EditorGUIUtility.TrTextContent("Filter Scanned Commands");
        private static readonly GUIContent s_CullingScratchSlotsLabel =
            EditorGUIUtility.TrTextContent("Culling Scratch Slots");
        private static readonly GUIContent s_ActiveCullingScratchSlotsLabel =
            EditorGUIUtility.TrTextContent("Active Scratch Slots");
        private static readonly GUIContent s_LastCullingScratchSplitsLabel =
            EditorGUIUtility.TrTextContent("Last Scratch Splits");
        private static readonly GUIContent s_LastCullingScratchPacketsLabel =
            EditorGUIUtility.TrTextContent("Last Scratch Packets");
        private static readonly GUIContent s_CullingScratchFilteredCommandsLabel =
            EditorGUIUtility.TrTextContent("Filter Command Capacity");
        private static readonly GUIContent s_CullingScratchFilteredRangesLabel =
            EditorGUIUtility.TrTextContent("Filter Range Capacity");
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
        private static readonly GUIContent s_VisibleCapacityCacheEntriesLabel =
            EditorGUIUtility.TrTextContent("Visible Capacity Cache Entries");
        private static readonly GUIContent s_PickingDrawBuildWorksLabel =
            EditorGUIUtility.TrTextContent("Picking Draw Build Works");
        private static readonly GUIContent s_BatchDrawBuildWorksLabel =
            EditorGUIUtility.TrTextContent("Batch Draw Build Works");
        private static readonly GUIContent s_MeshVisibleWorksLabel = EditorGUIUtility.TrTextContent("Mesh Visible Works");
        private static readonly GUIContent s_MeshVisibleOutputsLabel = EditorGUIUtility.TrTextContent("Mesh Visible Outputs");
        private static readonly GUIContent s_MeshBatchVisibleOutputsLabel =
            EditorGUIUtility.TrTextContent("Mesh Batch Visible Outputs");
        private static readonly GUIContent s_MeshBatchReduceWorksLabel =
            EditorGUIUtility.TrTextContent("Mesh Batch Reduce Works");
        private static readonly GUIContent s_MeshVisibleInlineWorksLabel =
            EditorGUIUtility.TrTextContent("Mesh Visible Inline Works");
        private static readonly GUIContent s_MeshVisibleScheduledWorksLabel =
            EditorGUIUtility.TrTextContent("Mesh Visible Scheduled Works");
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
        private static readonly GUIContent s_GpuBufferInfosLabel =
            EditorGUIUtility.TrTextContent("GPU Buffer Infos");
        private static readonly GUIContent s_RecordCopyDescriptorsLabel =
            EditorGUIUtility.TrTextContent("Record Copy Descriptors");
        private static readonly GUIContent s_SharedValueBufferInfosLabel =
            EditorGUIUtility.TrTextContent("Shared Value Buffer Infos");
        private static readonly GUIContent s_PerSharpBufferInfosLabel =
            EditorGUIUtility.TrTextContent("Per-Sharp Buffer Infos");
        private static readonly GUIContent s_TransformUploadPageWorksLabel =
            EditorGUIUtility.TrTextContent("Transform Page Works");
        private static readonly GUIContent s_ColorUploadPageWorksLabel =
            EditorGUIUtility.TrTextContent("Color Page Works");
        private static readonly GUIContent s_VelocityUploadPageWorksLabel =
            EditorGUIUtility.TrTextContent("Velocity Page Works");
        private static readonly GUIContent s_UVUploadPageWorksLabel =
            EditorGUIUtility.TrTextContent("UV Page Works");
        private static readonly GUIContent s_CustomDataUploadPageWorksLabel =
            EditorGUIUtility.TrTextContent("Custom Data Page Works");
        private static readonly GUIContent s_MeshIndexUploadPageWorksLabel =
            EditorGUIUtility.TrTextContent("Mesh Index Page Works");
        private static readonly GUIContent s_ExtraUploadPageWorksLabel =
            EditorGUIUtility.TrTextContent("Extra Page Works");
        private static readonly GUIContent s_RenderKernelFlagsLabel =
            EditorGUIUtility.TrTextContent("Render Kernel Flags");
        private static readonly GUIContent s_AnimatedTransformPageWorksLabel =
            EditorGUIUtility.TrTextContent("Animated Transform Page Works");
        private static readonly GUIContent s_AnimatedColorPageWorksLabel =
            EditorGUIUtility.TrTextContent("Animated Color Page Works");
        private static readonly GUIContent s_AnimatedVelocityPageWorksLabel =
            EditorGUIUtility.TrTextContent("Animated Velocity Page Works");
        private static readonly GUIContent s_LastUploadCopyWorksLabel = EditorGUIUtility.TrTextContent("Upload Copy Works");
        private static readonly GUIContent s_MergedUploadCopyWorksLabel =
            EditorGUIUtility.TrTextContent("Merged Copy Works");
        private static readonly GUIContent s_UploadCopySortsLabel =
            EditorGUIUtility.TrTextContent("Upload Copy Sorts");
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
            "Non-default Sort Mode allocates camera-view sorting positions. Billboard modes use one bounds center per particle page; Mesh uses one position per particle.";
        private const string PerParticleGpuDataNotice =
            "Per-particle GPU data adds upload columns and increases particle buffer bandwidth.";
        private const string ShadowsOnlyNotice =
            "Shadows Only renders in light views and skips camera, picking, and selection draw output.";
        private const string MotionVectorsNotice =
            "Motion vectors set BRG motion flags and split draw ranges by motion mode.";
        private const string StaticShadowCasterNotice =
            "Static Shadow Caster changes BRG filter settings and can split draw ranges.";

        private SerializedProperty m_Main;
        private SerializedProperty m_Emission;
        private SerializedProperty m_Shape;
        private SerializedProperty m_ForceOverLifetime;
        private SerializedProperty m_ColorOverLifetime;
        private SerializedProperty m_ColorBySpeed;
        private SerializedProperty m_SizeOverLifetime;
        private SerializedProperty m_SizeBySpeed;
        private SerializedProperty m_RotationOverLifetime;
        private SerializedProperty m_RotationBySpeed;
        private SerializedProperty m_Noise;
        private SerializedProperty m_CustomData;
        private SerializedProperty m_VelocityOverLifetime;
        private SerializedProperty m_InheritVelocity;
        private SerializedProperty m_LimitVelocityOverLifetime;
        private SerializedProperty m_TextureSheetAnimation;
        private SerializedProperty m_Renderer;
        private bool m_MainExpanded = true;
        private bool m_EmissionExpanded = true;
        private bool m_ShapeExpanded = true;
        private bool m_ForceOverLifetimeExpanded;
        private bool m_ColorOverLifetimeExpanded;
        private bool m_ColorBySpeedExpanded;
        private bool m_SizeOverLifetimeExpanded;
        private bool m_SizeBySpeedExpanded;
        private bool m_RotationOverLifetimeExpanded;
        private bool m_RotationBySpeedExpanded;
        private bool m_NoiseExpanded;
        private bool m_CustomDataExpanded;
        private bool m_VelocityOverLifetimeExpanded;
        private bool m_InheritVelocityExpanded;
        private bool m_LimitVelocityOverLifetimeExpanded;
        private bool m_TextureSheetAnimationExpanded;
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
            m_ForceOverLifetime =
                VividParticleSystemEditorUtility.FindForceOverLifetimeProperty(serializedObject);
            m_ColorOverLifetime =
                VividParticleSystemEditorUtility.FindColorOverLifetimeProperty(serializedObject);
            m_ColorBySpeed =
                VividParticleSystemEditorUtility.FindColorBySpeedProperty(serializedObject);
            m_SizeOverLifetime =
                VividParticleSystemEditorUtility.FindSizeOverLifetimeProperty(serializedObject);
            m_SizeBySpeed =
                VividParticleSystemEditorUtility.FindSizeBySpeedProperty(serializedObject);
            m_RotationOverLifetime =
                VividParticleSystemEditorUtility.FindRotationOverLifetimeProperty(serializedObject);
            m_RotationBySpeed =
                VividParticleSystemEditorUtility.FindRotationBySpeedProperty(serializedObject);
            m_Noise = VividParticleSystemEditorUtility.FindNoiseProperty(serializedObject);
            m_CustomData = VividParticleSystemEditorUtility.FindCustomDataProperty(serializedObject);
            m_VelocityOverLifetime =
                VividParticleSystemEditorUtility.FindVelocityOverLifetimeProperty(serializedObject);
            m_InheritVelocity =
                VividParticleSystemEditorUtility.FindInheritVelocityProperty(serializedObject);
            m_LimitVelocityOverLifetime =
                VividParticleSystemEditorUtility.FindLimitVelocityOverLifetimeProperty(serializedObject);
            m_TextureSheetAnimation =
                VividParticleSystemEditorUtility.FindTextureSheetAnimationProperty(serializedObject);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawHeaderInspector();
            DrawModule(s_MainLabel, ref m_MainExpanded, () => DrawMainModule(m_Main));
            DrawModule(s_EmissionLabel, ref m_EmissionExpanded, () => DrawEmissionModule(m_Emission));
            DrawModule(s_ShapeLabel, ref m_ShapeExpanded, () => DrawShapeModule(m_Shape));
            DrawModule(
                s_ForceOverLifetimeLabel,
                ref m_ForceOverLifetimeExpanded,
                () => DrawForceOverLifetimeModule(m_ForceOverLifetime));
            DrawModule(
                s_VelocityOverLifetimeLabel,
                ref m_VelocityOverLifetimeExpanded,
                () => DrawVelocityOverLifetimeModule(m_VelocityOverLifetime));
            DrawModule(
                s_InheritVelocityLabel,
                ref m_InheritVelocityExpanded,
                () => DrawInheritVelocityModule(m_InheritVelocity));
            DrawModule(
                s_LimitVelocityOverLifetimeLabel,
                ref m_LimitVelocityOverLifetimeExpanded,
                () => DrawLimitVelocityOverLifetimeModule(m_LimitVelocityOverLifetime));
            DrawModule(
                s_ColorOverLifetimeLabel,
                ref m_ColorOverLifetimeExpanded,
                () => DrawColorOverLifetimeModule(m_ColorOverLifetime));
            DrawModule(
                s_ColorBySpeedLabel,
                ref m_ColorBySpeedExpanded,
                () => DrawColorBySpeedModule(m_ColorBySpeed));
            DrawModule(
                s_SizeOverLifetimeLabel,
                ref m_SizeOverLifetimeExpanded,
                () => DrawSizeOverLifetimeModule(m_SizeOverLifetime));
            DrawModule(
                s_SizeBySpeedLabel,
                ref m_SizeBySpeedExpanded,
                () => DrawSizeBySpeedModule(m_SizeBySpeed));
            DrawModule(
                s_RotationOverLifetimeLabel,
                ref m_RotationOverLifetimeExpanded,
                () => DrawRotationOverLifetimeModule(m_RotationOverLifetime));
            DrawModule(
                s_RotationBySpeedLabel,
                ref m_RotationBySpeedExpanded,
                () => DrawRotationBySpeedModule(m_RotationBySpeed));
            DrawModule(s_NoiseLabel, ref m_NoiseExpanded, () => DrawNoiseModule(m_Noise));
            DrawModule(
                s_CustomDataLabel,
                ref m_CustomDataExpanded,
                () => DrawCustomDataModule(m_CustomData));
            DrawModule(
                s_TextureSheetAnimationLabel,
                ref m_TextureSheetAnimationExpanded,
                () => DrawTextureSheetAnimationModule(m_TextureSheetAnimation));
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

                if (GUILayout.Button(s_RestartLabel))
                    ExecuteOnTargets(
                        "Restart Vivid Particle System",
                        system => VividParticleSystemEditorUtility.RestartPreview(system, play: true));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                m_EmitCount = Mathf.Max(1, EditorGUILayout.IntField(s_EmitCountLabel, m_EmitCount));
                if (GUILayout.Button(s_EmitLabel, GUILayout.Width(80.0f)))
                    ExecuteOnTargets("Emit Vivid Particles", system => system.Emit(m_EmitCount));
            }

            if (target is VividParticleSystem previewSystem)
            {
                float duration = Mathf.Max(0.01f, previewSystem.main.duration);
                EditorGUI.BeginChangeCheck();
                float previewTime = EditorGUILayout.Slider(
                    s_PreviewTimeLabel,
                    Mathf.Clamp(previewSystem.time, 0.0f, duration),
                    0.0f,
                    duration);
                if (EditorGUI.EndChangeCheck())
                {
                    ExecuteOnTargets(
                        "Scrub Vivid Particle System",
                        system => VividParticleSystemEditorUtility.ScrubPreview(system, previewTime));
                }
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
                EditorGUILayout.IntField(
                    s_ActiveSimulationQueryLinesLabel,
                    VividParticleSystemManager.lastActiveSimulationQueryLineCount);
                EditorGUILayout.IntField(
                    s_InvalidActiveSimulationQueryLinesLabel,
                    VividParticleSystemManager.lastInvalidActiveSimulationQueryLineCount);
                EditorGUILayout.IntField(
                    s_SimulationModuleGroupsLabel,
                    VividParticleSystemManager.lastActiveSimulationModuleGroupCount);
                EditorGUILayout.IntField(
                    s_SimulationModuleGroupCacheBuildsLabel,
                    VividParticleSystemManager.lastSimulationModuleGroupCacheBuildCount);
                EditorGUILayout.IntField(
                    s_SimulationModuleGroupCacheHitsLabel,
                    VividParticleSystemManager.lastSimulationModuleGroupCacheHitCount);
                EditorGUILayout.IntField(
                    s_SimulationModuleGroupSourceScansLabel,
                    VividParticleSystemManager.lastSimulationModuleGroupSourceScanCount);
                EditorGUILayout.IntField(
                    s_BaseSimulationPageWorksLabel,
                    VividParticleSystemManager.lastBaseSimulationPageWorkCount);
                EditorGUILayout.IntField(
                    s_VelocitySimulationPageWorksLabel,
                    VividParticleSystemManager.lastVelocitySimulationPageWorkCount);
                EditorGUILayout.IntField(
                    s_PendingSimulationSystemsLabel,
                    VividParticleSystemManager.pendingSimulationSystemCount);
                EditorGUILayout.IntField(
                    s_NativeSimulationConfigsLabel,
                    VividParticleSystemManager.nativeSimulationConfigCount);
                EditorGUILayout.IntField(
                    s_NativeRenderModuleConfigsLabel,
                    VividParticleSystemManager.nativeRenderModuleConfigCount);
                EditorGUILayout.IntField(
                    s_NativeSimulationBurstsLabel,
                    VividParticleSystemManager.nativeSimulationBurstCount);
                EditorGUILayout.IntField(
                    s_SimulationPrepareInlineLabel,
                    VividParticleSystemManager.lastSimulationPrepareInlineCount);
                EditorGUILayout.IntField(
                    s_SimulationPrepareScheduledLabel,
                    VividParticleSystemManager.lastSimulationPrepareScheduledCount);
                EditorGUILayout.IntField(
                    s_NativeSimulationConfigUpdatesLabel,
                    VividParticleSystemManager.nativeSimulationConfigUpdateCount);
                EditorGUILayout.IntField(
                    s_NativeSimulationBurstRebuildsLabel,
                    VividParticleSystemManager.nativeSimulationBurstRebuildCount);
                EditorGUILayout.IntField(
                    s_EmissionPlanWorksLabel,
                    VividParticleSystemManager.lastEmissionPlanWorkCount);
                EditorGUILayout.IntField(
                    s_EmissionPlanFallbacksLabel,
                    VividParticleSystemManager.lastEmissionPlanManagedFallbackCount);
                EditorGUILayout.IntField(
                    s_EmissionPlanReservationsLabel,
                    VividParticleSystemManager.lastEmissionPlanNativeReservationCount);
                EditorGUILayout.IntField(
                    s_EmissionPlanReservedParticlesLabel,
                    VividParticleSystemManager.lastEmissionPlanReservedParticleCount);
                EditorGUILayout.IntField(
                    s_EmissionInitializeWorksLabel,
                    runtimeStats.LastEmissionInitializeWorkCount);
                EditorGUILayout.IntField(
                    s_EmissionInitializeInlineWorksLabel,
                    runtimeStats.LastEmissionInitializeInlineWorkCount);
                EditorGUILayout.IntField(
                    s_EmissionInitializeScheduledWorksLabel,
                    runtimeStats.LastEmissionInitializeScheduledWorkCount);
                EditorGUILayout.IntField(
                    s_EmissionInitializePageWorksLabel,
                    VividParticleSystemManager.lastEmissionInitializePageWorkCount);
            }

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStats();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(
                    s_PerformanceSummaryLabel,
                VividParticleSystemEditorUtility.FormatPerformanceSummary(runtimeStats, rendererStats));
                EditorGUILayout.IntField(s_RenderRecordsLabel, rendererStats.RenderRecordCount);
                EditorGUILayout.IntField(s_RenderRecordPoolLabel, rendererStats.RenderRecordPoolCount);
                EditorGUILayout.IntField(s_ReusedRenderRecordsLabel, rendererStats.LastReusedRenderRecordCount);
                EditorGUILayout.IntField(s_CreatedRenderRecordsLabel, rendererStats.LastCreatedRenderRecordCount);
                EditorGUILayout.IntField(s_LineGroupsLabel, rendererStats.LineGroupCount);
                EditorGUILayout.IntField(s_LineGroupPoolLabel, rendererStats.LineGroupPoolCount);
                EditorGUILayout.IntField(s_ReusedLineGroupsLabel, rendererStats.LastReusedLineGroupCount);
                EditorGUILayout.IntField(s_CreatedLineGroupsLabel, rendererStats.LastCreatedLineGroupCount);
                EditorGUILayout.IntField(s_EcsRendererQueryCreatedLabel, rendererStats.LastEcsRendererQueryCreatedCount);
                EditorGUILayout.IntField(s_EcsRendererQueryReusedLabel, rendererStats.LastEcsRendererQueryReusedCount);
                EditorGUILayout.IntField(
                    s_EcsRendererQueryCacheBuildsLabel,
                    rendererStats.LastEcsRendererQueryCacheBuildCount);
                EditorGUILayout.IntField(
                    s_EcsRendererQueryCacheHitsLabel,
                    rendererStats.LastEcsRendererQueryCacheHitCount);
                EditorGUILayout.IntField(
                    s_EcsRendererQuerySourceScansLabel,
                    rendererStats.LastEcsRendererQuerySourceScanCount);
                EditorGUILayout.IntField(
                    s_EcsRendererQueryCachedLinesLabel,
                    rendererStats.LastEcsRendererQueryCachedLineCount);
                EditorGUILayout.IntField(
                    s_EcsRendererLineGroupCacheBuildsLabel,
                    rendererStats.LastEcsRendererLineGroupCacheBuildCount);
                EditorGUILayout.IntField(
                    s_EcsRendererLineGroupCacheHitsLabel,
                    rendererStats.LastEcsRendererLineGroupCacheHitCount);
                EditorGUILayout.IntField(
                    s_EcsRendererLineGroupSourceScansLabel,
                    rendererStats.LastEcsRendererLineGroupCacheSourceScanCount);
                EditorGUILayout.IntField(s_EcsLineGroupsLabel, rendererStats.EcsLineGroupCount);
                EditorGUILayout.IntField(s_EcsLinesLabel, rendererStats.EcsLineCount);
                EditorGUILayout.IntField(s_EcsMatchedLinesLabel, rendererStats.EcsMatchedLineCount);
                EditorGUILayout.IntField(s_EcsSkippedLinesLabel, rendererStats.EcsSkippedLineCount);
                EditorGUILayout.IntField(
                    s_ActiveRendererQueryLinesLabel,
                    VividParticleSystemManager.lastActiveRendererQueryLineCount);
                EditorGUILayout.IntField(
                    s_InvalidActiveRendererQueryLinesLabel,
                    VividParticleSystemManager.lastInvalidActiveRendererQueryLineCount);
                EditorGUILayout.IntField(s_RendererRecordRefsLabel, rendererStats.RendererRecordRefCount);
                EditorGUILayout.IntField(
                    s_InvalidRendererRecordRefsLabel,
                    rendererStats.LastInvalidRendererRecordRefCount);
                EditorGUILayout.IntField(s_DrawBatchesLabel, rendererStats.DrawBatchCount);
                EditorGUILayout.IntField(s_DrawBatchPoolLabel, rendererStats.DrawBatchPoolCount);
                EditorGUILayout.IntField(s_ReusedDrawBatchesLabel, rendererStats.LastReusedDrawBatchCount);
                EditorGUILayout.IntField(s_CreatedDrawBatchesLabel, rendererStats.LastCreatedDrawBatchCount);
                EditorGUILayout.IntField(s_CullingRecordsLabel, rendererStats.CullingRecordCount);
                EditorGUILayout.IntField(
                    s_CullingPageBoundsCapacityLabel,
                    rendererStats.CullingPageBoundsCapacity);
                EditorGUILayout.IntField(
                    s_CullingRecordBuildWorksLabel,
                    rendererStats.LastCullingRecordBuildWorkCount);
                EditorGUILayout.Toggle(
                    s_PendingCullingRecordBuildLabel,
                    rendererStats.HasPendingCullingRecordBuild);
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
                EditorGUILayout.EnumPopup(s_LastCullingViewTypeLabel, rendererStats.LastCullingViewType);
                EditorGUILayout.IntField(
                    s_LastCullingSourceCommandsLabel,
                    rendererStats.LastCullingSourceDrawCommandCount);
                EditorGUILayout.IntField(s_LastCullingSourceRangesLabel, rendererStats.LastCullingSourceDrawRangeCount);
                EditorGUILayout.IntField(
                    s_LastCullingSourceVisibleLabel,
                    rendererStats.LastCullingSourceVisibleInstanceCount);
                EditorGUILayout.IntField(
                    s_LastCullingSourceSortingLabel,
                    rendererStats.LastCullingSourceSortingPositionCount);
                EditorGUILayout.IntField(
                    s_LastCullingFilteredCommandsLabel,
                    rendererStats.LastCullingFilteredDrawCommandCount);
                EditorGUILayout.IntField(
                    s_LastCullingFilteredRangesLabel,
                    rendererStats.LastCullingFilteredDrawRangeCount);
                EditorGUILayout.IntField(
                    s_LastCullingFilteredVisibleLabel,
                    rendererStats.LastCullingFilteredVisibleInstanceCount);
                EditorGUILayout.IntField(
                    s_LastCullingFilteredSortingLabel,
                    rendererStats.LastCullingFilteredSortingPositionCount);
                EditorGUILayout.Toggle(
                    s_LastCullingUsedFilteredLayoutLabel,
                    rendererStats.LastCullingUsedFilteredLayout);
                EditorGUILayout.Toggle(
                    s_LastCullingUsedPickingFilterLabel,
                    rendererStats.LastCullingUsedPickingFilter);
                EditorGUILayout.IntField(
                    s_LastCullingFilterPassesLabel,
                    rendererStats.LastCullingFilterPassCount);
                EditorGUILayout.IntField(
                    s_LastCullingFilterCommandsLabel,
                    rendererStats.LastCullingFilterCommandCount);
                EditorGUILayout.IntField(
                    s_CullingScratchSlotsLabel,
                    rendererStats.CullingScratchSlotCount);
                EditorGUILayout.IntField(
                    s_ActiveCullingScratchSlotsLabel,
                    rendererStats.ActiveCullingScratchSlotCount);
                EditorGUILayout.IntField(
                    s_LastCullingScratchSplitsLabel,
                    rendererStats.LastCullingScratchSplitCount);
                EditorGUILayout.IntField(
                    s_LastCullingScratchPacketsLabel,
                    rendererStats.LastCullingScratchPacketCount);
                EditorGUILayout.IntField(
                    s_CullingScratchFilteredCommandsLabel,
                    rendererStats.CullingScratchFilteredCommandCapacity);
                EditorGUILayout.IntField(
                    s_CullingScratchFilteredRangesLabel,
                    rendererStats.CullingScratchFilteredRangeCapacity);
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
                EditorGUILayout.IntField(
                    s_VisibleCapacityCacheEntriesLabel,
                    rendererStats.LastVisibleInstanceCapacityCacheEntryCount);
                EditorGUILayout.IntField(
                    s_PickingDrawBuildWorksLabel,
                    rendererStats.LastPickingDrawBuildWorkCount);
                EditorGUILayout.IntField(
                    s_BatchDrawBuildWorksLabel,
                    rendererStats.LastBatchDrawBuildWorkCount);
                EditorGUILayout.IntField(s_MeshVisibleWorksLabel, rendererStats.MeshVisibleCountWorkCount);
                EditorGUILayout.IntField(s_MeshVisibleOutputsLabel, rendererStats.MeshVisibleCountOutputCount);
                EditorGUILayout.IntField(
                    s_MeshBatchVisibleOutputsLabel,
                    rendererStats.MeshBatchVisibleCountOutputCount);
                EditorGUILayout.IntField(
                    s_MeshBatchReduceWorksLabel,
                    rendererStats.LastMeshVisibleBatchReduceWorkCount);
                EditorGUILayout.IntField(s_MeshVisibleInlineWorksLabel, rendererStats.LastMeshVisibleCountInlineWorkCount);
                EditorGUILayout.IntField(
                    s_MeshVisibleScheduledWorksLabel,
                    rendererStats.LastMeshVisibleCountScheduledWorkCount);
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
                EditorGUILayout.IntField(s_GpuBufferInfosLabel, rendererStats.LastGpuBufferInfoCount);
                EditorGUILayout.IntField(s_RecordCopyDescriptorsLabel, rendererStats.LastRecordCopyDescriptorCount);
                EditorGUILayout.IntField(s_SharedValueBufferInfosLabel, rendererStats.LastSharedValueBufferInfoCount);
                EditorGUILayout.IntField(s_PerSharpBufferInfosLabel, rendererStats.LastPerSharpValueBufferInfoCount);
                EditorGUILayout.IntField(s_TransformUploadPageWorksLabel, rendererStats.LastTransformUploadPageWorkCount);
                EditorGUILayout.IntField(s_ColorUploadPageWorksLabel, rendererStats.LastColorUploadPageWorkCount);
                EditorGUILayout.IntField(s_VelocityUploadPageWorksLabel, rendererStats.LastVelocityStretchUploadPageWorkCount);
                EditorGUILayout.IntField(s_UVUploadPageWorksLabel, rendererStats.LastUVUploadPageWorkCount);
                EditorGUILayout.IntField(s_CustomDataUploadPageWorksLabel, rendererStats.LastCustomDataUploadPageWorkCount);
                EditorGUILayout.IntField(s_MeshIndexUploadPageWorksLabel, rendererStats.LastMeshIndexUploadPageWorkCount);
                EditorGUILayout.IntField(s_ExtraUploadPageWorksLabel, rendererStats.LastExtraDataUploadPageWorkCount);
                EditorGUILayout.TextField(
                    s_RenderKernelFlagsLabel,
                    $"0x{VividParticleSystemManager.lastRenderKernelFlags:X8}");
                EditorGUILayout.IntField(
                    s_AnimatedTransformPageWorksLabel,
                    VividParticleSystemManager.lastAnimatedTransformPageWorkCount);
                EditorGUILayout.IntField(
                    s_AnimatedColorPageWorksLabel,
                    VividParticleSystemManager.lastAnimatedColorPageWorkCount);
                EditorGUILayout.IntField(
                    s_AnimatedVelocityPageWorksLabel,
                    VividParticleSystemManager.lastAnimatedVelocityPageWorkCount);
                EditorGUILayout.IntField(s_LastUploadCopyWorksLabel, rendererStats.LastUploadCopyWorkCount);
                EditorGUILayout.IntField(s_MergedUploadCopyWorksLabel, rendererStats.LastMergedUploadCopyWorkCount);
                EditorGUILayout.IntField(s_UploadCopySortsLabel, rendererStats.LastUploadCopySortCount);
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
            SerializedProperty emitterVelocityMode =
                VividParticleSystemEditorUtility.FindRelative(module, "m_EmitterVelocityMode");
            EditorGUILayout.PropertyField(emitterVelocityMode);
            if (emitterVelocityMode != null
                && !emitterVelocityMode.hasMultipleDifferentValues
                && (VividParticleEmitterVelocityMode)emitterVelocityMode.enumValueIndex
                    == VividParticleEmitterVelocityMode.Custom)
            {
                DrawRelative(module, "m_CustomEmitterVelocity");
            }

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

        private static void DrawForceOverLifetimeModule(SerializedProperty module)
        {
            if (module == null)
                return;

            SerializedProperty enabled = VividParticleSystemEditorUtility.FindRelative(module, "m_Enabled");
            EditorGUILayout.PropertyField(enabled);
            using (new EditorGUI.DisabledScope(
                enabled != null && !enabled.hasMultipleDifferentValues && !enabled.boolValue))
            {
                DrawRelative(module, "m_Force");
                DrawRelative(module, "m_Space");
            }
        }

        private static void DrawColorOverLifetimeModule(SerializedProperty module)
        {
            DrawEnabledModule(module, "m_Color");
        }

        private static void DrawColorBySpeedModule(SerializedProperty module)
        {
            DrawEnabledModule(module, "m_Color", "m_Range");
        }

        private static void DrawSizeOverLifetimeModule(SerializedProperty module)
        {
            DrawEnabledModule(module, "m_Size");
        }

        private static void DrawSizeBySpeedModule(SerializedProperty module)
        {
            DrawEnabledModule(module, "m_Size", "m_Range");
        }

        private static void DrawRotationOverLifetimeModule(SerializedProperty module)
        {
            DrawEnabledModule(module, "m_AngularVelocity");
        }

        private static void DrawRotationBySpeedModule(SerializedProperty module)
        {
            if (module == null)
                return;

            SerializedProperty enabled = VividParticleSystemEditorUtility.FindRelative(module, "m_Enabled");
            EditorGUILayout.PropertyField(enabled);
            using (new EditorGUI.DisabledScope(
                enabled != null && !enabled.hasMultipleDifferentValues && !enabled.boolValue))
            {
                SerializedProperty separateAxes =
                    VividParticleSystemEditorUtility.FindRelative(module, "m_SeparateAxes");
                EditorGUILayout.PropertyField(separateAxes);
                bool drawsSeparateAxes = separateAxes != null
                    && !separateAxes.hasMultipleDifferentValues
                    && separateAxes.boolValue;
                if (drawsSeparateAxes)
                {
                    DrawRelative(module, "m_X");
                    DrawRelative(module, "m_Y");
                }

                DrawRelative(module, "m_Z");
                DrawRelative(module, "m_Range");
            }
        }

        private static void DrawNoiseModule(SerializedProperty module)
        {
            if (module == null)
                return;

            SerializedProperty enabled = VividParticleSystemEditorUtility.FindRelative(module, "m_Enabled");
            EditorGUILayout.PropertyField(enabled);
            using (new EditorGUI.DisabledScope(
                enabled != null && !enabled.hasMultipleDifferentValues && !enabled.boolValue))
            {
                SerializedProperty separateAxes =
                    VividParticleSystemEditorUtility.FindRelative(module, "m_SeparateAxes");
                EditorGUILayout.PropertyField(separateAxes);
                bool drawsSeparateAxes = separateAxes != null
                    && !separateAxes.hasMultipleDifferentValues
                    && separateAxes.boolValue;
                if (drawsSeparateAxes)
                {
                    DrawRelative(module, "m_StrengthX");
                    DrawRelative(module, "m_StrengthY");
                    DrawRelative(module, "m_StrengthZ");
                }
                else
                {
                    DrawRelative(module, "m_Strength");
                }

                DrawRelative(module, "m_Frequency");
                DrawRelative(module, "m_Damping");
                DrawRelative(module, "m_Quality");
                DrawRelative(module, "m_OctaveCount");
                DrawRelative(module, "m_OctaveMultiplier");
                DrawRelative(module, "m_OctaveScale");
                DrawRelative(module, "m_ScrollSpeed");
                SerializedProperty remapEnabled =
                    VividParticleSystemEditorUtility.FindRelative(module, "m_RemapEnabled");
                EditorGUILayout.PropertyField(remapEnabled);
                using (new EditorGUI.DisabledScope(
                    remapEnabled != null
                    && !remapEnabled.hasMultipleDifferentValues
                    && !remapEnabled.boolValue))
                {
                    DrawRelative(module, "m_RemapX");
                    DrawRelative(module, "m_RemapY");
                    DrawRelative(module, "m_RemapZ");
                }
                DrawRelative(module, "m_PositionAmount");
                DrawRelative(module, "m_RotationAmount");
                DrawRelative(module, "m_SizeAmount");
            }
        }

        private static void DrawVelocityOverLifetimeModule(SerializedProperty module)
        {
            if (module == null)
                return;

            SerializedProperty enabled = VividParticleSystemEditorUtility.FindRelative(module, "m_Enabled");
            EditorGUILayout.PropertyField(enabled);
            using (new EditorGUI.DisabledScope(
                enabled != null && !enabled.hasMultipleDifferentValues && !enabled.boolValue))
            {
                DrawRelative(module, "m_X");
                DrawRelative(module, "m_Y");
                DrawRelative(module, "m_Z");
                DrawRelative(module, "m_Space");
            }
        }

        private static void DrawInheritVelocityModule(SerializedProperty module)
        {
            if (module == null)
                return;

            SerializedProperty enabled = VividParticleSystemEditorUtility.FindRelative(module, "m_Enabled");
            EditorGUILayout.PropertyField(enabled);
            using (new EditorGUI.DisabledScope(
                enabled != null && !enabled.hasMultipleDifferentValues && !enabled.boolValue))
            {
                DrawRelative(module, "m_Mode");
                DrawRelative(module, "m_Curve");
            }
        }

        private static void DrawLimitVelocityOverLifetimeModule(SerializedProperty module)
        {
            if (module == null)
                return;

            SerializedProperty enabled = VividParticleSystemEditorUtility.FindRelative(module, "m_Enabled");
            EditorGUILayout.PropertyField(enabled);
            using (new EditorGUI.DisabledScope(
                enabled != null && !enabled.hasMultipleDifferentValues && !enabled.boolValue))
            {
                SerializedProperty separateAxes =
                    VividParticleSystemEditorUtility.FindRelative(module, "m_SeparateAxes");
                EditorGUILayout.PropertyField(separateAxes);
                bool drawsSeparateAxes = separateAxes != null
                    && !separateAxes.hasMultipleDifferentValues
                    && separateAxes.boolValue;
                if (drawsSeparateAxes)
                {
                    DrawRelative(module, "m_LimitX");
                    DrawRelative(module, "m_LimitY");
                    DrawRelative(module, "m_LimitZ");
                }
                else
                {
                    DrawRelative(module, "m_Limit");
                }

                DrawRelative(module, "m_Dampen");
                DrawRelative(module, "m_Space");
                DrawRelative(module, "m_Drag");
                DrawRelative(module, "m_MultiplyDragByParticleSize");
                DrawRelative(module, "m_MultiplyDragByParticleVelocity");
            }
        }

        private static void DrawCustomDataModule(SerializedProperty module)
        {
            if (module == null)
                return;

            DrawCustomDataStream(module, streamIndex: 1);
            EditorGUILayout.Space(EditorGUIUtility.standardVerticalSpacing);
            DrawCustomDataStream(module, streamIndex: 2);
        }

        private static void DrawCustomDataStream(SerializedProperty module, int streamIndex)
        {
            SerializedProperty mode = VividParticleSystemEditorUtility.FindRelative(
                module,
                $"m_Mode{streamIndex}");
            EditorGUILayout.PropertyField(mode, EditorGUIUtility.TrTextContent($"Custom {streamIndex}"));
            if (mode == null || mode.hasMultipleDifferentValues)
                return;

            var dataMode = (VividParticleCustomDataMode)mode.enumValueIndex;
            if (dataMode == VividParticleCustomDataMode.Vector)
            {
                SerializedProperty componentCount = VividParticleSystemEditorUtility.FindRelative(
                    module,
                    $"m_NumberOfComponents{streamIndex}");
                EditorGUILayout.PropertyField(componentCount);
                int count = componentCount != null && !componentCount.hasMultipleDifferentValues
                    ? Mathf.Clamp(componentCount.intValue, 1, 4)
                    : 4;
                string[] components = { "X", "Y", "Z", "W" };
                for (int component = 0; component < count; component++)
                    DrawRelative(module, $"m_Vector{streamIndex}{components[component]}");
            }
            else if (dataMode == VividParticleCustomDataMode.Color)
            {
                DrawRelative(module, $"m_Color{streamIndex}");
            }
        }

        private static void DrawTextureSheetAnimationModule(SerializedProperty module)
        {
            if (module == null)
                return;

            DrawRelative(module, "m_Enabled");
            SerializedProperty enabled = module.FindPropertyRelative("m_Enabled");
            using (new EditorGUI.DisabledScope(enabled != null && !enabled.boolValue))
            {
                DrawRelative(module, "m_NumTilesX");
                DrawRelative(module, "m_NumTilesY");
                DrawRelative(module, "m_Animation");
                DrawRelative(module, "m_FrameOverTime");
                DrawRelative(module, "m_StartFrame");
                DrawRelative(module, "m_CycleCount");

                SerializedProperty animation = module.FindPropertyRelative("m_Animation");
                if (animation != null
                    && !animation.hasMultipleDifferentValues
                    && (VividParticleTextureSheetAnimationType)animation.enumValueIndex
                        == VividParticleTextureSheetAnimationType.SingleRow)
                {
                    DrawRelative(module, "m_RowIndex");
                }
            }
        }

        private static void DrawEnabledModule(SerializedProperty module, string valuePropertyName)
        {
            DrawEnabledModule(module, valuePropertyName, null);
        }

        private static void DrawEnabledModule(
            SerializedProperty module,
            string valuePropertyName,
            string secondaryPropertyName)
        {
            if (module == null)
                return;

            SerializedProperty enabled = VividParticleSystemEditorUtility.FindRelative(module, "m_Enabled");
            EditorGUILayout.PropertyField(enabled);
            using (new EditorGUI.DisabledScope(
                enabled != null && !enabled.hasMultipleDifferentValues && !enabled.boolValue))
            {
                DrawRelative(module, valuePropertyName);
                if (!string.IsNullOrEmpty(secondaryPropertyName))
                    DrawRelative(module, secondaryPropertyName);
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
                DrawRelative(module, "m_SortingPriority");
                DrawRelative(module, "m_BatchLayer");
                DrawRelative(module, "m_ShadowCastingMode");
                DrawRelative(module, "m_MotionVectorGenerationMode");
                DrawRelative(module, "m_StaticShadowCaster");
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

            if ((notices & VividParticleRendererInspectorNotice.ShadowsOnlySkipsRegularViews) != 0)
                EditorGUILayout.HelpBox(ShadowsOnlyNotice, MessageType.Info);

            if ((notices & VividParticleRendererInspectorNotice.MotionVectorsAffectDrawOutput) != 0)
                EditorGUILayout.HelpBox(MotionVectorsNotice, MessageType.Info);

            if ((notices & VividParticleRendererInspectorNotice.StaticShadowCasterAffectsDrawOutput) != 0)
                EditorGUILayout.HelpBox(StaticShadowCasterNotice, MessageType.Info);
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

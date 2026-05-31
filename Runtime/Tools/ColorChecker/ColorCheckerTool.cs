#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [ExecuteAlways]
    [SelectionBase]
    [DisallowMultipleComponent]
    [AddComponentMenu("Rendering/VividRP/Color Checker Tool")]
    public sealed class ColorCheckerTool : MonoBehaviour, ISerializationCallbackReceiver
    {
        public enum ColorCheckerMode
        {
            [InspectorName("Color Palette")]
            Colors,
            [InspectorName("Cross Polarized Grayscale")]
            Grayscale,
            MiddleGray,
            Reflection,
            SteppedLuminance,
            [InspectorName("Material Palette")]
            Materials,
            [InspectorName("External Texture")]
            Texture,
        }

        internal const int MaxColorFields = 64;
        internal const int MaxMaterialFields = 12;
        internal const int TextureSize = 8;
        internal const int SmoothnessColumns = 6;
        internal const string GeometryName = "Colorchecker Geometry";
        internal const string ShaderName = "VividRP/Tools/ColorChecker";

        private static readonly int CompareToUnlitId = Shader.PropertyToID("_Compare_to_Unlit");
        private static readonly int NumberOfFieldsId = Shader.PropertyToID("_NumberOfFields");
        private static readonly int FieldsPerRowId = Shader.PropertyToID("_FieldsPerRow");
        private static readonly int GridThicknessId = Shader.PropertyToID("_gridThickness");
        private static readonly int SquareSizeId = Shader.PropertyToID("_SquareSize");
        private static readonly int AddGradientId = Shader.PropertyToID("_Add_Gradient");
        private static readonly int GradientColorAId = Shader.PropertyToID("_Gradient_Color_A");
        private static readonly int GradientColorBId = Shader.PropertyToID("_Gradient_Color_B");
        private static readonly int GradientPowerId = Shader.PropertyToID("_gradient_power");
        private static readonly int SphereModeId = Shader.PropertyToID("_sphereMode");
        private static readonly int MaterialModeId = Shader.PropertyToID("_material_mode");
        private static readonly int CheckerTextureId = Shader.PropertyToID("_CheckerTexture");
        private static readonly int TextureModeId = Shader.PropertyToID("_texture_mode");
        private static readonly int ReflectionModeId = Shader.PropertyToID("_reflection_mode");
        private static readonly int RawTextureId = Shader.PropertyToID("_rawTexture");
        private static readonly int RawTextureAvailableId = Shader.PropertyToID("_rawTextureAvailable");
        private static readonly int RawTexturePreExposureId = Shader.PropertyToID("_rawTexturePreExposure");
        private static readonly int TextureSliceId = Shader.PropertyToID("_textureSlice");

        private static readonly Color32[] s_ColorPalette =
        {
            new(245, 245, 240, 255),
            new(201, 202, 201, 255),
            new(161, 162, 162, 255),
            new(120, 121, 121, 255),
            new(83, 85, 85, 255),
            new(50, 50, 51, 255),
            new(42, 63, 147, 255),
            new(72, 149, 72, 255),
            new(175, 50, 57, 255),
            new(238, 200, 22, 255),
            new(188, 84, 150, 255),
            new(0, 137, 166, 255),
            new(220, 123, 46, 255),
            new(72, 92, 168, 255),
            new(194, 84, 97, 255),
            new(91, 59, 104, 255),
            new(161, 189, 62, 255),
            new(229, 161, 40, 255),
            new(115, 82, 68, 255),
            new(194, 149, 128, 255),
            new(93, 123, 157, 255),
            new(91, 108, 65, 255),
            new(130, 129, 175, 255),
            new(99, 191, 171, 255),
            new(50, 50, 50, 255),
            new(243, 243, 243, 255),
            new(85, 61, 49, 255),
            new(135, 92, 60, 255),
            new(114, 103, 91, 255),
            new(123, 130, 52, 255),
            new(148, 125, 117, 255),
            new(135, 136, 131, 255),
            new(163, 163, 163, 255),
            new(177, 167, 132, 255),
            new(192, 191, 187, 255),
            new(224, 199, 168, 255),
            new(204, 157, 178, 255),
            new(188, 120, 140, 255),
            new(123, 102, 157, 255),
            new(103, 133, 166, 255),
            new(137, 167, 197, 255),
            new(119, 159, 139, 255),
            new(49, 98, 125, 255),
            new(66, 130, 85, 255),
            new(217, 156, 52, 255),
            new(200, 115, 76, 255),
            new(175, 54, 60, 255),
            new(180, 67, 124, 255),
            new(55, 79, 137, 255),
            new(40, 97, 140, 255),
            new(89, 128, 159, 255),
            new(136, 159, 107, 255),
            new(97, 142, 117, 255),
            new(41, 83, 87, 255),
            new(142, 51, 34, 255),
            new(200, 115, 76, 255),
            new(212, 135, 23, 255),
            new(164, 94, 114, 255),
            new(202, 121, 140, 255),
            new(96, 60, 94, 255),
            new(233, 233, 227, 255),
            new(147, 147, 146, 255),
            new(55, 58, 58, 255),
            new(19, 20, 22, 255),
        };

        private static readonly Color32[] s_CrossPolarizedGrayscale =
        {
            new(19, 20, 22, 255),
            new(55, 58, 58, 255),
            new(101, 102, 100, 255),
            new(147, 147, 146, 255),
            new(186, 188, 187, 255),
            new(233, 233, 227, 255),
        };

        private static readonly Color32[] s_MaterialPalette =
        {
            new(237, 237, 237, 0),
            new(39, 39, 39, 0),
            new(193, 190, 187, 255),
            new(247, 221, 188, 255),
            new(251, 249, 246, 255),
            new(249, 228, 164, 255),
            new(175, 54, 60, 0),
            new(177, 167, 132, 0),
            new(87, 108, 67, 0),
            new(98, 122, 157, 0),
            new(245, 245, 246, 255),
            new(242, 230, 176, 255),
        };

        private static readonly Color32[] s_MiddleGray =
        {
            new(120, 121, 121, 255),
        };

        [SerializeField]
        private ColorCheckerMode m_Mode = ColorCheckerMode.Colors;

        [SerializeField]
        private bool m_AddGradient;

        [SerializeField]
        private bool m_UnlitCompare;

        [SerializeField]
        private bool m_SphereMode;

        [SerializeField, Range(1, MaxColorFields)]
        private int m_FieldCount = 24;

        [SerializeField, Range(1, MaxMaterialFields)]
        private int m_MaterialFieldsCount = 6;

        [SerializeField, Range(1, TextureSize)]
        private int m_FieldsPerRow = 6;

        [SerializeField, Range(0f, 0.49f)]
        private float m_GridThickness = 0.1f;

        [SerializeField, Min(0.001f)]
        private float m_FieldSize = 0.1f;

        [SerializeField, Min(0.01f)]
        private float m_GradientPower = 2.2f;

        [SerializeField]
        private Color32 m_GradientA = new(19, 20, 22, 255);

        [SerializeField]
        private Color32 m_GradientB = new(233, 233, 227, 255);

        [SerializeField]
        private Texture2D m_UserTexture;

        [SerializeField]
        private Texture2D m_UserTextureRaw;

        [SerializeField, Range(0f, 1f)]
        private float m_TextureSlice = 0.5f;

        [SerializeField]
        private bool m_UnlitTextureExposure = true;

        [SerializeField]
        private Color32[] m_CustomColors = CreateColorPalette();

        [SerializeField]
        private Color32[] m_CustomMaterials = CreateMaterialPalette();

        [SerializeField]
        private bool[] m_IsMetalBools = CreateMaterialMetalFlags();

        [SerializeField, HideInInspector]
        private Texture2D m_ColorCheckerTexture;

        [SerializeField, HideInInspector]
        private GameObject m_ColorCheckerObject;

        private MeshRenderer m_ColorCheckerRenderer;
        private MeshFilter m_ColorCheckerFilter;
        private Material m_ColorCheckerMaterial;
        private MaterialPropertyBlock m_MaterialPropertyBlock;
        private readonly Color32[] m_SteppedLuminance = new Color32[16];
        private bool m_IsRefreshing;

        internal int fieldsToDisplay { get; private set; }
        internal int fieldsPerRowToDisplay { get; private set; }
        internal float sizeToDisplay { get; private set; }
        internal bool sphereModeToDisplay { get; private set; }
        internal bool gradientToDisplay { get; private set; }
        internal float gridToDisplay { get; private set; }
        internal GameObject colorCheckerObject => m_ColorCheckerObject;
        internal MeshRenderer colorCheckerRenderer => m_ColorCheckerRenderer;
        internal Texture2D colorCheckerTexture => m_ColorCheckerTexture;

        public ColorCheckerMode Mode
        {
            get => m_Mode;
            set
            {
                if (m_Mode == value)
                    return;

                m_Mode = value;
                Refresh();
            }
        }

        public bool addGradient
        {
            get => m_AddGradient;
            set
            {
                if (m_AddGradient == value)
                    return;

                m_AddGradient = value;
                Refresh();
            }
        }

        public bool unlitCompare
        {
            get => m_UnlitCompare;
            set
            {
                if (m_UnlitCompare == value)
                    return;

                m_UnlitCompare = value;
                Refresh();
            }
        }

        public bool sphereMode
        {
            get => m_SphereMode;
            set
            {
                if (m_SphereMode == value)
                    return;

                m_SphereMode = value;
                Refresh();
            }
        }

        public int fieldCount
        {
            get => m_FieldCount;
            set
            {
                m_FieldCount = Mathf.Clamp(value, 1, MaxColorFields);
                Refresh();
            }
        }

        public int materialFieldsCount
        {
            get => m_MaterialFieldsCount;
            set
            {
                m_MaterialFieldsCount = Mathf.Clamp(value, 1, MaxMaterialFields);
                Refresh();
            }
        }

        public int fieldsPerRow
        {
            get => m_FieldsPerRow;
            set
            {
                m_FieldsPerRow = Mathf.Clamp(value, 1, TextureSize);
                Refresh();
            }
        }

        public float gridThickness
        {
            get => m_GridThickness;
            set
            {
                m_GridThickness = Mathf.Clamp(value, 0f, 0.49f);
                Refresh();
            }
        }

        public float fieldSize
        {
            get => m_FieldSize;
            set
            {
                m_FieldSize = Mathf.Max(0.001f, value);
                Refresh();
            }
        }

        public float gradientPower
        {
            get => m_GradientPower;
            set
            {
                m_GradientPower = Mathf.Max(0.01f, value);
                Refresh();
            }
        }

        public Color32 gradientA
        {
            get => m_GradientA;
            set
            {
                m_GradientA = value;
                UpdateMaterial();
            }
        }

        public Color32 gradientB
        {
            get => m_GradientB;
            set
            {
                m_GradientB = value;
                UpdateMaterial();
            }
        }

        public Texture2D userTexture
        {
            get => m_UserTexture;
            set
            {
                m_UserTexture = value;
                UpdateMaterial();
            }
        }

        public Texture2D userTextureRaw
        {
            get => m_UserTextureRaw;
            set
            {
                m_UserTextureRaw = value;
                UpdateMaterial();
            }
        }

        public float textureSlice
        {
            get => m_TextureSlice;
            set
            {
                m_TextureSlice = Mathf.Clamp01(value);
                UpdateMaterial();
            }
        }

        public bool unlitTextureExposure
        {
            get => m_UnlitTextureExposure;
            set
            {
                m_UnlitTextureExposure = value;
                UpdateMaterial();
            }
        }

        public Color32[] customColors => m_CustomColors;
        public Color32[] customMaterials => m_CustomMaterials;
        public bool[] isMetalBools => m_IsMetalBools;
        public Color32[] crossPolarizedGrayscale => s_CrossPolarizedGrayscale;
        public Color32[] middleGray => s_MiddleGray;
        public Color32[] steppedLuminance => m_SteppedLuminance;

        private void Awake()
        {
            Refresh();
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void OnValidate()
        {
            ClampSerializedSettings();
            Refresh();
        }

        private void OnDestroy()
        {
            DestroyGeneratedObject(m_ColorCheckerObject);
            m_ColorCheckerObject = null;

            DestroyGeneratedObject(m_ColorCheckerTexture);
            m_ColorCheckerTexture = null;

            DestroyGeneratedObject(m_ColorCheckerMaterial);
            m_ColorCheckerMaterial = null;
            m_MaterialPropertyBlock = null;
        }

        public void OnBeforeSerialize()
        {
            ApplyMaterialMetalFlags();
        }

        public void OnAfterDeserialize()
        {
        }

        internal void Refresh()
        {
            if (m_IsRefreshing)
                return;

            m_IsRefreshing = true;
            try
            {
                ClampSerializedSettings();
                EnsureColorArrays();
                EnsureSteppedLuminance();
                EnsureTexture();
                EnsureGeometryObject();
                UpdateMaterial();
                UpdateGeometry();
            }
            finally
            {
                m_IsRefreshing = false;
            }
        }

        internal void UpdateMaterial()
        {
            EnsureColorArrays();
            EnsureSteppedLuminance();
            EnsureTexture();
            EnsureRendererReferences();

            fieldsToDisplay = m_FieldCount;
            fieldsPerRowToDisplay = m_FieldsPerRow;
            sizeToDisplay = m_FieldSize;
            sphereModeToDisplay = m_SphereMode;
            gradientToDisplay = m_AddGradient;
            gridToDisplay = m_GridThickness;
            var unlitToDisplay = m_UnlitCompare;
            var textureToDisplay = m_ColorCheckerTexture;

            switch (m_Mode)
            {
                case ColorCheckerMode.Colors:
                    UpdateTexture(m_CustomColors);
                    break;
                case ColorCheckerMode.Grayscale:
                    UpdateTexture(s_CrossPolarizedGrayscale);
                    fieldsToDisplay = s_CrossPolarizedGrayscale.Length;
                    fieldsPerRowToDisplay = s_CrossPolarizedGrayscale.Length;
                    break;
                case ColorCheckerMode.MiddleGray:
                    UpdateTexture(s_MiddleGray);
                    fieldsToDisplay = 1;
                    fieldsPerRowToDisplay = 1;
                    sizeToDisplay *= 4f;
                    gradientToDisplay = false;
                    break;
                case ColorCheckerMode.Reflection:
                    SetSingleTextureColor(Color.white);
                    fieldsToDisplay = 1;
                    fieldsPerRowToDisplay = 1;
                    sizeToDisplay *= 4f;
                    sphereModeToDisplay = true;
                    gradientToDisplay = false;
                    break;
                case ColorCheckerMode.SteppedLuminance:
                    UpdateTexture(m_SteppedLuminance);
                    fieldsToDisplay = m_SteppedLuminance.Length;
                    fieldsPerRowToDisplay = m_SteppedLuminance.Length;
                    gridToDisplay = 0f;
                    sphereModeToDisplay = false;
                    break;
                case ColorCheckerMode.Materials:
                    ApplyMaterialMetalFlags();
                    UpdateTexture(m_CustomMaterials);
                    fieldsToDisplay = m_MaterialFieldsCount * SmoothnessColumns;
                    fieldsPerRowToDisplay = SmoothnessColumns;
                    sphereModeToDisplay = true;
                    unlitToDisplay = false;
                    gradientToDisplay = false;
                    break;
                case ColorCheckerMode.Texture:
                    fieldsToDisplay = 1;
                    fieldsPerRowToDisplay = 1;
                    sizeToDisplay *= 6f;
                    sphereModeToDisplay = false;
                    unlitToDisplay = true;
                    gradientToDisplay = false;
                    gridToDisplay = 0f;
                    if (m_UserTexture != null)
                        textureToDisplay = m_UserTexture;
                    else
                        UpdateTexture(Array.Empty<Color32>());
                    break;
            }

            if (m_ColorCheckerRenderer == null)
                return;

            m_MaterialPropertyBlock ??= new MaterialPropertyBlock();
            var propertyBlock = m_MaterialPropertyBlock;
            m_ColorCheckerRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetInt(CompareToUnlitId, unlitToDisplay ? 1 : 0);
            propertyBlock.SetInt(NumberOfFieldsId, fieldsToDisplay);
            propertyBlock.SetInt(FieldsPerRowId, fieldsPerRowToDisplay);
            propertyBlock.SetFloat(GridThicknessId, gridToDisplay * 0.5f);
            propertyBlock.SetFloat(SquareSizeId, sizeToDisplay);
            propertyBlock.SetInt(AddGradientId, gradientToDisplay ? 1 : 0);
            propertyBlock.SetColor(GradientColorAId, m_GradientA);
            propertyBlock.SetColor(GradientColorBId, m_GradientB);
            propertyBlock.SetFloat(GradientPowerId, m_GradientPower);
            propertyBlock.SetInt(SphereModeId, sphereModeToDisplay ? 1 : 0);
            propertyBlock.SetInt(MaterialModeId, m_Mode == ColorCheckerMode.Materials ? 1 : 0);
            propertyBlock.SetTexture(CheckerTextureId, textureToDisplay != null ? textureToDisplay : Texture2D.grayTexture);
            propertyBlock.SetInt(TextureModeId, m_Mode == ColorCheckerMode.Texture ? 1 : 0);
            propertyBlock.SetInt(ReflectionModeId, m_Mode == ColorCheckerMode.Reflection ? 1 : 0);
            propertyBlock.SetTexture(RawTextureId, m_UserTextureRaw != null ? m_UserTextureRaw : Texture2D.blackTexture);
            propertyBlock.SetInt(RawTextureAvailableId, m_UserTextureRaw != null ? 1 : 0);
            propertyBlock.SetInt(RawTexturePreExposureId, m_UnlitTextureExposure ? 1 : 0);
            propertyBlock.SetFloat(TextureSliceId, m_TextureSlice);
            m_ColorCheckerRenderer.SetPropertyBlock(propertyBlock);
        }

        internal void UpdateGeometry()
        {
            EnsureRendererReferences();
            if (m_ColorCheckerFilter == null)
                return;

            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            if (cube == null || sphere == null)
                return;

            var fieldCountToDisplay = Mathf.Max(1, fieldsToDisplay);
            var rowFieldCount = Mathf.Max(1, fieldsPerRowToDisplay);
            var rowCount = Mathf.CeilToInt(fieldCountToDisplay / (float)rowFieldCount);
            var mesh = new Mesh
            {
                name = "VividRP Color Checker Mesh",
                hideFlags = HideFlags.HideAndDontSave,
            };

            if (sphereModeToDisplay)
            {
                var instanceCount = gradientToDisplay ? fieldCountToDisplay + 1 : fieldCountToDisplay;
                var combine = new CombineInstance[instanceCount];
                var scale = Mathf.Lerp(1f, 0.01f, gridToDisplay) * sizeToDisplay * 0.5f;
                var lastFullRowStart = fieldCountToDisplay - (fieldCountToDisplay - ((rowCount - 1) * rowFieldCount));
                var fieldsInLastRow = fieldCountToDisplay % rowFieldCount;

                for (var fieldIndex = 0; fieldIndex < fieldCountToDisplay; fieldIndex++)
                {
                    var posX = fieldIndex % rowFieldCount * sizeToDisplay + sizeToDisplay * 0.5f;
                    if (fieldIndex + 1 > lastFullRowStart && fieldsInLastRow != 0)
                    {
                        var missing = rowFieldCount - fieldsInLastRow;
                        var spacing = missing / (float)(fieldsInLastRow * 2);
                        posX += sizeToDisplay * spacing + (fieldIndex - lastFullRowStart) * sizeToDisplay * spacing * 2f;
                    }

                    var posY = fieldIndex / rowFieldCount * sizeToDisplay + sizeToDisplay * 0.5f;
                    combine[fieldIndex].mesh = sphere;
                    combine[fieldIndex].transform = Matrix4x4.TRS(
                        new Vector3(posX, posY, 0f),
                        Quaternion.identity,
                        new Vector3(scale, scale, scale));
                }

                if (gradientToDisplay)
                {
                    var gradientScale = new Vector3(sizeToDisplay * rowFieldCount, sizeToDisplay, 0.02f);
                    var gradientPos = new Vector3(gradientScale.x * 0.5f, gradientScale.y * 0.5f - sizeToDisplay, 0f);
                    combine[fieldCountToDisplay].mesh = cube;
                    combine[fieldCountToDisplay].transform = Matrix4x4.TRS(gradientPos, Quaternion.identity, gradientScale);
                }

                mesh.CombineMeshes(combine, true, true, false);
            }
            else
            {
                var rowCountWithGradient = gradientToDisplay ? rowCount + 1 : rowCount;
                var scale = new Vector3(sizeToDisplay * rowFieldCount, sizeToDisplay * rowCountWithGradient, 0.02f);
                var pos = new Vector3(scale.x * 0.5f, scale.y * 0.5f, 0f);
                if (gradientToDisplay)
                    pos.y -= sizeToDisplay;

                var combine = new[]
                {
                    new CombineInstance
                    {
                        mesh = cube,
                        transform = Matrix4x4.TRS(pos, Quaternion.identity, scale),
                    },
                };
                mesh.CombineMeshes(combine, true, true, false);
            }

            var previousMesh = m_ColorCheckerFilter.sharedMesh;
            m_ColorCheckerFilter.sharedMesh = mesh;
            if (previousMesh != null && previousMesh.hideFlags == HideFlags.HideAndDontSave)
                DestroyGeneratedObject(previousMesh);
        }

        internal void UpdateTexture(Color32[] colors)
        {
            EnsureTexture();
            if (m_ColorCheckerTexture == null)
                return;

            for (var y = 0; y < TextureSize; y++)
            {
                for (var x = 0; x < TextureSize; x++)
                {
                    var index = x + y * TextureSize;
                    m_ColorCheckerTexture.SetPixel(x, y, index < colors.Length ? colors[index] : Color.grey);
                }
            }

            m_ColorCheckerTexture.Apply(false, false);
        }

        internal void ResetColors()
        {
            switch (m_Mode)
            {
                case ColorCheckerMode.Colors:
                    CopyColors(s_ColorPalette, m_CustomColors);
                    break;
                case ColorCheckerMode.Materials:
                    CopyColors(s_MaterialPalette, m_CustomMaterials);
                    ResetMaterialMetalFlagsFromPalette();
                    break;
            }

            Refresh();
        }

        internal void ApplyMaterialMetalFlags()
        {
            EnsureColorArrays();
            for (var i = 0; i < MaxMaterialFields; i++)
            {
                var color = m_CustomMaterials[i];
                color.a = m_IsMetalBools[i] ? byte.MaxValue : byte.MinValue;
                m_CustomMaterials[i] = color;
            }
        }

        private static Color32[] CreateColorPalette()
        {
            return (Color32[])s_ColorPalette.Clone();
        }

        private static Color32[] CreateMaterialPalette()
        {
            return (Color32[])s_MaterialPalette.Clone();
        }

        private static bool[] CreateMaterialMetalFlags()
        {
            var flags = new bool[MaxMaterialFields];
            for (var i = 0; i < flags.Length; i++)
                flags[i] = s_MaterialPalette[i].a == byte.MaxValue;
            return flags;
        }

        private void ClampSerializedSettings()
        {
            m_FieldCount = Mathf.Clamp(m_FieldCount, 1, MaxColorFields);
            m_MaterialFieldsCount = Mathf.Clamp(m_MaterialFieldsCount, 1, MaxMaterialFields);
            m_FieldsPerRow = Mathf.Clamp(m_FieldsPerRow, 1, TextureSize);
            m_GridThickness = Mathf.Clamp(m_GridThickness, 0f, 0.49f);
            m_FieldSize = Mathf.Max(0.001f, m_FieldSize);
            m_GradientPower = Mathf.Max(0.01f, m_GradientPower);
            m_TextureSlice = Mathf.Clamp01(m_TextureSlice);
        }

        private void EnsureColorArrays()
        {
            EnsureColorArray(ref m_CustomColors, s_ColorPalette, MaxColorFields);
            EnsureColorArray(ref m_CustomMaterials, s_MaterialPalette, MaxMaterialFields);

            if (m_IsMetalBools == null || m_IsMetalBools.Length != MaxMaterialFields)
            {
                var newFlags = CreateMaterialMetalFlags();
                if (m_IsMetalBools != null)
                {
                    var length = Mathf.Min(m_IsMetalBools.Length, newFlags.Length);
                    Array.Copy(m_IsMetalBools, newFlags, length);
                }

                m_IsMetalBools = newFlags;
            }
        }

        private static void EnsureColorArray(ref Color32[] array, Color32[] defaults, int length)
        {
            if (array != null && array.Length == length)
                return;

            var newArray = new Color32[length];
            CopyColors(defaults, newArray);

            if (array != null)
            {
                var copyLength = Mathf.Min(array.Length, newArray.Length);
                Array.Copy(array, newArray, copyLength);
            }

            array = newArray;
        }

        private static void CopyColors(Color32[] source, Color32[] destination)
        {
            if (destination == null)
                return;

            var length = Mathf.Min(source.Length, destination.Length);
            for (var i = 0; i < length; i++)
                destination[i] = source[i];
        }

        private void ResetMaterialMetalFlagsFromPalette()
        {
            EnsureColorArrays();
            for (var i = 0; i < MaxMaterialFields; i++)
                m_IsMetalBools[i] = s_MaterialPalette[i].a == byte.MaxValue;
        }

        private void EnsureSteppedLuminance()
        {
            for (var i = 0; i < m_SteppedLuminance.Length; i++)
            {
                var luminance = (byte)(17 * i);
                m_SteppedLuminance[i] = new Color32(luminance, luminance, luminance, byte.MaxValue);
            }
        }

        private void EnsureTexture()
        {
            if (m_ColorCheckerTexture != null
                && m_ColorCheckerTexture.width == TextureSize
                && m_ColorCheckerTexture.height == TextureSize)
            {
                return;
            }

            DestroyGeneratedObject(m_ColorCheckerTexture);
            m_ColorCheckerTexture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false, false)
            {
                name = "ProceduralColorcheckerTexture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            UpdateTexture(Array.Empty<Color32>());
        }

        private void SetSingleTextureColor(Color color)
        {
            EnsureTexture();
            if (m_ColorCheckerTexture == null)
                return;

            m_ColorCheckerTexture.SetPixel(0, 0, color);
            m_ColorCheckerTexture.Apply(false, false);
        }

        private void EnsureGeometryObject()
        {
            if (m_ColorCheckerObject == null)
            {
                m_ColorCheckerObject = new GameObject(GeometryName)
                {
                    tag = "EditorOnly",
                    hideFlags = HideFlags.DontSaveInBuild,
                };
                m_ColorCheckerObject.transform.SetParent(transform, false);
                m_ColorCheckerObject.transform.localPosition = Vector3.zero;
                m_ColorCheckerObject.transform.localRotation = Quaternion.identity;
                m_ColorCheckerObject.transform.localScale = Vector3.one;
            }

            tag = "EditorOnly";
            EnsureRendererReferences();

            if (m_ColorCheckerRenderer != null)
            {
                m_ColorCheckerRenderer.sharedMaterial = ResolveColorCheckerMaterial();
                m_ColorCheckerRenderer.shadowCastingMode = ShadowCastingMode.Off;
                m_ColorCheckerRenderer.receiveShadows = false;
                m_ColorCheckerRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }

            if (m_ColorCheckerFilter != null)
                m_ColorCheckerFilter.hideFlags = HideFlags.NotEditable;
        }

        private void EnsureRendererReferences()
        {
            if (m_ColorCheckerObject == null)
            {
                m_ColorCheckerRenderer = null;
                m_ColorCheckerFilter = null;
                return;
            }

            if (!m_ColorCheckerObject.TryGetComponent(out m_ColorCheckerRenderer))
                m_ColorCheckerRenderer = m_ColorCheckerObject.AddComponent<MeshRenderer>();

            if (!m_ColorCheckerObject.TryGetComponent(out m_ColorCheckerFilter))
                m_ColorCheckerFilter = m_ColorCheckerObject.AddComponent<MeshFilter>();
        }

        private Material ResolveColorCheckerMaterial()
        {
            if (m_ColorCheckerMaterial != null)
                return m_ColorCheckerMaterial;

            var shader = PipelineResourceManager.Get<VividRPCoreResources>()?.ColorCheckerShader;
            shader ??= Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{ShaderName}' for {nameof(ColorCheckerTool)}.", this);
                return null;
            }

            m_ColorCheckerMaterial = CoreUtils.CreateEngineMaterial(shader);
            m_ColorCheckerMaterial.name = "VividRP Color Checker Material";
            m_ColorCheckerMaterial.hideFlags = HideFlags.HideAndDontSave;
            return m_ColorCheckerMaterial;
        }

        private static void DestroyGeneratedObject(UnityEngine.Object obj)
        {
            if (obj == null)
                return;

            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }
    }
}
#endif

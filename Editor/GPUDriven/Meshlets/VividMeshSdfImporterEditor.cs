using Unity.Mathematics;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Editor.GPUDriven.Meshlets
{
    [CustomEditor(typeof(VividMeshSdfImporter))]
    internal sealed class VividMeshSdfImporterEditor : ScriptedImporterEditor
    {
        private enum SliceAxis { X, Y, Z }

        private SliceAxis m_Axis = SliceAxis.Z;
        private int m_Slice = -1;
        private Texture3D m_PreviewSource;
        private Texture2D m_Preview;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            ApplyRevertGUI();

            if (assetTarget is not VividMeshSdfAsset asset || asset.DistanceField == null)
                return;

            Texture3D field = asset.DistanceField;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Baked Grid", $"{field.width} × {field.height} × {field.depth}");
            EditorGUILayout.LabelField("Voxel Size", $"{asset.VoxelSize:G6} local units");
            EditorGUILayout.LabelField("Sign Mode", asset.SignMode.ToString());
            if (asset.SignMode == VividMeshSdfSignMode.ThinShell)
                EditorGUILayout.LabelField("Shell Thickness", $"{asset.ShellThickness:G6} local units");

            var axis = (SliceAxis)EditorGUILayout.EnumPopup("Slice Axis", m_Axis);
            var dimensions = new int3(field.width, field.height, field.depth);
            int maxSlice = dimensions[(int)axis] - 1;
            int startSlice = axis == m_Axis && m_Slice >= 0 ? Mathf.Min(m_Slice, maxSlice) : maxSlice / 2;
            int slice = EditorGUILayout.IntSlider("Slice", startSlice, 0, maxSlice);
            if (m_PreviewSource != field || m_Axis != axis || m_Slice != slice)
                UpdatePreview(asset, dimensions, axis, slice);

            EditorGUILayout.LabelField("Orange inside · Blue outside · White at the surface", EditorStyles.miniLabel);
            Rect rect = GUILayoutUtility.GetRect(1f, 260f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawPreviewTexture(rect, m_Preview, null, ScaleMode.ScaleToFit);
        }

        public override void OnDisable()
        {
            DestroyImmediate(m_Preview);
            base.OnDisable();
        }

        private void UpdatePreview(VividMeshSdfAsset asset, int3 dimensions, SliceAxis axis, int slice)
        {
            int uAxis = axis == SliceAxis.X ? 1 : 0;
            int vAxis = axis == SliceAxis.Z ? 1 : 2;
            int width = dimensions[uAxis];
            int height = dimensions[vAxis];
            if (m_Preview == null || m_Preview.width != width || m_Preview.height != height)
            {
                DestroyImmediate(m_Preview);
                m_Preview = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                };
            }

            var distances = asset.DistanceField.GetPixelData<half>(0);
            var pixels = m_Preview.GetPixelData<Color32>(0);
            var inside = new Color(0.84f, 0.27f, 0.08f);
            var outside = new Color(0.06f, 0.22f, 0.54f);
            var coord = new int3();
            coord[(int)axis] = slice;
            for (int y = 0; y < height; y++)
            {
                coord[vAxis] = y;
                for (int x = 0; x < width; x++)
                {
                    coord[uAxis] = x;
                    int index = coord.x + dimensions.x * (coord.y + dimensions.y * coord.z);
                    float distance = distances[index];
                    float t = Mathf.Clamp01(Mathf.Abs(distance) / (4f * asset.VoxelSize));
                    pixels[x + y * width] = Color.Lerp(Color.white, distance < 0f ? inside : outside, t);
                }
            }
            m_Preview.Apply(false);
            m_PreviewSource = asset.DistanceField;
            m_Axis = axis;
            m_Slice = slice;
        }
    }
}

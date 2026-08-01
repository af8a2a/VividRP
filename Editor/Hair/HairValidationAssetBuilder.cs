using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    internal static class HairValidationAssetBuilder
    {
        private const string OutputFolder =
            "Assets/VividRPValidation/Hair";
        private const string MeshPath =
            OutputFolder + "/ChiangHairValidationDots.asset";
        private const string MaterialPath =
            OutputFolder + "/ChiangHairValidation.mat";

        [MenuItem("VividRP/Hair/Create Chiang Validation Assets")]
        private static void CreateValidationAssets()
        {
            var shader = Shader.Find("VividRP/Material/Hair");
            if (shader == null)
            {
                Debug.LogError(
                    "Cannot create Hair validation assets because "
                    + "VividRP/Material/Hair was not imported.");
                return;
            }

            EnsureOutputFolder();
            var generatedMesh = HairDotsMeshBuilder.Build(
                CreateValidationSegments());
            generatedMesh.name = "Chiang Hair Validation DOTS";
            var mesh = SaveOrReplaceMesh(generatedMesh);
            var material = SaveOrUpdateMaterial(shader);
            AssetDatabase.SaveAssetIfDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(material);
            AssetDatabase.SaveAssets();
            Selection.activeObject = mesh;

            Debug.Log(
                $"Created Chiang Hair validation assets at {OutputFolder}.");
        }

        internal static IReadOnlyList<HairStrandSegment>
            CreateValidationSegments(
                int strandCount = 32,
                int segmentCountPerStrand = 8)
        {
            strandCount = Mathf.Max(strandCount, 1);
            segmentCountPerStrand = Mathf.Max(
                segmentCountPerStrand,
                1);
            var segments = new List<HairStrandSegment>(
                strandCount * segmentCountPerStrand);

            for (var strandIndex = 0;
                 strandIndex < strandCount;
                 strandIndex++)
            {
                var strandFraction = (strandIndex + 0.5f) / strandCount;
                var angle = strandIndex
                    * 2.39996323f;
                var bundleRadius = 0.12f
                    * Mathf.Sqrt(strandFraction);
                var root = new Vector3(
                    Mathf.Cos(angle) * bundleRadius,
                    0.0f,
                    Mathf.Sin(angle) * bundleRadius);

                for (var segmentIndex = 0;
                     segmentIndex < segmentCountPerStrand;
                     segmentIndex++)
                {
                    var startU = segmentIndex
                        / (float)segmentCountPerStrand;
                    var endU = (segmentIndex + 1.0f)
                        / segmentCountPerStrand;
                    var start = CreatePoint(
                        root,
                        angle,
                        startU,
                        strandFraction);
                    var end = CreatePoint(
                        root,
                        angle,
                        endU,
                        strandFraction);
                    segments.Add(new HairStrandSegment(start, end));
                }
            }

            return segments;
        }

        private static HairStrandPoint CreatePoint(
            Vector3 root,
            float angle,
            float strandU,
            float strandFraction)
        {
            var curlAngle = angle + strandU * 1.75f;
            var curlRadius = 0.035f * strandU * strandU;
            var position = root + new Vector3(
                Mathf.Cos(curlAngle) * curlRadius,
                strandU * 0.75f,
                Mathf.Sin(curlAngle) * curlRadius);
            var radius = Mathf.Lerp(0.006f, 0.0015f, strandU);
            return new HairStrandPoint(
                position,
                radius,
                new Vector2(strandU, strandFraction));
        }

        private static void EnsureOutputFolder()
        {
            const string root = "Assets/VividRPValidation";
            if (!AssetDatabase.IsValidFolder(root))
            {
                AssetDatabase.CreateFolder(
                    "Assets",
                    "VividRPValidation");
            }

            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                AssetDatabase.CreateFolder(
                    root,
                    "Hair");
            }
        }

        private static Mesh SaveOrReplaceMesh(Mesh generatedMesh)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generatedMesh, MeshPath);
                return generatedMesh;
            }

            Undo.RecordObject(existing, "Update Chiang Hair Validation Mesh");
            EditorUtility.CopySerialized(generatedMesh, existing);
            Object.DestroyImmediate(generatedMesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static Material SaveOrUpdateMaterial(Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(
                MaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "Chiang Hair Validation"
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                Undo.RecordObject(
                    material,
                    "Update Chiang Hair Validation Material");
                material.shader = shader;
            }

            material.SetColor(
                "_HairBaseColor",
                new Color(0.227f, 0.130f, 0.035f, 1.0f));
            material.SetFloat("_HairAbsorptionModel", 1.0f);
            material.SetFloat("_HairMelanin", 0.805f);
            material.SetFloat("_HairMelaninRedness", 0.05f);
            material.SetFloat("_HairLongitudinalRoughness", 0.4f);
            material.SetFloat("_HairAzimuthalRoughness", 0.6f);
            material.SetFloat("_HairIor", 1.55f);
            material.SetFloat("_HairCuticleAngleDegrees", 3.0f);
            material.SetFloat("_HairFresnelApproximation", 1.0f);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}

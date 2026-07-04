using UnityEditor;
using UnityEngine;

namespace VividRP.Editor
{
    public sealed class ParticleUnlitShaderGUI : LWGUI.LWGUI
    {
        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            ParticleUnlitMaterialUtility.SetupMaterial(material, oldShader, true);
        }

        public override void ValidateMaterial(Material material)
        {
            base.ValidateMaterial(material);
            ParticleUnlitMaterialUtility.SetupMaterial(material, null, true);
        }
    }

    internal static class ParticleUnlitMaterialUtility
    {
        internal const string ParticleUnlitShaderName = "VividRP/Particles/Unlit";
        private const string ParticleUnlitShaderRelativePath = "Shaders/Particles/ParticleUnlit.shader";

        internal static Shader GetParticleUnlitShader()
        {
            Shader shader = Shader.Find(ParticleUnlitShaderName);
            if (shader != null)
                return shader;

            string[] candidatePaths = VividPackagePathUtility.GetCandidateAssetPaths(ParticleUnlitShaderRelativePath);
            for (int index = 0; index < candidatePaths.Length; index++)
            {
                shader = AssetDatabase.LoadAssetAtPath<Shader>(candidatePaths[index]);
                if (shader != null)
                    return shader;
            }

            return null;
        }

        internal static void SetupMaterial(Material material, Shader oldShader, bool logWarnings)
        {
            UnlitMaterialUtility.SetupMaterial(material, oldShader, logWarnings);
        }
    }
}

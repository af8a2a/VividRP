using UnityEditor;
using UnityEngine;

namespace UnityGLTF
{
    public class TextureImporterHelper
    {
        public static TextureImporterFormat GetAutomaticFormat(Texture2D texture, BuildTarget buildTarget)
        {
            // Unity 6.7 no longer exposes the internal texture format helpers used by
            // upstream UnityGLTF. RGBA32 is a stable public fallback and keeps the
            // embedded importer independent from UnityEditor internals.
            return TextureImporterFormat.RGBA32;
        }
    }
}

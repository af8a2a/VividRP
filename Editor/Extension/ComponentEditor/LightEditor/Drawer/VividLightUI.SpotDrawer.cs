namespace UnityEditor.Rendering.Universal
{
    partial class VividLightUI
    {
        static void DrawSpotShapeContent(VividSerializedLight serializedLight, Editor owner)
        {
            serializedLight.settings.DrawInnerAndOuterSpotAngle();
        }
    }
}
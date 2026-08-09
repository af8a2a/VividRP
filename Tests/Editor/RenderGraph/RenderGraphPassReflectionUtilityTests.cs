using System.Reflection;
using NUnit.Framework;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class RenderGraphPassReflectionUtilityTests
    {
        [Test]
        public void GetRenderGraphResourceName_ReturnsAttributeName_WhenResourceDefinesDisplayName()
        {
            var field = typeof(DrawObjectPass).GetField("m_ColorTarget", BindingFlags.Instance | BindingFlags.NonPublic);
            var attr = field?.GetCustomAttribute<RenderGraphResource>();

            var resourceName = field.GetRenderGraphResourceName(attr);

            Assert.That(resourceName, Is.EqualTo("Color"));
        }

        [Test]
        public void GetRenderGraphResourceName_ReturnsFieldName_WhenResourceNameIsNotProvided()
        {
            var field = typeof(FullScreenPass).GetField("texture", BindingFlags.Instance | BindingFlags.NonPublic);
            var attr = field?.GetCustomAttribute<RenderGraphResource>();

            var resourceName = field.GetRenderGraphResourceName(attr);

            Assert.That(resourceName, Is.EqualTo("texture"));
        }

        [Test]
        public void GetInstanceField_ReturnsRenamedField_WhenFormerSerializedNameMatches()
        {
            var field = (typeof(MotionVectorPass)).GetInstanceField(
                "m_MotionVectorDepthTexture");

            Assert.That(field, Is.Not.Null);
            Assert.That(field.Name, Is.EqualTo("m_CameraDepthStencilTexture"));
        }
    }
}

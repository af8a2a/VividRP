using System.Reflection;
using NUnit.Framework;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class BloomPassTests
    {
        [Test]
        public void BloomPass_UsesStableResourceLayout_ForSourceOverrides()
        {
            Assert.That(typeof(IStablePassResourceLayout).IsAssignableFrom(typeof(BloomPass)), Is.True);
        }

        [Test]
        public void BloomPass_CachesMipHandleNames_ForPrepareReuse()
        {
            var downNames = GetPrivateStaticStringArray("s_MipDownNames");
            var upNames = GetPrivateStaticStringArray("s_MipUpNames");

            Assert.That(downNames, Has.Length.EqualTo(16));
            Assert.That(upNames, Has.Length.EqualTo(16));
            Assert.That(downNames[0], Is.EqualTo("BloomMipDown0"));
            Assert.That(downNames[15], Is.EqualTo("BloomMipDown15"));
            Assert.That(upNames[0], Is.EqualTo("BloomMipUp0"));
            Assert.That(upNames[15], Is.EqualTo("BloomMipUp15"));
        }

        private static string[] GetPrivateStaticStringArray(string fieldName)
        {
            var field = typeof(BloomPass).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (string[])field.GetValue(null);
        }
    }
}

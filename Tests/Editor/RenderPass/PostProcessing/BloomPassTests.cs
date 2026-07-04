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

        [Test]
        public void BloomSettingsData_DefaultsExperimentalSpdDownsampleOff()
        {
            var settings = BloomSettingsData.CreateDefault();

            Assert.That(settings.experimentalSpdDownsample, Is.False);
        }

        [Test]
        public void ShouldUseSpdDownsample_RequiresRequestAndEligibleResources()
        {
            Assert.That(BloomPass.ShouldUseSpdDownsample(false, 8, true, true), Is.False);
            Assert.That(BloomPass.ShouldUseSpdDownsample(true, 1, true, true), Is.False);
            Assert.That(BloomPass.ShouldUseSpdDownsample(true, 14, true, true), Is.False);
            Assert.That(BloomPass.ShouldUseSpdDownsample(true, 8, false, true), Is.False);
            Assert.That(BloomPass.ShouldUseSpdDownsample(true, 8, true, false), Is.False);
            Assert.That(BloomPass.ShouldUseSpdDownsample(true, 8, true, true), Is.True);
            Assert.That(BloomPass.ShouldUseSpdDownsample(true, 13, true, true), Is.True);
        }

        [Test]
        public void GetBoundSpdMipIndex_ClampsToLastAvailableMip()
        {
            Assert.That(BloomPass.GetBoundSpdMipIndex(0, 8), Is.EqualTo(0));
            Assert.That(BloomPass.GetBoundSpdMipIndex(7, 8), Is.EqualTo(7));
            Assert.That(BloomPass.GetBoundSpdMipIndex(12, 8), Is.EqualTo(7));
            Assert.That(BloomPass.GetBoundSpdMipIndex(12, 13), Is.EqualTo(12));
            Assert.That(BloomPass.GetBoundSpdMipIndex(12, 0), Is.EqualTo(0));
        }

        private static string[] GetPrivateStaticStringArray(string fieldName)
        {
            var field = typeof(BloomPass).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (string[])field.GetValue(null);
        }
    }
}

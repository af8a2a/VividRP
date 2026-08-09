using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class HairShaderContractTests
    {
        [Test]
        public void HairShader_ImportsWithoutCompilerMessages()
        {
            Shader shader = Shader.Find("VividRP/Material/Hair");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.passCount, Is.EqualTo(2));

            ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
            Assert.That(
                messages,
                Is.Empty,
                string.Join(
                    "\n",
                    messages.Select(message =>
                        $"{message.file}:{message.line}: {message.message}")));
        }
    }
}

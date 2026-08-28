using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class PassRecorderCascadedShadowIntegrationTests
    {
        [SetUp]
        public void SetUp()
        {
            PassRecorder.Dispose();
        }

        [TearDown]
        public void TearDown()
        {
            PassRecorder.Dispose();
        }

        [Test]
        public void Compile_InsertsCascadedShadowPassesAroundDirectionalShadowPass_WhenRuntimeGraphUsesRayTracedDirectionalShadow()
        {
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<DirectionalRayTracedShadowPass>(),
            });

            try
            {
                Compile(graphAsset);

                var passes = GetCompiledPasses();

                Assert.That(
                    passes.Select(pass => pass.GetType()),
                    Is.EqualTo(new[]
                    {
                        typeof(CSMShadowPass),
                        typeof(DirectionalRayTracedShadowPass),
                        typeof(CSMShadowResolvePass),
                    }));

                var runtimePassDefinitions = GetRuntimePassDefinitions();
                Assert.That(runtimePassDefinitions, Has.Count.EqualTo(passes.Count));
                Assert.That(runtimePassDefinitions[0], Is.Null);
                Assert.That(runtimePassDefinitions[1], Is.Not.Null);
                Assert.That(runtimePassDefinitions[2], Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void Compile_SharesDirectionalShadowResourcesWithInjectedCSMResolvePass_WhenRuntimeGraphUsesRayTracedDirectionalShadow()
        {
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<DirectionalRayTracedShadowPass>(),
            });

            try
            {
                Compile(graphAsset);

                var passes = GetCompiledPasses();
                var csmShadowPass = passes[0] as CSMShadowPass;
                var directionalShadowPass = passes[1] as DirectionalRayTracedShadowPass;
                var csmResolvePass = passes[2] as CSMShadowResolvePass;

                Assert.That(csmShadowPass, Is.Not.Null);
                Assert.That(directionalShadowPass, Is.Not.Null);
                Assert.That(csmResolvePass, Is.Not.Null);
                Assert.That(
                    GetTextureField(csmResolvePass, "m_DepthTexture"),
                    Is.SameAs(GetTextureField(directionalShadowPass, "m_DepthTexture")));
                Assert.That(
                    GetTextureField(csmResolvePass, "m_GBuffer1"),
                    Is.SameAs(GetTextureField(directionalShadowPass, "m_GBuffer1")));
                Assert.That(
                    GetTextureField(csmResolvePass, "m_DirectionalShadowTexture"),
                    Is.SameAs(GetTextureField(directionalShadowPass, "m_DirectionalShadowTexture")));
                Assert.That(
                    GetTextureField(csmResolvePass, "m_CSMShadowAtlas"),
                    Is.SameAs(GetTextureField(csmShadowPass, "m_ShadowAtlas")));
            }
            finally
            {
                Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void Compile_CachesCascadedShadowCasterPassPresence_UntilRecorderIsDisposed()
        {
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<CSMShadowPass>(),
            });

            try
            {
                Assert.That(PassRecorder.HasCascadedShadowCasterPass, Is.False);

                Compile(graphAsset);

                Assert.That(PassRecorder.HasCascadedShadowCasterPass, Is.True);

                PassRecorder.Dispose();

                Assert.That(PassRecorder.HasCascadedShadowCasterPass, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void Compile_LegacyMeshletShadowPass_DoesNotRegisterUnifiedShadowRendering()
        {
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<MeshletShadowPass>(),
            });

            try
            {
                Assert.That(PassRecorder.HasCascadedShadowCasterPass, Is.False);

                Compile(graphAsset);

                Assert.That(PassRecorder.HasCascadedShadowCasterPass, Is.False);
                Assert.That(GetCompiledPasses(), Is.Empty);

                PassRecorder.Dispose();

                Assert.That(PassRecorder.HasCascadedShadowCasterPass, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void Compile_LegacyMeshletShadowPass_ForwardsAtlasWithoutRecordingASecondPass()
        {
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<CSMShadowPass>(),
            });
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<MeshletShadowPass>(),
                ResourceBindings =
                {
                    new RenderGraphPassResourceBinding
                    {
                        FieldName = "m_CSMShadowAtlas",
                        ResourceKind = RenderGraphResourceKind.Texture,
                        SourceKind = RenderGraphPassBindingSourceKind.PassField,
                        SourcePassIndex = 0,
                        SourceFieldName = "m_ShadowAtlas",
                        ConnectionKind = RenderGraphPassBindingConnectionKind.Input,
                    },
                },
            });
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<CSMShadowResolvePass>(),
                ResourceBindings =
                {
                    new RenderGraphPassResourceBinding
                    {
                        FieldName = "m_CSMShadowAtlas",
                        ResourceKind = RenderGraphResourceKind.Texture,
                        SourceKind = RenderGraphPassBindingSourceKind.PassField,
                        SourcePassIndex = 1,
                        SourceFieldName = "m_CSMShadowAtlas",
                        ConnectionKind = RenderGraphPassBindingConnectionKind.Input,
                    },
                },
            });

            try
            {
                Compile(graphAsset);

                var passes = GetCompiledPasses();
                Assert.That(
                    passes.Select(pass => pass.GetType()),
                    Is.EqualTo(new[]
                    {
                        typeof(CSMShadowPass),
                        typeof(CSMShadowResolvePass),
                    }));
                Assert.That(
                    GetTextureField(passes[1], "m_CSMShadowAtlas"),
                    Is.SameAs(GetTextureField(passes[0], "m_ShadowAtlas")));
            }
            finally
            {
                Object.DestroyImmediate(graphAsset);
            }
        }

        private static void Compile(RenderGraphData graphAsset)
        {
            var method = typeof(PassRecorder).GetMethod("Compile", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { graphAsset });
        }

        private static IList<IRenderPass> GetCompiledPasses()
        {
            var field = typeof(PassRecorder).GetField("s_RenderPasses", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null);
            return (IList<IRenderPass>)field.GetValue(null);
        }

        private static IList<RenderGraphPassDefinition> GetRuntimePassDefinitions()
        {
            var field = typeof(PassRecorder).GetField("s_RuntimePassDefinitions", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null);
            return (IList<RenderGraphPassDefinition>)field.GetValue(null);
        }

        private static RenderGraphTexture GetTextureField(object pass, string fieldName)
        {
            Assert.That(pass, Is.Not.Null);

            var field = pass.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (RenderGraphTexture)field.GetValue(pass);
        }

        private static string GetPassTypeName<T>()
        {
            var type = typeof(T);
            return $"{type.FullName}, {type.Assembly.GetName().Name}";
        }
    }
}

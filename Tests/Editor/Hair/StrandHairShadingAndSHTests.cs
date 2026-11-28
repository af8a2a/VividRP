using System;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class StrandHairShadingAndSHTests
    {
        [Test]
        public void L1SH_ConstantFieldReconstructsExactValue()
        {
            const int rayCount = 256;
            const float goldenAngle = 2.39996322973f;
            const float dOmega = 4.0f * Mathf.PI / rayCount;
            const float constantEvents = 2.5f;

            Vector4 sh = Vector4.zero;
            for (int r = 0; r < rayCount; r++)
            {
                float z = 1.0f - 2.0f * ((r + 0.5f) / rayCount);
                float radial = Mathf.Sqrt(Mathf.Clamp01(1.0f - z * z));
                float phi = goldenAngle * r;
                Vector3 dir = new Vector3(Mathf.Cos(phi) * radial, z, Mathf.Sin(phi) * radial);
                sh += HairGroomL1SH.EvaluateBasis(dir) * (constantEvents * dOmega);
            }

            Assert.That(HairGroomL1SH.EvaluateScatterEvents(sh, Vector3.up), Is.EqualTo(constantEvents).Within(0.05f));
            Assert.That(HairGroomL1SH.EvaluateScatterEvents(sh, Vector3.right), Is.EqualTo(constantEvents).Within(0.05f));
            Assert.That(HairGroomL1SH.EvaluateScatterEvents(sh, Vector3.back), Is.EqualTo(constantEvents).Within(0.05f));
        }

        [Test]
        public void L1SH_BiasedNegativeMissPreservesBacklitRim()
        {
            // Simulate a strand on the outer silhouette: +Z hemisphere is occluded (5 events),
            // -Z hemisphere is open sky (-5 negative miss bias per IJGC SIGGRAPH 2025).
            const int rayCount = 256;
            const float goldenAngle = 2.39996322973f;
            const float dOmega = 4.0f * Mathf.PI / rayCount;

            Vector4 sh = Vector4.zero;
            for (int r = 0; r < rayCount; r++)
            {
                float z = 1.0f - 2.0f * ((r + 0.5f) / rayCount);
                float radial = Mathf.Sqrt(Mathf.Clamp01(1.0f - z * z));
                float phi = goldenAngle * r;
                Vector3 dir = new Vector3(Mathf.Cos(phi) * radial, z, Mathf.Sin(phi) * radial);
                float biased = dir.z > 0.0f ? HairGroomL1SH.MaxScatterEvents : -HairGroomL1SH.MaxScatterEvents;
                sh += HairGroomL1SH.EvaluateBasis(dir) * (biased * dOmega);
            }

            float openEvents = HairGroomL1SH.EvaluateScatterEvents(sh, Vector3.back);
            float occEvents = HairGroomL1SH.EvaluateScatterEvents(sh, Vector3.forward);
            float openTrans = HairGroomL1SH.EvaluateTransmittance(sh, Vector3.back, 0.72f);
            float occTrans = HairGroomL1SH.EvaluateTransmittance(sh, Vector3.forward, 0.72f);

            Assert.That(openEvents, Is.EqualTo(0.0f).Within(1e-4f), "Open hemisphere must clamp to 0 scatter events.");
            Assert.That(openTrans, Is.EqualTo(1.0f).Within(1e-4f), "Open silhouette rim must remain fully unoccluded.");
            Assert.That(occEvents, Is.GreaterThan(2.5f), "Occluded hemisphere must report strong self-shadowing.");
            Assert.That(occTrans, Is.LessThan(0.25f), "Occluded hemisphere transmittance must be significantly attenuated.");
        }

        [Test]
        public void HairGroomAsset_UpdateBakedVisibilitySH_PersistsBinaryAndSetsFlag()
        {
            var strands = new[] { new HairGroomStrandDesc(0, 3, Vector2.zero, 0, 0.5f) };
            var verts = new[]
            {
                new HairGroomRestVertex(new Vector3(0f, 0f, 0f), 0.0005f, Vector3.up, 0.0f, 0.2f),
                new HairGroomRestVertex(new Vector3(0f, 0.05f, 0f), 0.0004f, Vector3.up, 0.5f, 0.2f),
                new HairGroomRestVertex(new Vector3(0f, 0.10f, 0f), 0.0003f, Vector3.up, 1.0f, 0.2f),
            };
            var segs = new[]
            {
                new HairGroomSegmentIndex(0, 0, 0),
                new HairGroomSegmentIndex(1, 0, 1),
            };

            byte[] raw = HairGroomBinaryCodec.WriteToBytes(
                strands, verts, segs, new Bounds(Vector3.zero, Vector3.one * 0.2f), 0.0005f, HairGroomFlags.None);

            var asset = ScriptableObject.CreateInstance<HairGroomAsset>();
            try
            {
                asset.ImportFromBinary(raw);
                Assert.That(asset.HasBakedVisibilitySH, Is.False);
                int prevVersion = asset.ContentVersion;

                var baked = new[]
                {
                    new HairGroomVisibilitySH(new Vector4(1.2f, -0.3f, 0.4f, 0.8f)),
                    new HairGroomVisibilitySH(new Vector4(0.5f,  0.1f, -0.2f, 0.3f)),
                    new HairGroomVisibilitySH(new Vector4(-0.9f, 0.0f, 0.5f, -0.4f)),
                };
                byte[] updated = asset.UpdateBakedVisibilitySH(baked);

                Assert.That(asset.HasBakedVisibilitySH, Is.True);
                Assert.That(asset.ContentVersion, Is.GreaterThan(prevVersion));

                var parsed = HairGroomBinaryCodec.Read(updated);
                Assert.That(parsed.VisibilitySH, Is.Not.Null);
                Assert.That(parsed.VisibilitySH.Length, Is.EqualTo(3));
                for (int i = 0; i < 3; i++)
                    Assert.That(parsed.VisibilitySH[i].Coefficients, Is.EqualTo(baked[i].Coefficients));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ScratchSlot_AllocatesShadingAndMaskBuffersAndReusesWithoutRegrowth()
        {
            using var slot = new StrandHairModelScratchSlot();
            slot.EnsureCapacity(vertexCount: 100, segmentCount: 80);

            Assert.That(slot.VisibleVertexMaskBuffer, Is.Not.Null);
            Assert.That(slot.VisibleVertexMaskBuffer.IsValid(), Is.True);
            Assert.That(slot.VisibleVertexMaskBuffer.count, Is.EqualTo(4)); // ceil(100 / 32)
            Assert.That(slot.VertexShadingBuffer, Is.Not.Null);
            Assert.That(slot.VertexShadingBuffer.IsValid(), Is.True);
            Assert.That(slot.VertexShadingBuffer.count, Is.EqualTo(100));

            var maskRef = slot.VisibleVertexMaskBuffer;
            var shadeRef = slot.VertexShadingBuffer;
            slot.EnsureCapacity(vertexCount: 90, segmentCount: 70);

            Assert.That(slot.VisibleVertexMaskBuffer, Is.SameAs(maskRef));
            Assert.That(slot.VertexShadingBuffer, Is.SameAs(shadeRef));
        }
    }
}

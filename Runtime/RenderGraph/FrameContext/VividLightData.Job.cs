using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace VividRP.Runtime
{
    partial class VividLightData
    {
        private static DirectionalLightData CreateDirectionalLightData(VividLightRenderData trackedLightData)
        {
            return new DirectionalLightData
            {
                directionWS = -trackedLightData.forwardWS,
                shadowStrength = trackedLightData.shadowStrength,
                color = trackedLightData.color,
                renderingLayerMask = trackedLightData.renderingLayerMask,
            };
        }

        private static PunctualLightData CreatePunctualLightData(VividLightRenderData trackedLightData)
        {
            var range = Mathf.Max(trackedLightData.range, 0.001f);
            GetSpotAngleParameters(trackedLightData.lightType, trackedLightData.innerSpotAngle, trackedLightData.spotAngle, out var angleScale, out var angleOffset);

            return new PunctualLightData
            {
                positionWS = trackedLightData.positionWS,
                range = range,
                color = trackedLightData.color,
                lightType = GetPunctualLightType(trackedLightData.lightType),
                directionWS = trackedLightData.forwardWS,
                angleScale = angleScale,
                angleOffset = angleOffset,
                inverseRangeSquared = trackedLightData.inverseRangeSquared > 0.0f
                    ? trackedLightData.inverseRangeSquared
                    : 1.0f / Mathf.Max(range * range, 1e-6f),
                shadowStrength = trackedLightData.shadowStrength,
                renderingLayerMask = trackedLightData.renderingLayerMask,
            };
        }

        private static AreaLightData CreateAreaLightData(VividLightRenderData trackedLightData)
        {
            var range = Mathf.Max(trackedLightData.range, 0.001f);
            var width = Mathf.Max(trackedLightData.areaSize.x, 0.0f);
            var height = trackedLightData.lightType == LightType.Tube
                ? 0.0f
                : Mathf.Max(trackedLightData.areaSize.y, 0.0f);

            return new AreaLightData
            {
                positionWS = trackedLightData.positionWS,
                rangeAttenuationScale = 1.0f / Mathf.Max(range * range, 1e-6f),
                color = trackedLightData.color,
                lightType = GetAreaLightType(trackedLightData.lightType),
                forwardWS = NormalizeDirection(trackedLightData.forwardWS, Vector3.forward),
                rangeAttenuationBias = 1.0f,
                rightWS = NormalizeDirection(trackedLightData.rightWS, Vector3.right),
                width = width,
                upWS = NormalizeDirection(trackedLightData.upWS, Vector3.up),
                height = height,
                renderingLayerMask = trackedLightData.renderingLayerMask,
                range = range,
                padding = Vector2.zero,
            };
        }

        private static PunctualLightCullData CreatePunctualLightCullData(PunctualLightData source)
        {
            GetPunctualLightCullingShapeData(
                source,
                out var directionWS,
                out var cosOuterAngle,
                out var radiusAtRange);
            GetPunctualLightCullingSphere(source, out var cullingCenterWS, out var cullingRadius);

            return new PunctualLightCullData
            {
                positionWS = source.positionWS,
                range = source.range,
                directionWS = directionWS,
                lightType = source.lightType,
                cosOuterAngle = cosOuterAngle,
                radiusAtRange = radiusAtRange,
                cullingCenterWS = cullingCenterWS,
                cullingRadius = cullingRadius,
            };
        }


        [BurstCompile]
        private struct BuildVisibleLightCandidatesJob : IJob
        {
            [ReadOnly] public NativeArray<VisibleLightRenderDataRecord> visibleLightRenderDataRecords;

            public bool collectDirectionalLights;
            public bool collectPunctualLights;
            public bool collectAreaLights;
            public NativeList<DirectionalLightCandidate> directionalLights;
            public NativeList<PunctualLightCandidate> punctualLights;
            public NativeList<AreaLightCandidate> areaLights;

            public void Execute()
            {
                for (var lightIndex = 0; lightIndex < visibleLightRenderDataRecords.Length; lightIndex++)
                {
                    var visibleLightRenderDataRecord = visibleLightRenderDataRecords[lightIndex];
                    var lightRenderData = visibleLightRenderDataRecord.lightRenderData;

                    if (collectDirectionalLights && lightRenderData.lightType == LightType.Directional)
                    {
                        directionalLights.AddNoResize(
                            CreateDirectionalLightCandidate(
                                visibleLightRenderDataRecord.visibleLightIndex,
                                lightRenderData));
                    }

                    if (collectPunctualLights && IsPunctualLightSupported(lightRenderData))
                    {
                        punctualLights.AddNoResize(
                            CreatePunctualLightCandidate(
                                lightRenderData));
                    }

                    if (collectAreaLights && IsAreaLightSupported(lightRenderData))
                    {
                        areaLights.AddNoResize(
                            CreateAreaLightCandidate(
                                lightRenderData));
                    }
                }
            }
        }

        [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private struct BuildPunctualLightClusteredCullDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<PunctualLightCullData> punctualLightCullData;

            [WriteOnly] public NativeArray<SFiniteLightBound> punctualLightBounds;

            [WriteOnly] public NativeArray<LightVolumeData> punctualLightVolumeData;

            public float4x4 worldToViewMatrix;

            public void Execute(int index)
            {
                var viewSpaceCullData = BuildPunctualLightViewSpaceCullDataRecord(
                    punctualLightCullData[index],
                    worldToViewMatrix);
                BuildPunctualLightVolumeDataAndBound(
                    punctualLightCullData[index],
                    viewSpaceCullData,
                    out var lightVolumeData,
                    out var lightBound);
                punctualLightVolumeData[index] = lightVolumeData;
                punctualLightBounds[index] = lightBound;
            }
        }
    }
}
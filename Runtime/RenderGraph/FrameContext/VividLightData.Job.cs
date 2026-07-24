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
                volumetricDimmer = GetVolumetricDimmer(trackedLightData),
                volumetricShadowDimmer = GetVolumetricShadowDimmer(trackedLightData),
                volumetricFadeDistance = GetVolumetricFadeDistance(trackedLightData),
                affectVolumetric = GetAffectVolumetric(trackedLightData),
            };
        }

        private static PunctualLightData CreatePunctualLightData(VividLightRenderData trackedLightData)
        {
            var range = Mathf.Max(trackedLightData.range, 0.001f);
            GetSpotAngleParameters(trackedLightData.lightType, trackedLightData.innerSpotAngle, trackedLightData.spotAngle, out var angleScale, out var angleOffset);
            var directionWS = NormalizeDirection(trackedLightData.forwardWS, Vector3.forward);
            var projectorSize = GetProjectorBoxSize(trackedLightData);
            GetPunctualLightAxisScales(
                trackedLightData.lightType,
                trackedLightData.innerSpotAngle,
                trackedLightData.spotAngle,
                projectorSize,
                out var rightAxisScale,
                out var upAxisScale);
            var rightWS = NormalizeDirection(trackedLightData.rightWS, Vector3.right) * rightAxisScale;
            var upWS = NormalizeDirection(trackedLightData.upWS, Vector3.up) * upAxisScale;
            var shapeRadius = Mathf.Max(trackedLightData.shapeRadius, 0.0f);

            return new PunctualLightData
            {
                positionWS = trackedLightData.positionWS,
                range = range,
                color = trackedLightData.color,
                lightType = GetPunctualLightType(trackedLightData.lightType),
                directionWS = directionWS,
                angleScale = angleScale,
                rightWS = rightWS,
                angleOffset = angleOffset,
                upWS = upWS,
                shapeRadiusSquared = trackedLightData.lightType == LightType.Box ? 0.0f : shapeRadius * shapeRadius,
                projectorSize = projectorSize,
                rangeAttenuationScale = trackedLightData.rangeAttenuationScale > 0.0f
                    ? trackedLightData.rangeAttenuationScale
                    : 1.0f / Mathf.Max(range * range, 1e-6f),
                rangeAttenuationBias = trackedLightData.rangeAttenuationBias > 0.0f
                    ? trackedLightData.rangeAttenuationBias
                    : 1.0f,
                shadowStrength = trackedLightData.shadowStrength,
                renderingLayerMask = trackedLightData.renderingLayerMask,
                volumetricDimmer = GetVolumetricDimmer(trackedLightData),
                volumetricShadowDimmer = GetVolumetricShadowDimmer(trackedLightData),
                volumetricFadeDistance = GetVolumetricFadeDistance(trackedLightData),
                affectVolumetric = GetAffectVolumetric(trackedLightData),
            };
        }

        private static AreaLightData CreateAreaLightData(VividLightRenderData trackedLightData)
        {
            var range = Mathf.Max(trackedLightData.range, 0.001f);
            var width = Mathf.Max(trackedLightData.areaSize.x, 0.0f);
            var height = trackedLightData.lightType == LightType.Tube
                ? 0.0f
                : Mathf.Max(trackedLightData.areaSize.y, 0.0f);
            var barnDoorAngleRadians = Mathf.Deg2Rad * Mathf.Clamp(trackedLightData.barnDoorAngle, 0.0f, 90.0f);

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
                cosBarnDoorAngle = Mathf.Cos(barnDoorAngleRadians),
                barnDoorLength = Mathf.Max(trackedLightData.barnDoorLength, 0.0f),
                volumetricDimmer = GetVolumetricDimmer(trackedLightData),
                volumetricShadowDimmer = GetVolumetricShadowDimmer(trackedLightData),
                volumetricFadeDistance = GetVolumetricFadeDistance(trackedLightData),
                affectVolumetric = GetAffectVolumetric(trackedLightData),
            };
        }

        private static VividReGIRLightData CreateReGIRLightData(VividLightRenderData trackedLightData)
        {
            var range = Mathf.Max(trackedLightData.range, 0.001f);
            GetSpotAngleParameters(trackedLightData.lightType, trackedLightData.innerSpotAngle, trackedLightData.spotAngle, out var angleScale, out var angleOffset);
            var isRectangleLight = trackedLightData.lightType == LightType.Rectangle;
            var barnDoorAngleRadians = Mathf.Deg2Rad
                * Mathf.Clamp(trackedLightData.barnDoorAngle, 0.0f, 90.0f);

            return new VividReGIRLightData
            {
                positionWS = trackedLightData.positionWS,
                range = range,
                color = trackedLightData.color,
                lightType = GetReGIRLightType(trackedLightData.lightType),
                directionWS = NormalizeDirection(trackedLightData.forwardWS, Vector3.forward),
                angleScale = angleScale,
                rightWS = NormalizeDirection(trackedLightData.rightWS, Vector3.right),
                angleOffset = angleOffset,
                upWS = NormalizeDirection(trackedLightData.upWS, Vector3.up),
                shapeRadius = Mathf.Max(trackedLightData.shapeRadius, 0.0f),
                areaSize = new Vector2(
                    Mathf.Max(trackedLightData.areaSize.x, 0.0f),
                    trackedLightData.lightType == LightType.Tube ? 0.0f : Mathf.Max(trackedLightData.areaSize.y, 0.0f)),
                power = ComputeReGIRPower(trackedLightData),
                renderingLayerMask = trackedLightData.renderingLayerMask,
                cosBarnDoorAngle = isRectangleLight
                    ? Mathf.Cos(barnDoorAngleRadians)
                    : 0.0f,
                barnDoorLength = isRectangleLight
                    ? Mathf.Max(trackedLightData.barnDoorLength, 0.0f)
                    : 0.0f,
            };
        }

        private static float ComputeReGIRPower(VividLightRenderData trackedLightData)
        {
            var basePower = GetLightIntensity(trackedLightData.color);
            if (basePower <= 0.0f)
                return 0.0f;

            return trackedLightData.lightType switch
            {
                LightType.Rectangle => basePower
                    * Mathf.Max(trackedLightData.areaSize.x, 0.0f)
                    * Mathf.Max(trackedLightData.areaSize.y, 0.0f),
                LightType.Tube => basePower * Mathf.Max(trackedLightData.areaSize.x, 0.0f),
                _ => basePower,
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
                rightWS = NormalizeDirection(source.rightWS, Vector3.right),
                projectorWidth = Mathf.Max(source.projectorSize.x, 0.0f),
                upWS = NormalizeDirection(source.upWS, Vector3.up),
                projectorHeight = Mathf.Max(source.projectorSize.y, 0.0f),
                cullingCenterWS = cullingCenterWS,
                cullingRadius = cullingRadius,
                affectVolumetric = source.affectVolumetric != 0u && source.volumetricDimmer > 0.0f ? 1u : 0u,
            };
        }

        private static uint GetAffectVolumetric(VividLightRenderData trackedLightData)
        {
            return (trackedLightData.flags & VividLightRenderDataFlags.AffectVolumetric) != 0 ? 1u : 0u;
        }

        private static float GetVolumetricDimmer(VividLightRenderData trackedLightData)
        {
            if (GetAffectVolumetric(trackedLightData) == 0u)
                return 0.0f;

            return Mathf.Clamp(
                trackedLightData.volumetricDimmer,
                0.0f,
                VividAdditionalLightData.MaxVolumetricDimmer);
        }

        private static float GetVolumetricShadowDimmer(VividLightRenderData trackedLightData)
        {
            if (GetAffectVolumetric(trackedLightData) == 0u)
                return 0.0f;

            return Mathf.Clamp01(trackedLightData.volumetricShadowDimmer);
        }

        private static float GetVolumetricFadeDistance(VividLightRenderData trackedLightData)
        {
            return Mathf.Max(trackedLightData.volumetricFadeDistance, 0.0f);
        }


        [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private struct CollectReGIRSceneLightRenderDataRecordsJob : IJob
        {
            [ReadOnly] public NativeArray<VividLightRenderData> sceneLightRenderDataRecords;

            public NativeList<VividLightRenderData> reGIRSceneLightRenderDataRecords;
            public Vector3 collectionBoxCenterWS;
            public Vector3 collectionBoxHalfExtents;

            public void Execute()
            {
                for (var lightIndex = 0; lightIndex < sceneLightRenderDataRecords.Length; lightIndex++)
                {
                    var trackedLightData = sceneLightRenderDataRecords[lightIndex];
                    if (!IsReGIRLightSupported(trackedLightData)
                        || !IsLightEnabledAndActive(trackedLightData)
                        || !IntersectsReGIRCollectionBox(
                            trackedLightData,
                            collectionBoxCenterWS,
                            collectionBoxHalfExtents))
                    {
                        continue;
                    }

                    reGIRSceneLightRenderDataRecords.AddNoResize(trackedLightData);
                }
            }
        }

        [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private struct BuildLightGridLightCandidatesJob : IJob
        {
            [ReadOnly] public NativeArray<VisibleLightRenderDataRecord> visibleLightRenderDataRecords;
            [ReadOnly] public NativeArray<VividLightRenderData> reGIRSceneLightRenderDataRecords;

            public NativeList<PunctualLightCandidate> punctualLights;
            public NativeList<AreaLightCandidate> areaLights;
            public NativeList<VividReGIRLightData> reGIRLights;
            public NativeList<SFiniteLightBound> punctualLightBounds;
            public NativeList<LightVolumeData> punctualLightVolumeData;
            public NativeList<SFiniteLightBound> areaLightBounds;
            public NativeList<LightVolumeData> areaLightVolumeData;
            public float4x4 worldToViewMatrix;

            public void Execute()
            {
                BuildVisibleLightGridCandidates();
                BuildReGIRLightCandidates();
            }

            private void BuildVisibleLightGridCandidates()
            {
                for (var lightIndex = 0; lightIndex < visibleLightRenderDataRecords.Length; lightIndex++)
                {
                    var lightRenderData = visibleLightRenderDataRecords[lightIndex].lightRenderData;

                    if (IsPunctualLightSupported(lightRenderData))
                    {
                        var candidate = CreatePunctualLightCandidate(lightRenderData);
                        var viewSpaceCullData = BuildPunctualLightViewSpaceCullDataRecord(
                            candidate.lightCullData,
                            worldToViewMatrix);
                        BuildPunctualLightVolumeDataAndBound(
                            candidate.lightCullData,
                            viewSpaceCullData,
                            out var lightVolumeData,
                            out var lightBound);

                        punctualLights.AddNoResize(candidate);
                        punctualLightBounds.AddNoResize(lightBound);
                        punctualLightVolumeData.AddNoResize(lightVolumeData);
                    }

                    if (IsAreaLightSupported(lightRenderData))
                    {
                        var candidate = CreateAreaLightCandidate(lightRenderData);
                        BuildAreaLightVolumeDataAndBound(
                            candidate.lightData,
                            worldToViewMatrix,
                            out var lightVolumeData,
                            out var lightBound);

                        areaLights.AddNoResize(candidate);
                        areaLightBounds.AddNoResize(lightBound);
                        areaLightVolumeData.AddNoResize(lightVolumeData);
                    }
                }
            }

            private void BuildReGIRLightCandidates()
            {
                for (var lightIndex = 0; lightIndex < reGIRSceneLightRenderDataRecords.Length; lightIndex++)
                    reGIRLights.AddNoResize(CreateReGIRLightData(reGIRSceneLightRenderDataRecords[lightIndex]));
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

        [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private struct BuildAreaLightClusteredCullDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<AreaLightData> areaLightData;

            [WriteOnly] public NativeArray<SFiniteLightBound> areaLightBounds;

            [WriteOnly] public NativeArray<LightVolumeData> areaLightVolumeData;

            public float4x4 worldToViewMatrix;

            public void Execute(int index)
            {
                BuildAreaLightVolumeDataAndBound(
                    areaLightData[index],
                    worldToViewMatrix,
                    out var lightVolumeData,
                    out var lightBound);
                areaLightVolumeData[index] = lightVolumeData;
                areaLightBounds[index] = lightBound;
            }
        }
    }
}

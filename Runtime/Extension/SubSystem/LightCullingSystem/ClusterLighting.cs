using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    public class ClusterLighting : ScriptableRenderPass
    {
        private static LocalKeyword s_BigTileVolumetricLightListKeyword;

        // Profiling tag
        private static string m_ScreenSpaceAABBTag = "ScreenSpaceAABB";
        private static string m_CoarseCullingTag = "CoarseCulling";
        private static string m_ClusterCullingTag = "ClusterCulling";
        private static string m_ClearLightListsTag = "ClearLightLists";
        private static string m_BigTileTag = "BigTile";

        private static ProfilingSampler m_ScreenSpaceAABBSampler = new ProfilingSampler(m_ScreenSpaceAABBTag);
        private static ProfilingSampler m_CoarseCullingSampler = new ProfilingSampler(m_CoarseCullingTag);
        private static ProfilingSampler m_ClusterCullingSampler = new ProfilingSampler(m_ClusterCullingTag);
        private static ProfilingSampler m_ClearLightListsSampler = new ProfilingSampler(m_ClearLightListsTag);

        // Public Variables
        internal ShaderVariablesLightList lightCBuffer;

        private int m_MaxDirectionalLightsOnScreen = 16;

        private int m_MaxPunctualLightsOnScreen = 512;

        // TODO: change to m_MaxDirectionalLightsOnScreen + m_MaxPunctualLightsOnScreen(512) + m_MaxAreaLightsOnScreen + m_MaxEnvLightsOnScreen
        private int m_MaxLightOnScreen = 16 + 512;

        // Big Tile
        private ComputeShader bigTilePrepassShader;
        private int bigTilePrepassKernel;
        // private int numBigTilesX, numBigTilesY;

        // Private Variables
        private ComputeShader m_gpulightsCS_ClearLists;
        private ComputeShader m_gpuLightsCS_CoarseCulling;
        private ComputeShader m_gpuLightsCS_Cluster;
        private int m_ClearKernel;
        private int m_ScreenSpaceAABBKernel;
        private int m_CoarseCullingLightsKernel;
        private int m_ClusterCullingLightsKernel;

        private GPULightsDataBuildSystem m_GPULightsDataBuildSystem;
        ReflectionProbeManager m_ReflectionProbeManager;

        // Constants
        private const int
            k_Log2NumClusters =
                6; // accepted range is from 0 to 6 (NR_THREADS is set to 64). NumClusters is 1<<g_iLog2NumClusters

        private const float k_ClustLogBase = 1.02f; // each slice 2% bigger than the previous

        // Statics
        // Left-handed to right-handed
        static readonly Matrix4x4 s_FlipMatrixLHSRHS = Matrix4x4.Scale(new Vector3(1, 1, -1));


        public ClusterLighting()
        {
            var shaderResources = GraphicsSettings.GetRenderPipelineSettings<ClusterLightingRuntimeShader>();

            m_gpulightsCS_ClearLists = shaderResources.gpuLightsClearLists;
            m_gpuLightsCS_CoarseCulling = shaderResources.gpuLightsCoarseCullingCS;
            m_gpuLightsCS_Cluster = shaderResources.gpuLightsCluster;

            m_ClearKernel = m_gpulightsCS_ClearLists.FindKernel("ClearList");
            m_ScreenSpaceAABBKernel = m_gpuLightsCS_CoarseCulling.FindKernel("ScreenSpaceAABB");
            m_CoarseCullingLightsKernel = m_gpuLightsCS_CoarseCulling.FindKernel("CoarseCullingLights");
            m_ClusterCullingLightsKernel = m_gpuLightsCS_Cluster.FindKernel("ClusterCullingLights");

            // Big tile prepass
            bigTilePrepassShader = shaderResources.gpuLightsBigTile;
            bigTilePrepassKernel = bigTilePrepassShader.FindKernel("BigTileLightListGen");

            m_ReflectionProbeManager = ReflectionProbeManager.Create();
            lightCBuffer = new ShaderVariablesLightList();

            m_GPULightsDataBuildSystem = new GPULightsDataBuildSystem();

            s_BigTileVolumetricLightListKeyword = new LocalKeyword(bigTilePrepassShader, "GENERATE_VOLUMETRIC_BIGTILE");
            renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
        }


        private class SetupLightPassData
        {
            internal UniversalRenderingData renderingData;
            internal UniversalCameraData cameraData;
            internal UniversalLightData lightData;
            internal ClusterLighting gpuLights;
        };

        static ProfilingSampler s_SetupClusterDeferredLights = new ProfilingSampler("Setup ClusterDeferred Lights");


        internal void SetupLights(UnsafeCommandBuffer cmd, UniversalRenderingData renderingData,
            UniversalCameraData cameraData, UniversalLightData lightData)
        {
            m_ReflectionProbeManager.UpdateGpuData(CommandBufferHelpers.GetNativeCommandBuffer(cmd),
                ref renderingData.cullResults);
        }


        internal void SetupRenderGraphLights(RenderGraph renderGraph, UniversalRenderingData renderingData,
            UniversalCameraData cameraData,
            UniversalLightData lightData)
        {
            using (var builder = renderGraph.AddUnsafePass<SetupLightPassData>(s_SetupClusterDeferredLights.name,
                       out var passData,
                       s_SetupClusterDeferredLights))
            {
                passData.renderingData = renderingData;
                passData.cameraData = cameraData;
                passData.lightData = lightData;
                passData.gpuLights = this;

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((SetupLightPassData data, UnsafeGraphContext rgContext) =>
                {
                    data.gpuLights.SetupLights(rgContext.cmd, data.renderingData, data.cameraData, data.lightData);
                });
            }
        }

        public void NewFrame(int maxBoundsCount)
        {
            m_GPULightsDataBuildSystem.NewFrame(maxBoundsCount);
        }

        internal void PreSetup(UniversalLightData lightData, UniversalCameraData cameraData)
        {
            var desc = cameraData.cameraTargetDescriptor;
            int width = desc.width;
            int height = desc.height;

            var temp = new Matrix4x4();
            temp.SetRow(0, new Vector4(0.5f * width, 0.0f, 0.0f, 0.5f * width));
            temp.SetRow(1, new Vector4(0.0f, 0.5f * height, 0.0f, 0.5f * height));
            temp.SetRow(2, new Vector4(0.0f, 0.0f, 0.5f, 0.5f));
            temp.SetRow(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));

            var temp2 = new Matrix4x4();
            temp2.SetRow(0, new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
            temp2.SetRow(1, new Vector4(0.0f, 1.0f, 0.0f, 0.0f));
            temp2.SetRow(2, new Vector4(0.0f, 0.0f, 0.5f, 0.5f));
            temp2.SetRow(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));

            // camera to screen matrix (and it's inverse)
            {
                //Matrix4x4 gpuProjectionMatrix = cameraData.GetGPUProjectionMatrix(cameraData.IsCameraProjectionMatrixFlipped());
                Matrix4x4 projMatrix = cameraData.GetProjectionMatrix();

                projMatrix *= s_FlipMatrixLHSRHS;

                lightCBuffer.g_mScrProjectionArr = temp * projMatrix;
                lightCBuffer.g_mInvScrProjectionArr = lightCBuffer.g_mScrProjectionArr.inverse;

                lightCBuffer.g_mProjectionArr = temp2 * projMatrix;
                lightCBuffer.g_mInvProjectionArr = lightCBuffer.g_mProjectionArr.inverse;
            }

            var scaledCameraWidth = (float)cameraData.cameraTargetDescriptor.width;
            var scaledCameraHeight = (float)cameraData.cameraTargetDescriptor.height;

            if (cameraData.camera.allowDynamicResolution)
            {
                scaledCameraWidth *= ScalableBufferManager.widthScaleFactor;
                scaledCameraHeight *= ScalableBufferManager.heightScaleFactor;
            }

            int envLightsCount = m_GPULightsDataBuildSystem.envLightsCount;
            int additionalLightsCount = lightData.additionalLightsCount;
            lightCBuffer.g_iNrVisibLights = additionalLightsCount + envLightsCount;

            /// <see cref="ScriptableRenderer"/> cmd.SetGlobalVector(ShaderPropertyId.screenSize...
            lightCBuffer.g_screenSize = new Vector4(scaledCameraWidth, scaledCameraHeight, 1.0f / scaledCameraWidth,
                1.0f / scaledCameraHeight);
            lightCBuffer.g_viDimensions = new Vector2Int((int)scaledCameraWidth, (int)scaledCameraHeight);
            lightCBuffer.g_isOrthographic = cameraData.camera.orthographic ? 1u : 0u;
            //lightCBuffer.g_BaseFeatureFlags = 0; // Filled for each individual pass.
            //lightCBuffer.g_iNumSamplesMSAA = msaaSamples;
            lightCBuffer._EnvLightIndexShift = (uint)additionalLightsCount;
            lightCBuffer._DecalIndexShift = (uint)(additionalLightsCount + envLightsCount);

            const float C = (float)(1 << k_Log2NumClusters);
            var geomSeries =
                (1.0 - Mathf.Pow(k_ClustLogBase, C)) / (1 - k_ClustLogBase); // geometric series: sum_k=0^{C-1} base^k

            // Tile/Cluster
            lightCBuffer._NumTileFtplX = (uint)RenderingUtilsExt.DivRoundUp(width, LightDefinitions.s_TileSizeFptl);
            lightCBuffer._NumTileFtplY = (uint)RenderingUtilsExt.DivRoundUp(height, LightDefinitions.s_TileSizeFptl);
            lightCBuffer.g_fClustScale =
                (float)(geomSeries / (cameraData.camera.farClipPlane - cameraData.camera.nearClipPlane));

            lightCBuffer.g_fClustBase = k_ClustLogBase;
            lightCBuffer.g_fNearPlane = cameraData.camera.nearClipPlane;
            lightCBuffer.g_fFarPlane = cameraData.camera.farClipPlane;
            lightCBuffer.g_iLog2NumClusters = k_Log2NumClusters;
            lightCBuffer.g_isLogBaseBufferEnabled = 1; // Need depth
            lightCBuffer._NumTileClusteredX =
                (uint)RenderingUtilsExt.DivRoundUp(width, LightDefinitions.s_TileSizeClustered);
            lightCBuffer._NumTileClusteredY =
                (uint)RenderingUtilsExt.DivRoundUp(height, LightDefinitions.s_TileSizeClustered);
        }

        private class GPULightsPassData
        {
            internal UniversalLightData lightData;
            internal GPULightsDataBuildSystem gpuLightsDataBuildSystem;

            // Compute Shaders
            internal ComputeShader gpuLightsClearLists;
            internal ComputeShader gpuLightsCoarseCullingCS;
            internal ComputeShader gpuLightsCluster;
            internal int clearKernel;
            internal int screenSpaceAABBKernel;
            internal int coarseCullingLightsKernel;
            internal int clusterCullingLightsKernel;

            internal int nrBigTilesX;
            internal int nrBigTilesY;

            internal int nrClustersX;
            internal int nrClustersY;


            // LightsData
            internal ShaderVariablesLightList lightListCB;
            internal BufferHandle lightBoundsBuffer; //aka.HDRP convexBoundsBuffer;

            internal BufferHandle lightVolumeDataBuffer;

            // AABB, CoarseCull Buffer
            internal BufferHandle AABBBoundsBuffer;
            internal BufferHandle coarseLightList;

            // ClusterBuffer
            internal BufferHandle globalLightListAtomic;
            internal BufferHandle perVoxelOffset;
            internal BufferHandle perVoxelLightLists;
            internal BufferHandle perTileLogBaseTweak;
        }

        public class GPULightsOutPassData : ContextItem
        {
            internal ShaderVariablesLightList lightListCB;

            // LightsDataBuffer
            internal BufferHandle GPULightsData;
            //internal BufferHandle envLightsData;


            // Big Tile
            public BufferHandle bigTileLightList;
            public BufferHandle bigTileVolumetricLightList;


            // CoarseBuffer
            internal BufferHandle coarseLightList;

            // ClusterBuffer
            internal BufferHandle perVoxelOffset;
            internal BufferHandle perVoxelLightLists;
            internal BufferHandle perTileLogBaseTweak;

            public override void Reset()
            {
                // We should always reset texture handles since they are only vaild for the current frame.
                lightListCB = new ShaderVariablesLightList();
                GPULightsData = BufferHandle.nullHandle;
                perVoxelOffset = BufferHandle.nullHandle;
                perVoxelLightLists = BufferHandle.nullHandle;
                perTileLogBaseTweak = BufferHandle.nullHandle;
            }
        }

        private void InitResources(RenderGraph renderGraph, GPULightsPassData passData, UniversalLightData lightData,
            GPULightsOutPassData outData,
            UniversalCameraData cameraData)
        {
            // Copy the constant buffer into the parameter struct.
            passData.lightListCB = lightCBuffer;

            passData.lightData = lightData;
            passData.gpuLightsDataBuildSystem = m_GPULightsDataBuildSystem;

            // Compute Shaders
            passData.gpuLightsClearLists = m_gpulightsCS_ClearLists;
            passData.gpuLightsCoarseCullingCS = m_gpuLightsCS_CoarseCulling;
            passData.gpuLightsCluster = m_gpuLightsCS_Cluster;
            passData.clearKernel = m_ClearKernel;
            passData.screenSpaceAABBKernel = m_ScreenSpaceAABBKernel;
            passData.coarseCullingLightsKernel = m_CoarseCullingLightsKernel;
            passData.clusterCullingLightsKernel = m_ClusterCullingLightsKernel;

            var width = cameraData.cameraTargetDescriptor.width;
            var height = cameraData.cameraTargetDescriptor.height;
            passData.nrBigTilesX = RenderingUtilsExt.DivRoundUp(width, 64);
            passData.nrBigTilesY = RenderingUtilsExt.DivRoundUp(height, 64);


            // passData.bigTilePrepassShader = bigTilePrepassShader;
            // passData.bigTilePrepassKernel = bigTilePrepassKernel;


            var bufferSystem = GraphicsBufferSystem.instance;
            int allLightsBufferSize = m_MaxLightOnScreen;

            GraphicsBuffer lightBoundsBuf = bufferSystem.GetGraphicsBuffer<SFiniteLightBound>(
                GraphicsBufferSystemBufferID.GPULightsLightBoundsBuffer,
                m_MaxLightOnScreen, "lightBoundsBuffer");
            GraphicsBuffer lightVolumeDataBuf = bufferSystem.GetGraphicsBuffer<LightVolumeData>(
                GraphicsBufferSystemBufferID.GPULightsLightVolumeDataBuffer,
                m_MaxLightOnScreen, "lightVolumeDataBuffer");

            passData.lightBoundsBuffer = renderGraph.ImportBuffer(lightBoundsBuf);
            passData.lightVolumeDataBuffer = renderGraph.ImportBuffer(lightVolumeDataBuf);
            //passData.lightBoundsBuffer = renderGraph.CreateBuffer(new BufferDesc(allLightsBufferSize, Marshal.SizeOf(typeof(SFiniteLightBound)), "lightBoundsBuffer"));
            //passData.lightVolumeDataBuffer = renderGraph.CreateBuffer(new BufferDesc(allLightsBufferSize, Marshal.SizeOf(typeof(LightVolumeData)), "lightVolumeDataBuffer"));

            passData.AABBBoundsBuffer = renderGraph.CreateBuffer(new BufferDesc(allLightsBufferSize,
                Marshal.SizeOf(typeof(float4)), "AABBBoundsBuffer"));
            passData.coarseLightList =
                renderGraph.CreateBuffer(new BufferDesc(
                    LightDefinitions.s_MaxNrBigTileLightsPlusOne * passData.nrBigTilesX * passData.nrBigTilesY,
                    sizeof(uint), "coarseLightList"));


            // passData.bigTileLightList =
            //     renderGraph.CreateBuffer(new BufferDesc(LightDefinitions.s_MaxNrBigTileLightsPlusOne * passData.nrBigTilesX * passData.nrBigTilesY / 2,
            //         sizeof(uint), "BigTiles"));
            // passData.bigTileVolumetricLightList =
            //     renderGraph.CreateBuffer(new BufferDesc(LightDefinitions.s_MaxNrBigTileLightsPlusOne * passData.nrBigTilesX * passData.nrBigTilesY / 2,
            //         sizeof(uint), "BigTiles For Volumetric"));


            // Cluster buffers
            passData.nrClustersX = (width + LightDefinitions.s_TileSizeClustered - 1) /
                                   LightDefinitions.s_TileSizeClustered;
            passData.nrClustersY = (height + LightDefinitions.s_TileSizeClustered - 1) /
                                   LightDefinitions.s_TileSizeClustered;
            var nrClusterTiles = passData.nrClustersX * passData.nrClustersY;

            passData.globalLightListAtomic =
                renderGraph.CreateBuffer(new BufferDesc(1, sizeof(uint), "globalLightListAtomic"));
            passData.perVoxelLightLists =
                renderGraph.CreateBuffer(new BufferDesc(32 * (1 << k_Log2NumClusters) * nrClusterTiles, sizeof(uint),
                    "perVoxelLightLists"));
            ;
            passData.perVoxelOffset = renderGraph.CreateBuffer(new BufferDesc(
                (int)LightCategory.Count * (1 << k_Log2NumClusters) * nrClusterTiles,
                sizeof(uint), "perVoxelOffset"));
            passData.perTileLogBaseTweak =
                renderGraph.CreateBuffer(new BufferDesc(nrClusterTiles, sizeof(float), "perTileLogBaseTweak"));

            // Outdata
            outData.lightListCB = passData.lightListCB;
            outData.GPULightsData =
                renderGraph.CreateBuffer(new BufferDesc(m_MaxPunctualLightsOnScreen,
                    Marshal.SizeOf(typeof(GPULightData)), "GPULightsData"));

            outData.coarseLightList = passData.coarseLightList;

            outData.perVoxelOffset = passData.perVoxelOffset;
            outData.perVoxelLightLists = passData.perVoxelLightLists;
            outData.perTileLogBaseTweak = passData.perTileLogBaseTweak;
        }

        /// <summary>
        /// Clear one compute buffer.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="bufferToClear"></param>
        static void ClearLightList(ComputeCommandBuffer cmd, GPULightsPassData data, GraphicsBuffer bufferToClear)
        {
            Vector2 countAndOffset = new Vector2Int(bufferToClear.count, 0);
            int totalNumberOfGroupsNeeded = RenderingUtilsExt.DivRoundUp(bufferToClear.count, 64);
            const int
                maxAllowedGroups =
                    65535; // On higher resolutions we might end up with more than 65535 group which is not allowed, so we need to to have multiple dispatches.

            cmd.SetComputeBufferParam(data.gpuLightsClearLists, data.clearKernel, "_LightListToClear", bufferToClear);

            int i = 0;
            while (totalNumberOfGroupsNeeded > 0)
            {
                countAndOffset.y = maxAllowedGroups * i;
                cmd.SetComputeVectorParam(data.gpuLightsClearLists, "_LightListEntriesAndOffset", countAndOffset);

                int currGroupCount = Math.Min(maxAllowedGroups, totalNumberOfGroupsNeeded);

                cmd.DispatchCompute(data.gpuLightsClearLists, data.clearKernel, currGroupCount, 1, 1);

                totalNumberOfGroupsNeeded -= currGroupCount;
                i++;
            }
        }

        /// <summary>
        /// Clear all light lists compute buffer.
        /// </summary>
        /// <param name="cmd"></param>
        static void ClearAllLightLists(ComputeCommandBuffer cmd, GPULightsPassData data)
        {
            using (new ProfilingScope(cmd, m_ClearLightListsSampler))
            {
                if (data.coarseLightList.IsValid())
                {
                    ClearLightList(cmd, data, data.coarseLightList);
                    ClearLightList(cmd, data, data.perVoxelOffset);
                }
            }
        }

        static void GenerateLightsScreenSpaceAABBs(ComputeCommandBuffer cmd, GPULightsPassData data)
        {
            // GenerateLightsScreenSpaceAABBs
            using (new ProfilingScope(cmd, m_ScreenSpaceAABBSampler))
            {
                int totalLightCount = data.lightListCB.g_iNrVisibLights;
                cmd.SetComputeBufferParam(data.gpuLightsCoarseCullingCS, data.screenSpaceAABBKernel,
                    ShaderConstants.g_LightBounds,
                    data.lightBoundsBuffer); // in
                cmd.SetComputeBufferParam(data.gpuLightsCoarseCullingCS, data.screenSpaceAABBKernel,
                    ShaderConstants.g_vBoundsBuffer,
                    data.AABBBoundsBuffer); // out

                const int threadsPerLight = 4; // Shader: THREADS_PER_LIGHT (4)
                const int threadsPerGroup = 64; // Shader: THREADS_PER_GROUP (64)

                int groupCount = RenderingUtilsExt.DivRoundUp(totalLightCount * threadsPerLight, threadsPerGroup);
                cmd.DispatchCompute(data.gpuLightsCoarseCullingCS, data.screenSpaceAABBKernel, groupCount, 1, 1);
            }
        }


        static void CoarseCullingLights(ComputeCommandBuffer cmd, GPULightsPassData data)
        {
            using (new ProfilingScope(cmd, m_CoarseCullingSampler))
            {
                cmd.SetComputeBufferParam(data.gpuLightsCoarseCullingCS, data.coarseCullingLightsKernel,
                    ShaderConstants.g_LightVolumeData,
                    data.lightVolumeDataBuffer); // in
                cmd.SetComputeBufferParam(data.gpuLightsCoarseCullingCS, data.coarseCullingLightsKernel,
                    ShaderConstants.g_LightBounds,
                    data.lightBoundsBuffer); // in
                cmd.SetComputeBufferParam(data.gpuLightsCoarseCullingCS, data.coarseCullingLightsKernel,
                    ShaderConstants.g_vBoundsBuffer,
                    data.AABBBoundsBuffer); // in
                cmd.SetComputeBufferParam(data.gpuLightsCoarseCullingCS, data.coarseCullingLightsKernel,
                    ShaderConstants.g_vLightList,
                    data.coarseLightList); // out

                cmd.DispatchCompute(data.gpuLightsCoarseCullingCS, data.coarseCullingLightsKernel, data.nrBigTilesX,
                    data.nrBigTilesY, 1);
            }
        }


        static void ClusterCullingLights(ComputeCommandBuffer cmd, GPULightsPassData data)
        {
            using (new ProfilingScope(cmd, m_ClusterCullingSampler))
            {
                cmd.SetComputeBufferParam(data.gpuLightsCluster, data.clusterCullingLightsKernel,
                    ShaderConstants.g_LightVolumeData,
                    data.lightVolumeDataBuffer); // in
                cmd.SetComputeBufferParam(data.gpuLightsCluster, data.clusterCullingLightsKernel,
                    ShaderConstants.g_LightBounds, data.lightBoundsBuffer); // in

                cmd.SetComputeBufferParam(data.gpuLightsCluster, data.clusterCullingLightsKernel,
                    ShaderConstants.g_vBoundsBuffer, data.AABBBoundsBuffer); // in
                cmd.SetComputeBufferParam(data.gpuLightsCluster, data.clusterCullingLightsKernel,
                    ShaderConstants.g_CoarseLightList,
                    data.coarseLightList); // in

                cmd.SetComputeBufferParam(data.gpuLightsCluster, data.clusterCullingLightsKernel,
                    ShaderConstants.g_vLayeredLightList,
                    data.perVoxelLightLists); // out
                cmd.SetComputeBufferParam(data.gpuLightsCluster, data.clusterCullingLightsKernel,
                    ShaderConstants.g_LayeredOffset, data.perVoxelOffset); // out
                cmd.SetComputeBufferParam(data.gpuLightsCluster, data.clusterCullingLightsKernel,
                    ShaderConstants.g_LayeredSingleIdxBuffer,
                    data.globalLightListAtomic); // used
                cmd.SetComputeBufferParam(data.gpuLightsCluster, data.clusterCullingLightsKernel,
                    ShaderConstants.g_logBaseBuffer,
                    data.perTileLogBaseTweak); // out
                ConstantBuffer.Push(cmd, data.lightListCB, data.gpuLightsCluster,
                    ShaderConstants.ShaderVariablesLightList);

                cmd.DispatchCompute(data.gpuLightsCluster, data.clusterCullingLightsKernel, data.nrClustersX,
                    data.nrClustersY, 1);
            }
        }


        static void ExecutePass(GPULightsPassData data, ComputeGraphContext context)
        {
            // TODO: We should add envLights(probe) and decals as HDRP.
            int totalLightCount = data.lightListCB.g_iNrVisibLights;
            if (totalLightCount == 0)
            {
                ClearAllLightLists(context.cmd, data);
                return;
            }

            var cmd = context.cmd;
            // Set lightsData here.
            cmd.SetBufferData(data.lightBoundsBuffer, data.gpuLightsDataBuildSystem.lightBounds, 0, 0,
                data.gpuLightsDataBuildSystem.boundsCount);
            cmd.SetBufferData(data.lightVolumeDataBuffer, data.gpuLightsDataBuildSystem.lightVolumes, 0, 0,
                data.gpuLightsDataBuildSystem.boundsCount);
            // Push Constant buffer
            ConstantBuffer.Push(cmd, data.lightListCB, data.gpuLightsCoarseCullingCS,
                ShaderConstants.ShaderVariablesLightList);

            GenerateLightsScreenSpaceAABBs(context.cmd, data);

            // CoarseCullingLights
            CoarseCullingLights(context.cmd, data);


            // ClusterLights
            ClusterCullingLights(context.cmd, data);

            // Use RenderSetGlobalAsync to set global
            // Resolve
            {
                //ResolveGPULightsData(cmd, data);
            }
        }

        internal void Render(RenderGraph renderGraph, ContextContainer frameData)
        {
            // We need reBuild GPULightsData at main thread.
            m_GPULightsDataBuildSystem.ReBuildGPULightsDataBuffer(frameData.Get<UniversalLightData>());

            using (var builder =
                   renderGraph.AddComputePass<GPULightsPassData>("GPU Lights", out var passData, Profiling.GPULights))
            {
                // Access resources.
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();

                var reflectionProbes = renderingData.cullResults.visibleReflectionProbes;
                var reflectionProbeCount = Mathf.Min(reflectionProbes.Length,
                    UniversalRenderPipeline.maxVisibleReflectionProbes);

                m_GPULightsDataBuildSystem.BuildGPULightList(lightData, cameraData);
                m_GPULightsDataBuildSystem.BuildEnvLightList(ref reflectionProbes, reflectionProbeCount, cameraData);

                // Set passData
                GPULightsOutPassData outPassData = frameData.GetOrCreate<GPULightsOutPassData>();
                InitResources(renderGraph, passData, lightData, outPassData, cameraData);

                // Declare input/output
                builder.UseBuffer(passData.lightBoundsBuffer, AccessFlags.Write);
                builder.UseBuffer(passData.lightVolumeDataBuffer, AccessFlags.Write);

                builder.UseBuffer(passData.AABBBoundsBuffer, AccessFlags.Write);
                builder.UseBuffer(passData.coarseLightList, AccessFlags.Write);

                builder.UseBuffer(passData.globalLightListAtomic, AccessFlags.Write);
                builder.UseBuffer(passData.perVoxelOffset, AccessFlags.Write);
                builder.UseBuffer(passData.perVoxelLightLists, AccessFlags.Write);
                builder.UseBuffer(passData.perTileLogBaseTweak, AccessFlags.Write);
                builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);

                // builder.UseBuffer(passData.bigTileLightList, AccessFlags.Write);
                // builder.UseBuffer(passData.bigTileVolumetricLightList, AccessFlags.Write);

                // Setup builder state
#if ASYNC_COMPUTE
                builder.EnableAsyncCompute(true);
#endif
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((GPULightsPassData data, ComputeGraphContext context) =>
                {
                    ExecutePass(data, context);
                });
            }
        }

        private class GlobalSyncPassData
        {
            internal GPULightsDataBuildSystem gpuLightsDataBuildSystem;
            internal GPULightsOutPassData outData;
        }

        static void ResolveGPULightsData(ComputeCommandBuffer cmd, GlobalSyncPassData data)
        {
            var outData = data.outData;
            cmd.SetBufferData(outData.GPULightsData, data.gpuLightsDataBuildSystem.gpuLightsData, 0, 0,
                data.gpuLightsDataBuildSystem.lightsCount);

            // We inited lightCBuffer at preSetup.
            ConstantBuffer.PushGlobal(cmd, outData.lightListCB, ShaderConstants.ShaderVariablesLightList);
            // Lights data ref
            cmd.SetGlobalBuffer(ShaderConstants.g_GPULightDatas, outData.GPULightsData);
            // Coarse cull result
            cmd.SetGlobalBuffer(ShaderConstants.g_CoarseLightList, outData.coarseLightList);
            // Cluster cull result
            cmd.SetGlobalBuffer(ShaderConstants.g_vLightListCluster, outData.perVoxelLightLists);
            cmd.SetGlobalBuffer(ShaderConstants.g_vLayeredOffsetsBuffer, outData.perVoxelOffset);
            cmd.SetGlobalBuffer(ShaderConstants.g_logBaseBuffer, outData.perTileLogBaseTweak);

            cmd.SetKeyword(ShaderGlobalKeywords.GPULightsCluster, true);
        }

        internal void RenderSetGlobalSync(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddComputePass<GlobalSyncPassData>("GPU Lights Global Sync",
                       out var passData, Profiling.GPULightsGlobalAsync))
            {
                if (!frameData.Contains<GPULightsOutPassData>())
                {
                    return;
                }

                var outData = frameData.Get<GPULightsOutPassData>();
                passData.gpuLightsDataBuildSystem = m_GPULightsDataBuildSystem;
                passData.outData = outData;

                builder.UseBuffer(outData.GPULightsData, AccessFlags.Write);

                builder.UseBuffer(outData.coarseLightList);

                builder.UseBuffer(outData.perVoxelOffset);
                builder.UseBuffer(outData.perVoxelLightLists);
                builder.UseBuffer(outData.perTileLogBaseTweak);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((GlobalSyncPassData data, ComputeGraphContext context) =>
                {
                    ResolveGPULightsData(context.cmd, data);
                });
            }
        }

        /// <inheritdoc/>
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException("cmd");

            // Clean Keyword if need
            cmd.SetKeyword(ShaderGlobalKeywords.GPULightsCluster, false);
        }

        /// <summary>
        /// Clean up resources used by this pass.
        /// </summary>
        public void Dispose()
        {
            //m_RenderTarget?.Release();
        }

        static class ShaderConstants
        {
            public static readonly int g_LightBounds = Shader.PropertyToID("g_LightBounds");
            public static readonly int g_vBoundsBuffer = Shader.PropertyToID("g_vBoundsBuffer");
            public static readonly int g_LightVolumeData = Shader.PropertyToID("g_LightVolumeData");
            public static readonly int g_vLightList = Shader.PropertyToID("g_vLightList");
            public static readonly int ShaderVariablesLightList = Shader.PropertyToID("ShaderVariablesLightList");
            public static readonly int g_CoarseLightList = Shader.PropertyToID("g_CoarseLightList");
            public static readonly int g_vLayeredLightList = Shader.PropertyToID("g_vLayeredLightList");
            public static readonly int g_LayeredOffset = Shader.PropertyToID("g_LayeredOffset");
            public static readonly int g_LayeredSingleIdxBuffer = Shader.PropertyToID("g_LayeredSingleIdxBuffer");
            public static readonly int g_vLightListCluster = Shader.PropertyToID("g_vLightListCluster");
            public static readonly int g_vLayeredOffsetsBuffer = Shader.PropertyToID("g_vLayeredOffsetsBuffer");
            public static readonly int g_logBaseBuffer = Shader.PropertyToID("g_logBaseBuffer");
            public static readonly int g_GPULightDatas = Shader.PropertyToID("g_GPULightDatas");
            public static readonly int g_vVolumetricLightList = Shader.PropertyToID("g_vVolumetricLightList");
        }


        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var punctualLightCount = lightData.additionalLightsCount;
            var reflectionProbes = renderingData.cullResults.visibleReflectionProbes;
            var reflectionProbeCount =
                Mathf.Min(reflectionProbes.Length, UniversalRenderPipeline.maxVisibleReflectionProbes);
            NewFrame(punctualLightCount + reflectionProbeCount);
            PreSetup(lightData, cameraData);
            SetupRenderGraphLights(renderGraph, renderingData, cameraData, lightData);
            // GPULightList
            Render(renderGraph, frameData);
            RenderSetGlobalSync(renderGraph, frameData);
        }


        static class Profiling
        {
            public static ProfilingSampler GPULightsGlobalAsync = new ProfilingSampler(nameof(GPULightsGlobalAsync));
            public static ProfilingSampler GPULights = new ProfilingSampler(nameof(GPULights));
        }
    }
}
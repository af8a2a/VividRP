using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    partial class NSFluidEvaluator
    {
        private Material m_NSFluidMobile;

        class PassDataMobile
        {
            public Material FluidMobile;

            #region Advection

            public float AdvectSpeed;

            #endregion

            #region Diffusion

            public int DiffusionTimes;
            public float Viscosity;

            #endregion

            #region Force

            public BufferHandle InteractorData;
            public int InteractorCount;

            #endregion

            #region Divergence

            public TextureHandle DivergenceTexture;

            #endregion

            #region Pressure

            public int PressureTimes;
            public TextureHandle[] PressureTexture;

            #endregion

            public float SimulationResolution;
            public TextureHandle VelocityTexture;
            public TextureHandle VelocityTextureRW;
        }


        public TextureHandle EvaluateNavierStokesFluidMobile(RenderGraph renderGraph, NSFluidPlane plane)
        {
            if (!m_NSFluidMobile)
            {
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<FluidRuntimeShader>();
                m_NSFluidMobile = CoreUtils.CreateEngineMaterial(runtimeShader.fluidMobile);
            }

            TextureHandle result = TextureHandle.nullHandle;


            using (var builder = renderGraph.AddUnsafePass<PassDataMobile>("Navier Stokes Fluid Mobile", out var data))
            {
                var resolution = (int)plane.resolution;

                data.PressureTimes = plane.pressureTimes;
                data.FluidMobile = m_NSFluidMobile;
                data.InteractorData = renderGraph.ImportBuffer(plane.interactorData);
                data.AdvectSpeed = plane.advectSpeed;
                data.DiffusionTimes = plane.diffusionTimes;
                data.Viscosity = plane.Viscosity;
                data.InteractorCount = plane.interactorsCount;
                builder.UseBuffer(data.InteractorData);


                data.VelocityTexture = renderGraph.CreateTexture(new TextureDesc(resolution, resolution)
                {
                    format = GraphicsFormat.R16G16_SFloat,
                    name = "CurrentVelocityTexture",
                    enableRandomWrite = true
                });
                data.VelocityTextureRW = renderGraph.CreateTexture(new TextureDesc(resolution, resolution)
                {
                    format = GraphicsFormat.R16G16_SFloat,
                    name = "NextVelocityTexture",
                    enableRandomWrite = true
                });


                data.DivergenceTexture = renderGraph.CreateTexture(new TextureDesc(resolution, resolution)
                {
                    format = GraphicsFormat.R16_SFloat,
                    enableRandomWrite = true,
                    name = "DivergenceTexture",
                });
                data.PressureTexture = new TextureHandle[2];
                data.PressureTexture[0] = renderGraph.CreateTexture(new TextureDesc(resolution, resolution)
                {
                    format = GraphicsFormat.R16_SFloat,
                    enableRandomWrite = true,
                    name = "PressureTexture_0",
                });
                data.PressureTexture[1] = renderGraph.CreateTexture(new TextureDesc(resolution, resolution)
                {
                    format = GraphicsFormat.R16_SFloat,
                    enableRandomWrite = true,
                    name = "PressureTexture_1",
                });

                builder.UseTexture(data.DivergenceTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.VelocityTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.VelocityTextureRW, AccessFlags.ReadWrite);
                builder.UseTexture(data.PressureTexture[0], AccessFlags.ReadWrite);
                builder.UseTexture(data.PressureTexture[1], AccessFlags.ReadWrite);


                builder.AllowPassCulling(false);
                builder.SetRenderFunc<PassDataMobile>((passData, ctx) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);


                    var mpb = ctx.renderGraphPool.GetTempMaterialPropertyBlock();
                    var material = passData.FluidMobile;
                    using (new ProfilingScope(cmd, FluidAdvection))
                    {
                        mpb.SetTexture("_Tex0", passData.VelocityTexture);
                        mpb.SetTexture("_Tex1", passData.VelocityTexture);
                        mpb.SetFloat(_AdvectSpeed, passData.AdvectSpeed);


                        CoreUtils.DrawFullScreen(cmd, material, passData.VelocityTextureRW, mpb, 0);

                        (passData.VelocityTexture, passData.VelocityTextureRW) = (passData.VelocityTextureRW, passData.VelocityTexture);
                    }


                    //>>>>>>>>>>>>>>>>>>>>>>>>>>>diffusion
                    using (new ProfilingScope(cmd, FluidDiffusion))
                    {
                        mpb.SetFloat(_Viscosity, passData.Viscosity);
                        for (int i = 0; i < passData.DiffusionTimes; i++)
                        {
                            mpb.SetTexture("_Tex0", passData.VelocityTexture);
                            mpb.SetTexture("_Tex1", passData.VelocityTexture);
                            CoreUtils.DrawFullScreen(cmd, material, passData.VelocityTextureRW, mpb, 1);

                            (passData.VelocityTexture, passData.VelocityTextureRW) = (passData.VelocityTextureRW, passData.VelocityTexture);
                        }
                    }

                    //>>>>>>>>>>>>>>>>>>>>>>>>>>>force
                    using (new ProfilingScope(cmd, FluidForceApply))
                    {
                        mpb.SetBuffer(_InteractorData, passData.InteractorData);
                        mpb.SetInteger(_InteractorCount, passData.InteractorCount);
                        mpb.SetTexture("_Tex0", passData.VelocityTexture);

                        CoreUtils.DrawFullScreen(cmd, material, passData.VelocityTextureRW, mpb, 2);
                        (passData.VelocityTexture, passData.VelocityTextureRW) = (passData.VelocityTextureRW, passData.VelocityTexture);
                    }


                    //>>>>>>>>>>>>>>>>>>>>>>>>>>>divergence
                    using (new ProfilingScope(cmd, FluidDivergence))
                    {
                        mpb.SetTexture("_Tex0", passData.VelocityTexture);
                        CoreUtils.DrawFullScreen(cmd, material, passData.DivergenceTexture, mpb, 3);
                    }


                    //>>>>>>>>>>>>>>>>>>>>>>>>>>>presure
                    using (new ProfilingScope(cmd, FluidPressure))
                    {
                        for (int i = 0; i < passData.PressureTimes; i++)
                        {
                            mpb.SetTexture("_Tex0", passData.PressureTexture[0]);
                            mpb.SetTexture("_Tex1", passData.DivergenceTexture);
                            CoreUtils.DrawFullScreen(cmd, material, passData.PressureTexture[1], mpb, 4);

                            (passData.PressureTexture[0], passData.PressureTexture[1]) = (passData.PressureTexture[1], passData.PressureTexture[0]);
                        }
                    }


                    //>>>>>>>>>>>>>>>>>>>>>>>>>>>gradient
                    using (new ProfilingScope(cmd, FluidGradient))
                    {
                        mpb.SetTexture("_Tex0", passData.PressureTexture[0]);
                        mpb.SetTexture("_Tex1", passData.VelocityTexture);
                        CoreUtils.DrawFullScreen(cmd, material, passData.VelocityTextureRW, mpb, 5);
                        (passData.VelocityTexture, passData.VelocityTextureRW) = (passData.VelocityTextureRW, passData.VelocityTexture);
                    }
                });
                result = data.VelocityTexture;
            }

            return result;
        }
    }
}
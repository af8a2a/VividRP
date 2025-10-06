using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public partial class NSFluidEvaluator : Singleton<NSFluidEvaluator>
    {
        private ComputeShader m_Advection;
        private ComputeShader m_Diffusion;
        private ComputeShader m_Force;
        private ComputeShader m_Divergence;
        private ComputeShader m_PressureInit;
        private ComputeShader m_Pressure;
        private ComputeShader m_Gradient;

        #region ShaderID

        static int _VelocityTexture = Shader.PropertyToID("_VelocityTexture");
        static int _VelocityTextureRW = Shader.PropertyToID("_VelocityTextureRW");
        static int _SimulationResolution = Shader.PropertyToID("_SimulationResolution");
        static int _AdvectSpeed = Shader.PropertyToID("_AdvectSpeed");
        static int _Viscosity = Shader.PropertyToID("_Viscosity");
        static int _DiffusionTextureRW = Shader.PropertyToID("_DiffusionTextureRW");
        static int _InteractorCount = Shader.PropertyToID("_InteractorCount");
        static int _InteractorData = Shader.PropertyToID("_InteractorData");
        static int _DivergenceTexture = Shader.PropertyToID("_DivergenceTexture");
        static int _DivergenceTextureRW = Shader.PropertyToID("_DivergenceTextureRW");
        static int _PressureTexture = Shader.PropertyToID("_PressureTexture");
        static int _PressureTextureRW = Shader.PropertyToID("_PressureTextureRW");

        #endregion

        static ProfilingSampler FluidAdvection = new ProfilingSampler(nameof(FluidAdvection));
        static ProfilingSampler FluidDiffusion = new ProfilingSampler(nameof(FluidDiffusion));
        static ProfilingSampler FluidForceApply = new ProfilingSampler(nameof(FluidForceApply));
        static ProfilingSampler FluidDivergence = new ProfilingSampler(nameof(FluidDivergence));
        static ProfilingSampler FluidPressure = new ProfilingSampler(nameof(FluidPressure));
        static ProfilingSampler FluidGradient = new ProfilingSampler(nameof(FluidGradient));

        static void EvaluateFluidAdvection(ComputeCommandBuffer cmd, PassData passData)
        {
            var cs = passData.Advection;
            var kernel = 0;
            using (new ProfilingScope(cmd, FluidAdvection))
            {
                cmd.SetComputeTextureParam(cs, kernel, _VelocityTexture, passData.VelocityTexture);
                cmd.SetComputeTextureParam(cs, kernel, _VelocityTextureRW, passData.VelocityTextureRW);

                cmd.SetComputeFloatParam(cs, _SimulationResolution, passData.SimulationResolution);
                cmd.SetComputeFloatParam(cs, _AdvectSpeed, passData.AdvectSpeed);
                var tx = CoreUtils.DivRoundUp((int)passData.SimulationResolution, 8);
                var ty = CoreUtils.DivRoundUp((int)passData.SimulationResolution, 8);

                cmd.DispatchCompute(cs, kernel, tx, ty, 1);

                //swap
                (passData.VelocityTexture, passData.VelocityTextureRW) = (passData.VelocityTextureRW, passData.VelocityTexture);
            }
        }

        /// <summary>
        /// Diffusion via Jacobi on vector field 
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="passData"></param>
        static void EvaluateFluidDiffusion(ComputeCommandBuffer cmd, PassData passData)
        {
            var cs = passData.Diffusion;
            var kernel = 0;
            using (new ProfilingScope(cmd, FluidDiffusion))
            {
                cmd.SetComputeFloatParam(cs, _SimulationResolution, passData.SimulationResolution);
                cmd.SetComputeFloatParam(cs, _Viscosity, passData.Viscosity);
                var tx = CoreUtils.DivRoundUp((int)passData.SimulationResolution, 8);
                var ty = CoreUtils.DivRoundUp((int)passData.SimulationResolution, 8);

                for (int i = 0; i < passData.DiffusionTimes; i++)
                {
                    cmd.SetComputeTextureParam(cs, kernel, _VelocityTexture, passData.VelocityTexture);
                    cmd.SetComputeTextureParam(cs, kernel, _DiffusionTextureRW, passData.VelocityTextureRW);
                    cmd.DispatchCompute(cs, kernel, tx, ty, 1);
                    (passData.VelocityTexture, passData.VelocityTextureRW) = (passData.VelocityTextureRW, passData.VelocityTexture);
                }
            }
        }

        static void EvaluateForceApply(ComputeCommandBuffer cmd, PassData passData)
        {
            using (new ProfilingScope(cmd, FluidForceApply))
            {
                var cs = passData.Force;
                var kernel = 0;
                var tx = CoreUtils.DivRoundUp((int)passData.SimulationResolution, 8);
                var ty = CoreUtils.DivRoundUp((int)passData.SimulationResolution, 8);

                cmd.SetComputeBufferParam(cs, kernel, _InteractorData, passData.InteractorData);
                cmd.SetComputeFloatParam(cs, _SimulationResolution, passData.SimulationResolution);

                cmd.SetComputeIntParam(cs, _InteractorCount, passData.InteractorCount);
                cmd.SetComputeTextureParam(cs, kernel, _VelocityTexture, passData.VelocityTexture);
                cmd.SetComputeTextureParam(cs, kernel, _VelocityTextureRW, passData.VelocityTextureRW);


                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
                (passData.VelocityTexture, passData.VelocityTextureRW) = (passData.VelocityTextureRW, passData.VelocityTexture);
            }
        }


        static void EvaluateDivergence(ComputeCommandBuffer cmd, PassData passData)
        {
            using (new ProfilingScope(cmd, FluidDivergence))
            {
                var cs = passData.Divergence;
                var kernel = 0;
                var tx = CoreUtils.DivRoundUp((int)passData.SimulationResolution, 8);
                var ty = CoreUtils.DivRoundUp((int)passData.SimulationResolution, 8);

                cmd.SetComputeFloatParam(cs, _SimulationResolution, passData.SimulationResolution);
                cmd.SetComputeTextureParam(cs, kernel, _VelocityTexture, passData.VelocityTexture);
                cmd.SetComputeTextureParam(cs, kernel, _DivergenceTextureRW, passData.DivergenceTexture);

                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }
        }

        static void EvaluatePressure(ComputeCommandBuffer cmd, PassData passData)
        {
            using (new ProfilingScope(cmd, FluidPressure))
            {
                var cs = passData.PressureInit;


                //init
                var kernel = 0;
                var tx = CoreUtils.DivRoundUp((int)passData.SimulationResolution, 8);
                var ty = CoreUtils.DivRoundUp((int)passData.SimulationResolution, 8);
                cmd.SetComputeTextureParam(cs, kernel, _PressureTextureRW, passData.PressureTexture[0]);
                cmd.DispatchCompute(cs, kernel, tx, ty, 1);


                //eval pressure
                cs = passData.Pressure;
                cmd.SetComputeFloatParam(cs, _SimulationResolution, passData.SimulationResolution);
                cmd.SetComputeTextureParam(cs, kernel, _DivergenceTexture, passData.DivergenceTexture);
                for (int i = 0; i < passData.PressureTimes; i++)
                {
                    cmd.SetComputeTextureParam(cs, kernel, _PressureTexture, passData.PressureTexture[0]);
                    cmd.SetComputeTextureParam(cs, kernel, _PressureTextureRW, passData.PressureTexture[1]);

                    cmd.DispatchCompute(cs, kernel, tx, ty, 1);
                    (passData.PressureTexture[0], passData.PressureTexture[1]) = (passData.PressureTexture[1], passData.PressureTexture[0]);
                }
            }
        }


        static void EvaluateGradient(ComputeCommandBuffer cmd, PassData passData)
        {
            using (new ProfilingScope(cmd, FluidGradient))
            {
                var cs = passData.Gradient;
                var kernel = 0;
                var tx = CoreUtils.DivRoundUp((int)passData.SimulationResolution, 8);
                var ty = CoreUtils.DivRoundUp((int)passData.SimulationResolution, 8);

                cmd.SetComputeFloatParam(cs, _SimulationResolution, passData.SimulationResolution);


                cmd.SetComputeTextureParam(cs, kernel, _PressureTexture, passData.PressureTexture[0]);
                cmd.SetComputeTextureParam(cs, kernel, _VelocityTexture, passData.VelocityTexture);
                cmd.SetComputeTextureParam(cs, kernel, _VelocityTextureRW, passData.VelocityTextureRW);
                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
                (passData.VelocityTexture, passData.VelocityTextureRW) = (passData.VelocityTextureRW, passData.VelocityTexture);
            }
        }


        class PassData
        {
            #region Advection

            public ComputeShader Advection;
            public float AdvectSpeed;

            #endregion

            #region Diffusion

            public ComputeShader Diffusion;
            public int DiffusionTimes;
            public float Viscosity;

            #endregion

            #region Force

            public ComputeShader Force;
            public BufferHandle InteractorData;
            public int InteractorCount;

            #endregion

            #region Divergence

            public ComputeShader Divergence;
            public TextureHandle DivergenceTexture;

            #endregion

            #region Pressure

            public ComputeShader PressureInit;
            public ComputeShader Pressure;
            public int PressureTimes;
            public TextureHandle[] PressureTexture;

            #endregion

            #region Gradient

            public ComputeShader Gradient;

            #endregion

            public float SimulationResolution;
            public TextureHandle VelocityTexture;
            public TextureHandle VelocityTextureRW;
        }

        public TextureHandle EvaluateNavierStokesFluid(RenderGraph renderGraph, NSFluidPlane plane)
        {
            if (!m_Advection)
            {
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<FluidRuntimeShader>();
                m_Advection = runtimeShader.fluidAdvection;
                m_Diffusion = runtimeShader.fluidDiffusion;
                m_Force = runtimeShader.fluidForce;
                m_Divergence = runtimeShader.fluidDivergence;
                m_PressureInit = runtimeShader.fluidPressureInit;
                m_Pressure = runtimeShader.fluidPressure;
                m_Gradient = runtimeShader.fluidGradient;
            }

            TextureHandle result = TextureHandle.nullHandle;
            using (var builder = renderGraph.AddComputePass<PassData>("Navier Stokes Fluid", out var passData))
            {
                var resolution = (int)plane.resolution;
                passData.VelocityTexture = renderGraph.CreateTexture(new TextureDesc(resolution, resolution)
                {
                    format = GraphicsFormat.R16G16_SFloat,
                    name = "CurrentVelocityTexture",
                    enableRandomWrite = true
                });
                passData.VelocityTextureRW = renderGraph.CreateTexture(new TextureDesc(resolution, resolution)
                {
                    format = GraphicsFormat.R16G16_SFloat,
                    name = "NextVelocityTexture",
                    enableRandomWrite = true
                });


                passData.DivergenceTexture = renderGraph.CreateTexture(new TextureDesc(resolution, resolution)
                {
                    format = GraphicsFormat.R16_SFloat,
                    enableRandomWrite = true,
                    name = "DivergenceTexture",
                });
                passData.PressureTexture = new TextureHandle[2];
                passData.PressureTexture[0] = renderGraph.CreateTexture(new TextureDesc(resolution, resolution)
                {
                    format = GraphicsFormat.R16_SFloat,
                    enableRandomWrite = true,
                    name = "PressureTexture_0",
                });
                passData.PressureTexture[1] = renderGraph.CreateTexture(new TextureDesc(resolution, resolution)
                {
                    format = GraphicsFormat.R16_SFloat,
                    enableRandomWrite = true,
                    name = "PressureTexture_1",
                });

                builder.UseTexture(passData.DivergenceTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.VelocityTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.VelocityTextureRW, AccessFlags.ReadWrite);
                builder.UseTexture(passData.PressureTexture[0], AccessFlags.ReadWrite);
                builder.UseTexture(passData.PressureTexture[1], AccessFlags.ReadWrite);

                passData.SimulationResolution = resolution;
                passData.Advection = m_Advection;
                passData.Force = m_Force;
                passData.Divergence = m_Divergence;
                passData.PressureInit = m_PressureInit;
                passData.Pressure = m_Pressure;
                passData.Gradient = m_Gradient;

                passData.PressureTimes = plane.pressureTimes;

                passData.InteractorData = renderGraph.ImportBuffer(plane.interactorData);
                passData.AdvectSpeed = plane.advectSpeed;
                passData.Diffusion = m_Diffusion;
                passData.DiffusionTimes = plane.diffusionTimes;
                passData.Viscosity = plane.Viscosity;
                passData.InteractorCount = plane.interactorsCount;
                builder.UseBuffer(passData.InteractorData);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc<PassData>((data, ctx) =>
                {
                    var cmd = ctx.cmd;
                    EvaluateFluidAdvection(cmd, data);
                    EvaluateFluidDiffusion(cmd, data);
                    EvaluateForceApply(cmd, data);
                    EvaluateDivergence(cmd, data);
                    EvaluatePressure(cmd, data);
                    EvaluateGradient(cmd, data);
                });
                result = passData.DivergenceTexture;
            }

            return result;
        }
    }
}
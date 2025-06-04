using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Base class for sky rendering.
    /// </summary>
    public abstract class SkyRenderer
    {
        int m_LastFrameUpdate = -1;

        /// <summary>Determines if the sky should be rendered when the sun light changes.</summary>
        public bool SupportDynamicSunLight = true;

        /// <summary>
        /// Called on startup. Create resources used by the renderer (shaders, materials, etc).
        /// </summary>
        public abstract void Build();

        // TODO: Set parameters for user.
        /// <summary>
        /// calls this function once every frame. Implement it if your SkyRenderer needs to iterate independently of the user defined update frequency (see SkySettings UpdateMode).
        /// </summary>
        /// <returns>True if the update determines that sky lighting needs to be re-rendered. False otherwise.</returns>
        protected virtual bool Update(int frameIndex) { return false; }

        /// <summary>
        /// Whether the PreRenderSky step is required or not.
        /// </summary>
        /// <param name="skySettings">Sky setting for the current sky.</param>
        /// <returns>True if the sky needs a pre-render pass.</returns>
        public virtual bool RequiresPreRender(SkySettings skySettings) { return false; }

        // TODO: Set parameters for PreRenderSky.
        /// <summary>
        /// Preprocess for rendering the sky. Called before the DepthPrePass operations
        /// </summary>
        /// <param name="builtinParams">Engine parameters that you can use to render the sky.</param>
        public virtual void PreRenderSky() { }

        /// <summary>
        /// Implements actual rendering of the sky. calls this when rendering the sky into a cubemap (for lighting) and also during main frame rendering.
        /// </summary>
        /// <param name="skySettings"></param>
        /// <param name="renderForCubemap"></param>
        public abstract void RenderSky(CommandBuffer cmd, SkyBasePassData basePassData, SkySettings skySettings, bool renderForCubemap);

        /// <summary>
        /// Returns exposure setting for the provided SkySettings.
        /// </summary>
        /// <param name="skySettings">SkySettings for which exposure is required.</param>
        /// <returns>Returns SkySetting exposure.</returns>
        protected static float GetSkyIntensity(SkySettings skySettings)
        {
            return skySettings.GetIntensityFromSettings();
        }

        /// <summary>
        /// Setup global parameters for the sky renderer.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="skySettings"></param>
        public virtual void SetGlobalSkyData(CommandBuffer cmd, SkySettings skySettings)
        {
        }

        /// <summary>
        /// Return true if not able to update environment.(AmbientProbe and Cubemap)
        /// </summary>
        /// <param name="frameIndex"></param>
        /// <returns></returns>
        internal bool DoUpdate(int frameIndex)
        {
            if (m_LastFrameUpdate < frameIndex)
            {
                var result = Update(frameIndex);

                return result;
            }

            return false;
        }

        internal void Reset()
        {
            m_LastFrameUpdate = -1;
        }

        /// <summary>
        /// Called on cleanup. Release resources used by the renderer.
        /// </summary>
        public abstract void Cleanup();
    }
}
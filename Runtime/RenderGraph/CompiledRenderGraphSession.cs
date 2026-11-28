using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    /// <summary>
    /// Encapsulates the compiled runtime state for a single <see cref="RenderGraphData"/> asset
    /// (or the fallback graph when no asset is assigned).
    /// Allows <see cref="PassRecorder"/> to keep multiple compiled graphs resident concurrently
    /// and switch between them without destroying pass instances or recompiling every frame.
    /// </summary>
    internal sealed class CompiledRenderGraphSession
    {
        internal RenderGraphData GraphAsset;
        internal long ImportVersion;
        internal bool IsCompiled;
        internal bool HasCascadedShadowCasterPass;

        internal readonly List<IRenderPass> RenderPasses = new();
        internal readonly List<RenderGraphPassDefinition> RuntimePassDefinitions = new();
        internal readonly Dictionary<IRenderPass, PassResource> PassResources = new();
        internal readonly Dictionary<IRenderPass, int> PassIndices = new();
        internal readonly Dictionary<IRenderPass, Vector2Int> PassRenderSizes = new();
        internal readonly Dictionary<IRenderPass, Dictionary<string, AccessFlags>> PassResourceAccessOverrides = new();
        internal bool[] PassActiveStates = Array.Empty<bool>();

        internal bool Matches(RenderGraphData graphAsset)
        {
            if (!IsCompiled || GraphAsset != graphAsset)
                return false;

            return graphAsset == null || graphAsset.ImportVersion == ImportVersion;
        }

        internal bool[] EnsurePassActiveStatesCapacity(int passCount)
        {
            if (PassActiveStates.Length < passCount)
                PassActiveStates = new bool[passCount];

            return PassActiveStates;
        }

        internal void Dispose(Action<IRenderPass> disposePassAction, Action<IRenderPass> cleanupPassFrameStateAction)
        {
            for (var i = 0; i < RenderPasses.Count; i++)
            {
                var pass = RenderPasses[i];
                if (pass == null)
                    continue;

                try
                {
                    disposePassAction?.Invoke(pass);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                finally
                {
                    cleanupPassFrameStateAction?.Invoke(pass);
                }
            }

            DisposeAccelerationStructures();
            ClearState();
        }

        internal void ClearState()
        {
            RenderPasses.Clear();
            RuntimePassDefinitions.Clear();
            PassResources.Clear();
            PassIndices.Clear();
            PassRenderSizes.Clear();
            PassResourceAccessOverrides.Clear();
            PassActiveStates = Array.Empty<bool>();
            GraphAsset = null;
            ImportVersion = 0;
            IsCompiled = false;
            HasCascadedShadowCasterPass = false;
        }

        private void DisposeAccelerationStructures()
        {
            if (PassResources.Count == 0)
                return;

            var disposedAccelerationStructures = new HashSet<RenderGraphAccelerationStructure>();
            foreach (var resources in PassResources.Values)
            {
                if (resources?.AccelerationStructures == null)
                    continue;

                for (var i = 0; i < resources.AccelerationStructures.Length; i++)
                {
                    var accelerationStructure = resources.AccelerationStructures[i]?.AccelerationStructure;
                    if (accelerationStructure != null && disposedAccelerationStructures.Add(accelerationStructure))
                        accelerationStructure.Dispose();
                }
            }
        }
    }
}

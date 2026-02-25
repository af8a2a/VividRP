using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph.Passes;
using VividRP.Runtime.RenderGraph.Resource;
using VividRP.Runtime.Utility;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    [RenderPass("Preview", PassType.Unsafe)]
    public class PreviewPassNodeData : RenderPassNodeData
    {
        private struct PreviewMaterialEntry
        {
            public Shader Shader;
            public Material Material;
        }

        public Shader PreviewShader;
        public int PreviewWidth = 256;
        public int PreviewHeight = 144;

        public override PassType Type => PassType.Unsafe;

        private static readonly int s_BlitTextureId = Shader.PropertyToID("_BlitTexture");
        private static readonly int s_MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int s_BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly Dictionary<string, PreviewMaterialEntry> s_NodeMaterials = new();

        public PreviewPassNodeData()
        {
            NodeName = "Preview";
            AddPort("Input Texture", PortType.Texture, true, AccessFlags.Read);
            AddPort("Output Texture", PortType.Texture, false, AccessFlags.Read);
        }

        private class PassData
        {
            public string NodeGuid;
            public int Width;
            public int Height;
            public TextureHandle Source;
            public Material Material;
        }

        public static void ReleasePreviewResources(string nodeGuid)
        {
            if (string.IsNullOrEmpty(nodeGuid))
                return;

            if (s_NodeMaterials.TryGetValue(nodeGuid, out var entry))
            {
                DestroyMaterial(entry.Material);
                s_NodeMaterials.Remove(nodeGuid);
            }

            RenderGraphPreviewCache.Clear(nodeGuid);
        }

        private Material GetPreviewMaterial()
        {
            var shader = PreviewShader != null ? PreviewShader : VividResources.BlitShader;
            if (shader == null)
            {
                Debug.LogError("[VividRP] Preview node requires a shader. Assign a ShaderGraph shader or ensure Hidden/VividRP/Blit exists.");
                return null;
            }

            if (s_NodeMaterials.TryGetValue(Guid, out var entry))
            {
                if (entry.Material != null && entry.Shader == shader)
                    return entry.Material;

                DestroyMaterial(entry.Material);
            }

            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            s_NodeMaterials[Guid] = new PreviewMaterialEntry
            {
                Shader = shader,
                Material = material
            };
            return material;
        }

        public override void Record(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            PassExecutionContext context)
        {
            var sourceInput = context.ResolveInput(Ports[0]);
            if (!sourceInput.IsValid || sourceInput.Type != ResourceType.Texture)
            {
                ReleasePreviewResources(Guid);
                return;
            }

            using var builder = renderGraph.AddUnsafePass<PassData>(NodeName, out var passData);

            passData.NodeGuid = Guid;
            passData.Width = PreviewWidth;
            passData.Height = PreviewHeight;
            passData.Source = sourceInput.TextureHandle;
            passData.Material = GetPreviewMaterial();

            builder.UseTexture(sourceInput.TextureHandle, Ports[0].Access);
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc<PassData>((data, unsafeGraphContext) =>
            {
                if (data.Material == null)
                    return;

                var previewTexture = RenderGraphPreviewCache.GetOrCreate(
                    data.NodeGuid, data.Width, data.Height);
                if (previewTexture == null)
                    return;

                var cmd = unsafeGraphContext.cmd;
                cmd.SetRenderTarget(previewTexture);
                cmd.SetViewport(new Rect(0, 0, previewTexture.width, previewTexture.height));
                cmd.ClearRenderTarget(false, true, Color.clear);

                data.Material.SetTexture(s_BlitTextureId, data.Source);
                data.Material.SetTexture(s_MainTexId, data.Source);
                data.Material.SetTexture(s_BaseMapId, data.Source);
                Blitter.BlitTexture(cmd, data.Source, new Vector2(1f, 1f), data.Material, 0);
            });
        }

        private static void DestroyMaterial(Material material)
        {
            if (material == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(material);
            else
                UnityEngine.Object.DestroyImmediate(material);
        }
    }
}

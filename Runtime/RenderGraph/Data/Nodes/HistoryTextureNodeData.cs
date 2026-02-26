using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph.Resource;

namespace VividRP.Runtime.RenderGraph.Data
{
    public enum TextureSizeMode
    {
        Explicit,
        CameraRelative
    }

    [Serializable]
    [ResourceNode("History Texture")]
    public class HistoryTextureNodeData : ResourceNodeData, IHistoryResourceNode
    {
        public TextureSizeMode SizeMode = TextureSizeMode.Explicit;
        public int Width = 1920;
        public int Height = 1080;
        public float Scale = 1.0f;
        public GraphicsFormat Format = GraphicsFormat.R8G8B8A8_SRGB;
        public bool EnableRandomWrite;

        public string HistoryPortId
        {
            get
            {
                foreach (var port in Ports)
                {
                    if (!port.IsInput && port.DisplayName == "History")
                        return port.Id;
                }
                return null;
            }
        }

        public HistoryTextureNodeData()
        {
            NodeName = "History Texture";
            AddPort("Current", PortType.Texture, false, AccessFlags.ReadWrite);
            AddPort("History", PortType.Texture, false, AccessFlags.Read);
        }

        private void ResolveSize(Camera camera, out int width, out int height)
        {
            if (SizeMode == TextureSizeMode.CameraRelative && camera != null)
            {
                width = Mathf.Max(1, Mathf.RoundToInt(camera.pixelWidth * Scale));
                height = Mathf.Max(1, Mathf.RoundToInt(camera.pixelHeight * Scale));
            }
            else
            {
                width = Width;
                height = Height;
            }
        }

        public override ResourceSlot CreateResource(ResourceCreationContext context)
        {
            ResolveSize(context.Camera, out var w, out var h);

            var desc = new TextureDesc(w, h)
            {
                colorFormat = Format,
                clearBuffer = true,
                clearColor = Color.clear,
                enableRandomWrite = EnableRandomWrite,
                name = NodeName
            };

            var rtHandle = context.HistoryManager.GetOrAllocate(Guid, desc, EnableRandomWrite);
            return ResourceSlot.FromTexture(context.RenderGraph.ImportTexture(rtHandle));
        }

        public ResourceSlot CreateHistorySlot(ResourceCreationContext context)
        {
            var rtHandle = context.HistoryManager.GetHistoryHandle(Guid);
            if (rtHandle == null)
                return default;

            return ResourceSlot.FromTexture(context.RenderGraph.ImportTexture(rtHandle));
        }
    }
}

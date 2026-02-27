using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph.Passes;
using VividRP.Runtime.RenderGraph.Passes.DataDriven;
using VividRP.Runtime.RenderGraph.Resource;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    [RenderPass("Raster Pass", PassType.Raster)]
    public class RasterPassNodeData : RenderPassNodeData
    {
        private struct PortSpec
        {
            public string DisplayName;
            public PortType Type;
            public bool IsInput;
            public AccessFlags Access;
            public ResourceIntent Intent;
        }

        private class PassData
        {
            public DataDrivenRasterPassLogic Logic;
            public RendererListHandle RendererList;
            public bool HasRendererList;
        }

        private static readonly Dictionary<Type, DataDrivenRasterPassLogic> s_LogicInstances = new();

        public const int MaxColorAttachments = 8;

        [SerializeField] private string m_PassLogicTypeName = typeof(DefaultRasterPassLogic).AssemblyQualifiedName;
        [SerializeField] private bool m_UseCameraResolution = true;
        [SerializeField] private Vector2Int m_OutputResolution = new Vector2Int(1920, 1080);
        [SerializeField] private GraphicsFormat m_OutputColorFormat = GraphicsFormat.R8G8B8A8_SRGB;
        [SerializeField] private bool m_ClearColorBuffer = true;
        [SerializeField] private Color m_ClearColor = Color.clear;
        [SerializeField] private DepthBits m_OutputDepthBits = DepthBits.Depth32;
        [SerializeField] private bool m_ClearDepthBuffer = true;
        [SerializeField] private BakedRasterPass m_BakedPass = new();

        public override PassType Type => PassType.Raster;

        public RasterPassNodeData()
        {
            NodeName = "Raster Pass";
            EnsureBakedDescriptor();
        }

        public string PassLogicTypeName => m_PassLogicTypeName;
        public bool UseCameraResolution
        {
            get => m_UseCameraResolution;
            set => m_UseCameraResolution = value;
        }

        public Vector2Int OutputResolution
        {
            get => m_OutputResolution;
            set => m_OutputResolution = value;
        }

        public GraphicsFormat OutputColorFormat
        {
            get => m_OutputColorFormat;
            set => m_OutputColorFormat = value;
        }

        public bool ClearColorBuffer
        {
            get => m_ClearColorBuffer;
            set => m_ClearColorBuffer = value;
        }

        public Color ClearColor
        {
            get => m_ClearColor;
            set => m_ClearColor = value;
        }

        public DepthBits OutputDepthBits
        {
            get => m_OutputDepthBits;
            set => m_OutputDepthBits = value;
        }

        public bool ClearDepthBuffer
        {
            get => m_ClearDepthBuffer;
            set => m_ClearDepthBuffer = value;
        }

        public BakedRasterPass BakedPass => m_BakedPass;

        public bool HasDepthAttachment()
        {
            return m_BakedPass != null && m_BakedPass.DepthAttachment.IsDefined;
        }

        public bool HasRendererListInput()
        {
            return m_BakedPass != null &&
                   m_BakedPass.RendererLists != null &&
                   m_BakedPass.RendererLists.Length > 0;
        }

        public bool EnsureBakedDescriptor()
        {
            var logicType = ResolvePassLogicType();
            if (!RasterPassReflectionCompiler.TryCompile(logicType, out var layout, out var errors))
            {
                Debug.LogError($"[VividRP] Failed to compile raster pass '{NodeName}'. {string.Join(" ", errors)}");
                return false;
            }

            BuildPortsAndDescriptor(layout);
            return true;
        }

        public bool TryCompileLayout(out string[] errors)
        {
            var logicType = ResolvePassLogicType();
            return RasterPassReflectionCompiler.TryCompile(logicType, out _, out errors);
        }

        public override void Record(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            PassExecutionContext context)
        {
            if (!EnsureBakedDescriptor())
                return;

            using var builder = renderGraph.AddRasterRenderPass<PassData>(
                NodeName, out var passData);

            passData.Logic = GetLogicInstance(ResolvePassLogicType());

            foreach (var binding in m_BakedPass.ColorAttachments)
            {
                TextureHandle handle = default;

                if (binding.Intent == ResourceIntent.Write)
                {
                    handle = CreateTransientColorAttachment(renderGraph, context.Camera, binding.DisplayName);
                }
                else
                {
                    var slot = context.ResolveInput(binding.InputPortId);
                    if (slot.IsValid && slot.Type == ResourceType.Texture)
                        handle = slot.TextureHandle;
                }

                if (!handle.IsValid())
                    continue;

                builder.SetRenderAttachment(handle, binding.MrtIndex, binding.Access);

                if (!string.IsNullOrEmpty(binding.OutputPortId))
                    context.StoreOutput(binding.OutputPortId, ResourceSlot.FromTexture(handle));
            }

            if (m_BakedPass.DepthAttachment.IsDefined)
            {
                var depthBinding = m_BakedPass.DepthAttachment;
                TextureHandle depthHandle = default;

                if (depthBinding.Intent == ResourceIntent.Write)
                {
                    depthHandle = CreateTransientDepthAttachment(renderGraph, context.Camera, depthBinding.DisplayName);
                }
                else
                {
                    var slot = context.ResolveInput(depthBinding.InputPortId);
                    if (slot.IsValid && slot.Type == ResourceType.Texture)
                        depthHandle = slot.TextureHandle;
                }

                if (depthHandle.IsValid())
                {
                    builder.SetRenderAttachmentDepth(depthHandle);

                    if (!string.IsNullOrEmpty(depthBinding.OutputPortId))
                        context.StoreOutput(depthBinding.OutputPortId, ResourceSlot.FromTexture(depthHandle));
                }
            }

            foreach (var readBinding in m_BakedPass.ReadResources)
            {
                var slot = context.ResolveInput(readBinding.InputPortId);
                if (!slot.IsValid)
                    continue;

                if (readBinding.PortType == PortType.Texture && slot.Type == ResourceType.Texture)
                {
                    builder.UseTexture(slot.TextureHandle, readBinding.Access);
                }
                else if (readBinding.PortType == PortType.Buffer && slot.Type == ResourceType.Buffer)
                {
                    builder.UseBuffer(slot.BufferHandle, readBinding.Access);
                }
            }

            passData.HasRendererList = false;
            foreach (var rendererBinding in m_BakedPass.RendererLists)
            {
                var slot = context.ResolveInput(rendererBinding.InputPortId);
                if (!slot.IsValid || slot.Type != ResourceType.RendererList)
                    continue;

                passData.RendererList = slot.RendererListHandle;
                passData.HasRendererList = true;
                builder.UseRendererList(slot.RendererListHandle);
                break;
            }

            builder.SetRenderFunc<PassData>((data, rasterContext) =>
            {
                data.Logic?.Execute(rasterContext,
                    new DataDrivenRasterPassContext(data.RendererList, data.HasRendererList));
            });
        }

        private Type ResolvePassLogicType()
        {
            if (!string.IsNullOrEmpty(m_PassLogicTypeName))
            {
                var resolvedType =  Type.GetType(m_PassLogicTypeName);
                if (resolvedType != null &&
                    typeof(DataDrivenRasterPassLogic).IsAssignableFrom(resolvedType) &&
                    !resolvedType.IsAbstract)
                {
                    return resolvedType;
                }
            }

            m_PassLogicTypeName = typeof(DefaultRasterPassLogic).AssemblyQualifiedName;
            return typeof(DefaultRasterPassLogic);
        }

        private static DataDrivenRasterPassLogic GetLogicInstance(Type logicType)
        {
            if (!s_LogicInstances.TryGetValue(logicType, out var logic))
            {
                logic = (DataDrivenRasterPassLogic)Activator.CreateInstance(logicType);
                s_LogicInstances[logicType] = logic;
            }

            return logic;
        }

        private TextureHandle CreateTransientColorAttachment(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            Camera camera,
            string displayName)
        {
            GetOutputSize(camera, out int width, out int height);

            var desc = new TextureDesc(width, height)
            {
                colorFormat = m_OutputColorFormat,
                clearBuffer = m_ClearColorBuffer,
                clearColor = m_ClearColor,
                name = $"{NodeName} {displayName}"
            };

            return renderGraph.CreateTexture(desc);
        }

        private TextureHandle CreateTransientDepthAttachment(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            Camera camera,
            string displayName)
        {
            GetOutputSize(camera, out int width, out int height);

            var desc = new TextureDesc(width, height)
            {
                depthBufferBits = m_OutputDepthBits,
                clearBuffer = m_ClearDepthBuffer,
                clearColor = Color.black,
                name = $"{NodeName} {displayName}"
            };

            return renderGraph.CreateTexture(desc);
        }

        private void GetOutputSize(Camera camera, out int width, out int height)
        {
            if (m_UseCameraResolution && camera != null)
            {
                width = Mathf.Max(1, camera.pixelWidth);
                height = Mathf.Max(1, camera.pixelHeight);
                return;
            }

            width = Mathf.Max(1, m_OutputResolution.x);
            height = Mathf.Max(1, m_OutputResolution.y);
        }

        private void BuildPortsAndDescriptor(RasterPassLayout layout)
        {
            var specs = new List<PortSpec>();

            foreach (var colorAttachment in layout.ColorAttachments)
            {
                if (colorAttachment.Intent == ResourceIntent.ReadWrite)
                {
                    specs.Add(new PortSpec
                    {
                        DisplayName = $"{colorAttachment.DisplayName} In",
                        Type = PortType.Texture,
                        IsInput = true,
                        Access = AccessFlags.ReadWrite,
                        Intent = ResourceIntent.ReadWrite
                    });
                }

                specs.Add(new PortSpec
                {
                    DisplayName = $"{colorAttachment.DisplayName} Out",
                    Type = PortType.Texture,
                    IsInput = false,
                    Access = AccessFromIntent(colorAttachment.Intent),
                    Intent = colorAttachment.Intent
                });
            }

            if (layout.HasDepthAttachment)
            {
                var depth = layout.DepthAttachment;
                if (depth.Intent == ResourceIntent.ReadWrite)
                {
                    specs.Add(new PortSpec
                    {
                        DisplayName = "Depth In",
                        Type = PortType.Texture,
                        IsInput = true,
                        Access = AccessFlags.ReadWrite,
                        Intent = ResourceIntent.ReadWrite
                    });
                }

                specs.Add(new PortSpec
                {
                    DisplayName = "Depth Out",
                    Type = PortType.Texture,
                    IsInput = false,
                    Access = AccessFromIntent(depth.Intent),
                    Intent = depth.Intent
                });
            }

            foreach (var readResource in layout.ReadResources)
            {
                specs.Add(new PortSpec
                {
                    DisplayName = $"{readResource.DisplayName} In",
                    Type = readResource.PortType,
                    IsInput = true,
                    Access = AccessFlags.Read,
                    Intent = ResourceIntent.Read
                });
            }

            foreach (var rendererList in layout.RendererLists)
            {
                specs.Add(new PortSpec
                {
                    DisplayName = $"{rendererList.DisplayName} Cmd",
                    Type = PortType.RendererList,
                    IsInput = true,
                    Access = AccessFlags.Read,
                    Intent = ResourceIntent.Read
                });
            }

            SyncPorts(specs);

            var colorAttachments = new List<AttachmentBinding>(layout.ColorAttachments.Length);
            foreach (var colorAttachment in layout.ColorAttachments)
            {
                colorAttachments.Add(new AttachmentBinding
                {
                    FieldName = colorAttachment.FieldName,
                    DisplayName = colorAttachment.DisplayName,
                    InputPortId = colorAttachment.Intent == ResourceIntent.ReadWrite
                        ? FindPortId($"{colorAttachment.DisplayName} In", PortType.Texture, true)
                        : null,
                    OutputPortId = FindPortId($"{colorAttachment.DisplayName} Out", PortType.Texture, false),
                    Intent = colorAttachment.Intent,
                    MrtIndex = colorAttachment.MrtIndex,
                    Access = AccessFromIntent(colorAttachment.Intent)
                });
            }

            var depthAttachment = new DepthAttachmentBinding();
            if (layout.HasDepthAttachment)
            {
                depthAttachment = new DepthAttachmentBinding
                {
                    IsDefined = true,
                    FieldName = layout.DepthAttachment.FieldName,
                    DisplayName = layout.DepthAttachment.DisplayName,
                    InputPortId = layout.DepthAttachment.Intent == ResourceIntent.ReadWrite
                        ? FindPortId("Depth In", PortType.Texture, true)
                        : null,
                    OutputPortId = FindPortId("Depth Out", PortType.Texture, false),
                    Intent = layout.DepthAttachment.Intent,
                    Access = AccessFromIntent(layout.DepthAttachment.Intent)
                };
            }

            var readResources = new List<ReadResourceBinding>(layout.ReadResources.Length);
            foreach (var readResource in layout.ReadResources)
            {
                readResources.Add(new ReadResourceBinding
                {
                    FieldName = readResource.FieldName,
                    DisplayName = readResource.DisplayName,
                    PortType = readResource.PortType,
                    InputPortId = FindPortId($"{readResource.DisplayName} In", readResource.PortType, true),
                    Access = AccessFlags.Read
                });
            }

            var rendererLists = new List<RendererListBinding>(layout.RendererLists.Length);
            foreach (var rendererList in layout.RendererLists)
            {
                rendererLists.Add(new RendererListBinding
                {
                    FieldName = rendererList.FieldName,
                    DisplayName = rendererList.DisplayName,
                    InputPortId = FindPortId($"{rendererList.DisplayName} Cmd", PortType.RendererList, true)
                });
            }

            var inputResourceIndices = new List<int>();
            for (int i = 0; i < Ports.Count; i++)
            {
                if (Ports[i].IsInput)
                    inputResourceIndices.Add(i);
            }

            m_BakedPass = new BakedRasterPass
            {
                PassName = NodeName,
                PassLogicTypeName = layout.PassLogicType.AssemblyQualifiedName,
                ColorAttachments = colorAttachments.ToArray(),
                DepthAttachment = depthAttachment,
                ReadResources = readResources.ToArray(),
                RendererLists = rendererLists.ToArray(),
                InputResourceIndices = inputResourceIndices.ToArray()
            };
        }

        private void SyncPorts(List<PortSpec> specs)
        {
            var existingPorts = Ports ?? new List<RenderGraphPortData>();
            var syncedPorts = new List<RenderGraphPortData>(specs.Count);

            foreach (var spec in specs)
            {
                var port = existingPorts.FirstOrDefault(p =>
                    p.DisplayName == spec.DisplayName &&
                    p.Type == spec.Type &&
                    p.IsInput == spec.IsInput);

                if (port == null)
                {
                    port = new RenderGraphPortData
                    {
                        Id = Guid.NewGuid().ToString()
                    };
                }

                port.DisplayName = spec.DisplayName;
                port.Type = spec.Type;
                port.IsInput = spec.IsInput;
                port.Access = spec.Access;
                port.Intent = spec.Intent;

                syncedPorts.Add(port);
            }

            Ports = syncedPorts;
        }

        private string FindPortId(string displayName, PortType type, bool isInput)
        {
            foreach (var port in Ports)
            {
                if (port.DisplayName == displayName &&
                    port.Type == type &&
                    port.IsInput == isInput)
                {
                    return port.Id;
                }
            }

            return null;
        }

        private static AccessFlags AccessFromIntent(ResourceIntent intent)
        {
            return intent switch
            {
                ResourceIntent.Write => AccessFlags.Write,
                ResourceIntent.ReadWrite => AccessFlags.ReadWrite,
                _ => AccessFlags.Read
            };
        }
    }
}

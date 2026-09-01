using System;
using System.Collections;
using System.Reflection;
using Unity.Collections.LowLevel.Unsafe;
using Unity.GraphToolkit.Editor;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Scripting.LifecycleManagement;
using VividRP.Runtime.GPUDriven;
using Object = UnityEngine.Object;

namespace VividRP.Editor.GPUDriven
{
    [InitializeOnLoad]
    [NoAutoStaticsCleanup]
    internal static class MaterialGraphPreviewCostOverlayBootstrap
    {
        private static double s_NextScanTime;

        static MaterialGraphPreviewCostOverlayBootstrap()
        {
            EditorApplication.delayCall += EnsureOverlays;
            EditorApplication.update += EnsureOverlays;
        }

        private static void EnsureOverlays()
        {
            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime < s_NextScanTime)
                return;

            s_NextScanTime = currentTime + 0.5d;
            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (!MaterialGraphEditorWindowUtility.TryGetCurrentGraph(window, out _)
                    || window.overlayCanvas == null
                    || window.TryGetOverlay(MaterialGraphPreviewCostOverlay.OverlayId, out _))
                {
                    continue;
                }

                var overlay = new MaterialGraphPreviewCostOverlay();
                window.overlayCanvas.Add(overlay);
                overlay.displayed = true;
            }
        }
    }

    [Overlay(
        typeof(EditorWindow),
        OverlayId,
        "Material Preview & Cost",
        false,
        defaultDockZone = DockZone.RightColumn,
        defaultDockPosition = DockPosition.Top,
        defaultLayout = Layout.Panel,
        defaultWidth = 330f,
        defaultHeight = 640f,
        minWidth = 270f,
        minHeight = 300f,
        group = "VividRP")]
    internal sealed class MaterialGraphPreviewCostOverlay : Overlay
    {
        internal const string OverlayId = "vividrp-material-graph-preview-cost";

        public override VisualElement CreatePanelContent()
        {
            return new MaterialGraphPreviewCostPanel(
                () => MaterialGraphEditorWindowUtility.TryGetCurrentGraph(
                    containerWindow,
                    out MaterialGraphEditorGraph graph)
                        ? graph
                        : null);
        }
    }

    [CustomEditor(typeof(MaterialGraphImportAsset))]
    internal sealed class MaterialGraphImportAssetEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.style.flexGrow = 1f;

            var openButton = new Button(() => AssetDatabase.OpenAsset(target))
            {
                text = "Open Material Graph",
            };
            openButton.style.marginLeft = 4f;
            openButton.style.marginRight = 4f;
            openButton.style.marginTop = 4f;
            openButton.style.marginBottom = 4f;
            root.Add(openButton);

            root.Add(new MaterialGraphPreviewCostPanel(LoadGraph));
            return root;
        }

        private MaterialGraphEditorGraph LoadGraph()
        {
            string assetPath = AssetDatabase.GetAssetPath(target);
            return string.IsNullOrEmpty(assetPath)
                ? null
                : GraphDatabase.LoadGraph<MaterialGraphEditorGraph>(assetPath);
        }
    }

    internal sealed class MaterialGraphPreviewCostPanel : VisualElement
    {
        internal const string PreviewName = "vivid-material-graph-preview";
        internal const string StatusName = "vivid-material-graph-status";
        internal const string CostContainerName = "vivid-material-graph-costs";

        private readonly Func<MaterialGraphEditorGraph> m_GraphProvider;
        private readonly MaterialGraphPreviewRenderer m_PreviewRenderer = new();
        private readonly Image m_Preview;
        private readonly Label m_PreviewCaption;
        private readonly Label m_Status;
        private readonly VisualElement m_Metadata;
        private readonly VisualElement m_Costs;
        private readonly VisualElement m_Stages;
        private readonly VisualElement m_Diagnostics;
        private string m_Signature;

        internal MaterialGraphPreviewCostPanel(
            Func<MaterialGraphEditorGraph> graphProvider)
        {
            m_GraphProvider = graphProvider
                ?? throw new ArgumentNullException(nameof(graphProvider));
            style.flexGrow = 1f;
            style.paddingLeft = 8f;
            style.paddingRight = 8f;
            style.paddingTop = 6f;
            style.paddingBottom = 8f;

            var content = new ScrollView(ScrollViewMode.Vertical);
            content.style.flexGrow = 1f;
            Add(content);

            content.Add(CreateTitle("Generated Program Preview"));
            m_Preview = new Image
            {
                name = PreviewName,
                scaleMode = ScaleMode.ScaleToFit,
            };
            m_Preview.style.height = 190f;
            m_Preview.style.marginTop = 4f;
            m_Preview.style.marginBottom = 3f;
            m_Preview.style.backgroundColor = new Color(0.055f, 0.06f, 0.075f);
            content.Add(m_Preview);

            m_PreviewCaption = CreateSecondaryLabel();
            m_PreviewCaption.text =
                "AOT Surface dispatcher · neutral parameters · textures unbound";
            content.Add(m_PreviewCaption);

            m_Status = new Label { name = StatusName };
            m_Status.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_Status.style.marginTop = 8f;
            m_Status.style.marginBottom = 4f;
            m_Status.style.whiteSpace = WhiteSpace.Normal;
            content.Add(m_Status);

            m_Metadata = new VisualElement();
            content.Add(m_Metadata);

            content.Add(CreateSectionLabel("Compile Cost / Budget"));
            m_Costs = new VisualElement { name = CostContainerName };
            content.Add(m_Costs);

            content.Add(CreateSectionLabel("Stage Summary"));
            m_Stages = new VisualElement();
            content.Add(m_Stages);

            content.Add(CreateSectionLabel("Diagnostics"));
            m_Diagnostics = new VisualElement();
            content.Add(m_Diagnostics);

            RegisterCallback<DetachFromPanelEvent>(_ => m_PreviewRenderer.Dispose());
            schedule.Execute(Refresh).Every(350);
        }

        private void Refresh()
        {
            MaterialGraphEditorGraph graph = m_GraphProvider();
            if (graph == null)
            {
                ApplyUnavailable();
                return;
            }

            MaterialGraphPreviewCostViewModel viewModel;
            try
            {
                viewModel = MaterialGraphPreviewCostViewModel.Build(graph);
            }
            catch (Exception exception)
            {
                ApplyException(exception);
                return;
            }

            string signature = viewModel.Signature;
            if (string.Equals(signature, m_Signature, StringComparison.Ordinal))
                return;

            m_Signature = signature;
            ApplyStatus(viewModel);
            ApplyMetadata(viewModel);
            ApplyCosts(viewModel);
            ApplyStages(viewModel);
            ApplyDiagnostics(viewModel);

            m_Preview.image = viewModel.CanPreview
                ? m_PreviewRenderer.Render(viewModel.ProgramID)
                : null;
            m_PreviewCaption.text = viewModel.CanPreview
                ? "AOT Surface dispatcher · neutral parameters · textures unbound"
                : GetPreviewUnavailableMessage(viewModel.Status);
        }

        private void ApplyUnavailable()
        {
            const string signature = "unavailable";
            if (string.Equals(signature, m_Signature, StringComparison.Ordinal))
                return;

            m_Signature = signature;
            m_Preview.image = null;
            m_PreviewCaption.text = "Open a Vivid Material Graph to enable preview.";
            m_Status.text = "No Material Graph";
            m_Status.style.color = new Color(0.72f, 0.72f, 0.74f);
            m_Metadata.Clear();
            m_Costs.Clear();
            m_Stages.Clear();
            m_Diagnostics.Clear();
        }

        private void ApplyException(Exception exception)
        {
            string message = exception?.Message ?? "Unknown editor error.";
            string signature = $"exception:{message}";
            if (string.Equals(signature, m_Signature, StringComparison.Ordinal))
                return;

            m_Signature = signature;
            m_Preview.image = null;
            m_PreviewCaption.text = "Preview unavailable.";
            m_Status.text = "Editor preview failed";
            m_Status.style.color = new Color(0.95f, 0.38f, 0.32f);
            m_Metadata.Clear();
            m_Costs.Clear();
            m_Stages.Clear();
            m_Diagnostics.Clear();
            m_Diagnostics.Add(CreateDiagnostic(message, true));
        }

        private void ApplyStatus(MaterialGraphPreviewCostViewModel viewModel)
        {
            switch (viewModel.Status)
            {
                case MaterialGraphPreviewStatus.Ready:
                    m_Status.text = "Ready · Frozen Catalog dispatch";
                    m_Status.style.color = new Color(0.38f, 0.82f, 0.48f);
                    break;
                case MaterialGraphPreviewStatus.CatalogMiss:
                    m_Status.text = "Compiled · not in Frozen Catalog";
                    m_Status.style.color = new Color(0.95f, 0.68f, 0.25f);
                    break;
                case MaterialGraphPreviewStatus.OverBudget:
                    m_Status.text = "Rejected · cost budget exceeded";
                    m_Status.style.color = new Color(0.95f, 0.38f, 0.32f);
                    break;
                default:
                    m_Status.text = "Compile error";
                    m_Status.style.color = new Color(0.95f, 0.38f, 0.32f);
                    break;
            }
        }

        private void ApplyMetadata(MaterialGraphPreviewCostViewModel viewModel)
        {
            m_Metadata.Clear();
            string programID = viewModel.ProgramID == VividMaterialProgramID.Invalid
                ? "—"
                : ((uint) viewModel.ProgramID).ToString();
            m_Metadata.Add(CreateKeyValue("Program ID", programID));
            m_Metadata.Add(CreateKeyValue(
                "Topology",
                $"{viewModel.ClosureCount} slab(s), {viewModel.OperatorCount} operator(s)"));
            m_Metadata.Add(CreateKeyValue(
                "Compiled hash",
                ShortHash(viewModel.CompiledHash)));
            m_Metadata.Add(CreateKeyValue(
                "Semantic hash",
                ShortHash(viewModel.SemanticHash)));
        }

        private void ApplyCosts(MaterialGraphPreviewCostViewModel viewModel)
        {
            m_Costs.Clear();
            if (viewModel.Metrics.Count == 0)
            {
                m_Costs.Add(CreateSecondaryLabel("Cost is unavailable until lowering succeeds."));
                return;
            }

            for (int metricIndex = 0;
                 metricIndex < viewModel.Metrics.Count;
                 metricIndex++)
            {
                m_Costs.Add(CreateCostRow(viewModel.Metrics[metricIndex]));
            }
        }

        private void ApplyStages(MaterialGraphPreviewCostViewModel viewModel)
        {
            m_Stages.Clear();
            for (int stageIndex = 0; stageIndex < viewModel.Stages.Count; stageIndex++)
            {
                MaterialGraphStageCostSummary stage = viewModel.Stages[stageIndex];
                m_Stages.Add(CreateKeyValue(
                    stage.Name,
                    $"{stage.NodeCount} nodes · {stage.TextureSampleCount} samples · "
                    + $"{stage.DerivativeCount} derivatives · {stage.ArithmeticNodeCount} math"));
            }
        }

        private void ApplyDiagnostics(MaterialGraphPreviewCostViewModel viewModel)
        {
            m_Diagnostics.Clear();
            if (viewModel.Diagnostics.Count == 0)
            {
                m_Diagnostics.Add(CreateSecondaryLabel("No diagnostics."));
                return;
            }

            for (int diagnosticIndex = 0;
                 diagnosticIndex < viewModel.Diagnostics.Count;
                 diagnosticIndex++)
            {
                m_Diagnostics.Add(CreateDiagnostic(
                    viewModel.Diagnostics[diagnosticIndex],
                    viewModel.Status == MaterialGraphPreviewStatus.CompileError
                    || viewModel.Status == MaterialGraphPreviewStatus.OverBudget));
            }
        }

        private static VisualElement CreateCostRow(MaterialGraphCostMetric metric)
        {
            var row = new VisualElement();
            row.style.marginBottom = 5f;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            var name = new Label(metric.Name);
            name.style.flexGrow = 1f;
            var value = new Label($"{metric.Actual} / {metric.Maximum}");
            value.style.color = metric.IsExceeded
                ? new Color(0.95f, 0.38f, 0.32f)
                : new Color(0.72f, 0.72f, 0.74f);
            header.Add(name);
            header.Add(value);
            row.Add(header);

            var progress = new ProgressBar
            {
                lowValue = 0f,
                highValue = Mathf.Max(1, metric.Maximum),
                value = Mathf.Min(metric.Actual, Mathf.Max(1, metric.Maximum)),
                title = string.Empty,
            };
            progress.style.height = 5f;
            progress.style.marginTop = 2f;
            row.Add(progress);
            return row;
        }

        private static VisualElement CreateKeyValue(string key, string value)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 2f;
            var keyLabel = new Label(key);
            keyLabel.style.minWidth = 92f;
            keyLabel.style.color = new Color(0.72f, 0.72f, 0.74f);
            var valueLabel = new Label(value);
            valueLabel.style.flexGrow = 1f;
            valueLabel.style.whiteSpace = WhiteSpace.Normal;
            row.Add(keyLabel);
            row.Add(valueLabel);
            return row;
        }

        private static Label CreateTitle(string text)
        {
            var label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 13f;
            return label;
        }

        private static Label CreateSectionLabel(string text)
        {
            var label = CreateTitle(text);
            label.style.marginTop = 10f;
            label.style.marginBottom = 5f;
            return label;
        }

        private static Label CreateSecondaryLabel(string text = "")
        {
            var label = new Label(text);
            label.style.fontSize = 10f;
            label.style.color = new Color(0.67f, 0.67f, 0.7f);
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static Label CreateDiagnostic(string text, bool isError)
        {
            var label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 4f;
            label.style.color = isError
                ? new Color(0.95f, 0.48f, 0.42f)
                : new Color(0.92f, 0.72f, 0.3f);
            return label;
        }

        private static string ShortHash(string hash)
        {
            if (string.IsNullOrEmpty(hash))
                return "—";
            return hash.Length <= 18 ? hash : hash.Substring(0, 18) + "…";
        }

        private static string GetPreviewUnavailableMessage(
            MaterialGraphPreviewStatus status)
        {
            return status == MaterialGraphPreviewStatus.CatalogMiss
                ? "Bake the Frozen Catalog to generate a preview dispatcher entry."
                : "Resolve compile and budget diagnostics to enable preview.";
        }
    }

    internal sealed class MaterialGraphPreviewRenderer : IDisposable
    {
        private const string ShaderName =
            "Hidden/VividRP/Editor/Material Graph Preview";
        private const int PreviewSize = 512;

        private Material m_Material;
        private RenderTexture m_Texture;
        private GraphicsBuffer m_ParameterBuffer;
        private GraphicsBuffer m_ResourceBuffer;

        internal Texture Render(VividMaterialProgramID programID)
        {
            EnsureResources();
            if (m_Material == null || m_Texture == null)
                return null;

            BindProgramData(programID);
            m_Material.SetInt("_ProgramID", unchecked((int) (uint) programID));
            Graphics.Blit(Texture2D.whiteTexture, m_Texture, m_Material);
            return m_Texture;
        }

        public void Dispose()
        {
            if (m_Material != null)
                Object.DestroyImmediate(m_Material);
            if (m_Texture != null)
            {
                m_Texture.Release();
                Object.DestroyImmediate(m_Texture);
            }
            m_ParameterBuffer?.Dispose();
            m_ResourceBuffer?.Dispose();

            m_Material = null;
            m_Texture = null;
            m_ParameterBuffer = null;
            m_ResourceBuffer = null;
        }

        private void BindProgramData(VividMaterialProgramID programID)
        {
            MaterialProgramRuntimeBinding program =
                GPUDrivenMaterialCompiler.GetRuntimeProgramBinding(programID);
            var legacy = new VividMaterialData
            {
                AlbedoColor = new float4(0.42f, 0.55f, 0.72f, 1.0f),
                Emission = float4.zero,
                Roughness = 0.45f,
                Metallic = 0.15f,
                AlphaClipThreshold = 0.0f,
            };
            var dual = new VividDualSlabMaterialData
            {
                BaseAlbedoColor = legacy.AlbedoColor,
                BaseRoughness = legacy.Roughness,
                BaseMetallic = legacy.Metallic,
                TopAlbedoColor = new float4(0.88f, 0.52f, 0.24f, 1.0f),
                TopRoughness = Mathf.Clamp01(legacy.Roughness * 0.55f),
                TopMetallic = Mathf.Clamp01(legacy.Metallic * 0.35f),
                LayerWeight = 0.5f,
            };
            bool isDual = program.Topology
                != MaterialProgramTopologySpecialization.SingleSlab;
            uint4[] parameterLanes =
                GPUDrivenMaterialCompiler.CreatePreviewParameterLanes(
                    program,
                    legacy,
                    dual,
                    isDual);
            EnsureBuffer(
                ref m_ParameterBuffer,
                Mathf.Max(1, parameterLanes.Length),
                UnsafeUtility.SizeOf<uint4>(),
                "Vivid Material Preview Parameters");
            if (parameterLanes.Length > 0)
                m_ParameterBuffer.SetData(parameterLanes);

            int resourceCount = program.ResourceCount;
            var resources = new VividMaterialResourceData[Mathf.Max(1, resourceCount)];
            for (int resourceIndex = 0;
                 resourceIndex < resourceCount;
                 resourceIndex++)
            {
                resources[resourceIndex] = CreatePreviewResource();
            }
            EnsureBuffer(
                ref m_ResourceBuffer,
                resources.Length,
                UnsafeUtility.SizeOf<VividMaterialResourceData>(),
                "Vivid Material Preview Resources");
            m_ResourceBuffer.SetData(resources);

            m_Material.SetBuffer("_MaterialParameterData", m_ParameterBuffer);
            m_Material.SetBuffer("_MaterialResourceData", m_ResourceBuffer);
            m_Material.SetInt("_MaterialParameterDataCount", parameterLanes.Length);
            m_Material.SetInt("_MaterialResourceDataCount", resourceCount);
        }

        private static VividMaterialResourceData CreatePreviewResource()
        {
            return new VividMaterialResourceData
            {
                SurfaceBinding = new VividSurfaceBindingData
                {
                    BaseColorResource = VividSurfaceBindingData.InvalidResource,
                    NormalResource = VividSurfaceBindingData.InvalidResource,
                    MaskResource = VividSurfaceBindingData.InvalidResource,
                    UVScaleBias = new float4(1.0f, 1.0f, 0.0f, 0.0f),
                },
                TextureTilingOffset = new float4(1.0f, 1.0f, 0.0f, 0.0f),
                MetallicSmoothnessRemap = new float4(0.0f, 1.0f, 0.0f, 1.0f),
                AmbientOcclusionRemap = new float4(0.0f, 1.0f, 0.0f, 0.0f),
                NormalsStrength = 1.0f,
            };
        }

        private static void EnsureBuffer(
            ref GraphicsBuffer buffer,
            int count,
            int stride,
            string name)
        {
            if (buffer != null && buffer.count == count && buffer.stride == stride)
                return;

            buffer?.Dispose();
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, stride)
            {
                name = name,
            };
        }

        private void EnsureResources()
        {
            if (m_Material == null)
            {
                Shader shader = Shader.Find(ShaderName);
                if (shader != null)
                {
                    m_Material = new Material(shader)
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                }
            }

            if (m_Texture != null)
                return;

            m_Texture = new RenderTexture(
                PreviewSize,
                PreviewSize,
                0,
                RenderTextureFormat.ARGBHalf)
            {
                name = "Vivid Material Graph Preview",
                hideFlags = HideFlags.HideAndDontSave,
            };
            m_Texture.Create();
        }
    }

    internal static class MaterialGraphEditorWindowUtility
    {
        private const string GraphViewEditorWindowTypeName =
            "Unity.GraphToolkit.Editor.GraphViewEditorWindow";
        private const BindingFlags InstanceBindings =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static bool TryGetCurrentGraph(
            EditorWindow window,
            out MaterialGraphEditorGraph graph)
        {
            graph = null;
            if (window == null || !IsGraphViewEditorWindow(window.GetType()))
                return false;

            if (TryGetMember(window, "Graph", out graph) && graph != null)
                return true;
            if (!TryGetMember(window, "GraphView", out VisualElement graphView)
                || graphView == null
                || !TryGetMember(graphView, "GraphModel", out object graphModel)
                || graphModel == null)
            {
                return false;
            }

            if (TryGetMember(graphModel, "NodeModels", out IEnumerable nodeModels)
                && nodeModels != null)
            {
                foreach (object nodeModel in nodeModels)
                {
                    if (TryGetMember(nodeModel, "Node", out Node node)
                        && node?.Graph is MaterialGraphEditorGraph liveGraph)
                    {
                        graph = liveGraph;
                        return true;
                    }
                }
            }

            if (!TryGetMember(graphModel, "GraphObject", out object graphObject)
                || graphObject == null
                || !TryGetMember(graphObject, "FilePath", out string assetPath)
                || string.IsNullOrEmpty(assetPath)
                || !assetPath.EndsWith(
                    $".{MaterialGraphEditorGraph.AssetExtension}",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            graph = GraphDatabase.LoadGraph<MaterialGraphEditorGraph>(assetPath);
            return graph != null;
        }

        private static bool IsGraphViewEditorWindow(Type type)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                if (string.Equals(
                        current.FullName,
                        GraphViewEditorWindowTypeName,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryGetMember<T>(
            object source,
            string memberName,
            out T value)
        {
            value = default;
            if (source == null)
                return false;

            try
            {
                Type type = source.GetType();
                object raw = type.GetProperty(memberName, InstanceBindings)
                        ?.GetValue(source)
                    ?? type.GetField(memberName, InstanceBindings)
                        ?.GetValue(source);
                if (raw is T typed)
                {
                    value = typed;
                    return true;
                }
            }
            catch (TargetInvocationException)
            {
            }
            catch (Exception)
            {
            }

            return false;
        }
    }
}

using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VividRP.Runtime.RenderGraph;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph.Nodes
{
    [NodeEditor(typeof(PreviewPassNodeData))]
    public class PreviewPassNodeView : RenderGraphNodeView
    {
        private readonly PreviewPassNodeData m_Data;
        private readonly Image m_PreviewImage;
        private readonly Label m_Placeholder;

        public PreviewPassNodeView(PreviewPassNodeData data) : base(data)
        {
            m_Data = data;
            titleContainer.style.backgroundColor = new StyleColor(new Color(0.55f, 0.35f, 0.15f));

            var shaderField = new ObjectField("Preview Shader")
            {
                objectType = typeof(Shader),
                value = data.PreviewShader,
                allowSceneObjects = false
            };
            shaderField.RegisterValueChangedCallback(evt => data.PreviewShader = evt.newValue as Shader);

            var widthField = new IntegerField("Preview Width") { value = data.PreviewWidth };
            widthField.RegisterValueChangedCallback(evt =>
            {
                data.PreviewWidth = Mathf.Clamp(evt.newValue, 16, 2048);
                widthField.value = data.PreviewWidth;
            });

            var heightField = new IntegerField("Preview Height") { value = data.PreviewHeight };
            heightField.RegisterValueChangedCallback(evt =>
            {
                data.PreviewHeight = Mathf.Clamp(evt.newValue, 16, 2048);
                heightField.value = data.PreviewHeight;
            });

            m_PreviewImage = new Image
            {
                scaleMode = ScaleMode.ScaleToFit
            };
            m_PreviewImage.style.width = 220;
            m_PreviewImage.style.height = 124;
            m_PreviewImage.style.marginTop = 6;
            m_PreviewImage.style.marginBottom = 2;
            m_PreviewImage.style.unityBackgroundImageTintColor = new StyleColor(Color.white);

            m_Placeholder = new Label("Connect a texture and run the graph to preview.");
            m_Placeholder.style.whiteSpace = WhiteSpace.Normal;
            m_Placeholder.style.fontSize = 10;
            m_Placeholder.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f));

            extensionContainer.Add(shaderField);
            extensionContainer.Add(widthField);
            extensionContainer.Add(heightField);
            extensionContainer.Add(m_PreviewImage);
            extensionContainer.Add(m_Placeholder);

            extensionContainer.schedule.Execute(UpdatePreviewImage).Every(100);
            UpdatePreviewImage();
            RefreshExpandedState();
        }

        private void UpdatePreviewImage()
        {
            var previewTexture = RenderGraphPreviewCache.Get(m_Data.Guid);
            m_PreviewImage.image = previewTexture;
            m_Placeholder.style.display = previewTexture == null
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }
    }
}

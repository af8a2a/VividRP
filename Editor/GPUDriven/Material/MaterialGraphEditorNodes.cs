using System;
using Unity.GraphToolkit.Editor;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.GPUDriven
{
    [Serializable]
    internal readonly struct MaterialGraphValuePort
    {
    }

    [Serializable]
    internal readonly struct MaterialGraphClosurePort
    {
    }

    internal enum MaterialGraphConstantType
    {
        Bool = 0,
        Float = 1,
        Float2 = 2,
        Float3 = 3,
        Float4 = 4,
    }

    internal enum MaterialGraphUnaryOperator
    {
        Saturate = 0,
        OneMinus = 1,
        Normalize = 2,
        Ddx = 3,
        Ddy = 4,
    }

    internal enum MaterialGraphBinaryOperator
    {
        Add = 0,
        Multiply = 1,
        Subtract = 2,
        Divide = 3,
        Min = 4,
        Max = 5,
        Dot = 6,
    }

    internal enum MaterialGraphSwizzle
    {
        X = 0,
        Y = 1,
        Z = 2,
        W = 3,
        XYZ = 4,
    }

    [Serializable]
    [UseWithGraph(typeof(MaterialGraphEditorGraph))]
    internal abstract class MaterialGraphEditorNode : Node
    {
        internal const string ValueOutputPortName = "Out";
        internal const string ClosureOutputPortName = "Closure";

        protected static T GetOptionValue<T>(
            Node node,
            string optionName,
            T fallback)
        {
            INodeOption option = node.GetNodeOptionByName(optionName);
            return option != null && option.TryGetValue(out T value)
                ? value
                : fallback;
        }

        protected static void AddValueOutput(IPortDefinitionContext context)
        {
            context.AddOutputPort<MaterialGraphValuePort>(ValueOutputPortName)
                .WithDisplayName("Out")
                .Build();
        }

        protected static void AddClosureOutput(IPortDefinitionContext context)
        {
            context.AddOutputPort<MaterialGraphClosurePort>(ClosureOutputPortName)
                .WithDisplayName("Closure")
                .Build();
        }
    }

    [Serializable]
    [Node("Material/Input", "", "Parameter")]
    internal sealed class MaterialParameterNode : MaterialGraphEditorNode
    {
        internal const string ParameterOptionName = "Parameter";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<MaterialParameter>(ParameterOptionName)
                .WithDefaultValue(MaterialParameter.BaseColor);
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            AddValueOutput(context);
        }

        internal MaterialParameter GetParameter()
        {
            return GetOptionValue(
                this,
                ParameterOptionName,
                MaterialParameter.BaseColor);
        }
    }

    [Serializable]
    [Node("Material/Input", "", "Named Parameter")]
    internal sealed class MaterialNamedParameterNode : MaterialGraphEditorNode
    {
        internal const string SymbolOptionName = "Symbol";
        internal const string TypeOptionName = "Type";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<string>(SymbolOptionName)
                .WithDefaultValue("Parameter");
            context.AddOption<MaterialValueType>(TypeOptionName)
                .WithDefaultValue(MaterialValueType.Float);
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            AddValueOutput(context);
        }

        internal MaterialParameterDeclaration GetDeclaration()
        {
            return new MaterialParameterDeclaration(
                GetOptionValue(this, SymbolOptionName, "Parameter"),
                GetOptionValue(this, TypeOptionName, MaterialValueType.Float));
        }
    }

    [Serializable]
    [Node("Material/Input", "", "External Input")]
    internal sealed class MaterialExternalInputNode : MaterialGraphEditorNode
    {
        internal const string InputOptionName = "Input";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<MaterialExternalInput>(InputOptionName)
                .WithDefaultValue(MaterialExternalInput.UV0);
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            AddValueOutput(context);
        }

        internal MaterialExternalInput GetInput()
        {
            return GetOptionValue(
                this,
                InputOptionName,
                MaterialExternalInput.UV0);
        }
    }

    [Serializable]
    [Node("Material/Input", "", "Texture Resource")]
    internal sealed class MaterialTextureResourceNode : MaterialGraphEditorNode
    {
        internal const string ResourceOptionName = "Resource";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<MaterialTextureResource>(ResourceOptionName)
                .WithDefaultValue(MaterialTextureResource.BaseColor);
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            AddValueOutput(context);
        }

        internal MaterialTextureResource GetResource()
        {
            return GetOptionValue(
                this,
                ResourceOptionName,
                MaterialTextureResource.BaseColor);
        }
    }

    [Serializable]
    [Node("Material/Input", "", "Named Texture Resource")]
    internal sealed class MaterialNamedTextureResourceNode : MaterialGraphEditorNode
    {
        internal const string SymbolOptionName = "Symbol";
        internal const string TypeOptionName = "Type";
        internal const string SampleClassOptionName = "Sample Class";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<string>(SymbolOptionName)
                .WithDefaultValue("Texture");
            context.AddOption<MaterialValueType>(TypeOptionName)
                .WithDefaultValue(MaterialValueType.Texture2D);
            context.AddOption<MaterialTextureSampleClass>(SampleClassOptionName)
                .WithDefaultValue(MaterialTextureSampleClass.Raw);
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            AddValueOutput(context);
        }

        internal MaterialResourceDeclaration GetDeclaration()
        {
            return new MaterialResourceDeclaration(
                GetOptionValue(this, SymbolOptionName, "Texture"),
                GetOptionValue(this, TypeOptionName, MaterialValueType.Texture2D),
                GetOptionValue(
                    this,
                    SampleClassOptionName,
                    MaterialTextureSampleClass.Raw));
        }
    }

    [Serializable]
    [Node("Material/Input", "", "Constant")]
    internal sealed class MaterialConstantNode : MaterialGraphEditorNode
    {
        internal const string TypeOptionName = "Type";
        internal const string ValueOptionName = "Value";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<MaterialGraphConstantType>(TypeOptionName)
                .WithDefaultValue(MaterialGraphConstantType.Float);
            context.AddOption<Vector4>(ValueOptionName)
                .WithDefaultValue(Vector4.zero);
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            AddValueOutput(context);
        }

        internal MaterialGraphConstantType GetConstantType()
        {
            return GetOptionValue(
                this,
                TypeOptionName,
                MaterialGraphConstantType.Float);
        }

        internal Vector4 GetValue()
        {
            return GetOptionValue(this, ValueOptionName, Vector4.zero);
        }
    }

    [Serializable]
    [Node("Material/Texture", "", "Sample Texture 2D")]
    internal sealed class MaterialTextureSampleNode : MaterialGraphEditorNode
    {
        internal const string TexturePortName = "Texture";
        internal const string UVPortName = "UV";

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<MaterialGraphValuePort>(TexturePortName).Build();
            context.AddInputPort<MaterialGraphValuePort>(UVPortName).Build();
            AddValueOutput(context);
        }
    }

    [Serializable]
    [Node("Material/Math", "", "Unary Math")]
    internal sealed class MaterialUnaryNode : MaterialGraphEditorNode
    {
        internal const string OperatorOptionName = "Operator";
        internal const string InputPortName = "In";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<MaterialGraphUnaryOperator>(OperatorOptionName)
                .WithDefaultValue(MaterialGraphUnaryOperator.Saturate);
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<MaterialGraphValuePort>(InputPortName).Build();
            AddValueOutput(context);
        }

        internal MaterialGraphUnaryOperator GetOperator()
        {
            return GetOptionValue(
                this,
                OperatorOptionName,
                MaterialGraphUnaryOperator.Saturate);
        }
    }

    [Serializable]
    [Node("Material/Math", "", "Binary Math")]
    internal sealed class MaterialBinaryNode : MaterialGraphEditorNode
    {
        internal const string OperatorOptionName = "Operator";
        internal const string LeftPortName = "A";
        internal const string RightPortName = "B";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<MaterialGraphBinaryOperator>(OperatorOptionName)
                .WithDefaultValue(MaterialGraphBinaryOperator.Multiply);
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<MaterialGraphValuePort>(LeftPortName).Build();
            context.AddInputPort<MaterialGraphValuePort>(RightPortName).Build();
            AddValueOutput(context);
        }

        internal MaterialGraphBinaryOperator GetOperator()
        {
            return GetOptionValue(
                this,
                OperatorOptionName,
                MaterialGraphBinaryOperator.Multiply);
        }
    }

    [Serializable]
    [Node("Material/Channel", "", "Swizzle")]
    internal sealed class MaterialSwizzleNode : MaterialGraphEditorNode
    {
        internal const string SwizzleOptionName = "Swizzle";
        internal const string InputPortName = "In";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<MaterialGraphSwizzle>(SwizzleOptionName)
                .WithDefaultValue(MaterialGraphSwizzle.X);
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<MaterialGraphValuePort>(InputPortName).Build();
            AddValueOutput(context);
        }

        internal MaterialGraphSwizzle GetSwizzle()
        {
            return GetOptionValue(this, SwizzleOptionName, MaterialGraphSwizzle.X);
        }
    }

    [Serializable]
    [Node("Material/Closure", "", "Standard Slab")]
    internal sealed class MaterialStandardSlabNode : MaterialGraphEditorNode
    {
        internal const string FeatureMaskOptionName = "Features";
        internal const string BaseColorPortName = "BaseColor";
        internal const string RoughnessPortName = "Roughness";
        internal const string MetallicPortName = "Metallic";
        internal const string NormalPortName = "Normal";
        internal const string TangentPortName = "Tangent";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<ClosureFeatureMask>(FeatureMaskOptionName)
                .WithDefaultValue(MaterialGraphDefaults.StandardSlabFeatures);
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<MaterialGraphValuePort>(BaseColorPortName).Build();
            context.AddInputPort<MaterialGraphValuePort>(RoughnessPortName).Build();
            context.AddInputPort<MaterialGraphValuePort>(MetallicPortName).Build();
            context.AddInputPort<MaterialGraphValuePort>(NormalPortName).Build();
            context.AddInputPort<MaterialGraphValuePort>(TangentPortName).Build();
            AddClosureOutput(context);
        }

        internal ClosureFeatureMask GetFeatureMask()
        {
            return GetOptionValue(
                this,
                FeatureMaskOptionName,
                MaterialGraphDefaults.StandardSlabFeatures);
        }
    }

    [Serializable]
    [Node("Material/Closure", "", "Horizontal Mix")]
    internal sealed class MaterialHorizontalMixNode : MaterialGraphEditorNode
    {
        internal const string BackgroundPortName = "Background";
        internal const string ForegroundPortName = "Foreground";
        internal const string WeightPortName = "Weight";

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<MaterialGraphClosurePort>(BackgroundPortName).Build();
            context.AddInputPort<MaterialGraphClosurePort>(ForegroundPortName).Build();
            context.AddInputPort<MaterialGraphValuePort>(WeightPortName).Build();
            AddClosureOutput(context);
        }
    }

    [Serializable]
    [Node("Material/Closure", "", "Vertical Layer")]
    internal sealed class MaterialVerticalLayerNode : MaterialGraphEditorNode
    {
        internal const string BottomPortName = "Bottom";
        internal const string TopPortName = "Top";
        internal const string WeightPortName = "Weight";

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<MaterialGraphClosurePort>(BottomPortName).Build();
            context.AddInputPort<MaterialGraphClosurePort>(TopPortName).Build();
            context.AddInputPort<MaterialGraphValuePort>(WeightPortName).Build();
            AddClosureOutput(context);
        }
    }

    [Serializable]
    [Node("Material", "", "Material Output")]
    internal sealed class MaterialOutputNode : MaterialGraphEditorNode
    {
        internal const string MaterialFeaturesOptionName = "Material Features";
        internal const string ShadingModelsOptionName = "Shading Models";
        internal const string SurfacePortName = "Surface";
        internal const string CoveragePortName = "Coverage";
        internal const string AlphaClipThresholdPortName = "AlphaClipThreshold";
        internal const string EmissionPortName = "Emission";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<MaterialFeatureMask>(MaterialFeaturesOptionName)
                .WithDefaultValue(MaterialGraphDefaults.StandardMaterialFeatures);
            context.AddOption<MaterialShadingModelMask>(ShadingModelsOptionName)
                .WithDefaultValue(MaterialGraphDefaults.StandardShadingModels);
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<MaterialGraphClosurePort>(SurfacePortName).Build();
            context.AddInputPort<MaterialGraphValuePort>(CoveragePortName).Build();
            context.AddInputPort<MaterialGraphValuePort>(AlphaClipThresholdPortName).Build();
            context.AddInputPort<MaterialGraphValuePort>(EmissionPortName).Build();
        }

        internal MaterialFeatureMask GetMaterialFeatures()
        {
            return GetOptionValue(
                this,
                MaterialFeaturesOptionName,
                MaterialGraphDefaults.StandardMaterialFeatures);
        }

        internal MaterialShadingModelMask GetShadingModels()
        {
            return GetOptionValue(
                this,
                ShadingModelsOptionName,
                MaterialGraphDefaults.StandardShadingModels);
        }
    }
}

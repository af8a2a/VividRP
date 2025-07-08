using LWGUI;
using UnityEngine;

namespace UnityEditor.Rendering.Universal
{
    internal class RemapDrawer : SubDrawer
    {
        private string _extraPropName;
        private string _useLimit;
        private SubDrawer subDrawer = new SubDrawer();

        public RemapDrawer(string group) : this(group, string.Empty)
        {
        }

        public override void BuildStaticMetaData(Shader inShader, MaterialProperty inProp, MaterialProperty[] inProps,
            PropertyStaticData inoutPropertyStaticData)
        {
            base.BuildStaticMetaData(inShader, inProp, inProps, inoutPropertyStaticData);
            inoutPropertyStaticData.AddExtraProperty(_extraPropName);
        }

        public override void GetDefaultValueDescription(Shader inShader,
            MaterialProperty inProp,
            MaterialProperty inDefaultProp,
            PerShaderData inPerShaderData,
            PerMaterialData inoutPerMaterialData)
        {
            if (string.IsNullOrEmpty(_extraPropName)
                || !inoutPerMaterialData.propDynamicDatas.ContainsKey(_extraPropName)
               )
            {
                Debug.LogError(inProp.name + " has no available min/max properties!");
                return;
            }

            inoutPerMaterialData.propDynamicDatas[inProp.name].defaultValueDescription =
                inoutPerMaterialData.GetPropDynamicData(_extraPropName)?.defualtProperty.floatValue.ToString();
        }

        public RemapDrawer(string group, string extraPropName) : this(group, extraPropName, "true")
        {
        }

        public RemapDrawer(string group, string extraPropName, string useLimit)
        {
            this.group = group;
            this._extraPropName = extraPropName;
            this._useLimit = useLimit;
        }

        protected override bool IsMatchPropType(MaterialProperty property)
        {
            return property.type == MaterialProperty.PropType.Vector;
        }

        public void ApplyProperty(MaterialProperty prop)
        {
        }

        public override void DrawProp(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            float minf = prop.vectorValue.x;
            float maxf = prop.vectorValue.y;
            // define draw area
            Rect controlRect = position; // this is the full length rect area
            var labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 0;
            Rect inputRect = MaterialEditor.GetRectAfterLabelWidth(controlRect); // this is the remaining rect area after label's area

            // draw label
            EditorGUI.PrefixLabel(controlRect, label);

            // draw min max slider
            var indentLevel = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            Rect[] splittedRect = Helper.SplitRect(inputRect, 3);

            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = prop.hasMixedValue;
            var newMinf = EditorGUI.FloatField(splittedRect[0], minf);
            if (Helper.EndChangeCheck(metaDatas, prop))
            {
                ApplyProperty(prop, ref newMinf, ref maxf);
            }

            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = prop.hasMixedValue;
            var newMaxf = EditorGUI.FloatField(splittedRect[2], maxf);
            if (Helper.EndChangeCheck(metaDatas, prop))
            {
                ApplyProperty(prop, ref minf, ref newMaxf);
            }

            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = prop.hasMixedValue;
            if (splittedRect[1].width > 50f)
                EditorGUI.MinMaxSlider(splittedRect[1], ref minf, ref maxf, 0, 1);
            EditorGUI.showMixedValue = false;

            // write back min max if changed
            if (EditorGUI.EndChangeCheck())
            {
                ApplyProperty(prop, ref minf, ref maxf);
            }

            EditorGUI.indentLevel = indentLevel;
            EditorGUIUtility.labelWidth = labelWidth;
        }

        private void ApplyProperty(MaterialProperty materialProperty, ref float minf, ref float maxf)
        {
            minf = Mathf.Clamp(minf, 0, 1);
            maxf = Mathf.Clamp(maxf, 0, 1);
            ShaderGUIHelper.ConvertLinearStep2(minf, maxf, out float z, out float w);
            if (!string.IsNullOrEmpty(_extraPropName))
            {
                var extraProp = metaDatas.perMaterialData.GetPropDynamicData(_extraPropName).property;
                if (extraProp != null && (extraProp.type == MaterialProperty.PropType.Float ||
                                          extraProp.type == MaterialProperty.PropType.Range))
                {
                    materialProperty.vectorValue = new Vector4(minf, maxf, z * extraProp.floatValue, w * (_useLimit == "true" ? 1 : extraProp.floatValue));
                    return;
                }
            }

            materialProperty.vectorValue = new Vector4(minf, maxf, z, w);
        }
    }
}
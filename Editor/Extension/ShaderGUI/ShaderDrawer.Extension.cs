using System;
using UnityEditor;
using UnityEngine;

namespace LWGUI
{
    public class TexKWDrawer : TexDrawer
    {
        private string _keyWord = String.Empty;

        public TexKWDrawer(string group) : base(group)
        {
            _keyWord = String.Empty;
        }

        public TexKWDrawer(string group, string keyWord) : base(group)
        {
            this._keyWord = keyWord;
        }

        public TexKWDrawer(string group, string keyWord, string prop) : base(group, prop)
        {
            this._keyWord = keyWord;
        }

        public override void DrawProp(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            EditorGUI.BeginChangeCheck();
            base.DrawProp(position, prop, label, editor);
            if (EditorGUI.EndChangeCheck())
            {
                Helper.SetShaderKeywordEnabled(editor.targets, Helper.GetKeywordName(_keyWord, prop.name), prop.textureValue != null);
            }
        }

        public override void Apply(MaterialProperty prop)
        {
            base.Apply(prop);
            if (!prop.hasMixedValue && IsMatchPropType(prop))
                Helper.SetShaderKeywordEnabled(prop.targets, Helper.GetKeywordName(_keyWord, prop.name), prop.textureValue != null);
        }
    }
    
}
#if USE_QUERY_BUILDER
using System;
using UnityEngine;

namespace UnityEditor.Search
{
    abstract class QueryBlockEditor<T, B> : EditorWindow, IBlockEditor where B : QueryBlock
    {
        public T value;
        public object[] args { get; set; }
        public EditorWindow window => this;
        public B block { get; protected set; }

        protected abstract T Draw();
        protected virtual void Apply(in T value)
        {
            if (block is QueryFilterBlock filterBlock)
                filterBlock.formatValue = value;
            block.source.Apply();
        }

        public void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope(Styles.panelBorder))
            {
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                value = Draw();
                if (EditorGUI.EndChangeCheck())
                    Apply(value);
                GUILayout.FlexibleSpace();
            }

            if (Event.current.isKey && Event.current.keyCode == KeyCode.Escape)
                Close();
        }

        public void OnDisable()
        {
            block?.CloseEditor();
        }

        protected IBlockEditor Show(in B block, in Rect rect, in float width = 400f)
        {
            this.block = block;
            ShowAsDropDown(new Rect(rect.x, rect.yMax, rect.width, rect.height), new Vector2(width, EditorGUI.kSingleLineHeight * 1.5f));
            m_Parent.window.m_DontSaveToLayout = true;
            return this;
        }
    }

    class QueryTextBlockEditor : QueryBlockEditor<string, QueryBlock>
    {
        public static IBlockEditor Open(in Rect rect, QueryBlock block)
        {
            var w = CreateInstance<QueryTextBlockEditor>();
            w.value = block.value;
            return w.Show(block, rect, 200f);
        }

        protected override string Draw()
        {
            GUIUtility.SetKeyboardControlToFirstControlId();
            return EditorGUILayout.TextField(value, GUILayout.ExpandWidth(true));
        }

        protected override void Apply(in string value)
        {
            block.value = value;
            base.Apply(value);
        }
    }

    class QueryNumberBlockEditor : QueryBlockEditor<float, QueryFilterBlock>
    {
        public static IBlockEditor Open(in Rect rect, QueryFilterBlock block)
        {
            var w = CreateInstance<QueryNumberBlockEditor>();
            w.value = Convert.ToSingle(block.formatValue);
            w.block = block;
            return w.Show(block, new Rect(rect.x - 20f, rect.y, rect.width, rect.height), 150f);
        }

        protected override float Draw()
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginChangeCheck();
            var selectedOpIndex = EditorGUILayout.Popup(Array.IndexOf(QueryFilterBlock.ops, block.op), QueryFilterBlock.ops, GUILayout.MaxWidth(40f));
            if (EditorGUI.EndChangeCheck() && selectedOpIndex >= 0)
                block.SetOperator(QueryFilterBlock.ops[selectedOpIndex]);

            EditorGUIUtility.labelWidth = 40f;
            var newValue = EditorGUILayout.FloatField("Value", value, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
            return newValue;
        }

        protected override void Apply(in float value)
        {
            block.value = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            base.Apply(value);
        }
    }

    class QueryVectorBlockEditor : QueryBlockEditor<Vector4, QueryFilterBlock>
    {
        bool focused;
        int dimension { get; set; }

        public static IBlockEditor Open(in Rect rect, QueryFilterBlock block, int dimension)
        {
            var w = CreateInstance<QueryVectorBlockEditor>();
            w.block = block;
            w.value = (Vector4)block.formatValue;
            w.dimension = dimension;
            return w.Show(block, new Rect(rect.x - 150f, rect.y, rect.width, rect.height), dimension * 80f + 30f);
        }

        protected override void Apply(in Vector4 value)
        {
            block.SetValue(value);
        }

        protected override Vector4 Draw()
        {
            if (!focused)
            {
                GUIUtility.SetKeyboardControlToFirstControlId();
                focused = true;
            }

            var evt = Event.current;
            EditorGUIUtility.labelWidth = 12f;
            GUILayout.BeginHorizontal();

            EditorGUI.BeginChangeCheck();
            var selectedOpIndex = EditorGUILayout.Popup(Array.IndexOf(QueryFilterBlock.ops, block.op), QueryFilterBlock.ops, GUILayout.MaxWidth(40f));
            if (EditorGUI.EndChangeCheck() && selectedOpIndex >= 0)
                block.SetOperator(QueryFilterBlock.ops[selectedOpIndex]);

            if (dimension >= 2)
            {
                value.x = DrawVectorComponent(evt, "x", value.x);
                value.y = DrawVectorComponent(evt, "y", value.y);
            }

            if (dimension >= 3)
                value.z = DrawVectorComponent(evt, "z", value.z);

            if (dimension >= 4)
                value.w = DrawVectorComponent(evt, "w", value.w);

            GUILayout.EndHorizontal();
            return value;
        }

        private float DrawVectorComponent(in Event evt, in string label, float v)
        {
            if (float.IsNaN(v))
                EditorGUI.showMixedValue = true;
            v = EditorGUILayout.FloatField(label, v);
            var r = GUILayoutUtility.GetLastRect();
            if (evt.type == EventType.MouseDown && evt.button == 2 && r.Contains(evt.mousePosition))
            {
                v = float.NaN;
                GUI.changed = true;
                evt.Use();
            }
            EditorGUI.showMixedValue = false;
            return v;
        }
    }
}
#endif

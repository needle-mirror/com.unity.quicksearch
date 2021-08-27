#if USE_QUERY_BUILDER
//#define DEBUG_FILTER_BLOCK
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor.Search
{
    enum QueryBlockFormat
    {
        Default = 0,
        Toogle,
        Number,
        Object,
        Color,
        Enum,
        Range,
        Vector2,
        Vector3,
        Vector4
    }

    class QueryFilterBlock : QueryBlock
    {
        static readonly Dictionary<string, string> FilterAliases = new Dictionary<string, string> { { "t", "type" } };
        public static readonly string[] ops = new[] { "=", "<", "<=", ">=", ">" };

        public string id { get; private set; }
        public string op { get; protected set; }
        public QueryBlockFormat format { get; protected set; }
        public object formattedValue { get; set; }
        protected Type valueType { get; set; }
        public PropertyRange range { get; set; }

        public override bool formatNames => true;
        public override bool wantsEvents => HasInPlaceEditor();

        protected QueryFilterBlock(IQuerySource source, in string id, in string op, in string value)
            : base(source)
        {
            this.id = id;
            name = FindAlias(id);
            this.value = value ?? string.Empty;
            this.op = op;
            range = new PropertyRange(-128, 128);

            if (!string.IsNullOrEmpty(value))
                ParseValue(value);

            #if DEBUG_FILTER_BLOCK
            Debug.Log($"Block {this} => {format}, {formattedValue}");
            #endif
        }

        public QueryFilterBlock(IQuerySource source, FilterNode node)
            : this(source, node.filterId, node.operatorId, node.filterValue)
        {
        }

        public QueryFilterBlock(IQuerySource source, in string id, in Type type)
            : this(source, id, "=", null)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                format = QueryBlockFormat.Object;
            }
            else if (type.IsEnum)
            {
                SetEnumType(type);
            }
            else if (type == typeof(Color))
            {
                format = QueryBlockFormat.Color;
                formattedValue = new Color(0f, 0.8f, 0.0f);
            }
            else if (type == typeof(Vector2))
            {
                format = QueryBlockFormat.Vector2;
                SetVectorValue(new Vector4(float.NaN, float.NaN, float.NaN, float.NaN));
            }
            else if (type == typeof(Vector3))
            {
                format = QueryBlockFormat.Vector3;
                SetVectorValue(new Vector4(float.NaN, float.NaN, float.NaN, float.NaN));
            }
            else if (type == typeof(Vector4))
            {
                format = QueryBlockFormat.Vector4;
                SetVectorValue(new Vector4(float.NaN, float.NaN, float.NaN, float.NaN));
            }

            valueType = type;

            #if DEBUG_FILTER_BLOCK
            Debug.Log($"Block {this} => [{valueType}] {format}, {formattedValue}");
            #endif
        }

        public void SetEnumType(in Type type)
        {
            format = QueryBlockFormat.Enum;
            var enums = type.GetEnumValues();
            if (enums.Length > 0)
                SetValue(enums.GetValue(0));
        }

        private void ParseValue(in string value)
        {
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                format = QueryBlockFormat.Toogle;
                formattedValue = true;
            }
            else if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            {
                format = QueryBlockFormat.Toogle;
                formattedValue = false;
            }
            else if (Utils.TryParse(value, out float number))
            {
                format = QueryBlockFormat.Number;
                formattedValue = number;
                if (number < range.min)
                    range = new PropertyRange(number * 2f, range.max);
                else if (number > range.max)
                    range = new PropertyRange(range.min, number * 2f);

            }
            else if (value != null && value.Length > 0 && value[0] == '#' && ColorUtility.TryParseHtmlString(value, out var c))
            {
                format = QueryBlockFormat.Color;
                formattedValue = c;
            }
            else if (value != null && SearchValue.TryParseRange(value, out var range))
            {
                format = QueryBlockFormat.Range;
                formattedValue = range;
            }
            else if (Utils.TryParseVectorValue(value, out var v4, out var dimension))
            {
                if (dimension == 2)
                    format = QueryBlockFormat.Vector2;
                else if (dimension == 3)
                    format = QueryBlockFormat.Vector3;
                else if(dimension == 4)
                    format = QueryBlockFormat.Vector4;
                SetVectorValue(v4);
            }
            else if (!string.IsNullOrEmpty(value))
            {
                var guid = AssetDatabase.AssetPathToGUID(value);
                if (!string.IsNullOrEmpty(guid))
                {
                    format = QueryBlockFormat.Object;
                    formattedValue = AssetDatabase.LoadMainAssetAtPath(value);
                }
            }
            else
            {
                format = QueryBlockFormat.Default;
                formattedValue = value ?? string.Empty;
            }
        }

        public override void Apply(in object value)
        {
            formattedValue = value;
            if (value is float f)
            {
                format = QueryBlockFormat.Number;
                this.value = FormatFloatString(f);
                if (editor is QueryNumberBlockEditor ne)
                    range = ne.range;
            }
            else if (value is Vector4 v)
                SetVectorValue(v);
            else if (value != null)
                this.value = value.ToString();
            source.Apply();
        }

        private void SetVectorValue(in Vector4 v)
        {
            formattedValue = v;
            switch (format)
            {
                case QueryBlockFormat.Vector2: value = $"({FormatFloatString(v.x)},{FormatFloatString(v.y)})"; break;
                case QueryBlockFormat.Vector3: value = $"({FormatFloatString(v.x)},{FormatFloatString(v.y)},{FormatFloatString(v.z)})"; break;
                case QueryBlockFormat.Vector4: value = $"({FormatFloatString(v.x)},{FormatFloatString(v.y)},{FormatFloatString(v.z)},{FormatFloatString(v.w)})"; break;
            }
        }

        private static string FormatFloatString(in float f)
        {
            if (float.IsNaN(f))
                return string.Empty;
            return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public override void Apply(in SearchProposition searchProposition)
        {
            if (format == QueryBlockFormat.Enum)
            {
                if (searchProposition.data is Enum e)
                    SetValue(e);
                else if (searchProposition.type?.IsEnum == true)
                {
                    SetEnumType(searchProposition.type);
                    source.Apply();
                }
            }
        }

        public override IBlockEditor OpenEditor(in Rect rect)
        {
            var screenRect = new Rect(rect.position + context.searchView.position.position, rect.size);
            switch (format)
            {
                case QueryBlockFormat.Number: return QueryNumberBlockEditor.Open(screenRect, this);
                case QueryBlockFormat.Default: return QueryTextBlockEditor.Open(screenRect, this);
                case QueryBlockFormat.Vector2: return QueryVectorBlockEditor.Open(screenRect, this, 2);
                case QueryBlockFormat.Vector3: return QueryVectorBlockEditor.Open(screenRect, this, 3);
                case QueryBlockFormat.Vector4: return QueryVectorBlockEditor.Open(screenRect, this, 4);
                case QueryBlockFormat.Enum: return QuerySelector.Open(rect, this);
            }

            return null;
        }

        public override IEnumerable<SearchProposition> FetchPropositions()
        {
            if (format == QueryBlockFormat.Enum)
            {
                if (formattedValue is Enum ve)
                {
                    foreach (Enum v in ve.GetType().GetEnumValues())
                        yield return new SearchProposition(category: null, label: ObjectNames.NicifyVariableName(v.ToString()), data: v);
                }
                else
                {
                    foreach (var e in TypeCache.GetTypesDerivedFrom<Enum>())
                    {
                        if (!e.IsVisible)
                            continue;
                        var category = e.FullName;
                        var cpos = category.LastIndexOf('.');
                        if (cpos != -1)
                            category = category.Substring(0, cpos);
                        category = category.Replace(".", "/");
                        yield return new SearchProposition(category: category, label: ObjectNames.NicifyVariableName(e.Name), type: e);
                    }
                }
            }
        }

        protected override Color GetBackgroundColor()
        {
            return QueryColors.filter + new Color((float)format / 20f, (float)format / 18f, (float)format / 15f);
        }

        private string FindAlias(in string key)
        {
            if (!string.IsNullOrEmpty(key) && FilterAliases.TryGetValue(key, out var alias))
                return alias;
            return key;
        }

        public override string ToString()
        {
            return $"{id}{op}{FormatStringValue(value)}";
        }

        private object FormatStringValue(in string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";
            if (value.IndexOf(' ') != -1)
                return '"' + value + '"';
            return value;
        }

        protected override void AddContextualMenuItems(GenericMenu menu)
        {
            foreach (var _e in Enum.GetValues(typeof(QueryBlockFormat)))
            {
                var e = (QueryBlockFormat)_e;
                menu.AddItem(EditorGUIUtility.TrTextContent($"Format/{ObjectNames.NicifyVariableName(e.ToString())}"), format == e, () => SetFormat(e));
            }

            menu.AddItem(EditorGUIUtility.TrTextContent($"Operator/Equal (=)"), string.Equals(op, "=", StringComparison.Ordinal), () => SetOperator("="));
            menu.AddItem(EditorGUIUtility.TrTextContent($"Operator/Contains (:)"), string.Equals(op, ":", StringComparison.Ordinal), () => SetOperator(":"));
            menu.AddItem(EditorGUIUtility.TrTextContent($"Operator/Less Than or Equal (<=)"), string.Equals(op, "<=", StringComparison.Ordinal), () => SetOperator("<="));
            menu.AddItem(EditorGUIUtility.TrTextContent($"Operator/Greater Than or Equal (>=)"), string.Equals(op, ">=", StringComparison.Ordinal), () => SetOperator(">="));
            menu.AddItem(EditorGUIUtility.TrTextContent($"Operator/Less Than (<)"), string.Equals(op, "<", StringComparison.Ordinal), () => SetOperator("<"));
            menu.AddItem(EditorGUIUtility.TrTextContent($"Operator/Greater Than (>)"), string.Equals(op, ">", StringComparison.Ordinal), () => SetOperator(">"));
            //menu.AddItem(EditorGUIUtility.TrTextContent($"Operator/Not Equal (!=)"), string.Equals(op, "!=", StringComparison.Ordinal), () => SetOperator("!="));
        }

        internal void SetOperator(in string op)
        {
            this.op = op;
            source.Apply();
        }

        void SetFormat(in QueryBlockFormat format)
        {
            this.format = format;
            if (format == QueryBlockFormat.Vector2 || format == QueryBlockFormat.Vector3 || format == QueryBlockFormat.Vector4)
            {
                if (Utils.TryParseVectorValue(value, out var v4, out _))
                    SetVectorValue(v4);
                else
                    formattedValue = new Vector4(float.NaN, float.NaN, float.NaN, float.NaN);
            }
            source.Repaint();
        }

        bool HasInPlaceEditor()
        {
            switch (format)
            {
                case QueryBlockFormat.Object:
                case QueryBlockFormat.Color:
                case QueryBlockFormat.Toogle:
                    return true;
            }

            return false;
        }

        private float GetInPlaceEditorWidth()
        {
            switch (format)
            {
                case QueryBlockFormat.Object: return 120f;
                case QueryBlockFormat.Color: return 20f;
                case QueryBlockFormat.Toogle: return 4f;
            }

            return 0f;
        }

        private object DrawInPlaceEditor(in Rect at, in Rect blockRect)
        {
            var x = at.xMax - 4f;
            switch (format)
            {
                case QueryBlockFormat.Object:
                    var editorRect = new Rect(x, blockRect.y + 2f, blockRect.width - (x - blockRect.xMin) - 6f, blockRect.height - 4f);
                    var objectFieldType = valueType ?? typeof(UnityEngine.Object);
                    var allowSceneObjects = typeof(Component).IsAssignableFrom(objectFieldType) || typeof(GameObject).IsAssignableFrom(objectFieldType);
                    return EditorGUI.ObjectField(editorRect, formattedValue as UnityEngine.Object, objectFieldType, allowSceneObjects: allowSceneObjects);
                case QueryBlockFormat.Color:
                    Color c = Color.black;
                    if (formattedValue is Color fc)
                        c = fc;
                    editorRect = new Rect(x, blockRect.y + 2f, blockRect.width - (x - blockRect.xMin) - 8f, blockRect.height - 4f);
                    return EditorGUI.ColorField(editorRect, c, showEyedropper: false, showAlpha: true);
                case QueryBlockFormat.Toogle:
                    var b = false;
                    if (formattedValue is bool fb)
                        b = fb;
                    editorRect = new Rect(x, blockRect.y + 2f, blockRect.width - (x - blockRect.xMin) - 8f, blockRect.height - 4f);
                    return EditorGUI.Toggle(editorRect, b);
            }

            return null;
        }

        private void SetValue(object value)
        {
            formattedValue = value;
            if (value is Color c)
            {
                this.op = "=";
                this.value = $"#{ColorUtility.ToHtmlStringRGB(c).ToLowerInvariant()}";
            }
            else if (value is UnityEngine.Object obj)
            {
                var assetPath = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    this.op = "=";
                    this.value = assetPath;
                }
                else if (!string.IsNullOrEmpty(assetPath))
                {
                    this.op = "=";
                    this.value = obj.GetInstanceID().ToString();
                }
            }
            else if (value is Enum e)
            {
                op = "=";
                this.value = e.ToString();
            }
            else
            {
                this.value = formattedValue?.ToString() ?? string.Empty;
            }
            source.Apply();
        }

        public override Rect Layout(in Vector2 at, in float availableSpace)
        {
            if (!HasInPlaceEditor())
                return base.Layout(at, availableSpace);

            var labelStyle = Styles.QueryBuilder.label;
            var nameContent = labelStyle.CreateContent(name);
            var editorWidth = GetInPlaceEditorWidth();
            var blockWidth = nameContent.width + editorWidth + labelStyle.margin.horizontal * 2f + blockExtraPadding;
            return GetRect(at, blockWidth, blockHeight);
        }

        protected override Rect DrawSeparator(in Rect at)
        {
            var sepRect = new Rect(at.xMax, at.yMin + 1f, 1f, Mathf.Ceil(at.height - 1f));
            var opRect = new Rect(at.xMax - 6f, at.yMin - 1f, 11f, Mathf.Ceil(at.height - 1f));
            if (string.Equals(op, ">=", StringComparison.Ordinal))
                Styles.QueryBuilder.label.Draw(opRect, "\u2265", false, false, false, false);
            else if (string.Equals(op, "<=", StringComparison.Ordinal))
                Styles.QueryBuilder.label.Draw(opRect, "\u2264", false, false, false, false);
            else if (string.Equals(op, ">", StringComparison.Ordinal))
                Styles.QueryBuilder.label.Draw(opRect, "\u003E", false, false, false, false);
            else if (string.Equals(op, "<", StringComparison.Ordinal))
                Styles.QueryBuilder.label.Draw(opRect, "\u003C", false, false, false, false);
            else
                return base.DrawSeparator(at);

            return sepRect;
        }

        protected override void Draw(in Rect blockRect, in Vector2 mousePosition)
        {
            if (!HasInPlaceEditor())
            {
                base.Draw(blockRect, mousePosition);
                return;
            }

            var labelStyle = Styles.QueryBuilder.label;
            var nameContent = labelStyle.CreateContent(name);

            if (Event.current.type == EventType.Repaint)
                DrawBackground(blockRect);

            var nameRect = DrawName(blockRect, mousePosition, nameContent);
            EditorGUI.BeginChangeCheck();
            var newValue = DrawInPlaceEditor(nameRect, blockRect);
            if (EditorGUI.EndChangeCheck())
                SetValue(newValue);

            if (Event.current.type == EventType.Repaint)
                DrawBorders(blockRect, mousePosition);
        }
    }

    abstract class QueryBlockEditor<T> : EditorWindow, IBlockEditor
    {
        public T value;
        public object[] args { get; set; }
        public EditorWindow window => this;
        public IBlockSource dataSource { get; protected set; }

        protected abstract T Draw();

        public void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope(Styles.panelBorder))
            {
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                value = Draw();
                if (EditorGUI.EndChangeCheck())
                    dataSource.Apply(value);
                GUILayout.FlexibleSpace();
            }

            if (Event.current.isKey && Event.current.keyCode == KeyCode.Escape)
                Close();
        }

        public void OnDisable()
        {
            dataSource.CloseEditor();
        }

        protected IBlockEditor Show(IBlockSource dataSource, in Rect rect, in float width = 400f)
        {
            this.dataSource = dataSource;
            ShowAsDropDown(new Rect(rect.x, rect.yMax, rect.width, rect.height), new Vector2(width, EditorGUI.kSingleLineHeight * 1.5f));
            return this;
        }
    }

    class QueryNumberBlockEditor : QueryBlockEditor<float>
    {
        public QueryFilterBlock block{ get; set; }
        public PropertyRange range
        {
            get => block.range;
            set => block.range = value;
        }

        public static IBlockEditor Open(in Rect rect, QueryFilterBlock block)
        {
            var w = CreateInstance<QueryNumberBlockEditor>();
            w.value = (float)block.formattedValue;
            w.block = block;
            return w.Show(block, rect);
        }

        protected override float Draw()
        {
            const float minMaxControlWidth = 50f;
            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginChangeCheck();
            var selectedOpIndex = EditorGUILayout.Popup(Array.IndexOf(QueryFilterBlock.ops, block.op), QueryFilterBlock.ops, GUILayout.MaxWidth(40f));
            if (EditorGUI.EndChangeCheck() && selectedOpIndex >= 0)
                block.SetOperator(QueryFilterBlock.ops[selectedOpIndex]);

            EditorGUI.BeginChangeCheck();
            var min = EditorGUILayout.DoubleField(range.min, GUILayout.Width(minMaxControlWidth));
            if (EditorGUI.EndChangeCheck())
                range = new PropertyRange(min, range.max);
            var newValue = EditorGUILayout.Slider(value, (float)range.min, (float)range.max, GUILayout.ExpandWidth(true));
            if (newValue != value)
            {
                if (newValue == range.max)
                    range = new PropertyRange(range.min, range.max + Math.Abs(Math.Min(range.max, 1000)));
                else if (newValue == range.min)
                    range = new PropertyRange(range.min - Math.Abs(Math.Min(range.min, 1000)), range.max);
            }
            EditorGUI.BeginChangeCheck();
            var max = EditorGUILayout.DoubleField(range.max, GUILayout.Width(minMaxControlWidth));
            if (EditorGUI.EndChangeCheck())
                range = new PropertyRange(range.min, max);
            EditorGUILayout.EndHorizontal();
            return newValue;
        }
    }

    class QueryTextBlockEditor : QueryBlockEditor<string>
    {
        public static IBlockEditor Open(in Rect rect, IBlockSource dataSource)
        {
            var w = CreateInstance<QueryTextBlockEditor>();
            if (dataSource is QueryFilterBlock b)
                w.value = b.value;
            return w.Show(dataSource, rect, 200f);
        }

        protected override string Draw()
        {
            GUIUtility.SetKeyboardControlToFirstControlId();
            return EditorGUILayout.TextField(value, GUILayout.ExpandWidth(true));
        }
    }

    class QueryVectorBlockEditor : QueryBlockEditor<Vector4>
    {
        bool focused;
        int dimension { get; set; }
        QueryFilterBlock block { get; set; }

        public static IBlockEditor Open(in Rect rect, QueryFilterBlock block, int dimension)
        {
            var w = CreateInstance<QueryVectorBlockEditor>();
            w.block = block;
            w.value = (Vector4)block.formattedValue;
            w.dimension = dimension;
            return w.Show(block, rect, dimension * 80f + 30f);
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

#if USE_QUERY_BUILDER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace UnityEditor.Search
{
    class QuerySelectorItemGUI : AdvancedDropdownGUI
    {
        class Styles
        {
            public static GUIStyle itemStyle = new GUIStyle("DD LargeItemStyle")
            {
                fixedHeight = 22
            };

            // public static GUIStyle textStyle = new GUIStyle("DD LargeItemStyle")
            public static GUIStyle textStyle = new GUIStyle(Search.Styles.QueryBuilder.label)
            {
                padding = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.MiddleCenter
            };

            public static GUIStyle propositionIcon = new GUIStyle("label")
            {
                fixedWidth = 18f,
                fixedHeight = 18f,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };

            public static Vector4 borderWidth4 = new Vector4(1, 1, 1, 1);
            public static Vector4 borderRadius4 = new Vector4(8f, 8f, 8f, 8f);
        }

        internal override GUIStyle lineStyle => Styles.itemStyle;
        internal override Vector2 iconSize => new Vector2(Styles.propositionIcon.fixedWidth, Styles.propositionIcon.fixedHeight);

        public QuerySelectorItemGUI(AdvancedDropdownDataSource dataSource)
            : base(dataSource)
        {
        }

        internal override Rect GetItemRect(GUIContent content)
        {
            return base.GetItemRect(content);
        }

        internal override float CalcItemHeight(GUIContent content, float width)
        {
            return SearchField.minSinglelineTextHeight + 2;
        }

        internal override Vector2 CalcItemSize(GUIContent content)
        {
            return base.CalcItemSize(content);
        }

        internal override void DrawItemContent(AdvancedDropdownItem item, Rect rect, GUIContent content, bool isHover, bool isActive, bool on, bool hasKeyboardFocus)
        {
            if (item.children.Any())
            {
                base.DrawItemContent(item, rect, content, isHover, isActive, on, hasKeyboardFocus);
                return;
            }

            var bgColor = ((SearchProposition)item.userData).color;
            if (bgColor == Color.clear)
                bgColor = QueryColors.filter;

            // Add spacing between items
            rect.y += 3;
            rect.yMax -= 3;

            // Left margin
            rect.x += 2;

            if (content.image != null)
            {
                // Draw icon if needed. If no icon, rect is already offsetted.
                var iconRect = new Rect(rect.x, rect.y, iconSize.x, iconSize.y);
                Styles.propositionIcon.Draw(iconRect, GUIContent.Temp("", content.image), false, false, false, false);
                rect.xMin += iconSize.x + 2;
            }

            var textContent = GUIContent.Temp(content.text);
            var backgroundRect = rect;
            var size = CalcItemSize(textContent);
            if (backgroundRect.width > size.x + iconSize.x)
                backgroundRect.width = size.x + iconSize.x;

            var selected = isHover || on;
            var color = selected ? bgColor * QueryColors.selectedTint : bgColor;

            // Draw blockbackground
            GUI.DrawTexture(backgroundRect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, false, 0f, color, Vector4.zero, Styles.borderRadius4);

            // Draw Text
            Styles.textStyle.Draw(backgroundRect, textContent, false, false, false, false);

            if (selected)
            {
                // Draw border
                GUI.DrawTexture(backgroundRect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, false, 0f, QueryColors.selectedBorderColor, Styles.borderWidth4, Styles.borderRadius4);
            }
        }
    }

    class QuerySelector : AdvancedDropdown, IBlockEditor
    {
        private readonly string m_Title;
        private readonly IBlockSource m_BlockSource;
        private readonly IEnumerable<SearchProposition> m_Propositions;

        public SearchContext context => m_BlockSource.context;
        public EditorWindow window => m_WindowInstance;

        public QuerySelector(Rect rect, IBlockSource dataSource, string title = null)
            : base(new AdvancedDropdownState())
        {
            m_BlockSource = dataSource;
            m_Title = title ?? m_BlockSource.editorTitle ?? m_BlockSource.name ?? string.Empty;
            m_Propositions = m_BlockSource.FetchPropositions().Where(p => p.valid);

            minimumSize = new Vector2(Mathf.Max(rect.width, 300f), 350f);
            maximumSize = new Vector2(Mathf.Max(rect.width, 400f), 450f);

            m_DataSource = new CallbackDataSource(BuildRoot);
            m_Gui = new QuerySelectorItemGUI(m_DataSource);
        }

        public static QuerySelector Open(Rect r, IBlockSource source, string title = null)
        {
            var w = new QuerySelector(r, source, title);
            w.Show(r);
            w.Bind();
            return w;
        }

        readonly struct ItemPropositionComparer : IComparer<AdvancedDropdownItem>
        {
            public int Compare(AdvancedDropdownItem x, AdvancedDropdownItem y)
            {
                if (x.userData is SearchProposition px && y.userData is SearchProposition py)
                    return px.priority.CompareTo(py.priority);
                return x.CompareTo(y);
            }
        }

        private void Bind()
        {
            m_WindowInstance.windowClosed += OnClose;
            m_WindowInstance.selectionCanceled += OnSelectionCanceled;
            m_DataSource.searchMatchItem = OnSearchItemMatch;
            m_DataSource.searchMatchItemComparer = new ItemPropositionComparer();
        }

        private bool OnSearchItemMatch(in AdvancedDropdownItem item, in string[] words, out bool didMatchStart)
        {
            didMatchStart = false;
            var label = item.displayName ?? item.name;
            var pp = label.LastIndexOf('(');
            pp = pp == -1 ? label.Length : pp;
            foreach (var w in words)
            {
                var fp = label.IndexOf(w, 0, pp, StringComparison.OrdinalIgnoreCase);
                if (fp == -1)
                    return false;
                didMatchStart |= (fp == 0 || label[fp-1] == ' ');
            }
            return true;
        }

        private void OnClose(AdvancedDropdownWindow w = null)
        {
            m_BlockSource.CloseEditor();
        }

        private void OnSelectionCanceled()
        {
            OnClose();
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var rootItem = new AdvancedDropdownItem(m_Title);
            var formatNames = m_BlockSource.formatNames;
            foreach (var p in m_Propositions)
            {
                var path = p.path;
                var name = p.label;
                var prefix = p.category;

                if (name.LastIndexOf('/') != -1)
                {
                    var ls = path.LastIndexOf('/');
                    name = path.Substring(ls+1);
                    prefix = path.Substring(0, ls);
                }

                var newItem = new AdvancedDropdownItem(path)
                {
                    displayName = formatNames ? ObjectNames.NicifyVariableName(name) : name,
                    icon = p.icon,
                    tooltip = p.help,
                    userData = p
                };

                var parent = rootItem;
                if (prefix != null)
                    parent = MakeParents(prefix, p, rootItem);

                var fit = FindItem(name, parent);
                if (fit == null)
                    parent.AddChild(newItem);
                else if (p.icon)
                    fit.icon = p.icon;
            }

            return rootItem;
        }

        private AdvancedDropdownItem FindItem(string path, AdvancedDropdownItem root)
        {
            var pos = path.IndexOf('/');
            var name = pos == -1 ? path : path.Substring(0, pos);
            var suffix = pos == -1 ? null : path.Substring(pos + 1);

            foreach (var c in root.children)
            {
                if (suffix == null && string.Equals(c.name, name, StringComparison.Ordinal))
                    return c;

                if (suffix == null)
                    continue;

                var f = FindItem(suffix, c);
                if (f != null)
                    return f;
            }

            return null;
        }

        private AdvancedDropdownItem MakeParents(string prefix, in SearchProposition proposition, AdvancedDropdownItem parent)
        {
            var parts = prefix.Split('/');

            foreach (var p in parts)
            {
                var f = FindItem(p, parent);
                if (f != null)
                {
                    if (f.icon == null)
                        f.icon = proposition.icon;
                    parent = f;
                }
                else
                {
                    var newItem = new AdvancedDropdownItem(p) { icon = proposition.icon };
                    parent.AddChild(newItem);
                    parent = newItem;
                }
            }

            return parent;
        }

        protected override void ItemSelected(AdvancedDropdownItem i)
        {
            if (i.userData is SearchProposition sp)
                m_BlockSource.Apply(sp);
        }
    }
}
#endif

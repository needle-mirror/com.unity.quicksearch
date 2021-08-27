#if USE_QUERY_BUILDER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace UnityEditor.Search
{
    class QuerySelector : AdvancedDropdown, IBlockEditor
    {
        private readonly string m_Title;
        private readonly IBlockSource m_BlockSource;
        private readonly IEnumerable<SearchProposition> m_Propositions;

        public SearchContext context => m_BlockSource.context;
        public EditorWindow window => m_WindowInstance;

        public QuerySelector(Rect rect, IBlockSource dataSource)
            : base(new AdvancedDropdownState())
        {
            m_BlockSource = dataSource;
            m_Title = m_BlockSource.name ?? string.Empty;
            m_Propositions = m_BlockSource.FetchPropositions().Where(p => p.valid).OrderBy(p => p.priority);

            minimumSize = new Vector2(Mathf.Max(rect.width, 300f), 350f);
        }

        public static QuerySelector Open(Rect r, IBlockSource source)
        {
            var w = new QuerySelector(r, source);
            w.Show(r);
            w.Bind();
            return w;
        }

        private void Bind()
        {
            m_WindowInstance.windowClosed += OnClose;
            m_WindowInstance.selectionCanceled += OnSelectionCanceled;
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
                var path = string.IsNullOrEmpty(p.category) ? p.label : $"{p.category}/{p.label}";
                var name = p.label;
                var prefix = p.category;

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

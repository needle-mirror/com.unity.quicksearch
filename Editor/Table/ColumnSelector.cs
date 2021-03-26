using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

#if USE_SEARCH_MODULE
namespace UnityEditor.Search
{
    class ColumnSelector : AdvancedDropdown
    {
        const string k_AddlAllItemName = "Add all...";
        private readonly string m_Title;
        private readonly IEnumerable<SearchColumn> m_Columns;
        private readonly AddColumnsHandler m_AddColumnsHandler;
        private readonly int m_ActiveColumnIndex;

        public delegate void AddColumnsHandler(IEnumerable<SearchColumn> descriptors, int activeColumnIndex);

        public ColumnSelector(IEnumerable<SearchColumn> descriptors, string title, AddColumnsHandler addColumnsHandler, int activeColumnIndex)
            : base(new AdvancedDropdownState())
        {
            m_Title = title;
            m_Columns = descriptors;
            m_AddColumnsHandler = addColumnsHandler;
            m_ActiveColumnIndex = activeColumnIndex;

            minimumSize = new Vector2(250, 350);
        }

        public static AdvancedDropdown AddColumns(AddColumnsHandler addColumnsHandler, IEnumerable<SearchColumn> descriptors, Vector2 mousePosition, int activeColumnIndex)
        {
            var dropdown = new ColumnSelector(descriptors, "Select column...", addColumnsHandler, activeColumnIndex);
            dropdown.Show(new Rect(mousePosition.x, mousePosition.y, 1, 1));
            return dropdown;
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var rootItem = new AdvancedDropdownItem(m_Title);
            foreach (var column in m_Columns)
            {
                var path = column.path;
                var pos = path.LastIndexOf('/');
                var name = pos == -1 ? column.name : path.Substring(pos + 1);
                var prefix = pos == -1 ? null : path.Substring(0, pos);

                AdvancedDropdownItem newItem = new AdvancedDropdownItem(name)
                {
                    displayName = string.IsNullOrEmpty(column.content.text) ? column.content.tooltip : SearchColumn.ParseName(column.content.text),
                    icon = column.content.image as Texture2D,
                    tooltip = column.content.tooltip,
                    userData = column
                };

                var parent = rootItem;
                if (prefix != null)
                    parent = MakeParents(prefix, column, rootItem);

                if (FindItem(name, parent) == null)
                    parent.AddChild(newItem);
            }

            rootItem.SortChildren(SortColumnProviders);
            foreach (var c in rootItem.children)
                c.SortChildren(SortColumns, true);
            return rootItem;
        }

        protected override void ItemSelected(AdvancedDropdownItem i)
        {
            var properties = new List<SearchColumn>();
            if (i.userData is SearchColumn column)
                properties.Add(column);
            else if (i.userData is AdvancedDropdownItem addAllItem)
                AddAll(properties, addAllItem.children, addAllItem.children.Where(c => c.userData is SearchColumn).All(c => c.children.Any()));

            m_AddColumnsHandler?.Invoke(properties, m_ActiveColumnIndex);
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

        private AdvancedDropdownItem MakeParents(string prefix, SearchColumn desc, AdvancedDropdownItem parent)
        {
            var parts = prefix.Split('/');

            foreach (var p in parts)
            {
                var f = FindItem(p, parent);
                if (f != null)
                {
                    parent = f;
                }
                else
                {
                    AdvancedDropdownItem newItem = new AdvancedDropdownItem(p) { icon = desc.content.image as Texture2D, tooltip = desc.content.tooltip };
                    newItem.AddChild(new AdvancedDropdownItem(k_AddlAllItemName) { userData = newItem });
                    parent.AddChild(newItem);
                    parent = newItem;
                }
            }

            return parent;
        }

        private int SortColumnProviders(AdvancedDropdownItem lhs, AdvancedDropdownItem rhs)
        {
            if (!lhs.hasChildren && rhs.hasChildren)
                return -1;
            else if (lhs.hasChildren && !rhs.hasChildren)
                return 1;
            if (string.Equals(lhs.displayName, "Default"))
                return -1;
            if (string.Equals(rhs.displayName, "Default"))
                return 1;

            return lhs.displayName.CompareTo(rhs.displayName);
        }

        private int SortColumns(AdvancedDropdownItem lhs, AdvancedDropdownItem rhs)
        {
            if (lhs.hasChildren && !rhs.hasChildren)
                return 1;
            if (!lhs.hasChildren && rhs.hasChildren)
                return -1;

            if (string.Equals(lhs.displayName, k_AddlAllItemName))
                return -1;
            if (string.Equals(rhs.displayName, k_AddlAllItemName))
                return 1;

            return lhs.displayName.CompareTo(rhs.displayName);
        }

        private void AddAll(List<SearchColumn> properties, IEnumerable<AdvancedDropdownItem> children, bool recursive)
        {
            foreach (var toAdd in children)
            {
                if (recursive)
                    AddAll(properties, toAdd.children, recursive);

                if (!(toAdd.userData is SearchColumn ac))
                    continue;
                properties.Add(ac);
            }
        }
    }
}
#endif
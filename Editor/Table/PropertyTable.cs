using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace UnityEditor.Search
{
    interface ITableView
    {
        SearchContext context { get; }
        void AddColumn(Vector2 mousePosition, int activeColumnIndex);
        void AddColumns(IEnumerable<SearchColumn> descriptors, int activeColumnIndex);
        void SetupColumns(IEnumerable<SearchItem> elements = null);
        void RemoveColumn(int activeColumnIndex);
        void SwapColumns(int columnIndex, int swappedColumnIndex);
        void UpdateColumnSettings(int columnIndex, MultiColumnHeaderState.Column columnSettings);
        IEnumerable<SearchItem> GetElements();
        IEnumerable<SearchColumn> GetColumns();
        IEnumerable<SearchItem> GetRows();
        SearchTable GetSearchTable();
        void SetSelection(IEnumerable<SearchItem> items);
        void DoubleClick(SearchItem item);
        void SetDirty();
        void AddColumnHeaderContextMenuItems(GenericMenu menu, SearchColumn sourceColumn);
        bool AddColumnHeaderContextMenuItems(GenericMenu menu);
        bool OpenContextualMenu(Event evt, SearchItem item);
        bool IsReadOnly();
    }

    class PropertyItem : TreeViewItem
    {
        private readonly SearchItem m_Data;

        public PropertyItem(int id, int depth, SearchItem ltd)
            : base(id, depth, ltd != null ? ltd.id : "root")
        {
            m_Data = ltd;
        }

        public SearchItem GetData() { return m_Data; }
    }

    class PropertyColumn : MultiColumnHeaderState.Column
    {
        #if !USE_SEARCH_MODULE
        public object userDataObj;
        #endif

        public PropertyColumn(SearchColumn c)
        {
            userDataObj = c;
        }
    }

    class PropertyTable : TreeView, IDisposable
    {
        static class Constants
        {
            public static readonly string focusHelper = "PropertyTableFocusHelper";
            public static readonly string serializeTreeViewState = "_TreeViewState";
            public static readonly string serializeColumnHeaderState = "_ColumnHeaderState";
        }

        class PropertyTableColumnHeader : MultiColumnHeader
        {
            private readonly ITableView m_TableView;

            public PropertyTableColumnHeader(ITableView tableView)
                : base(new MultiColumnHeaderState(ConvertColumns(tableView.GetColumns())))
            {
                height = 22f;
                canSort = true;
                m_TableView = tableView;
                sortingChanged += mch => m_TableView.SetDirty();
                visibleColumnsChanged += mch => m_TableView.SetDirty();
                #if USE_SEARCH_MODULE
                allowDraggingColumnsToReorder = true;
                columnSettingsChanged += OnColumnSettingsChanged;
                columnsSwapped += OnColumnsSwapped;
                #endif
            }

            private static MultiColumnHeaderState.Column[] ConvertColumns(IEnumerable<SearchColumn> searchColumns)
            {
                var headerColumns = new List<MultiColumnHeaderState.Column>();
                foreach (var c in searchColumns)
                {
                    headerColumns.Add(new PropertyColumn(c)
                    {
                        width = c.width,
                        headerContent = new GUIContent(c.content),
                        canSort = c.options.HasFlag(SearchColumnFlags.CanSort),
                        sortedAscending = !c.options.HasFlag(SearchColumnFlags.SortedDescending),
                        headerTextAlignment = GetHeaderTextAligment(c.options),
                        allowToggleVisibility = c.options.HasFlag(SearchColumnFlags.CanHide),
                        sortingArrowAlignment = TextAlignment.Right,
                        minWidth = 32f,
                        maxWidth = 1000000f,
                        autoResize = false,
                        contextMenuText = null
                    });
                }

                if (headerColumns.Count == 0)
                    headerColumns.Add(new PropertyColumn(new SearchColumn("Invalid")));

                return headerColumns.ToArray();
            }

            private static TextAlignment GetHeaderTextAligment(SearchColumnFlags options)
            {
                if (options.HasFlag(SearchColumnFlags.TextAlignmentCenter))
                    return TextAlignment.Center;
                if (options.HasFlag(SearchColumnFlags.TextAlignmentRight))
                    return TextAlignment.Right;
                return TextAlignment.Left;
            }

            protected override void AddColumnHeaderContextMenuItems(GenericMenu menu)
            {
                var isFullMenuOverride = m_TableView.AddColumnHeaderContextMenuItems(menu);
                if (isFullMenuOverride)
                    return;

                var mousePosition = Event.current.mousePosition;
                var windowMousePosition = Utils.Unclip(new Rect(mousePosition, Vector2.zero)).position;

                #if USE_SEARCH_MODULE
                var activeColumn = currentColumnIndex;
                #else
                int activeColumn = -1;
                for (int i = 0; i < state.columns.Length; ++i)
                {
                    var vi = GetVisibleColumnIndex(i);
                    if (vi == -1)
                        continue;

                    var cr = GetColumnRect(vi);
                    if (!cr.Contains(mousePosition))
                        continue;

                    activeColumn = i;
                    break;
                }
                #endif

                if (state.columns.Length > 1)
                {
                    for (int i = 0; i < state.columns.Length; ++i)
                    {
                        var column = state.columns[i];
                        var menuContent = new GUIContent("Show Columns/" + GetDisplayLabel(column.headerContent));
                        if (state.visibleColumns.Length == 1 && state.visibleColumns.Contains(i))
                            menu.AddDisabledItem(menuContent, state.visibleColumns.Contains(i));
                        else
                            menu.AddItem(menuContent, state.visibleColumns.Contains(i), index => ToggleColumnVisibility((int)index), i);
                    }
                }


                if (activeColumn != -1)
                {
                    if (state.columns[activeColumn] is PropertyColumn pc && pc.userDataObj is SearchColumn sourceColumn)
                        m_TableView.AddColumnHeaderContextMenuItems(menu, sourceColumn);
                }

                // If the table view is readonly, we can't change the columns
                if (m_TableView.IsReadOnly())
                    return;

                menu.AddSeparator("");
                menu.AddItem(EditorGUIUtility.TrTextContent("Add Column..."), false, () => m_TableView.AddColumn(windowMousePosition, activeColumn));

                if (activeColumn != -1)
                {
                    var colName = state.columns[activeColumn].headerContent.text;
                    menu.AddItem(EditorGUIUtility.TrTextContent($"Edit {colName}..."), false, EditColumn, activeColumn);
                    menu.AddItem(EditorGUIUtility.TrTextContent($"Remove {colName}"), false, () =>
                    {
                        if (state.columns.Length == 1)
                            ResetColumnLayout();
                        else
                            m_TableView.RemoveColumn(activeColumn);
                    });
                }

                menu.AddSeparator("");
                menu.AddItem(EditorGUIUtility.TrTextContent("Reset Columns"), false, ResetColumnLayout);
            }

            private void EditColumn(object userData)
            {
                int columnIndex = (int)userData;
                var column = state.columns[columnIndex];

                ColumnEditor.ShowWindow(column, (_column) => m_TableView.UpdateColumnSettings(columnIndex, _column));
            }

            private void ResetColumnLayout()
            {
                m_TableView.SetupColumns();
                SearchAnalytics.SendEvent(null, SearchAnalytics.GenericEventType.QuickSearchTableReset, m_TableView.context.searchQuery);
            }

            private void ToggleColumnVisibility(int columnIndex)
            {
                ToggleVisibility(columnIndex);
                SearchAnalytics.SendEvent(null, SearchAnalytics.GenericEventType.QuickSearchTableToggleColumnVisibility);
            }

            private static string GetDisplayLabel(GUIContent content)
            {
                if (!string.IsNullOrEmpty(content.text))
                    return content.text;

                if (!string.IsNullOrEmpty(content.tooltip))
                    return content.tooltip;

                if (content.image && !string.IsNullOrEmpty(content.image.name))
                    return content.image.name;

                return "Unnamed";
            }

            public override void OnGUI(Rect rect, float xScroll)
            {
                // If the table view is readonly, we can't change the columns or export the table
                if (m_TableView.IsReadOnly())
                {
                    base.OnGUI(rect, xScroll);
                    return;
                }

                #if USE_SEARCH_MODULE
                var moreButtonRects = GetMoreButtonRect(rect, 1);

                GUI.Label(rect, GUIContent.none, DefaultStyles.background);
                if (GUI.Button(moreButtonRects[0], Styles.addMoreColumns, Styles.actionButton))
                    m_TableView.AddColumn(moreButtonRects[0].center - new Vector2(200f, 0), -1);

                rect.xMax = moreButtonRects[0].xMin;
                #endif
                base.OnGUI(rect, xScroll);
            }

            private Rect[] GetMoreButtonRect(Rect headerRect, int numButtons)
            {
                const float k_MoreButtonSize = 16f;
                const float k_MoreButtonHPadding = 4f;
                var rects = new Rect[numButtons];
                var x = headerRect.xMax - numButtons * (k_MoreButtonHPadding + k_MoreButtonSize);
                var y = headerRect.yMin + (headerRect.height - k_MoreButtonSize) / 2f;

                for (var i = 0; i < numButtons; ++i)
                {
                    rects[i] = new Rect(x, y, k_MoreButtonSize, k_MoreButtonSize);
                    x += k_MoreButtonHPadding + k_MoreButtonSize;
                }

                return rects;
            }

            private void OnColumnSettingsChanged(int columnIndex)
            {
                m_TableView.UpdateColumnSettings(columnIndex, state.columns[columnIndex]);
            }

            private void OnColumnsSwapped(int columnIndex, int swappedColumnIndex)
            {
                m_TableView.SwapColumns(columnIndex, swappedColumnIndex);
            }
        }

        private readonly string m_SerializationUID;
        private readonly ITableView m_TableView;
        private List<TreeViewItem> m_Items;
        private Rect m_ViewRect;
        private int m_FrameColumnIndex = -1;

        public PropertyTable(string serializationUID, ITableView tableView)
            : base(new TreeViewState(), new PropertyTableColumnHeader(tableView))
        {
            m_TableView = tableView;
            m_SerializationUID = serializationUID;

            multiColumnHeader.sortingChanged += OnSortingChanged;
            multiColumnHeader.visibleColumnsChanged += OnVisibleColumnChanged;
            showAlternatingRowBackgrounds = true;
            showBorder = false;
            rowHeight = EditorGUIUtility.singleLineHeight + 4;

            if (m_SerializationUID != null)
                DeserializeState(m_SerializationUID);
            Reload();
        }

        public override void OnGUI(Rect tableRect)
        {
            var evt = Event.current;
            if (evt.type == EventType.Layout)
                return;

            m_ViewRect = tableRect;
            m_ViewRect.yMin -= 20;
            m_ViewRect.yMax += 20;
            base.OnGUI(tableRect);

            if (m_FrameColumnIndex >= 0)
            {
                //multiColumnHeader.UpdateColumnHeaderRects(tableRect);
                var rect = multiColumnHeader.GetColumnRect(m_FrameColumnIndex);
                state.scrollPos.x = rect.position.x;
                m_FrameColumnIndex = -1;
                Repaint();
            }
        }

        public void Dispose()
        {
            if (m_SerializationUID != null)
                SerializeState(m_SerializationUID);
        }

        protected override TreeViewItem BuildRoot()
        {
            m_Items = null;
            return new PropertyItem(-1, -1, null);
        }

        protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
        {
            if (m_Items == null)
                m_Items = m_TableView.GetElements().Select(d => new PropertyItem(d.id.GetHashCode(), 0, d)).Cast<TreeViewItem>().ToList();

            if (multiColumnHeader.sortedColumnIndex >= 0)
                Sort(m_Items, multiColumnHeader.sortedColumnIndex);

            Utils.SetChildParentReferences(m_Items, root);

            return m_Items;
        }

        protected override bool CanStartDrag(CanStartDragArgs args)
        {
            if (args.draggedItem is PropertyItem pi)
                return pi.GetData()?.provider?.startDrag != null;
            return false;
        }

        protected override void SetupDragAndDrop(SetupDragAndDropArgs args)
        {
            var selectedObjects = args.draggedItemIDs.Select(i => GetObject(i)).Where(o => o).ToArray();
            if (selectedObjects.Length == 0)
                return;
            var paths = selectedObjects.Select(i => GetAssetPath(i)).ToArray();
            Utils.StartDrag(selectedObjects, paths, paths[0]);
        }

        private string GetAssetPath(UnityEngine.Object obj)
        {
            return SearchUtils.GetObjectPath(obj);
        }

        private UnityEngine.Object GetObject(int id)
        {
            if (FindItem(id, rootItem) is PropertyItem pi)
                return pi.GetData().ToObject();
            return null;
        }

        protected override DragAndDropVisualMode HandleDragAndDrop(DragAndDropArgs args)
        {
            return DragAndDropVisualMode.None;
        }

        private void HandleContextualMenu(Event evt, Rect rowRect, PropertyItem item)
        {
            if (evt.type != EventType.MouseDown || evt.button != 1 || !rowRect.Contains(evt.mousePosition))
                return;
            if (!m_TableView.OpenContextualMenu(evt, item.GetData()))
                return;
            evt.Use();
            GUIUtility.ExitGUI();
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var evt = Event.current;
            var item = (PropertyItem)args.item;

            for (int i = 0, end = args.GetNumVisibleColumns(); i < end; ++i)
                CellGUI(args.GetCellRect(i), item, args.GetColumn(i), args.item.id);

            HandleContextualMenu(evt, args.rowRect, item);
        }

        protected override void BeforeRowsGUI()
        {
            base.BeforeRowsGUI();
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            m_TableView.SetSelection(FindRows(selectedIds).Select(e => ((PropertyItem)e).GetData()));
        }

        protected override void DoubleClickedItem(int id)
        {
            m_TableView.DoubleClick(((PropertyItem)FindItem(id, rootItem)).GetData());
        }

        protected override void KeyEvent()
        {
            if (Event.current.type != EventType.KeyDown)
                return;

            if (Event.current.character == '\t')
            {
                GUI.FocusControl(Constants.focusHelper);
                Event.current.Use();
            }
        }

        private void SerializeState(string uid)
        {
            SessionState.SetString(uid + Constants.serializeTreeViewState, JsonUtility.ToJson(state));
            SessionState.SetString(uid + Constants.serializeColumnHeaderState, JsonUtility.ToJson(multiColumnHeader.state));
        }

        private void DeserializeState(string uid)
        {
            var headerState = new MultiColumnHeaderState(multiColumnHeader.state.columns);
            var columnHeaderState = SessionState.GetString(uid + Constants.serializeColumnHeaderState, "");

            if (!string.IsNullOrEmpty(columnHeaderState))
                JsonUtility.FromJsonOverwrite(columnHeaderState, headerState);

            if (MultiColumnHeaderState.CanOverwriteSerializedFields(headerState, multiColumnHeader.state))
                OverwriteSerializedFields(headerState, multiColumnHeader.state);

            var treeViewState = SessionState.GetString(uid + Constants.serializeTreeViewState, "");
            if (!string.IsNullOrEmpty(treeViewState))
                JsonUtility.FromJsonOverwrite(treeViewState, state);
        }

        private static void OverwriteSerializedFields(MultiColumnHeaderState source, MultiColumnHeaderState destination)
        {
            destination.visibleColumns = source.visibleColumns;
            destination.sortedColumns = source.sortedColumns;

            for (int i = 0; i < destination.columns.Length; ++i)
            {
                destination.columns[i].sortedAscending = source.columns[i].sortedAscending;

                if (!(((PropertyColumn)source.columns[i]).userDataObj is SearchColumn sourceColumn) ||
                    !(((PropertyColumn)destination.columns[i]).userDataObj is SearchColumn destColumn) ||
                    string.Equals(sourceColumn.name, destColumn.name))
                {
                    // Only makes sense if the property is the same property name
                    destination.columns[i].width = source.columns[i].width;
                }
            }
        }

        private void CellGUI(Rect cellRect, PropertyItem item, int columnIndex, int itemId)
        {
            CenterRectUsingSingleLineHeight(ref cellRect);
            var unclipRect = Utils.Unclip(cellRect);
            if (!unclipRect.Overlaps(m_ViewRect))
                return;

            var column = (SearchColumn)((PropertyColumn)multiColumnHeader.GetColumn(columnIndex)).userDataObj;
            if (column.drawer == null && column.getter == null)
                return;

            // allow to capture tabs
            if (multiColumnHeader.state.visibleColumns.Length > 1)
            {
                if (itemId == state.lastClickedID && HasFocus() && columnIndex == multiColumnHeader.state.visibleColumns[multiColumnHeader.state.visibleColumns[0] == 0 ? 1 : 0])
                    GUI.SetNextControlName(Constants.focusHelper);
            }

            bool isFocused = false;
            bool isSelected = IsSelected(itemId);
            var ltd = item.GetData();
            var eventArgs = new SearchColumnEventArgs(ltd, m_TableView.context, column)
            {
                rect = cellRect,
                focused = isFocused,
                selected = isSelected
            };

            if (column.getter != null)
                eventArgs.value = column.getter(eventArgs);

            if (eventArgs.value != null && column.drawer == null)
            {
                DefaultDrawing(cellRect, column, eventArgs.value);

                return;
            }

            if (column.drawer == null)
                return;

            EditorGUI.BeginChangeCheck();
            var newValue = column.drawer(eventArgs);

            if (EditorGUI.EndChangeCheck() && column.setter != null)
            {
                eventArgs.value = newValue;
                column.setter?.Invoke(eventArgs);

                var selIds = GetSelection();
                if (selIds.Contains(ltd.id.GetHashCode()))
                {
                    IList<TreeViewItem> rows = FindRows(selIds);

                    foreach (var r in rows)
                    {
                        if (r.id == itemId)
                            continue;

                        var data = ((PropertyItem)r).GetData();
                        column.setter?.Invoke(new SearchColumnEventArgs(data, m_TableView.context, column) { value = newValue, multiple = true });
                    }
                }
            }
        }

        internal static void DefaultDrawing(Rect cellRect, SearchColumn column, object value)
        {
            if (value is Texture2D tex)
                GUI.DrawTexture(cellRect, tex, ScaleMode.ScaleToFit);
            else
            {
                var itemStyle = ItemSelectors.GetItemContentStyle(column);
                GUI.Label(cellRect, value.ToString(), itemStyle);
            }
        }

        private void OnVisibleColumnChanged(MultiColumnHeader header)
        {
            Reload();
            m_TableView.SetDirty();
        }

        private void OnSortingChanged(MultiColumnHeader multiColumnHeader)
        {
            var rows = GetRows();
            Sort(rows, multiColumnHeader.sortedColumnIndex);
            m_TableView.SetDirty();
        }

        private void Sort(IList<TreeViewItem> rows, int sortIdx)
        {
            Debug.Assert(sortIdx >= 0);

            SearchColumn column = Col(sortIdx);
            if (column == null)
                return;

            var get = column.getter;
            if (get == null)
                return;

            var myRows = rows as List<TreeViewItem>;
            var sortAscending = multiColumnHeader.IsSortedAscending(sortIdx);
            var sortOrder = sortAscending ? 1 : -1;

            var comp = column.comparer;
            if (comp == null)
            {
                if (column.getter != null)
                {
                    if (rows.Count > 0)
                    {
                        myRows.Sort((lhs, rhs) =>
                        {
                            var lhsargs = new SearchColumnEventArgs(((PropertyItem)lhs).GetData(), m_TableView.context, column);
                            var rhsargs = new SearchColumnEventArgs(((PropertyItem)rhs).GetData(), m_TableView.context, column);
                            var lhsv = get(lhsargs);
                            if (lhsv is IComparable c)
                                return Comparer.Default.Compare(c, get(rhsargs)) * sortOrder;
                            return Comparer.Default.Compare(lhsv?.GetHashCode() ?? 0, get(rhsargs)?.GetHashCode() ?? 0) * sortOrder;
                        });
                    }
                }

                return;
            }

            myRows.Sort((lhs, rhs) =>
            {
                var lhsargs = new SearchColumnEventArgs(((PropertyItem)lhs).GetData(), m_TableView.context, column);
                var rhsargs = new SearchColumnEventArgs(((PropertyItem)rhs).GetData(), m_TableView.context, column);

                lhsargs.value = get(lhsargs);
                rhsargs.value = get(rhsargs);

                return comp(new SearchColumnCompareArgs(lhsargs, rhsargs, sortAscending)) * sortOrder;
            });
        }

        private SearchColumn Col(int idx)
        {
            return ((PropertyColumn)multiColumnHeader.state.columns[idx]).userDataObj as SearchColumn;
        }

        public void FrameColumn(int columnIndex)
        {
            m_FrameColumnIndex = multiColumnHeader.GetVisibleColumnIndex(Math.Max(0, columnIndex));
        }
    }
}

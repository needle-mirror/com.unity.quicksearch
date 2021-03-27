using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityEditor.Search
{
    [Flags]
    enum ShownPanels
    {
        None = 0,
        LeftSideBar = 1 << 0,
        InspectorPreview = 1 << 1
    }

    static class ShownPanelsExtensions
    {
        public static bool HasAny(this ShownPanels flags, ShownPanels f) => (flags & f) != 0;
        public static bool HasAll(this ShownPanels flags, ShownPanels all) => (flags & all) == all;
    }

    static class EventModifiersExtensions
    {
        public static bool HasAny(this EventModifiers flags, EventModifiers f) => (flags & f) != 0;
        public static bool HasAll(this EventModifiers flags, EventModifiers all) => (flags & all) == all;
    }

    [EditorWindowTitle(title = "Search")]
    class QuickSearch : EditorWindow, ISearchView, IDisposable, IHasCustomMenu
    {
        internal const string k_TogleSyncShortcutName = "Search/Toggle Sync Search View";

        internal enum SearchEventStatus
        {
            DoNotSendEvent,
            WaitForEvent,
            EventSent,
        }

        const int k_ResetSelectionIndex = -1;
        const string k_LastSearchPrefKey = "last_search";
        const string k_SideBarWidthKey = "Search.SidebarWidth";
        const string k_DetailsWidthKey = "Search.DetailsWidth";
        const float k_DetailsViewShowMinSize = 550f;
        const int k_MinimumGroupVisible = 1;
        private static readonly string k_CheckWindowKeyName = $"{typeof(QuickSearch).FullName}h";
        private static readonly string[] k_Dots = { ".", "..", "..." };

        private static EditorWindow s_FocusedWindow;
        private static SearchContext s_GlobalContext = null;

        // Selection state
        private GroupedSearchList m_FilteredItems;
        private readonly List<int> m_Selection = new List<int>();
        private int m_DelayedCurrentSelection = k_ResetSelectionIndex;
        private SearchSelection m_SearchItemSelection;
        private string m_LastSelectedMoreGroup;

        private bool m_Disposed = false;
        private DetailView m_DetailView;
        private IResultView m_ResultView;
        private float m_PreviousItemSize = -1;
        private RefreshFlags m_DebounceRefreshFlags;
        private Action m_DebounceOff = null;
        private bool m_SyncSearch;

        [SerializeField] private EditorWindow m_LastFocusedWindow;
        [SerializeField] private bool m_SearchBoxFocus;
        [SerializeField] private float m_ItemSize = 1;
        [SerializeField] private Vector3 m_WindowSize;
        [SerializeField] private SplitterInfo m_SideBarSplitter;
        [SerializeField] private SplitterInfo m_DetailsPanelSplitter;
        [SerializeField] private string[] m_ProviderIds;
        [SerializeField] private string m_WindowId;
        [SerializeField] private ShownPanels m_ShownPanels = ShownPanels.None;
        [SerializeField] private Vector2 m_SideBarScrollPosition;
        [SerializeField] private int m_ContextHash;
        [SerializeField] internal SearchEventStatus searchEventStatus;
        [SerializeField] private string searchTopic;
        [SerializeField] private bool m_Multiselect;
        [SerializeField] internal SearchQuery activeSearchQuery;

        internal event Action nextFrame;
        internal event Action<Vector2, Vector2> resized;

        public Action<SearchItem, bool> selectCallback { get; set; }
        public Func<SearchItem, bool> filterCallback { get; set; }
        public Action<SearchItem> trackingCallback { get; set; }

        internal bool searchInProgress => (context?.searchInProgress ?? false) || nextFrame != null || m_DebounceOff != null;
        internal string currentGroup => m_FilteredItems.currentGroup;

        public SearchSelection selection
        {
            get
            {
                if (m_SearchItemSelection == null)
                    m_SearchItemSelection = new SearchSelection(m_Selection, m_FilteredItems);
                return m_SearchItemSelection;
            }
        }

        public SearchContext context { get; private set; }
        public ISearchList results => m_FilteredItems;
        public DisplayMode displayMode
        {
            get
            {
                if (m_ItemSize <= (int)DisplayMode.List)
                    return DisplayMode.List;

                #if USE_SEARCH_MODULE
                if (m_ItemSize >= (int)DisplayMode.Table)
                    return DisplayMode.Table;
                #endif

                return DisplayMode.Grid;
            }
        }
        public float itemIconSize { get => m_ItemSize; set => UpdateItemSize(value); }
        public bool multiselect { get => m_Multiselect; set => m_Multiselect = value; }

        public bool syncSearch
        {
            get => m_SyncSearch;
            set
            {
                if (value == m_SyncSearch)
                    return;

                m_SyncSearch = value;
                #if USE_SEARCH_MODULE
                if (value)
                    NotifySyncSearch(m_FilteredItems.currentGroup, UnityEditor.SearchService.SearchService.SyncSearchEvent.StartSession);
                else
                    NotifySyncSearch(m_FilteredItems.currentGroup, UnityEditor.SearchService.SearchService.SyncSearchEvent.EndSession);
                #endif
            }
        }

        internal string windowId => m_WindowId;
        internal IResultView resultView => m_ResultView;

        private string searchTopicPlaceHolder => $"Search {searchTopic}";

        public void SetSearchText(string searchText, TextCursorPlacement moveCursor = TextCursorPlacement.Default)
        {
            SetSearchText(searchText, moveCursor, -1);
        }

        public void SetSearchText(string searchText, TextCursorPlacement moveCursor, int cursorInsertPosition)
        {
            if (context == null)
                return;
            context.searchText = searchText ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(context.searchText))
                SearchField.UpdateLastSearchText(context.searchText);
            RefreshSearch();
            SetTextEditorState(searchText, te => SearchField.MoveCursor(moveCursor, cursorInsertPosition));
        }

        private void SetTextEditorState(string searchText, Action<TextEditor> handler, bool selectAll = false)
        {
            nextFrame += () =>
            {
                var te = SearchField.GetTextEditor();
                if (searchText != null)
                    te.text = searchText;
                if (selectAll)
                    te.SelectAll();
                handler?.Invoke(te);
            };
            Repaint();
        }

        public void Refresh(RefreshFlags flags = RefreshFlags.Default)
        {
            m_DebounceRefreshFlags |= flags;
            DebounceRefresh();
        }

        private void RefreshSearch()
        {
            m_DebounceOff?.Invoke();
            m_DebounceOff = null;

            if (context == null)
                return;

            ClearCurrentErrors();

            SearchSettings.ApplyContextOptions(context);
            var foundItems = SearchService.GetItems(context);
            if (selectCallback != null)
                foundItems.Add(SearchItem.none);

            SetItems(filterCallback == null ? foundItems : foundItems.Where(item => filterCallback(item)));

            #if USE_SEARCH_MODULE
            if (syncSearch)
                NotifySyncSearch(m_FilteredItems.currentGroup, UnityEditor.SearchService.SearchService.SyncSearchEvent.SyncSearch);
            #endif

            Utils.tick += UpdateAsyncResults;
        }

        public static QuickSearch Create(SearchFlags flags = SearchFlags.OpenDefault)
        {
            return Create(null, topic: "Unity", flags);
        }

        public static QuickSearch Create(SearchContext context, string topic = "Unity", SearchFlags flags = SearchFlags.OpenDefault)
        {
            s_GlobalContext = context ?? new SearchContext(SearchService.Providers.Where(p => p.active));
            s_FocusedWindow = focusedWindow;

            if (s_GlobalContext != null)
                s_GlobalContext.options |= flags;

            QuickSearch qsWindow;
            if (flags.HasAny(SearchFlags.ReuseExistingWindow) && HasOpenInstances<QuickSearch>())
            {
                qsWindow = GetWindow<QuickSearch>(false, null, false);
                if (context != null)
                {
                    qsWindow.SetContext(context);
                    qsWindow.RefreshSearch();
                }
            }
            else
            {
                qsWindow = CreateInstance<QuickSearch>();
                qsWindow.m_WindowId = GUID.Generate().ToString();
            }

            qsWindow.multiselect = flags.HasAny(SearchFlags.Multiselect);
            qsWindow.searchTopic = topic;

            // Ensure we won't send events while doing a domain reload.
            qsWindow.searchEventStatus = SearchEventStatus.WaitForEvent;
            return qsWindow;
        }

        private void SetContext(SearchContext newContext)
        {
            if (newContext == null || newContext == context)
                return;
            context?.Dispose();
            context = newContext;

            context.searchView = this;
            context.focusedWindow = m_LastFocusedWindow;
            context.asyncItemReceived -= OnAsyncItemsReceived;
            context.asyncItemReceived += OnAsyncItemsReceived;

            m_ProviderIds = context.GetProviders().Select(p => p.id).ToArray();

            m_FilteredItems?.Dispose();
            m_FilteredItems = new GroupedSearchList(context);

            LoadContext();
        }

        public static QuickSearch Open(float defaultWidth = 950, float defaultHeight = 539, SearchFlags flags = SearchFlags.OpenDefault)
        {
            return Create(flags).ShowWindow(defaultWidth, defaultHeight, flags);
        }

        public static QuickSearch OpenWithContextualProvider(params string[] providerIds)
        {
            return OpenWithContextualProvider(null, providerIds, SearchFlags.OpenContextual);
        }

        internal static QuickSearch OpenWithContextualProvider(string searchQuery, string[] providerIds, SearchFlags flags, string topic = null)
        {
            var providers = providerIds.Select(id => SearchService.Providers.Find(p => p.id == id)).Where(p => p != null).ToArray();
            if (providers.Length != providerIds.Length)
                Debug.LogWarning($"Cannot find one of these search providers {string.Join(", ", providerIds)}");

            if (providers.Length == 0)
                return OpenDefaultQuickSearch();

            var context = SearchService.CreateContext(providers, searchQuery);
            topic = topic ?? string.Join(", ", providers.Select(p => p.name.ToLower()));
            var qsWindow = Create(context, topic, flags);

            var evt = SearchAnalytics.GenericEvent.Create(qsWindow.m_WindowId, SearchAnalytics.GenericEventType.QuickSearchOpen, "Contextual");
            evt.message = providers[0].id;
            if (providers.Length > 1)
                evt.description = providers[1].id;
            if (providers.Length > 2)
                evt.description = providers[2].id;
            if (providers.Length > 3)
                evt.stringPayload1 = providers[3].id;
            if (providers.Length > 4)
                evt.stringPayload1 = providers[4].id;

            SearchAnalytics.SendEvent(evt);

            return qsWindow.ShowWindow();
        }

        public QuickSearch ShowWindow(float defaultWidth = 950, float defaultHeight = 538, SearchFlags flags = SearchFlags.OpenDefault)
        {
            var windowSize = new Vector2(defaultWidth, defaultHeight);
            if (flags.HasAny(SearchFlags.Dockable))
            {
                bool firstOpen = !EditorPrefs.HasKey(k_CheckWindowKeyName);
                if (firstOpen)
                {
                    var centeredPosition = Utils.GetMainWindowCenteredPosition(windowSize);
                    position = centeredPosition;
                    Utils.CallDelayed(() => position = centeredPosition);
                }
                Show(true);
            }
            else
            {
                this.ShowDropDown(windowSize);
            }
            Focus();
            return this;
        }

        public static QuickSearch ShowPicker(
            SearchContext context,
            Action<SearchItem, bool> selectHandler,
            Action<SearchItem> trackingHandler = null,
            Func<SearchItem, bool> filterHandler = null,
            IEnumerable<SearchItem> subset = null,
            string title = "item", float itemSize = 64f,
            float defaultWidth = 850, float defaultHeight = 539, SearchFlags flags = SearchFlags.OpenPicker)
        {
            if (selectHandler == null)
                return null;

            var qs = Create(context, flags: flags);
            qs.searchTopic = title;
            qs.searchEventStatus = SearchEventStatus.WaitForEvent;
            qs.titleContent.text = $"Select {title}...";
            qs.itemIconSize = itemSize;
            qs.multiselect = false;
            qs.selectCallback = selectHandler;
            qs.trackingCallback = trackingHandler;
            qs.filterCallback = filterHandler;
            qs.context.wantsMore = true;

            if (subset != null)
                qs.context.subset = subset.ToList();

            qs.Refresh(RefreshFlags.StructureChanged);
            if (flags.HasAny(SearchFlags.Dockable))
                qs.Show();
            else
                qs.ShowAuxWindow();
            qs.position = Utils.GetMainWindowCenteredPosition(new Vector2(defaultWidth, defaultHeight));
            qs.Focus();

            return qs;
        }

        public static QuickSearch ShowObjectPicker(
            Action<UnityEngine.Object, bool> selectHandler,
            Action<UnityEngine.Object> trackingHandler,
            string searchText, string typeName, Type filterType,
            float defaultWidth = 850, float defaultHeight = 539, SearchFlags flags = SearchFlags.OpenPicker)
        {
            if (selectHandler == null || typeName == null)
                return null;

            if (filterType == null)
                filterType = TypeCache.GetTypesDerivedFrom<UnityEngine.Object>()
                    .FirstOrDefault(t => t.Name == typeName) ?? typeof(UnityEngine.Object);

            var qs = Create();
            qs.searchTopic = "object";
            qs.searchEventStatus = SearchEventStatus.WaitForEvent;
            qs.titleContent.text = $"Select {filterType?.Name ?? typeName}...";
            qs.itemIconSize = 64;
            qs.multiselect = false;
            qs.filterCallback = (item) => item == SearchItem.none || (IsObjectMatchingType(item ?? SearchItem.none, filterType));
            qs.selectCallback = (item, canceled) => selectHandler?.Invoke(Utils.ToObject(item, filterType), canceled);
            qs.trackingCallback = (item) => trackingHandler?.Invoke(Utils.ToObject(item, filterType));
            qs.context.wantsMore = true;
            qs.context.filterType = filterType;
            qs.SetSearchText(searchText, TextCursorPlacement.MoveToStartOfNextWord);

            if (flags.HasAny(SearchFlags.Dockable))
                qs.Show();
            else
                qs.ShowAuxWindow();

            qs.position = Utils.GetMainWindowCenteredPosition(new Vector2(defaultWidth, defaultHeight));
            qs.Focus();

            return qs;
        }

        public void SetSelection(params int[] selection)
        {
            SetSelection(true, selection);
        }

        private void SetSelection(bool trackSelection, int[] selection)
        {
            if (!multiselect && selection.Length > 1)
                throw new Exception("Multi selection is not allowed.");

            var lastIndexAdded = k_ResetSelectionIndex;

            m_Selection.Clear();
            m_SearchItemSelection = null;
            foreach (var idx in selection)
            {
                if (!IsItemValid(idx))
                    continue;

                m_Selection.Add(idx);
                lastIndexAdded = idx;
            }

            if (lastIndexAdded != k_ResetSelectionIndex)
            {
                m_SearchItemSelection = null;
                if (trackSelection)
                    TrackSelection(lastIndexAdded);
            }
        }

        public void AddSelection(params int[] selection)
        {
            if (!multiselect && m_Selection.Count == 1)
                throw new Exception("Multi selection is not allowed.");

            var lastIndexAdded = k_ResetSelectionIndex;

            foreach (var idx in selection)
            {
                if (!IsItemValid(idx))
                    continue;

                if (m_Selection.Contains(idx))
                {
                    m_Selection.Remove(idx);
                }
                else
                {
                    m_Selection.Add(idx);
                    lastIndexAdded = idx;
                }
            }

            if (lastIndexAdded != k_ResetSelectionIndex)
            {
                m_SearchItemSelection = null;
                TrackSelection(lastIndexAdded);
            }
        }

        public void ExecuteSearchQuery(SearchQuery query)
        {
            SetSelection();

            var queryContext = SearchService.CreateContext(query.providerIds, query.text, context?.options ?? SearchFlags.Default);
            SetContext(queryContext);

            if (query.viewState != null && query.viewState.isValid)
                SetViewState(query.viewState);

            RefreshSearch();
            SetTextEditorState(queryContext.searchText, null, selectAll: true);
            SearchQuery.AddToRecentSearch(query);

            activeSearchQuery = query;
        }

        public void ExecuteSearchQuery(SearchQuery query, SearchAnalytics.GenericEventType eventType)
        {
            ExecuteSearchQuery(query);
            SendEvent(eventType, query.text);
        }

        internal void SetViewState(ResultViewState viewState)
        {
            itemIconSize = viewState.itemSize;
            m_ResultView.SetViewState(viewState);
            if (!string.IsNullOrEmpty(viewState.group))
                SelectGroup(viewState.group);
        }

        public void ExecuteAction(SearchAction action, SearchItem[] items, bool endSearch = true)
        {
            var item = items.LastOrDefault();
            if (item == null)
                return;

            SendSearchEvent(item, action);
            EditorApplication.delayCall -= DelayTrackSelection;

            if (selectCallback != null)
            {
                selectCallback(item, false);
                selectCallback = null;
            }
            else
            {
                SearchField.UpdateLastSearchText(context.searchText);

                if (action.handler != null && items.Length == 1)
                    action.handler(item);
                else if (action.execute != null)
                    action.execute(items);
                else
                    action.handler?.Invoke(item);
            }

            if (endSearch && action.closeWindowAfterExecution && !docked)
                CloseSearchWindow();
        }

        public void ShowItemContextualMenu(SearchItem item, Rect position)
        {
            SendEvent(SearchAnalytics.GenericEventType.QuickSearchShowActionMenu, item.provider.id);
            var menu = new GenericMenu();
            var shortcutIndex = 0;
            var currentSelection = new[] { item };
            foreach (var action in item.provider.actions.Where(a => a.enabled(currentSelection)))
            {
                var itemName = !string.IsNullOrWhiteSpace(action.content.text) ? action.content.text : action.content.tooltip;
                if (shortcutIndex == 0)
                    itemName += " _enter";
                else if (shortcutIndex == 1)
                    itemName += " _&enter";
                else if (shortcutIndex == 2)
                    itemName += " _&%enter";
                else if (shortcutIndex == 3)
                    itemName += " _&%#enter";
                menu.AddItem(new GUIContent(itemName, action.content.image), false, () => ExecuteAction(action, currentSelection, false));
                ++shortcutIndex;
            }

            if (position == default)
                menu.ShowAsContext();
            else
                menu.DropDown(position);
        }

        internal void OnEnable()
        {
            hideFlags |= HideFlags.DontSaveInEditor;
            m_LastFocusedWindow = m_LastFocusedWindow ?? s_FocusedWindow;
            wantsLessLayoutEvents = true;

            InitializeSplitters();
            if (activeSearchQuery)
                ExecuteSearchQuery(activeSearchQuery);
            else
            {
                SetContext(CreateSearchViewContext());
                LoadSessionSettings();
            }

            SearchSettings.SortActionsPriority();

            // Create search view state objects
            m_DetailView = new DetailView(this);

            resized += OnWindowResized;

            SelectSearch();
            UpdateWindowTitle();
        }

        private void InitializeSplitters()
        {
            m_SideBarSplitter = m_SideBarSplitter ?? new SplitterInfo(SplitterInfo.Side.Left, 0.15f, 0.25f, this);
            m_SideBarSplitter.host = this;

            m_DetailsPanelSplitter = m_DetailsPanelSplitter ?? new SplitterInfo(SplitterInfo.Side.Right, 0.5f, 0.80f, this);
            m_DetailsPanelSplitter.host = this;

            Utils.CallDelayed(() =>
            {
                if (m_SideBarSplitter.pos <= 0f)
                    m_SideBarSplitter.SetPosition(EditorPrefs.GetFloat(k_SideBarWidthKey, -1f));
                if (m_DetailsPanelSplitter.pos <= 0f)
                    m_DetailsPanelSplitter.SetPosition(EditorPrefs.GetFloat(k_DetailsWidthKey, -1f));
            });
        }

        private SearchContext CreateSearchViewContext()
        {
            // Create search view context
            if (s_GlobalContext == null)
            {
                if (m_ProviderIds == null || m_ProviderIds.Length == 0)
                    return new SearchContext(SearchService.Providers.Where(p => p.active));
                else
                    return new SearchContext(m_ProviderIds.Select(id => SearchService.GetProvider(id)).Where(p => p != null));
            }
            else
            {
                var tempContext = s_GlobalContext;
                s_GlobalContext = null;
                return tempContext;
            }
        }

        internal void OnDisable()
        {
            s_FocusedWindow = null;
            AutoComplete.Clear();

            syncSearch = false;

            resized = null;
            nextFrame = null;
            Utils.tick -= UpdateAsyncResults;
            EditorApplication.delayCall -= DelayTrackSelection;

            selectCallback?.Invoke(null, true);

            SaveSessionSettings();

            m_DetailView?.Dispose();
            m_ResultView?.Dispose();

            // End search session
            context.asyncItemReceived -= OnAsyncItemsReceived;
            context.Dispose();
            context = null;
        }

        internal void OnGUI()
        {
            if (context == null)
                return;

            var evt = Event.current;
            var eventType = evt.rawType;
            if (eventType == EventType.Repaint)
            {
                var newWindowSize = position.size;
                if (!newWindowSize.Equals(m_WindowSize))
                {
                    if (m_WindowSize.x > 0)
                        resized?.Invoke(m_WindowSize, newWindowSize);
                    m_WindowSize = newWindowSize;
                }

                nextFrame?.Invoke();
                nextFrame = null;
            }

            HandleKeyboardNavigation(evt);
            if (context == null)
                return;

            using (new EditorGUILayout.VerticalScope(GUIStyle.none))
            {
                UpdateFocusControlState(evt);
                DrawToolbar(evt);
                DrawPanels(evt);
                DrawStatusBar();
                AutoComplete.Draw(context, this);
            }
        }

        #if USE_SEARCH_MODULE
        private void NotifySyncSearch(string groupId, UnityEditor.SearchService.SearchService.SyncSearchEvent evt)
        {
            var syncViewId = groupId;
            switch (groupId)
            {
                case "asset":
                    syncViewId = typeof(ProjectSearchEngine).FullName;
                    break;
                case "scene":
                    syncViewId = typeof(SceneSearchEngine).FullName;
                    break;
            }
            UnityEditor.SearchService.SearchService.NotifySyncSearchChanged(evt, syncViewId, context.searchText);
        }

        #endif

        private void DrawPanels(Event evt)
        {
            using (var s = new EditorGUILayout.HorizontalScope())
            {
                var shrinkedView = position.width <= k_DetailsViewShowMinSize || selectCallback != null;
                var showSideBar = !shrinkedView && m_ShownPanels.HasAny(ShownPanels.LeftSideBar);
                var showDetails = !shrinkedView && m_ShownPanels.HasAny(ShownPanels.InspectorPreview);

                var windowWidth = position.width;
                var resultViewSize = windowWidth;

                if (showSideBar)
                {
                    DrawSideBar(evt, s.rect);
                    resultViewSize -= m_SideBarSplitter.width + 1f;
                }

                if (showDetails)
                {
                    m_DetailsPanelSplitter.Init(windowWidth - 250f);
                    m_DetailsPanelSplitter.Draw(evt, s.rect);

                    resultViewSize -= m_DetailsPanelSplitter.width - 1;
                }

                DrawItems(evt, Mathf.Ceil(resultViewSize));

                if (showDetails)
                {
                    m_DetailView.Draw(context, m_DetailsPanelSplitter.width);
                }
            }
        }

        private void DrawSideBar(Event evt, Rect areaRect)
        {
            m_SideBarSplitter.Init(180f);
            m_SideBarSplitter.Draw(evt, areaRect);

            m_SideBarScrollPosition = Utils.BeginPanelView(m_SideBarScrollPosition, Styles.panelBackgroundLeft);
            m_ResultView?.DrawControlLayout(m_SideBarSplitter.width);
            DrawSavedSearches(evt);
            Utils.EndPanelView();
        }

        private void DrawSavedSearches(Event evt)
        {
            var maxWidth = GUILayout.MaxWidth(m_SideBarSplitter.width - 1);

            GUILayout.Label("Saved Searches", Styles.panelHeader, maxWidth);
            EditorGUI.BeginChangeCheck();
            var sortOrder = EditorGUILayout.EnumPopup(SearchSettings.savedSearchesSortOrder, Styles.sidebarDropdown);
            if (EditorGUI.EndChangeCheck())
            {
                SearchSettings.savedSearchesSortOrder = (SearchQuerySortOrder)sortOrder;
                SearchSettings.Save();
                SearchQuery.SortQueries();
                SendEvent(SearchAnalytics.GenericEventType.QuickSearchSavedSearchesSorted, SearchSettings.savedSearchesSortOrder.ToString());
            }

            Rect searchQueryButtonRect = Rect.zero;
            foreach (var query in EnumerateSearchQueries())
            {
                var isSelected = query == activeSearchQuery;
                var itemStyle = isSelected ? Styles.savedSearchItemSelected : Styles.savedSearchItem;
                var itemContent = new GUIContent(query.displayName, query.icon, query.tooltip);
                if (EditorGUILayout.DropdownButton(itemContent, FocusType.Keyboard, itemStyle, maxWidth))
                {
                    if (isSelected)
                        activeSearchQuery = null;
                    else
                        ExecuteSearchQuery(query, SearchAnalytics.GenericEventType.QuickSearchSavedSearchesExecuted);
                    GUIUtility.ExitGUI();
                }
                searchQueryButtonRect = GUILayoutUtility.GetLastRect();
                EditorGUIUtility.AddCursorRect(searchQueryButtonRect, MouseCursor.Link);
                if (evt.type == EventType.MouseUp && evt.button == 1 && searchQueryButtonRect.Contains(evt.mousePosition))
                {
                    ShowSavedSearchQueryContextualMenu(query);
                    GUIUtility.ExitGUI();
                }
            }

            GUILayout.Space(10);

            if (searchQueryButtonRect != Rect.zero && evt.mousePosition.y > searchQueryButtonRect.yMax && evt.type == EventType.MouseDown)
            {
                activeSearchQuery = null;
                Repaint();
            }
        }

        private IEnumerable<SearchQuery> EnumerateSearchQueries()
        {
            if (SearchSettings.savedSearchesSortOrder == SearchQuerySortOrder.RecentlyUsed)
                return SearchQuery.savedQueries.Take(11);
            return SearchQuery.savedQueries;
        }

        private void ShowSavedSearchQueryContextualMenu(SearchQuery query)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Open"), false, () => ExecuteSearchQuery(query, SearchAnalytics.GenericEventType.QuickSearchSavedSearchesExecuted));
            menu.AddItem(new GUIContent("Edit"), false, () => Selection.activeObject = query);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete"), false, () => DeleteSavedSearchQuery(query));

            menu.ShowAsContext();
        }

        private void DeleteSavedSearchQuery(SearchQuery query)
        {
            if (!EditorUtility.DisplayDialog($"Deleting search query {query.name}?",
                $"You are about to delete the search query {query.name}, are you sure?", "Yes", "No"))
                return;
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(query));
        }

        internal void OnLostFocus()
        {
            AutoComplete.Clear();
        }

        internal void Update()
        {
            if (context == null || focusedWindow != this)
                return;

            if (context.options.HasAny(SearchFlags.Debug))
                return;

            var time = EditorApplication.timeSinceStartup;
            var repaintRequested = hasFocus && SearchField.UpdateBlinkCursorState(time);
            if (repaintRequested)
                Repaint();
        }

        private void SetItems(IEnumerable<SearchItem> items)
        {
            m_SearchItemSelection = null;
            m_FilteredItems.Clear();
            m_FilteredItems.AddItems(items);
            SetSelection(trackSelection: false, m_Selection.ToArray());
        }

        private void RefreshViews(RefreshFlags additionalFlags = RefreshFlags.None)
        {
            UpdateWindowTitle();

            m_ResultView?.Refresh(m_DebounceRefreshFlags | additionalFlags);
            m_DetailView?.Refresh(m_DebounceRefreshFlags | additionalFlags);
            m_DebounceRefreshFlags = RefreshFlags.None;

            Repaint();
        }

        private void OnAsyncItemsReceived(SearchContext context, IEnumerable<SearchItem> items)
        {
            var filteredItems = items;
            if (filterCallback != null)
                filteredItems = filteredItems.Where(item => filterCallback(item));
            m_FilteredItems.AddItems(filteredItems);
            Utils.tick += UpdateAsyncResults;
        }

        private void UpdateAsyncResults()
        {
            Utils.tick -= UpdateAsyncResults;
            RefreshViews(RefreshFlags.ItemsChanged);
        }

        private bool ToggleFilter(string providerId)
        {
            var enabled = context.IsEnabled(providerId);
            var provider = SearchService.GetProvider(providerId);
            if (provider != null)
                SearchService.SetActive(providerId, !enabled);
            if (providerId == m_FilteredItems.currentGroup)
                SelectGroup(null);
            context.SetFilter(providerId, !enabled);
            SendEvent(SearchAnalytics.GenericEventType.FilterWindowToggle, providerId, context.IsEnabled(providerId).ToString());
            Refresh();
            return !enabled;
        }

        private void TogglePanelView(ShownPanels panelOption)
        {
            var hasOptions = !m_ShownPanels.HasAny(panelOption);
            SendEvent(SearchAnalytics.GenericEventType.QuickSearchOpenToggleToggleSidePanel, panelOption.ToString(), hasOptions.ToString());
            if (hasOptions)
                m_ShownPanels |= panelOption;
            else
                m_ShownPanels &= ~panelOption;

            if (panelOption == ShownPanels.LeftSideBar)
            {
                SearchSettings.showSavedSearchPanel = hasOptions;
                SearchSettings.Save();
            }
        }

        public void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(new GUIContent("Preferences"), false, () => OpenPreferences());

            #if UNITY_EDITOR_WIN
            var savedSearchContent = new GUIContent("Side Bar\tF3");
            var previewInspectorContent = new GUIContent("Preview Inspector\tF4");
            #else
            var savedSearchContent = new GUIContent("Side Bar");
            var previewInspectorContent = new GUIContent("Preview Inspector");
            #endif

            menu.AddItem(savedSearchContent, m_ShownPanels.HasAny(ShownPanels.LeftSideBar), () => TogglePanelView(ShownPanels.LeftSideBar));
            menu.AddItem(previewInspectorContent, m_ShownPanels.HasAny(ShownPanels.InspectorPreview), () => TogglePanelView(ShownPanels.InspectorPreview));
            menu.AddItem(new GUIContent("Options/Keep Open"), SearchSettings.keepOpen, () => ToggleKeepOpen());
            menu.AddItem(new GUIContent("Options/Show Status"), SearchSettings.showStatusBar, () => ToggleShowStatusBar());
            menu.AddItem(new GUIContent("Options/Show more results"), context?.wantsMore ?? false, () => ToggleWantsMore());
            if (Utils.isDeveloperBuild)
            {
                menu.AddItem(new GUIContent("Options/Debug"), context?.options.HasAny(SearchFlags.Debug) ?? false, () => ToggleDebugQuery());
            }
        }

        private void ToggleShowStatusBar()
        {
            SearchSettings.showStatusBar = !SearchSettings.showStatusBar;
            SendEvent(SearchAnalytics.GenericEventType.PreferenceChanged, nameof(SearchSettings.showStatusBar), SearchSettings.showStatusBar.ToString());
        }

        private void ToggleKeepOpen()
        {
            SearchSettings.keepOpen = !SearchSettings.keepOpen;
            SendEvent(SearchAnalytics.GenericEventType.PreferenceChanged, nameof(SearchSettings.keepOpen), SearchSettings.keepOpen.ToString());
        }

        private void ToggleWantsMore()
        {
            SearchSettings.wantsMore = context.wantsMore = !context?.wantsMore ?? false;
            SendEvent(SearchAnalytics.GenericEventType.PreferenceChanged, nameof(context.wantsMore), context.wantsMore.ToString());
            Refresh();
        }

        private void ToggleDebugQuery()
        {
            if (context.options.HasAny(SearchFlags.Debug))
                context.options &= ~SearchFlags.Debug;
            else
                context.options |= SearchFlags.Debug;
            Refresh();
        }

        private void SendSearchEvent(SearchItem item, SearchAction action = null)
        {
            if (searchEventStatus == SearchEventStatus.DoNotSendEvent)
                return;

            var evt = new SearchAnalytics.SearchEvent();
            if (item != null)
                evt.Success(item, action);

            if (evt.success)
            {
                evt.Done();
                searchEventStatus = SearchEventStatus.EventSent;
            }
            evt.searchText = context.searchText;
            SearchAnalytics.SendSearchEvent(evt, context);
        }

        private void UpdateWindowTitle()
        {
            titleContent.image = activeSearchQuery?.icon ?? Icons.quickSearchWindow;
            if (!titleContent.image)
                titleContent.image = Icons.quickSearchWindow;

            if (context == null)
                return;

            if (context.options.HasAll(SearchFlags.OpenPicker))
                return;

            if (m_FilteredItems.Count == 0)
                titleContent.text = $"Search";
            else
                titleContent.text = $"Search ({m_FilteredItems.Count - (selectCallback != null ? 1 : 0)})";
        }

        private static string FormatStatusMessage(SearchContext context, int totalCount)
        {
            var providers = context.providers.ToList();
            if (providers.Count == 0)
                return "There is no activated search provider";

            var msg = "Searching ";
            if (providers.Count > 1)
                msg += Utils.FormatProviderList(providers.Where(p => !p.isExplicitProvider), showFetchTime: !context.searchInProgress);
            else
                msg += Utils.FormatProviderList(providers);

            if (totalCount > 0)
            {
                msg += $" and found <b>{totalCount}</b> result";
                if (totalCount > 1)
                    msg += "s";
                if (!context.searchInProgress)
                {
                    if (context.searchElapsedTime > 1.0)
                        msg += $" in {PrintTime(context.searchElapsedTime)}";
                }
                else
                    msg += " so far";
            }
            else if (!string.IsNullOrEmpty(context.searchQuery))
            {
                if (!context.searchInProgress)
                    msg += " and found nothing";
            }

            if (context.searchInProgress)
                msg += k_Dots[(int)EditorApplication.timeSinceStartup % k_Dots.Length];

            return msg;
        }

        private static string PrintTime(double timeMs)
        {
            if (timeMs >= 1000)
                return $"{Math.Round(timeMs / 1000.0)} seconds";
            return $"{Math.Round(timeMs)} ms";
        }

        private void DrawStatusBar()
        {
            using (new GUILayout.HorizontalScope(Styles.statusBarBackground))
            {
                var alwaysPrintError = m_FilteredItems.currentGroup == null || !string.IsNullOrEmpty(context.filterId) || m_FilteredItems.TotalCount == 0;
                if (context.GetAllErrors().FirstOrDefault(e => alwaysPrintError || e.provider.id == m_FilteredItems.currentGroup) is SearchQueryError err)
                {
                    var errStyle = err.type == SearchQueryErrorType.Error ? Styles.statusError : Styles.statusWarning;
                    EditorGUILayout.LabelField(Utils.GUIContentTemp(err.reason, err.reason + '\n'), errStyle, GUILayout.ExpandWidth(true));
                }
                else if (SearchSettings.showStatusBar)
                {
                    var status = FormatStatusMessage(context, m_FilteredItems?.TotalCount ?? 0);
                    EditorGUILayout.LabelField(Utils.GUIContentTemp(status, status + '\n'), Styles.statusLabel, GUILayout.ExpandWidth(true));
                }
                else
                {
                    GUILayout.FlexibleSpace();
                }

                EditorGUI.BeginChangeCheck();
                var newItemIconSize = itemIconSize;
                if (itemIconSize <= (int)DisplayMode.Limit)
                {
                    var sliderRect = EditorGUILayout.GetControlRect(false, Styles.statusBarBackground.fixedHeight, GUILayout.Width(55f));
                    sliderRect.y -= 1f;
                    newItemIconSize = GUI.HorizontalSlider(sliderRect, newItemIconSize, 0f, (float)DisplayMode.Limit);
                }

                #if USE_SEARCH_MODULE
                var isList = displayMode == DisplayMode.List;
                if (GUILayout.Toggle(isList, Styles.listModeContent, Styles.statusBarButton) != isList)
                {
                    newItemIconSize = (float)DisplayMode.List;
                    SendEvent(SearchAnalytics.GenericEventType.QuickSearchSizeRadioButton, DisplayMode.List.ToString());
                }
                var isGrid = displayMode == DisplayMode.Grid;
                if (GUILayout.Toggle(isGrid, Styles.gridModeContent, Styles.statusBarButton) != isGrid)
                {
                    newItemIconSize = (float)DisplayMode.Grid;
                    SendEvent(SearchAnalytics.GenericEventType.QuickSearchSizeRadioButton, DisplayMode.Grid.ToString());
                }
                var isTable = displayMode == DisplayMode.Table;
                if (GUILayout.Toggle(isTable, Styles.tableModeContent, Styles.statusBarButton) != isTable)
                {
                    newItemIconSize = (float)DisplayMode.Table;
                    SendEvent(SearchAnalytics.GenericEventType.QuickSearchSizeRadioButton, DisplayMode.Table.ToString());
                }
                #endif

                if (EditorGUI.EndChangeCheck())
                {
                    newItemIconSize = Mathf.Round(newItemIconSize);
                    itemIconSize = newItemIconSize;
                    SearchSettings.itemIconSize = newItemIconSize;
                    m_ResultView.focusSelectedItem = true;
                }

                var hasProgress = context.searchInProgress;
                if (hasProgress)
                {
                    var searchInProgressRect = EditorGUILayout.GetControlRect(false,
                        Styles.searchInProgressButton.fixedHeight, Styles.searchInProgressButton, Styles.searchInProgressLayoutOptions);

                    int frame = (int)Mathf.Repeat(Time.realtimeSinceStartup * 5, 11.99f);
                    if (GUI.Button(searchInProgressRect, Styles.statusWheel[frame], Styles.searchInProgressButton))
                    {
                        OpenPreferences();
                        GUIUtility.ExitGUI();
                    }
                }
                else
                {
                    if (GUILayout.Button(Styles.prefButtonContent, Styles.statusBarButton))
                    {
                        OpenPreferences();
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private void OpenPreferences()
        {
            SettingsService.OpenUserPreferences(SearchSettings.settingsPreferencesKey);
            SendEvent(SearchAnalytics.GenericEventType.QuickSearchOpenPreferences);
        }

        private bool IsItemValid(int index)
        {
            if (index < 0 || index >= m_FilteredItems.Count)
                return false;
            return true;
        }

        private void DelayTrackSelection()
        {
            if (m_FilteredItems.Count == 0)
                return;

            if (!IsItemValid(m_DelayedCurrentSelection))
                return;

            var selectedItem = m_FilteredItems[m_DelayedCurrentSelection];
            if (trackingCallback == null)
                selectedItem?.provider?.trackSelection?.Invoke(selectedItem, context);
            else
                trackingCallback(selectedItem);

            m_DelayedCurrentSelection = k_ResetSelectionIndex;
        }

        private void TrackSelection(int currentSelection)
        {
            if (!SearchSettings.trackSelection)
                return;

            m_DelayedCurrentSelection = currentSelection;
            EditorApplication.delayCall -= DelayTrackSelection;
            EditorApplication.delayCall += DelayTrackSelection;
        }

        private void UpdateFocusControlState(Event evt)
        {
            if (evt.type != EventType.Repaint)
                return;

            if (m_SearchBoxFocus)
            {
                SearchField.Focus();
                m_SearchBoxFocus = false;
            }
        }

        private bool HandleDefaultPressEnter(Event evt)
        {
            if (evt.type != EventType.KeyDown)
                return false;

            if (evt.modifiers > 0)
                return false;

            if (AutoComplete.enabled)
                return false;

            #if USE_SEARCH_MODULE
            if (GUIUtility.textFieldInput)
                return false;
            #endif

            if (m_Selection.Count != 0 || results.Count == 0)
                return false;

            if (evt.keyCode != KeyCode.KeypadEnter && evt.keyCode != KeyCode.Return)
                return false;

            SetSelection(0);
            evt.Use();
            GUIUtility.ExitGUI();
            return true;
        }

        private void HandleKeyboardNavigation(Event evt)
        {
            if (!evt.isKey)
                return;

            // Ignore tabbing and line return in quicksearch
            if (evt.keyCode == KeyCode.None && (evt.character == '\t' || (int)evt.character == 10))
                evt.Use();

            if (AutoComplete.HandleKeyEvent(evt))
                return;

            if (HandleDefaultPressEnter(evt))
                return;

            if (SearchField.HandleKeyEvent(evt))
                return;

            if (evt.type == EventType.KeyDown
                #if USE_SEARCH_MODULE
                && !GUIUtility.textFieldInput
                #endif
            )
            {
                var ctrl = evt.control || evt.command;
                if (evt.keyCode == KeyCode.Escape)
                {
                    if (!docked)
                    {
                        SendEvent(SearchAnalytics.GenericEventType.QuickSearchDismissEsc);
                        selectCallback?.Invoke(null, true);
                        selectCallback = null;
                        evt.Use();
                        CloseSearchWindow();
                    }
                    else
                    {
                        evt.Use();
                        ClearSearch();
                    }
                }
                else if (evt.keyCode == KeyCode.F1)
                {
                    SetSearchText("?");
                    SendEvent(SearchAnalytics.GenericEventType.QuickSearchToggleHelpProviderF1);
                    evt.Use();
                }
                else if (evt.keyCode == KeyCode.F5)
                {
                    Refresh();
                    evt.Use();
                }
                else if (evt.keyCode == KeyCode.F4)
                {
                    TogglePanelView(ShownPanels.InspectorPreview);
                    evt.Use();
                }
                else if (evt.keyCode == KeyCode.F3)
                {
                    TogglePanelView(ShownPanels.LeftSideBar);
                    evt.Use();
                }
                else if (evt.modifiers.HasAny(EventModifiers.Alt) && evt.keyCode == KeyCode.LeftArrow)
                {
                    string previousGroupId = null;
                    foreach (var group in EnumerateGroups())
                    {
                        if (previousGroupId != null && group.id == m_FilteredItems.currentGroup)
                        {
                            SelectGroup(previousGroupId);
                            break;
                        }
                        previousGroupId = group.id;
                    }
                    evt.Use();
                }
                else if (evt.modifiers.HasAny(EventModifiers.Alt) && evt.keyCode == KeyCode.RightArrow)
                {
                    bool selectNext = false;
                    foreach (var group in EnumerateGroups())
                    {
                        if (selectNext)
                        {
                            SelectGroup(group.id);
                            break;
                        }
                        else if (group.id == m_FilteredItems.currentGroup)
                            selectNext = true;
                    }
                    evt.Use();
                }
                else if (evt.keyCode == KeyCode.Tab && evt.modifiers == EventModifiers.None)
                {
                    if (AutoComplete.Show(context, position))
                        evt.Use();
                }
            }

            if (evt.type != EventType.Used && m_ResultView != null)
                m_ResultView.HandleInputEvent(evt, m_Selection);

            if (m_FilteredItems.Count == 0)
                SelectSearch();
        }

        public void SelectSearch()
        {
            m_SearchBoxFocus = true;
        }

        private void CloseSearchWindow()
        {
            if (s_FocusedWindow)
                s_FocusedWindow.Focus();
            Close();
        }

        private void DrawHelpText()
        {
            if (string.IsNullOrEmpty(context.searchText.Trim()))
            {
                using (new GUILayout.VerticalScope(Styles.noResult))
                {
                    GUILayout.FlexibleSpace();
                    using (new GUILayout.HorizontalScope(Styles.noResult, GUILayout.ExpandHeight(true)))
                    {
                        GUILayout.FlexibleSpace();
                        using (new GUILayout.VerticalScope(Styles.tipsSection, GUILayout.Height(160)))
                        {
                            GUILayout.Label("<b>Search Tips</b>", Styles.tipText);
                            GUILayout.Space(15);
                            for (var i = 0; i < Styles.searchTipIcons.Length; ++i)
                            {
                                using (new GUILayout.HorizontalScope(Styles.noResult))
                                {
                                    GUILayout.Label(Styles.searchTipIcons[i], Styles.tipIcon);
                                    GUILayout.Label(Styles.searchTipLabels[i], Styles.tipText);
                                }
                            }
                            GUILayout.FlexibleSpace();
                        }
                        GUILayout.FlexibleSpace();
                    }
                    GUILayout.FlexibleSpace();
                }
            }
            else
            {
                GUILayout.Box(GetNoResultsHelpString(), Styles.noResult, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            }
        }

        private string GetNoResultsHelpString()
        {
            var provider = SearchService.GetProvider(m_FilteredItems.currentGroup);
            if (m_FilteredItems.TotalCount == 0 || provider == null)
                return $"No result for query \"{context.searchText}\"\nTry something else?";

            return $"There is no result for {provider.name}\nSelect another search tab?";
        }

        private void DrawItems(Event evt, float availableSpace)
        {
            using (new EditorGUILayout.VerticalScope(Styles.panelBackground, GUILayout.Width(availableSpace)))
            {
                DrawTabs(evt, availableSpace);
                if (m_FilteredItems.Count > 0 && m_ResultView != null)
                    m_ResultView.Draw(m_Selection, availableSpace);
                else
                    DrawHelpText();
            }
        }

        static class ComputedValues
        {
            public static float tabButtonsWidth { get; private set; }
            static ComputedValues()
            {
                tabButtonsWidth = Styles.tabMoreButton.CalcSize(Styles.moreProviderFiltersContent).x
                    #if USE_SEARCH_MODULE
                    + Styles.syncButton.CalcSize(Styles.syncSearchButtonContent).x
                    + Styles.syncButton.margin.horizontal
                    #endif
                    + Styles.tabMoreButton.margin.horizontal;
            }
        }

        public IEnumerable<IGroup> EnumerateGroups()
        {
            return m_FilteredItems.EnumerateGroups();
        }

        private void DrawTabs(Event evt, float availableSpace)
        {
            const float tabMarginLeft = 3f;
            const float tabMarginRight = 3f;
            var maxBarWidth = availableSpace - ComputedValues.tabButtonsWidth;
            var moreTabButtonSize = Styles.searchTabMoreButton.CalcSize(GUIContent.none);
            var realTabMarginRight = Mathf.Max(tabMarginRight, Styles.searchTabMoreButton.margin.right);
            var tabExtraSpace = tabMarginLeft + moreTabButtonSize.x + Styles.searchTabMoreButton.margin.left + realTabMarginRight;
            using (new EditorGUILayout.HorizontalScope(Styles.searchTabBackground, GUILayout.MaxWidth(availableSpace)))
            {
                var maxTabWidth = 100f;
                Dictionary<string, GUIContent> groupsContent = new Dictionary<string, GUIContent>();
                var allGroups = EnumerateGroups().ToList();
                var currentGroupIndex = -1;
                var lastSelectedMoreGroupIndex = -1;

                var isEmptyQuery = string.IsNullOrEmpty(context.searchText.Trim());
                for (var i = 0; i < allGroups.Count; ++i)
                {
                    var group = allGroups[i];
                    GUIContent content;
                    if (isEmptyQuery)
                    {
                        content = new GUIContent($"{group.name}");
                    }
                    else
                    {
                        var formattedCount = Utils.FormatCount((ulong)group.count);
                        content = new GUIContent($"{group.name} {string.Format(Styles.tabCountTextColorFormat, formattedCount)}");
                    }

                    if (!groupsContent.TryAdd(group.name, content))
                        continue;
                    maxTabWidth = Mathf.Max(Styles.searchTab.CalcSize(content).x + tabExtraSpace, maxTabWidth);
                    if (group.id == m_FilteredItems.currentGroup)
                        currentGroupIndex = i;
                    if (group.id == m_LastSelectedMoreGroup)
                        lastSelectedMoreGroupIndex = i;
                }

                // Get the maximum visible group visible
                var visibleGroupCount = Math.Min(Math.Max(Mathf.FloorToInt(maxBarWidth / maxTabWidth), k_MinimumGroupVisible), allGroups.Count);
                var needTabDropdown = visibleGroupCount < allGroups.Count;
                var availableRect = GUILayoutUtility.GetRect(
                    maxTabWidth * visibleGroupCount,
                    Styles.searchTab.fixedHeight,
                    Styles.searchTab,
                    GUILayout.MaxWidth(maxBarWidth));

                var visibleGroups = new List<IGroup>(visibleGroupCount);
                var hiddenGroups = new List<IGroup>(Math.Max(allGroups.Count - visibleGroupCount, 0));
                GetVisibleAndHiddenGroups(allGroups, visibleGroups, hiddenGroups, visibleGroupCount, currentGroupIndex, lastSelectedMoreGroupIndex);

                var groupIndex = 0;
                var groupStartPosition = availableRect.x;
                foreach (var group in visibleGroups)
                {
                    var oldColor = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, group.count == 0 ? 0.5f : 1f);
                    var isCurrentGroup = m_FilteredItems.currentGroup == group.id;
                    var content = groupsContent[group.name];
                    var tabRect = new Rect(availableRect);
                    tabRect.x = groupStartPosition + groupIndex * maxTabWidth;
                    tabRect.width = maxTabWidth;
                    var moreTabButtonRect = new Rect(tabRect);
                    moreTabButtonRect.x = tabRect.xMax - moreTabButtonSize.x - realTabMarginRight;
                    moreTabButtonRect.width = moreTabButtonSize.x;
                    var hovered = tabRect.Contains(evt.mousePosition);
                    var hoveredMoreTab = moreTabButtonRect.Contains(evt.mousePosition);
                    var showMoreTabButton = needTabDropdown && groupIndex == visibleGroupCount - 1;
                    if (evt.type == EventType.Repaint)
                    {
                        Styles.searchTab.Draw(tabRect, content, hovered, isCurrentGroup, false, false);

                        if (showMoreTabButton)
                        {
                            Styles.searchTabMoreButton.Draw(moreTabButtonRect, hoveredMoreTab, isCurrentGroup, true, false);
                        }
                    }
                    else if (evt.type == EventType.MouseUp && hovered)
                    {
                        if (showMoreTabButton && hoveredMoreTab)
                            ShowHiddenGroups(hiddenGroups);
                        else
                            SelectGroup(group.id);
                        evt.Use();
                    }
                    GUI.color = oldColor;

                    ++groupIndex;
                }

                GUILayout.FlexibleSpace();

                #if USE_SEARCH_MODULE
                DrawSyncSearchButton();
                #endif

                if (EditorGUILayout.DropdownButton(Styles.moreProviderFiltersContent, FocusType.Keyboard, Styles.tabMoreButton))
                    ShowFilters();
                EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
            }
        }

        private void ShowFilters()
        {
            var activeProviders = SearchService.OrderedProviders.Where(p => !p.isExplicitProvider).ToList();
            var explicitProviders = SearchService.OrderedProviders.Where(p => p.isExplicitProvider).ToList();
            var filterMenu = new GenericMenu();

            if (activeProviders.Count > 0)
            {
                filterMenu.AddDisabledItem(new GUIContent("Search Providers"));
                filterMenu.AddSeparator("");

                foreach (var p in activeProviders)
                {
                    var filterContent = new GUIContent($"{p.name} ({p.filterId})");
                    filterMenu.AddItem(filterContent, context.IsEnabled(p.id), () => ToggleFilter(p.id));

                    // Asset provider indexes if more than one.
                    if (p.id == "asset")
                    {
                        var dbs = SearchDatabase.EnumerateAll().ToList();
                        if (dbs.Count > 1)
                        {
                            foreach (var db in dbs)
                                filterMenu.AddItem(new GUIContent($"{db.name} ({db.settings.type} index)"), !db.settings.options.disabled, () => ToggleIndexEnabled(db));
                        }
                    }
                }
            }

            if (explicitProviders.Count > 0)
            {
                filterMenu.AddSeparator("");
                filterMenu.AddDisabledItem(new GUIContent("Special Search Providers"));
                filterMenu.AddSeparator("");

                foreach (var p in explicitProviders)
                {
                    var filterContent = new GUIContent($"{p.name} ({p.filterId})");
                    filterMenu.AddItem(filterContent, context.IsEnabled(p.id), () => ToggleExplicitProvider(p));
                }
            }

            filterMenu.ShowAsContext();
        }

        private void ToggleIndexEnabled(SearchDatabase db)
        {
            db.settings.options.disabled = !db.settings.options.disabled;
            Refresh(RefreshFlags.GroupChanged);
        }

        private void ToggleExplicitProvider(SearchProvider provider)
        {
            if (ToggleFilter(provider.id) && !context.searchText.Trim().StartsWith(provider.filterId))
                SetSearchText($"{provider.filterId} ", TextCursorPlacement.MoveLineEnd);
        }

        private void ShowHiddenGroups(IEnumerable<IGroup> hiddenGroups)
        {
            var groupMenu = new GenericMenu();
            foreach (var g in hiddenGroups)
            {
                var groupContent = new GUIContent($"{g.name} ({g.count})");
                groupMenu.AddItem(groupContent, false, () => SetLastTabGroup(g.id));
            }
            groupMenu.ShowAsContext();
        }

        private void SetLastTabGroup(string groupId)
        {
            if (m_FilteredItems.currentGroup != groupId)
                SelectGroup(groupId);
            m_LastSelectedMoreGroup = groupId;
        }

        private void GetVisibleAndHiddenGroups(List<IGroup> allGroups, List<IGroup> visibleGroups, List<IGroup> hiddenGroups, int visibleGroupCount, int currentGroupIndex, int lastSelectedMoreGroupIndex)
        {
            var nonEssentialGroupCount = Math.Max(visibleGroupCount - k_MinimumGroupVisible, 0);
            if (allGroups.Count > 0)
            {
                var currentIndex = 0;
                for (; currentIndex < nonEssentialGroupCount && currentIndex < allGroups.Count; ++currentIndex)
                {
                    visibleGroups.Add(allGroups[currentIndex]);
                }

                // For the last group, we have to determine which we should show
                if (currentGroupIndex != -1 && currentGroupIndex >= currentIndex)
                {
                    m_LastSelectedMoreGroup = m_FilteredItems.currentGroup;
                    lastSelectedMoreGroupIndex = currentGroupIndex;
                }
                var lastGroupIndex = lastSelectedMoreGroupIndex < currentIndex ? currentIndex : lastSelectedMoreGroupIndex;

                for (var i = currentIndex; i < allGroups.Count; ++i)
                {
                    if (i == lastGroupIndex)
                        visibleGroups.Add(allGroups[i]);
                    else
                        hiddenGroups.Add(allGroups[i]);
                }
            }
        }

        internal void SelectGroup(string groupId)
        {
            if (m_FilteredItems.currentGroup == groupId)
                return;
            var selectedProvider = SearchService.GetProvider(groupId);
            if (selectedProvider != null && selectedProvider.showDetailsOptions.HasAny(ShowDetailsOptions.ListView))
            {
                if (m_PreviousItemSize == -1f)
                    m_PreviousItemSize = itemIconSize;
                itemIconSize = 1;
            }
            else if (m_PreviousItemSize >= 0f)
            {
                itemIconSize = m_PreviousItemSize;
                m_PreviousItemSize = -1f;
            }

            var evt = SearchAnalytics.GenericEvent.Create(m_WindowId, SearchAnalytics.GenericEventType.QuickSearchSwitchTab, groupId ?? string.Empty);
            evt.stringPayload1 = m_FilteredItems.currentGroup;
            evt.intPayload1 = m_FilteredItems.GetGroupById(groupId)?.count ?? 0;
            SearchAnalytics.SendEvent(evt);

            m_ResultView?.OnGroupChanged(m_FilteredItems.currentGroup, groupId);

            m_FilteredItems.currentGroup = groupId;
            #if USE_SEARCH_MODULE
            if (syncSearch && groupId != null)
                NotifySyncSearch(m_FilteredItems.currentGroup, UnityEditor.SearchService.SearchService.SyncSearchEvent.SyncSearch);
            #endif

            RefreshViews(RefreshFlags.GroupChanged);
        }

        private void OnWindowResized(Vector2 oldSize, Vector2 newSize)
        {
            m_SideBarSplitter.Resize(oldSize, newSize);
            m_DetailsPanelSplitter.Resize(oldSize, newSize);
        }

        private void DrawToolbar(Event evt)
        {
            if (context == null)
                return;

            using (new GUILayout.HorizontalScope(Styles.toolbar))
            {
                var searchButtonStackWidth = BUTTON_STACK_COUNT * (Styles.toolbarButton.fixedWidth + Styles.toolbarButton.margin.left) + Styles.searchField.margin.left * 2;
                var searchTextRect = SearchField.GetRect(context.searchText, position.width, searchButtonStackWidth);
                var searchClearButtonRect = Styles.searchFieldBtn.margin.Remove(searchTextRect);
                searchClearButtonRect.xMin = searchClearButtonRect.xMax - SearchField.s_CancelButtonWidth;

                if (evt.type == EventType.MouseUp && searchClearButtonRect.Contains(evt.mousePosition))
                    ClearSearch();

                var previousSearchText = context.searchText;
                if (evt.type != EventType.KeyDown || evt.keyCode != KeyCode.None || evt.character != '\r')
                    context.searchText = SearchField.Draw(searchTextRect, context.searchText, Styles.searchField);

                if (string.IsNullOrEmpty(context.searchText))
                {
                    GUI.Label(searchTextRect, searchTopicPlaceHolder, Styles.placeholderTextStyle);
                }
                else
                {
                    GUI.Button(searchClearButtonRect, Icons.clear, Styles.searchFieldBtn);
                    EditorGUIUtility.AddCursorRect(searchClearButtonRect, MouseCursor.Arrow);
                }

                EditorGUIUtility.AddCursorRect(searchClearButtonRect, MouseCursor.Arrow);

                DrawTabToFilterInfo(evt, searchTextRect);
                DrawToolbarButtons();

                if (!string.Equals(previousSearchText, context.searchText, StringComparison.Ordinal))
                {
                    SetSelection();
                    ClearCurrentErrors();
                    DebounceRefresh();
                }
                else
                {
                    // Only draw errors when you are done typing, to prevent cases where
                    // the cursor moved because of changes but we did not clear the errors yet.
                    DrawQueryErrors();
                }
            }
        }

        private void DrawQueryErrors()
        {
            if (context.searchInProgress)
                return;

            if (!context.options.HasAny(SearchFlags.ShowErrorsWithResults) && m_FilteredItems.Count > 0)
                return;

            List<SearchQueryError> errors;
            if (m_FilteredItems.currentGroup == (m_FilteredItems as IGroup)?.id)
                errors = context.GetAllErrors().ToList();
            else
                errors = context.GetErrorsByProvider(m_FilteredItems.currentGroup).ToList();

            var te = SearchField.GetTextEditor();
            errors.Sort(SearchQueryError.Compare);
            DrawQueryErrors(errors, te);
        }

        private void DrawQueryErrors(IEnumerable<SearchQueryError> errors, TextEditor te)
        {
            var alreadyShownErrors = new List<SearchQueryError>();
            foreach (var searchQueryError in errors)
            {
                var queryErrorStart = searchQueryError.index;
                var queryErrorEnd = queryErrorStart + searchQueryError.length;

                // Do not show error if the cursor is inside the error itself, or if the error intersect with
                // the current token
                if (te.cursorIndex >= queryErrorStart && te.cursorIndex <= queryErrorEnd)
                    continue;
                SearchPropositionOptions.GetTokenBoundariesAtCursorPosition(context.searchText, te.cursorIndex, out var tokenStartPos, out var tokenEndPos);
                if (queryErrorStart >= tokenStartPos && queryErrorStart <= tokenEndPos)
                    continue;
                if (queryErrorEnd >= tokenStartPos && queryErrorEnd <= tokenEndPos)
                    continue;

                // Do not stack errors on top of each other
                if (alreadyShownErrors.Any(e => e.Overlaps(searchQueryError)))
                    continue;

                alreadyShownErrors.Add(searchQueryError);

                if (searchQueryError.type == SearchQueryErrorType.Error)
                {
                    SearchField.DrawError(
                        queryErrorStart,
                        searchQueryError.length,
                        searchQueryError.reason);
                }
                else
                {
                    SearchField.DrawWarning(
                        queryErrorStart,
                        searchQueryError.length,
                        searchQueryError.reason);
                }
            }
        }

        private int BUTTON_STACK_COUNT = 2; // Number of buttons drawn in DrawToolbarButtons
        private void DrawToolbarButtons()
        {
            BUTTON_STACK_COUNT = 0;
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(context.searchQuery)))
            {
                BUTTON_STACK_COUNT++;
                if (GUILayout.Button(Styles.saveQueryButtonContent, Styles.toolbarButton))
                    SaveSearchQueryFromContext();
            }
            EditorGUI.BeginChangeCheck();
            BUTTON_STACK_COUNT++;
            GUILayout.Toggle(m_ShownPanels.HasFlag(ShownPanels.InspectorPreview), Styles.previewInspectorButtonContent, Styles.toolbarButton);
            if (EditorGUI.EndChangeCheck())
            {
                TogglePanelView(ShownPanels.InspectorPreview);
            }
        }

        private void DrawTabToFilterInfo(Event evt, Rect searchTextRect)
        {
            if (evt.type != EventType.Repaint)
                return;

            var searchTextTrimmedRect = Styles.searchFieldTabToFilterBtn.margin.Remove(searchTextRect);
            var searchTextWidth = Styles.searchField.CalcSize(Utils.GUIContentTemp(context.searchText)).x;
            var pressTabToFilterContextStart = searchTextTrimmedRect.width - Styles.pressToFilterContentWidth;
            var showPressTabToFilter = searchTextWidth < pressTabToFilterContextStart;

            // Prevent overlap with search topic place holder only when it is visible
            if (showPressTabToFilter && string.IsNullOrEmpty(context.searchText))
            {
                var searchTopicPlaceHolderWidth = Styles.placeholderTextStyle.CalcSize(Utils.GUIContentTemp(searchTopicPlaceHolder)).x;
                var searchTopicPlaceHolderEnd = searchTextRect.center.x + searchTopicPlaceHolderWidth / 2;
                if (searchTopicPlaceHolderEnd >= pressTabToFilterContextStart)
                    showPressTabToFilter = false;
            }

            if (showPressTabToFilter)
            {
                EditorGUI.BeginDisabledGroup(true);
                GUI.Label(searchTextTrimmedRect, Styles.pressToFilterContent, Styles.searchFieldTabToFilterBtn);
                EditorGUI.EndDisabledGroup();
            }
        }

        private void ClearSearch()
        {
            activeSearchQuery = null;
            SendEvent(SearchAnalytics.GenericEventType.QuickSearchClearSearch);
            AutoComplete.Clear();
            context.searchText = "";
            GUI.changed = true;
            GUI.FocusControl(null);
            SetSelection();
            DebounceRefresh();
            GUIUtility.ExitGUI();
        }

        [CommandHandler("OpenQuickSearch")]
        internal static void OpenQuickSearchCommand(CommandExecuteContext c)
        {
            OpenDefaultQuickSearch();
        }

        private void SaveSearchQueryFromContext()
        {
            bool newQuery = false;
            var searchQueryPath = activeSearchQuery != null ? AssetDatabase.GetAssetPath(activeSearchQuery) : null;

            if (string.IsNullOrEmpty(searchQueryPath))
            {
                var initialFolder = SearchSettings.GetFullQueryFolderPath();
                var searchQueryFileName = SearchQuery.GetQueryName(context.searchQuery);
                searchQueryPath = EditorUtility.SaveFilePanel("Save search query...", initialFolder, searchQueryFileName, "asset");
                if (string.IsNullOrEmpty(searchQueryPath))
                    return;

                searchQueryPath = Utils.CleanPath(searchQueryPath);
                if (!System.IO.Directory.Exists(Path.GetDirectoryName(searchQueryPath)) || !Utils.IsPathUnderProject(searchQueryPath))
                    return;

                searchQueryPath = Utils.GetPathUnderProject(searchQueryPath);
                SearchSettings.queryFolder = Utils.CleanPath(Path.GetDirectoryName(searchQueryPath));
                newQuery = true;
            }

            try
            {
                activeSearchQuery = AssetDatabase.LoadAssetAtPath<SearchQuery>(searchQueryPath) ?? SearchQuery.Create(context);
                if (!activeSearchQuery)
                {
                    Debug.LogError($"Failed to save search query at {searchQueryPath}");
                    return;
                }

                var folder = Utils.CleanPath(Path.GetDirectoryName(searchQueryPath));
                var queryName = Path.GetFileNameWithoutExtension(searchQueryPath);
                activeSearchQuery.viewState = m_ResultView.SaveViewState(queryName);
                activeSearchQuery.viewState.group = m_FilteredItems.currentGroup;

                if (SearchQuery.SaveQuery(activeSearchQuery, context, folder, queryName) && newQuery)
                {
                    SearchSettings.AddRecentSearch(activeSearchQuery.text);
                    SearchQuery.ResetSearchQueryItems();
                    Selection.activeObject = activeSearchQuery;
                }

                SendEvent(SearchAnalytics.GenericEventType.QuickSearchCreateSearchQuery, activeSearchQuery.text, SearchSettings.queryFolder);
            }
            catch
            {
                Debug.LogError($"Failed to save search query at {searchQueryPath}");
            }
        }

        private void DebounceRefresh()
        {
            if (!this)
                return;

            if (SearchSettings.debounceMs == 0)
                RefreshSearch();
            else if (m_DebounceOff == null)
                m_DebounceOff = Utils.CallDelayed(RefreshSearch, SearchSettings.debounceMs / 1000.0);
        }

        private static bool IsObjectMatchingType(SearchItem item, Type filterType)
        {
            if (item == SearchItem.none)
                return true;
            var obj = Utils.ToObject(item, filterType);
            if (!obj)
                return false;
            var objType = obj.GetType();
            return filterType.IsAssignableFrom(objType);
        }

        private void LoadContext()
        {
            var contextHash = context.GetHashCode();
            if (context.options.HasAny(SearchFlags.FocusContext))
            {
                var contextualProvider = GetContextualProvider();
                if (contextualProvider != null)
                    contextHash ^= contextualProvider.id.GetHashCode();
            }
            if (m_ContextHash == 0)
                m_ContextHash = contextHash;
        }

        private bool LoadSessionSettings()
        {
            if (context != null && string.IsNullOrEmpty(context.searchText))
                SetSearchText(SearchSettings.GetScopeValue(k_LastSearchPrefKey, m_ContextHash, ""));
            else
                RefreshSearch();

            #if USE_SEARCH_MODULE
            if (context?.options.HasAny(SearchFlags.Expression) ?? false)
                itemIconSize = (int)DisplayMode.Table;
            else
            #endif
            {
                itemIconSize = SearchSettings.itemIconSize;
            }

            m_ShownPanels = ShownPanels.None;
            if (SearchSettings.GetScopeValue(nameof(ShownPanels.InspectorPreview), m_ContextHash, 0) != 0)
            {
                m_ShownPanels |= ShownPanels.InspectorPreview;
            }

            if (SearchSettings.showSavedSearchPanel)
            {
                m_ShownPanels |= ShownPanels.LeftSideBar;
            }

            if (m_FilteredItems != null)
            {
                var loadGroup = SearchSettings.GetScopeValue(nameof(m_FilteredItems.currentGroup), m_ContextHash, ((IGroup)m_FilteredItems).id);
                if (context.options.HasAny(SearchFlags.FocusContext))
                {
                    var contextualProvider = GetContextualProvider();
                    if (contextualProvider != null)
                        loadGroup = contextualProvider.id;
                }

                SelectGroup(loadGroup);
            }

            return true;
        }

        private void SaveSessionSettings()
        {
            SearchSettings.SetScopeValue(k_LastSearchPrefKey, m_ContextHash, context.searchText);
            SearchSettings.SetScopeValue(nameof(ShownPanels.InspectorPreview), m_ContextHash, m_ShownPanels.HasAny(ShownPanels.InspectorPreview) ? 1 : 0);
            EditorPrefs.SetFloat(k_SideBarWidthKey, m_SideBarSplitter.pos);
            EditorPrefs.SetFloat(k_DetailsWidthKey, m_DetailsPanelSplitter.pos);

            if (m_FilteredItems != null)
                SearchSettings.SetScopeValue(nameof(m_FilteredItems.currentGroup), m_ContextHash, m_FilteredItems.currentGroup);

            SearchSettings.Save();
        }

        private void UpdateItemSize(float value)
        {
            var oldMode = displayMode;
            #if USE_SEARCH_MODULE
            m_ItemSize = value > (int)DisplayMode.Table ? (int)DisplayMode.Limit : value;
            #else
            m_ItemSize = value > (int)DisplayMode.Limit ? (int)DisplayMode.Limit : value;
            #endif
            var newMode = displayMode;
            if (m_ResultView == null || oldMode != newMode)
            {
                if (newMode == DisplayMode.List)
                    m_ResultView = new ListView(this);
                else if (newMode == DisplayMode.Grid)
                    m_ResultView = new GridView(this);
                #if USE_SEARCH_MODULE
                else if (newMode == DisplayMode.Table)
                    m_ResultView = new TableView(this);
                #endif
                RefreshViews(RefreshFlags.DisplayModeChanged);
            }
        }

        internal void SendEvent(SearchAnalytics.GenericEventType category, string name = null, string message = null, string description = null)
        {
            SearchAnalytics.SendEvent(m_WindowId, category, name, message, description);
        }

        #if USE_SEARCH_MODULE
        [MenuItem("Edit/Search All... %k", priority = 161)]
        #else
        [MenuItem("Edit/Search All... %k", priority = 145)]
        #endif
        private static QuickSearch OpenDefaultQuickSearch()
        {
            var window = Open(flags: SearchFlags.OpenGlobal);
            SearchAnalytics.SendEvent(window.m_WindowId, SearchAnalytics.GenericEventType.QuickSearchOpen, "Default");
            return window;
        }

        #if USE_SEARCH_MODULE

        private SearchProvider GetProviderById(string providerId)
        {
            return context.providers.FirstOrDefault(p => p.active && p.id == providerId);
        }

        [Shortcut(k_TogleSyncShortcutName, typeof(QuickSearch), KeyCode.L, ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        internal static void ToggleSyncSearchView(ShortcutArguments args)
        {
            var window = args.context as QuickSearch;
            if (window == null)
                return;
            window.SetSyncSearchView(!window.syncSearch);
        }

        private void SetSyncSearchView(bool sync)
        {
            var providerSupportsSync = GetProviderById(m_FilteredItems.currentGroup)?.supportsSyncViewSearch ?? false;
            var searchViewSyncEnabled = providerSupportsSync && SearchViewSyncEnabled(m_FilteredItems.currentGroup);
            var supportsSync = providerSupportsSync && searchViewSyncEnabled;
            if (!supportsSync)
                return;

            syncSearch = sync;
            if (syncSearch)
                SendEvent(SearchAnalytics.GenericEventType.QuickSearchSyncViewButton, m_FilteredItems.currentGroup);
            Refresh();
        }

        private void DrawSyncSearchButton()
        {
            var providerSupportsSync = GetProviderById(m_FilteredItems.currentGroup)?.supportsSyncViewSearch ?? false;
            var searchViewSyncEnabled = providerSupportsSync && SearchViewSyncEnabled(m_FilteredItems.currentGroup);
            var supportsSync = providerSupportsSync && searchViewSyncEnabled;
            using (new EditorGUI.DisabledScope(!supportsSync))
            {
                EditorGUI.BeginChangeCheck();
                var syncButtonContent = m_FilteredItems.currentGroup == "all" ? Styles.syncSearchAllGroupTabContent : !providerSupportsSync ? Styles.syncSearchProviderNotSupportedContent : !searchViewSyncEnabled ? Styles.syncSearchViewNotEnabledContent : syncSearch ? Styles.syncSearchOnButtonContent : Styles.syncSearchButtonContent;
                var sync = GUILayout.Toggle(syncSearch, syncButtonContent, Styles.syncButton);
                if (EditorGUI.EndChangeCheck())
                {
                    SetSyncSearchView(sync);
                }
            }
        }

        private static bool SearchViewSyncEnabled(string groupId)
        {
            switch (groupId)
            {
                case "asset":
                    return UnityEditor.SearchService.ProjectSearch.HasEngineOverride();
                case "scene":
                    return UnityEditor.SearchService.SceneSearch.HasEngineOverride();
                default:
                    return false;
            }
        }

        [CommandHandler("OpenQuickSearchInContext")]
        static void OpenQuickSearchInContextCommand(CommandExecuteContext c)
        {
            var query = c.GetArgument<string>(0);
            var sourceContext = c.GetArgument<string>(1);
            var wasReused = HasOpenInstances<QuickSearch>();
            var flags = SearchFlags.OpenContextual | SearchFlags.ReuseExistingWindow;
            var qsWindow = Create(flags);
            SearchProvider contextualProvider = null;
            if (wasReused)
                contextualProvider = qsWindow.GetContextualProvider();
            qsWindow.ShowWindow(flags: flags);
            if (contextualProvider != null)
                qsWindow.SelectGroup(contextualProvider.id);
            qsWindow.SendEvent(SearchAnalytics.GenericEventType.QuickSearchJumpToSearch, qsWindow.m_FilteredItems.currentGroup, sourceContext);
            qsWindow.SetSearchText(query);
            qsWindow.syncSearch = true;
            c.result = true;
        }

        #endif

        [Shortcut("Help/Search Contextual")]
        internal static void OpenContextual()
        {
            Open(flags: SearchFlags.OpenContextual);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (m_Disposed)
                return;

            if (disposing)
                Close();

            m_Disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private void ClearCurrentErrors()
        {
            context.ClearErrors();
        }

        private SearchProvider GetContextualProvider()
        {
            return context.providers.FirstOrDefault(p => p.active && (p.isEnabledForContextualSearch?.Invoke() ?? false));
        }

        #if USE_SEARCH_MODULE
        [WindowAction]
        internal static WindowAction CreateSearchHelpWindowAction()
        {
            // Developer-mode render doc button to enable capturing any HostView content/panels
            var action = WindowAction.CreateWindowActionButton("HelpSearch", OpenSearchHelp, null, ContainerWindow.kButtonWidth + 1, Icons.help);
            action.validateHandler = (window, _) => window.GetType() == typeof(QuickSearch);
            return action;
        }

        private static void OpenSearchHelp(EditorWindow window, WindowAction action)
        {
            var windowId = (window as QuickSearch)?.windowId ?? null;
            SearchAnalytics.SendEvent(windowId, SearchAnalytics.GenericEventType.QuickSearchOpenDocLink);
            EditorUtility.OpenWithDefaultApp("https://docs.unity3d.com/2021.1/Documentation/Manual/search-overview.html");
        }

        #endif
    }
}

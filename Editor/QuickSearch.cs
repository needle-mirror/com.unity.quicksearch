//#define QUICKSEARCH_DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Unity.QuickSearch
{
    /// <summary>
    /// Quick Search Editor Window
    /// </summary>
    /// <example>
    /// using Unity.QuickSearch;
    /// [MenuItem("Tools/Quick Search %space", priority = 42)]
    /// private static void OpenQuickSearch()
    /// {
    ///     QuickSearch.Open();
    /// }
    /// </example>
    public class QuickSearch : EditorWindow, ISearchView
    {
        const int k_ResetSelectionIndex = -1;
        const string k_FilterPrefKey = SearchService.prefKey + ".filters";
        const string k_LastSearchPrefKey = "last_search";

        private static readonly bool isDeveloperMode = Utils.IsDeveloperMode();
        private static EditorWindow s_FocusedWindow;

        // Selection state
        private SortedSearchList m_FilteredItems;
        private readonly List<int> m_Selection = new List<int>();
        private int m_DelayedCurrentSelection = k_ResetSelectionIndex;
        private bool m_FocusSelectedItem = false;

        private EditorWindow lastFocusedWindow;
        private bool m_SearchBoxFocus;
        private bool m_ShowFilterWindow = false;
        private bool m_ShowCreateWindow = false;
        private SearchAnalytics.SearchEvent m_CurrentSearchEvent;
        private double m_DebounceTime = 0.0;
        private float m_ItemSize = 0;
        private DetailView m_DetailView;
        private IResultView m_ResultView;
        private bool m_DetailsViewSplitterResize = false;
        private float m_DetailsViewSplitterPos = -1f;

        internal event Action nextFrame;
        internal string searchTopic { get; set; }
        internal bool sendAnalyticsEvent { get; set; }
        internal bool saveFilters { get; set; }

        public Action<SearchItem, bool> selectCallback { get; private set; }
        public Func<SearchItem, bool> filterCallback { get; private set; }
        public Action<SearchItem> trackingCallback { get; private set; }
        public SearchSelection selection { get => new SearchSelection(m_Selection, m_FilteredItems); }
        public SearchContext context { get; private set; }
        public ISearchList results => m_FilteredItems;
        public DisplayMode displayMode { get => IsDisplayGrid() ? DisplayMode.Grid : DisplayMode.List; }
        public float itemIconSize { get => m_ItemSize; set => UpdateItemSize(value); }
        [SerializeField] public bool multiselect { get; set; }

        /// <summary>
        /// Set the search text and refresh search results.
        /// </summary>
        /// <param name="searchText">New search text</param>
        public void SetSearchText(string searchText, TextCursorPlacement moveCursor = TextCursorPlacement.Default)
        {
            context.searchText = searchText ?? String.Empty;
            Refresh();

            SearchField.ClearAutoComplete();
            nextFrame += () => SearchField.MoveCursor(moveCursor);
        }

        /// <summary>
        /// Open the Quick Search filter window to edit active filters.
        /// </summary>
        public void PopFilterWindow()
        {
            nextFrame += () => m_ShowFilterWindow = true;
        }

        public void PopSearchQueryCreateWindow()
        {
            nextFrame += () => m_ShowCreateWindow = true;
        }

        /// <summary>
        /// Re-fetch the search results and refresh the UI.
        /// </summary>
        public void Refresh()
        {
            var foundItems = SearchService.GetItems(context);
            if (selectCallback != null)
                foundItems.Add(SearchItem.none);
            else if (String.IsNullOrEmpty(context.searchText))
                foundItems = SearchQuery.GetAllSearchQueryItems();

            SetItems(filterCallback == null ? foundItems : foundItems.Where(item => filterCallback(item)));

            EditorApplication.update -= UpdateAsyncResults;
            EditorApplication.update += UpdateAsyncResults;
        }

        /// <summary>
        /// Creates a new instance of a Quick Search window but does not show it immediately allowing a user to setup filter before showing the window.
        /// </summary>
        /// <returns>Returns the Quick Search editor window instance.</returns>
        public static QuickSearch Create()
        {
            s_FocusedWindow = focusedWindow;
            var qsWindow = SearchSettings.dockable && HasOpenInstances<QuickSearch>() ? GetWindow<QuickSearch>() : CreateInstance<QuickSearch>();
            qsWindow.multiselect = true;
            qsWindow.saveFilters = true;
            qsWindow.searchTopic = "anything";
            qsWindow.sendAnalyticsEvent = true; // Ensure we won't send events while doing a domain reload.
            return qsWindow;
        }

        /// <summary>
        /// Creates and open a new instance of Quick Search
        /// </summary>
        /// <param name="defaultWidth">Initial width of the window.</param>
        /// <param name="defaultHeight">Initial height of the window.</param>
        /// <returns>Returns the Quick Search editor window instance.</returns>
        public static QuickSearch Open(float defaultWidth = 850, float defaultHeight = 539)
        {
            return Create().ShowWindow(defaultWidth, defaultHeight);
        }

        /// <summary>
        /// Open the quick search window using a specific context (activating specific filters and such).
        /// </summary>
        /// <param name="providerId">Unique name of the provider to start the quick search in.</param>
        /// <example>
        /// [MenuItem("Tools/Search Menus _F1")]
        /// public static void SearchMenuItems()
        /// {
        ///     QuickSearch.OpenWithContextualProvider("menu");
        /// }
        /// </example>
        public static QuickSearch OpenWithContextualProvider(string providerId)
        {
            var provider = SearchService.Providers.Find(p => p.name.id == providerId);
            if (provider == null)
            {
                Debug.LogWarning("Quick Search cannot find search provider " + providerId);
                return QuickSearch.Open();
            }

            var qsWindow = Create();
            qsWindow.InitContextualProviders(new [] {provider});
            qsWindow.searchTopic = provider.name.displayName.ToLower();
            return qsWindow.ShowWindow();
        }

        /// <summary>
        /// Open the default Quick Search window using default settings.
        /// </summary>
        /// <param name="defaultWidth">Initial width of the window.</param>
        /// <param name="defaultHeight">Initial height of the window.</param>
        /// <returns>Returns the Quick Search editor window instance.</returns>
        public QuickSearch ShowWindow(float defaultWidth = 850, float defaultHeight = 539)
        {
            var windowSize = new Vector2(defaultWidth, defaultHeight);
            Refresh();
            if (SearchSettings.dockable)
                this.Show();
            else
                this.ShowDropDown(windowSize);
            Focus();
            return this;
        }

        /// <summary>
        /// Use Quick Search to as an object picker to select any object based on the specified filter type.
        /// </summary>
        /// <param name="selectHandler"></param>
        /// <param name="trackingHandler"></param>
        /// <param name="searchText"></param>
        /// <param name="typeName"></param>
        /// <param name="filterType"></param>
        /// <param name="defaultWidth"></param>
        /// <param name="defaultHeight"></param>
        /// <returns></returns>
        public static QuickSearch ShowObjectPicker(
            Action<UnityEngine.Object, bool> selectHandler,
            Action<UnityEngine.Object> trackingHandler,
            string searchText, string typeName, Type filterType,
            float defaultWidth = 850, float defaultHeight = 539)
        {
            if (selectHandler == null || typeName == null)
                return null;

            if (filterType == null)
                filterType = TypeCache.GetTypesDerivedFrom<UnityEngine.Object>()
                    .FirstOrDefault(t => t.Name == typeName) ?? typeof(UnityEngine.Object);

            var qs = Create();
            qs.saveFilters = false;
            qs.searchTopic = "object";
            qs.sendAnalyticsEvent = true;
            qs.titleContent.text = $"Select {filterType?.Name ?? typeName}...";
            qs.itemIconSize = 64;
            qs.multiselect = false;

            qs.filterCallback = (item) => IsObjectMatchingType(item ?? SearchItem.none, filterType);
            qs.selectCallback = (item, canceled) => selectHandler?.Invoke(Utils.ToObject(item, filterType), canceled);
            qs.trackingCallback = (item) => trackingHandler?.Invoke(Utils.ToObject(item, filterType));
            qs.context.wantsMore = true;
            qs.context.filterType = filterType;
            qs.SetSearchText(searchText, TextCursorPlacement.MoveToStartOfNextWord);

            qs.Refresh();
            qs.ShowAuxWindow();
            qs.position = Utils.GetMainWindowCenteredPosition(new Vector2(defaultWidth, defaultHeight));
            qs.Focus();

            return qs;
        }

        [UsedImplicitly]
        internal void OnEnable()
        {
            hideFlags |= HideFlags.DontSaveInEditor;
            itemIconSize = SearchSettings.itemIconSize;
            lastFocusedWindow = s_FocusedWindow;

            #if UNITY_2020_2_OR_NEWER
            wantsLessLayoutEvents = true;
            #endif

            SearchQuery.ResetSearchQueryItems();

            SearchSettings.SortActionsPriority();

            // Create search view context
            context = new SearchContext(SearchService.Providers.Where(p => p.active)) { focusedWindow = lastFocusedWindow, searchView = this };

            // Create search view state objects
            m_SearchBoxFocus = true;
            m_CurrentSearchEvent = new SearchAnalytics.SearchEvent();
            m_DetailView = new DetailView(this);
            m_FilteredItems = new SortedSearchList(context);

            LoadGlobalSettings();
            LoadSessionSettings();

            context.asyncItemReceived += OnAsyncItemsReceived;

            DebugInfo.Enable(this);
        }

        [UsedImplicitly]
        internal void OnDisable()
        {
            DebugInfo.Disable();

            s_FocusedWindow = null;

            EditorApplication.update -= UpdateAsyncResults;
            EditorApplication.delayCall -= DebouncedRefresh;
            EditorApplication.delayCall -= DelayTrackSelection;

            if (!isDeveloperMode)
                SendSearchEvent(null); // Track canceled searches

            SaveSessionSettings();
            SaveGlobalSettings();

            // End search session
            context.asyncItemReceived -= OnAsyncItemsReceived;
            context.Dispose();
            context = null;

            Resources.UnloadUnusedAssets();
        }

        [UsedImplicitly]
        internal void OnGUI()
        {
            if (context == null)
                return;

            if (Event.current.type == EventType.Repaint)
            {
                nextFrame?.Invoke();
                nextFrame = null;
            }

            HandleKeyboardNavigation();

            var windowBorder = SearchSettings.dockable ? GUIStyle.none : Styles.panelBorder;
            using (new EditorGUILayout.VerticalScope(windowBorder))
            {
                var rect = DrawToolbar(context);
                if (context == null)
                    return;
                using (new EditorGUILayout.HorizontalScope())
                {
                    var showDetails = selectCallback == null && m_DetailView.HasDetails(context);
                    if (m_DetailsViewSplitterPos < 0f)
                        m_DetailsViewSplitterPos = position.width - Styles.previewSize.x;

                    DrawItems(showDetails ? m_DetailsViewSplitterPos-2f : position.width);

                    if (showDetails)
                    {
                        m_DetailView.Draw(context, position.width - m_DetailsViewSplitterPos + 2f);
                        DrawDetailsViewSplitter();
                    }
                }

                DebugInfo.Draw();
                DrawStatusBar();
                SearchField.AutoCompletion(rect, context, this);
            }

            UpdateFocusControlState();
        }

        [UsedImplicitly]
        internal void Update()
        {
            if (focusedWindow != this)
                return;

            var time = EditorApplication.timeSinceStartup;
            var repaintRequested = SearchField.UpdateBlinkCursorState(time);
            if (repaintRequested)
                Repaint();
        }

        internal void SetItems(IEnumerable<SearchItem> items)
        {
            m_FilteredItems = items as List<SearchItem> ?? items.ToList();
            SetSelection();
            UpdateWindowTitle();
        }

        private void SetFilteredProviders(IEnumerable<SearchProvider> providers)
        {
            context.SetFilteredProviders(providers.Select(p => p.name.id));
        }

        private void OnAsyncItemsReceived(IEnumerable<SearchItem> items)
        {
            var filteredItems = items;
            if (filterCallback != null)
                filteredItems = filteredItems.Where(item => filterCallback(item));
            m_FilteredItems.AddItems(filteredItems);
            EditorApplication.update -= UpdateAsyncResults;
            EditorApplication.update += UpdateAsyncResults;
        }

        private void UpdateAsyncResults()
        {
            EditorApplication.update -= UpdateAsyncResults;

            UpdateWindowTitle();
            Repaint();
        }

        private void SendSearchEvent(SearchItem item, SearchAction action = null)
        {
            if (item != null)
                m_CurrentSearchEvent.Success(item, action);

            if (m_CurrentSearchEvent.success || m_CurrentSearchEvent.elapsedTimeMs > 7000)
            {
                m_CurrentSearchEvent.Done();
                if (item != null)
                    m_CurrentSearchEvent.searchText = $"{context.searchText} => {item.id}";
                else
                    m_CurrentSearchEvent.searchText = context.searchText;
                if (sendAnalyticsEvent)
                    SearchAnalytics.SendSearchEvent(m_CurrentSearchEvent, context);
            }

            // Prepare next search event
            m_CurrentSearchEvent = new SearchAnalytics.SearchEvent();
        }

        private void UpdateWindowTitle()
        {
            if (!titleContent.image)
                titleContent.image = Icons.quicksearch;
            if (m_FilteredItems.Count == 0)
                titleContent.text = $"Search {searchTopic}";
            else
            {
                var itemStr = m_FilteredItems.Count <= 1 ? "item" : "items";
                titleContent.text = $"Found {m_FilteredItems.Count - (selectCallback != null ? 1 : 0)} {itemStr}";
            }
        }

        private static string FormatStatusMessage(SearchContext context, ICollection<SearchItem> items)
        {
            var msg = "";
            if (string.IsNullOrEmpty(context.searchText) && items.Any())
            {
                msg = $"Existing search queries";
                return msg;
            }


            if (context.actionId != null)
                msg = $"Executing action for {context.actionId} ";

            var providers = context.providers.ToList();
            if (providers.Count == 0)
                return "There is no activated search provider";

            if (providers.All(p => p.isExplicitProvider))
            {
                if (msg.Length == 0)
                    msg = "Activate ";

                msg += Utils.FormatProviderList(providers);
            }
            else
            {
                if (msg.Length == 0)
                    msg = "Searching ";

                msg += Utils.FormatProviderList(providers.Where(p => !p.isExplicitProvider));
            }

            if (items != null && items.Count > 0)
            {
                msg += $" and found <b>{items.Count}</b> result";
                if (items.Count > 1)
                    msg += "s";
            }
            else if (!string.IsNullOrEmpty(context.searchQuery))
                msg += " and found nothing";

            return msg;
        }

        private void DrawStatusBar()
        {
            using (new GUILayout.HorizontalScope())
            {
                var title = FormatStatusMessage(context, m_FilteredItems);
                var tooltip = Utils.FormatProviderList(context.providers, true);
                var statusLabelContent = EditorGUIUtility.TrTextContent(title, tooltip);
                GUILayout.Label(statusLabelContent, Styles.statusLabel, GUILayout.MaxWidth(position.width - 100));
                GUILayout.FlexibleSpace();

                EditorGUI.BeginChangeCheck();
                var newItem = GUILayout.HorizontalSlider(itemIconSize, 0f, 165f,
                    Styles.itemIconSizeSlider, Styles.itemIconSizeSliderThumb, GUILayout.Width(100f));
                if (EditorGUI.EndChangeCheck())
                {
                    itemIconSize = newItem;
                    m_FocusSelectedItem = true;
                }

                if (GUILayout.Button(SearchAnalytics.Version, Styles.versionLabel))
                    Utils.OpenDocumentationUrl();
                if (Event.current.type == EventType.Repaint)
                {
                    var helpButtonRect = GUILayoutUtility.GetLastRect();
                    EditorGUIUtility.AddCursorRect(helpButtonRect, MouseCursor.Link);
                }

                if (context.searchInProgress)
                {
                    var searchInProgressRect = EditorGUILayout.GetControlRect(false,
                        Styles.searchInProgressButton.fixedHeight, Styles.searchInProgressButton, Styles.searchInProgressLayoutOptions);

                    int frame = (int)Mathf.Repeat(Time.realtimeSinceStartup * 5, 11.99f);
                    GUI.Button(searchInProgressRect, Styles.statusWheel[frame], Styles.searchInProgressButton);

                    if (Event.current.type == EventType.MouseDown && searchInProgressRect.Contains(Event.current.mousePosition))
                        SettingsService.OpenUserPreferences(SearchSettings.settingsPreferencesKey);
                }
                else
                {
                    if (GUILayout.Button(Styles.prefButtonContent, Styles.prefButton))
                        SettingsService.OpenUserPreferences(SearchSettings.settingsPreferencesKey);
                }
            }
        }

        private bool IsItemValid(int index)
        {
            if (index < 0 || index >= m_FilteredItems.Count)
                return false;
            return true;
        }

        private bool IsSelectedItemValid()
        {
            var selectionIndex = m_Selection.Count == 0 ? k_ResetSelectionIndex : m_Selection.Last();
            return IsItemValid(selectionIndex);
        }

        public void SetSelection(params int[] selection)
        {
            if (!multiselect && selection.Length > 1)
                throw new Exception("Multi selection is not allowed.");

            var lastIndexAdded = k_ResetSelectionIndex;

            m_Selection.Clear();
            foreach (var idx in selection)
            {
                if (!IsItemValid(idx))
                    continue;

                m_Selection.Add(idx);
                lastIndexAdded = idx;
            }

            if (lastIndexAdded != k_ResetSelectionIndex)
                TrackSelection(lastIndexAdded);
            else
                SearchField.ResetAutoComplete();
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
                TrackSelection(lastIndexAdded);
        }

        private void DelayTrackSelection()
        {
            if (m_FilteredItems.Count == 0)
                return;

            if (!IsItemValid(m_DelayedCurrentSelection))
                return;

            var selectedItem = m_FilteredItems[m_DelayedCurrentSelection];
            if (trackingCallback == null)
                selectedItem.provider?.trackSelection?.Invoke(selectedItem, context);
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

        private void UpdateFocusControlState()
        {
            if (Event.current.type != EventType.Repaint)
                return;

            if (m_SearchBoxFocus)
            {
                SearchField.Focus();
                m_SearchBoxFocus = false;
            }
        }

        private int GetItemCount()
        {
            return m_FilteredItems.Count;
        }

        private void HandleKeyboardNavigation()
        {
            var evt = Event.current;

            if (SearchField.HandleKeyEvent(evt))
                return;

            var ctrl = evt.control || evt.command;
            if (evt.type == EventType.KeyDown)
            {
                var selectedIndex = m_Selection.Count == 0 ? k_ResetSelectionIndex : m_Selection.Last();
                var firstIndex = m_Selection.Count == 0 ? k_ResetSelectionIndex : m_Selection.First();
                var lastIndex = selectedIndex;
                if (evt.keyCode == KeyCode.DownArrow)
                {
                    if (multiselect && evt.modifiers.HasFlag(EventModifiers.Shift) && m_Selection.Count > 0)
                    {
                        if (lastIndex >= firstIndex)
                        {
                            if (lastIndex < m_FilteredItems.Count-1)
                                AddSelection(lastIndex+1);
                        }
                        else
                            m_Selection.Remove(lastIndex);
                    }
                    else
                        SetSelection(selectedIndex + 1);
                    evt.Use();
                }
                else if (evt.keyCode == KeyCode.UpArrow)
                {
                    if (selectedIndex >= 0)
                    {
                        if (multiselect && evt.modifiers.HasFlag(EventModifiers.Shift) && m_Selection.Count > 0)
                        {
                            if (firstIndex < lastIndex)
                                m_Selection.Remove(lastIndex);
                            else if (lastIndex > 0)
                                AddSelection(lastIndex-1);
                        }
                        else
                            SetSelection(selectedIndex - 1);
                        if (selectedIndex - 1 < 0)
                            m_SearchBoxFocus = true;
                        evt.Use();
                    }
                }
                else if (evt.keyCode == KeyCode.PageDown)
                {
                    var jumpAtIndex = Math.Min(selectedIndex + m_ResultView.GetDisplayItemCount() - 1, m_FilteredItems.Count-1);
                    if (multiselect && evt.modifiers.HasFlag(EventModifiers.Shift) && m_Selection.Count > 0)
                    {
                        var r = 0;
                        var range = new int[jumpAtIndex - selectedIndex];
                        for (int i = selectedIndex + 1; i <= jumpAtIndex; ++i)
                            range[r++] = i;
                        AddSelection(range);
                    }
                    else
                    {
                        SetSelection(jumpAtIndex);
                    }
                    evt.Use();
                }
                else if (evt.keyCode == KeyCode.PageUp)
                {
                    var jumpAtIndex = Math.Max(0, selectedIndex - m_ResultView.GetDisplayItemCount());
                    if (multiselect && evt.modifiers.HasFlag(EventModifiers.Shift) && m_Selection.Count > 0)
                    {
                        var r = 0;
                        var range = new int[selectedIndex - jumpAtIndex];
                        for (int i = selectedIndex - 1; i >= jumpAtIndex; --i)
                            range[r++] = i;
                        AddSelection(range);
                    }
                    else
                    {
                        SetSelection(jumpAtIndex);
                    }
                    evt.Use();
                }
                else if (evt.keyCode == KeyCode.RightArrow && evt.modifiers.HasFlag(EventModifiers.Alt))
                {
                    m_CurrentSearchEvent.useActionMenuShortcut = true;
                    if (selectedIndex != -1 && selection.Count <= 1)
                    {
                        var item = m_FilteredItems.ElementAt(selectedIndex);
                        var menuPositionY = (selectedIndex+1) * Styles.itemRowHeight - m_ResultView.scrollPosition.y + Styles.itemRowHeight/2.0f;
                        ShowItemContextualMenu(item, context, new Rect(m_ResultView.rect.xMax - Styles.actionButtonSize, menuPositionY, 1, 1));
                        evt.Use();
                    }
                }
                else if (evt.keyCode == KeyCode.LeftArrow && evt.modifiers.HasFlag(EventModifiers.Alt))
                {
                    m_CurrentSearchEvent.useFilterMenuShortcut = true;
                    PopFilterWindow();
                    evt.Use();
                }
                else if (evt.keyCode == KeyCode.KeypadEnter || evt.keyCode == KeyCode.Return)
                {
                    if (selectedIndex == -1 && m_FilteredItems.Count > 0)
                        selectedIndex = 0;

                    if (selectedIndex != -1)
                    {
                        var item = m_FilteredItems.ElementAt(selectedIndex);
                        if (item.provider.actions.Count > 0)
                        {
                            SearchAction action = item.provider.actions[0];
                            if (context.actionId != null)
                            {
                                action = SearchService.GetAction(item.provider, context.actionId);
                            }
                            else if (evt.modifiers.HasFlag(EventModifiers.Alt))
                            {
                                var actionIndex = 1;
                                if (evt.modifiers.HasFlag(EventModifiers.Control))
                                {
                                    actionIndex = 2;
                                    if (evt.modifiers.HasFlag(EventModifiers.Shift))
                                        actionIndex = 3;
                                }
                                action = item.provider.actions[Math.Max(0, Math.Min(actionIndex, item.provider.actions.Count - 1))];
                            }

                            if (action != null)
                            {
                                evt.Use();
                                m_CurrentSearchEvent.endSearchWithKeyboard = true;
                                ExecuteAction(action, selection.ToArray(), context);
                                GUIUtility.ExitGUI();
                            }
                        }
                    }
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    m_CurrentSearchEvent.endSearchWithKeyboard = true;
                    selectCallback?.Invoke(null, true);
                    evt.Use();
                    CloseSearchWindow();
                }
                else if (evt.keyCode == KeyCode.F1)
                {
                    SetSearchText("?");
                    evt.Use();
                }
                else if (ctrl && evt.keyCode == KeyCode.S)
                {
                    PopSearchQueryCreateWindow();
                    evt.Use();
                }
                else if (!EditorGUIUtility.editingTextField)
                    SearchField.Focus();

                var newSelection = m_Selection.Count == 0 ? k_ResetSelectionIndex : m_Selection.Last();
                if (selectedIndex != newSelection)
                    m_FocusSelectedItem = true;
            }

            if (m_FilteredItems.Count == 0)
                m_SearchBoxFocus = true;
        }

        private void CloseSearchWindow()
        {
            if (s_FocusedWindow)
                s_FocusedWindow.Focus();
            Close();
        }

        private bool IsDisplayGrid()
        {
            return m_ItemSize >= 32;
        }

        private void DrawHelpText()
        {
            const string help = "Search {0}!\r\n\r\n" +
                "- <b>Alt + Up/Down Arrow</b> \u2192 Search history\r\n" +
                "- <b>Alt + Left</b> \u2192 Filter\r\n" +
                "- <b>Alt + Right</b> \u2192 Actions menu\r\n" +
                "- <b>Enter</b> \u2192 Default action\r\n" +
                "- <b>Alt + Enter</b> \u2192 Secondary action\r\n" +
                "- Drag items around\r\n" +
                "- Type <b>?</b> to get help\r\n";

            if (String.IsNullOrEmpty(context.searchText.Trim()))
            {
                GUILayout.Box(string.Format(help, searchTopic), Styles.noResult, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            }
            else if (context.actionId != null)
            {
                GUILayout.Box("Waiting for a command...", Styles.noResult, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            }
            else
            {
                GUILayout.Box("No result for query \"" + context.searchText + "\"\n" + "Try something else?",
                              Styles.noResult, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            }
        }

        private void DrawItems(float sliderPos)
        {
            if (m_FilteredItems.Count > 0)
            {
                if (m_ResultView != null)
                    m_ResultView.Draw(m_Selection, sliderPos, ref m_FocusSelectedItem);
            }
            else
            {
                DrawHelpText();
            }
        }

        private void DrawDetailsViewSplitter()
        {
            var sliderRect = new Rect(m_DetailsViewSplitterPos - 2f, m_ResultView.rect.y, 3f, m_ResultView.rect.height);
            EditorGUIUtility.AddCursorRect(sliderRect, MouseCursor.ResizeHorizontal);

            if (Event.current.type == EventType.MouseDown && sliderRect.Contains(Event.current.mousePosition))
                m_DetailsViewSplitterResize = true;

            if (m_DetailsViewSplitterResize)
            {
                m_DetailsViewSplitterPos = Mathf.Min(Event.current.mousePosition.x, position.width-5f);
                Repaint();
            }

            if (Event.current.type == EventType.MouseUp)
                m_DetailsViewSplitterResize = false;
        }

        public void ExecuteAction(SearchAction action, SearchItem[] items, SearchContext context, bool endSearch = true)
        {
            var item = items.LastOrDefault();
            if (item == null)
                return;

            SendSearchEvent(item, action);
            EditorApplication.delayCall -= DelayTrackSelection;

            if (selectCallback != null)
            {
                selectCallback(item, false);
            }
            else
            {
                if (endSearch)
                    SearchField.UpdateLastSearchText(context.searchText);

                if (action.execute != null)
                    action.execute(context, items);
                else action.handler?.Invoke(item, context);
            }

            if (endSearch && action.closeWindowAfterExecution)
                CloseSearchWindow();
        }

        private Rect DrawToolbar(SearchContext context)
        {
            if (context == null)
                return Rect.zero;

            var searchTextRect = Rect.zero;
            using (new GUILayout.HorizontalScope(Styles.toolbar))
            {
                var rightRect = EditorGUILayout.GetControlRect(GUILayout.MaxWidth(32f), GUILayout.ExpandHeight(true));
                if (EditorGUI.DropdownButton(rightRect, Styles.filterButtonContent, FocusType.Passive, Styles.filterButton) || m_ShowFilterWindow)
                {
                    if (FilterWindow.canShow)
                    {
                        m_ShowFilterWindow = false;
                        rightRect.x += 12f; rightRect.y -= 3f;
                        if (FilterWindow.ShowAtPosition(this, context, rightRect))
                            GUIUtility.ExitGUI();
                    }
                }

                searchTextRect = GUILayoutUtility.GetRect(position.width, Styles.searchField.fixedHeight, Styles.searchField,
                    GUILayout.MaxWidth(position.width - Styles.kSearchFieldWidthOffset), GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

                var previousSearchText = context.searchText;
                context.searchText = SearchField.Draw(searchTextRect, context.searchText, Styles.searchField);

                if (String.IsNullOrEmpty(context.searchText))
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUI.TextArea(searchTextRect, $"search {searchTopic}...", Styles.placeholderTextStyle);
                    EditorGUI.EndDisabledGroup();
                }
                else
                {
                    DrawSearchQueryToolbar();

                    if (GUILayout.Button(Icons.clear, Styles.searchFieldBtn, GUILayout.Width(Styles.kSearchBoxBtnSize), GUILayout.Height(Styles.kSearchBoxBtnSize)))
                    {
                        SearchField.ResetAutoComplete();
                        context.searchText = "";
                        GUI.changed = true;
                        GUI.FocusControl(null);
                    }
                }

                if (String.Compare(previousSearchText, context.searchText, StringComparison.Ordinal) != 0 || m_FilteredItems.Count == 0)
                {
                    SetSelection();
                    DebouncedRefresh();
                }
            }

            return searchTextRect;
        }

        private void DrawSearchQueryToolbar()
        {
            var searchQueryRect = EditorGUILayout.GetControlRect(false, Styles.kSearchBoxBtnSize, Styles.searchFieldBtn, GUILayout.Width(Styles.kSearchBoxBtnSize));
            if (EditorGUI.DropdownButton(searchQueryRect, Styles.createSearchQueryContent, FocusType.Passive, Styles.searchFieldBtn) || m_ShowCreateWindow)
            {
                if (SearchQueryCreateWindow.canShow)
                {
                    searchQueryRect.x = searchQueryRect.x - SearchQueryCreateWindow.Styles.windowSize.x + searchQueryRect.width;
                    searchQueryRect.y += 8f;
                    m_ShowCreateWindow = false;
                    var screenRect = new Rect(GUIUtility.GUIToScreenPoint(searchQueryRect.position), searchQueryRect.size);
                    SearchQueryCreateWindow.ShowAtPosition(this, context, screenRect);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void DebouncedRefresh()
        {
            var currentTime = EditorApplication.timeSinceStartup;
            if (m_DebounceTime != 0 && currentTime - m_DebounceTime > 0.100)
            {
                Refresh();
                m_DebounceTime = 0;
            }
            else
            {
                if (m_DebounceTime == 0)
                    m_DebounceTime = currentTime;

                EditorApplication.delayCall -= DebouncedRefresh;
                EditorApplication.delayCall += DebouncedRefresh;
            }
        }

        public void ShowItemContextualMenu(SearchItem item, SearchContext context, Rect position)
        {
            var menu = new GenericMenu();
            var shortcutIndex = 0;
            var currentSelection = new [] { item };
            foreach (var action in item.provider.actions.Where(a => a.enabled(context, currentSelection)))
            {
                var itemName = action.content.tooltip;
                if (shortcutIndex == 0)
                {
                    itemName += " _enter";
                }
                else if (shortcutIndex == 1)
                {
                    itemName += " _&enter";
                }
                else if (shortcutIndex == 2)
                {
                    itemName += " _&%enter";
                }
                else if (shortcutIndex == 3)
                {
                    itemName += " _&%#enter";
                }
                menu.AddItem(new GUIContent(itemName, action.content.image), false, () => ExecuteAction(action, currentSelection, context));
                ++shortcutIndex;
            }

            if (position == default)
                menu.ShowAsContext();
            else
                menu.DropDown(position);
        }

        private static bool IsObjectMatchingType(SearchItem item, Type filterType)
        {
            if (item == SearchItem.none)
                return true;
            var obj = Utils.ToObject(item, filterType);
            if (!obj)
                return false;
            var objType = obj.GetType();
            return objType == filterType || obj.GetType().IsSubclassOf(filterType);
        }

        private static string GetSessionPrefKeyName(string prefix, SearchContext context)
        {
            if (context.filters.All(d => !d.isEnabled))
                return $"{SearchService.prefKey}.{prefix}.noscope";

            var scope = context.filters.Where(d => d.isEnabled && !d.provider.isExplicitProvider).Select(d => d.provider.filterId.GetHashCode()).Aggregate((h1, h2) => (h1 ^ h2).GetHashCode());
            return $"{SearchService.prefKey}.{prefix}.{scope}";
        }

        private static bool LoadFilters(SearchContext context, string prefKey)
        {
            var filtersStr = EditorPrefs.GetString(prefKey, null);
            try
            {
                context.ResetFilter(true);
                if (!string.IsNullOrEmpty(filtersStr))
                {
                    var filters = Utils.JsonDeserialize(filtersStr) as List<object>;
                    foreach (var filterObj in filters)
                    {
                        var filterJson = filterObj as Dictionary<string, object>;
                        if (filterJson == null)
                            continue;

                        var providerId = filterJson["providerId"] as string;
                        context.SetFilter(filterJson["isEnabled"].ToString() == "True", providerId);
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        private static void SaveFilters(SearchContext context, string prefKey)
        {
            var filters = SearchService.Providers.Where(p => p.active).Select(provider =>
            {
                var filterDict = new Dictionary<string, object>
                {
                    ["providerId"] = provider.name.id,
                    ["isEnabled"] = context.IsEnabled(provider.name.id)
                };
                return filterDict;
            }).ToList();

            var filterStr = Utils.JsonSerialize(filters);
            EditorPrefs.SetString(prefKey, filterStr);
        }

        private static void SaveSessionSetting(string key, string value, SearchContext context)
        {
            var prefKeyName = GetSessionPrefKeyName(key, context);
            #if QUICKSEARCH_DEBUG
            UnityEngine.Debug.Log($"Saving session setting {prefKeyName} with {value}");
            #endif
            EditorPrefs.SetString(prefKeyName, value);
        }

        private static string LoadSessionSetting(string key, SearchContext context, string defaultValue = default)
        {
            var prefKeyName = GetSessionPrefKeyName(key, context);
            var value = EditorPrefs.GetString(prefKeyName, defaultValue);
            #if QUICKSEARCH_DEBUG
            UnityEngine.Debug.Log($"Loading session setting {prefKeyName} with {value}");
            #endif
            return value;
        }

        private bool LoadSessionSettings()
        {
            var lastSearch = LoadSessionSetting(k_LastSearchPrefKey, context, String.Empty);
            if (context != null)
                context.searchText = lastSearch;
            return true;
        }

        private void SaveSessionSettings()
        {
            SaveSessionSetting(k_LastSearchPrefKey, context.searchText, context);
        }

        private void LoadGlobalSettings()
        {
            LoadFilters(context, k_FilterPrefKey);
        }

        private void SaveGlobalSettings()
        {
            if (saveFilters)
                SaveFilters(context, k_FilterPrefKey);
        }

        private void ResetPreferences()
        {
            EditorPrefs.SetString(k_FilterPrefKey, null);
            Refresh();
        }

        private void InitContextualProviders(IEnumerable<SearchProvider> providers)
        {
            saveFilters = false;
            SetFilteredProviders(providers);
            LoadSessionSettings();
        }

        private void UpdateItemSize(float value)
        {
            var oldMode = displayMode;
            m_ItemSize = SearchSettings.itemIconSize = value;
            var newMode = displayMode;
            if (m_ResultView == null || oldMode != newMode)
            {
                if (newMode == DisplayMode.List)
                    m_ResultView = new ListView(this);
                else if (newMode == DisplayMode.Grid)
                    m_ResultView = new GridView(this);
            }
        }

        [InitializeOnLoadMethod]
        private static void OpenQuickSearchFirstUse()
        {
            var quickSearchFirstUseTokenPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Library", "~quicksearch.new"));
            if (System.IO.File.Exists(quickSearchFirstUseTokenPath))
            {
                System.IO.File.Delete(quickSearchFirstUseTokenPath);
                EditorApplication.delayCall += OpenQuickSearch;
            }
        }

        [UsedImplicitly, CommandHandler(nameof(OpenQuickSearch))]
        private static void OpenQuickSearchCommand(CommandExecuteContext c)
        {
            OpenQuickSearch();
        }

        [UsedImplicitly, Shortcut("Help/Quick Search", KeyCode.O, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
        private static void OpenQuickSearch()
        {
            Open();
        }

        [Shortcut("Help/Quick Search Contextual", KeyCode.C, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
        private static void OpenContextual()
        {
            var qsWindow = Create();
            var contextualProviders = SearchService.Providers
                .Where(searchProvider => searchProvider.active && (searchProvider.isEnabledForContextualSearch?.Invoke() ?? false)).ToArray();
            if (contextualProviders.Length > 0)
                qsWindow.InitContextualProviders(contextualProviders);
            qsWindow.ShowWindow();
        }

        #if QUICKSEARCH_DEBUG
        [UsedImplicitly, MenuItem("Quick Search/Clear Preferences")]
        private static void ClearPreferences()
        {
            EditorPrefs.DeleteAll();
        }

        static void TestObjectPickerHandler(UnityEngine.Object obj, bool canceled)
        {
            if (obj && !canceled)
                Debug.Log($"Picked {obj}");
            else if (canceled)
                Debug.Log($"Pick canceled");
            else
                Debug.Log($"Picked nothing");
        }

        [MenuItem("Quick Search/Object Picker/Material")]
        static void TestObjectPickerMaterial()
        {
            ShowObjectPicker(TestObjectPickerHandler, null, "", "Material", typeof(Material));
        }

        [MenuItem("Quick Search/Object Picker/Game Object")]
        static void TestObjectPickerGameObject()
        {
            ShowObjectPicker(TestObjectPickerHandler, null, "", "GameObject", typeof(GameObject));
        }
        #endif
    }
}

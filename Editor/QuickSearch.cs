//#define QUICKSEARCH_DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
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
    ///     SearchService.LoadFilters(); // Set desire filters here...
    ///     QuickSearch.ShowWindow();
    /// }
    /// </example>
    public partial class QuickSearch : EditorWindow, ISearchView
    {
        const string packageName = "com.unity.quicksearch";
        const string documentationUrl = "https://docs.unity3d.com/Packages/com.unity.quicksearch@1.6/";

        internal static string packageFolderName = $"Packages/{packageName}";
        
        private static EditorWindow s_FocusedWindow;
        private static bool isDeveloperMode = Utils.IsDeveloperMode();

        private const int k_ResetSelectionIndex = -1;
        private const string k_QuickSearchBoxName = "QuickSearchBox";
        private const string s_Helpme = "Search {0}!\r\n\r\n" +
            "- <b>Alt + Up/Down Arrow</b> \u2192 Search history\r\n" +
            "- <b>Alt + Left</b> \u2192 Filter\r\n" +
            "- <b>Alt + Right</b> \u2192 Actions menu\r\n" +
            "- <b>Enter</b> \u2192 Default action\r\n" +
            "- <b>Alt + Enter</b> \u2192 Secondary action\r\n" +
            "- Drag items around\r\n" +
            "- Type <b>?</b> to get help\r\n";

        private static readonly SearchItem s_NoneItem = new SearchItem(Guid.NewGuid().ToString())
        {
            label = "None",
            description = "Clear the current value",
            score = int.MinValue,
            provider = new SearchProvider("none")
            {
                priority = int.MinValue,
                toObject = (item, type) => null,
                fetchThumbnail = (item, context) => Icons.clear,
                actions = new[] { new SearchAction("select", "select") }.ToList()
            }
        };

        [SerializeField] private Vector2 m_ScrollPosition;
        [SerializeField] private EditorWindow lastFocusedWindow;
        [SerializeField] private int m_SelectedIndex = k_ResetSelectionIndex;
        [SerializeField] private string m_SearchTopic = "anything";

        private event Action nextFrame;
        private SearchList m_FilteredItems;
        private bool m_FocusSelectedItem = false;
        private Rect m_ScrollViewOffset;
        private bool m_SearchBoxFocus;
        private int m_SearchBoxControlID = -1;
        private double m_ClickTime = 0;
        private bool m_CursorBlinking;
        private bool m_IsRepaintAfterTimeRequested = false;
        private double m_RequestRepaintAfterTime = 0;
        private double m_NextBlinkTime = 0;
        private bool m_PrepareDrag;
        private Vector3 m_DragStartPosition;
        private string m_CycledSearch;
        private bool m_ShowFilterWindow = false;
        private SearchAnalytics.SearchEvent m_CurrentSearchEvent;
        private double m_DebounceTime = 0.0;
        private float m_Height = 0;
        private GUIContent m_StatusLabelContent = new GUIContent();
        private int m_DelayedCurrentSelection = -1;
        private double m_LastPreviewStamp = 0;
        private Texture2D m_PreviewTexture;
        private int m_LastSelectedIndexDetails = -1;
        private float m_DrawItemsWidth = 0;
        private Rect m_ItemVisibleRegion = Rect.zero;
        private LinkedList<SearchItem> m_ItemPreviewCache = new LinkedList<SearchItem>();
        private Rect m_AutoCompleteRect;
        private bool m_DiscardAutoComplete = false;
        private bool m_AutoCompleting = false;
        private int m_AutoCompleteIndex = -1;
        private int m_AutoCompleteMaxIndex = 0;
        private string m_AutoCompleteLastInput;
        private List<string> m_CacheCheckList = null;

        private Action<SearchItem, bool> selectCallback;
        private Func<SearchItem, bool> filterCallback;
        private Action<SearchItem> trackingCallback;

        internal string searchTopic { get => m_SearchTopic; set => m_SearchTopic = value; }
        internal bool sendAnalyticsEvent { get; set; }
        internal static Rect contextualActionPosition { get; private set; }

        public SearchItem selection => IsSelectedItemValid() ? m_FilteredItems[m_SelectedIndex] : null;
        public SearchContext context { get; private set;}
        public IEnumerable<SearchItem> results => m_FilteredItems;
        public float itemIconSize { get; set; } = 128;
        public DisplayMode displayMode => IsDisplayGrid() ? DisplayMode.Grid : DisplayMode.List;
        
        /// <summary>
        /// Set the search text and refresh search results.
        /// </summary>
        /// <param name="searchText">New search text</param>
        public void SetSearchText(string searchText)
        {
            if (searchText == null)
                return;

            context.searchText = searchText;
            Refresh();

            var te = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), m_SearchBoxControlID);
            nextFrame += () => te.MoveLineEnd();
        }

        /// <summary>
        /// Open the Quick Search filter window to edit active filters.
        /// </summary>
        public void PopFilterWindow()
        {
            nextFrame += () => m_ShowFilterWindow = true;
        }

        /// <summary>
        /// Re-fetch the search results and refresh the UI.
        /// </summary>
        public void Refresh()
        {
            var foundItems = SearchService.GetItems(context);
            if (selectCallback != null)
                foundItems.Add(s_NoneItem);
            m_FilteredItems = filterCallback == null ? foundItems : foundItems.Where(item => filterCallback(item)).ToList();
            SetSelection(k_ResetSelectionIndex);

            EditorApplication.delayCall -= UpdateAsyncResults;
            EditorApplication.delayCall += UpdateAsyncResults;
        }

        /// <summary>
        /// Opens the Quick Search documentation page
        /// </summary>
        public static void OpenDocumentationUrl()
        {
            var uri = new Uri(documentationUrl);
            Process.Start(uri.AbsoluteUri);
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
        public static ISearchView OpenWithContextualProvider(string providerId)
        {
            var provider = SearchService.Providers.Find(p => p.name.id == providerId);
            if (provider == null)
            {
                Debug.LogWarning("Quick Search Cannot find search provider with id: " + providerId);
                SearchService.LoadFilters();
                return ShowWindow();
            }
            SearchService.Filter.ResetFilter(false, true);
            SearchService.Filter.SetFilter(true, providerId);
            var toolWindow = ShowWindow();
            toolWindow.searchTopic = provider.name.displayName.ToLower();
            toolWindow.UpdateWindowTitle();
            return toolWindow;
        }

        /// <summary>
        /// Open Quick Search using a global context.
        /// The context is discovered by asking each providers if the current context is suitable for them by calling `IsEnabledForContextualSearch`.
        /// </summary>
        [Shortcut("Help/Quick Search Contextual", KeyCode.C, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
        public static void OpenContextual()
        {
            var contextualProviders = SearchService.Providers
                .Where(searchProvider => searchProvider.active && searchProvider.isEnabledForContextualSearch != null && searchProvider.isEnabledForContextualSearch()).ToArray();
            if (contextualProviders.Length == 0)
            {
                OpenQuickSearch();
                return;
            }

            SearchService.Filter.ResetFilter(false, true);
            foreach (var searchProvider in contextualProviders)
                SearchService.Filter.SetFilter(true, searchProvider.name.id, null, true);

            ShowWindow();
        }

        /// <summary>
        /// Open the default Quick Search window using default settings.
        /// </summary>
        /// <param name="defaultWidth">Initial width of the window.</param>
        /// <param name="defaultHeight">Initial height of the window.</param>
        /// <returns>Returns the Quick Search editor window instance.</returns>
        public static QuickSearch ShowWindow(float defaultWidth = 850, float defaultHeight = 539)
        {
            s_FocusedWindow = focusedWindow;

            var windowSize = new Vector2(defaultWidth, defaultHeight);
            var qsWindow = CreateInstance<QuickSearch>();
            qsWindow.ShowDropDown(windowSize);

            qsWindow.searchTopic = "anything";
            qsWindow.sendAnalyticsEvent = true; // Ensure we won't send events while doing a domain reload.
            qsWindow.Focus();

            return qsWindow;
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
        public static ISearchView ShowObjectPicker(
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

            var qs = CreateInstance<QuickSearch>();
            qs.searchTopic = "object";
            qs.sendAnalyticsEvent = true;
            qs.titleContent.text = $"Select {filterType?.Name ?? typeName}...";
            qs.itemIconSize = 64;

            qs.filterCallback = (item) => IsObjectMatchingType(item ?? s_NoneItem, filterType);
            qs.selectCallback = (item, canceled) => selectHandler?.Invoke(Utils.ToObject(item, filterType), canceled);
            qs.trackingCallback = (item) => trackingHandler?.Invoke(Utils.ToObject(item, filterType));
            qs.context.wantsMore = true;
            qs.context.filterType = filterType;
            qs.SetSearchText(searchText);

            qs.ShowAuxWindow();
            qs.position = Utils.GetMainWindowCenteredPosition(new Vector2(defaultWidth, defaultHeight));
            qs.Focus();

            return qs;
        }

        [UsedImplicitly]
        internal void OnEnable()
        {
            m_SearchBoxFocus = true;
            m_CurrentSearchEvent = new SearchAnalytics.SearchEvent();

            context = new SearchContext { searchText = String.Empty, focusedWindow = lastFocusedWindow, searchView = this };
            SearchService.Enable(context, true);
            context.searchText = SearchService.LastSearch;

            itemIconSize = SearchSettings.itemIconSize;
            lastFocusedWindow = s_FocusedWindow;

            Refresh();
            UpdateWindowTitle();

            AsyncSearchSession.asyncItemReceived += OnAsyncItemsReceived;
        }

        [UsedImplicitly]
        internal void OnDisable()
        {
            EditorApplication.delayCall -= DebouncedRefresh;
            s_FocusedWindow = null;

            if (!isDeveloperMode)
                SendSearchEvent(null); // Track canceled searches

            SearchService.Disable(context);
            AsyncSearchSession.asyncItemReceived -= OnAsyncItemsReceived;

            Resources.UnloadUnusedAssets();
        }

        [UsedImplicitly]
        internal void OnGUI()
        {
            if (Event.current.type == EventType.Repaint)
            {
                nextFrame?.Invoke();
                nextFrame = null;
            }

            if (m_Height != position.height)
                OnResize();

            HandleKeyboardNavigation();

            EditorGUILayout.BeginVertical(Styles.panelBorder);
            {
                var rect = DrawToolbar(context);
                UpdateScrollAreaOffset();
                EditorGUILayout.BeginHorizontal();
                {
                    DrawItems(context);
                    if (Event.current.type == EventType.Repaint)
                        m_DrawItemsWidth = GUILayoutUtility.GetLastRect().width;
                    DrawDetails(context, m_SelectedIndex);
                }
                EditorGUILayout.EndHorizontal();
                DrawAutoCompletion(rect);
                DrawStatusBar();
            }
            EditorGUILayout.EndVertical();

            UpdateFocusControlState();
        }

        [UsedImplicitly]
        internal void OnResize()
        {
            if (m_Height > 0 && m_ScrollPosition.y > 0)
                m_ScrollPosition.y -= position.height - m_Height;
            m_Height = position.height;
        }

        [UsedImplicitly]
        internal void Update()
        {
            bool repaintRequested = false;
            var timeSinceStartup = EditorApplication.timeSinceStartup;
            if (m_IsRepaintAfterTimeRequested && m_RequestRepaintAfterTime <= EditorApplication.timeSinceStartup)
            {
                m_IsRepaintAfterTimeRequested = false;
                repaintRequested = true;
            }

            if (timeSinceStartup >= m_NextBlinkTime)
            {
                m_NextBlinkTime = timeSinceStartup + 0.5;
                m_CursorBlinking = !m_CursorBlinking;
                repaintRequested = true;
            }

            if (repaintRequested)
                Repaint();
        }

        private void OnAsyncItemsReceived(IEnumerable<SearchItem> items)
        {
            var filteredItems = items;
            if (filterCallback != null)
                filteredItems = filteredItems.Where(item => filterCallback(item));
            if (m_SelectedIndex == -1)
            {
                m_FilteredItems.AddItems(filteredItems);
            }
            else
            {
                m_FilteredItems.InsertRange(m_SelectedIndex + 1, filteredItems);
            }

            EditorApplication.delayCall -= UpdateAsyncResults;
            EditorApplication.delayCall += UpdateAsyncResults;
        }

        private void UpdateAsyncResults()
        {
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
                    SearchAnalytics.SendSearchEvent(m_CurrentSearchEvent);
            }

            // Prepare next search event
            m_CurrentSearchEvent = new SearchAnalytics.SearchEvent();
        }

        private void UpdateWindowTitle()
        {
            if (!titleContent.image)
                titleContent.image = Icons.quicksearch;
            if (m_FilteredItems == null || m_FilteredItems.Count == 0)
                titleContent.text = $"Search {m_SearchTopic}";
            else
            {
                var itemStr = m_FilteredItems.Count <= 1 ? "item" : "items";
                titleContent.text = $"Found {m_FilteredItems.Count} {itemStr}";
            }
        }

        private void DrawAutoCompletion(Rect rect)
        {
            if (m_DiscardAutoComplete || m_SearchBoxControlID <= 0)
                return;

            var te = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), m_SearchBoxControlID);
            var cursorPosition = te.cursorIndex;
            if (cursorPosition == 0)
                return;

            if (m_AutoCompleting && Event.current.type == EventType.MouseDown && !m_AutoCompleteRect.Contains(Event.current.mousePosition))
            {
                m_DiscardAutoComplete = true;
                m_AutoCompleting = false;
                return;
            }

            var searchText = context.searchText;
            var lastTokenStartPos = searchText.LastIndexOf(' ', Math.Max(0, te.cursorIndex - 1));
            var lastToken = lastTokenStartPos == -1 ? searchText : searchText.Substring(lastTokenStartPos + 1);
            var keywords = SearchService.GetKeywords(context, lastToken).Where(k => !k.Equals(lastToken, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (keywords.Length > 0)
            {
                const int maxAutoCompleteCount = 16;
                m_AutoCompleteMaxIndex = Math.Min(keywords.Length, maxAutoCompleteCount);
                if (!m_AutoCompleting)
                    m_AutoCompleteIndex = 0;

                if (Event.current.type == EventType.Repaint)
                {
                    var content = new GUIContent(context.searchText.Substring(0, context.searchText.Length - lastToken.Length));
                    var offset = Styles.searchField.CalcSize(content).x;
                    m_AutoCompleteRect = rect;
                    m_AutoCompleteRect.x += offset;
                    m_AutoCompleteRect.y = rect.yMax;
                    m_AutoCompleteRect.width = 250;
                    m_AutoCompleteRect.x = Math.Min(position.width - m_AutoCompleteRect.width - 25, m_AutoCompleteRect.x);
                }

                var autoFill = TextFieldAutoComplete(ref m_AutoCompleteRect, lastToken, keywords, maxAutoCompleteCount, 0.1f);
                if (autoFill == null)
                {
                    // No more results
                    m_AutoCompleting = false;
                    m_AutoCompleteIndex = -1;
                }
                else if (autoFill != lastToken)
                {
                    var regex = new Regex(Regex.Escape(lastToken), RegexOptions.IgnoreCase);
                    autoFill = regex.Replace(autoFill, "");
                    context.searchText = context.searchText.Insert(cursorPosition, autoFill);
                    Refresh();
                    nextFrame += () => 
                    {
                        m_AutoCompleting = false;
                        m_DiscardAutoComplete = true;
                        te.MoveToStartOfNextWord();
                    };
                }
                else
                    m_AutoCompleting = true;
            }
            else
            {
                m_AutoCompleting = false;
                m_AutoCompleteIndex = -1;
            }
        }

        private void DrawStatusBar()
        {
            var msg = "";
            if (context.isActionQuery)
                msg = "Action for ";

            IEnumerable<SearchProvider> providerList = SearchService.Filter.filteredProviders;
            if (SearchService.OverrideFilter.filteredProviders.Count > 0)
            {
                if (SearchService.OverrideFilter.filteredProviders.All(p => p.isExplicitProvider))
                {
                    if (msg.Length == 0)
                        msg = "Activate ";
                }
                else
                {
                    if (msg.Length == 0)
                        msg = "Searching only ";
                }

                providerList = SearchService.OverrideFilter.filteredProviders;
            }
            else
            {
                if (msg.Length == 0)
                    msg = "Searching ";
            }

            msg += Utils.FormatProviderList(providerList);

            if (m_FilteredItems != null && m_FilteredItems.Count > 0)
                msg += $" and found <b>{m_FilteredItems.Count}</b> results";

            m_StatusLabelContent.text = msg;
            m_StatusLabelContent.tooltip = Utils.FormatProviderList(providerList, true);

            GUILayout.BeginHorizontal();
            GUILayout.Label(m_StatusLabelContent, Styles.statusLabel, GUILayout.MaxWidth(position.width - 100));
            GUILayout.FlexibleSpace();

            EditorGUI.BeginChangeCheck();
            itemIconSize = GUILayout.HorizontalSlider(itemIconSize, 0f, 165f, 
                Styles.itemIconSizeSlider, Styles.itemIconSizeSliderThumb, GUILayout.Width(100f));
            if (EditorGUI.EndChangeCheck())
            {
                m_FocusSelectedItem = true;
                SearchSettings.itemIconSize = itemIconSize;
            }

            if (GUILayout.Button(SearchAnalytics.Version, Styles.versionLabel))
            {
                OpenDocumentationUrl();
            }

            if (AsyncSearchSession.SearchInProgress)
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

            GUILayout.EndHorizontal();
        }

        private bool IsItemValid(int index)
        {
            if (m_FilteredItems == null || index < 0 || index >= m_FilteredItems.Count)
                return false;
            return true;
        }

        private bool IsSelectedItemValid()
        {
            return IsItemValid(m_SelectedIndex);
        }

        private void DrawDetails(SearchContext context, int selectedIndex)
        {
            if (!IsItemValid(selectedIndex))
                return;

            var item = m_FilteredItems[selectedIndex];
            if (!item.provider.showDetails)
                return;

            var now = EditorApplication.timeSinceStartup;

            using (new EditorGUILayout.VerticalScope(GUILayout.Width(Styles.previewSize.x)))
            {
                if (item.provider.fetchPreview != null)
                {
                    if (now - m_LastPreviewStamp > 2.5)
                        m_PreviewTexture = null;

                    if (!m_PreviewTexture || m_LastSelectedIndexDetails != selectedIndex)
                    {
                        m_LastPreviewStamp = now;
                        m_PreviewTexture = item.provider.fetchPreview(item, context, Styles.previewSize, FetchPreviewOptions.Preview2D | FetchPreviewOptions.Large);
                        m_LastSelectedIndexDetails = selectedIndex;
                    }

                    if (m_PreviewTexture == null || AssetPreview.IsLoadingAssetPreviews())
                        Repaint();

                    GUILayout.Space(10);
                    GUILayout.Label(m_PreviewTexture, Styles.largePreview, GUILayout.MaxWidth(Styles.previewSize.x), GUILayout.MaxHeight(Styles.previewSize.y));
                }

                var description = SearchContent.FormatDescription(item, context, 2048);
                GUILayout.Label(description, Styles.previewDescription);

                GUILayout.Space(10);
                if (selectCallback == null)
                {
                    foreach (var action in item.provider.actions)
                    {
                        if (action == null || action.Id == "context" || action.content == null || action.handler == null)
                            continue;
                        if (GUILayout.Button(new GUIContent(action.DisplayName, action.content.image, action.content.tooltip), GUILayout.ExpandWidth(true)))
                        {
                            ExecuteAction(action, item, context, true);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                else  if (GUILayout.Button("Select", GUILayout.ExpandWidth(true)))
                {
                    selectCallback(item, false);
                    CloseSearchWindow();
                    GUIUtility.ExitGUI();
                }
            }
        }

        private int SetSelection(int selection)
        {
            if (m_FilteredItems == null)
                return -1;
            var previousSelection = m_SelectedIndex;
            m_SelectedIndex = Math.Max(-1, Math.Min(selection, m_FilteredItems.Count - 1));
            if (m_SelectedIndex == k_ResetSelectionIndex)
            {
                m_ScrollPosition.y = 0;
                m_DiscardAutoComplete = false;
            }
            if (previousSelection != m_SelectedIndex && m_SelectedIndex != -k_ResetSelectionIndex)
                TrackSelection(m_SelectedIndex);
            return m_SelectedIndex;
        }
        
        private void DelayTrackSelection()
        {
            if (m_FilteredItems == null || m_FilteredItems.Count == 0)
                return;

            if (m_DelayedCurrentSelection < 0 || m_DelayedCurrentSelection >= m_FilteredItems.Count)
                return;

            var selectedItem = m_FilteredItems[m_DelayedCurrentSelection];
            if (trackingCallback == null)
                selectedItem.provider?.trackSelection?.Invoke(selectedItem, context);
            else
                trackingCallback(selectedItem);

            m_DelayedCurrentSelection = -1;
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
                EditorGUI.FocusTextInControl(k_QuickSearchBoxName);
                m_SearchBoxFocus = false;
            }
        }

        private int GetItemCount()
        {
            if (m_FilteredItems == null)
                return 0;
            return m_FilteredItems.Count;
        }

        private int GetDisplayItemCount()
        {
            var itemCount = GetItemCount();
            var availableHeight = position.height - m_ScrollViewOffset.yMax - Styles.statusLabel.fixedHeight;
            return Math.Max(0, Math.Min(itemCount, (int)(availableHeight / Styles.itemRowHeight) + 2));
        }

        private void HandleKeyboardNavigation()
        {
            var evt = Event.current;
            if (evt.type == EventType.KeyDown)
            {
                var prev = m_SelectedIndex;
                if (evt.keyCode == KeyCode.DownArrow)
                {
                    if (m_AutoCompleting)
                    {
                        m_AutoCompleteIndex = SearchService.Wrap(m_AutoCompleteIndex + 1, m_AutoCompleteMaxIndex);
                        Event.current.Use();
                    }
                    else
                    {
                        if (m_SelectedIndex == -1 && evt.modifiers.HasFlag(EventModifiers.Alt))
                        {
                            m_CurrentSearchEvent.useHistoryShortcut = true;
                            m_CycledSearch = SearchService.CyclePreviousSearch(-1);
                            GUI.FocusControl(null);
                        }
                        else
                        {
                            SetSelection(m_SelectedIndex + 1);
                            Event.current.Use();
                        }
                    }
                }
                else if (evt.keyCode == KeyCode.UpArrow)
                {
                    if (m_AutoCompleting)
                    {
                        m_AutoCompleteIndex = SearchService.Wrap(m_AutoCompleteIndex - 1, m_AutoCompleteMaxIndex);
                        Event.current.Use();
                    }
                    else
                    {
                        if (m_SelectedIndex >= 0)
                        {
                            if (SetSelection(m_SelectedIndex - 1) == k_ResetSelectionIndex)
                                m_SearchBoxFocus = true;
                            Event.current.Use();
                        }
                        else if (evt.modifiers.HasFlag(EventModifiers.Alt))
                        {
                            m_CurrentSearchEvent.useHistoryShortcut = true;
                            m_CycledSearch = SearchService.CyclePreviousSearch(+1);
                            GUI.FocusControl(null);
                        }
                    }
                }
                else if (evt.keyCode == KeyCode.PageDown)
                {
                    SetSelection(m_SelectedIndex + GetDisplayItemCount() - 1);
                    Event.current.Use();
                }
                else if (evt.keyCode == KeyCode.PageUp)
                {
                    SetSelection(m_SelectedIndex - GetDisplayItemCount());
                    Event.current.Use();
                }
                else if (evt.keyCode == KeyCode.RightArrow && evt.modifiers.HasFlag(EventModifiers.Alt))
                {
                    m_CurrentSearchEvent.useActionMenuShortcut = true;
                    if (m_SelectedIndex != -1)
                    {
                        var item = m_FilteredItems.ElementAt(m_SelectedIndex);
                        var menuPositionY = (m_SelectedIndex+1) * Styles.itemRowHeight - m_ScrollPosition.y + Styles.itemRowHeight/2.0f;
                        contextualActionPosition = new Rect(position.width - Styles.actionButtonSize, menuPositionY, 1, 1);
                        ShowItemContextualMenu(item, context, contextualActionPosition);
                        Event.current.Use();
                    }
                }
                else if (evt.keyCode == KeyCode.LeftArrow && evt.modifiers.HasFlag(EventModifiers.Alt))
                {
                    m_CurrentSearchEvent.useFilterMenuShortcut = true;
                    PopFilterWindow();
                    Event.current.Use();
                }
                else if (evt.keyCode == KeyCode.KeypadEnter || evt.keyCode == KeyCode.Return)
                {
                    if (m_AutoCompleting && m_AutoCompleteIndex != -1)
                        return;

                    var selectedIndex = m_SelectedIndex;
                    if (selectedIndex == -1 && m_FilteredItems != null && m_FilteredItems.Count > 0)
                        selectedIndex = 0;

                    if (selectedIndex != -1 && m_FilteredItems != null)
                    {
                        var item = m_FilteredItems.ElementAt(selectedIndex);
                        if (item.provider.actions.Count > 0)
                        {
                            SearchAction action = item.provider.actions[0];
                            if (context.actionQueryId != null)
                            {
                                action = SearchService.GetAction(item.provider, context.actionQueryId);
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
                                Event.current.Use();
                                m_CurrentSearchEvent.endSearchWithKeyboard = true;
                                ExecuteAction(action, item, context);
                                GUIUtility.ExitGUI();
                            }
                        }
                    }
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    if (m_AutoCompleting)
                    {
                        m_DiscardAutoComplete = true;
                        Event.current.Use();
                    }
                    else
                    {
                        m_CurrentSearchEvent.endSearchWithKeyboard = true;
                        selectCallback?.Invoke(null, true);
                        CloseSearchWindow();
                        Event.current.Use();
                    }
                }
                else if (evt.keyCode == KeyCode.F1)
                {
                    SetSearchText("?");
                }
                else
                    GUI.FocusControl(k_QuickSearchBoxName);

                if (prev != m_SelectedIndex)
                {
                    m_FocusSelectedItem = true;
                    Repaint();
                }
            }

            if (m_FilteredItems == null || m_FilteredItems.Count == 0)
                m_SearchBoxFocus = true;
        }

        private void CloseSearchWindow()
        {
            if (s_FocusedWindow)
                s_FocusedWindow.Focus();
            Close();
        }
        
        private bool IsDragFinishedFarEnough(Event evt)
        {
            return evt.type == EventType.DragExited && Vector2.Distance(m_DragStartPosition, evt.mousePosition) < 3;
        }

        private Vector2 GetScrollViewOffsetedMousePosition()
        {
            return Event.current.mousePosition + new Vector2(m_ScrollViewOffset.x, m_ScrollViewOffset.height - m_ScrollPosition.y);
        }

        private void HandleMouseDown()
        {
            m_PrepareDrag = true;
            m_DragStartPosition = Event.current.mousePosition;

            if (m_AutoCompleting && !m_AutoCompleteRect.Contains(GetScrollViewOffsetedMousePosition()))
            {
                m_AutoCompleting = false;
                m_DiscardAutoComplete = true;
            }
        }

        private void HandleMouseUp(int clickedItemIndex, int itemTotalCount)
        {
            if (clickedItemIndex >= 0 && clickedItemIndex < itemTotalCount)
            {
                if (Event.current.button == 0)
                {
                    SetSelection(clickedItemIndex);

                    if ((EditorApplication.timeSinceStartup - m_ClickTime) < 0.2)
                    {
                        var item = m_FilteredItems.ElementAt(clickedItemIndex);
                        if (item.provider.actions.Count > 0)
                            ExecuteAction(item.provider.actions[0], item, context);
                        GUIUtility.ExitGUI();
                    }
                    EditorGUI.FocusTextInControl(k_QuickSearchBoxName);
                    Event.current.Use();
                    m_ClickTime = EditorApplication.timeSinceStartup;
                }
                else if (Event.current.button == 1)
                {
                    var item = m_FilteredItems.ElementAt(clickedItemIndex);
                    if (item.provider.actions.Count > 0)
                    {
                        var contextAction = item.provider.actions.Find(a => a.Id == SearchAction.kContextualMenuAction);
                        contextualActionPosition = new Rect(Event.current.mousePosition.x, Event.current.mousePosition.y, 1, 1);
                        if (contextAction != null)
                        {
                            const bool endSearch = false;
                            ExecuteAction(contextAction, item, context, endSearch);
                        }
                        else
                        {
                            ShowItemContextualMenu(item, context, contextualActionPosition);
                        }
                    }
                }
            }

            m_PrepareDrag = false;
            DragAndDrop.PrepareStartDrag(); // Reset drag content
        }

        private void HandleMouseDrag(int dragIndex, int itemTotalCount)
        {
            if (m_FilteredItems != null && dragIndex >= 0 && dragIndex < itemTotalCount)
            {
                var item = m_FilteredItems.ElementAt(dragIndex);
                if (item.provider?.startDrag != null)
                {
                    item.provider.startDrag(item, context);
                    m_PrepareDrag = false;

                    m_CurrentSearchEvent.useDragAndDrop = true;
                    SendSearchEvent(item);

                    Event.current.Use();
                    #if UNITY_EDITOR_OSX
                    CloseSearchWindow();
                    GUIUtility.ExitGUI();
                    #endif
                }
            }
        }

        private bool IsDisplayGrid()
        {
            return itemIconSize >= 32;
        }

        private void DrawHelpText()
        {
            if (String.IsNullOrEmpty(context.searchText.Trim()))
            {
                GUILayout.Box(string.Format(s_Helpme, m_SearchTopic), Styles.noResult, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            }
            else if (context.isActionQuery)
            {
                GUILayout.Box("Waiting for a command...", Styles.noResult, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            }
            else
            {
                GUILayout.Box("No result for query \"" + context.searchText + "\"\n" + "Try something else?",
                              Styles.noResult, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            }
        }

        private void DrawItems(SearchContext context)
        {
            context.totalItemCount = m_FilteredItems.Count;

            using (var scrollViewScope = new EditorGUILayout.ScrollViewScope(m_ScrollPosition))
            {
                m_ScrollPosition = scrollViewScope.scrollPosition;
                if (m_FilteredItems.Count > 0)
                {
                    if (IsDisplayGrid())
                        DrawGrid();
                    else
                        DrawList();
                }
                else
                {
                    DrawHelpText();
                }
            }
        }

        private void ExecuteAction(SearchAction action, SearchItem item, SearchContext context, bool endSearch = true)
        {
            SendSearchEvent(item, action);

            if (selectCallback != null)
            {
                selectCallback(item, false);
            }
            else
            {
                if (endSearch)
                {
                    SearchService.LastSearch = context.searchText;
                    SearchService.SetRecent(item);
                }
                action.handler(item, context);
            }

            if (endSearch && action.closeWindowAfterExecution)
                CloseSearchWindow();
        }

        private void UpdateScrollAreaOffset()
        {
            var rect = GUILayoutUtility.GetLastRect();
            if (rect.height > 1)
            {
                m_ScrollViewOffset = rect;
                m_ScrollViewOffset.height += Styles.statusOffset;
            }
        }

        private Rect DrawToolbar(SearchContext context)
        {
            if (context == null)
                return Rect.zero;

            Rect searchTextRect = Rect.zero;
            GUILayout.BeginHorizontal(Styles.toolbar);
            {
                var rightRect = EditorGUILayout.GetControlRect(GUILayout.MaxWidth(32f), GUILayout.ExpandHeight(true));
                if (EditorGUI.DropdownButton(rightRect, Styles.filterButtonContent, FocusType.Passive, Styles.filterButton) || m_ShowFilterWindow)
                {
                    if (FilterWindow.canShow)
                    {
                        rightRect.x += 12f; rightRect.y -= 3f;
                        if (m_ShowFilterWindow)
                            rightRect.y += 30f;

                        m_ShowFilterWindow = false;
                        if (FilterWindow.ShowAtPosition(this, rightRect))
                            GUIUtility.ExitGUI();
                    }
                }

                EditorGUI.BeginChangeCheck();

                var previousSearchText = context.searchText;
                using (new BlinkCursorScope(m_CursorBlinking, new Color(0, 0, 0, 0.01f)))
                {
                    var userSearchQuery = context.searchText;
                    if (!String.IsNullOrEmpty(m_CycledSearch) && (Event.current.type == EventType.Repaint || Event.current.type == EventType.Layout))
                    {
                        userSearchQuery = m_CycledSearch;
                        m_CycledSearch = null;
                        m_SearchBoxFocus = true;
                        GUI.changed = true;
                    }

                    GUI.SetNextControlName(k_QuickSearchBoxName);
                    context.searchText = GUILayout.TextField(userSearchQuery, Styles.searchField,
                        GUILayout.MaxWidth(position.width - 80), GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                    m_SearchBoxControlID = GUIUtility.keyboardControl;
                    searchTextRect = GUILayoutUtility.GetLastRect();
                }

                if (String.IsNullOrEmpty(context.searchText))
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUI.TextArea(GUILayoutUtility.GetLastRect(), $"search {m_SearchTopic}...", Styles.placeholderTextStyle);
                    EditorGUI.EndDisabledGroup();
                }
                if (!String.IsNullOrEmpty(context.searchText))
                {
                    if (GUILayout.Button(Icons.clear, Styles.searchFieldClear, GUILayout.Width(24), GUILayout.Height(24)))
                    {
                        m_DiscardAutoComplete = false;
                        context.searchText = "";
                        GUI.changed = true;
                        GUI.FocusControl(null);
                    }
                }

                if (String.Compare(previousSearchText, context.searchText, StringComparison.Ordinal) != 0 || m_FilteredItems == null)
                {
                    SetSelection(k_ResetSelectionIndex);
                    DebouncedRefresh();
                }

                #if QUICKSEARCH_DEBUG
                DrawDebugTools();
                #endif
            }
            GUILayout.EndHorizontal();

            return searchTextRect;
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

                #if QUICKSEARCH_DEBUG
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "Debouncing {0}", m_Context.searchText);
                #endif
                EditorApplication.delayCall -= DebouncedRefresh;
                EditorApplication.delayCall += DebouncedRefresh;
            }
        }

        private void ShowItemContextualMenu(SearchItem item, SearchContext context, Rect position = default)
        {
            var menu = new GenericMenu();
            int shortcutIndex = 0;
            foreach (var action in item.provider.actions)
            {
                if (action.Id == "context")
                    continue;

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
                menu.AddItem(new GUIContent(itemName, action.content.image), false, () => ExecuteAction(action, item, context));
                ++shortcutIndex;
            }

            if (position == default)
                menu.ShowAsContext();
            else
                menu.DropDown(position);
        }

        private string TextFieldAutoComplete(ref Rect area, string input, string[] source, int maxShownCount = 5, float levenshteinDistance = 0.5f)
        {
            if (input.Length <= 0)
                return input;

            string rst = input;
            if (m_AutoCompleteLastInput != input)
            {
                // Update cache
                m_AutoCompleteLastInput = input;

                List<string> uniqueSrc = new List<string>(new HashSet<string>(source));
                int srcCnt = uniqueSrc.Count;
                m_CacheCheckList = new List<string>(System.Math.Min(maxShownCount, srcCnt)); // optimize memory alloc

                // Start with - slow
                for (int i = 0; i < srcCnt && m_CacheCheckList.Count < maxShownCount; i++)
                {
                    if (uniqueSrc[i].StartsWith(input, StringComparison.OrdinalIgnoreCase))
                    {
                        m_CacheCheckList.Add(uniqueSrc[i]);
                        uniqueSrc.RemoveAt(i);
                        srcCnt--;
                        i--;
                    }
                }

                // Contains - very slow
                if (m_CacheCheckList.Count == 0)
                {
                    for (int i = 0; i < srcCnt && m_CacheCheckList.Count < maxShownCount; i++)
                    {
                        if (uniqueSrc[i].IndexOf(input, StringComparison.OrdinalIgnoreCase) != -1)
                        {
                            m_CacheCheckList.Add(uniqueSrc[i]);
                            uniqueSrc.RemoveAt(i);
                            srcCnt--;
                            i--;
                        }
                    }
                }

                // Levenshtein Distance - very very slow.
                if (levenshteinDistance > 0f && // only developer request
                    input.Length > 3 && // 3 characters on input, hidden value to avoid doing too early.
                    m_CacheCheckList.Count < maxShownCount) // have some empty space for matching.
                {
                    levenshteinDistance = Mathf.Clamp01(levenshteinDistance);
                    for (int i = 0; i < srcCnt && m_CacheCheckList.Count < maxShownCount; i++)
                    {
                        int distance = Utils.LevenshteinDistance(uniqueSrc[i], input, caseSensitive: false);
                        bool closeEnough = (int)(levenshteinDistance * uniqueSrc[i].Length) > distance;
                        if (closeEnough)
                        {
                            m_CacheCheckList.Add(uniqueSrc[i]);
                            uniqueSrc.RemoveAt(i);
                            srcCnt--;
                            i--;
                        }
                    }
                }
            }

            if (m_CacheCheckList.Count == 0)
                return null;

            // Draw recommend keyword(s)
            if (m_CacheCheckList.Count > 0)
            {
                int cnt = m_CacheCheckList.Count;
                float height = cnt * EditorStyles.toolbarDropDown.fixedHeight;
                area = new Rect(area.x, area.y, area.width, height);
                GUI.depth -= 10;
                GUI.BeginClip(area);
                Rect line = new Rect(0, 0, area.width, EditorStyles.toolbarDropDown.fixedHeight);

                for (int i = 0; i < cnt; i++)
                {
                    var selected = i == m_AutoCompleteIndex;
                    if (selected)
                    {
                        if (Event.current.type == EventType.KeyUp && Event.current.keyCode == KeyCode.Return)
                        {
                            Event.current.Use();
                            GUI.changed = true;
                            return m_CacheCheckList[i];
                        }
                        GUI.DrawTexture(line, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, false, 0, Styles.textAutoCompleteSelectedColor, 0f, 1.0f);
                    }
                    else
                    {
                        GUI.DrawTexture(line, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, false, 0, Styles.textAutoCompleteBgColor, 0f, 1.0f);
                    }

                    if (GUI.Button(line, m_CacheCheckList[i], selected ? Styles.selectedItemLabel : Styles.itemLabel))
                    {
                        rst = m_CacheCheckList[i];
                        GUI.changed = true;
                    }

                    line.y += line.height;
                }
                GUI.EndClip();
                GUI.depth += 10;
            }
            return rst;
        }
        
        private static bool IsObjectMatchingType(SearchItem item, Type filterType)
        {
            if (item == s_NoneItem)
                return true;
            var obj = Utils.ToObject(item, filterType);
            if (!obj)
                return false;
            var objType = obj.GetType();
            return objType == filterType || obj.GetType().IsSubclassOf(filterType);
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
            SearchService.LoadFilters();
            ShowWindow();
        }

        #if QUICKSEARCH_DEBUG
        [UsedImplicitly, MenuItem("Quick Search/Clear Preferences")]
        private static void ClearPreferences()
        {
            EditorPrefs.DeleteAll();
        }

        private void DrawDebugTools()
        {
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
            {
                SearchService.Refresh();
                Refresh();
            }
            if (GUILayout.Button("Save", EditorStyles.toolbarButton))
            {
                SearchService.SaveGlobalSettings();
            }
            if (GUILayout.Button("Reset", EditorStyles.toolbarButton))
            {
                SearchService.Reset();
                Refresh();
            }
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

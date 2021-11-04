using System;
using UnityEngine;

#if USE_SEARCH_MODULE
using UnityEditor.ShortcutManagement;
#endif

namespace UnityEditor.Search
{
    static class Styles
    {
        static Styles()
        {
            if (!isDarkTheme)
            {
                selectedItemLabel.normal.textColor = Color.white;
                selectedItemDescription.normal.textColor = Color.white;
            }

            statusWheel = new GUIContent[12];
            for (int i = 0; i < 12; i++)
                statusWheel[i] = EditorGUIUtility.IconContent("WaitSpin" + i.ToString("00"));

            #if USE_SEARCH_MODULE
            var syncShortcut = ShortcutManager.instance.GetShortcutBinding(QuickSearch.k_TogleSyncShortcutName);
            var tooltip = $"Synchronize search fields ({syncShortcut})";
            syncSearchButtonContent = new GUIContent(string.Empty, EditorGUIUtility.LoadIcon("QuickSearch/SyncSearch"), tooltip);
            syncSearchOnButtonContent = new GUIContent(string.Empty, EditorGUIUtility.LoadIcon("QuickSearch/SyncSearch On"), tooltip);
            #endif
        }

        private const int itemRowPadding = 4;
        public const float actionButtonSize = 16f;
        public const float itemPreviewSize = 32f;
        public const float descriptionPadding = 2f;
        public const float itemRowHeight = itemPreviewSize + itemRowPadding * 2f;

        private static bool isDarkTheme => EditorGUIUtility.isProSkin;

        private static readonly RectOffset marginNone = new RectOffset(0, 0, 0, 0);
        private static readonly RectOffset paddingNone = new RectOffset(0, 0, 0, 0);
        private static readonly RectOffset defaultPadding = new RectOffset(itemRowPadding, itemRowPadding, itemRowPadding, itemRowPadding);

        public static readonly string highlightedTextColorFormat = isDarkTheme ? "<color=#F6B93F>{0}</color>" : "<b>{0}</b>";
        public static readonly string tabCountTextColorFormat = isDarkTheme ? "<color=#7B7B7B>{0}</color>" : "<color=#6A6A6A>{0}</color>";

        public static readonly GUIStyle panelBorder = new GUIStyle("grey_border")
        {
            name = "quick-search-border",
            padding = new RectOffset(1, 1, 1, 1),
            margin = new RectOffset(0, 0, 0, 0)
        };

        public static readonly GUIStyle autoCompleteBackground = new GUIStyle("grey_border")
        {
            name = "quick-search-auto-complete-background",
            padding = new RectOffset(1, 1, 1, 1),
            margin = new RectOffset(0, 0, 0, 0)
        };

        public static readonly GUIContent moreActionsContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, "Open actions menu", Icons.more);
        public static readonly GUIContent moreProviderFiltersContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, "Display search provider filter ids and toggle their activate state.", Icons.more);
        public static readonly GUIContent resetSearchColumnsContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, "Reset search result columns.", Utils.LoadIcon("Refresh"));

        public static readonly GUIStyle scrollbar = new GUIStyle("VerticalScrollbar");
        public static readonly float scrollbarWidth = scrollbar.fixedWidth + scrollbar.margin.horizontal;

        public static readonly GUIStyle itemBackground1 = new GUIStyle
        {
            name = "quick-search-item-background1",
            fixedHeight = 0,

            margin = marginNone,
            padding = defaultPadding
        };

        public static readonly GUIStyle itemBackground2 = new GUIStyle(itemBackground1) { name = "quick-search-item-background2" };
        public static readonly GUIStyle selectedItemBackground = new GUIStyle(itemBackground1) { name = "quick-search-item-selected-background" };

        public static readonly GUIStyle gridItemBackground = new GUIStyle()
        {
            name = "quick-search-grid-item-background",
            alignment = TextAnchor.MiddleCenter,
            imagePosition = ImagePosition.ImageOnly
        };

        public static readonly GUIStyle gridItemLabel = new GUIStyle("ProjectBrowserGridLabel")
        {
            wordWrap = true,
            fixedWidth = 0,
            fixedHeight = 0,
            alignment = TextAnchor.MiddleCenter,
            margin = marginNone,
            padding = new RectOffset(2, 1, 1, 1)
        };

        public static readonly GUIStyle itemGridBackground1 = new GUIStyle(itemBackground1) { fixedHeight = 0, };
        public static readonly GUIStyle itemGridBackground2 = new GUIStyle(itemBackground2) { fixedHeight = 0 };

        public static readonly GUIStyle preview = new GUIStyle
        {
            name = "quick-search-item-preview",
            fixedWidth = 0,
            fixedHeight = 0,
            alignment = TextAnchor.MiddleCenter,
            imagePosition = ImagePosition.ImageOnly,
            margin = new RectOffset(8, 2, 2, 2),
            padding = paddingNone
        };

        public static readonly Vector2 previewSize = new Vector2(256, 256);
        public static readonly GUIStyle largePreview = new GUIStyle
        {
            name = "quick-search-item-large-preview",
            alignment = TextAnchor.MiddleCenter,
            imagePosition = ImagePosition.ImageOnly,
            margin = new RectOffset(8, 8, 2, 2),
            padding = paddingNone,
            stretchWidth = true,
            stretchHeight = true
        };

        public static readonly GUIStyle itemLabel = new GUIStyle(EditorStyles.label)
        {
            name = "quick-search-item-label",
            richText = true,
            wordWrap = false,
            margin = new RectOffset(8, 4, 4, 2),
            padding = paddingNone
        };

        public static readonly GUIStyle itemLabelLeftAligned = new GUIStyle(itemLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(2, 2, 0, 0)
        };
        public static readonly GUIStyle itemLabelCenterAligned = new GUIStyle(itemLabelLeftAligned) { alignment = TextAnchor.MiddleCenter };
        public static readonly GUIStyle itemLabelrightAligned = new GUIStyle(itemLabelLeftAligned) { alignment = TextAnchor.MiddleRight };

        public static readonly GUIStyle itemLabelCompact = new GUIStyle(itemLabel)
        {
            name = "quick-search-item-compact-label",
            margin = new RectOffset(8, 4, 2, 2)
        };

        public static readonly GUIStyle itemLabelGrid = new GUIStyle(itemLabel)
        {
            fontSize = itemLabel.fontSize - 1,
            wordWrap = true,
            alignment = TextAnchor.UpperCenter,
            margin = marginNone,
            padding = new RectOffset(1, 1, 1, 1)
        };

        public static readonly GUIStyle selectedItemLabel = new GUIStyle(itemLabel)
        {
            name = "quick-search-item-selected-label",
            padding = paddingNone
        };

        public static readonly GUIStyle selectedItemLabelCompact = new GUIStyle(selectedItemLabel)
        {
            name = "quick-search-item-selected-compact-label",
            margin = new RectOffset(8, 4, 2, 2)
        };

        public static readonly GUIStyle autoCompleteItemLabel = new GUIStyle(EditorStyles.label)
        {
            richText = true,
            name = "quick-search-auto-complete-item-label",
            fixedHeight = EditorStyles.toolbarButton.fixedHeight,
            padding = new RectOffset(9, 10, 0, 1)
        };

        public static readonly GUIStyle autoCompleteSelectedItemLabel = new GUIStyle(autoCompleteItemLabel)
        {
            name = "quick-search-auto-complete-item-selected-label"
        };

        public static readonly GUIStyle autoCompleteTooltip = new GUIStyle(EditorStyles.label)
        {
            richText = true,
            alignment = TextAnchor.MiddleRight,
            padding = new RectOffset(2, 6, 0, 2)
        };

        public static readonly GUIStyle noResult = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
        {
            name = "quick-search-no-result",
            fontSize = 20,
            fixedHeight = 0,
            fixedWidth = 0,
            wordWrap = true,
            richText = true,
            alignment = TextAnchor.MiddleCenter,
            margin = marginNone,
            padding = paddingNone
        };

        public static readonly GUIStyle itemDescription = new GUIStyle(EditorStyles.label)
        {
            name = "quick-search-item-description",
            richText = true,
            wordWrap = false,
            margin = new RectOffset(4, 4, 1, 4),
            padding = paddingNone,
            fontSize = Math.Max(9, itemLabel.fontSize - 2)
        };

        public static readonly GUIStyle previewDescription = new GUIStyle(itemDescription)
        {
            wordWrap = true,
            margin = new RectOffset(4, 4, 10, 4),
            padding = new RectOffset(4, 4, 4, 4),
            fontSize = Math.Max(11, itemLabel.fontSize),
            alignment = TextAnchor.MiddleLeft
        };

        public static readonly GUIStyle statusLabel = new GUIStyle(itemDescription)
        {
            name = "quick-search-status-label",
            margin = new RectOffset(4, 4, 1, 1),
            fontSize = Math.Max(9, itemLabel.fontSize - 1),
            clipping = TextClipping.Clip,
            imagePosition = ImagePosition.TextOnly
        };

        public static readonly GUIStyle selectedItemDescription = new GUIStyle(itemDescription)
        {
            name = "quick-search-item-selected-description"
        };

        public static readonly GUIStyle actionButton = new GUIStyle("IconButton")
        {
            name = "quick-search-action-button",

            fixedWidth = actionButtonSize,
            fixedHeight = actionButtonSize,

            imagePosition = ImagePosition.ImageOnly,
            alignment = TextAnchor.MiddleCenter,

            margin = new RectOffset(4, 4, 4, 4),
            padding = paddingNone
        };

        public static readonly GUIStyle tabMoreButton = new GUIStyle(actionButton)
        {
            margin = new RectOffset(4, 4, 6, 0)
        };

        public static readonly GUIStyle tabButton = new GUIStyle("IconButton")
        {
            margin = new RectOffset(4, 4, 6, 0),
            padding = paddingNone,
            fixedWidth = actionButtonSize,
            fixedHeight = actionButtonSize,
            imagePosition = ImagePosition.ImageOnly,
            alignment = TextAnchor.MiddleCenter
        };

        public static readonly GUIStyle actionButtonHovered = new GUIStyle(actionButton)
        {
            name = "quick-search-action-button-hovered"
        };

        private static readonly GUIStyleState clear = new GUIStyleState()
        {
            background = null,
            scaledBackgrounds = new Texture2D[] { null },
            textColor = isDarkTheme ? new Color(210 / 255f, 210 / 255f, 210 / 255f) : Color.black
        };

        public static readonly GUIStyle toolbar = new GUIStyle("Toolbar")
        {
            name = "quick-search-bar",
            margin = new RectOffset(0, 0, 0, 0),
            padding = new RectOffset(4, 8, 4, 4),
            border = new RectOffset(0, 0, 0, 0),
            fixedHeight = 0f
        };

        public static readonly GUIStyle queryBuilderToolbar = new GUIStyle("Toolbar")
        {
            margin = new RectOffset(4, 4, 0, 4),
            padding = new RectOffset(4, 8, 4, 4),
            border = new RectOffset(0, 0, 0, 0),
            fixedHeight = 0f
        };


        const int k_SearchFieldFontSize = 15;

        public static readonly GUIStyle searchField = new GUIStyle("ToolbarSeachTextFieldPopup")
        {
            name = "quick-search-search-field",
            wordWrap = true,
            fontSize = k_SearchFieldFontSize,
            fixedHeight = 0f,
            fixedWidth = 0f,
            alignment = TextAnchor.MiddleLeft,
            margin = new RectOffset(4, 4, 4, 4),
            padding = new RectOffset(8, 20, 0, 0),
            border = new RectOffset(0, 0, 0, 0),
            normal = clear,
            focused = clear,
            hover = clear,
            active = clear,
            onNormal = clear,
            onHover = clear,
            onFocused = clear,
            onActive = clear,
        };

        public static readonly GUIStyle queryBuilderSearchField = new GUIStyle()
        {
            wordWrap = false,
            fontSize = k_SearchFieldFontSize,
            fixedHeight = 0f,
            fixedWidth = 0f,
            alignment = TextAnchor.MiddleLeft,
            margin = new RectOffset(0, 0, 0, 0),
            padding = new RectOffset(2, 2, 0, 0),
            border = new RectOffset(0, 0, 0, 0),
            normal = clear,
            focused = clear,
            hover = clear,
            active = clear,
            onNormal = clear,
            onHover = clear,
            onFocused = clear,
            onActive = clear,
        };

        public static readonly GUIStyle placeholderTextStyle = new GUIStyle(searchField)
        {
            name = "quick-search-search-field-placeholder",
            fontSize = k_SearchFieldFontSize,
            padding = new RectOffset(0, 0, 0, 0),
            alignment = TextAnchor.MiddleCenter,
            normal = clear,
            focused = clear,
            hover = clear,
            active = clear,
            onNormal = clear,
            onHover = clear,
            onFocused = clear,
            onActive = clear
        };

        public static readonly GUIStyle searchFieldBtn = new GUIStyle()
        {
            name = "quick-search-search-field-clear",
            richText = false,
            fixedHeight = 0,
            fixedWidth = 0,
            margin = new RectOffset(0, 4, 0, 0),
            padding = new RectOffset(0, 0, 0, 0),
            normal = clear,
            focused = clear,
            hover = clear,
            active = clear,
            onNormal = clear,
            onHover = clear,
            onFocused = clear,
            onActive = clear,
            alignment = TextAnchor.MiddleRight,
            imagePosition = ImagePosition.ImageOnly
        };

        public static readonly GUIStyle searchFieldTabToFilterBtn = new GUIStyle()
        {
            richText = true,
            fontSize = k_SearchFieldFontSize,
            fixedHeight = 0,
            fixedWidth = 0,
            margin = new RectOffset(0, 25, 0, 0),
            padding = new RectOffset(0, 0, 0, 2),
            normal = clear,
            focused = clear,
            hover = clear,
            active = clear,
            onNormal = clear,
            onHover = clear,
            onFocused = clear,
            onActive = clear,
            alignment = TextAnchor.MiddleRight,
            imagePosition = ImagePosition.TextOnly
        };

        public static readonly GUIContent searchFavoriteButtonContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, "Mark as Favorite", EditorGUIUtility.FindTexture("Favorite Icon"));
        public static readonly GUIContent searchFavoriteOnButtonContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, "Remove as Favorite", EditorGUIUtility.FindTexture("Favorite On Icon"));
        public static readonly GUIContent saveQueryButtonContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, "Save search query as an asset.", EditorGUIUtility.FindTexture("SaveAs"));
        public static readonly GUIContent previewInspectorContent = EditorGUIUtility.TrTextContentWithIcon("Inspector", "Open Inspector (F4)", EditorGUIUtility.FindTexture("UnityEditor.InspectorWindow"));
        public static readonly GUIContent previewInspectorButtonContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, "Open Inspector (F4)", EditorGUIUtility.FindTexture("UnityEditor.InspectorWindow"));
        public static readonly GUIContent sortButtonContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, "Change Searches sorting order", EditorGUIUtility.FindTexture("UnityEditor/Filter Icon"));
        public static readonly GUIContent saveSearchesContent = EditorGUIUtility.TrTextContent("Searches");
        public static readonly GUIContent toggleSavedSearchesTextfieldContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, "Filter Searches", Icons.quickSearchWindow);

        #if USE_SEARCH_MODULE
        public static readonly GUIContent syncSearchButtonContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, "Synchronize search fields (Ctrl + K)", EditorGUIUtility.LoadIcon("QuickSearch/SyncSearch"));
        public static readonly GUIContent syncSearchOnButtonContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, "Synchronize search fields (Ctrl + K)", EditorGUIUtility.LoadIcon("QuickSearch/SyncSearch On"));
        public static readonly GUIContent syncSearchAllGroupTabContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, "Choose a specific search tab (eg. Project) to enable synchronization.", EditorGUIUtility.LoadIcon("QuickSearch/SyncSearch"));
        public static readonly GUIContent syncSearchProviderNotSupportedContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, "Search provider doesn't support synchronization", EditorGUIUtility.LoadIcon("QuickSearch/SyncSearch"));
        public static readonly GUIContent syncSearchViewNotEnabledContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, "Search provider uses a search engine\nthat cannot be synchronized.\nSee Preferences -> Search.", EditorGUIUtility.LoadIcon("QuickSearch/SyncSearch"));
		#if !USE_QUERY_BUILDER
        public static readonly GUIContent searchTipsHelp = EditorGUIUtility.TrTextContentWithIcon("Type '?' for help", EditorGUIUtility.LoadIcon("QuickSearch/Help"));
		#endif
        public static readonly GUIContent searchTipsDrag = EditorGUIUtility.TrTextContentWithIcon("Drag from search results to Scene, Hierarchy or Inspector", EditorGUIUtility.LoadIcon("QuickSearch/DragArrow"));
        public static readonly GUIContent searchTipsSaveSearches = EditorGUIUtility.TrTextContentWithIcon("Save Searches you use often", EditorGUIUtility.FindTexture("SaveAs"));
        public static readonly GUIContent searchTipsPreviewInspector = EditorGUIUtility.TrTextContentWithIcon("Enable the Preview Inspector to edit search results in place", EditorGUIUtility.LoadIcon("UnityEditor.InspectorWindow"));
        public static readonly GUIContent searchTipsSync = EditorGUIUtility.TrTextContentWithIcon("Enable sync to keep other Editor search fields populated ", EditorGUIUtility.LoadIcon("QuickSearch/SyncSearch On"));
        public static readonly GUIContent saveSearchesIconContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, Utils.LoadIcon("UnityEditor/Search/SearchQueryAsset Icon"));
        public static readonly GUIContent openSaveSearchesIconContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, "Open Saved Searches Panel (F3)", Utils.LoadIcon("UnityEditor/Search/SearchQueryAsset Icon"));
        public static readonly GUIContent queryBuilderIconContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, "Toggle Query Builder Mode (F1)", Utils.LoadIcon("Assembly Icon"));
        #else
		#if !USE_QUERY_BUILDER
        public static readonly GUIContent searchTipsHelp = EditorGUIUtility.TrTextContent("Type '?' for help");
		#endif
        public static readonly GUIContent searchTipsDrag = EditorGUIUtility.TrTextContent("Drag from search results to Scene, Hierarchy or Inspector");
        public static readonly GUIContent searchTipsSaveSearches = EditorGUIUtility.TrTextContent("Save Searches you use often");
        public static readonly GUIContent searchTipsPreviewInspector = EditorGUIUtility.TrTextContent("Enable the Preview Inspector to edit search results in place");
        public static readonly GUIContent saveSearchesIconContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, Icons.showPanels);
        public static readonly GUIContent openSaveSearchesIconContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, "Open Saved Searches Panel (F3)", Icons.showPanels);
        #endif

        public static readonly GUIContent[] searchTipIcons =
        {
            #if USE_SEARCH_MODULE
            new GUIContent(string.Empty, EditorGUIUtility.LoadIcon("QuickSearch/Help")),
            new GUIContent(string.Empty, EditorGUIUtility.LoadIcon("QuickSearch/DragArrow")),
            new GUIContent(string.Empty, EditorGUIUtility.FindTexture("SaveAs")),
            new GUIContent(string.Empty, EditorGUIUtility.LoadIcon("UnityEditor.InspectorWindow")),
            new GUIContent(string.Empty, EditorGUIUtility.LoadIcon("QuickSearch/SyncSearch On"))
            #else
            new GUIContent(string.Empty, Icons.quicksearchHelp),
            new GUIContent(string.Empty, Icons.dragArrow),
            new GUIContent(string.Empty, EditorGUIUtility.FindTexture("SaveAs")),
            new GUIContent(string.Empty, EditorGUIUtility.FindTexture("UnityEditor.InspectorWindow")),
            #endif
        };

        public static readonly GUIContent[] searchTipLabels =
        {
			#if !USE_QUERY_BUILDER
            new GUIContent(L10n.Tr("Type '?' for help")),
			#endif
            new GUIContent(L10n.Tr("Drag from search results to Scene, Hierarchy or Inspector")),
            new GUIContent(L10n.Tr("Save Searches you use often")),
            new GUIContent(L10n.Tr("Enable the Preview Inspector to edit search results in place")),
            new GUIContent(L10n.Tr("Enable sync to keep other Editor search fields populated"))
        };

        public static readonly GUIStyle tipIcon = new GUIStyle("Label")
        {
            margin = new RectOffset(4, 4, 2, 2),
            stretchWidth = false
        };
        public static readonly GUIStyle tipText = new GUIStyle("Label")
        {
            richText = true,
            wordWrap = false
        };

        public const float tipSizeOffset = 100f;
        public const float tipMaxSize = 335f;

        public static readonly GUIStyle tipsSection = Utils.FromUSS("quick-search-tips-section");

        public static readonly GUIStyle statusError = new GUIStyle("CN StatusError") { padding = new RectOffset(2, 2, 1, 1) };
        public static readonly GUIStyle statusWarning = new GUIStyle("CN StatusWarn") { padding = new RectOffset(2, 2, 1, 1) };

        public static readonly GUIStyle toolbarButton = new GUIStyle("IconButton")
        {
            margin = new RectOffset(4, 4, (int)SearchField.textTopBottomPadding, (int)SearchField.textTopBottomPadding),
            padding = new RectOffset(0, 0, 0, 0),
            fixedWidth = 24f,
            fixedHeight = 24f,
            imagePosition = ImagePosition.ImageOnly,
            alignment = TextAnchor.MiddleCenter
        };

        public static readonly GUIStyle openSearchesPanelButton = new GUIStyle("IconButton")
        {
            margin = new RectOffset(4, 4, 4, 4),
            padding = new RectOffset(2, 2, 2, 2),
            fixedWidth = 24f,
            fixedHeight = 24f,
            imagePosition = ImagePosition.ImageOnly,
            alignment = TextAnchor.MiddleCenter
        };

        public static readonly GUIStyle savedSearchesHeaderButton = new GUIStyle("IconButton")
        {
            margin = new RectOffset(2, 2, 2, 2),
            padding = new RectOffset(0, 0, 0, 0),
            fixedWidth = 24f,
            fixedHeight = 24f,
            imagePosition = ImagePosition.ImageOnly,
            alignment = TextAnchor.MiddleCenter
        };

        public static readonly GUIStyle toolbarDropdownButton = new GUIStyle("ToolbarCreateAddNewDropDown")
        {
            margin = new RectOffset(2, 2, 0, 0),
            padding = new RectOffset(2, 0, 0, 0),
            fixedWidth = 32f,
            fixedHeight = 24f,
            imagePosition = ImagePosition.ImageOnly,
            alignment = TextAnchor.MiddleLeft
        };

        public static GUIStyle toolbarSearchField = new GUIStyle(EditorStyles.toolbarSearchField)
        {
            margin = new RectOffset(4, 4, 4, 4)
        };

        public static readonly GUIStyle panelHeader = Utils.FromUSS(new GUIStyle()
        {
            margin = new RectOffset(2, 2, 2, 2),
            padding = new RectOffset(1, 1, 4, 4),
            fixedHeight = 24,
            wordWrap = false,
            alignment = TextAnchor.MiddleLeft,
            stretchWidth = false,
            clipping = TextClipping.Clip
        }, "quick-search-panel-header");

        public static readonly GUIStyle panelHeaderIcon = new GUIStyle()
        {
            fixedWidth = 24,
            fixedHeight = 24,
            alignment = TextAnchor.MiddleCenter,
            stretchWidth = true,
            stretchHeight = true,
            margin = new RectOffset(4, 4, 2, 2),
            padding = new RectOffset(4, 4, 4, 4)
        };

        public static readonly GUIStyle reportHeader = new GUIStyle(panelHeader)
        {
            stretchWidth = false,
            margin = new RectOffset(10, 10, 0, 0)
        };

        public static readonly GUIStyle reportButton = new GUIStyle("IconButton")
        {
            fixedHeight = 20,
            margin = new RectOffset(2, 2, 0, 0),
            padding = new RectOffset(0, 0, 0, 0)
        };

        public static readonly GUIStyle sidebarDropdown = new GUIStyle(EditorStyles.popup)
        {
            fixedHeight = 20,
            margin = new RectOffset(10, 10, 8, 4)
        };

        public static readonly GUIStyle sidebarActionDropdown = new GUIStyle(EditorStyles.miniButton)
        {
            fixedHeight = 20,
            margin = new RectOffset(0, 0, 8, 4),
            padding = new RectOffset(2, 2, 1, 1)
        };

        public static readonly GUIStyle sidebarToggle = new GUIStyle(EditorStyles.toggle)
        {
            margin = new RectOffset(10, 2, 4, 2)
        };

        public static readonly GUIStyle readOnlyObjectField = new GUIStyle("ObjectField")
        {
            padding = new RectOffset(3, 3, 2, 2)
        };

        public static readonly GUIContent prefButtonContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, "Open search preferences...", Icons.settings);
        public static readonly GUIStyle statusBarButton = new GUIStyle("IconButton")
        {
            fixedWidth = 16,
            fixedHeight = 16,
            margin = new RectOffset(0, 2, 3, 2),
            padding = new RectOffset(0, 0, 0, 0),
            alignment = TextAnchor.MiddleCenter,
            #if USE_SEARCH_MODULE
            imagePosition = ImagePosition.ImageOnly,
            #endif
        };

        public static readonly GUIStyle statusBarPrefsButton = new GUIStyle(statusBarButton)
        {
            margin = new RectOffset(0, 2, 2, 2),
        };

        public static readonly GUIStyle searchInProgressButton = new GUIStyle(statusBarButton)
        {
            alignment = TextAnchor.MiddleLeft,
            contentOffset = new Vector2(-1, 0),
            padding = new RectOffset(2, 2, 2, 2),
            richText = false,
            stretchHeight = false,
            stretchWidth = false
        };

        public static readonly GUILayoutOption[] searchInProgressLayoutOptions = new[] { GUILayout.MaxWidth(searchInProgressButton.fixedWidth) };
        public static readonly GUIContent emptyContent = EditorGUIUtility.TrTextContent(string.Empty, "No content");

        public static readonly GUIContent[] statusWheel;

        public static readonly GUIStyle statusBarBackground = new GUIStyle()
        {
            name = "quick-search-status-bar-background",
            fixedHeight = 21f
        };

        public static readonly GUIStyle resultview = new GUIStyle()
        {
            name = "quick-search-result-view",
            padding = new RectOffset(1, 1, 1, 1)
        };

        public static readonly GUIStyle panelBackground = new GUIStyle() { name = "quick-search-panel-background" };
        public static readonly GUIStyle panelBackgroundLeft = new GUIStyle() { name = "quick-search-panel-background-left" };
        public static readonly GUIStyle panelBackgroundRight = new GUIStyle() { name = "quick-search-panel-background-right" };
        public static readonly GUIStyle searchTabBackground = new GUIStyle() { name = "quick-search-tab-background" };
        public static readonly GUIStyle searchTab = Utils.FromUSS(new GUIStyle() { richText = true }, "quick-search-tab");
        public static readonly GUIStyle searchTabMoreButton = new GUIStyle("IN Foldout")
        {
            margin = new RectOffset(10, 2, 0, 0)
        };

        public static readonly GUIContent pressToFilterContent = EditorGUIUtility.TrTextContent("Press Tab \u21B9 to filter");
        public static readonly float pressToFilterContentWidth = searchFieldTabToFilterBtn.CalcSize(pressToFilterContent).x;
        public static readonly GUIStyle searchReportField = new GUIStyle(searchTabBackground)
        {
            padding = toolbar.padding
        };

        public static readonly GUIStyle inspector = new GUIStyle()
        {
            name = "quick-search-inspector",
            margin = new RectOffset(1, 0, 0, 0),
            padding = new RectOffset(0, 0, 0, 0)
        };

        public static readonly GUIStyle inpsectorMargins = new GUIStyle(EditorStyles.inspectorDefaultMargins)
        {
            padding = new RectOffset(8, 8, 4, 4)
        };

        public static readonly GUIStyle inpsectorWideMargins = new GUIStyle(inpsectorMargins)
        {
            padding = new RectOffset(18, 8, 4, 4)
        };

        public static class Wiggle
        {
            private static Texture2D GenerateSolidColorTexture(Color fillColor)
            {
                Texture2D texture = new Texture2D(1, 1);
                var fillColorArray = texture.GetPixels();

                for (var i = 0; i < fillColorArray.Length; ++i)
                    fillColorArray[i] = fillColor;

                texture.hideFlags = HideFlags.HideAndDontSave;
                texture.SetPixels(fillColorArray);
                texture.Apply();

                return texture;
            }

            public static readonly GUIStyle wiggle = new GUIStyle()
            {
                name = "quick-search-wiggle",
                fixedHeight = 1f,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.LowerCenter,
                normal = new GUIStyleState { background = GenerateSolidColorTexture(Color.red), scaledBackgrounds = new[] { GenerateSolidColorTexture(Color.red) } },
            };

            public static readonly GUIStyle wiggleWarning = new GUIStyle()
            {
                name = "quick-search-wiggle",
                fixedHeight = 1f,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.LowerCenter,
                normal = new GUIStyleState { background = GenerateSolidColorTexture(Color.yellow), scaledBackgrounds = new[] { GenerateSolidColorTexture(Color.yellow) } },
            };

            public static readonly GUIStyle wiggleTooltip = new GUIStyle()
            {
                name = "quick-search-wiggle-tooltip",
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
        }

        public static readonly GUIStyle dropdownItem = Utils.FromUSS("quick-search-dropdown-item");
        public static readonly GUIStyle sidebarButtons = new GUIStyle()
        {
            fixedHeight = 20,
            margin = new RectOffset(0, 8, 8, 4),
        };

        public static readonly GUIStyle dropdownItemButton = new GUIStyle(actionButton)
        {
            margin = new RectOffset(4, 4, 0, 0)
        };

        public static readonly GUIContent importReport = EditorGUIUtility.TrIconContent("Profiler.Open", "Import Report...");

        public static readonly GUIContent addMoreColumns = EditorGUIUtility.TrIconContent("CreateAddNew", "Add column...");
        public static readonly GUIContent resetColumns = EditorGUIUtility.TrIconContent("Animation.FilterBySelection", "Reset Columns Layout");

        #if USE_SEARCH_MODULE
        public static readonly GUIContent listModeContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, "List View", EditorGUIUtility.LoadIconRequired("ListView"));
        public static readonly GUIContent gridModeContent = new GUIContent(string.Empty, EditorGUIUtility.LoadIconRequired("GridView"), $"Grid View ({(int)DisplayMode.Grid}x{(int)DisplayMode.Grid})");
        public static readonly GUIContent tableModeContent = EditorGUIUtility.TrTextContentWithIcon(string.Empty, "Table View", EditorGUIUtility.LoadIconRequired("TableView"));

        public static readonly GUIContent tableSaveButtonContent = EditorGUIUtility.TrTextContentWithIcon("Save", "Save current table configuration", EditorGUIUtility.LoadIconRequired("SaveAs"));
        public static readonly GUIContent tableDeleteButtonContent = EditorGUIUtility.TrIconContent("Grid.EraserTool", "Delete table configuration");
        #else
        public static readonly GUIContent listModeContent = EditorGUIUtility.TrTextContent("L", "List View");
        public static readonly GUIContent gridModeContent = new GUIContent("G", $"Grid View ({(int)DisplayMode.Grid}x{(int)DisplayMode.Grid})");
        public static readonly GUIContent tableModeContent = EditorGUIUtility.TrTextContent("T", "Table View");
        #endif

        public static class QueryBuilder
        {
            public static readonly Color labelColor;
            public static readonly Color splitterColor;
            public static readonly GUIStyle label;

            public static GUIContent createContent = EditorGUIUtility.IconContent("Toolbar Plus More", "|Add new query block (Tab)");
            public static GUIStyle addNewDropDown = new GUIStyle("ToolbarCreateAddNewDropDown")
            {
                fixedWidth = 32f,
                fixedHeight = 0,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(2, 2, 0, 0)
            };

            static QueryBuilder()
            {
                ColorUtility.TryParseHtmlString("#202427", out labelColor);
                splitterColor = new Color(labelColor.r, labelColor.g, labelColor.b, 0.5f);

                label = new GUIStyle("ToolbarLabel")
                {
                    richText = true,
                    alignment = TextAnchor.MiddleLeft,
                    margin = new RectOffset(6, 6, 0, 0),
                    normal = new GUIStyleState { textColor = labelColor },
                    hover = new GUIStyleState { textColor = labelColor }
                };
            }
        }
    }

    static class QueryColors
    {
        private static bool isDarkTheme => EditorGUIUtility.isProSkin;

        public static readonly Color area;
        public static readonly Color filter;
        public static readonly Color property;
        public static readonly Color type;
        public static readonly Color typeIcon;
        public static readonly Color word;
        public static readonly Color combine;
        public static readonly Color expression;
        public static readonly Color textureBackgroundColor = new Color(0.2f, 0.2f, 0.25f, 0.95f);
        public static readonly Color selectedBorderColor = new Color(58 / 255f, 121 / 255f, 187 / 255f);
        public static readonly Color hoveredBorderColor = new Color(0.6f, 0.6f, 0.6f);
        public static readonly Color normalBorderColor = new Color(0.1f, 0.1f, 0.1f);
        public static readonly Color selectedTint = new Color(1.3f, 1.2f, 1.3f, 1f);

        static QueryColors()
        {
            ColorUtility.TryParseHtmlString("#74CBEE", out area);
            ColorUtility.TryParseHtmlString("#78CAB6", out filter);
            ColorUtility.TryParseHtmlString("#A38CD0", out property);
            ColorUtility.TryParseHtmlString("#EBD05F", out type);
            ColorUtility.TryParseHtmlString("#EBD05F", out typeIcon);
            ColorUtility.TryParseHtmlString("#739CEB", out word);
            ColorUtility.TryParseHtmlString("#B7B741", out combine);
            ColorUtility.TryParseHtmlString("#8DBB65", out expression);
            if (isDarkTheme)
            {
                ColorUtility.TryParseHtmlString("#383838", out textureBackgroundColor);
                selectedBorderColor = Color.white;
            }
            else
            {
                ColorUtility.TryParseHtmlString("#CBCBCB", out textureBackgroundColor);
            }
        }
    }
}

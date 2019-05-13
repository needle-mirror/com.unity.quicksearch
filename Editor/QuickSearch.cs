//#define QUICKSEARCH_DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Unity.QuickSearch
{
    internal class QuickSearchTool : EditorWindow
    {
        public static EditorWindow s_FocusedWindow;

        private const int k_ResetSelectionIndex = -1;

        [SerializeField] private Vector2 m_ScrollPosition;
        [SerializeField] public EditorWindow lastFocusedWindow;
        [SerializeField] private int m_SelectedIndex = k_ResetSelectionIndex;
        [SerializeField] private bool m_SaveStateOnExit = true;

        private bool m_SendAnalyticsEvent;
        private SearchContext m_Context;
        private List<SearchItem> m_FilteredItems;
        private bool m_FocusSelectedItem = false;
        private Rect m_ScrollViewOffset;
        private bool m_SearchBoxFocus;
        private double m_ClickTime = 0;
        private bool m_CursorBlinking;
        private bool m_IsRepaintAfterTimeRequested = false;
        private double m_RequestRepaintAfterTime = 0;
        private double m_NextBlinkTime = 0;
        private bool m_PrepareDrag;
        private string m_CycledSearch;
        private bool m_ShowFilterWindow = false;
        private SearchAnalytics.SearchEvent m_CurrentSearchEvent;
        private double m_DebounceTime = 0.0;

        private float m_Height = 0;

        private const string k_QuickSearchBoxName = "QuickSearchBox";

        private static class Styles
        {
            static Styles()
            {
                if (!hasPro)
                {
                    selectedItemLabel.normal.textColor = Color.white;
                    selectedItemDescription.normal.textColor = Color.white;
                }
            }

            private const int itemRowPadding = 4;
            public const float actionButtonSize = 24f;
            public const float itemPreviewSize = 32f;
            public const float itemRowSpacing = 35.0f;
            private const int actionButtonMargin = (int)((itemRowHeight - actionButtonSize) / 2f);
            public const float itemRowHeight = itemPreviewSize + itemRowPadding * 2f;

            private static bool hasPro => EditorGUIUtility.isProSkin;

            private static readonly RectOffset marginNone = new RectOffset(0, 0, 0, 0);
            private static readonly RectOffset paddingNone = new RectOffset(0, 0, 0, 0);
            private static readonly RectOffset defaultPadding = new RectOffset(itemRowPadding, itemRowPadding, itemRowPadding, itemRowPadding);

            private static readonly Color darkColor1 = new Color(61 / 255f, 61 / 255f, 61 / 255f);
            private static readonly Color darkColor2 = new Color(71 / 255f, 106 / 255f, 155 / 255f);
            private static readonly Color darkColor3 = new Color(68 / 255f, 68 / 255f, 71 / 255f);
            private static readonly Color darkColor4 = new Color(111 / 255f, 111 / 255f, 111 / 255f);
            private static readonly Color darkColor5 = new Color(71 / 255f, 71 / 255f, 71 / 255f);
            private static readonly Color darkColor6 = new Color(63 / 255f, 63 / 255f, 63 / 255f);
            private static readonly Color darkColor7 = new Color(71 / 255f, 71 / 255f, 71 / 255f); // TODO: Update me

            private static readonly Color lightColor1 = new Color(171 / 255f, 171 / 255f, 171 / 255f);
            private static readonly Color lightColor2 = new Color(71 / 255f, 106 / 255f, 155 / 255f);
            private static readonly Color lightColor3 = new Color(168 / 255f, 168 / 255f, 171 / 255f);
            private static readonly Color lightColor4 = new Color(111 / 255f, 111 / 255f, 111 / 255f);
            private static readonly Color lightColor5 = new Color(181 / 255f, 181 / 255f, 181 / 255f);
            private static readonly Color lightColor6 = new Color(214 / 255f, 214 / 255f, 214 / 255f);
            private static readonly Color lightColor7 = new Color(230 / 255f, 230 / 255f, 230 / 255f);


            #if !UNITY_2019_3_OR_NEWER
            private static readonly Color darkSelectedRowColor = new Color(61 / 255f, 96 / 255f, 145 / 255f);
            private static readonly Color lightSelectedRowColor = new Color(61 / 255f, 128 / 255f, 223 / 255f);
            private static readonly Texture2D alternateRowBackgroundImage = GenerateSolidColorTexture(hasPro ? darkColor1 : lightColor1);
            private static readonly Texture2D selectedRowBackgroundImage = GenerateSolidColorTexture(hasPro ? darkSelectedRowColor : lightSelectedRowColor);
            private static readonly Texture2D selectedHoveredRowBackgroundImage = GenerateSolidColorTexture(hasPro ? darkColor2 : lightColor2);
            private static readonly Texture2D hoveredRowBackgroundImage = GenerateSolidColorTexture(hasPro ? darkColor3 : lightColor3);
            #endif

            private static readonly Texture2D buttonPressedBackgroundImage = GenerateSolidColorTexture(hasPro ? darkColor4 : lightColor4);
            private static readonly Texture2D buttonHoveredBackgroundImage = GenerateSolidColorTexture(hasPro ? darkColor5 : lightColor5);

            private static readonly Texture2D searchFieldBg = GenerateSolidColorTexture(hasPro ? darkColor6 : lightColor6);
            private static readonly Texture2D searchFieldFocusBg = GenerateSolidColorTexture(hasPro ? darkColor7 : lightColor7);

            public const string s_Helpme = "Search anything!\r\n\r\n" +
                                           "- Alt + Up/Down Arrow: Search history\r\n" +
                                           "- Alt + Left: Filter\r\n" +
                                           "- Alt + Right: Actions menu\r\n" +
                                           "- Enter: Default action\r\n" +
                                           "- Alt + Enter: Secondary action\r\n" +
                                           "- Drag items around\r\n";

            public static readonly GUIStyle panelBorder = new GUIStyle("grey_border")
            {
                name = "quick-search-border", 
                padding = new RectOffset(1, 1, 1, 1), 
                margin = new RectOffset(0, 0, 0, 0)
            };
            public static readonly GUIContent filterButtonContent = new GUIContent("", Icons.filter);

            public static readonly GUIStyle itemBackground1 = new GUIStyle
            {
                name = "quick-search-item-background1",
                fixedHeight = itemRowHeight,

                margin = marginNone,
                padding = defaultPadding,

                #if !UNITY_2019_3_OR_NEWER
                hover = new GUIStyleState { background = hoveredRowBackgroundImage, scaledBackgrounds = new[] { hoveredRowBackgroundImage } }
                #endif
            };

            public static readonly GUIStyle itemBackground2 = new GUIStyle(itemBackground1)
            {
                name = "quick-search-item-background2",

                #if !UNITY_2019_3_OR_NEWER
                normal = new GUIStyleState { background = alternateRowBackgroundImage, scaledBackgrounds = new[] { alternateRowBackgroundImage } }
                #endif
            };

            public static readonly GUIStyle selectedItemBackground = new GUIStyle(itemBackground1)
            {
                name = "quick-search-item-selected-background",

                #if !UNITY_2019_3_OR_NEWER
                normal = new GUIStyleState { background = selectedRowBackgroundImage, scaledBackgrounds = new[] { selectedRowBackgroundImage } },
                hover = new GUIStyleState { background = selectedHoveredRowBackgroundImage, scaledBackgrounds = new[] { selectedHoveredRowBackgroundImage } }
                #endif
            };

            public static readonly GUIStyle preview = new GUIStyle
            {
                name = "quick-search-item-preview",
                fixedWidth = itemPreviewSize,
                fixedHeight = itemPreviewSize,
                alignment = TextAnchor.MiddleCenter,
                imagePosition = ImagePosition.ImageOnly,

                margin = new RectOffset(2, 2, 2, 2),
                padding = paddingNone
            };

            public static readonly GUIStyle itemLabel = new GUIStyle(EditorStyles.label)
            {
                name = "quick-search-item-label",
                richText = true,
                margin = new RectOffset(4, 4, 6, 2),
                padding = paddingNone
            };

            public static readonly GUIStyle selectedItemLabel = new GUIStyle(itemLabel)
            {
                name = "quick-search-item-selected-label",

                margin = new RectOffset(4, 4, 6, 2),
                padding = paddingNone
            };

            public static readonly GUIStyle noResult = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                name = "quick-search-no-result",
                fontSize = 20,
                fixedHeight = 0,
                fixedWidth = 0,
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter,
                margin = marginNone,
                padding = paddingNone
            };

            public static readonly GUIStyle itemDescription = new GUIStyle(EditorStyles.label)
            {
                name = "quick-search-item-description",
                richText = true,
                margin = new RectOffset(4, 4, 1, 4),
                padding = paddingNone,

                fontSize = Math.Max(9, itemLabel.fontSize - 2),
                fontStyle = FontStyle.Italic
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

                margin = new RectOffset(4, 4, actionButtonMargin, actionButtonMargin),
                padding = paddingNone,

                active = new GUIStyleState { background = buttonPressedBackgroundImage, scaledBackgrounds = new[] { buttonPressedBackgroundImage } },
                hover = new GUIStyleState { background = buttonHoveredBackgroundImage, scaledBackgrounds = new[] { buttonHoveredBackgroundImage } }
            };

            private const float k_ToolbarHeight = 40.0f;

            private static readonly GUIStyleState clear = new GUIStyleState()
            {
                background = null, 
                scaledBackgrounds = new Texture2D[] { null },
                textColor = hasPro ? new Color (210 / 255f, 210 / 255f, 210 / 255f) : Color.black
            };

            private static readonly GUIStyleState searchFieldBgNormal = new GUIStyleState() { background = searchFieldBg, scaledBackgrounds = new Texture2D[] { null } };
            private static readonly GUIStyleState searchFieldBgFocus = new GUIStyleState() { background = searchFieldFocusBg, scaledBackgrounds = new Texture2D[] { null } };

            public static readonly GUIStyle toolbar = new GUIStyle("Toolbar")
            {
                name = "quick-search-bar",
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(0, 0, 0, 0),
                fixedHeight = k_ToolbarHeight,

                normal = searchFieldBgNormal,
                focused = searchFieldBgFocus, hover = searchFieldBgFocus, active = searchFieldBgFocus,
                onNormal = clear, onHover = searchFieldBgFocus, onFocused = searchFieldBgFocus, onActive = searchFieldBgFocus,
            };

            public static readonly GUIStyle searchField = new GUIStyle("ToolbarSeachTextFieldPopup")
            {
                name = "quick-search-search-field",
                fontSize = 28,
                alignment = TextAnchor.MiddleLeft,
                margin = new RectOffset(10, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(0, 0, 0, 0),
                fixedHeight = 0,
                normal = clear,
                focused = clear, hover = clear, active = clear,
                onNormal = clear, onHover = clear, onFocused = clear, onActive = clear,
            };

            public static readonly GUIStyle placeholderTextStyle = new GUIStyle(searchField)
            {
                fontSize = 20,
                fontStyle = FontStyle.Italic,
                padding = new RectOffset(6, 0, 0, 0)
            };

            public static readonly GUIStyle searchFieldClear = new GUIStyle()
            {
                name = "quick-search-search-field-clear",
                fixedHeight = 0,
                fixedWidth = 0,
                margin = new RectOffset(0, 5, 8, 0),
                padding = new RectOffset(0, 0, 0, 0),
                normal = clear,
                focused = clear, hover = clear, active = clear,
                onNormal = clear, onHover = clear, onFocused = clear, onActive = clear
            };

            public static readonly GUIStyle filterButton = new GUIStyle(EditorStyles.whiteLargeLabel)
            {
                name = "quick-search-filter-button",
                margin = new RectOffset(-4, 0, 0, 0),
                padding = new RectOffset(0, 0, 1, 0),
                normal = clear,
                focused = clear, hover = clear, active = clear,
                onNormal = clear, onHover = clear, onFocused = clear, onActive = clear
            };

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
        }

        [UsedImplicitly]
        internal void OnEnable()
        {
            m_CurrentSearchEvent = new SearchAnalytics.SearchEvent();
            m_Context = new SearchContext { searchText = SearchService.LastSearch, focusedWindow = lastFocusedWindow };
            SearchService.Enable(m_Context);
            m_SearchBoxFocus = true;
            lastFocusedWindow = s_FocusedWindow;
            UpdateWindowTitle();

            Refresh();

            SearchService.asyncItemReceived += OnAsyncItemsReceived;
            //SearchService.contentRefreshed += (a, b, c) => Refresh();
        }

        private void OnAsyncItemsReceived(IEnumerable<SearchItem> items)
        {
            if (m_SelectedIndex == -1)
            {
                m_FilteredItems.AddRange(items);
                SearchService.SortItemList(m_FilteredItems);
            }
            else
            {
                m_FilteredItems.InsertRange(m_SelectedIndex + 1, items);
            }

            Repaint();
        }

        [UsedImplicitly]
        internal void OnDisable()
        {
            s_FocusedWindow = null;

            m_CurrentSearchEvent.Done();
            m_CurrentSearchEvent.searchText = m_Context.searchText;
            m_CurrentSearchEvent.saveSearchStateOnExit = m_SaveStateOnExit;

            SearchService.asyncItemReceived -= OnAsyncItemsReceived;
            SearchService.Disable(m_Context, m_SaveStateOnExit);

            if (m_SendAnalyticsEvent)
                SearchAnalytics.SendSearchEvent(m_CurrentSearchEvent);
        }

        private void UpdateWindowTitle()
        {
            titleContent.image = Icons.quicksearch;
            if (m_FilteredItems == null || m_FilteredItems.Count == 0)
                titleContent.text = "Search Anything!";
            else
                titleContent.text = $"Found {m_FilteredItems.Count} Anything!";
        }

        [UsedImplicitly]
        internal void OnGUI()
        {
            if (m_Height != position.height)
                OnResize();

            HandleKeyboardNavigation(m_Context);

            if (!SearchSettings.useDockableWindow)
                EditorGUILayout.BeginVertical(Styles.panelBorder);
            {
                DrawToolbar(m_Context);
                DrawItems(m_Context);
            }
            if (!SearchSettings.useDockableWindow)
                EditorGUILayout.EndVertical();

            UpdateFocusControlState();
            RequestRepaintAfterTime(2);
        }

        [UsedImplicitly]
        internal void OnFocus()
        {
            if (SearchSettings.useDockableWindow)
                m_SearchBoxFocus = true;
        }

        internal void OnResize()
        {
            if (m_Height > 0 && m_ScrollPosition.y > 0)
                m_ScrollPosition.y -= position.height - m_Height;
            m_Height = position.height;
        }

        public void Refresh()
        {
            m_FilteredItems = SearchService.GetItems(m_Context);
            SetSelection(k_ResetSelectionIndex);
            UpdateWindowTitle();
            Repaint();
        }

        private int SetSelection(int selection)
        {
            var previousSelection = m_SelectedIndex;
            m_SelectedIndex = Math.Max(-1, Math.Min(selection, m_FilteredItems.Count - 1));
            if (m_SelectedIndex == k_ResetSelectionIndex)
                m_ScrollPosition.y = 0;
            if (previousSelection != m_SelectedIndex)
                RaiseSelectionChanged(previousSelection, m_SelectedIndex);
            return m_SelectedIndex;
        }

        private void RaiseSelectionChanged(int previousSelection, int currentSelection)
        {
            if (currentSelection == -1)
                return;
            
            EditorApplication.delayCall += () => TrackSelection(currentSelection);
        }

        private void TrackSelection(int currentSelection)
        {
            if (m_FilteredItems == null || m_FilteredItems.Count == 0)
                return;

            var selectedItem = m_FilteredItems[currentSelection];
            if (selectedItem.provider == null || selectedItem.provider.trackSelection == null)
                return;

            selectedItem.provider.trackSelection(selectedItem, m_Context);
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

        private int GetDisplayItemCount()
        {
            if (m_FilteredItems == null)
                return 0;
            var itemCount = m_FilteredItems.Count;
            var availableHeight = position.height - m_ScrollViewOffset.yMax;
            return Math.Max(0, Math.Min(itemCount, (int)(availableHeight / Styles.itemRowHeight) + 2));
        }

        private void HandleKeyboardNavigation(SearchContext context)
        {
            // TODO: support page down and page up

            var evt = Event.current;
            if (evt.type == EventType.KeyDown)
            {
                var prev = m_SelectedIndex;
                if (evt.keyCode == KeyCode.DownArrow)
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
                else if (evt.keyCode == KeyCode.UpArrow)
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
                        ShowItemContextualMenu(item, context, new Rect(position.width - Styles.actionButtonSize, menuPositionY, 1, 1));
                        Event.current.Use();
                    }
                }
                else if (evt.keyCode == KeyCode.LeftArrow && evt.modifiers.HasFlag(EventModifiers.Alt))
                {
                    m_CurrentSearchEvent.useFilterMenuShortcut = true;
                    m_ShowFilterWindow = true;
                    Event.current.Use();
                }
                else if (evt.keyCode == KeyCode.KeypadEnter || evt.keyCode == KeyCode.Return)
                {
                    var selectedIndex = m_SelectedIndex;
                    if (selectedIndex == -1 && m_FilteredItems != null && m_FilteredItems.Count > 0)
                        selectedIndex = 0;

                    if (selectedIndex != -1 && m_FilteredItems != null)
                    {
                        int actionIndex = 0;
                        if (evt.modifiers.HasFlag(EventModifiers.Alt))
                        {
                            actionIndex = 1;
                            if (evt.modifiers.HasFlag(EventModifiers.Control))
                            {
                                actionIndex = 2;
                                if (evt.modifiers.HasFlag(EventModifiers.Shift))
                                    actionIndex = 3;
                            }
                        }
                        var item = m_FilteredItems.ElementAt(selectedIndex);
                        if (item.provider.actions.Any())
                        {
                            Event.current.Use();
                            actionIndex = Math.Max(0, Math.Min(actionIndex, item.provider.actions.Count - 1));

                            m_CurrentSearchEvent.endSearchWithKeyboard = true;
                            ExecuteAction(item.provider.actions[actionIndex], item, context);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    m_CurrentSearchEvent.endSearchWithKeyboard = true;
                    CloseSearchWindow();
                    Event.current.Use();
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

        private void HandleItemEvents(int itemTotalCount, SearchContext context)
        {
            if (Event.current.type == EventType.MouseDown)
            {
                var clickedItemIndex = (int)(Event.current.mousePosition.y / Styles.itemRowHeight);
                if (clickedItemIndex >= 0 && clickedItemIndex < itemTotalCount)
                {
                    SetSelection(clickedItemIndex);
                    if ((EditorApplication.timeSinceStartup - m_ClickTime) < 0.2)
                    {
                        var item = m_FilteredItems.ElementAt(m_SelectedIndex);
                        ExecuteAction(item.provider.actions[0], item, context);
                        GUIUtility.ExitGUI();
                    }
                    EditorGUI.FocusTextInControl(k_QuickSearchBoxName);
                    Event.current.Use();
                }
                m_ClickTime = EditorApplication.timeSinceStartup;
                m_PrepareDrag = true;
            }
            else if (Event.current.type == EventType.MouseDrag && m_PrepareDrag)
            {
                if (m_FilteredItems != null && m_SelectedIndex >= 0)
                {
                    var item = m_FilteredItems.ElementAt(m_SelectedIndex);
                    if (item.provider?.startDrag != null)
                    {
                        m_CurrentSearchEvent.useDragAndDrop = true;
                        m_CurrentSearchEvent.Success(item);
                        item.provider.startDrag(item, context);
                        m_PrepareDrag = false;
                    }
                }
            }
        }

        private void DrawItems(SearchContext context)
        {
            UpdateScrollAreaOffset();

            context.totalItemCount = m_FilteredItems.Count;

            using (var scrollViewScope = new EditorGUILayout.ScrollViewScope(m_ScrollPosition))
            {
                m_ScrollPosition = scrollViewScope.scrollPosition;

                var itemCount = m_FilteredItems.Count;
                var availableHeight = position.height - m_ScrollViewOffset.yMax;
                var itemSkipCount = Math.Max(0, (int)(m_ScrollPosition.y / Styles.itemRowHeight));
                var itemDisplayCount = Math.Max(0, Math.Min(itemCount, (int)(availableHeight / Styles.itemRowHeight) + 2));
                var topSpaceSkipped = itemSkipCount * Styles.itemRowHeight;

                int rowIndex = itemSkipCount;
                var limitCount = Math.Max(0, Math.Min(itemDisplayCount, itemCount - itemSkipCount));
                if (limitCount > 0)
                {
                    if (topSpaceSkipped > 0)
                        GUILayout.Space(topSpaceSkipped);

                    foreach (var item in m_FilteredItems.GetRange(itemSkipCount, limitCount))
                    {
                        try
                        {
                            DrawItem(item, context, rowIndex++);
                        }
                        #if QUICKSEARCH_DEBUG
                        catch (Exception ex)
                        {
                            Debug.LogError($"itemCount={itemCount}, " +
                                           $"itemSkipCount={itemSkipCount}, " +
                                           $"limitCount={limitCount}, " +
                                           $"availableHeight={availableHeight}, " +
                                           $"itemDisplayCount={itemDisplayCount}, " +
                                           $"m_SelectedIndex={m_SelectedIndex}, " +
                                           $"m_ScrollViewOffset.yMax={m_ScrollViewOffset.yMax}, " +
                                           $"rowIndex={rowIndex-1}");
                            Debug.LogException(ex);
                        }
                        #else
                        catch
                        {
                            // ignored
                        }
                        #endif
                    }

                    var bottomSpaceSkipped = (itemCount - rowIndex) * Styles.itemRowHeight;
                    if (bottomSpaceSkipped > 0)
                        GUILayout.Space(bottomSpaceSkipped);

                    HandleItemEvents(itemCount, context);

                    // Fix selected index display if out of virtual scrolling area
                    if (Event.current.type == EventType.Repaint && m_FocusSelectedItem && m_SelectedIndex >= 0)
                    {
                        ScrollToItem(itemSkipCount + 1, itemSkipCount + itemDisplayCount - 2, m_SelectedIndex);
                        m_FocusSelectedItem = false;
                    }
                }
                else
                {
                    if (String.IsNullOrEmpty(m_Context.searchText.Trim()))
                    {
                        GUILayout.Box(Styles.s_Helpme, Styles.noResult, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                    }
                    else
                    {
                        GUILayout.Box("No result for query \"" + m_Context.searchText + "\"\n" + "Try something else?",
                                      Styles.noResult, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                    }
                }
            }
        }

        private void ExecuteAction(SearchAction action, SearchItem item, SearchContext context)
        {
            m_CurrentSearchEvent.Success(item, action);
            SearchService.LastSearch = context.searchText;
            action.handler(item, context);
            CloseSearchWindow();
        }

        private void ScrollToItem(int start, int end, int selection)
        {
            if (start <= selection && selection < end)
                return;

            Rect projectedSelectedItemRect = new Rect(0, selection * Styles.itemRowHeight, position.width, Styles.itemRowHeight);
            if (selection < start)
            {
                m_ScrollPosition.y = Mathf.Max(0, projectedSelectedItemRect.y - 2);
                Repaint();
            }
            else if (selection > end)
            {
                Rect visibleRect = GetVisibleRect();
                m_ScrollPosition.y += (projectedSelectedItemRect.yMax - visibleRect.yMax) + 2;
                Repaint();
            }
        }

        private void UpdateScrollAreaOffset()
        {
            var rect = GUILayoutUtility.GetLastRect();
            if (rect.height > 1)
                m_ScrollViewOffset = rect;
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

        private void RequestRepaintAfterTime(double seconds)
        {
            if (!m_IsRepaintAfterTimeRequested)
            {
                m_IsRepaintAfterTimeRequested = true;
                m_RequestRepaintAfterTime = EditorApplication.timeSinceStartup + seconds;
            }
        }

        private void DrawToolbar(SearchContext context)
        {
            if (context == null)
                return;
            
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
                    context.searchText = EditorGUILayout.TextField(userSearchQuery, Styles.searchField, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                }

                if (String.IsNullOrEmpty(context.searchText))
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUI.TextArea(GUILayoutUtility.GetLastRect(), "search anything...", Styles.placeholderTextStyle);
                    EditorGUI.EndDisabledGroup();
                }
                if (!String.IsNullOrEmpty(context.searchText))
                {
                    if (GUILayout.Button(Icons.clear, Styles.searchFieldClear, GUILayout.Width(24), GUILayout.Height(24)))
                    {
                        context.searchText = "";
                        GUI.changed = true;
                        GUI.FocusControl(null);
                    }
                }

                if (EditorGUI.EndChangeCheck() || m_FilteredItems == null)
                {
                    SetSelection(k_ResetSelectionIndex);
                    DebouncedRefresh();
                }

                #if QUICKSEARCH_DEBUG
                DrawDebugTools();
                #endif
            }
            GUILayout.EndHorizontal();
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
                EditorApplication.delayCall += DebouncedRefresh;
            }
        }

        private Rect GetVisibleRect()
        {
            Rect visibleRect = position;
            visibleRect.x = m_ScrollPosition.x;
            visibleRect.y = m_ScrollPosition.y;
            visibleRect.height -= m_ScrollViewOffset.yMax;
            return visibleRect;
        }

        private void DrawItem(SearchItem item, SearchContext context, int index)
        {
            var bgStyle = index % 2 == 0 ? Styles.itemBackground1 : Styles.itemBackground2;
            if (m_SelectedIndex == index)
                bgStyle = Styles.selectedItemBackground;

            using (new EditorGUILayout.HorizontalScope(bgStyle))
            {
                GUILayout.Label(item.thumbnail ?? item.provider.fetchThumbnail(item, context), Styles.preview);

                using (new EditorGUILayout.VerticalScope())
                {
                    var textMaxWidthLayoutOption = GUILayout.MaxWidth(position.width - Styles.actionButtonSize - Styles.itemPreviewSize - Styles.itemRowSpacing);
                    GUILayout.Label(item.label ?? item.id, m_SelectedIndex == index ? Styles.selectedItemLabel : Styles.itemLabel, textMaxWidthLayoutOption);
                    GUILayout.Label(FormatDescription(item, context), m_SelectedIndex == index ? Styles.selectedItemDescription : Styles.itemDescription, textMaxWidthLayoutOption);
                }

                if (item.provider.actions.Count > 1)
                {
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button(Icons.more, Styles.actionButton))
                    {
                        ShowItemContextualMenu(item, context);
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private static string FormatDescription(SearchItem item, SearchContext context)
        {
            var desc = item.description ?? item.provider.fetchDescription(item, context);
            var parts = context.searchQuery.Split('*', ' ', '.').Where(p => !String.IsNullOrEmpty(p) && p.Length > 2);
            foreach (var p in parts)
                desc = Regex.Replace(desc, Regex.Escape(p), "<color=#FFFF00>" + p + "</color>", RegexOptions.IgnoreCase);

            return desc;
        }

        private void ShowItemContextualMenu(SearchItem item, SearchContext context, Rect position = default(Rect))
        {
            var menu = new GenericMenu();
            foreach (var action in item.provider.actions)
                menu.AddItem(new GUIContent(action.content.tooltip, action.content.image), false, () => ExecuteAction(action, item, context));

            if (position == default(Rect))
                menu.ShowAsContext();
            else
                menu.DropDown(position);
        }

        public static void ShowWindow(bool saveSearchStateOnExit = true)
        {
            s_FocusedWindow = focusedWindow;

            var windowSize = new Vector2(550, 400);
            
            QuickSearchTool qsWindow;
            if (!SearchSettings.useDockableWindow)
            {
                qsWindow = CreateInstance<QuickSearchTool>();
                qsWindow.autoRepaintOnSceneChange = true;
                qsWindow.ShowDropDown(windowSize);
            }
            else
            {
                qsWindow = GetWindow<QuickSearchTool>();
                qsWindow.Show();
            }

            // Ensure we won't send events while doing a domain reload.
            qsWindow.m_SendAnalyticsEvent = true;
            qsWindow.m_SaveStateOnExit = saveSearchStateOnExit;
            qsWindow.Focus();
        }

        #if UNITY_2019_1_OR_NEWER
        [UsedImplicitly,
         Shortcut("Help/Quick Search", KeyCode.O, ShortcutModifiers.Alt | ShortcutModifiers.Shift),
         MenuItem("Help/Quick Search &'", priority = 9000)]
        #else
        [MenuItem("Help/Quick Search &'", priority = 9000)]
        #endif
        private static void PopQuickSearch()
        {
            ShowWindow();
        }

        #if QUICKSEARCH_DEBUG
        [UsedImplicitly, MenuItem("Tools/Clear Editor Preferences")]
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
                SearchService.SaveSettings();
            }
            if (GUILayout.Button("Reset", EditorStyles.toolbarButton))
            {
                SearchService.Reset();
                Refresh();
            }
        }
        #endif
    }
}

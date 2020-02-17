using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.QuickSearch
{
    partial class QuickSearch
    {
        private void DrawList()
        {
            var itemCount = m_FilteredItems.Count;
            var availableHeight = position.height - m_ScrollViewOffset.yMax - Styles.statusLabel.fixedHeight;
            var itemSkipCount = Math.Max(0, (int)(m_ScrollPosition.y / Styles.itemRowHeight));
            var itemDisplayCount = Math.Max(0, Math.Min(itemCount, (int)(availableHeight / Styles.itemRowHeight) + 2));
            var topSpaceSkipped = itemSkipCount * Styles.itemRowHeight;

            int rowIndex = itemSkipCount;

            if (topSpaceSkipped > 0)
                GUILayout.Space(topSpaceSkipped);

            m_ItemVisibleRegion = new Rect(m_ScrollViewOffset.x, m_ScrollViewOffset.yMax, m_DrawItemsWidth, availableHeight);

            int thumbnailFetched = 0;
            var limitCount = Math.Max(0, Math.Min(itemDisplayCount, itemCount - itemSkipCount));
            foreach (var item in m_FilteredItems.GetRange(itemSkipCount, limitCount))
            {
                try
                {
                    DrawItem(item, context, rowIndex++, ref thumbnailFetched);
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

            // Fix selected index display if out of virtual scrolling area
            if (Event.current.type == EventType.Repaint && m_FocusSelectedItem && m_SelectedIndex >= 0)
            {
                ScrollListToItem(itemSkipCount + 1, itemSkipCount + itemDisplayCount - 2, m_SelectedIndex);
                m_FocusSelectedItem = false;
            }
            else
                HandleListItemEvents(itemCount, context);
        }

        private void DrawItem(SearchItem item, SearchContext context, int index, ref int thumbnailFetched)
        {
            var bgStyle = index % 2 == 0 ? Styles.itemBackground1 : Styles.itemBackground2;
            if (m_SelectedIndex == index)
                bgStyle = Styles.selectedItemBackground;

            using (new EditorGUILayout.HorizontalScope(bgStyle))
            {
                DrawListThumbnail(item, context, ref thumbnailFetched);

                using (new EditorGUILayout.VerticalScope())
                {
                    var maxWidth = m_DrawItemsWidth - Styles.actionButtonSize - Styles.itemPreviewSize - Styles.itemRowSpacing - Styles.descriptionPadding;
                    var textMaxWidthLayoutOption = GUILayout.MaxWidth(maxWidth);
                    GUILayout.Label(item.provider.fetchLabel(item, context), m_SelectedIndex == index ? Styles.selectedItemLabel : Styles.itemLabel, textMaxWidthLayoutOption);
                    GUILayout.Label(SearchContent.FormatDescription(item, context, maxWidth), m_SelectedIndex == index ? Styles.selectedItemDescription : Styles.itemDescription, textMaxWidthLayoutOption);
                }

                if (selectCallback == null && item.provider.actions.Count > 1)
                {
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button(Styles.moreActionsContent, Styles.actionButton))
                    {
                        ShowItemContextualMenu(item, context);
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private void DrawListThumbnail(SearchItem item, SearchContext context, ref int previewFetchedCount, int maxThumbnailFetchPerRepaint = 1, int maxItemPreviewCachedCount = 25)
        {
            Texture2D thumbnail = null;
            if (Event.current.type == EventType.Repaint)
            {
                if (SearchSettings.fetchPreview)
                {
                    thumbnail = item.preview;
                    var shouldFetchPreview = !thumbnail && item.provider.fetchPreview != null;
                    if (shouldFetchPreview && previewFetchedCount < maxThumbnailFetchPerRepaint)
                    {
                        if (m_ItemPreviewCache.Count > maxItemPreviewCachedCount)
                        {
                            m_ItemPreviewCache.First().preview = null;
                            m_ItemPreviewCache.RemoveFirst();
                        }

                        var previewSize = new Vector2(Styles.preview.fixedWidth, Styles.preview.fixedHeight);
                        thumbnail = item.provider.fetchPreview(item, context, previewSize, FetchPreviewOptions.Preview2D | FetchPreviewOptions.Normal);
                        if (thumbnail)
                        {
                            previewFetchedCount++;
                            item.preview = thumbnail;
                            m_ItemPreviewCache.AddLast(item);
                        }
                    }
                    else if (shouldFetchPreview && previewFetchedCount == maxThumbnailFetchPerRepaint)
                    {
                        previewFetchedCount++;
                        RequestRepaintAfterTime(0.3f);
                    }
                }

                if (!thumbnail)
                {
                    thumbnail = item.thumbnail;
                    if (!thumbnail && item.provider.fetchThumbnail != null)
                    {
                        thumbnail = item.provider.fetchThumbnail(item, context);
                        if (thumbnail)
                            item.thumbnail = thumbnail;
                    }
                }
            }
            GUILayout.Label(thumbnail ?? Icons.quicksearch, Styles.preview);
        }

        private void HandleListItemEvents(int itemTotalCount, SearchContext context)
        {
            var mpOffseted = GetScrollViewOffsetedMousePosition();
            if (m_AutoCompleting && m_AutoCompleteRect.Contains(mpOffseted))
                return;

            if (Event.current.isMouse && !m_ItemVisibleRegion.Contains(mpOffseted))
                return;

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                var clickedItemIndex = (int)(Event.current.mousePosition.y / Styles.itemRowHeight);
                if (clickedItemIndex >= 0 && clickedItemIndex < itemTotalCount)
                    HandleMouseDown();
            }
            else if (Event.current.type == EventType.MouseUp || IsDragFinishedFarEnough(Event.current))
            {
                var clickedItemIndex = (int)(Event.current.mousePosition.y / Styles.itemRowHeight);
                HandleMouseUp(clickedItemIndex, itemTotalCount);
            }
            else if (Event.current.type == EventType.MouseDrag && m_PrepareDrag)
            {
                var dragIndex = (int)(Event.current.mousePosition.y / Styles.itemRowHeight);
                HandleMouseDrag(dragIndex, itemTotalCount);
            }
        }

        private void RequestRepaintAfterTime(double seconds)
        {
            if (!m_IsRepaintAfterTimeRequested)
            {
                m_IsRepaintAfterTimeRequested = true;
                m_RequestRepaintAfterTime = EditorApplication.timeSinceStartup + seconds;
            }
        }

        private void ScrollListToItem(int start, int end, int selection)
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
                Rect visibleRect = GetListVisibleRect();
                m_ScrollPosition.y += (projectedSelectedItemRect.yMax - visibleRect.yMax) + 2;
                Repaint();
            }
        }

        private Rect GetListVisibleRect()
        {
            Rect visibleRect = position;
            visibleRect.x = m_ScrollPosition.x;
            visibleRect.y = m_ScrollPosition.y;
            visibleRect.height -= m_ScrollViewOffset.yMax;
            return visibleRect;
        }
    }
}
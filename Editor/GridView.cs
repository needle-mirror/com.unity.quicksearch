using System;
using UnityEditor;
using UnityEngine;

namespace Unity.QuickSearch
{
    partial class QuickSearch
    {
        const float itemPadding = 4f;
        const float itemLabelHeight = 32f;
        const float itemLabelTopPadding = 4f;

        private void DrawGrid()
        {
            float itemWidth = itemIconSize + itemPadding * 2;
            float itemHeight = itemIconSize + itemLabelHeight + itemLabelTopPadding + itemPadding * 2;

            var gridWidth = m_DrawItemsWidth;
            var itemCount = m_FilteredItems.Count;
            int columnCount = (int)(gridWidth / itemWidth);
            int lineCount = Mathf.CeilToInt(itemCount / (float)columnCount);
            var gridHeight = lineCount * itemHeight - Styles.statusLabel.fixedHeight;
            var availableHeight = position.height - m_ScrollViewOffset.yMax - Styles.statusLabel.fixedHeight;

            if (gridHeight > availableHeight)
            {
                gridWidth -= Styles.scrollbar.fixedWidth;
                columnCount = (int)(gridWidth / itemWidth);
                lineCount = Mathf.CeilToInt(itemCount / (float)columnCount);
                gridHeight = lineCount * itemHeight;
            }

            var spaceBetweenTiles = (gridWidth - (columnCount * itemWidth)) / (columnCount + 1f);

            Rect gridRect = new Rect(0, m_ScrollPosition.y, gridWidth, availableHeight);
            Rect itemRect = new Rect(spaceBetweenTiles, 0, itemWidth, itemHeight);

            m_ItemVisibleRegion = new Rect(m_ScrollViewOffset.x, m_ScrollViewOffset.yMax, gridWidth, availableHeight);

            GUILayout.Space(gridHeight);

            int index = 0;
            var eventType = Event.current.type;
            var mouseButton = Event.current.button;
            var mousePosition = Event.current.mousePosition;
            var isHoverGrid = !(m_AutoCompleting && m_AutoCompleteRect.Contains(GetScrollViewOffsetedMousePosition()));
            isHoverGrid &= gridRect.Contains(mousePosition);
            foreach (var item in m_FilteredItems)
            {
                if (index == m_SelectedIndex && m_FocusSelectedItem)
                {
                    FocusGridItemRect(itemRect);
                    m_FocusSelectedItem = false;
                }

                if (itemRect.Overlaps(gridRect))
                {
                    if (Event.current.isMouse && !isHoverGrid)
                    {
                        // Skip
                    }
                    else if (eventType == EventType.MouseDown && mouseButton == 0)
                    {
                        if (itemRect.Contains(mousePosition))
                            HandleMouseDown();
                    }
                    else if (Event.current.type == EventType.MouseUp || IsDragFinishedFarEnough(Event.current))
                    {
                        if (itemRect.Contains(mousePosition))
                        {
                            HandleMouseUp(index, itemCount);
                            if (index == m_SelectedIndex)
                                EditorApplication.delayCall += () => m_FocusSelectedItem = true;
                        }
                    }
                    else if (eventType == EventType.MouseDrag && m_PrepareDrag)
                    {
                        if (itemRect.Contains(mousePosition))
                            HandleMouseDrag(index, itemCount);
                    }
                    else if (eventType == EventType.Repaint)
                    {
                        DrawGridItem(index, item, itemRect, isHoverGrid);
                    }
                    else
                    {
                        item.preview = null;
                    }
                }

                itemRect = new Rect(itemRect.x + itemWidth + spaceBetweenTiles, itemRect.y, itemWidth, itemHeight);
                if (itemRect.xMax > gridWidth)
                    itemRect = new Rect(spaceBetweenTiles, itemRect.y + itemHeight, itemRect.width, itemRect.height);

                ++index;
            }
        }

        private void DrawGridItem(int index, SearchItem item, Rect itemRect, bool canHover)
        {
            //GUI.DrawTexture(itemRect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, false, 0f, Color.red, 1f, 0f);

            var itemContent = canHover ? new GUIContent("", item.GetDescription(context, true)) : GUIContent.none;
            if (m_SelectedIndex == index)
                GUI.Label(itemRect, itemContent, Styles.selectedGridItemBackground);
            else if (canHover)
                GUI.Label(itemRect, itemContent, itemRect.Contains(Event.current.mousePosition) ? Styles.itemGridBackground2 : Styles.itemGridBackground1);

            Texture2D thumbnail = null;
            var shouldFetchPreview = SearchSettings.fetchPreview && itemIconSize > 64;
            if (SearchSettings.fetchPreview && itemIconSize > 64)
            {
                thumbnail = item.preview;
                shouldFetchPreview = !thumbnail && item.provider.fetchPreview != null;
                if (shouldFetchPreview)
                {
                    var previewSize = new Vector2(itemIconSize, itemIconSize);
                    thumbnail = item.provider.fetchPreview(item, context, previewSize, FetchPreviewOptions.Preview2D | FetchPreviewOptions.Normal);
                    if (thumbnail)
                    {
                        item.preview = thumbnail;
                    }
                }
            }

            if (!thumbnail)
            {
                thumbnail = item.thumbnail;
                if (!thumbnail && item.provider.fetchThumbnail != null)
                {
                    thumbnail = item.provider.fetchThumbnail(item, context);
                    if (thumbnail && !shouldFetchPreview)
                        item.thumbnail = thumbnail;
                }
            }

            if (thumbnail)
            {
                var thumbnailRect = new Rect(itemRect.x + itemPadding, itemRect.y + itemPadding, itemIconSize, itemIconSize);
                var dw = thumbnailRect.width - thumbnail.width;
                var dh = thumbnailRect.height - thumbnail.height;
                if (dw > 0 || dh > 0)
                {
                    var scaledWidth = Mathf.Min(thumbnailRect.width, thumbnail.width);
                    var scaledHeight = Mathf.Min(thumbnailRect.height, thumbnail.height);
                    thumbnailRect = new Rect(
                        thumbnailRect.center.x - scaledWidth / 2f,
                        thumbnailRect.center.y - scaledHeight / 2f,
                        scaledWidth, scaledHeight);
                }
                GUI.DrawTexture(thumbnailRect, thumbnail, ScaleMode.ScaleToFit, true, 0f, Color.white, 0f, 4f);
            }

            var labelRect = new Rect(
                itemRect.x + itemPadding, itemRect.yMax - itemLabelHeight - itemPadding, 
                itemRect.width - itemPadding * 2f, itemLabelHeight - itemPadding);
            var maxCharLength = Utils.GetNumCharactersThatFitWithinWidth(Styles.itemLabelGrid, item.GetLabel(context, true), itemRect.width * 2f);
            var itemLabel = item.GetLabel(context);
            if (itemLabel.Length > maxCharLength)
            {
                maxCharLength = Math.Max(0, maxCharLength-3);
                itemLabel = Utils.StripHTML(itemLabel);
                itemLabel = itemLabel.Substring(0, maxCharLength / 2) + "\u2026" + itemLabel.Substring(itemLabel.Length - maxCharLength / 2);
            }
            GUI.Label(labelRect, itemLabel, Styles.itemLabelGrid);
        }

        private void FocusGridItemRect(Rect itemRect)
        {
            // Focus item
            if (itemRect.center.y <= m_ScrollPosition.y)
            {
                m_ScrollPosition.y = Mathf.Max(0, itemRect.yMin - 20);
                Repaint();
            }
            else if (itemRect.center.y > m_ScrollPosition.y + m_ItemVisibleRegion.height)
            {
                m_ScrollPosition.y = itemRect.yMin - itemRect.height;
                Repaint();
            }
        }
    }
}
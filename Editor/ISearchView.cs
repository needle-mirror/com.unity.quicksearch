using System;
using UnityEngine;

namespace Unity.QuickSearch
{
    public enum DisplayMode
    {
        None,
        List,
        Grid
    }

    public enum TextCursorPlacement
    {
        None, 

        MoveLineEnd,
        MoveLineStart,
        MoveToEndOfPreviousWord,
        MoveToStartOfNextWord,
        MoveWordLeft,
        MoveWordRight,

        Default = MoveLineEnd
    }

    /// <summary>
    /// Search view interface used by the search context to execute a few UI operations.
    /// </summary>
    public interface ISearchView
    {
        /// <summary>
        /// Returns the selected item in the view
        /// </summary>
        SearchSelection selection { get; }

        /// <summary>
        /// Return the list of all search results.
        /// </summary>
        ISearchList results { get; }

        /// <summary>
        /// Returns the current view search context
        /// </summary>
        SearchContext context { get; }

        /// <summary>
        /// Defines the size of items in the search view.
        /// </summary>
        float itemIconSize { get; set; }

        /// <summary>
        /// Indicates how the data is displayed in the UI.
        /// </summary>
        DisplayMode displayMode { get; }

        /// <summary>
        /// Allow multi-selection or not.
        /// </summary>
        bool multiselect { get; set; }

        /// <summary>
        /// Callback used to override the select behavior.
        /// </summary>
        Action<SearchItem, bool> selectCallback { get; }

        /// <summary>
        /// Callback used to filter items shown in the list.
        /// </summary>
        Func<SearchItem, bool> filterCallback { get; }

        /// <summary>
        /// Callback used to override the tracking behavior.
        /// </summary>
        Action<SearchItem> trackingCallback { get; }

        /// <summary>
        /// Update the search view with a new selection.
        /// </summary>
        /// <param name="itemIndex"></param>
        void SetSelection(params int[] selection);

        /// <summary>
        /// Add new items to the current selection
        /// </summary>
        /// <param name="selection"></param>
        void AddSelection(params int[] selection);

        /// <summary>
        /// Sets the search query text.
        /// </summary>
        /// <param name="searchText">Text to be displayed in the search view.</param>
        void SetSearchText(string searchText, TextCursorPlacement moveCursor = TextCursorPlacement.Default);

        /// <summary>
        /// Open the associated filter window.
        /// </summary>
        void PopFilterWindow();

        /// <summary>
        /// Make sure the search is now focused.
        /// </summary>
        void Focus();

        /// <summary>
        /// Triggers a refresh of the search view, re-fetching all the search items from enabled search providers.
        /// </summary>
        void Refresh();

        /// <summary>
        /// Request the search view to repaint itself
        /// </summary>
        void Repaint();

        /// <summary>
        /// Execute an search item action.
        /// </summary>
        void ExecuteAction(SearchAction action, SearchItem[] items, SearchContext context, bool endSearch = true);

        /// <summary>
        /// Close the search view
        /// </summary>
        void Close();

        /// <summary>
        /// Show a contextual menu for the specified item.
        /// </summary>
        void ShowItemContextualMenu(SearchItem item, SearchContext context, Rect contextualActionPosition);
    }
}
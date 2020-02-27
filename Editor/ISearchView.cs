using System.Collections.Generic;

namespace Unity.QuickSearch
{
    public enum DisplayMode
    {
        None,
        List,
        Grid
    }

    /// <summary>
    /// Search view interface used by the search context to execute a few UI operations.
    /// </summary>
    public interface ISearchView
    {
        /// <summary>
        /// Returns the selected item in the view
        /// </summary>
        SearchItem selection { get; }

        /// <summary>
        /// Return the list of all search results.
        /// </summary>
        IEnumerable<SearchItem> results { get; }

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
        /// Sets the search query text.
        /// </summary>
        /// <param name="searchText">Text to be displayed in the search view.</param>
        void SetSearchText(string searchText);

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
        /// Load all global Window preferences. Will reset the filters according to what is saved in preferences.
        /// </summary>
        void LoadGlobalSettings();
    }
}
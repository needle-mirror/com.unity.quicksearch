//#define QUICKSEARCH_DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor;

namespace Unity.QuickSearch
{
    /// <summary>
    /// Various search options used to fetch items.
    /// </summary>
    [Flags]
    public enum SearchFlags
    {
        /// <summary>
        /// No specific search options.
        /// </summary>
        None = 0,

        /// <summary>
        /// Search items are fetch synchronously.
        /// </summary>
        Synchronous = 1 << 0,

        /// <summary>
        /// Fetch items will be sorted by the search service.
        /// </summary>
        Sorted = 1 << 1,

        /// <summary>
        /// Send the first items asynchronously
        /// </summary>
        FirstBatchAsync = 1 << 2,

        /// <summary>
        /// Sets the search to search for all results.
        /// </summary>
        WantsMore = 1 << 3,

        /// <summary>
        ///
        /// </summary>
        BasicIndexing = 1 << 5,

        /// <summary>
        ///
        /// </summary>
        FullIndexing = 1 << 6,

        IndexingMask = BasicIndexing | FullIndexing,

        Default = Sorted
    }

    /// <summary>
    /// The search context contains many fields to process a search query.
    /// </summary>
    [DebuggerDisplay("{m_SearchText}")]
    public class SearchContext : IDisposable
    {
        private static readonly string[] k_Empty = new string[0];
        private string m_SearchText = "";
        private string m_CachedPhrase;

        [DebuggerDisplay("{provider.name.id} - Enabled: {isEnabled}")]
        internal class FilterDesc
        {
            public SearchProvider provider;
            public bool isEnabled;
        }
        private List<FilterDesc> m_ProviderDescs = new List<FilterDesc>();
        private bool m_Disposed = false;
        internal IEnumerable<FilterDesc> filters => m_ProviderDescs;

        public SearchContext(IEnumerable<SearchProvider> providers, string searchText, SearchFlags options)
        {
            this.providers = providers.ToList();
            this.searchText = searchText;
            this.options = options;
        }

        public SearchContext(IEnumerable<SearchProvider> providers, string searchText)
            : this(providers, searchText, SearchFlags.Default)
        {
        }

        public SearchContext(IEnumerable<SearchProvider> providers)
            : this(providers, String.Empty, SearchFlags.Default)
        {
        }

        ~SearchContext()
        {
            Dispose(false);
        }

        public void ResetFilter(bool enableAll)
        {
            foreach (var t in m_ProviderDescs)
                t.isEnabled = enableAll;
        }

        public void SetFilter(string providerId, bool isEnabled)
        {
            var index = m_ProviderDescs.FindIndex(t => t.provider.name.id == providerId);
            if (index != -1)
            {
                m_ProviderDescs[index].isEnabled = isEnabled;
            }
        }

        public bool IsEnabled(string providerId)
        {
            var index = m_ProviderDescs.FindIndex(t => t.provider.name.id == providerId);
            if (index != -1)
            {
                return m_ProviderDescs[index].isEnabled;
            }

            return false;
        }

        public void ReportProgress(float progress = 0f, string status = null)
        {
            SearchService.ReportProgress(this, progress, status);
        }

        internal void SetFilteredProviders(IEnumerable<string> providerIds)
        {
            ResetFilter(false);
            foreach (var id in providerIds)
                SetFilter(id, true);
        }

        private void BeginSession()
        {
            #if QUICKSEARCH_DEBUG
            UnityEngine.Debug.Log($"Start search session {String.Join(", ", m_SearchProviders.Select(p=>p.name.id))} -> {searchText}");
            #endif

            foreach (var desc in m_ProviderDescs)
            {
                using (var enableTimer = new DebugTimer(null))
                {
                    desc.provider.OnEnable(enableTimer.timeMs);
                }
            }
        }

        private void EndSession()
        {
            sessions.StopAllAsyncSearchSessions();
            sessions.Clear();

            foreach (var desc in m_ProviderDescs)
                desc.provider.OnDisable();

            #if QUICKSEARCH_DEBUG
            UnityEngine.Debug.Log($"End search session {String.Join(", ", m_SearchProviders.Select(p => p.name.id))}");
            #endif
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!m_Disposed)
            {
                EndSession();

                m_ProviderDescs = null;
                m_Disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Raw search text (i.e. what is in the search text box)
        /// </summary>
        public string searchText
        {
            get => m_SearchText;

            set
            {
                if (m_SearchText.Equals(value))
                    return;

                m_SearchText = value ?? String.Empty;

                // Reset a few values
                filterId = actionId = null;
                textFilters = searchWords = k_Empty;
                searchQuery = searchText ?? String.Empty;

                if (String.IsNullOrEmpty(searchQuery))
                    return;

                var isActionQuery = searchQuery.StartsWith(">", StringComparison.Ordinal);
                if (isActionQuery)
                {
                    var searchIndex = 1;
                    var potentialCommand = Utils.GetNextWord(searchQuery, ref searchIndex).ToLowerInvariant();
                    if (SearchService.ActionIdToProviders.ContainsKey(potentialCommand))
                    {
                        // We are in command mode:
                        actionId = potentialCommand;
                        searchQuery = searchQuery.Remove(0, searchIndex).Trim();
                    }
                }
                else
                {
                    foreach (var providerFilterId in m_ProviderDescs.Select(desc => desc.provider.filterId))
                    {
                        if (searchQuery.StartsWith(providerFilterId, StringComparison.OrdinalIgnoreCase))
                        {
                            filterId = providerFilterId;
                            searchQuery = searchQuery.Remove(0, providerFilterId.Length).Trim();
                            break;
                        }
                    }
                }

                var tokens = searchQuery.ToLowerInvariant().Split(' ').ToArray();
                searchWords = tokens.Where(t => t.IndexOf(':') == -1).ToArray();
                textFilters = tokens.Where(t => t.IndexOf(':') != -1).ToArray();
            }
        }

        /// <summary>
        /// Returns the time it took to evaluate the last query in milliseconds.
        /// </summary>
        internal double searchElapsedTime => (searchFinishTime - searchStartTime) * 1000.0;
        internal double searchStartTime { get; set; } = 0;
        internal double searchFinishTime { get; set; } = 0;

        /// <summary>
        /// Progress handle to set the search current progress.
        /// </summary>
        public int progressId { get; set; } = -1;

        /// <summary>
        /// Processed search query (no filterId, no textFilters)
        /// </summary>
        public string searchQuery { get; private set; } = String.Empty;

        /// <summary>
        /// Search query tokenized by words. All text filters are discarded and all words are lower cased.
        /// </summary>
        public string[] searchWords { get; private set; } = k_Empty;

        /// <summary>
        /// Returns a phrase that contains only words separated by spaces
        /// </summary>
        internal string searchPhrase
        {
            get
            {
                if (m_CachedPhrase == null && searchWords.Length > 0)
                    m_CachedPhrase = string.Join(" ", searchWords).Trim();
                return m_CachedPhrase ?? String.Empty;
            }
        }

        /// <summary>
        /// All tokens containing a colon (':')
        /// </summary>
        public string[] textFilters { get; private set; } = k_Empty;

        /// <summary>
        /// Editor window that initiated the search.
        /// </summary>
        public EditorWindow focusedWindow { get; internal set; }

        /// <summary>
        /// Search context options
        /// </summary>
        public SearchFlags options { get; set; }

        /// <summary>
        /// Indicates if the search should return results as many as possible.
        /// </summary>
        public bool wantsMore
        {
            get
            {
                return options.HasFlag(SearchFlags.WantsMore);
            }

            set
            {
                if (value)
                    options |= SearchFlags.WantsMore;
                else
                    options &= ~SearchFlags.WantsMore;
            }
        }

        /// <summary>
        /// Indicates that the search results should be filter for this type.
        /// </summary>
        [CanBeNull] internal Type filterType { get; set; }
//
        /// <summary>
        /// The search action id to be executed.
        /// </summary>
        [CanBeNull] public string actionId { get; private set; }

        /// <summary>
        /// Explicit filter id. Usually it is the first search token like h:, p: to do an explicit search for a given provider.
        /// Can be null
        /// </summary>
        [CanBeNull] public string filterId { get; private set; }

        /// <summary>
        /// Returns a unique code that represents filtered providers for the current context.
        /// </summary>
        internal int scopeHash => filters.Where(d => d.isEnabled && !d.provider.isExplicitProvider)
                .Select(d => d.provider.filterId.GetHashCode())
                .Aggregate((h1, h2) => (h1 ^ h2).GetHashCode());

        /// <summary>
        /// Which Providers are active for this particular context.
        /// </summary>
        public IEnumerable<SearchProvider> providers
        {
            get
            {
                if (actionId != null)
                    return m_ProviderDescs.Where(d => d.provider.actions.Any(a => a.Id == actionId)).Select(d => d.provider);

                if (filterId != null)
                    return m_ProviderDescs.Where(d => d.provider.filterId == filterId).Select(d => d.provider);

                return m_ProviderDescs.Where(d => d.isEnabled && !d.provider.isExplicitProvider).Select(d => d.provider);
            }

            private set
            {
                if (m_ProviderDescs?.Count > 0)
                    EndSession();

                if (value != null)
                    m_ProviderDescs = value.Where(provider => provider.active).Select(provider => new FilterDesc() { provider = provider, isEnabled = true }).ToList();
                else
                    m_ProviderDescs.Clear();

                BeginSession();
            }
        }

        /// <summary>
        /// Search view holding and presenting the search results.
        /// </summary>
        [CanBeNull] public ISearchView searchView { get; internal set; }

        /// <summary>
        /// An instance of MultiProviderAsyncSearchSession holding all the async search sessions associated with this search context.
        /// </summary>
        internal MultiProviderAsyncSearchSession sessions { get; } = new MultiProviderAsyncSearchSession();

        /// <summary>
        /// Indicates if an asynchronous search is currently in progress for this context.
        /// </summary>
        public bool searchInProgress => sessions.searchInProgress;

        /// <summary>
        /// Return the search result selection if any.
        /// </summary>
        public SearchSelection selection => searchView?.selection;

        /// <summary>
        /// This event is used to receive any async search result.
        /// </summary>
        public event Action<SearchContext, IEnumerable<SearchItem>> asyncItemReceived
        {
            add
            {
                lock (this)
                    sessions.asyncItemReceived += value;
            }
            remove
            {
                lock (this)
                    sessions.asyncItemReceived -= value;
            }
        }

        public event Action<SearchContext> sessionStarted
        {
            add
            {
                lock (this)
                    sessions.sessionStarted += value;
            }
            remove
            {
                lock (this)
                    sessions.sessionStarted -= value;
            }
        }

        public event Action<SearchContext> sessionEnded
        {
            add
            {
                lock (this)
                    sessions.sessionEnded += value;
            }
            remove
            {
                lock (this)
                    sessions.sessionEnded -= value;
            }
        }
    }
}
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
    /// The search context contains many fields to process a search query.
    /// </summary>
    [DebuggerDisplay("{m_SearchText}")]
    public class SearchContext : IDisposable
    {
        private static readonly string[] k_Empty = new string[0];
        private string m_SearchText = "";
        private string m_CachedPhrase;
        private readonly object m_AsyncItemReceivedEventLock = new object();
        private List<SearchProvider> m_SearchProviders = new List<SearchProvider>();
        private bool m_Disposed = false;

        public SearchContext(IEnumerable<SearchProvider> providers, string searchText = "")
        {
            this.providers = providers.ToList();
            this.searchText = searchText;
        }

        private void BeginSession()
        {
            #if QUICKSEARCH_DEBUG
            UnityEngine.Debug.Log($"Start search session {String.Join(", ", m_SearchProviders.Select(p=>p.name.id))} -> {searchText}");
            #endif

            foreach (var provider in m_SearchProviders)
            {
                using (var enableTimer = new DebugTimer(null))
                {
                    provider.onEnable?.Invoke();
                    provider.enableTime = enableTimer.timeMs;
                }
            }
        }

        private void EndSession()
        {
            sessions.StopAllAsyncSearchSessions();
            sessions.Clear();

            foreach (var provider in m_SearchProviders)
                provider.onDisable?.Invoke();

            #if QUICKSEARCH_DEBUG
            UnityEngine.Debug.Log($"End search session {String.Join(", ", m_SearchProviders.Select(p => p.name.id))}");
            #endif
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!m_Disposed)
            {
                if (disposing)
                    EndSession();

                m_SearchProviders = null;
                m_Disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Raw search text (i.e. what is in the search text box)
        /// </summary>
        public string searchText
        { 
            get
            {
                return m_SearchText;
            }
            
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
                    foreach (var providerFilterId in m_SearchProviders.Select(p => p.filterId))
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
                    m_CachedPhrase = String.Join(" ", searchWords).Trim();
                return m_CachedPhrase ?? String.Empty;
            }
        }

        /// <summary>
        /// All tokens containing a colon (':')
        /// </summary>
        public string[] textFilters { get; private set; } = k_Empty;

        /// <summary>
        /// Mark the number of item found after running the search.
        /// </summary>
        public int totalItemCount { get; internal set; } = 0;

        /// <summary>
        /// Editor window that initiated the search.
        /// </summary>
        public EditorWindow focusedWindow { get; internal set; }

        /// <summary>
        /// Indicates if the search should return results as many as possible.
        /// </summary>
        public bool wantsMore { get; set; }

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
        /// Which Providers are active for this particular context.
        /// </summary>
        public IEnumerable<SearchProvider> providers
        {
            get
            {
                if (actionId != null)
                    return m_SearchProviders.Where(p => p.actions.Any(a => a.Id == actionId));

                if (filterId != null)
                    return m_SearchProviders.Where(p => p.filterId == filterId);

                return m_SearchProviders.Where(p=>!p.isExplicitProvider);
            }

            set
            {
                if (m_SearchProviders?.Count > 0)
                    EndSession();

                if (value != null)
                    m_SearchProviders = value.Where(p => p.active).ToList();
                else
                    m_SearchProviders.Clear();

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
        /// This event is used to receive any async search result.
        /// </summary>
        public event Action<IEnumerable<SearchItem>> asyncItemReceived
        {
            add
            {
                lock (m_AsyncItemReceivedEventLock)
                {
                    sessions.asyncItemReceived += value;
                }
            }
            remove
            {
                lock (m_AsyncItemReceivedEventLock)
                {
                    sessions.asyncItemReceived -= value;
                }
            }
        }
    }
}
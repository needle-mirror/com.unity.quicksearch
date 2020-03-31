//#define QUICKSEARCH_DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Unity.QuickSearch
{
    /// <summary>
    /// Attribute used to declare a static method that will create a new search provider at load time.
    /// </summary>
    public class SearchItemProviderAttribute : Attribute
    {
    }

    /// <summary>
    /// Attribute used to declare a static method that define new actions for specific search providers.
    /// </summary>
    public class SearchActionsProviderAttribute : Attribute
    {
    }

    /// <summary>
    /// Various search options used to fetch items.
    /// </summary>
    [Flags] public enum SearchFlags
    {
        /// <summary>
        /// No specific search options.
        /// </summary>
        None = 0,

        /// <summary>
        /// Search items are fetch synchronously.
        /// </summary>
        Synchronous  = 1 << 0,

        /// <summary>
        /// Fetch items will be sorted by the search service.
        /// </summary>
        Sorted = 1 << 1,

        Default = Sorted
    }

    /// <summary>
    /// Principal Quick Search API to initiate searches and fetch results.
    /// </summary>
    public static class SearchService
    {
        internal const string prefKey = "quicksearch";

        const string k_ActionQueryToken = ">";

        private const int k_MaxFetchTimeMs = 100;

        internal static Dictionary<string, List<string>> ActionIdToProviders { get; private set; } 

        /// <summary>
        /// Returns the list of all providers (active or not)
        /// </summary>
        public static List<SearchProvider> Providers { get; private set; }

        /// <summary>
        /// Returns the list of providers sorted by priority.
        /// </summary>
        public static IEnumerable<SearchProvider> OrderedProviders
        {
            get
            {
                return Providers.OrderBy(p => p.priority + (p.isExplicitProvider ? 100000 : 0));
            }
        }

        static SearchService()
        {
            Refresh();
        }

        /// <summary>
        /// Returns the data of a search provider given its ID.
        /// </summary>
        /// <param name="providerId">Unique ID of the provider</param>
        /// <returns>The matching provider</returns>
        public static SearchProvider GetProvider(string providerId)
        {
            return Providers.Find(p => p.name.id == providerId);
        }

        /// <summary>
        /// Returns the search action data for a given provider and search action id.
        /// </summary>
        /// <param name="provider">Provider to lookup</param>
        /// <param name="actionId">Unique action ID within the provider.</param>
        /// <returns>The matching action</returns>
        public static SearchAction GetAction(SearchProvider provider, string actionId)
        {
            if (provider == null)
                return null;
            return provider.actions.Find(a => a.Id == actionId);
        }

        /// <summary>
        /// Activate or deactivate a search provider. 
        /// Call Refresh after this to take effect on the next search.
        /// </summary>
        /// <param name="providerId">Provider id to activate or deactivate</param>
        /// <param name="active">Activation state</param>
        public static void SetActive(string providerId, bool active = true)
        {
            var provider = Providers.FirstOrDefault(p => p.name.id == providerId);
            if (provider == null)
                return;
            EditorPrefs.SetBool($"{prefKey}.{providerId}.active", active);
            provider.active = active;
        }

        /// <summary>
        /// Clears everything and reloads all search providers.
        /// </summary>
        /// <remarks>Use with care. Useful for unit tests.</remarks>
        public static void Refresh()
        {
            RefreshProviders();
            RefreshProviderActions();
        }

        /// <summary>
        /// Returns a list of keywords used by auto-completion for the active providers.
        /// </summary>
        /// <param name="context">Current search context</param>
        /// <param name="lastToken">Search token currently being typed.</param>
        /// <returns>A list of keywords that can be shown in an auto-complete dropdown.</returns>
        public static string[] GetKeywords(SearchContext context, string lastToken)
        {
            var keywords = new List<string>();
            if (lastToken.StartsWith(k_ActionQueryToken, StringComparison.Ordinal))
            {
                keywords.AddRange(ActionIdToProviders.Keys.Select(k => k_ActionQueryToken + k));
            }
            else
            {
                foreach (var provider in context.providers)
                {
                    try
                    {
                        provider.fetchKeywords?.Invoke(context, lastToken, keywords);
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError($"Failed to get keywords with {provider.name.displayName}.\r\n{ex}");
                    }
                }
            }

            return keywords.Distinct().ToArray();
        }

        /// <summary>
        /// Create context from a list of provider id.
        /// </summary>
        /// <param name="providerIds">List of provider id</param>
        /// <param name="searchQuery">seach Query</param>
        /// <returns>New SearchContext</returns>
        public static SearchContext CreateContext(IEnumerable<string> providerIds, string searchText = "")
        {
            return new SearchContext(providerIds.Select(id => GetProvider(id)).Where(p => p != null), searchText);
        }

        internal static SearchContext CreateContext(SearchProvider provider, string searchText = "")
        {
            return new SearchContext(new [] {provider}, searchText);
        }

        internal static SearchContext CreateContext(string providerId, string searchText = "")
        {
            return CreateContext(new []{providerId}, searchText);
        }

        /// <summary>
        /// Initiate a search and return all search items matching the search context. Other items can be found later using the asynchronous searches.
        /// </summary>
        /// <param name="context">The current search context</param>
        /// <returns>A list of search items matching the search query.</returns>
        public static List<SearchItem> GetItems(SearchContext context, SearchFlags options = SearchFlags.Default)
        {
            // Stop all search sessions every time there is a new search.
            context.sessions.StopAllAsyncSearchSessions();

            var allItems = new List<SearchItem>(3);
            #if QUICKSEARCH_DEBUG
            var debugProviderList = context.providers.ToList();
            using (new DebugTimer($"Search get items {String.Join(", ", debugProviderList.Select(p=>p.name.id))} -> {context.searchQuery}"));
            #endif
            foreach (var provider in context.providers)
            {
                using (var fetchTimer = new DebugTimer(null))
                {
                    try
                    {
                        var iterator = provider.fetchItems(context, allItems, provider);
                        if (iterator != null)
                        {
                            if (options.HasFlag(SearchFlags.Synchronous))
                            {
                                var stackedEnumerator = new StackedEnumerator<SearchItem>(iterator);
                                while (stackedEnumerator.MoveNext())
                                {
                                    if (stackedEnumerator.Current != null)
                                        allItems.Add(stackedEnumerator.Current);
                                }
                            }
                            else
                            {
                                var session = context.sessions.GetProviderSession(provider.name.id);
                                session.Reset(iterator, k_MaxFetchTimeMs);
                                session.Start();
                                if (!session.FetchSome(allItems, k_MaxFetchTimeMs))
                                    session.Stop();
                            }
                        }
                        provider.RecordFetchTime(fetchTimer.timeMs);
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogException(new Exception($"Failed to get fetch {provider.name.displayName} provider items.", ex));
                    }
                }
            }

            if (!options.HasFlag(SearchFlags.Sorted))
                return allItems;
            
            allItems.Sort(SortItemComparer);
            return allItems.GroupBy(i => i.id).Select(i => i.First()).ToList();
        }

        /// <summary>
        /// Execute a search request that will fetch search results asynchronously.
        /// </summary>
        /// <param name="context">Search context used to track asynchronous request.</param>
        /// <returns>Asynchronous list of search items.</returns>
        public static ISearchList Request(SearchContext context, SearchFlags options = SearchFlags.None)
        {
            if (options.HasFlag(SearchFlags.Synchronous))
            {
                throw new NotSupportedException($"Use {nameof(SearchService)}.{nameof(GetItems)}(context, " +
                                    $"{nameof(SearchFlags)}.{nameof(SearchFlags.Synchronous)}) to fetch items synchronously.");
            }

            ISearchList results = null;
            if (options.HasFlag(SearchFlags.Sorted))
                results = new SortedSearchList(context);
            else
                results = new AsyncSearchList(context);

            results.AddItems(GetItems(context, options));
            return results;
        }

        private static int SortItemComparer(SearchItem item1, SearchItem item2)
        {
            var po = item1.provider.priority.CompareTo(item2.provider.priority);
            if (po != 0)
                return po;
            po = item1.score.CompareTo(item2.score);
            if (po != 0)
                return po;
            return String.Compare(item1.id, item2.id, StringComparison.Ordinal);
        }

        internal static void RefreshProviders()
        {
            Providers = Utils.GetAllMethodsWithAttribute<SearchItemProviderAttribute>().Select(methodInfo =>
            {
                try
                {
                    SearchProvider fetchedProvider = null;
                    using (var fetchLoadTimer = new DebugTimer(null))
                    {
                        fetchedProvider = methodInfo.Invoke(null, null) as SearchProvider;
                        if (fetchedProvider == null)
                            return null;

                        fetchedProvider.loadTime = fetchLoadTimer.timeMs;

                        // Load per provider user settings
                        fetchedProvider.active = EditorPrefs.GetBool($"{prefKey}.{fetchedProvider.name.id}.active", fetchedProvider.active);
                        fetchedProvider.priority = EditorPrefs.GetInt($"{prefKey}.{fetchedProvider.name.id}.priority", fetchedProvider.priority);
                    }
                    return fetchedProvider;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogException(ex);
                    return null;
                }
            }).Where(provider => provider != null).ToList();
        }

        internal static void RefreshProviderActions()
        {
            ActionIdToProviders = new Dictionary<string, List<string>>();
            foreach (var action in Utils.GetAllMethodsWithAttribute<SearchActionsProviderAttribute>()
                                        .SelectMany(methodInfo => methodInfo.Invoke(null, null) as IEnumerable<object>)
                                        .Where(a => a != null).Cast<SearchAction>())
            {
                var provider = Providers.Find(p => p.name.id == action.providerId);
                if (provider == null)
                    continue;
                provider.actions.Add(action);
                if (!ActionIdToProviders.TryGetValue(action.Id, out var providerIds))
                {
                    providerIds = new List<string>();
                    ActionIdToProviders[action.Id] = providerIds;
                }
                providerIds.Add(provider.name.id);
            }
            SearchSettings.SortActionsPriority();
        }
    }
}

// #define QUICKSEARCH_DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;
using JetBrains.Annotations;

#if QUICKSEARCH_DEBUG
using System.Reflection;
using Debug = UnityEngine.Debug;
#endif

namespace Unity.QuickSearch
{
    public delegate Texture2D PreviewHandler(SearchItem item, SearchContext context);
    public delegate string FetchStringHandler(SearchItem item, SearchContext context);
    public delegate void ActionHandler(SearchItem item, SearchContext context);
    public delegate void StartDragHandler(SearchItem item, SearchContext context);
    public delegate void TrackSelectionHandler(SearchItem item, SearchContext context);
    public delegate bool EnabledHandler(SearchItem item, SearchContext context);
    public delegate void GetItemsHandler(SearchContext context, List<SearchItem> items, SearchProvider provider);
    public delegate void GetKeywordsHandler(SearchContext context, string lastToken, List<string> keywords);
    public delegate bool IsEnabledForContextualSearch();

    interface ISearchView
    {
        void SetSearchText(string searchText);
        void PopFilterWindow();
    }

    public class SearchAction
    {
        public const string kContextualMenuAction = "context";
        public SearchAction(string type, GUIContent content)
        {
            providerId = type;
            this.content = content;
            isEnabled = (item, context) => true;
        }

        public SearchAction(string type, string name, Texture2D icon = null, string tooltip = null)
            : this(type, new GUIContent(name, icon, tooltip ?? name))
        {
        }

        public string Id => content.text;
        public string DisplayName => content.tooltip;
        public bool closeWindowAfterExecution = true;

        // Unique (for a given provider) id of the action 
        public string providerId;
        public GUIContent content;
        // Called when an item is executed with this action
        public ActionHandler handler;
        // Called before displaying the menu to see if an action is available for a given item.
        public EnabledHandler isEnabled;
    }

    [Flags]
    public enum SearchItemDescriptionFormat
    {
        None = 0,
        Ellipsis = 1 << 0,
        RightToLeft = 1 << 1,
        Highlight = 1 << 2,
        FuzzyHighlight = 1 << 3
    }

    [DebuggerDisplay("{id} | {label}")]
    public class SearchItem : IEqualityComparer<SearchItem>, IEquatable<SearchItem>
    {
        // Unique id of this item among this provider items.
        public readonly string id;
        // The item score can affect how the item gets sorted within the same provider.
        public int score;
        // Display name of the item
        public string label;
        // If no description is provided, SearchProvider.fetchDescription will be called when the item is first displayed.
        public string description;
        // If true - description already has formatting / rich text
        public SearchItemDescriptionFormat descriptionFormat;
        // If no thumbnail are provider, SearchProvider.fetchThumbnail will be called when the item is first displayed.
        public Texture2D thumbnail;
        // Back pointer to the provider.
        public SearchProvider provider;
        // Search provider defined content
        public object data;

        public SearchItem(string _id)
        {
            id = _id;
        }

        public bool Equals(SearchItem x, SearchItem y)
        {
            return x.id == y.id;
        }

        public int GetHashCode(SearchItem obj)
        {
            return obj.id.GetHashCode();
        }

        public bool Equals(SearchItem other)
        {
            return id == other.id;
        }
    }

    public class SearchFilter
    {
        [DebuggerDisplay("{name.displayName}")]
        public class Entry
        {
            public Entry(NameId name)
            {
                this.name = name;
                isEnabled = true;
            }

            public NameId name;
            public bool isEnabled;
        }

        [DebuggerDisplay("{entry.name.displayName} expanded:{isExpanded}")]
        public class ProviderDesc
        {
            public ProviderDesc(NameId name, SearchProvider provider)
            {
                entry = new Entry(name);
                categories = new List<Entry>();
                isExpanded = false;
                this.provider = provider;
            }

            public int priority => provider.priority;

            public Entry entry;
            public bool isExpanded;
            public List<Entry> categories;
            public SearchProvider provider;
        }

        public bool allActive { get; internal set; }

        public List<SearchProvider> filteredProviders;
        public List<ProviderDesc> providerFilters;

        private List<SearchProvider> m_Providers;
        public List<SearchProvider> Providers
        {
            get => m_Providers;

            set
            {
                m_Providers = value;
                providerFilters.Clear();
                filteredProviders.Clear();
                foreach (var provider in m_Providers)
                {
                    var providerFilter = new ProviderDesc(new NameId(provider.name.id, GetProviderNameWithFilter(provider)), provider);
                    providerFilters.Add(providerFilter);
                    foreach (var subCategory in provider.subCategories)
                    {
                        providerFilter.categories.Add(new Entry(subCategory));
                    }
                }
                UpdateFilteredProviders();
            }
        }

        public SearchFilter()
        {
            filteredProviders = new List<SearchProvider>();
            providerFilters = new List<ProviderDesc>();
        }

        public void ResetFilter(bool enableAll)
        {
            allActive = enableAll;
            foreach (var providerDesc in providerFilters)
                SetFilterInternal(enableAll, providerDesc.entry.name.id);
            UpdateFilteredProviders();
        }

        public void SetFilter(bool isEnabled, string providerId, string subCategory = null)
        {
            if (SetFilterInternal(isEnabled, providerId, subCategory))
            {
                UpdateFilteredProviders();
            }
        }

        public void SetExpanded(bool isExpanded, string providerId)
        {
            var providerDesc = providerFilters.Find(pd => pd.entry.name.id == providerId);
            if (providerDesc != null)
            {
                providerDesc.isExpanded = isExpanded;
            }
        }

        public bool IsEnabled(string providerId, string subCategory = null)
        {
            var desc = providerFilters.Find(pd => pd.entry.name.id == providerId);
            if (desc != null)
            {
                if (subCategory == null)
                {
                    return desc.entry.isEnabled;
                }

                foreach (var cat in desc.categories)
                {
                    if (cat.name.id == subCategory)
                        return cat.isEnabled;
                }
            }

            return false;
        }

        public static string GetProviderNameWithFilter(SearchProvider provider)
        {
            return string.IsNullOrEmpty(provider.filterId) ? provider.name.displayName : provider.name.displayName + " (" + provider.filterId + ")";
        }

        public List<Entry> GetSubCategories(SearchProvider provider)
        {
            var desc = providerFilters.Find(pd => pd.entry.name.id == provider.name.id);
            return desc?.categories;
        }

        internal void UpdateFilteredProviders()
        {
            filteredProviders = Providers.Where(p => IsEnabled(p.name.id)).ToList();
        }

        internal bool SetFilterInternal(bool isEnabled, string providerId, string subCategory = null)
        {
            var providerDesc = providerFilters.Find(pd => pd.entry.name.id == providerId);
            if (providerDesc != null)
            {
                if (subCategory == null)
                {
                    providerDesc.entry.isEnabled = isEnabled;
                    foreach (var cat in providerDesc.categories)
                    {
                        cat.isEnabled = isEnabled;
                    }
                }
                else
                {
                    foreach (var cat in providerDesc.categories)
                    {
                        if (cat.name.id == subCategory)
                        {
                            cat.isEnabled = isEnabled;
                            if (isEnabled)
                                providerDesc.entry.isEnabled = true;
                        }
                    }
                }

                return true;
            }

            return false;
        }
    }

    [DebuggerDisplay("{id}")]
    public class NameId
    {
        public NameId(string id, string displayName = null)
        {
            this.id = id;
            this.displayName = displayName ?? id;
        }

        public string id;
        public string displayName;
    }

    [DebuggerDisplay("{name.id}")]
    public class SearchProvider
    {
        internal const int k_RecentUserScore = -99;

        public SearchProvider(string id, string displayName = null)
        {
            name = new NameId(id, displayName);
            actions = new List<SearchAction>();
            fetchItems = (context, items, provider) => {};
            fetchThumbnail = (item, context) => item.thumbnail ?? Icons.quicksearch;
            fetchLabel = (item, context) => item.label ?? item.id ?? String.Empty;
            fetchDescription = (item, context) => item.description ?? String.Empty;
            subCategories = new List<NameId>();
            priority = 100;
            fetchTimes = new double[10];
            fetchTimeWriteIndex = 0;
        }

        public SearchItem CreateItem(string id, int score, string label, string description, Texture2D thumbnail, object data)
        {
            // If the user searched that item recently,
            // let give it a good score so it gets sorted first.
            if (SearchService.IsRecent(id))
                score = Math.Min(k_RecentUserScore, score);

            return new SearchItem(id)
            {
                score = score,
                label = label,
                description = description,
                descriptionFormat = SearchItemDescriptionFormat.Highlight | SearchItemDescriptionFormat.Ellipsis,
                thumbnail = thumbnail,
                provider = this,
                data = data
            };
        }

        public SearchItem CreateItem(string id, string label = null, string description = null, Texture2D thumbnail = null, object data = null)
        {
            return CreateItem(id, 0, label, description, thumbnail, data);
        }

        public static bool MatchSearchGroups(SearchContext context, string content, bool useLowerTokens = false)
        {
            return MatchSearchGroups(context.searchQuery,
                useLowerTokens ? context.tokenizedSearchQueryLower : context.tokenizedSearchQuery, content, out _, out _, 
                useLowerTokens ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }

        public void RecordFetchTime(double t)
        {
            fetchTimes[fetchTimeWriteIndex] = t;
            fetchTimeWriteIndex = SearchService.Wrap(fetchTimeWriteIndex + 1, fetchTimes.Length);
        }

        private static bool MatchSearchGroups(string searchContext, string[] tokens, string content, out int startIndex, out int endIndex, StringComparison sc = StringComparison.OrdinalIgnoreCase)
        {
            startIndex = endIndex = -1;
            if (content == null)
                return false;

            if (string.IsNullOrEmpty(searchContext) || searchContext == content)
            {
                startIndex = 0;
                endIndex = content.Length - 1;
                return true;
            }

            // Each search group is space separated
            // Search group must match in order and be complete.
            var searchGroups = tokens;
            var startSearchIndex = 0;
            foreach (var searchGroup in searchGroups)
            {
                if (searchGroup.Length == 0)
                    continue;

                startSearchIndex = content.IndexOf(searchGroup, startSearchIndex, sc);
                if (startSearchIndex == -1)
                {
                    return false;
                }

                startIndex = startIndex == -1 ? startSearchIndex : startIndex;
                startSearchIndex = endIndex = startSearchIndex + searchGroup.Length - 1;
            }

            return startIndex != -1 && endIndex != -1;
        }

        public double avgTime
        {
            get
            {
                double total = 0.0;
                int validTimeCount = 0;
                foreach (var t in fetchTimes)
                {
                    if (t > 0.0)
                    {
                        total += t;
                        validTimeCount++;
                    }
                }

                if (validTimeCount == 0)
                    return 0.0;

                return total / validTimeCount;
            }
        }

        // Unique id of the provider
        public NameId name;
        // Text token use to "filter" a provider (ex:  "me:", "p:", "s:")
        public string filterId;
        // This provider is only active when specified explicitly using his filterId
        public bool isExplicitProvider;
        // Handler used to fetch and format the label of a search item.
        public FetchStringHandler fetchLabel;
        // Handler to provider an async description for an item. Will be called when the item is about to be displayed.
        // allow a plugin provider to only fetch long description when they are needed.
        public FetchStringHandler fetchDescription;
        // Handler to provider an async thumbnail for an item. Will be called when the item is about to be displayed.
        // allow a plugin provider to only fetch/generate preview when they are needed.
        public PreviewHandler fetchThumbnail;
        // If implemented, it means the item supports drag. It is up to the SearchProvider to properly setup the DragAndDrop manager.
        public StartDragHandler startDrag;
        // Called when the selection changed and can be tracked.
        public TrackSelectionHandler trackSelection;
        // MANDATORY: Handler to get items for a given search context. 
        public GetItemsHandler fetchItems;
        // Provider can return a list of words that will help the user complete his search query
        public GetKeywordsHandler fetchKeywords;
        // List of subfilters that will be visible in the FilterWindow for a given SearchProvider (see AssetProvider for an example).
        public List<NameId> subCategories;
        // Called when the QuickSearchWindow is opened. Allow the Provider to perform some caching.
        public Action onEnable;
        // Called when the QuickSearchWindow is closed. Allow the Provider to release cached resources.
        public Action onDisable;
        // Hint to sort the Provider. Affect the order of search results and the order in which provider are shown in the FilterWindow.
        public int priority;
        // Called when quicksearch is invoked in "contextual mode". If you return true it means the provider is enabled for this search context.
        public IsEnabledForContextualSearch isEnabledForContextualSearch;

        // INTERNAL
        internal List<SearchAction> actions;
        internal double[] fetchTimes;
        internal int fetchTimeWriteIndex;
    }

    [DebuggerDisplay("{searchQuery}")]
    public class SearchContext
    {
        // Raw search text (i.e. what is in the search text box)
        public string searchText;
        // Processed search query: filterId were removed.
        public string searchQuery;
        // Search query tokenized by words.
        public string[] tokenizedSearchQuery;
        // Search query tokenized by words all in lower case.
        public string[] tokenizedSearchQueryLower;
        // All tokens containing a colon (':')
        public string[] textFilters;
        // All sub categories related to this provider and their enabled state.
        public List<SearchFilter.Entry> categories;
        // Mark the number of item found after running the search.
        public int totalItemCount;
        // Editor window that initiated the search
        public EditorWindow focusedWindow;
        // Indicates if the search should return results as many as possible.
        public bool wantsMore;

        public string actionQueryId;
        public bool isActionQuery;

        // Async search information
        // Unique id of this search.
        public int searchId;
        // Send SearchService new asynchronous results. First parameter is the search Id who owns those results.
        public Action<int, SearchItem[]> sendAsyncItems;

        internal ISearchView searchView;

        static public readonly SearchContext Empty = new SearchContext {searchId = 0, searchText = String.Empty, searchQuery = String.Empty};

    }

    public class SearchItemProviderAttribute : Attribute
    {
    }

    public class SearchActionsProviderAttribute : Attribute
    {
    }

    public static class SearchService
    {
        public const string prefKey = "quicksearch";
        // Global settings
        const string k_FilterPrefKey = prefKey + ".filters";
        const string k_DefaultActionPrefKey = prefKey + ".defaultactions.";
        // Session settings
        const string k_LastSearchPrefKey = "last_search";
        const string k_RecentsPrefKey = "recents";

        const string k_ActionQueryToken = ">";

        private static int s_CurrentSearchId = 0;
        private static string s_LastSearch;
        private static int s_RecentSearchIndex = -1;
        private static List<int> s_UserScores = new List<int>();
        private static HashSet<int> s_SortedUserScores = new HashSet<int>();

        internal static List<string> s_RecentSearches = new List<string>(10);
        internal static List<SearchProvider> Providers { get; private set; }
        internal static IEnumerable<SearchProvider> OrderedProviders
        {
            get
            {
                return Providers.OrderBy(p => p.priority + (p.isExplicitProvider ? 100000 : 0));
            }
        }

        internal static Dictionary<string, string> TextFilterIds { get; private set; }
        internal static Dictionary<string, List<string>> ActionIdToProviders { get; private set; }
        internal static SearchFilter OverrideFilter { get; private set; }

        internal static string LastSearch
        {
            get => s_LastSearch;
            set
            {
                if (value == s_LastSearch)
                    return;
                s_LastSearch = value;
                if (String.IsNullOrEmpty(value))
                    return;
                s_RecentSearchIndex = 0;
                s_RecentSearches.Insert(0, value);
                if (s_RecentSearches.Count > 10)
                    s_RecentSearches.RemoveRange(10, s_RecentSearches.Count - 10);
                s_RecentSearches = s_RecentSearches.Distinct().ToList();
            }
        }

        internal static string CyclePreviousSearch(int shift)
        {
            if (s_RecentSearches.Count == 0)
                return s_LastSearch;

            s_RecentSearchIndex = Wrap(s_RecentSearchIndex + shift, s_RecentSearches.Count);
            
            return s_RecentSearches[s_RecentSearchIndex];
        }

        internal static int Wrap(int index, int n)
        {
            return ((index % n) + n) % n;
        }

        public static SearchFilter Filter { get; private set; }
        public static event Action<IEnumerable<SearchItem>> asyncItemReceived;
        public static event Action<string[], string[], string[]> contentRefreshed;

        static SearchService()
        {
            Refresh();
        }

        public static void SetRecent(SearchItem item)
        {
            int itemKey = item.id.GetHashCode();
            s_UserScores.Add(itemKey);
            s_SortedUserScores.Add(itemKey);
        }

        public static bool IsRecent(string id)
        {
            return s_SortedUserScores.Contains(id.GetHashCode());
        }

        public static SearchProvider GetProvider(string providerId)
        {
            return Providers.Find(p => p.name.id == providerId);
        }

        public static SearchAction GetAction(SearchProvider provider, string actionId)
        {
            if (provider == null)
                return null;
            return provider.actions.Find(a => a.Id == actionId);
        }

        internal static void Refresh()
        {
            Providers = new List<SearchProvider>();
            Filter = new SearchFilter();
            OverrideFilter = new SearchFilter();
            var settingsValid = FetchProviders();
            settingsValid = LoadGlobalSettings() || settingsValid;
            SortActionsPriority();

            if (!settingsValid)
            {
                // Override all settings
                SaveGlobalSettings();
            }
        }

        internal static bool LoadSessionSettings()
        {
            LastSearch = LoadSessionSetting(k_LastSearchPrefKey, String.Empty);
            return LoadRecents();
        }

        internal static void SaveSessionSettings()
        {
            SaveSessionSetting(k_LastSearchPrefKey, LastSearch);
            SaveRecents();
        }

        internal static bool LoadGlobalSettings()
        {
            return LoadFilters();
        }

        internal static void SaveGlobalSettings()
        {
            if (SearchService.Filter.allActive)
                SaveFilters();
        }

        [UsedImplicitly]
        internal static void Reset()
        {
            EditorPrefs.SetString(k_FilterPrefKey, null);
            Refresh();
        }

        public static string[] GetKeywords(SearchContext context, string lastToken)
        {
            #if QUICKSEARCH_DEBUG
            using (new DebugTimer("==> Get Keywords"))
            #endif
            {
                var allItems = new List<string>();
                if (context.isActionQuery && lastToken.StartsWith(k_ActionQueryToken))
                {
                    allItems.AddRange(ActionIdToProviders.Keys.Select(k => k_ActionQueryToken + k));
                }
                else
                {
                    List<SearchProvider> activeProviders = OverrideFilter.filteredProviders.Count > 0 ? OverrideFilter.filteredProviders : Filter.filteredProviders;
                    foreach (var provider in activeProviders)
                    {
                        try
                        {
                            provider.fetchKeywords?.Invoke(context, lastToken, allItems);
                        }
                        catch (Exception ex)
                        {
                            UnityEngine.Debug.LogError($"Failed to get keywords with {provider.name.displayName}.\r\n{ex}");
                        }
                    }
                }

                return allItems.Distinct().OrderBy(s=>s).ToArray();
            }
        }

        public static List<SearchItem> GetItems(SearchContext context)
        {
            PrepareSearch(context);

            if (context.isActionQuery || OverrideFilter.filteredProviders.Count > 0)
                return GetItems(context, OverrideFilter);

            if (string.IsNullOrEmpty(context.searchQuery))
                return new List<SearchItem>(0);

            return GetItems(context, Filter);
        }

        public static void Enable(SearchContext context)
        {
            s_CurrentSearchId = 0;
            LoadSessionSettings();
            PrepareSearch(context);
            foreach (var provider in Providers)
                provider.onEnable?.Invoke();
        }

        public static void Disable(SearchContext context)
        {
            asyncItemReceived = null;
            LastSearch = context.searchText;

            foreach (var provider in Providers)
                provider.onDisable?.Invoke();

            SaveSessionSettings();
            SaveGlobalSettings();
        }

        internal static void SetDefaultAction(string providerId, string actionId)
        {
            if (string.IsNullOrEmpty(providerId) || string.IsNullOrEmpty(actionId))
                return;

            EditorPrefs.SetString(k_DefaultActionPrefKey + providerId, actionId);
            SortActionsPriority();
        }

        internal static void SortActionsPriority(SearchProvider searchProvider)
        {
            if (searchProvider.actions.Count == 1)
                return;

            var defaultActionId = EditorPrefs.GetString(k_DefaultActionPrefKey + searchProvider.name.id);
            if (string.IsNullOrEmpty(defaultActionId))
                return;
            if (searchProvider.actions.Count == 0 || defaultActionId == searchProvider.actions[0].Id)
                return;

            searchProvider.actions.Sort((action1, action2) =>
            {
                if (action1.Id == defaultActionId)
                    return -1;

                if (action2.Id == defaultActionId)
                    return 1;

                return 0;
            });
        }

        internal static void SortActionsPriority()
        {
            foreach (var searchProvider in Providers)
            {
                SortActionsPriority(searchProvider);
            }
        }

        internal static void PrepareSearch(SearchContext context)
        {
            string[] overrideFilterId = null;
            context.searchQuery = context.searchText ?? String.Empty;
            context.isActionQuery = context.searchQuery.StartsWith(">");
            if (context.isActionQuery)
            {
                var searchIndex = 1;
                var potentialCommand = Utils.GetNextWord(context.searchQuery, ref searchIndex);
                if (ActionIdToProviders.ContainsKey(potentialCommand))
                {
                    // We are in command mode:
                    context.actionQueryId = potentialCommand;
                    overrideFilterId = ActionIdToProviders[potentialCommand].ToArray();
                    context.searchQuery = context.searchQuery.Remove(0, searchIndex).Trim();
                }
                else
                {
                    overrideFilterId = new string[0];
                }
            }
            else
            {
                foreach (var kvp in TextFilterIds)
                {
                    if (context.searchQuery.StartsWith(kvp.Key))
                    {
                        overrideFilterId = new [] {kvp.Value};
                        context.searchQuery = context.searchQuery.Remove(0, kvp.Key.Length).Trim();
                        break;
                    }
                }
            }

            var tokens = context.searchQuery.Split(' ');
            context.tokenizedSearchQuery = tokens.Where(t => !t.Contains(":")).ToArray();
            context.tokenizedSearchQueryLower = context.tokenizedSearchQuery.Select(t => t.ToLowerInvariant()).ToArray();
            context.textFilters = tokens.Where(t => t.Contains(":")).ToArray();

            // Reformat search text so it only contains text filter that are specific to providers and ensure those filters are at the beginning of the search text.
            context.searchQuery = string.Join(" ", context.textFilters.Concat(context.tokenizedSearchQuery));

            if (overrideFilterId != null)
            {
                OverrideFilter.ResetFilter(false);
                foreach (var provider in Providers)
                {
                    if (overrideFilterId.Contains(provider.name.id))
                    {
                        OverrideFilter.SetFilter(true, provider.name.id);
                    }
                }
            }
            else if (OverrideFilter.filteredProviders.Count > 0)
            {
                OverrideFilter.ResetFilter(false);
            }
        }

        private static List<SearchItem> GetItems(SearchContext context, SearchFilter filter)
        {
            #if QUICKSEARCH_DEBUG
            using (new DebugTimer("==> Search Items"))
            #endif
            {
                context.searchId = ++s_CurrentSearchId;
                context.sendAsyncItems = OnAsyncItemsReceived;
                var allItems = new List<SearchItem>(100);
                foreach (var provider in filter.filteredProviders)
                {
                    #if QUICKSEARCH_DEBUG
                    using (var fetchTimer = new DebugTimer($"{provider.name.id} fetch items"))
                    #else
                    using (var fetchTimer = new DebugTimer(null))
                    #endif
                    {
                        context.categories = filter.GetSubCategories(provider);
                        try
                        {
                            provider.fetchItems(context, allItems, provider);
                            provider.RecordFetchTime(fetchTimer.timeMs);
                        }
                        catch (Exception ex)
                        {
                            UnityEngine.Debug.LogError($"Failed to get fetch {provider.name.displayName} provider items.\r\n{ex}");
                        }
                    }
                }

                #if QUICKSEARCH_DEBUG
                using (new DebugTimer("<== Sort Items"))
                #endif
                {
                    SortItemList(allItems);
                    return allItems.GroupBy(i => i.id).Select(i => i.First()).ToList();
                }
            }
        }

        internal static void SortItemList(List<SearchItem> items)
        {
            items.Sort(SortItemComparer);
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

        private static void OnAsyncItemsReceived(int searchId, SearchItem[] items)
        {
            if (s_CurrentSearchId != searchId)
                return;
            EditorApplication.delayCall += () => asyncItemReceived?.Invoke(items);
        }

        private static bool FetchProviders()
        {
            try
            {
                Providers = Utils.GetAllMethodsWithAttribute<SearchItemProviderAttribute>()
                    .Select(methodInfo => methodInfo.Invoke(null, null) as SearchProvider)
                    .Where(provider => provider != null).ToList();

                ActionIdToProviders = new Dictionary<string, List<string>>();
                foreach (var action in Utils.GetAllMethodsWithAttribute<SearchActionsProviderAttribute>()
                         .SelectMany(methodInfo => methodInfo.Invoke(null, null) as IEnumerable<object>).Where(a => a != null).Cast<SearchAction>())
                {
                    var provider = Providers.Find(p => p.name.id == action.providerId);
                    if (provider != null)
                    {
                        provider.actions.Add(action);
                        if (!ActionIdToProviders.TryGetValue(action.Id, out var providerIds))
                        {
                            providerIds = new List<string>();
                            ActionIdToProviders[action.Id] = providerIds;
                        }
                        providerIds.Add(provider.name.id);
                    }
                }

                Filter.Providers = Providers.Where(p => !p.isExplicitProvider).ToList();
                OverrideFilter.Providers = Providers;
                TextFilterIds = new Dictionary<string, string>();
                foreach (var provider in Providers)
                {
                    // Load per provider user settings
                    provider.priority = EditorPrefs.GetInt($"{prefKey}.{provider.name.id}.priority", provider.priority);
                    if (string.IsNullOrEmpty(provider.filterId))
                        continue;

                    if (char.IsLetterOrDigit(provider.filterId[provider.filterId.Length - 1]))
                    {
                        UnityEngine.Debug.LogWarning($"Provider: {provider.name.id} filterId: {provider.filterId} must ends with non-alphanumeric character.");
                        continue;
                    }

                    TextFilterIds.Add(provider.filterId, provider.name.id);
                }
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        internal static bool LoadFilters()
        {
            try
            {
                var filtersStr = EditorPrefs.GetString(k_FilterPrefKey, null);
                Filter.ResetFilter(true);

                if (!string.IsNullOrEmpty(filtersStr))
                {
                    var filters = Utils.JsonDeserialize(filtersStr) as List<object>;
                    foreach (var filterObj in filters)
                    {
                        var filter = filterObj as Dictionary<string, object>;
                        if (filter == null)
                            continue;

                        var providerId = filter["providerId"] as string;
                        Filter.SetExpanded(filter["isExpanded"].ToString() == "True", providerId);
                        Filter.SetFilterInternal(filter["isEnabled"].ToString() == "True", providerId);
                        var categories = filter["categories"] as List<object>;
                        foreach (var catObj in categories)
                        {
                            var cat = catObj as Dictionary<string, object>;
                            Filter.SetFilterInternal(cat["isEnabled"].ToString() == "True", providerId, cat["id"] as string);
                        }
                    }
                }

                Filter.UpdateFilteredProviders();
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        private static bool LoadRecents()
        {
            try
            {
                var ro = Utils.JsonDeserialize(LoadSessionSetting(k_RecentsPrefKey));
                if (!(ro is List<object> recents))
                    return false;

                s_UserScores = recents.Select(Convert.ToInt32).ToList();
                s_SortedUserScores = new HashSet<int>(s_UserScores);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string FilterToString()
        {
            var filters = new List<object>();
            foreach (var providerDesc in Filter.providerFilters)
            {
                var filter = new Dictionary<string, object>
                {
                    ["providerId"] = providerDesc.entry.name.id, 
                    ["isEnabled"] = providerDesc.entry.isEnabled, 
                    ["isExpanded"] = providerDesc.isExpanded
                };
                var categories = new List<object>();
                filter["categories"] = categories;
                foreach (var cat in providerDesc.categories)
                {
                    categories.Add(new Dictionary<string, object>()
                    {
                        { "id", cat.name.id },
                        { "isEnabled", cat.isEnabled }
                    });
                }
                filters.Add(filter);
            }

            return Utils.JsonSerialize(filters);
        }

        private static string GetPrefKeyName(string suffix)
        {
            var scope = Filter.filteredProviders.Select(p => p.filterId.GetHashCode()).Aggregate((h1, h2) => (h1 ^ h2).GetHashCode());
            return $"{prefKey}.{scope}.{suffix}";
        }

        private static void SaveSessionSetting(string key, string value)
        {
            var prefKeyName = GetPrefKeyName(key);
            //UnityEngine.Debug.Log($"Saving session setting {prefKeyName} with {value}");
            EditorPrefs.SetString(prefKeyName, value);
        }

        private static string LoadSessionSetting(string key, string defaultValue = default)
        {
            var prefKeyName = GetPrefKeyName(key);
            var value = EditorPrefs.GetString(prefKeyName, defaultValue);
            //UnityEngine.Debug.Log($"Loading session setting {prefKeyName} with {value}");
            return value;
        }

        private static void SaveFilters()
        {
            var filter = FilterToString();
            EditorPrefs.SetString(k_FilterPrefKey, filter);
        }

        private static void SaveRecents()
        {
            // We only save the last 40 most recent items.
            SaveSessionSetting(k_RecentsPrefKey, Utils.JsonSerialize(s_UserScores.Skip(s_UserScores.Count - 40).ToArray()));
        }

        #region Refresh search content event
        private static double s_BatchElapsedTime;
        private static string[] s_UpdatedItems = new string[0];
        private static string[] s_RemovedItems = new string[0];
        private static string[] s_MovedItems = new string[0];
        internal static void RaiseContentRefreshed(IEnumerable<string> updated, IEnumerable<string> removed, IEnumerable<string> moved)
        {
            s_UpdatedItems = s_UpdatedItems.Concat(updated).Distinct().ToArray();
            s_RemovedItems = s_RemovedItems.Concat(removed).Distinct().ToArray();
            s_MovedItems = s_MovedItems.Concat(moved).Distinct().ToArray();

            RaiseContentRefreshed();
        }

        private static void RaiseContentRefreshed()
        {
            var currentTime = EditorApplication.timeSinceStartup;
            if (s_BatchElapsedTime != 0 && currentTime - s_BatchElapsedTime > 0.5)
            {
                if (s_UpdatedItems.Length != 0 || s_RemovedItems.Length != 0 || s_MovedItems.Length != 0)
                    contentRefreshed?.Invoke(s_UpdatedItems, s_RemovedItems, s_MovedItems);
                s_UpdatedItems = new string[0];
                s_RemovedItems = new string[0];
                s_MovedItems = new string[0];
                s_BatchElapsedTime = 0;
            }
            else
            {
                if (s_BatchElapsedTime == 0)
                    s_BatchElapsedTime = currentTime;
                EditorApplication.delayCall += RaiseContentRefreshed;
            }
        }
        #endregion
    }
}

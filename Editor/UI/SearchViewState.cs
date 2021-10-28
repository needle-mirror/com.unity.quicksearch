using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Search;

namespace UnityEditor.Search
{
    static class SearchViewFlagsExtensions
    {
        public static bool HasAny(this SearchViewFlags flags, SearchViewFlags f) => (flags & f) != 0;
        public static bool HasAll(this SearchViewFlags flags, SearchViewFlags all) => (flags & all) == all;
        public static bool HasNone(this SearchViewFlags flags, SearchViewFlags f) => (flags & f) == 0;
    }

    [Serializable]
    public class SearchViewState : ISerializationCallbackReceiver
    {
        static readonly Vector2 defaultSize = new Vector2(850f, 539f);
        static readonly string[] emptyProviders = new string[0];

        internal SearchContext context
        {
            get
            {
                if (m_Context == null && m_WasDeserialized)
                    BuildContext();
                return m_Context;
            }

            set
            {
                m_Context = value ?? throw new ArgumentNullException(nameof(value));
            }
        }

        [NonSerialized] private SearchContext m_Context;
        [NonSerialized] private bool m_WasDeserialized;
        [SerializeField] internal string[] providerIds;
        [SerializeField] private SearchFlags searchFlags;
        [SerializeField] internal string searchText; // Also used as the initial query when the view was created
        [SerializeField] internal bool forceViewMode;
        [SerializeField] internal string sessionId;
        [SerializeField] internal string sessionName;
        [SerializeField] internal bool excludeNoneItem;
        [SerializeField] internal SearchTable tableConfig;
        [SerializeField] internal bool ignoreSaveSearches;
        [SerializeField] internal bool hideAllGroup;
        [SerializeField] internal GUIContent windowTitle;

        #if USE_QUERY_BUILDER
        [SerializeField] internal bool queryBuilderEnabled;
        #endif

        public string title;
        public float itemSize;
        public Rect position;
        public SearchViewFlags flags;
        public string group;

        [SerializeField] private SearchFunctor<Action<SearchItem, bool>> m_SelectHandler;
        [SerializeField] private SearchFunctor<Action<SearchItem>> m_TrackingHandler;
        [SerializeField] private SearchFunctor<Func<SearchItem, bool>> m_FilterHandler;
        [SerializeField] private SearchFunctor<Action<SearchContext, string, string>> m_GroupChanged;

        internal Action<SearchItem, bool> selectHandler { get => m_SelectHandler?.handler; set => m_SelectHandler = new SearchFunctor<Action<SearchItem, bool>>(value); }
        internal Action<SearchItem> trackingHandler { get => m_TrackingHandler?.handler; set => m_TrackingHandler = new SearchFunctor<Action<SearchItem>>(value); }
        internal Func<SearchItem, bool> filterHandler { get => m_FilterHandler?.handler; set => m_FilterHandler = new SearchFunctor<Func<SearchItem, bool>>(value); }
        internal Action<SearchContext, string, string> groupChanged { get => m_GroupChanged?.handler; set => m_GroupChanged = new SearchFunctor<Action<SearchContext, string, string>>(value); }

        internal bool hasWindowSize => position.width > 0f && position.height > 0;
        internal Vector2 windowSize => hasWindowSize ? position.size : defaultSize;

        internal SearchViewState() : this(null, null) {}
        public SearchViewState(SearchContext context) : this(context, null) {}

        public SearchViewState(SearchContext context, SearchViewFlags flags)
            : this(context, null)
        {
            SetSearchViewFlags(flags);
        }

        internal SearchViewState(SearchContext context, Action<SearchItem, bool> selectHandler)
        {
            m_Context = context;
            sessionId = Guid.NewGuid().ToString("N");
            this.selectHandler = selectHandler;
            trackingHandler = null;
            filterHandler = null;
            title = "item";
            itemSize = (float)DisplayMode.Grid;
            position = Rect.zero;
            searchText = context?.searchText ?? string.Empty;
            tableConfig = null;
            providerIds = emptyProviders;
        }

        internal SearchViewState(SearchContext context,
                                 Action<UnityEngine.Object, bool> selectObjectHandler,
                                 Action<UnityEngine.Object> trackingObjectHandler,
                                 string typeName, Type filterType)
            : this(context, null)
        {
            if (filterType == null && !string.IsNullOrEmpty(typeName))
            {
                filterType = TypeCache.GetTypesDerivedFrom<UnityEngine.Object>().FirstOrDefault(t => t.Name == typeName);
                if (filterType is null)
                    throw new ArgumentNullException(nameof(filterType));
            }
            context.filterType = filterType;

            selectHandler = (item, canceled) => selectObjectHandler?.Invoke(Utils.ToObject(item, filterType), canceled);
            filterHandler = (item) => item == SearchItem.none || (IsObjectMatchingType(item ?? SearchItem.none, filterType ?? typeof(UnityEngine.Object)));
            trackingHandler = (item) => trackingObjectHandler?.Invoke(Utils.ToObject(item, filterType));
            title = filterType?.Name ?? typeName;
        }

        internal SearchViewState(SearchContext context, SearchTable tableConfig, SearchViewFlags flags = SearchViewFlags.None)
            : this(context, flags | SearchViewFlags.TableView)
        {
            group = null;
            this.tableConfig = tableConfig;
        }

        internal SearchViewState SetSearchViewFlags(SearchViewFlags flags)
        {
            if (m_Context != null)
            {
                context.options |= ToSearchFlags(flags);
            }
            this.flags = flags;

            if (flags.HasAny(SearchViewFlags.CompactView))
            {
                itemSize = 0;
                forceViewMode = true;
            }
            if (flags.HasAny(SearchViewFlags.ListView))
            {
                itemSize = (float)DisplayMode.List;
                forceViewMode = true;
            }
            if (flags.HasAny(SearchViewFlags.GridView))
            {
                itemSize = (float)DisplayMode.Grid;
                forceViewMode = true;
            }
            if (flags.HasAny(SearchViewFlags.TableView))
            {
                itemSize = (float)DisplayMode.Table;
                forceViewMode = true;
            }
            #if USE_QUERY_BUILDER
            if (flags.HasAny(SearchViewFlags.OpenInBuilderMode)) queryBuilderEnabled = true;
            if (flags.HasAny(SearchViewFlags.OpenInTextMode)) queryBuilderEnabled = false;
            #endif
            return this;
        }

        internal void Assign(SearchViewState state)
        {
            providerIds = state.context.providers.Select(p => p.id).ToArray();
            searchFlags = state.searchFlags;
            searchText = state.context.searchText;
            sessionId = state.sessionId;
            sessionName = state.sessionName;
            excludeNoneItem = state.excludeNoneItem;
            ignoreSaveSearches = state.ignoreSaveSearches;

            title = state.title;
            itemSize = state.itemSize;
            position = state.position;
            flags = state.flags;
            forceViewMode = state.forceViewMode;
            group = state.group;

            #if USE_QUERY_BUILDER
            queryBuilderEnabled = state.queryBuilderEnabled;
            #endif

            BuildContext();
        }

        private void BuildContext()
        {
            if (providerIds != null && providerIds.Length > 0)
                m_Context = SearchService.CreateContext(providerIds, searchText ?? string.Empty, searchFlags);
            else
                m_Context = SearchService.CreateContext(searchText ?? string.Empty, searchFlags | SearchFlags.OpenDefault);
            m_WasDeserialized = false;
        }

        internal static SearchFlags ToSearchFlags(SearchViewFlags flags)
        {
            var sf = SearchFlags.None;
            if (flags.HasAny(SearchViewFlags.Debug)) sf |= SearchFlags.Debug;
            if (flags.HasAny(SearchViewFlags.NoIndexing)) sf |= SearchFlags.NoIndexing;
            if (flags.HasAny(SearchViewFlags.Packages)) sf |= SearchFlags.Packages;
            return sf;
        }

        static bool IsObjectMatchingType(in SearchItem item, in Type filterType)
        {
            if (item == SearchItem.none)
                return true;
            var objType = item.ToType(filterType);
            if (objType == null)
                return false;
            return filterType.IsAssignableFrom(objType);
        }

        internal static SearchViewState LoadDefaults()
        {
            var viewState = new SearchViewState();
            return viewState.LoadDefaults();
        }

        internal SearchViewState LoadDefaults(SearchFlags additionalFlags = SearchFlags.None)
        {
            var runningTests = Utils.IsRunningTests();
            if (string.IsNullOrEmpty(title))
                title = "Unity";
            if (!forceViewMode && !runningTests)
                itemSize = SearchSettings.itemIconSize;

            #if USE_QUERY_BUILDER
            if (!runningTests && flags.HasNone(SearchViewFlags.OpenInBuilderMode) && flags.HasNone(SearchViewFlags.OpenInTextMode))
                queryBuilderEnabled = SearchSettings.queryBuilder;
            #endif

            if (context != null)
            {
                context.options |= additionalFlags;
                if (runningTests)
                    context.options |= SearchFlags.Dockable;
            }
            return this;
        }

        public void OnBeforeSerialize()
        {
            if (context == null)
                return;
            searchFlags = context.options;
            searchText = context.searchText;
            providerIds = GetProviderIds().ToArray();
        }

        public void OnAfterDeserialize()
        {
            m_WasDeserialized = true;
            if (tableConfig != null && tableConfig.columns?.Length == 0)
                tableConfig = null;
        }

        internal IEnumerable<string> GetProviderIds()
        {
            if (context != null)
                return context.GetProviders().Select(p => p.id);
            return providerIds;
        }

        internal IEnumerable<string> GetProviderTypes()
        {
            var providers = context != null ? context.GetProviders() : SearchService.GetProviders(providerIds);
            return providers.Select(p => p.type).Distinct();
        }

        internal bool HasFlag(SearchViewFlags f) => (flags & f) != 0;
    }
}

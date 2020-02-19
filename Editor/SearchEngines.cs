#if UNITY_2020_2_OR_NEWER
//#define USE_SEARCH_ENGINE_API // << Enable this when the Search API gets merged in latest 2020.2
#endif

#if USE_SEARCH_ENGINE_API
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.QuickSearch
{
    abstract class QuickSearchEngine : UnityEditor.SearchService.ISearchEngineBase
    {
        private bool m_LastActiveState;

        public SearchProvider provider { get; private set; }

        public virtual void BeginSession(UnityEditor.SearchService.ISearchContext context)
        {
            provider = SearchService.Providers.First(p => p.name.id == providerId);
            if (provider != null)
            {
                m_LastActiveState = provider.active;
                provider.active = true;
                provider.onEnable?.Invoke();
            }
        }

        public virtual void EndSession(UnityEditor.SearchService.ISearchContext context)
        {
            StopAsyncResults();
            if (provider != null)
            {
                provider.onDisable?.Invoke();
                provider.active = m_LastActiveState;
            }
        }

        public virtual void BeginSearch(string query, UnityEditor.SearchService.ISearchContext context)
        {
            StopAsyncResults();
            AsyncSearchSession.asyncItemReceived += onAsyncItemReceived;
        }

        public virtual void EndSearch(UnityEditor.SearchService.ISearchContext context) {}

        private void StopAsyncResults()
        {
            if (AsyncSearchSession.SearchInProgress)
            {
                SearchService.StopAllAsyncSearchSessions();
            }
            AsyncSearchSession.asyncItemReceived -= onAsyncItemReceived;
        }

        public string id => "quicksearch";
        public string displayName => "Quick Search";

        public abstract string providerId { get; }

        public abstract Action<IEnumerable<SearchItem>> onAsyncItemReceived { get; }
    }

    [UnityEditor.SearchService.Project.Engine]
    class ProjectSearchEngine : QuickSearchEngine, UnityEditor.SearchService.Project.IEngine
    {
        Action<IEnumerable<string>> m_OnAsyncItemReceived = items => {};

        public override string providerId => "asset";

        public override Action<IEnumerable<SearchItem>> onAsyncItemReceived => itemsReceived => m_OnAsyncItemReceived(itemsReceived.Select(item => item.id));

        public virtual IEnumerable<string> Search(string query, UnityEditor.SearchService.ISearchContext context, Action<IEnumerable<string>> asyncItemsReceived)
        {
            var searchContext = new SearchContext();
            if (context.requiredTypeNames != null && context.requiredTypeNames.Any())
            {
                searchContext.wantsMore = true;
                searchContext.filterType = Utils.GetTypeFromName(context.requiredTypeNames.First());
            }
            searchContext.searchText = query;
            var items = SearchService.GetItems(searchContext, provider);
            return items.Select(item => item.id);
        }
    }

    [UnityEditor.SearchService.Scene.Engine]
    class SceneSearchEngine : QuickSearchEngine, UnityEditor.SearchService.Scene.IEngine
    {
        private HashSet<int> m_SearchItems;

        public override string providerId => "scene";

        public override Action<IEnumerable<SearchItem>> onAsyncItemReceived => items => { };

        public override void BeginSearch(string query, UnityEditor.SearchService.ISearchContext context)
        {
            base.BeginSearch(query, context);
            var searchContext = new SearchContext { searchText = query };
            searchContext.wantsMore = true;
            if (context.requiredTypeNames != null && context.requiredTypeNames.Any())
            {
                searchContext.filterType = Utils.GetTypeFromName(context.requiredTypeNames.First());
            }
            else
            {
                searchContext.filterType = typeof(GameObject);
            }

            m_SearchItems = new HashSet<int>();
            foreach (var id in SearchService.GetItems(searchContext, provider).Select(item => Convert.ToInt32(item.id)))
            {
                m_SearchItems.Add(id);
            }
        }

        public override void EndSearch(UnityEditor.SearchService.ISearchContext context)
        {
            m_SearchItems.Clear();
        }

        public virtual bool Filter(string query, HierarchyProperty objectToFilter, UnityEditor.SearchService.ISearchContext context)
        {
            var id = objectToFilter.instanceID;
            return m_SearchItems.Contains(id);
        }
    }

    [UnityEditor.SearchService.ObjectSelector.Engine]
    class ObjectSelectorEngine : QuickSearchEngine, UnityEditor.SearchService.ObjectSelector.IEngine
    {
        public override string providerId => "res";

        public override Action<IEnumerable<SearchItem>> onAsyncItemReceived => items => {};
        public override void BeginSearch(string query, UnityEditor.SearchService.ISearchContext context) {}
        public override void BeginSession(UnityEditor.SearchService.ISearchContext context) {}
        public override void EndSearch(UnityEditor.SearchService.ISearchContext context) {}
        public override void EndSession(UnityEditor.SearchService.ISearchContext context) {}

        public bool SelectObject(UnityEditor.SearchService.ISearchContext context,
            Action<UnityEngine.Object, bool> selectHandler, Action<UnityEngine.Object> trackingHandler)
        {
            var selectContext = (UnityEditor.SearchService.ObjectSelector.SearchContext)context;
            return QuickSearch.ShowObjectPicker(selectHandler, trackingHandler, 
                selectContext.currentObject?.name ?? "", 
                selectContext.requiredTypeNames.First(), selectContext.requiredTypes.First()) != null;
        }
    }
}
#endif
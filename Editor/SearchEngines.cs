#if UNITY_2020_2_OR_NEWER
#define USE_SEARCH_ENGINE_API
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
        public SearchProvider provider { get; private set; }

        public SearchContext context { get; private set; }

        public virtual void BeginSession(UnityEditor.SearchService.ISearchContext context)
        {
            provider = SearchService.Providers.First(p => p.name.id == providerId);
            this.context = new SearchContext(new []{provider});
        }

        public virtual void EndSession(UnityEditor.SearchService.ISearchContext context)
        {
            StopAsyncResults();
            this.context = null;
        }

        public virtual void BeginSearch(string query, UnityEditor.SearchService.ISearchContext context)
        {
            StopAsyncResults();
            this.context.asyncItemReceived += onAsyncItemReceived;
        }

        public virtual void EndSearch(UnityEditor.SearchService.ISearchContext context) {}

        private void StopAsyncResults()
        {
            if (context.searchInProgress)
            {
                context.sessions.StopAllAsyncSearchSessions();
            }
            context.asyncItemReceived -= onAsyncItemReceived;
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
            if (asyncItemsReceived != null)
            {
                m_OnAsyncItemReceived = asyncItemsReceived;
            }

            if (context.requiredTypeNames != null && context.requiredTypeNames.Any())
            {
                this.context.wantsMore = true;
                this.context.filterType = Utils.GetTypeFromName(context.requiredTypeNames.First());
            }
            else
            {
                this.context.wantsMore = false;
                this.context.filterType = null;
            }
            this.context.searchText = query;
            var items = SearchService.GetItems(this.context);
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
            this.context.searchText = query;
            this.context.wantsMore = true;
            if (context.requiredTypeNames != null && context.requiredTypeNames.Any())
            {
                this.context.filterType = Utils.GetTypeFromName(context.requiredTypeNames.First());
            }
            else
            {
                this.context.filterType = typeof(GameObject);
            }

            m_SearchItems = new HashSet<int>();
            foreach (var id in SearchService.GetItems(this.context, SearchFlags.Synchronous).Select(item => Convert.ToInt32(item.id)))
            {
                m_SearchItems.Add(id);
            }
        }

        public override void EndSearch(UnityEditor.SearchService.ISearchContext context)
        {
            m_SearchItems.Clear();
            base.EndSearch(context);
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
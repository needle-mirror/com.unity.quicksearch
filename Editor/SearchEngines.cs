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
    class SearchApiSession : IDisposable
    {
        bool m_Disposed = false;

        public SearchContext context { get; private set; }

        public Action<IEnumerable<string>> onAsyncItemsReceived { get; set; }

        public SearchApiSession(SearchProvider provider)
        {
            context = new SearchContext(new []{ provider });
        }

        ~SearchApiSession()
        {
            Dispose(false);
        }

        public void StopAsyncResults()
        {
            if (context.searchInProgress)
            {
                context.sessions.StopAllAsyncSearchSessions();
            }
            context.asyncItemReceived -= OnAsyncItemsReceived;
        }

        public void StartAsyncResults()
        {
            context.asyncItemReceived += OnAsyncItemsReceived;
        }

        private void OnAsyncItemsReceived(IEnumerable<SearchItem> items)
        {
            onAsyncItemsReceived?.Invoke(items.Select(item => item.id));
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!m_Disposed)
            {
                StopAsyncResults();
                context?.Dispose();
                m_Disposed = true;
                context = null;

            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    abstract class QuickSearchEngine : UnityEditor.SearchService.ISearchEngineBase, IDisposable
    {
        bool m_Disposed = false;

        public Dictionary<Guid, SearchApiSession> searchSessions = new Dictionary<Guid, SearchApiSession>();

        ~QuickSearchEngine()
        {
            Dispose(false);
        }

        public virtual void BeginSession(UnityEditor.SearchService.ISearchContext context)
        {
            if (searchSessions.ContainsKey(context.guid))
                return;

            var provider = SearchService.Providers.First(p => p.name.id == providerId);
            searchSessions.Add(context.guid, new SearchApiSession(provider));
        }

        public virtual void EndSession(UnityEditor.SearchService.ISearchContext context)
        {
            if (!searchSessions.ContainsKey(context.guid))
                return;

            searchSessions[context.guid].StopAsyncResults();
            searchSessions[context.guid].Dispose();
            searchSessions.Remove(context.guid);
        }

        public virtual void BeginSearch(string query, UnityEditor.SearchService.ISearchContext context)
        {
            if (!searchSessions.ContainsKey(context.guid))
                return;
            searchSessions[context.guid].StopAsyncResults();
            searchSessions[context.guid].StartAsyncResults();
        }

        public virtual void EndSearch(UnityEditor.SearchService.ISearchContext context) {}

        public string id => "quicksearch";
        public string displayName => "Quick Search";

        public abstract string providerId { get; }

        protected virtual void Dispose(bool disposing)
        {
            if (!m_Disposed)
            {
                foreach (var kvp in searchSessions)
                {
                    kvp.Value.Dispose();
                }

                m_Disposed = true;
                searchSessions = null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    [UnityEditor.SearchService.Project.Engine]
    class ProjectSearchEngine : QuickSearchEngine, UnityEditor.SearchService.Project.IEngine
    {
        public override string providerId => "asset";

        public virtual IEnumerable<string> Search(string query, UnityEditor.SearchService.ISearchContext context, Action<IEnumerable<string>> asyncItemsReceived)
        {
            if (!searchSessions.ContainsKey(context.guid))
                return new string[] { };

            var searchSession = searchSessions[context.guid];

            if (asyncItemsReceived != null)
            {
                searchSession.onAsyncItemsReceived = asyncItemsReceived;
            }

            if (context.requiredTypeNames != null && context.requiredTypeNames.Any())
            {
                searchSession.context.wantsMore = true;
                searchSession.context.filterType = Utils.GetTypeFromName(context.requiredTypeNames.First());
            }
            else
            {
                searchSession.context.wantsMore = false;
                searchSession.context.filterType = null;
            }
            searchSession.context.searchText = query;
            var items = SearchService.GetItems(searchSession.context);
            return items.Select(item => item.id);
        }
    }

    [UnityEditor.SearchService.Scene.Engine]
    class SceneSearchEngine : QuickSearchEngine, UnityEditor.SearchService.Scene.IEngine
    {
        private readonly Dictionary<Guid, HashSet<int>> m_SearchItemsBySession = new Dictionary<Guid, HashSet<int>>();

        public override string providerId => "scene";

        public override void BeginSearch(string query, UnityEditor.SearchService.ISearchContext context)
        {
            if (!searchSessions.ContainsKey(context.guid))
                return;
            base.BeginSearch(query, context);

            var searchSession = searchSessions[context.guid];
            searchSession.context.searchText = query;
            searchSession.context.wantsMore = true;
            if (context.requiredTypeNames != null && context.requiredTypeNames.Any())
            {
                searchSession.context.filterType = Utils.GetTypeFromName(context.requiredTypeNames.First());
            }
            else
            {
                searchSession.context.filterType = typeof(GameObject);
            }

            if (!m_SearchItemsBySession.ContainsKey(context.guid))
                m_SearchItemsBySession.Add(context.guid, new HashSet<int>());
            var searchItemsSet = m_SearchItemsBySession[context.guid];
            searchItemsSet.Clear();

            foreach (var id in SearchService.GetItems(searchSession.context, SearchFlags.Synchronous).Select(item => Convert.ToInt32(item.id)))
            {
                searchItemsSet.Add(id);
            }
        }

        public override void EndSearch(UnityEditor.SearchService.ISearchContext context)
        {
            if (!searchSessions.ContainsKey(context.guid))
                return;
            if (m_SearchItemsBySession.ContainsKey(context.guid))
            {
                m_SearchItemsBySession[context.guid].Clear();
                m_SearchItemsBySession.Remove(context.guid);
            }
            base.EndSearch(context);
        }

        public virtual bool Filter(string query, HierarchyProperty objectToFilter, UnityEditor.SearchService.ISearchContext context)
        {
            if (!searchSessions.ContainsKey(context.guid))
                return false;
            if (!m_SearchItemsBySession.ContainsKey(context.guid))
                return false;
            return m_SearchItemsBySession[context.guid].Contains(objectToFilter.instanceID);
        }
    }

    [UnityEditor.SearchService.ObjectSelector.Engine]
    class ObjectSelectorEngine : QuickSearchEngine, UnityEditor.SearchService.ObjectSelector.IEngine
    {
        public override string providerId => "res";

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
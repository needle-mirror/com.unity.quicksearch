using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;

namespace Unity.QuickSearch
{
    enum SearchQuerySortOrder
    {
        RecentlyUsed,
        AToZ,
        ZToA
    }

    /// <summary>
    /// Asset storing a query that will be executable by a SearchEngine.
    /// </summary>
    [Serializable]
    class SearchQuery : ScriptableObject
    {
        static List<SearchQuery> s_SavedQueries;
        internal static IEnumerable<SearchQuery> savedQueries
        {
            get
            {
                if (s_SavedQueries == null || s_SavedQueries.Any(qs => !qs))
                {
                    s_SavedQueries = AssetDatabase.FindAssets($"t:{nameof(SearchQuery)}").Select(AssetDatabase.GUIDToAssetPath)
                        .Select(path => AssetDatabase.LoadAssetAtPath<SearchQuery>(path))
                        .Where(asset => asset != null).ToList();
                    SortQueries();
                }

                return s_SavedQueries;
            }
        }

        public static SearchQuery Create(SearchContext context, string description = null, Texture2D icon = null)
        {
            return Create(context.searchText, context.providers.Select(p => p.id), description, icon);
        }

        public static SearchQuery Create(string searchQuery, IEnumerable<string> providerIds, string description = null, Texture2D icon = null)
        {
            var queryAsset = CreateInstance<SearchQuery>();
            queryAsset.searchQuery = searchQuery;
            queryAsset.providerIds = providerIds.ToList();
            queryAsset.description = description;
            queryAsset.icon = icon;
            return queryAsset;
        }

        public static SearchQuery Create(string searchQuery, IEnumerable<SearchProvider> providers, string description = null, Texture2D icon = null)
        {
            return Create(searchQuery, providers.Select(p => p.id), description, icon);
        }

        public static string GetQueryName(string query)
        {
            return RemoveInvalidChars(query.Replace(":", "_").Replace(" ", "_"));
        }

        private static string RemoveInvalidChars(string filename)
        {
            filename = string.Concat(filename.Split(Path.GetInvalidFileNameChars()));
            if (filename.Length > 0 && !char.IsLetterOrDigit(filename[0]))
                filename = filename.Substring(1);
            return filename;
        }

        public static void SaveQuery(SearchQuery asset, string folder, string name = null)
        {
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            if (name == null)
            {
                name = GetQueryName(asset.searchQuery);
            }

            name += ".asset";
            var fullPath = Path.Combine(folder, name);
            AssetDatabase.CreateAsset(asset, fullPath);
            AssetDatabase.ImportAsset(fullPath);
        }

        public static IEnumerable<SearchItem> GetAllSearchQueryItems(SearchContext context)
        {
            var queryProvider = SearchService.GetProvider(Providers.Query.type);
            return savedQueries.Where(query => query && query.providerIds.Any(id => context.filters.Any(f => f.enabled && f.provider.id == id))).Select(query =>
            {
                var id = GlobalObjectId.GetGlobalObjectIdSlow(query).ToString();
                var description = string.IsNullOrEmpty(query.description) ? $"{query.searchQuery}" : $"{query.description} ({query.searchQuery})";
                var thumbnail = query.icon ? query.icon : Icons.favorite;
                return queryProvider.CreateItem(context, id, query.name, description, thumbnail, query);
            }).OrderBy(item => item.label);
        }

        public static void SortQueries()
        {
            switch (SearchSettings.savedSearchesSortOrder)
            {
                case SearchQuerySortOrder.RecentlyUsed:
                {
                    var now = DateTime.Now.Ticks;
                    s_SavedQueries = savedQueries.OrderByDescending(asset =>
                    {
                        var recentSearchIndex = SearchSettings.recentSearches.IndexOf(asset.searchQuery);
                        if (recentSearchIndex != -1)
                        {
                            return now + SearchSettings.recentSearches.Count - recentSearchIndex;
                        }

                        return asset.creationTime;
                    }).ToList();
                }
                break;
                case SearchQuerySortOrder.AToZ:
                    s_SavedQueries = savedQueries.OrderBy(asset => asset.name).ToList();
                    break;
                case SearchQuerySortOrder.ZToA:
                    s_SavedQueries = savedQueries.OrderByDescending(asset => asset.name).ToList();
                    break;
            }
        }

        public static void ResetSearchQueryItems()
        {
            s_SavedQueries = null;
        }

        [OnOpenAsset]
        private static bool OpenQuery(int instanceID, int line)
        {
            var query = EditorUtility.InstanceIDToObject(instanceID) as SearchQuery;
            if (query != null)
            {
                var searchWindow = QuickSearch.OpenWithContextualProvider(null, query.providerIds.ToArray(), SearchFlags.OpenContextual);
                ExecuteQuery(searchWindow, query, SearchAnalytics.GenericEventType.SearchQueryOpen);
                return searchWindow != null;
            }

            return false;
        }

        public static void ExecuteQuery(ISearchView view, SearchQuery query, SearchAnalytics.GenericEventType sourceEvt = SearchAnalytics.GenericEventType.SearchQueryExecute)
        {
            if (view is QuickSearch qs)
            {
                qs.SendEvent(sourceEvt, query.searchQuery);
                qs.ExecuteSearchQuery(query);
            }
        }

        private long m_CreationTime;
        public long creationTime
        {
            get
            {
                if (m_CreationTime == 0)
                {
                    var path = AssetDatabase.GetAssetPath(this);
                    var fileInfo = new FileInfo(path);
                    m_CreationTime = fileInfo.CreationTime.Ticks;
                }
                return m_CreationTime;
            }
        }
        public string description;
        public Texture2D icon;
        public string searchQuery;
        public List<string> providerIds;
    }
}

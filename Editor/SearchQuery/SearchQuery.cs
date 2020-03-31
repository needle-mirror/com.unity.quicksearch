using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;

namespace Unity.QuickSearch
{
    /// <summary>
    /// Asset storing a query that will be executable by a SearchEngine.
    /// </summary>
    [Serializable]
    [CreateAssetMenu(menuName = "Search Query", order = 201)]
    public class SearchQuery : ScriptableObject
    {
        static List<SearchItem> s_SearchQueryItems;

        public SearchQuery()
        {
        }

        public static SearchQuery Create(SearchContext context, string title = null, string description = null, Texture2D icon = null)
        {
            return Create(context.searchText, context.providers.Select(p => p.name.id), title, description, icon);
        }

        public static SearchQuery Create(string searchQuery, IEnumerable<string> providerIds, string title = null, string description = null, Texture2D icon = null)
        {
            var queryAsset = CreateInstance<SearchQuery>();
            queryAsset.searchQuery = searchQuery;
            queryAsset.providerIds = providerIds.ToList();
            queryAsset.title = title;
            queryAsset.description = description;
            queryAsset.icon = icon;
            return queryAsset;
        }

        public static SearchQuery Create(string searchQuery, IEnumerable<SearchProvider> providers, string title = null, string description = null, Texture2D icon = null)
        {
            return Create(searchQuery, providers.Select(p => p.name.id), title, description, icon);
        }

        public SearchContext CreateContext()
        {
            var context = new SearchContext(providers, searchQuery);
            return context;
        }

        public static void SaveQueryInDefaultFolder(SearchQuery asset)
        {
            SaveQuery(asset, SearchSettings.queryFolder);
        }

        public static string RemoveInvalidChars(string filename)
        {
            return string.Concat(filename.Split(Path.GetInvalidFileNameChars()));
        }

        public static void SaveQuery(SearchQuery asset, string folder, string name = null)
        {
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            if (name == null)
            {
                name = RemoveInvalidChars(asset.title ?? asset.searchQuery.Replace(":", "_").Replace(" ", "_"));
            }

            name += ".asset";
            var fullPath = Path.Combine(folder, name);
            AssetDatabase.CreateAsset(asset, fullPath);
            AssetDatabase.ImportAsset(fullPath);
        }

        public static IEnumerable<SearchQuery> GetAllQueries()
        {
            return AssetDatabase.FindAssets($"t:{nameof(SearchQuery)}")
                .Select(guid => AssetDatabase.LoadAssetAtPath<SearchQuery>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(asset => asset != null);
        }

        public static List<SearchItem> GetAllSearchQueryItems()
        {
            var queryProvider = SearchService.GetProvider(Providers.Query.type);
            return s_SearchQueryItems ?? (s_SearchQueryItems = SearchQuery.GetAllQueries().Select(query =>
            {
                var item = queryProvider.CreateItem(AssetDatabase.GetAssetPath(query.GetInstanceID()));
                item.label = string.IsNullOrEmpty(query.title) ? query.name : query.title;
                item.thumbnail = query.icon ? query.icon : Icons.favorite;
                item.description = string.IsNullOrEmpty(query.description) ? $"{query.searchQuery} - {AssetDatabase.GetAssetPath(query)}" : query.description;
                item.data = query;
                return item;
            }).ToList());
        }

        public static void ResetSearchQueryItems()
        {
            s_SearchQueryItems = null;
        }

        [OnOpenAsset]
        private static bool OpenQuery(int instanceID, int line)
        {
            var query = EditorUtility.InstanceIDToObject(instanceID) as SearchQuery;
            if (query != null)
            {
                var qsWindow = QuickSearch.Create();
                ExecuteQuery(qsWindow, query);
                qsWindow.ShowWindow();
            }

            return false;
        }

        public static void ExecuteQuery(ISearchView view, SearchQuery query)
        {
            view.context.SetFilteredProviders(query.providerIds);
            view.SetSearchText(query.searchQuery);
        }

        public string title;
        public string description;
        public Texture2D icon;
        public string searchQuery;
        public List<string> providerIds;

        public IEnumerable<SearchProvider> providers
        {
            get
            {
                return providerIds.Select(SearchService.GetProvider).Where(p => p != null  && p.active);
            }
        }
    }
}


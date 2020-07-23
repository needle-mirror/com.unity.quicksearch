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
    class SearchQuery : ScriptableObject
    {
        static List<SearchQuery> s_SavedQueries;

        public static SearchQuery Create(SearchContext context, string description = null, Texture2D icon = null)
        {
            return Create(context.searchText, context.providers.Select(p => p.name.id), description, icon);
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
            return Create(searchQuery, providers.Select(p => p.name.id), description, icon);
        }

        public static void SaveQueryInDefaultFolder(SearchQuery asset)
        {
            SaveQuery(asset, SearchSettings.queryFolder);
        }

        public static string RemoveInvalidChars(string filename)
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
                name = RemoveInvalidChars(asset.searchQuery.Replace(":", "_").Replace(" ", "_"));
            }

            name += ".asset";
            var fullPath = Path.Combine(folder, name);
            AssetDatabase.CreateAsset(asset, fullPath);
            AssetDatabase.ImportAsset(fullPath);
        }

        public static List<SearchQuery> GetAllQueries()
        {
            return AssetDatabase.FindAssets($"t:{nameof(SearchQuery)}").Select(AssetDatabase.GUIDToAssetPath)
                .Select(path => AssetDatabase.LoadAssetAtPath<SearchQuery>(path))
                .Where(asset => asset != null)
                .OrderBy(asset => asset.name).ToList();
        }

        public static List<SearchItem> GetAllSearchQueryItems(SearchContext context)
        {
            s_SavedQueries = s_SavedQueries ?? GetAllQueries();
            var queryProvider = SearchService.GetProvider(Providers.Query.type);
            return s_SavedQueries.Where(query => query && query.providerIds.Any(id => context.filters.Any(f => f.enabled && f.provider.name.id == id))).Select(query =>
            {
                var id = GlobalObjectId.GetGlobalObjectIdSlow(query).ToString();
                var description = string.IsNullOrEmpty(query.description) ? $"{query.searchQuery} - {AssetDatabase.GetAssetPath(query)}" : query.description;
                var thumbnail = query.icon ? query.icon : Icons.favorite;
                return queryProvider.CreateItem(context, id, query.name, description, thumbnail, query);
            }).ToList();
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
                return QuickSearch.OpenWithContextualProvider(query.searchQuery, false, query.providerIds.ToArray()) != null;

            return false;
        }

        public static void ExecuteQuery(ISearchView view, SearchQuery query, SearchAnalytics.GenericEventType sourceEvt = SearchAnalytics.GenericEventType.SearchQueryExecute)
        {
            if (view is QuickSearch qs)
            {
                qs.SendEvent(sourceEvt, query.searchQuery);
            }
            
            view.context.SetFilteredProviders(query.providerIds);
            view.SetSearchText(query.searchQuery);
        }

        public string description;
        public Texture2D icon;
        public string searchQuery;
        public List<string> providerIds;
    }
}


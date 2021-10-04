using System;
using System.Linq;
using System.Collections.Generic;

namespace UnityEditor.Search.Providers
{
    static class AdbProvider
    {
        public const string type = "adb";

        static ObjectQueryEngine<UnityEngine.Object> m_ResourcesQueryEngine;

        public static IEnumerable<string> EnumeratePaths(string searchQuery, SearchFlags flags)
        {
            #if USE_SEARCH_MODULE
            var searchFilter = new SearchFilter
            {
                searchArea = GetSearchArea(flags),
                showAllHits = true,
                originalText = searchQuery
            };
            SearchUtility.ParseSearchString(searchQuery, searchFilter);
            return EnumeratePaths(searchFilter);
            #else
            return AssetDatabase.FindAssets(searchQuery).Select(AssetDatabase.GUIDToAssetPath);
            #endif
        }

        public static IEnumerable<string> EnumeratePaths(Type type, SearchFlags flags)
        {
            #if USE_SEARCH_MODULE
            return EnumeratePaths(new SearchFilter
            {
                searchArea = GetSearchArea(flags),
                showAllHits = true,
                classNames = new[] { type.Name }
            });
            #else
            return AssetDatabase.FindAssets($"t:{type.Name}").Select(AssetDatabase.GUIDToAssetPath);
            #endif
        }

        #if USE_SEARCH_MODULE

        static SearchFilter.SearchArea GetSearchArea(in SearchFlags searchFlags)
        {
            if (searchFlags.HasAny(SearchFlags.Packages))
                return SearchFilter.SearchArea.AllAssets;
            return SearchFilter.SearchArea.InAssetsOnly;
        }

        static IEnumerable<string> EnumeratePaths(SearchFilter searchFilter)
        {
            var rIt = AssetDatabase.EnumerateAllAssets(searchFilter);
            while (rIt.MoveNext())
            {
                if (rIt.Current.pptrValue)
                    yield return AssetDatabase.GetAssetPath(rIt.Current.instanceID);
            }
        }

        #endif

        public static IEnumerable<string> EnumeratePaths(SearchContext context)
        {
            if (!string.IsNullOrEmpty(context.searchQuery))
                return EnumeratePaths(context.searchQuery, context.options);

            if (context.filterType != null)
                return EnumeratePaths(context.filterType, context.options);
            return Enumerable.Empty<string>();
        }

        static IEnumerable<SearchItem> FetchItems(SearchContext context, SearchProvider provider)
        {
            if (context.empty && context.filterType == null)
                yield break;

            if (m_ResourcesQueryEngine == null)
                m_ResourcesQueryEngine = new ObjectQueryEngine();

            // Search asset database
            foreach (var path in EnumeratePaths(context))
                yield return AssetProvider.CreateItem("ADB", context, provider, null, path, 998, SearchDocumentFlags.Asset);

            // Search builtin resources
            var resources = AssetDatabase.LoadAllAssetsAtPath("library/unity default resources")
                .Concat(AssetDatabase.LoadAllAssetsAtPath("resources/unity_builtin_extra"));
            if (context.wantsMore)
                resources = resources.Concat(AssetDatabase.LoadAllAssetsAtPath("library/unity editor resources"));

            if (context.filterType != null)
                resources = resources.Where(r => context.filterType.IsAssignableFrom(r.GetType()));

            if (!string.IsNullOrEmpty(context.searchQuery))
                resources = m_ResourcesQueryEngine.Search(context, provider, resources);
            else if (context.filterType == null)
                yield break;

            foreach (var obj in resources)
            {
                if (!obj)
                    continue;
                var gid = GlobalObjectId.GetGlobalObjectIdSlow(obj);
                if (gid.identifierType == 0)
                    continue;
                yield return AssetProvider.CreateItem("Resources", context, provider, gid.ToString(), null, 1998, SearchDocumentFlags.Resources);
            }
        }

        static IEnumerable<SearchProposition> FetchPropositions(SearchContext context, SearchPropositionOptions options)
        {
            if (!options.flags.HasAny(SearchPropositionFlags.QueryBuilder))
                yield break;

            #if USE_QUERY_BUILDER
            foreach (var f in QueryListBlockAttribute.GetPropositions(typeof(QueryTypeBlock)))
                yield return f;
            foreach (var f in QueryListBlockAttribute.GetPropositions(typeof(QueryLabelBlock)))
                yield return f;
            foreach (var f in QueryListBlockAttribute.GetPropositions(typeof(QueryAreaFilterBlock)))
                yield return f;
            foreach (var f in QueryListBlockAttribute.GetPropositions(typeof(QueryBundleFilterBlock)))
                yield return f;

            yield return new SearchProposition(category: null, "Reference", "ref:<$object:none,UnityEngine.Object$>", "Find all assets referencing a specific asset.");
            yield return new SearchProposition(category: null, "Glob", "glob:\"Assets/**/*.png\"", "Search according to a glob query.");
            #endif
        }

        [SearchItemProvider]
        internal static SearchProvider CreateProvider()
        {
            return new SearchProvider(type, "Asset Database")
            {
                type = "asset",
                active = false,
                priority = 2500,
                fetchItems = (context, items, provider) => FetchItems(context, SearchService.GetProvider("asset") ?? provider),
                fetchPropositions = (context, options) => FetchPropositions(context, options)
            };
        }

        [MenuItem("Window/Search/Asset Database", priority = 1271)] static void OpenProvider() => SearchService.ShowContextual(type);
        [ShortcutManagement.Shortcut("Help/Search/Asset Database")] static void OpenShortcut() => QuickSearch.OpenWithContextualProvider(type);
    }

    #if USE_QUERY_BUILDER
    [QueryListBlock(null, "area", "a", ":")]
    class QueryAreaFilterBlock : QueryListBlock
    {
        public QueryAreaFilterBlock(IQuerySource source, string id, string value, QueryListBlockAttribute attr)
            : base(source, id, value, attr)
        {
            icon = Utils.LoadIcon("Filter Icon");
        }

        public override IEnumerable<SearchProposition> GetPropositions(SearchPropositionFlags flags)
        {
            yield return CreateProposition(flags, "All", "all", "Search all", score: -99);
            yield return CreateProposition(flags, "Assets", "assets", "Search in Assets folder only", score: -98);
            yield return CreateProposition(flags, "Packages", "packages", "Search in packages only", score: -97);
        }
    }

    [QueryListBlock("Bundle", "bundle", "b", ":")]
    class QueryBundleFilterBlock : QueryListBlock
    {
        public QueryBundleFilterBlock(IQuerySource source, string id, string value, QueryListBlockAttribute attr)
            : base(source, id, value, attr)
        {
            icon = Utils.LoadIcon("Filter Icon");
        }

        public override IEnumerable<SearchProposition> GetPropositions(SearchPropositionFlags flags)
        {
            var bundleNames = AssetDatabase.GetAllAssetBundleNames();
            foreach (var bundleName in bundleNames)
            {
                yield return CreateProposition(flags, bundleName, bundleName, $"Search inside bundle \"{bundleName}\"");
            }
        }
    }
    #endif
}

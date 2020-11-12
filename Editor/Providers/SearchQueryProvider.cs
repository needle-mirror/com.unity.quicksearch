using System.Collections.Generic;

namespace UnityEditor.Search.Providers
{
    static class Query
    {
        internal const string type = "query";
        private const string displayName = "Saved Queries";

        [SearchItemProvider]
        internal static SearchProvider CreateProvider()
        {
            return new SearchProvider(type, displayName)
            {
                filterId = "q:",
                isExplicitProvider = true,
                fetchItems = (context, items, provider) => Search(context)
            };
        }

        private static IEnumerable<SearchItem> Search(SearchContext context)
        {
            var queryItems = SearchQuery.GetAllSearchQueryItems(context);
            if (string.IsNullOrEmpty(context.searchQuery))
            {
                foreach (var qi in queryItems)
                    yield return qi;
            }
            else
            {
                foreach (var qi in queryItems)
                {
                    if (SearchUtils.MatchSearchGroups(context, qi.label, true) ||
                        SearchUtils.MatchSearchGroups(context, ((SearchQuery)qi.data).text, true))
                    {
                        yield return qi;
                    }
                }
            }
        }

        [SearchActionsProvider]
        internal static IEnumerable<SearchAction> ActionHandlers()
        {
            return new[]
            {
                new SearchAction(type, "exec", null, "Execute search query")
                {
                    closeWindowAfterExecution = false,
                    handler = (item) => SearchQuery.ExecuteQuery(item.context.searchView, (SearchQuery)item.data)
                },
                new SearchAction(type, "select", null, "Select search query", (item) =>
                {
                    var queryPath = AssetDatabase.GetAssetPath((SearchQuery)item.data);
                    if (!string.IsNullOrEmpty(queryPath))
                        Utils.FrameAssetFromPath(queryPath);
                })
            };
        }
    }
}

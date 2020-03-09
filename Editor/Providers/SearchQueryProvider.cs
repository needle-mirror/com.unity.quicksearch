using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using UnityEditor;

namespace Unity.QuickSearch
{
namespace Providers
{
    [UsedImplicitly]
    static class Query
    {
        private const string type = "query";
        private const string displayName = "Search Queries";

        [UsedImplicitly, SearchItemProvider]
        private static SearchProvider CreateProvider()
        {
            return new SearchProvider(type, displayName)
            {
                filterId = "q:",
                isExplicitProvider = true,
                fetchItems = (context, items, provider) =>
                {
                    items.AddRange(SearchQuery.GetAllQueries().Select(query =>
                    {
                        var item = provider.CreateItem(AssetDatabase.GetAssetPath(query.GetInstanceID()));
                        item.label = string.IsNullOrEmpty(query.title) ? query.name : query.title;
                        item.thumbnail = query.icon ? query.icon : Icons.quicksearch;
                        item.description = string.IsNullOrEmpty(query.description) ? query.searchQuery : query.description;
                        item.data = query.searchQuery;
                        return item;
                    }));
                    return null;
                }
            };
        }

        [UsedImplicitly, SearchActionsProvider]
        private static IEnumerable<SearchAction> ActionHandlers()
        {
            return new[]
            {
                new SearchAction(type, "exec", null, "Execute Search Query")
                {
                    closeWindowAfterExecution = false,
                    handler = (item, context) =>
                    {
                        context.searchView.SetSearchText(item.data as string);
                    }
                },
                new SearchAction(type, "select", null, "Select Search Query")
                {
                    handler = (item, context) =>
                    {
                        Utils.FrameAssetFromPath(item.id);
                    }
                }
            };
        }
    }
}
}

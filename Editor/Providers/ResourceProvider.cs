using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;

namespace Unity.QuickSearch.Providers
{
    [UsedImplicitly]
    static class ResourcesProvider
    {
        private struct MatchOperation
        {
            public string name;
            public string matchToken;
            public Func<UnityEngine.Object, string, bool> matchQuery;
            public Func<SearchContext, string, IEnumerable<string>> fetchKeywords;
        }

        // Type cache
        #if UNITY_2019_2_OR_NEWER
        private static readonly string[] typeFilter = TypeCache.GetTypesDerivedFrom<UnityEngine.Object>()
            .Select(t => t.Name)
            .Distinct()
            .OrderBy(n => n).ToArray();
        #else
        private static readonly string[] typeFilter = {};
        #endif

        internal static string type = "res";
        internal static string displayName = "Resources";

        // Match operations for specific subfilters
        private static readonly List<MatchOperation> k_SubMatches = new List<MatchOperation>
        {
            new MatchOperation { name = "type", matchToken = "t", matchQuery = MatchByType, fetchKeywords = FetchTypeKeywords },
            new MatchOperation { name = "name", matchToken = "n", matchQuery = MatchByName },
            new MatchOperation { name = "id", matchToken = "id", matchQuery = MatchById },
            new MatchOperation { name = "tag", matchToken = "tag", matchQuery = MatchByTag, fetchKeywords = FetchTagKeywords }
        };

        // Descriptors for specific types of resources
        static List<ResourceDescriptor> k_Descriptors = Assembly
            .GetAssembly(typeof(ResourceDescriptor))
            .GetTypes().Where(t => typeof(ResourceDescriptor).IsAssignableFrom(t))
            .Select(t => t.GetConstructor(new Type[] { })?.Invoke(new object[] { }) as ResourceDescriptor)
            .OrderBy(descriptor => descriptor.Priority).Reverse().ToList();

        [UsedImplicitly, SearchItemProvider]
        internal static SearchProvider CreateProvider()
        {
            return new SearchProvider(type, displayName)
            {
                filterId = "res:",
                fetchItems = (context, items, provider) => SearchItems(context, provider),
                isExplicitProvider = true,
                fetchDescription = FetchDescription,
                fetchThumbnail = FetchThumbnail,
                fetchPreview = FetchPreview,
                trackSelection = TrackSelection,
                fetchKeywords = FetchKeywords,
            };
        }

        [UsedImplicitly, SearchActionsProvider]
        internal static IEnumerable<SearchAction> ActionHandlers()
        {
            return new[]
            {
                new SearchAction(type, "select", null, "Select resource...")
                {
                    handler = (item, context) => TrackSelection(item, context)
                }
            };
        }

        private static IEnumerable<SearchItem> SearchItems(SearchContext context, SearchProvider provider)
        {
            var subFilters = context.textFilters.Where(filter => filter.IndexOf(":") > 0 && !filter.EndsWith(":"))
                .Select(filter => filter.Split(':'));
            var enabledSubFilters = k_SubMatches.Where(subMatch => subFilters.FirstOrDefault(filter => subMatch.matchToken == filter[0]) != null)
                .Select(subMatch =>
                {
                    var filterQuery = subFilters.FirstOrDefault(filter => subMatch.matchToken == filter[0])?[1];
                    return Tuple.Create(subMatch, filterQuery);
                });

            var focusedFilters = context.textFilters.Where(filter => filter.EndsWith(":"))
                .Select(filter => filter.Substring(0, filter.Length - 1)).ToList();
            var enabledFocusedFilters = k_SubMatches.Where(subMatch => focusedFilters.Count == 0 || focusedFilters.FirstOrDefault(filterToken => subMatch.matchToken == filterToken) != null).ToList();

            var objs = Resources.FindObjectsOfTypeAll(typeof(UnityEngine.Object));
            var filteredObjs = objs.Where(obj => enabledSubFilters.All(subFilter => subFilter.Item1.matchQuery(obj, subFilter.Item2)));
            foreach (var obj in filteredObjs)
            {
                if (context.tokenizedSearchQuery.All(query => enabledFocusedFilters.Any(matchOp => matchOp.matchQuery(obj, query))))
                    yield return provider.CreateItem(obj.GetInstanceID().ToString(), $"{obj.name} [{obj.GetType()}] ({obj.GetInstanceID()})", null, null, obj.GetInstanceID());
                else
                    yield return null;
            }
        }

        private static string FetchDescription(SearchItem item, SearchContext context)
        {
            var instanceID = Convert.ToInt32(item.id);
            var obj = EditorUtility.InstanceIDToObject(instanceID);
            var sb = new StringBuilder();
            var matchingDescriptor = k_Descriptors.Where(descriptor => descriptor.Match(obj)).ToList();
            foreach (var descriptor in matchingDescriptor)
            {
                if (!descriptor.GetDescription(obj, sb))
                    break;
            }
            item.description = sb.ToString();
            return item.description;
        }

        private static Texture2D FetchThumbnail(SearchItem item, SearchContext context)
        {
            if (item.thumbnail)
                return item.thumbnail;

            var instanceID = Convert.ToInt32(item.id);
            var obj = EditorUtility.InstanceIDToObject(instanceID);
            var descriptor = k_Descriptors.FirstOrDefault(desc => desc.Match(obj));
            return descriptor == null ? Icons.quicksearch : descriptor.GetThumbnail(obj);
        }

        static Texture2D FetchPreview(SearchItem item, SearchContext context, Vector2 size, FetchPreviewOptions options)
        {
            if (item.preview)
                return item.preview;

            var instanceID = Convert.ToInt32(item.id);
            var obj = EditorUtility.InstanceIDToObject(instanceID);
            var descriptor = k_Descriptors.FirstOrDefault(desc => desc.Match(obj));
            return descriptor == null ? Icons.quicksearch : descriptor.GetPreview(obj, (int)size.x, (int)size.y);
        }

        private static void TrackSelection(SearchItem item, SearchContext context)
        {
            var instanceID = Convert.ToInt32(item.id);
            var obj = EditorUtility.InstanceIDToObject(instanceID);
            var descriptor = k_Descriptors.FirstOrDefault(desc => desc.Match(obj));
            descriptor?.TrackSelection(obj);
        }

        private static void FetchKeywords(SearchContext context, string lastToken, List<string> items)
        {
            var index = lastToken.IndexOf(":");
            if (index < 1)
                return;
            var filterToken = lastToken.Substring(0, index);
            var matchOp = k_SubMatches.FirstOrDefault(subMatch => subMatch.matchToken == filterToken);
            if (matchOp.fetchKeywords == null)
                return;
            items.AddRange(matchOp.fetchKeywords(context, lastToken).Select(k => $"{matchOp.matchToken}:{k}"));
        }

        private static bool MatchByType(UnityEngine.Object obj, string searchQuery)
        {
            var fullName = obj.GetType().FullName;
            return fullName != null && fullName.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool MatchByName(UnityEngine.Object obj, string searchQuery)
        {
            return obj.name.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool MatchById(UnityEngine.Object obj, string searchQuery)
        {
            return obj.GetInstanceID().ToString().IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool MatchByTag(UnityEngine.Object obj, string searchQuery)
        {
            var go = obj as GameObject;
            return go && go.tag.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IEnumerable<string> FetchTypeKeywords(SearchContext context, string lastToken)
        {
            return typeFilter;
        }

        private static IEnumerable<string> FetchTagKeywords(SearchContext context, string lastToken)
        {
            return UnityEditorInternal.InternalEditorUtility.tags;
        }
    }
}


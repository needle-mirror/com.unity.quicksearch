using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Unity.QuickSearch.Providers
{
    static class StaticMethodProvider
    {
        private const string type = "static_methods";
        private const string displayName = "Static API";

        private static MethodInfo[] methods;

        [SearchItemProvider]
        internal static SearchProvider CreateProvider()
        {
            return new SearchProvider(type, displayName)
            {
                priority = 85,
                filterId = "#",
                isExplicitProvider = true,
                fetchItems = (context, items, provider) => FetchItems(context, provider),
                fetchThumbnail = (item, context) => Icons.shortcut
            };
        }

        private static IEnumerable<SearchItem> FetchItems(SearchContext context, SearchProvider provider)
        {
            // Cache all available static APIs
            if (methods == null)
                methods = FetchStaticAPIMethodInfo();

            var lowerCasePattern = context.searchQuery.ToLowerInvariant();
            var matches = new List<int>();
            foreach (var m in methods)
            {
                long score = 0;
                if (FuzzySearch.FuzzyMatch(lowerCasePattern, m.Name.ToLowerInvariant(), ref score, matches))
                {
                    var visibilityString = !m.IsPublic ? "<i>Internal</i> - " : string.Empty;
                    yield return provider.CreateItem(context, m.Name, m.IsPublic ? ~(int)score - 999 : ~(int)score, m.Name, $"{visibilityString}{m.DeclaringType} - {m}", null, m);
                }
                else
                    yield return null;
            }
        }

        private static MethodInfo[] FetchStaticAPIMethodInfo()
        {
            #if QUICKSEARCH_DEBUG
            using (new DebugTimer("GetAllStaticMethods"))
            #endif
            {
                bool isDevBuild = UnityEditor.Unsupported.IsDeveloperBuild();
                var staticMethods = AppDomain.CurrentDomain.GetAllStaticMethods(isDevBuild);
                #if QUICKSEARCH_DEBUG
                Debug.Log($"Fetched {staticMethods.Length} APIs");
                #endif

                return staticMethods;
            }
        }

        private static void LogResult(object result)
        {
            if (result == null)
                return;

            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, result as UnityEngine.Object, result.ToString());
        }

        [SearchActionsProvider]
        internal static IEnumerable<SearchAction> ActionHandlers()
        {
            return new[]
            {
                new SearchAction(type, "exec", null, "Execute method", (items) =>
                {
                    foreach (var item in items)
                    {
                        var m = item.data as MethodInfo;
                        if (m == null)
                            return;
                        var result = m.Invoke(null, null);
                        if (result == null)
                            return;
                        if (result is string || !(result is IEnumerable list))
                        {
                            LogResult(result);
                            EditorGUIUtility.systemCopyBuffer = result.ToString();
                        }
                        else
                        {
                            foreach (var e in list)
                                LogResult(e);
                        }
                    }
                })
            };
        }
    }
}

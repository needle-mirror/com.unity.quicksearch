using System;
using System.Collections.Generic;
using System.Globalization;
using JetBrains.Annotations;
using UnityEditor;

namespace Unity.QuickSearch
{
    namespace Providers
    {
        [UsedImplicitly]
        static class Calculator
        {
            internal static string type = "calculator";
            internal static string displayName = "Calculator";

            [UsedImplicitly, SearchItemProvider]
            internal static SearchProvider CreateProvider()
            {
                return new SearchProvider(type, displayName)
                {
                    filterId = "=",
                    isExplicitProvider = true,
                    fetchItems = (context, items, provider) =>
                    {
                        var item = context.searchQuery;
                        if (Evaluate(context.searchQuery, out var result))
                            item += " = " + result;

                        items.Add(provider.CreateItem(type, "compute", item));
                    },

                    fetchThumbnail = (item, context) => Icons.settings
                };
            }

            [UsedImplicitly, SearchActionsProvider]
            internal static IEnumerable<SearchAction> ActionHandlers()
            {
                return new[]
                {
                    new SearchAction(type, "compute", null, "Compute...") {
                        handler = (item, context) =>
                        {
                            if (Evaluate(context.searchQuery, out var result))
                            {
                                UnityEngine.Debug.Log(result);
                                EditorGUIUtility.systemCopyBuffer = result.ToString(CultureInfo.InvariantCulture);
                            }
                        }
                    }
                };
            }

            internal static bool Evaluate(string expression, out double result)
            {
                try
                {
                    return ExpressionEvaluator.Evaluate(expression, out result);
                }
                catch (Exception)
                {
                    result = 0.0;
                    UnityEngine.Debug.LogError("Error while parsing: " + expression);
                    return false;
                }
            }
        }
    }
}

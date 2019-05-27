using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Unity.QuickSearch
{
    namespace Providers
    {
        [UsedImplicitly]
        static class PackageManagerProvider
        {
            internal static string type = "packages";
            internal static string displayName = "Packages";

            private static ListRequest s_ListRequest = null;
            private static SearchRequest s_SearchRequest = null;

            [UsedImplicitly, SearchItemProvider]
            internal static SearchProvider CreateProvider()
            {
                return new SearchProvider(type, displayName)
                {
                    priority = 90,
                    filterId = "pkg:",

                    onEnable = () =>
                    {
                        s_ListRequest = UnityEditor.PackageManager.Client.List();
                        s_SearchRequest = UnityEditor.PackageManager.Client.SearchAll();
                    },

                    fetchItems = (context, items, provider) =>
                    {
                        if (s_SearchRequest == null || s_ListRequest == null)
                            return;
                        
                        if (!s_SearchRequest.IsCompleted || !s_ListRequest.IsCompleted)
                            return;

                        if (s_SearchRequest.Result == null || s_ListRequest.Result == null)
                            return;

                        items.AddRange(s_SearchRequest.Result
                            .Where(p => SearchProvider.MatchSearchGroups(context, p.description.ToLowerInvariant(), true) ||
                                        SearchProvider.MatchSearchGroups(context, p.name.ToLowerInvariant(), true) ||
                                        p.keywords.Contains(context.searchQuery))
                            .Select(p => provider.CreateItem(p.packageId, 
                                String.IsNullOrEmpty(p.resolvedPath) ? 0 : 1, FormatLabel(p), FormatDescription(p), null, p)).ToArray());
                    },

                    fetchThumbnail = (item, context) => Icons.settings
                };
            }

            private static string FormatName(UnityEditor.PackageManager.PackageInfo pi)
            {
                if (String.IsNullOrEmpty(pi.displayName))
                    return $"{pi.name}@{pi.version}";
                return $"{pi.displayName} ({pi.name}@{pi.version})";
            }

            private static string FormatLabel(UnityEditor.PackageManager.PackageInfo pi)
            {
                var installedPackage = s_ListRequest.Result.FirstOrDefault(l => l.name == pi.name);
                var status = installedPackage != null ? (installedPackage.version == pi.version ? 
                    " - <i>In Project</i>" : " - <b>Update Available</b>") : "";
                if (String.IsNullOrEmpty(pi.displayName))
                    return $"{pi.name}@{pi.version}{status}";
                return $"{FormatName(pi)}{status}";
            }

            private static string FormatDescription(UnityEditor.PackageManager.PackageInfo pi)
            {
                const int k_MaxLength = 90;
                var desc = pi.description.Replace("\r", "").Replace("\n", "");
                if (desc.Length > k_MaxLength)
                    desc = desc.Substring(0, Math.Min(k_MaxLength, desc.Length)) + "...";
                return desc;
            }

            [UsedImplicitly, SearchActionsProvider]
            internal static IEnumerable<SearchAction> ActionHandlers()
            {
                return new[]
                {
                    new SearchAction(type, "install", null, "Install...")
                    {
                        handler = (item, context) =>
                        {
                            var packageInfo = (UnityEditor.PackageManager.PackageInfo)item.data;
                            if (EditorUtility.DisplayDialog("About to install package " + item.id, 
                                "Are you sure you want to install the following package?\r\n\r\n" +
                                FormatName(packageInfo), "Install...", "Cancel"))
                                UnityEditor.PackageManager.Client.Add(item.id);
                        }
                    },
                    new SearchAction(type, "browse", null, "Browse...")
                    {
                        handler = (item, context) =>
                        {
                            var packageInfo = (UnityEditor.PackageManager.PackageInfo)item.data;
                            if (String.IsNullOrEmpty(packageInfo.author.url))
                                Debug.LogWarning($"Package {FormatName(packageInfo)} has no URL defined.");
                            else
                                EditorUtility.OpenWithDefaultApp(packageInfo.author.url);
                        }
                    },
                    new SearchAction(type, "remove", null, "Remove")
                    {
                        handler = (item, context) =>
                        {
                            var packageInfo = (UnityEditor.PackageManager.PackageInfo)item.data;
                            UnityEditor.PackageManager.Client.Remove(packageInfo.name);
                        }
                    }
                };
            }
        }
    }
}

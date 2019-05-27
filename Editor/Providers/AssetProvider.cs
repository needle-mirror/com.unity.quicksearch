using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.QuickSearch
{
    namespace Providers
    {
        [UsedImplicitly]
        static class AssetProvider
        {
            internal const string k_ProjectAssetsType = "asset";

            internal static string displayName = "Asset";
            /* Filters:
                 t:<type>
                l:<label>
                ref[:id]:path
                v:<versionState>
                s:<softLockState>
                a:<area> [assets, packages]
             */

            internal static string[] typeFilter = new[]
            {
                "Folder",
                "DefaultAsset",
                "AnimationClip",
                "AudioClip",
                "AudioMixer",
                "ComputeShader",
                "Font",
                "GUISKin",
                "Material",
                "Mesh",
                "Model",
                "PhysicMaterial",
                "Prefab",
                "Scene",
                "Script",
                "Shader",
                "Sprite",
                "StyleSheet",
                "Texture",
                "VideoClip"
            };

            private static readonly char[] k_InvalidIndexedChars = new char[] { '*', ':' };
            private static readonly char[] k_InvalidSearchFileChars = Path.GetInvalidFileNameChars().Where(c => c != '*').ToArray();

            private static SearchProvider CreateProvider(string type, string label, string filterId, int priority, GetItemsHandler fetchItemsHandler)
            {
                return new SearchProvider(type, label)
                {
                    priority = priority,
                    filterId = filterId,

                    isEnabledForContextualSearch = () => QuickSearchTool.IsFocusedWindowTypeName("ProjectBrowser"),

                    fetchItems = fetchItemsHandler,

                    fetchKeywords = (context, lastToken, items) =>
                    {
                        if (!lastToken.StartsWith("t:"))
                            return;
                        items.AddRange(typeFilter.Select(t => "t:" + t));
                    },

                    fetchDescription = (item, context) =>
                    {
                        if (AssetDatabase.IsValidFolder(item.id))
                            return item.id;
                        long fileSize = new FileInfo(item.id).Length;
                        item.description = $"{item.id} ({EditorUtility.FormatBytes(fileSize)})";

                        return item.description;
                    },

                    fetchThumbnail = (item, context) =>
                    {
                        if (item.thumbnail)
                            return item.thumbnail;

                        if (context.totalItemCount < 200)
                        {
                            var obj = AssetDatabase.LoadAssetAtPath<Object>(item.id);
                            if (obj != null)
                                item.thumbnail = AssetPreview.GetAssetPreview(obj);
                            if (item.thumbnail)
                                return item.thumbnail;
                        }
                        item.thumbnail = AssetDatabase.GetCachedIcon(item.id) as Texture2D;
                        if (item.thumbnail)
                            return item.thumbnail;

                        item.thumbnail = UnityEditorInternal.InternalEditorUtility.FindIconForFile(item.id);
                        return item.thumbnail;
                    },

                    startDrag = (item, context) =>
                    {
                        var obj = AssetDatabase.LoadAssetAtPath<Object>(item.id);
                        if (obj != null)
                        {
                            DragAndDrop.PrepareStartDrag();
                            DragAndDrop.objectReferences = new[] { obj };
                            DragAndDrop.StartDrag("Drag asset");
                        }
                    },

                    trackSelection = (item, context) =>
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<Object>(item.id);
                        if (asset != null)
                            EditorGUIUtility.PingObject(asset);
                    },

                    subCategories = new List<NameId>()
                };
            }

            [UsedImplicitly, SearchItemProvider]
            internal static SearchProvider CreateProjectAssetsProvider()
            {
                FileEntryIndexer fileIndexer = null;
                if (SearchSettings.useFilePathIndexer)
                {
                    var listPackageRequest = UnityEditor.PackageManager.Client.List();
                    while (!listPackageRequest.IsCompleted)
                        System.Threading.Thread.Yield();

                    fileIndexer = new FileEntryIndexer(new FileEntryIndexer.Root[] { new FileEntryIndexer.Root(Application.dataPath, "Assets") }
                            .Concat(listPackageRequest.Result.Select(p => new FileEntryIndexer.Root(p.resolvedPath, p.assetPath))));
                }

                var provider = CreateProvider(k_ProjectAssetsType, displayName, "p:", 25, 
                    (context, items, _provider) => SearchAssets(context, items, _provider, fileIndexer));

                provider.subCategories.Add(new NameId("packages", "packages"));
                if (SearchSettings.useFilePathIndexer)
                    provider.subCategories.Add(new NameId("indexing", "use index (experimental)"));

                return provider;
            }

            private static void SearchAssets(SearchContext context, List<SearchItem> items, SearchProvider provider, FileEntryIndexer fileIndexer)
            {
                var filter = context.searchQuery;
                var searchPackages = context.categories.Any(c => c.name.id == "packages" && c.isEnabled);
                var searchIndex = context.categories.Any(c => c.name.id == "indexing" && c.isEnabled);

                if (searchIndex && fileIndexer != null && fileIndexer.IsReady())
                {
                    if (filter.IndexOfAny(k_InvalidIndexedChars) == -1)
                    {
                        items.AddRange(fileIndexer.EnumerateFileIndexes(filter, searchPackages ? int.MaxValue : 100).Take(201)
                                                  .Select(e => provider.CreateItem(e.path, e.score, Path.GetFileName(e.path),
                                                                                    null,//$"{e.path} ({e.score})", 
                                                                                    null, null)));
                        if (!context.wantsMore)
                            return;
                    }
                }

                if (!searchPackages)
                {
                    if (!filter.Contains("a:assets"))
                        filter = "a:assets " + filter;
                }

                items.AddRange(AssetDatabase.FindAssets(filter)
                                            .Select(AssetDatabase.GUIDToAssetPath)
                                            .Take(202)
                                            .Select(path => provider.CreateItem(path, Path.GetFileName(path))));

                var safeFilter = string.Join("_", context.searchQuery.Split(k_InvalidSearchFileChars));
                if (context.searchQuery.Contains('*'))
                {
                    items.AddRange(Directory.EnumerateFiles(Application.dataPath, safeFilter, SearchOption.AllDirectories)
                                            .Select(path => provider.CreateItem(path.Replace(Application.dataPath, "Assets").Replace("\\", "/"),
                                                                                Path.GetFileName(path))));
                }
            }

            [UsedImplicitly, SearchActionsProvider]
            internal static IEnumerable<SearchAction> ActionHandlers()
            {
                return CreateActionHandlers(k_ProjectAssetsType);
            }

            internal static IEnumerable<SearchAction> CreateActionHandlers(string type)
            {
                #if UNITY_EDITOR_OSX
                const string k_RevealActionLabel = "Reveal in Finder...";
                #else
                const string k_RevealActionLabel = "Show in Explorer...";
                #endif

                return new[]
                {
                    new SearchAction(type, "select", null, "Select asset...")
                    {
                        handler = (item, context) =>
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<Object>(item.id);
                            if (asset != null)
                            {
                                Selection.activeObject = asset;
                                EditorApplication.delayCall += () =>
                                {
                                    EditorWindow.FocusWindowIfItsOpen(Utils.GetProjectBrowserWindowType());
                                    EditorApplication.delayCall += () => EditorGUIUtility.PingObject(asset);
                                };
                            }
                            else
                            {
                                EditorUtility.RevealInFinder(item.id);
                            }
                        }
                    },
                    new SearchAction(type, "open", null, "Open asset...")
                    {
                        handler = (item, context) =>
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<Object>(item.id);
                            if (asset != null) AssetDatabase.OpenAsset(asset);
                        }
                    },
                    new SearchAction(type, "reveal", null, k_RevealActionLabel)
                    {
                        handler = (item, context) => EditorUtility.RevealInFinder(item.id)
                    },
                    new SearchAction(type, "context", null, "Context")
                    {
                        handler = (item, context) =>
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<Object>(item.id);
                            if (asset != null)
                            {
                                Selection.activeObject = asset;
                                EditorUtility.DisplayPopupMenu(QuickSearchTool.ContextualActionPosition, "Assets/", null);
                            }
                        }
                    }
                };
            }

            [UsedImplicitly]
            class AssetRefreshWatcher : AssetPostprocessor
            {
                private static bool s_Enabled;
                static AssetRefreshWatcher()
                {
                    EditorApplication.update += Enable;
                }

                private static void Enable()
                {
                    EditorApplication.update -= Enable;
                    s_Enabled = true;
                }

                [UsedImplicitly]
                static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
                {
                    if (!s_Enabled)
                        return;

                    SearchService.RaiseContentRefreshed(importedAssets, deletedAssets.Concat(movedFromAssetPaths).Distinct().ToArray(), movedAssets);
                }
            }

            #if UNITY_2019_1_OR_NEWER
            [UsedImplicitly, Shortcut("Help/Quick Search/Assets")]
            public static void PopQuickSearch()
            {
                SearchService.Filter.ResetFilter(false);
                SearchService.Filter.SetFilter(true, k_ProjectAssetsType);
                QuickSearchTool.ShowWindow(false);
            }
            #endif
        }
    }
}

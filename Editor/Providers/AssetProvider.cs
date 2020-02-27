using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.QuickSearch.Providers
{
    [UsedImplicitly]
    static class AssetProvider
    {
        private const string type = "asset";
        private const string displayName = "Asset";

        private static List<ADBIndex> assetIndexes;
        private static FileSearchIndexer fileIndexer;

        private static readonly string[] baseTypeFilters = new[]
        {
            "DefaultAsset", "AnimationClip", "AudioClip", "AudioMixer", "ComputeShader", "Font", "GUISKin", "Material", "Mesh",
            "Model", "PhysicMaterial", "Prefab", "Scene", "Script", "ScriptableObject", "Shader", "Sprite", "StyleSheet", "Texture", "VideoClip"
        };

        private static readonly string[] typeFilter =
            baseTypeFilters.Concat(TypeCache.GetTypesDerivedFrom<ScriptableObject>()
                .Where(t => !t.IsSubclassOf(typeof(Editor)) || !t.IsSubclassOf(typeof(EditorWindow)) || !t.IsSubclassOf(typeof(AssetImporter)))
                .Select(t => t.Name)
                .Distinct()
                .OrderBy(n => n)).ToArray();

        private static readonly char[] k_InvalidSearchFileChars = Path.GetInvalidFileNameChars().Where(c => c != '*').ToArray();

        private static void TrackAssetIndexChanges(string[] updated, string[] deleted, string[] moved)
        {
            if (updated.Concat(deleted).Any(u => u.EndsWith(".index", StringComparison.OrdinalIgnoreCase)))
                assetIndexes = ADBIndex.Enumerate().ToList();
        }

        [UsedImplicitly, SearchItemProvider]
        internal static SearchProvider CreateProvider()
        {
            return new SearchProvider(type, displayName)
            {
                priority = 25,
                filterId = "p:",
                showDetails = SearchSettings.fetchPreview,

                onEnable = () =>
                {
                    if (SearchSettings.assetIndexing == SearchAssetIndexing.Files && fileIndexer == null)
                    {
                        var packageRoots = Utils.GetPackagesPaths().Select(p => new SearchIndexerRoot(Path.GetFullPath(p).Replace('\\', '/'), p));
                        var roots = new[] { new SearchIndexerRoot(Application.dataPath, "Assets") }.Concat(packageRoots);
                        fileIndexer = new FileSearchIndexer(type, roots);
                        fileIndexer.Build();
                    }
                    else if (SearchSettings.assetIndexing == SearchAssetIndexing.Complete && assetIndexes == null)
                    {
                        assetIndexes = ADBIndex.Enumerate().ToList();
                        foreach (var db in assetIndexes)
                            db.IncrementalUpdate();
                        AssetPostprocessorIndexer.contentRefreshed += TrackAssetIndexChanges;
                    }
                },

                isEnabledForContextualSearch = () => Utils.IsFocusedWindowTypeName("ProjectBrowser"),

                toObject = (item, type) => AssetDatabase.LoadAssetAtPath(item.id, type),

                fetchItems = (context, items, provider) => SearchAssets(context, provider),

                fetchKeywords = (context, lastToken, items) =>
                {
                    if (!lastToken.Contains(":"))
                        return;
                    if (SearchSettings.assetIndexing == SearchAssetIndexing.Complete)
                        items.AddRange(assetIndexes.SelectMany(db => db.index.GetKeywords()));
                    else
                        items.AddRange(typeFilter.Select(t => "t:" + t));
                },

                fetchDescription = (item, context) => (item.description = GetAssetDescription(item.id)),
                fetchThumbnail = (item, context) => Utils.GetAssetThumbnailFromPath(item.id),
                fetchPreview = (item, context, size, options) => Utils.GetAssetPreviewFromPath(item.id, size, options),

                startDrag = (item, context) =>
                {
                    var obj = AssetDatabase.LoadAssetAtPath<Object>(item.id);
                    if (obj)
                        Utils.StartDrag(obj, item.label);
                },
                trackSelection = (item, context) => Utils.PingAsset(item.id)
            };
        }

        private static IEnumerator SearchAssets(SearchContext context, SearchProvider provider)
        {
            var searchQuery = context.searchQuery;
            const bool searchGuids = true;
            const bool searchPackages = true;

            // Search by GUIDs
            if (searchGuids)
            {
                var gui2Path = AssetDatabase.GUIDToAssetPath(searchQuery);
                if (!String.IsNullOrEmpty(gui2Path))
                    yield return provider.CreateItem(gui2Path, -1, $"{Path.GetFileName(gui2Path)} ({searchQuery})", null, null, null);
            }

            if (SearchSettings.assetIndexing == SearchAssetIndexing.Complete)
            {
                yield return assetIndexes.Select(db => SearchIndexes(context, provider, db.index));
            }
            else if (SearchSettings.assetIndexing == SearchAssetIndexing.Files)
            {
                if (searchQuery.IndexOf(':') != -1)
                {
                    foreach (var assetEntry in AssetDatabase.FindAssets(searchQuery)
                                                            .Select(AssetDatabase.GUIDToAssetPath)
                                                            .Select(path => provider.CreateItem(path, Path.GetFileName(path))))
                        yield return assetEntry;
                }

                while (!fileIndexer.IsReady())
                    yield return null;

                foreach (var item in SearchFiles(searchQuery, searchPackages, provider))
                    yield return item;
            }
            else
            {
                foreach (var assetEntry in AssetDatabase.FindAssets(searchQuery).Select(AssetDatabase.GUIDToAssetPath).Select(path => provider.CreateItem(path, Path.GetFileName(path))))
                    yield return assetEntry;
            }

            if (context.wantsMore && context.filterType != null && String.IsNullOrEmpty(context.searchQuery))
            {
                yield return AssetDatabase.FindAssets($"t:{context.filterType.Name}")
                    .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                    .Select(path => provider.CreateItem(path, 999, Path.GetFileName(path), null, null, null));
            }

            // Search file system wild cards
            if (context.searchQuery.Contains('*'))
            {
                var safeFilter = string.Join("_", context.searchQuery.Split(k_InvalidSearchFileChars));
                var projectFiles = System.IO.Directory.EnumerateFiles(Application.dataPath, safeFilter, System.IO.SearchOption.AllDirectories);
                projectFiles = projectFiles.Select(path => path.Replace(Application.dataPath, "Assets").Replace("\\", "/"));
                foreach (var fileEntry in projectFiles.Select(path => provider.CreateItem(path, Path.GetFileName(path))))
                    yield return fileEntry;
            }
        }

        private static IEnumerator SearchIndexes(SearchContext context, SearchProvider provider, AssetIndexer adbIndex)
        {
            var searchQuery = context.searchQuery;

            // Search index
            while (!adbIndex.IsReady())
                yield return null;

            yield return adbIndex.Search(searchQuery.ToLowerInvariant()).Select(e =>
            {
                var itemScore = e.score;
                var words = context.searchPhrase;
                var filenameNoExt = Path.GetFileNameWithoutExtension(e.id);
                if (filenameNoExt.Equals(words, StringComparison.OrdinalIgnoreCase))
                    itemScore = SearchProvider.k_RecentUserScore - 1;

                var filename = Path.GetFileName(e.id);
                string description = adbIndex.GetDebugIndexStrings(e.id);
                return provider.CreateItem(e.id, itemScore, filename, description, null, null);
            });
        }


        private static IEnumerable<SearchItem> SearchFiles(string filter, bool searchPackages, SearchProvider provider)
        {
            UnityEngine.Assertions.Assert.IsNotNull(fileIndexer);

            return fileIndexer.Search(filter, searchPackages ? int.MaxValue : 100).Select(e =>
            {
                var filename = Path.GetFileName(e.id);
                var filenameNoExt = Path.GetFileNameWithoutExtension(e.id);
                var itemScore = e.score;
                if (filenameNoExt.Equals(filter, StringComparison.OrdinalIgnoreCase))
                    itemScore = SearchProvider.k_RecentUserScore + 1;
                return provider.CreateItem(e.id, itemScore, filename, null, null, null);
            });
        }

        internal static string GetAssetDescription(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
                return assetPath;
            var fi = new FileInfo(assetPath);
            if (!fi.Exists)
                return "File does not exist anymore.";
            var fileSize = new FileInfo(assetPath).Length;
            return $"{assetPath} ({EditorUtility.FormatBytes(fileSize)})";
        }

        [UsedImplicitly, SearchActionsProvider]
        internal static IEnumerable<SearchAction> CreateActionHandlers()
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
                    handler = (item, context) => Utils.FrameAssetFromPath(item.id)
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
                            var old = Selection.activeObject;
                            Selection.activeObject = asset;
                            EditorUtility.DisplayPopupMenu(QuickSearch.contextualActionPosition, "Assets/", null);
                            EditorApplication.delayCall += () => Selection.activeObject = old;
                        }
                    }
                }
            };
        }

        [UsedImplicitly, Shortcut("Help/Quick Search/Assets")]
        internal static void PopQuickSearch()
        {
            QuickSearch.OpenWithContextualProvider(type);
        }
    }
}

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

        private static List<SearchDatabase> assetIndexes;
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

        private static readonly string[] k_NonSimpleSearchTerms = new string[] {"(", ")", "-", "=", "<", ">", "or", "and"};
        private static readonly char[] k_InvalidSearchFileChars = Path.GetInvalidFileNameChars().Where(c => c != '*').ToArray();

        [UsedImplicitly, SearchItemProvider]
        internal static SearchProvider CreateProvider()
        {
            return new SearchProvider(type, displayName)
            {
                priority = 25,
                filterId = "p:",
                showDetails = true,
                showDetailsOptions = ShowDetailsOptions.Default | ShowDetailsOptions.Inspector,

                isEnabledForContextualSearch = () => Utils.IsFocusedWindowTypeName("ProjectBrowser"),
                toObject = (item, type) => AssetDatabase.LoadAssetAtPath(item.id, type),
                fetchItems = (context, items, provider) => SearchAssets(context, provider),
                fetchKeywords = (context, lastToken, keywords) => FetchKeywords(lastToken, keywords),
                fetchDescription = (item, context) => (item.description = GetAssetDescription(item.id)),
                fetchThumbnail = (item, context) => Utils.GetAssetThumbnailFromPath(item.id),
                fetchPreview = (item, context, size, options) => Utils.GetAssetPreviewFromPath(item.id, size, options),
                openContextual = (selection, context, rect) => OpenContextualMenu(selection, rect),
                startDrag = (item, context) => StartDrag(item, context),
                trackSelection = (item, context) => Utils.PingAsset(item.id)
            };
        }

        private static void TrackAssetIndexChanges(string[] updated, string[] deleted, string[] moved)
        {
            if (updated.Concat(deleted).Any(u => u.EndsWith(".index", StringComparison.OrdinalIgnoreCase)))
                assetIndexes = SearchDatabase.Enumerate("asset").ToList();
        }

        private static bool OpenContextualMenu(SearchSelection selection, Rect contextRect)
        {
            var old = Selection.instanceIDs;
            SearchUtils.SelectMultipleItems(selection);
            EditorUtility.DisplayPopupMenu(contextRect, "Assets/", null);
            EditorApplication.delayCall += () => EditorApplication.delayCall += () => Selection.instanceIDs = old;
            return true;
        }

        private static void StartDrag(SearchItem item, SearchContext context)
        {
                var selectedObjects = context.selection.Select(i => AssetDatabase.LoadAssetAtPath<Object>(i.id));
                Utils.StartDrag(selectedObjects.ToArray(), item.GetLabel(context, true));
        }

        private static void FetchKeywords(in string lastToken, List<string> keywords)
        {
            if (assetIndexes == null || !lastToken.Contains(":"))
                return;
            if (SearchSettings.assetIndexing == SearchAssetIndexing.Complete)
                keywords.AddRange(assetIndexes.SelectMany(db => db.index.GetKeywords()));
            else
                keywords.AddRange(typeFilter.Select(t => "t:" + t));
        }

        private static void SetupIndexers()
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
                assetIndexes = SearchDatabase.Enumerate("asset").ToList();
                foreach (var db in assetIndexes)
                    db.IncrementalUpdate();
                AssetPostprocessorIndexer.contentRefreshed += TrackAssetIndexChanges;
            }
        }

        private static IEnumerator SearchAssets(SearchContext context, SearchProvider provider)
        {
            var searchQuery = context.searchQuery;

            if (!String.IsNullOrEmpty(searchQuery))
            {
                if (SearchSettings.assetIndexing == SearchAssetIndexing.Files)
                {
                    if (searchQuery.IndexOf(':') != -1)
                    {
                        foreach (var assetEntry in AssetDatabase.FindAssets(searchQuery)
                                                                .Select(AssetDatabase.GUIDToAssetPath)
                                                                .Select(path => provider.CreateItem(path, Path.GetFileName(path))))
                            yield return assetEntry;
                    }

                    SetupIndexers();
                    while (!fileIndexer.IsReady())
                        yield return null;

                    foreach (var item in SearchFiles(searchQuery, provider))
                        yield return item;
                }
                else if (SearchSettings.assetIndexing == SearchAssetIndexing.Complete)
                {
                    SetupIndexers();
                    yield return assetIndexes.Select(db => SearchIndexes(context, provider, db.index));
                }

                // Search file system wild cards
                if (context.searchQuery.Contains('*'))
                {
                    var globSearch = context.searchQuery;
                    if (globSearch.IndexOf("glob:", StringComparison.OrdinalIgnoreCase) == -1 && context.searchWords.Length == 1)
                        globSearch = $"glob:\"{globSearch}\"";
                    yield return AssetDatabase.FindAssets(globSearch)
                        .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                        .Select(path => provider.CreateItem(path, 999, Path.GetFileName(path), null, null, null));
                }
                else
                {
                    // Search by GUID
                    var guidPath = AssetDatabase.GUIDToAssetPath(searchQuery);
                    if (!String.IsNullOrEmpty(guidPath))
                        yield return provider.CreateItem(guidPath, -1, $"{Path.GetFileName(guidPath)} ({searchQuery})", null, null, null);

                    if (SearchSettings.assetIndexing == SearchAssetIndexing.NoIndexing)
                    {
                        // Finally search the default asset database for any remaining results.
                        foreach (var assetPath in AssetDatabase.FindAssets(searchQuery).Select(guid => AssetDatabase.GUIDToAssetPath(guid)))
                            yield return provider.CreateItem(assetPath, 998, Path.GetFileName(assetPath), null, null, null);
                    }
                    else if (!k_NonSimpleSearchTerms.Any(t => searchQuery.IndexOf(t, StringComparison.Ordinal) != -1))
                    {
                        foreach (var assetPath in Utils.FindAssets(searchQuery))
                            yield return provider.CreateItem(assetPath, 998, Path.GetFileName(assetPath), null, null, null);
                    }
                }
            }

            if (context.wantsMore && context.filterType != null && String.IsNullOrEmpty(searchQuery))
            {
                yield return AssetDatabase.FindAssets($"t:{context.filterType.Name}")
                    .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                    .Select(path => provider.CreateItem(path, 999, Path.GetFileName(path), null, null, null));
            }
        }

        private static IEnumerator SearchIndexes(SearchContext context, SearchProvider provider, SearchIndexer index)
        {
            var searchQuery = context.searchQuery;

            // Search index
            while (!index.IsReady())
                yield return null;

            yield return index.Search(searchQuery.ToLowerInvariant()).Select(e =>
            {
                var itemScore = e.score;
                var words = context.searchPhrase;
                var filenameNoExt = Path.GetFileNameWithoutExtension(e.id);
                if (filenameNoExt.Equals(words, StringComparison.OrdinalIgnoreCase))
                    itemScore = SearchProvider.k_RecentUserScore - 1;

                var filename = Path.GetFileName(e.id);
                string description = (index as AssetIndexer)?.GetDebugIndexStrings(e.id);
                return provider.CreateItem(e.id, itemScore, filename, description, null, null);
            });
        }


        private static IEnumerable<SearchItem> SearchFiles(string searchQuery, SearchProvider provider)
        {
            UnityEngine.Assertions.Assert.IsNotNull(fileIndexer);

            return fileIndexer.Search(searchQuery).Select(e =>
            {
                var filename = Path.GetFileName(e.id);
                var filenameNoExt = Path.GetFileNameWithoutExtension(e.id);
                var itemScore = e.score;
                if (filenameNoExt.Equals(searchQuery, StringComparison.OrdinalIgnoreCase))
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
                    handler = (item, context) => Utils.FrameAssetFromPath(item.id),
                    execute = (context, items) => SearchUtils.SelectMultipleItems(items, focusProjectBrowser: true)
                },
                new SearchAction(type, "open", null, "Open asset...")
                {
                    handler = (item, context) =>
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<Object>(item.id);
                        if (asset != null) AssetDatabase.OpenAsset(asset);
                    }
                },
                new SearchAction(type, "add_scene", null, "Add scene...")
                {
                    // Only works in single selection and adds a scene to the current hierarchy.
                    enabled = (context, items) => items.Count == 1 && items.Last().id.EndsWith(".unity", StringComparison.OrdinalIgnoreCase),
                    handler = (item, context) => UnityEditor.SceneManagement.EditorSceneManager.OpenScene(item.id, UnityEditor.SceneManagement.OpenSceneMode.Additive)
                },
                new SearchAction(type, "reveal", null, k_RevealActionLabel)
                {
                    handler = (item, context) => EditorUtility.RevealInFinder(item.id)
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

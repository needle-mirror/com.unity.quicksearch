using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.QuickSearch.Providers
{
    [UsedImplicitly]
    static class ObjectProvider
    {
        private const string type = "object";
        private readonly static Vector2 k_PreviewSize = new Vector2(64, 64);

        private static List<SearchDatabase> m_ObjectIndexes;
        private static List<SearchDatabase> indexes
        {
            get
            {
                if (m_ObjectIndexes == null)
                {
                    UpdateObjectIndexes();
                    AssetPostprocessorIndexer.contentRefreshed += TrackAssetIndexChanges;
                }

                return m_ObjectIndexes;
            }
        }

        [UsedImplicitly, SearchItemProvider]
        internal static SearchProvider CreateProvider()
        {
            return new SearchProvider(type, "Objects")
            {
                priority = 55,
                filterId = "o:",
                showDetails = true,
                showDetailsOptions = ShowDetailsOptions.Inspector | ShowDetailsOptions.Description | ShowDetailsOptions.Actions,

                isEnabledForContextualSearch = () => Utils.IsFocusedWindowTypeName("ProjectBrowser"),
                toObject = (item, type) => ToObject(item, type),
                fetchItems = (context, items, provider) => SearchObjects(context, provider),
                fetchKeywords = (context, lastToken, keywords) => FetchKeywords(lastToken, keywords),
                fetchLabel = (item, context) => FetchLabel(item),
                fetchDescription = (item, context) => FetchDescription(item),
                fetchThumbnail = (item, context) => FetchThumbnail(item),
                startDrag = (item, context) => StartDrag(item, context),
                trackSelection = (item, context) => TrackSelection(item)
            };
        }

        private static void UpdateObjectIndexes()
        {
            m_ObjectIndexes = SearchDatabase.Enumerate("scene", "prefab").ToList();
        }

        private static void TrackAssetIndexChanges(string[] updated, string[] deleted, string[] moved)
        {
            if (updated.Concat(deleted).Any(u => u.EndsWith(".index", StringComparison.OrdinalIgnoreCase)))
                UpdateObjectIndexes();
        }

        private static string FetchLabel(SearchItem item)
        {
            return (item.label = ((SearchDocument)item.data).metadata);
        }

        private static string FetchDescription(SearchItem item)
        {
            if (!GlobalObjectId.TryParse(item.id, out var gid))
                return null;

            var go = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid) as GameObject;
            if (go)
                return (item.description = $"Source: {SearchUtils.GetHierarchyPath(go)}");

            var sourceAssetPath = AssetDatabase.GUIDToAssetPath(gid.assetGUID.ToString());
            return (item.description = $"Source: {GetAssetDescription(sourceAssetPath)}");
        }

        private static Texture2D FetchThumbnail(SearchItem item)
        {
            if (!GlobalObjectId.TryParse(item.id, out var gid))
                return null;
            var sourceAssetPath = AssetDatabase.GUIDToAssetPath(gid.assetGUID.ToString());
            return Utils.GetAssetPreviewFromPath(sourceAssetPath, k_PreviewSize, FetchPreviewOptions.Preview2D);
        }

        private static void StartDrag(SearchItem item, SearchContext context)
        {
            Utils.StartDrag(context.selection.Select(i => ToObject(i, typeof(Object))).ToArray(), item.GetLabel(context, true));
        }

        private static void TrackSelection(SearchItem item)
        {
            var obj = ToObject(item, typeof(Object));
            if (obj)
                EditorGUIUtility.PingObject(obj);
        }

        private static void FetchKeywords(string lastToken, List<string> keywords)
        {
            if (!lastToken.Contains(":"))
                return;
            keywords.AddRange(indexes.SelectMany(db => db.index.GetKeywords()));
        }

        private static Object ToObject(SearchItem item, Type type)
        {
            if (!GlobalObjectId.TryParse(item.id, out var gid))
                return null;

            var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
            if (obj)
            {
                if (type == null)
                    return obj;
                var objType = obj.GetType();
                if (objType == type || objType.IsSubclassOf(type))
                    return obj;

                if (obj is GameObject go)
                    return go.GetComponent(type);
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(gid.assetGUID.ToString());
            return AssetDatabase.LoadMainAssetAtPath(assetPath);
        }

        private static IEnumerator SearchObjects(SearchContext context, SearchProvider provider)
        {
            var searchQuery = context.searchQuery;

            if (searchQuery.Length > 0)
                yield return indexes.Select(db => SearchIndexes(context, provider, db.index));

            if (context.wantsMore && context.filterType != null && !context.textFilters.Contains("t:"))
            {
                var oldSearchText = context.searchText;
                if (context.searchText.Length > 0)
                    context.searchText = $"({context.searchText}) t:{context.filterType.Name}";
                else
                    context.searchText = $"t:{context.filterType.Name}";
                yield return indexes.Select(db => SearchIndexes(context, provider, db.index, 999));
                context.searchText = oldSearchText;
            }
        }

        private static IEnumerator SearchIndexes(SearchContext context, SearchProvider provider, SearchIndexer index, int scoreModifier = 0)
        {
            var searchQuery = context.searchQuery;

            // Search index
            while (!index.IsReady())
                yield return null;

            yield return index.Search(searchQuery.ToLowerInvariant()).Select(e =>
            {
                var itemScore = e.score + scoreModifier;
                return provider.CreateItem(e.id, itemScore, null, null, null, index.GetDocument(e.index));
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

        private static void SelectItems(SearchContext context, SearchItem[] items)
        {
            Selection.instanceIDs = items.Select(i => ToObject(i, typeof(Object))).Where(o => o).Select(o=>o.GetInstanceID()).ToArray();
            if (Selection.instanceIDs.Length == 0)
                return;
            EditorApplication.delayCall += () =>
            {
                EditorWindow.FocusWindowIfItsOpen(Utils.GetProjectBrowserWindowType());
                EditorApplication.delayCall += () => EditorGUIUtility.PingObject(Selection.instanceIDs.LastOrDefault());
            };
        }

        private static void OpenItem(SearchItem item, SearchContext context)
        {
            if (!SelectObjectbyId(item.id, out var assetGUID))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(assetGUID);
                var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                if (asset != null)
                {
                    AssetDatabase.OpenAsset(asset);
                    EditorApplication.delayCall += () => SelectObjectbyId(item.id);
                }
            }
        }

        private static bool SelectObjectbyId(string id)
        {
            return SelectObjectbyId(id, out _);
        }

        private static bool SelectObjectbyId(string id, out string guid)
        {
            guid = null;
            if (!GlobalObjectId.TryParse(id, out var gid))
                return false;
            guid = gid.assetGUID.ToString();
            var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
            if (obj)
            {
                Utils.SelectObject(obj);
                return true;
            }
            return false;
        }

        [UsedImplicitly, SearchActionsProvider]
        internal static IEnumerable<SearchAction> CreateActionHandlers()
        {
            return new[]
            {
                new SearchAction(type, "select", null, "Select asset(s)...", SelectItems),
                new SearchAction(type, "open", null, "Open asset...", OpenItem)
            };
        }
    }
}

using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Experimental.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Unity.QuickSearch
{
    namespace Providers
    {
        [UsedImplicitly]
        static class SceneObjects
        {
            public struct GOD
            {
                public string name;
                public GameObject gameObject;
            }

            class SceneSearchProvider : SearchProvider
            {
                private GOD[] gods { get; set; }

                public SceneSearchProvider(string providerId, string displayName = null)
                    : base(providerId, displayName)
                {
                    priority = 50;
                    filterId = "h:";

                    subCategories = new List<NameId>
                    {
                        new NameId("fuzzy", "fuzzy")
                    };

                    onEnable = () =>
                    {
                        var objects = UnityEngine.Object.FindObjectsOfType(typeof(GameObject));
                        gods = new GOD[objects.Length];
                        for (int i = 0; i < objects.Length; ++i)
                        {
                            gods[i].gameObject = (GameObject)objects[i];
                            gods[i].name = CleanString(gods[i].gameObject.name.ToLower());
                        }
                    };

                    isEnabledForContextualSearch = () => QuickSearchTool.IsFocusedWindowTypeName("SceneView") || QuickSearchTool.IsFocusedWindowTypeName("SceneHierarchyWindow");

                    onDisable = () => 
                    {
                        gods = new GOD[0];
                    };

                    fetchItems = (context, items, provider) =>
                    {
                        if (gods == null)
                            return;

                        var useFuzzySearch = context.categories.Any(c => c.name.id == "fuzzy" && c.isEnabled);
                        var sq = CleanString(context.searchQuery.ToLowerInvariant());

                        int addedCount = 0;
                        List<int> matches = new List<int>();
                        for (int i = 0, end = gods.Length; i != end; ++i)
                        {
                            var go = gods[i].gameObject;
                            if (!go)
                                continue;

                            long score = 1;
                            if (useFuzzySearch)
                            {
                                if (!FuzzySearch.FuzzyMatch(sq, gods[i].name, ref score, matches))
                                    continue;
                            }
                            else
                            {
                                if (!MatchSearchGroups(context, gods[i].name, true))
                                    continue;
                            }

                            var gameObjectId = go.GetInstanceID().ToString();
                            var item = provider.CreateItem(gameObjectId, ~(int)score, null, null, null, null);
                            item.customDescriptionFormatter = useFuzzySearch;
                            items.Add(item);
                            if (++addedCount >= 200)
                                break;
                        }
                    };

                    fetchLabel = (item, context) =>
                    {
                        if (item.label != null)
                            return item.label;

                        var go = ObjectFromItem(item);
                        if (!go)
                            return item.id;
                        item.label = $"{go.transform.GetPath()} ({item.id})";

                        if (item.customDescriptionFormatter)
                        {
                            long score = 1;
                            List<int> matches = new List<int>();
                            var sq = CleanString(context.searchQuery.ToLowerInvariant());
                            if (FuzzySearch.FuzzyMatch(sq, CleanString(item.label), ref score, matches))
                                item.label = RichTextFormatter.FormatSuggestionTitle(item.label, matches, FuzzySearch.HighlightColorTag, FuzzySearch.HighlightColorTagSpecial);
                        }

                        return item.label;
                    };

                    fetchDescription = (item, context) =>
                    {
                        var go = ObjectFromItem(item);

                        item.description = GetHierarchyPath(go) + go.transform.GetPath() + " (" + item.score + ")";

                        const int maxCharCount = 105;
                        if (item.description.Length > maxCharCount)
                        {
                            item.description = item.description.Replace("<b>", "").Replace("</b>", "");
                            int cutPos = item.description.Length - maxCharCount;
                            item.description = "..." + item.description.Substring(cutPos);
                        }

                        if (item.customDescriptionFormatter)
                        {
                            long score = 1;
                            List<int> matches = new List<int>();
                            var sq = CleanString(context.searchQuery.ToLowerInvariant());
                            if (FuzzySearch.FuzzyMatch(sq, CleanString(item.description), ref score, matches))
                                item.description = RichTextFormatter.FormatSuggestionTitle(item.description, matches, FuzzySearch.HighlightColorTag, FuzzySearch.HighlightColorTagSpecial);
                        }
                        return item.description;
                    };

                    fetchThumbnail = (item, context) =>
                    {
                        if (item.thumbnail)
                            return item.thumbnail;

                        var obj = ObjectFromItem(item);
                        if (obj != null)
                        {
                            var assetPath = GetHierarchyAssetPath(obj, true);
                            if (!String.IsNullOrEmpty(assetPath))
                            {
                                item.thumbnail = AssetPreview.GetAssetPreview(obj);
                                if (item.thumbnail)
                                    return item.thumbnail;
                                item.thumbnail = Utils.GetAssetThumbnailFromPath(assetPath, true);
                                if (item.thumbnail)
                                    return item.thumbnail;
                            }

                            item.thumbnail = PrefabUtility.GetIconForGameObject(obj);
                            if (item.thumbnail)
                                return item.thumbnail;
                            item.thumbnail = EditorGUIUtility.ObjectContent(obj, obj.GetType()).image as Texture2D;
                        }

                        return item.thumbnail;
                    };

                    startDrag = (item, context) =>
                    {
                        var obj = ObjectFromItem(item);
                        if (obj != null)
                        {
                            DragAndDrop.PrepareStartDrag();
                            DragAndDrop.objectReferences = new[] { obj };
                            DragAndDrop.StartDrag("Drag scene object");
                        }
                    };

                    trackSelection = (item, context) => PingItem(item);
                }
            }

            private static string CleanString(string s)
            {
                return s.Replace('_', ' ')
                        .Replace('.', ' ')
                        .Replace('-', ' ');
            }

            private static UnityEngine.Object PingItem(SearchItem item)
            {
                var obj = ObjectFromItem(item);
                if (obj == null)
                    return null;
                EditorGUIUtility.PingObject(obj);
                return obj;
            }

            internal static string type = "scene";
            internal static string displayName = "Scene";
            [UsedImplicitly, SearchItemProvider]
            internal static SearchProvider CreateProvider()
            {
                return new SceneSearchProvider(type, displayName);
            }

            [UsedImplicitly, SearchActionsProvider]
            internal static IEnumerable<SearchAction> ActionHandlers()
            {
                return new SearchAction[]
                {
                    new SearchAction(type, "select", null, "Select object in scene...")
                    {
                        handler = (item, context) =>
                        {
                            var pingedObject = PingItem(item);
                            if (pingedObject != null)
                                FrameObject(pingedObject);
                        }
                    },

                    new SearchAction(type, "open", null, "Open containing asset...")
                    {
                        handler = (item, context) =>
                        {
                            var pingedObject = PingItem(item);
                            if (pingedObject != null)
                            {
                                var go = pingedObject as GameObject;
                                var assetPath = GetHierarchyAssetPath(go);
                                if (!String.IsNullOrEmpty(assetPath))
                                    Utils.FrameAssetFromPath(assetPath);
                                else
                                    FrameObject(go);
                            }
                        }
                    }
                };
            }

            private static void FrameObject(object obj)
            {
                Selection.activeGameObject = obj as GameObject ?? Selection.activeGameObject;
                SceneView.lastActiveSceneView.FrameSelected();
            }

            private static GameObject ObjectFromItem(SearchItem item)
            {
                var instanceID = Convert.ToInt32(item.id);
                var obj = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
                return obj;
            }

            public static string GetPath(this Transform tform)
            {
                if (tform.parent == null)
                    return "/" + tform.name;
                return tform.parent.GetPath() + "/" + tform.name;
            }

            private static readonly Stack<StringBuilder> _SbPool = new Stack<StringBuilder>();
            public static string GetHierarchyPath(GameObject gameObject, bool includeScene = true, [CanBeNull] Component component = null)
            {
                if (gameObject == null)
                    return String.Empty;

                StringBuilder sb;
                if (_SbPool.Count > 0)
                {
                    sb = _SbPool.Pop();
                    sb.Clear();
                }
                else
                {
                    sb = new StringBuilder(200);
                }

                try
                {

                    bool isPrefab;
                    #if UNITY_2018_3_OR_NEWER
                    isPrefab = UnityEditor.PrefabUtility.GetPrefabAssetType(gameObject.gameObject) != UnityEditor.PrefabAssetType.NotAPrefab;
                    #else
                    isPrefab = UnityEditor.PrefabUtility.GetPrefabType(o) == UnityEditor.PrefabType.Prefab;
                    #endif

                    var assetPath = string.Empty;
                    if (isPrefab)
                    {
                        #if UNITY_2018_3_OR_NEWER
                        assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
                        #else
                        assetPath = AssetDatabase.GetAssetPath(gameObject);
                        #endif
                        sb.Append("<b>" + assetPath + "</b>");
                    }
                    else
                    {
                        if (includeScene)
                        {
                            var sceneName = gameObject.scene.name;
                            if (sceneName == string.Empty)
                            {
                                #if UNITY_2018_3_OR_NEWER
                                var prefabStage = PrefabStageUtility.GetPrefabStage(gameObject);
                                if (prefabStage != null)
                                {
                                    sceneName = "Prefab Stage";
                                }
                                else
                                #endif
                                {
                                    sceneName = "Unsaved Scene";
                                }
                            }

                            sb.Append("<b>" + sceneName + "</b>");
                        }
                    }

                    var path = sb.ToString();
                    sb.Clear();
                    return path;
                }
                finally
                {
                    _SbPool.Push(sb);
                }

            }

            public static string GetHierarchyAssetPath(GameObject gameObject, bool prefabOnly = false)
            {
                if (gameObject == null)
                    return String.Empty;

                bool isPrefab;
                #if UNITY_2018_3_OR_NEWER
                isPrefab = UnityEditor.PrefabUtility.GetPrefabAssetType(gameObject.gameObject) != UnityEditor.PrefabAssetType.NotAPrefab;
                #else
                isPrefab = UnityEditor.PrefabUtility.GetPrefabType(o) == UnityEditor.PrefabType.Prefab;
                #endif

                var assetPath = string.Empty;
                if (isPrefab)
                {
                    #if UNITY_2018_3_OR_NEWER
                    assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
                    #else
                    assetPath = AssetDatabase.GetAssetPath(gameObject);
                    #endif
                    return assetPath;
                }

                if (prefabOnly)
                    return null;

                return gameObject.scene.path;
            }

            #if UNITY_2019_1_OR_NEWER
            [UsedImplicitly, Shortcut("Help/Quick Search/Scene")]
            public static void OpenQuickSearch()
            {
                QuickSearchTool.OpenWithContextualProvider(type);
            }
            #endif
        }
    }
}

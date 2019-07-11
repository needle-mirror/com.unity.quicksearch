//#define QUICKSEARCH_DEBUG

using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Experimental.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.QuickSearch.Providers
{
    [UsedImplicitly]
    class SceneObjectsProvider : SearchProvider
    {
        const string type = "scene";
        const string displayName = "Scene";

        const int k_LODDetail1 = 50000;
        const int k_LODDetail2 = 100000;
        const int k_LimitMatches = 1000;

        private static readonly Stack<StringBuilder> _SbPool = new Stack<StringBuilder>();

        struct GOD
        {
            public string name;
            public string rawname;
            public GameObject gameObject;
        }

        class SceneSearchIndexer : SearchIndexer
        {
            private const int k_MinIndexCharVariation = 2;
            private const int k_MaxIndexCharVariation = 8;

            private GOD[] gods { get; set; }

            public SceneSearchIndexer(string sceneName, GOD[] gods)
                : base(new []{new Root("", sceneName) })
            {
                this.gods = gods;
                minIndexCharVariation = k_MinIndexCharVariation;
                maxIndexCharVariation = k_MaxIndexCharVariation;
                skipEntryHandler = e => false;
                getEntryComponentsHandler = (e, i) => SplitComponents(e, entrySeparators, k_MaxIndexCharVariation);
                enumerateRootEntriesHandler = EnumerateSceneObjects;
            }

            private IEnumerable<string> EnumerateSceneObjects(Root root)
            {
                return gods.Select(god => god.rawname);
            }

            private static IEnumerable<string> SplitComponents(string path, char[] entrySeparators, int maxIndexCharVariation)
            {
                var nameTokens = path.Split(entrySeparators).Reverse().ToArray();
                var scc = nameTokens.SelectMany(s => SearchUtils.SplitCamelCase(s)).Where(s => s.Length > 0);
                return nameTokens.Concat(scc)
                          .Select(s => s.Substring(0, Math.Min(s.Length, maxIndexCharVariation)).ToLowerInvariant())
                          .Distinct();
            }
        }

        private GOD[] gods { get; set; }
        private SceneSearchIndexer indexer { get; set; }
        private Dictionary<int, string> componentsById = new Dictionary<int, string>();

        public SceneObjectsProvider(string providerId, string displayName = null)
            : base(providerId, displayName)
        {
            priority = 50;
            filterId = "h:";

            subCategories = new List<NameId>
            {
                new NameId("fuzzy", "fuzzy"),
                new NameId("limit", $"limit to {k_LimitMatches} matches")
            };

            isEnabledForContextualSearch = () =>
                QuickSearchTool.IsFocusedWindowTypeName("SceneView") ||
                QuickSearchTool.IsFocusedWindowTypeName("SceneHierarchyWindow");

            EditorApplication.hierarchyChanged += () => componentsById.Clear();

            onEnable = () =>
            {
                //using (new DebugTimer("Building Scene Object Description"))
                {
                    var objects = new GameObject[0];
                    var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                    if (prefabStage != null)
                    {
                        objects = SceneModeUtility.GetObjects(new[] { prefabStage.prefabContentsRoot }, true);
                    }
                    else
                    {
                        var goRoots = new List<UnityEngine.Object>();
                        for (int i = 0; i < SceneManager.sceneCount; ++i)
                        {
                            var scene = SceneManager.GetSceneAt(i);
                            if (!scene.IsValid() || !scene.isLoaded)
                                continue;
                            var sceneRootObjects = scene.GetRootGameObjects();
                            if (sceneRootObjects != null && sceneRootObjects.Length > 0)
                                goRoots.AddRange(sceneRootObjects);
                        }
                        objects = SceneModeUtility.GetObjects(goRoots.ToArray(), true);
                    }

                    //using (new DebugTimer($"Fetching {gods.Length} Scene Objects Components"))
                    {
                        var createNewIndex = componentsById.Count == 0;
                        if (createNewIndex)
                            indexer = null;

                        gods = new GOD[objects.Length];
                        for (int i = 0; i < objects.Length; ++i)
                        {
                            gods[i].gameObject = objects[i];
                            var id = gods[i].gameObject.GetInstanceID();
                            if (!componentsById.TryGetValue(id, out gods[i].name))
                            {
                                if (gods.Length > k_LODDetail2)
                                    gods[i].rawname = gods[i].gameObject.name;
                                else if (gods.Length > k_LODDetail1)
                                    gods[i].rawname = GetTransformPath(gods[i].gameObject.transform);
                                else
                                    gods[i].rawname = BuildComponents(gods[i].gameObject);
                                gods[i].name = CleanString(gods[i].rawname);
                                componentsById[id] = gods[i].name;
                            }
                        }

                        if (indexer == null)
                        {
                            indexer = new SceneSearchIndexer(SceneManager.GetActiveScene().name, gods);
                            indexer.Build();
                        }
                    }
                }
            };

            onDisable = () =>
            {
                gods = null;
            };

            fetchItems = (context, items, provider) =>
            {
                if (gods == null)
                    return null;

                if (indexer != null && indexer.IsReady())
                {
                    var results = indexer.Search(context.searchQuery).Take(201);
                    items.AddRange(results.Select(r =>
                    {
                        if (r.index < 0 || r.index >= gods.Length)
                            return provider.CreateItem("invalid");

                        var gameObjectId = gods[r.index].gameObject.GetInstanceID().ToString();
                        var gameObjectName = gods[r.index].gameObject.name;
                        var itemScore = r.score - 1000;
                        if (gameObjectName.Equals(context.searchQuery, StringComparison.InvariantCultureIgnoreCase))
                            itemScore *= 2;
                        var item = provider.CreateItem(gameObjectId, itemScore, null, null, null, r.index);
                        item.descriptionFormat = SearchItemDescriptionFormat.Ellipsis |
                                                 SearchItemDescriptionFormat.RightToLeft |
                                                 SearchItemDescriptionFormat.Highlight;
                        return item;
                    }));
                }

                return SearchGODs(context, provider);
            };

            fetchLabel = (item, context) =>
            {
                if (item.label != null)
                    return item.label;

                var go = ObjectFromItem(item);
                if (!go)
                    return item.id;

                var transformPath = GetTransformPath(go.transform);
                var components = go.GetComponents<Component>();
                if (components.Length > 2 && components[1] && components[components.Length-1])
                    item.label = $"{transformPath} ({components[1].GetType().Name}..{components[components.Length-1].GetType().Name})";
                else if (components.Length > 1 && components[1])
                    item.label = $"{transformPath} ({components[1].GetType().Name})";
                else
                    item.label = $"{transformPath} ({item.id})";

                long score = 1;
                List<int> matches = new List<int>();
                var sq = CleanString(context.searchQuery);
                if (FuzzySearch.FuzzyMatch(sq, CleanString(item.label), ref score, matches))
                    item.label = RichTextFormatter.FormatSuggestionTitle(item.label, matches);

                return item.label;
            };

            fetchDescription = (item, context) =>
            {
                #if QUICKSEARCH_DEBUG
                item.description = gods[(int)item.data].name + " * " + item.score;
                #else
                var go = ObjectFromItem(item);
                item.description = GetHierarchyPath(go);
                #endif
                return item.description;
            };

            fetchThumbnail = (item, context) =>
            {
                if (item.thumbnail)
                    return item.thumbnail;

                var obj = ObjectFromItem(item);
                if (obj != null)
                {
                    if (SearchSettings.fetchPreview)
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

        private IEnumerable<SearchItem> SearchGODs(SearchContext context, SearchProvider provider)
        {
            int addedCount = 0;
            List<int> matches = new List<int>();
            var sq = CleanString(context.searchQuery);

            var limitSearch = context.categories.Any(c => c.name.id == "limit" && c.isEnabled);
            var useFuzzySearch = gods.Length < k_LODDetail2 && context.categories.Any(c => c.name.id == "fuzzy" && c.isEnabled);

            long bestScore = 1;
            for (int i = 0, end = gods.Length; i != end; ++i)
            {
                var go = gods[i].gameObject;
                if (!go)
                    continue;

                long score = 1;
                if (useFuzzySearch)
                {
                    if (!FuzzySearch.FuzzyMatch(sq, gods[i].name, ref score, matches))
                    {
                        yield return null;
                        continue;
                    }

                    if (score > bestScore)
                        bestScore = score;
                    if (limitSearch && addedCount > (k_LimitMatches / 2))
                        useFuzzySearch = addedCount < ((k_LimitMatches * 7) / 8);
                }
                else
                {
                    if (!MatchSearchGroups(context, gods[i].name, true))
                    {
                        yield return null;
                        continue;
                    }
                    score = bestScore + 1;
                }

                var gameObjectId = go.GetInstanceID().ToString();
                var item = provider.CreateItem(gameObjectId, ~(int)score, null, null, null, i);
                item.descriptionFormat = SearchItemDescriptionFormat.Ellipsis | SearchItemDescriptionFormat.RightToLeft;
                if (useFuzzySearch)
                    item.descriptionFormat |= SearchItemDescriptionFormat.FuzzyHighlight;
                else
                    item.descriptionFormat |= SearchItemDescriptionFormat.Highlight;
                yield return item;
                if (limitSearch && ++addedCount > k_LimitMatches)
                    yield break;
            }
        }

        private static string CleanString(string s)
        {
            var sb = s.ToCharArray();
            for (int c = 0; c < s.Length; ++c)
            {
                var ch = s[c];
                if (ch == '_' || ch == '.' || ch == '-' || ch == '/')
                    sb[c] = ' ';
            }
            return new string(sb).ToLowerInvariant();
        }

        private static UnityEngine.Object PingItem(SearchItem item)
        {
            var obj = ObjectFromItem(item);
            if (obj == null)
                return null;
            EditorGUIUtility.PingObject(obj);
            return obj;
        }

        private static void FrameObject(object obj)
        {
            Selection.activeGameObject = obj as GameObject ?? Selection.activeGameObject;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.FrameSelected();
        }

        private static GameObject ObjectFromItem(SearchItem item)
        {
            var instanceID = Convert.ToInt32(item.id);
            var obj = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
            return obj;
        }

        private static string GetTransformPath(Transform tform)
        {
            if (tform.parent == null)
                return "/" + tform.name;
            return GetTransformPath(tform.parent) + "/" + tform.name;
        }

        private static string BuildComponents(GameObject go)
        {
            var components = new List<string>();
            var tform = go.transform;
            while (tform != null)
            {
                components.Insert(0, tform.name);
                tform = tform.parent;
            }

            components.Insert(0, go.scene.name);

            var gocs = go.GetComponents<Component>();
            for (int i = 1; i < gocs.Length; ++i)
            {
                var c = gocs[i];
                if (!c || c.hideFlags == HideFlags.HideInInspector)
                    continue;
                components.Add(c.GetType().Name);
            }

            return String.Join(" ", components.Distinct());
        }

        private static string GetHierarchyPath(GameObject gameObject, bool includeScene = true)
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
                isPrefab = PrefabUtility.GetPrefabAssetType(gameObject.gameObject) != PrefabAssetType.NotAPrefab;
                #else
                isPrefab = UnityEditor.PrefabUtility.GetPrefabType(o) == UnityEditor.PrefabType.Prefab;
                #endif

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

                sb.Append(GetTransformPath(gameObject.transform));

                var assetPath = string.Empty;
                if (isPrefab)
                {
                    #if UNITY_2018_3_OR_NEWER
                    assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
                    #else
                    assetPath = AssetDatabase.GetAssetPath(gameObject);
                    #endif
                    sb.Append(" (" + System.IO.Path.GetFileName(assetPath) + ")");
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

        private static string GetHierarchyAssetPath(GameObject gameObject, bool prefabOnly = false)
        {
            if (gameObject == null)
                return String.Empty;

            bool isPrefab;
            #if UNITY_2018_3_OR_NEWER
            isPrefab = PrefabUtility.GetPrefabAssetType(gameObject.gameObject) != PrefabAssetType.NotAPrefab;
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

        [UsedImplicitly, SearchItemProvider]
        internal static SearchProvider CreateProvider()
        {
            return new SceneObjectsProvider(type, displayName);
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

        #if UNITY_2019_1_OR_NEWER
        [UsedImplicitly, Shortcut("Help/Quick Search/Scene")]
        private static void OpenQuickSearch()
        {
            QuickSearchTool.OpenWithContextualProvider(type);
        }
        #endif
    }
}

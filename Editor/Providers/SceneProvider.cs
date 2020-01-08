//#define DEBUG_TIMING
using JetBrains.Annotations;
using System;
using System.Collections;
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
    public class SceneProvider : SearchProvider
    {
        protected Func<GameObject[]> fetchGameObjects { get; set; }
        protected Func<GameObject, string[]> buildKeywordComponents { get; set; }

        private SearchIndexer m_Indexer { get; set; }
        private GameObject[] m_GameObjects = null;
        private IEnumerator m_BuildIndexEnumerator = null;
        protected bool m_HierarchyChanged = true;
        private static List<int> s_FuzzyMatches = new List<int>();
        private static readonly Stack<StringBuilder> _SbPool = new Stack<StringBuilder>();

        public SceneProvider(string providerId, string filterId, string displayName)
            : base(providerId, displayName)
        {
            priority = 50;
            this.filterId = filterId;
            this.showDetails = true;

            subCategories = new List<NameEntry>
            {
                new NameEntry("fuzzy", "fuzzy"),
                new NameEntry("components", "components (c:)")
            };

            isEnabledForContextualSearch = () =>
                QuickSearch.IsFocusedWindowTypeName("SceneView") ||
                QuickSearch.IsFocusedWindowTypeName("SceneHierarchyWindow");

            EditorApplication.hierarchyChanged += () => m_HierarchyChanged = true;

            onEnable = () =>
            {
                if (m_HierarchyChanged)
                {
                    m_BuildIndexEnumerator = null;
                    m_HierarchyChanged = false;
                }
            };

            onDisable = () =>
            {
                // Only track changes that occurs when Quick Search is not active.
                m_HierarchyChanged = false;
            };

            fetchItems = (context, items, provider) => SearchItems(context, provider);

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
                var go = ObjectFromItem(item);
                return (item.description = GetHierarchyPath(go));
            };

            fetchThumbnail = (item, context) =>
            {
                var obj = ObjectFromItem(item);
                if (obj == null)
                    return null;

                return (item.thumbnail = Utils.GetThumbnailForGameObject(obj));
            };

            fetchPreview = (item, context, size, options) =>
            {
                var obj = ObjectFromItem(item);
                if (obj == null)
                    return item.thumbnail;

                var assetPath = GetHierarchyAssetPath(obj, true);
                if (String.IsNullOrEmpty(assetPath))
                    return item.thumbnail;
                return AssetPreview.GetAssetPreview(obj) ?? Utils.GetAssetPreviewFromPath(assetPath, size, options);
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

            fetchGameObjects = FetchGameObjects;
            buildKeywordComponents = o => null;

            trackSelection = (item, context) => PingItem(item);
        }

        private IEnumerator SearchFuzzy(SearchContext context, SearchProvider provider)
        {
            if (context.searchWords.Length > 0 && context.searchPhrase.Length > 2)
            {
                #if DEBUG_TIMING
                using (new DebugTimer($"Fuzzy search ({context.searchPhrase})"))
                #endif
                {
                    var useFuzzySearch = context.IsFilterEnabled("fuzzy");
                    foreach (var o in m_GameObjects)
                        yield return MatchItem(context, provider, o.GetInstanceID().ToString(), o.name, useFuzzySearch);
                }
            }
        }

        private IEnumerator SearchItems(SearchContext context, SearchProvider provider)
        {
            #if DEBUG_TIMING
            using (new DebugTimer($"Search scene ({context.searchQuery})"))
            #endif
            {
                if (m_BuildIndexEnumerator == null)
                {
                    m_GameObjects = fetchGameObjects();
                    m_Indexer = new SearchIndexer("scene");
                    m_BuildIndexEnumerator = BuildIndex(context, provider, m_GameObjects, m_Indexer);
                }

                yield return m_BuildIndexEnumerator;
                yield return SearchFuzzy(context, provider);

                // Indicate that we are still building the scene index.
                while (!m_Indexer.IsReady())
                    yield return null;

                yield return SearchIndex(context, provider, m_Indexer);
            }
        }

        private IEnumerable<SearchItem> SearchIndex(SearchContext context, SearchProvider provider, SearchIndexer indexer)
        {
            #if DEBUG_TIMING
            using (new DebugTimer($"Search index ({context.searchQuery})"))
            #endif
            {
                return indexer.SearchTerms(context.searchQuery).Select(r =>
                {
                    var gameObjectId = Convert.ToInt32(r.path);
                    var gameObject = EditorUtility.InstanceIDToObject(gameObjectId) as GameObject;
                    if (!gameObject)
                        return null;
                    return AddResult(provider, r.path, r.score, false);
                });
            }
        }

        protected GameObject[] FetchGameObjects()
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
                return SceneModeUtility.GetObjects(new[] { prefabStage.prefabContentsRoot }, true);
            
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

            return SceneModeUtility.GetObjects(goRoots.ToArray(), true)
                .Where(o => !o.hideFlags.HasFlag(HideFlags.HideInHierarchy)).ToArray();
        }

        private static IEnumerable<string> SplitWords(string path, char[] entrySeparators, int maxIndexCharVariation)
        {
            var nameTokens = path.Split(entrySeparators).Reverse().ToArray();
            var scc = nameTokens.SelectMany(s => SearchUtils.SplitCamelCase(s)).Where(s => s.Length > 0);
            return nameTokens.Concat(scc)
                      .Select(s => s.Substring(0, Math.Min(s.Length, maxIndexCharVariation)).ToLowerInvariant())
                      .Distinct();
        }

        private IEnumerator BuildIndex(SearchContext context, SearchProvider provider, GameObject[] objects, SearchIndexer indexer)
        {
            #if DEBUG_TIMING
            using (new DebugTimer("Build scene index"))
            #endif
            {
                var useFuzzySearch = context.IsFilterEnabled("fuzzy");
                var indexComponents = context.IsFilterEnabled("components");

                indexer.Start();
                for (int i = 0; i < objects.Length; ++i)
                {
                    var gameObject = objects[i];
                    var id = objects[i].GetInstanceID();
                    var name = gameObject.name;
                    var path = GetTransformPath(gameObject.transform);
                    var keywords = buildKeywordComponents(objects[i]);

                    var documentId = id.ToString();
                    int docIndex = indexer.AddDocument(documentId, false);

                    int scoreIndex = 1;
                    foreach (var word in SearchUtils.SplitEntryComponents(name, indexer.entrySeparators, 2, 12))
                        indexer.AddWord(word, scoreIndex++, docIndex);
                    foreach (var word in SplitWords(path, indexer.entrySeparators, 8))
                        indexer.AddWord(word, scoreIndex++, docIndex);

                    name = name.ToLowerInvariant();
                    indexer.AddWord(name, name.Length, 0, docIndex);
                    indexer.AddExactWord(name.ToLowerInvariant(), 0, docIndex);

                    var ptype = PrefabUtility.GetPrefabAssetType(gameObject);
                    var pstatus = PrefabUtility.GetPrefabInstanceStatus(gameObject);

                    if (ptype != PrefabAssetType.NotAPrefab)
                        indexer.AddProperty("prefab", ptype.ToString().ToLowerInvariant(), 30, docIndex);
                    if (pstatus != PrefabInstanceStatus.NotAPrefab)
                        indexer.AddProperty("prefab", pstatus.ToString().ToLowerInvariant(), 30, docIndex);

                    if (keywords != null)
                    {
                        foreach (var keyword in keywords)
                            foreach (var word in SplitWords(keyword, indexer.entrySeparators, 8))
                                indexer.AddWord(word, scoreIndex++, docIndex);
                    }

                    if (indexComponents)
                    {
                        var gocs = gameObject.GetComponents<Component>();
                        for (int componentIndex = 1; componentIndex < gocs.Length; ++componentIndex)
                        {
                            var c = gocs[componentIndex];
                            if (!c || c.hideFlags == HideFlags.HideInInspector)
                                continue;

                            indexer.AddProperty("c", c.GetType().Name.ToLowerInvariant(), 2, docIndex);
                        }
                    }

                    // While we are building the scene, lets search for objects name
                    yield return MatchItem(context, provider, documentId, name, useFuzzySearch);
                }

                indexer.Finish(true);
            }
        }

        private static SearchItem MatchItem(SearchContext context, SearchProvider provider, string id, string name, bool useFuzzySearch)
        {
            if (context.searchPhrase.Length > 2)
            {
                long score = 99;
                bool foundMatch = !useFuzzySearch
                    ? MatchSearchGroups(context, name, false)
                    : FuzzySearch.FuzzyMatch(context.searchPhrase, name, ref score, s_FuzzyMatches);
                if (foundMatch)
                    return AddResult(provider, id, (~(int)score) + 10000, useFuzzySearch);
            }

            return null;
        }

        private static SearchItem AddResult(SearchProvider provider, string id, int score, bool useFuzzySearch)
        {
            string description = null;
            #if false
            description = $"F:{useFuzzySearch} {id} ({score})";
            #endif
            var item = provider.CreateItem(id, score, null, description, null, null);
            return SetItemDescriptionFormat(item, useFuzzySearch);
        }

        private static SearchItem SetItemDescriptionFormat(SearchItem item, bool useFuzzySearch)
        {
            item.descriptionFormat = SearchItemDescriptionFormat.Ellipsis
                | SearchItemDescriptionFormat.RightToLeft
                | (useFuzzySearch ? SearchItemDescriptionFormat.FuzzyHighlight : SearchItemDescriptionFormat.Highlight);
            return item;
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

        public static string GetHierarchyPath(GameObject gameObject, bool includeScene = true)
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

                #if false
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
                    sb.Append(" (" + System.IO.Path.GetFileName(assetPath) + ")");
                }
                #endif

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

        public static IEnumerable<SearchAction> CreateActionHandlers(string providerId)
        {
            return new SearchAction[]
            {
                new SearchAction(providerId, "select", null, "Select object in scene...")
                {
                    handler = (item, context) =>
                    {
                        var pingedObject = PingItem(item);
                        if (pingedObject != null)
                            FrameObject(pingedObject);
                    }
                },

                new SearchAction(providerId, "open", null, "Open containing asset...")
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
    }

    static class BuiltInSceneObjectsProvider
    {
        const string k_DefaultProviderId = "scene";

        [UsedImplicitly, SearchItemProvider]
        internal static SearchProvider CreateProvider()
        {
            return new SceneProvider(k_DefaultProviderId, "h:", "Scene");
        }

        [UsedImplicitly, SearchActionsProvider]
        internal static IEnumerable<SearchAction> ActionHandlers()
        {
            return SceneProvider.CreateActionHandlers(k_DefaultProviderId);
        }

        #if UNITY_2019_1_OR_NEWER
        [UsedImplicitly, Shortcut("Help/Quick Search/Scene")]
        private static void OpenQuickSearch()
        {
            QuickSearch.OpenWithContextualProvider(k_DefaultProviderId);
        }
        #endif
    }
}

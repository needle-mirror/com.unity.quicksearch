//#define DEBUG_TIMING
using JetBrains.Annotations;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Unity.QuickSearch.Providers
{
    [UsedImplicitly]
    public class SceneProvider : SearchProvider
    {
        protected Func<GameObject[]> fetchGameObjects { get; set; }
        protected Func<GameObject, string[]> buildKeywordComponents { get; set; }
        protected bool m_HierarchyChanged = true;

        private GameObject[] m_GameObjects = null;
        private SceneQueryEngine m_SceneQueryEngine { get; set; }

        public SceneProvider(string providerId, string filterId, string displayName)
            : base(providerId, displayName)
        {
            priority = 50;
            this.filterId = filterId;
            this.showDetails = true;
            showDetailsOptions = ShowDetailsOptions.Inspector | ShowDetailsOptions.Actions;

            isEnabledForContextualSearch = () =>
                Utils.IsFocusedWindowTypeName("SceneView") ||
                Utils.IsFocusedWindowTypeName("SceneHierarchyWindow");

            EditorApplication.hierarchyChanged += () => m_HierarchyChanged = true;

            onEnable = () =>
            {
                if (m_HierarchyChanged)
                {
                    m_GameObjects = fetchGameObjects();
                    m_SceneQueryEngine = new SceneQueryEngine(m_GameObjects);
                    m_HierarchyChanged = false;
                }
            };

            onDisable = () =>
            {
                // Only track changes that occurs when Quick Search is not active.
                m_HierarchyChanged = false;
            };

            toObject = (item, type) => ObjectFromItem(item);

            fetchItems = (context, items, provider) => SearchItems(context, provider);

            fetchLabel = (item, context) =>
            {
                if (item.label != null)
                    return item.label;

                var go = ObjectFromItem(item);
                if (!go)
                    return item.id;

                if (context.searchView == null || context.searchView.displayMode == DisplayMode.List)
                {
                    var transformPath = SearchUtils.GetTransformPath(go.transform);
                    var components = go.GetComponents<Component>();
                    if (components.Length > 2 && components[1] && components[components.Length - 1])
                        item.label = $"{transformPath} ({components[1].GetType().Name}..{components[components.Length - 1].GetType().Name})";
                    else if (components.Length > 1 && components[1])
                        item.label = $"{transformPath} ({components[1].GetType().Name})";
                    else
                        item.label = $"{transformPath} ({item.id})";

                    long score = 1;
                    List<int> matches = new List<int>();
                    var sq = Utils.CleanString(context.searchQuery);
                    if (FuzzySearch.FuzzyMatch(sq, Utils.CleanString(item.label), ref score, matches))
                        item.label = RichTextFormatter.FormatSuggestionTitle(item.label, matches);
                }
                else
                {
                    item.label = go.name;
                }

                return item.label;
            };

            fetchDescription = (item, context) =>
            {
                var go = ObjectFromItem(item);
                return (item.description = SearchUtils.GetHierarchyPath(go));
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
                return Utils.GetSceneObjectPreview(obj, size, options, item.thumbnail);
            };

            startDrag = (item, context) =>
            {
                Utils.StartDrag(context.selection.Select(i => ObjectFromItem(i)).ToArray(), item.GetLabel(context, true));
            };

            trackSelection = (item, context) => PingItem(item);

            fetchGameObjects = SceneQueryEngine.FetchGameObjects;
            buildKeywordComponents = SceneQueryEngine.BuildKeywordComponents;
        }

        private IEnumerator SearchItems(SearchContext context, SearchProvider provider)
        {
            #if DEBUG_TIMING
            using (new DebugTimer($"Search scene ({context.searchQuery})"))
            #endif
            {
                if (!String.IsNullOrEmpty(context.searchQuery))
                {
                    yield return m_SceneQueryEngine.Search(context.searchQuery).Select(gameObject =>
                    {
                        if (!gameObject)
                            return null;
                        return AddResult(provider, gameObject.GetInstanceID().ToString(), 0, false);
                    });
                }

                if (context.wantsMore && context.filterType != null && String.IsNullOrEmpty(context.searchQuery))
                {
                    yield return GameObject.FindObjectsOfType(context.filterType)
                        .Select(go => AddResult(provider, go.GetInstanceID().ToString(), 999, false));
                }
            }
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

        private static void FrameObjects(UnityEngine.Object[] objects)
        {
            Selection.instanceIDs = objects.Select(o => o.GetInstanceID()).ToArray();
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.FrameSelected();
        }

        private static GameObject ObjectFromItem(SearchItem item)
        {
            var instanceID = Convert.ToInt32(item.id);
            var obj = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
            return obj;
        }

        public static IEnumerable<SearchAction> CreateActionHandlers(string providerId)
        {
            return new SearchAction[]
            {
                new SearchAction(providerId, "select", null, "Select object(s) in scene...")
                {
                    execute = (context, items) =>
                    {
                        FrameObjects(items.Select(i => i.provider.toObject(i, typeof(GameObject))).Where(i=>i).ToArray());
                    }
                },

                new SearchAction(providerId, "open", null, "Select containing asset...")
                {
                    handler = (item, context) =>
                    {
                        var pingedObject = PingItem(item);
                        if (pingedObject != null)
                        {
                            var go = pingedObject as GameObject;
                            var assetPath = SearchUtils.GetHierarchyAssetPath(go);
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

        [UsedImplicitly, Shortcut("Help/Quick Search/Scene")]
        private static void OpenQuickSearch()
        {
            QuickSearch.OpenWithContextualProvider(k_DefaultProviderId);
        }
    }
}

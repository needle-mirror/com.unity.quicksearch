//#define DEBUG_TIMING
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.QuickSearch.Providers
{
    [UsedImplicitly]
    public class SceneQueryEngine
    {
        private readonly GameObject[] m_GameObjects;
        private Dictionary<int, GOD> m_GODS = new Dictionary<int, GOD>();
        private readonly QueryEngine<GameObject> m_QueryEngine = new QueryEngine<GameObject>(true);

        public static readonly string[] none = new string[0];
        public static readonly char[] entrySeparators = { '/', ' ', '_', '-', '.' };

        public Func<GameObject, string[]> buildKeywordComponents { get; set; }

        class GOD
        {
            public string id;
            public string path;
            public string tag;
            public string[] types;
            public string[] words;

            public int? layer;
            public float size = float.MaxValue;

            public bool? isChild;
            public bool? isLeaf;
        }

        public SceneQueryEngine(GameObject[] gameObjects)
        {
            m_GameObjects = gameObjects;
            m_QueryEngine.AddFilter("id", GetId);
            m_QueryEngine.AddFilter("path", GetPath);
            m_QueryEngine.AddFilter("tag", GetTag);
            m_QueryEngine.AddFilter("layer", GetLayer);
            m_QueryEngine.AddFilter("size", GetSize);
            m_QueryEngine.AddFilter<string>("is", OnIsFilter, new []{":"});
            m_QueryEngine.AddFilter<string>("t", OnTypeFilter, new []{"=", ":"});
            m_QueryEngine.SetSearchDataCallback(OnSearchData);
            m_QueryEngine.SetGlobalStringComparisonOptions(StringComparison.Ordinal);
        }

        public IEnumerable<GameObject> Search(string searchQuery)
        {
            var query = m_QueryEngine.Parse(searchQuery);
            if (!query.valid)
                return Enumerable.Empty<GameObject>();
            return query.Apply(m_GameObjects);//.ToList();
        }

        public static GameObject[] FetchGameObjects()
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

        public static string[] BuildKeywordComponents(GameObject go)
        {
            return null;
        }
        
        public string GetId(GameObject go)
        {
            var god = GetGOD(go);

            if (god.id == null)
                god.id = go.GetInstanceID().ToString();

            return god.id;
        }

        public string GetPath(GameObject go)
        {
            var god = GetGOD(go);

            if (god.path == null)
                god.path = SearchUtils.GetTransformPath(go.transform).ToLowerInvariant();

            return god.path;
        }

        public string GetTag(GameObject go)
        {
            var god = GetGOD(go);

            if (god.tag == null)
                god.tag = go.tag.ToLowerInvariant();

            return god.tag;
        }

        public int GetLayer(GameObject go)
        {
            var god = GetGOD(go);

            if (!god.layer.HasValue)
                god.layer = go.layer;

            return god.layer.Value;
        }

        public float GetSize(GameObject go)
        {
            var god = GetGOD(go);

            if (god.size == float.MaxValue)
            {
                if (go.TryGetComponent<Collider>(out var collider))
                    god.size = collider.bounds.size.magnitude;
                else if (go.TryGetComponent<Renderer>(out var renderer))
                    god.size = renderer.bounds.size.magnitude;
                else
                    god.size = 0;
            }

            return god.size;
        }

        GOD GetGOD(GameObject go)
        {
            var instanceId = go.GetInstanceID();
            if (!m_GODS.TryGetValue(instanceId, out var god))
            {
                god = new GOD();
                m_GODS[instanceId] = god;
            }
            return god;
        }

        bool OnIsFilter(GameObject go, string op, string value)
        {
            var god = GetGOD(go);

            if (value == "child")
            {
                if (!god.isChild.HasValue)
                    god.isChild = go.transform.root != go.transform;
                return god.isChild.Value;
            }
            else if (value == "leaf")
            {
                if (!god.isLeaf.HasValue)
                    god.isLeaf = go.transform.childCount == 0;
                return god.isLeaf.Value;
            }
            else if (value == "root")
            {
                return go.transform.root == go.transform;
            }
            else if (value == "visible")
            {
                return IsInView(go, SceneView.GetAllSceneCameras().FirstOrDefault());
            }
            else if (value == "hidden")
            {
                return SceneVisibilityManager.instance.IsHidden(go);
            }

            return false;
        }

        bool OnTypeFilter(GameObject go, string op, string value)
        {
            var god = GetGOD(go);

            if (god.types == null)
            {
                var types = new List<string>();
                var ptype = PrefabUtility.GetPrefabAssetType(go);
                if (ptype != PrefabAssetType.NotAPrefab)
                    types.Add("prefab");

                var gocs = go.GetComponents<Component>();
                for (int componentIndex = 1; componentIndex < gocs.Length; ++componentIndex)
                {
                    var c = gocs[componentIndex];
                    if (!c || c.hideFlags.HasFlag(HideFlags.HideInInspector))
                        continue;

                    types.Add(c.GetType().Name.ToLowerInvariant());
                }

                god.types = types.ToArray();
            }
    
            if (op == "=")
                return god.types.Any(t => t.Equals(value.ToLowerInvariant(), StringComparison.Ordinal));
            return god.types.Any(t => t.IndexOf(value.ToLowerInvariant(), StringComparison.Ordinal) != -1);
        }

        IEnumerable<string> OnSearchData(GameObject go)
        {
            var god = GetGOD(go);

            if (god.words == null)
            {
                god.words = SplitWords(go.name, entrySeparators)
                    .Concat(buildKeywordComponents?.Invoke(go) ?? none)
                    .ToArray();
            }
            
            return god.words;
        }

        private static IEnumerable<string> SplitWords(string entry, char[] entrySeparators)
        {
            var nameTokens = CleanName(entry).Split(entrySeparators);
            var scc = nameTokens.SelectMany(s => SearchUtils.SplitCamelCase(s)).Where(s => s.Length > 0);
            var fcc = scc.Aggregate("", (current, s) => current + s[0]);
            return new[] { fcc, entry }.Concat(scc.Where(s => s.Length > 1))
                                .Where(s => s.Length > 0)
                                .Distinct()
                                .Select(w => w.ToLowerInvariant());
        }

        private static string CleanName(string s)
        {
            return s.Replace("(", "").Replace(")", "");
        }

        private bool IsInView(GameObject toCheck, Camera cam)
        {
            if (!cam || !toCheck)
                return false;

            var renderer = toCheck.GetComponentInChildren<Renderer>();
            if (!renderer)
                return false;

            Vector3 pointOnScreen = cam.WorldToScreenPoint(renderer.bounds.center);

            // Is in front
            if (pointOnScreen.z < 0)
                return false;

            // Is in FOV
            if ((pointOnScreen.x < 0) || (pointOnScreen.x > Screen.width) ||
                    (pointOnScreen.y < 0) || (pointOnScreen.y > Screen.height))
                return false;

            if (Physics.Linecast(cam.transform.position, renderer.bounds.center, out var hit))
            {
                if (hit.transform.GetInstanceID() != toCheck.GetInstanceID())
                    return false;
            }
            return true;
        }
    }
}

//#define QUICKSEARCH_DEBUG
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Experimental.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.QuickSearch.Providers
{
    public class SceneQueryEngineFilterAttribute : QueryEngineFilterAttribute
    {
        public SceneQueryEngineFilterAttribute(string token, string[] supportedOperators = null)
            : base(token, supportedOperators) { }

        public SceneQueryEngineFilterAttribute(string token, StringComparison options, string[] supportedOperators = null)
            : base(token, options, supportedOperators) { }

        public SceneQueryEngineFilterAttribute(string token, string paramTransformerFunction, string[] supportedOperators = null)
            : base(token, paramTransformerFunction, supportedOperators) { }

        public SceneQueryEngineFilterAttribute(string token, string paramTransformerFunction, StringComparison options, string[] supportedOperators = null)
            : base(token, paramTransformerFunction, options, supportedOperators) { }
    }

    public class SceneQueryEngineParameterTransformerAttribute : QueryEngineParameterTransformerAttribute { }

    [UsedImplicitly]
    public class SceneQueryEngine
    {
        private readonly GameObject[] m_GameObjects;
        private readonly Dictionary<int, GOD> m_GODS = new Dictionary<int, GOD>();
        private readonly QueryEngine<GameObject> m_QueryEngine = new QueryEngine<GameObject>(true);

        private static readonly string[] none = new string[0];
        private static readonly char[] entrySeparators = { '/', ' ', '_', '-', '.' };
        private static readonly Regex s_RangeRx = new Regex(@"\[(-?[\d\.]+)[,](-?[\d\.]+)\s*\]");

        public Func<GameObject, string[]> buildKeywordComponents { get; set; }

        class PropertyRange
        {
            public float min { get; private set; }
            public float max { get; private set; }

            public PropertyRange(float min, float max)
            {
                this.min = min;
                this.max = max;
            }

            public bool Contains(float f)
            {
                if (f >= min && f <= max)
                    return true;
                return false;
            }
        }

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

            public Dictionary<string, GOP> properties;
        }

        readonly struct GOP
        {
            public enum ValueType
            {
                Nil = 0,
                Bool,
                Number,
                Text
            }

            public readonly ValueType type;
            public readonly bool b;
            public readonly float number;
            public readonly string text;

            public bool valid => type != ValueType.Nil;

            public static GOP invalid = new GOP();

            public GOP(bool v)
            {
                this.type = ValueType.Bool;
                this.number = float.NaN;
                this.text = null;
                this.b = v;
            }

            public GOP(float number)
            {
                this.type = ValueType.Number;
                this.number = number;
                this.text = null;
                this.b = false;
            }

            public GOP(string text)
            {
                this.type = ValueType.Text;
                this.number = float.NaN;
                this.text = text;
                this.b = false;
            }
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

            m_QueryEngine.AddFilter("p", OnPropertyFilter, s => s, StringComparison.OrdinalIgnoreCase);

            m_QueryEngine.AddOperatorHandler(":", (GOP v, PropertyRange range) => PropertyRangeCompare(v, range, (f, r) => r.Contains(f)));
            m_QueryEngine.AddOperatorHandler("=", (GOP v, PropertyRange range) => PropertyRangeCompare(v, range, (f, r) => r.Contains(f)));
            m_QueryEngine.AddOperatorHandler("!=", (GOP v, PropertyRange range) => PropertyRangeCompare(v, range, (f, r) => !r.Contains(f)));
            m_QueryEngine.AddOperatorHandler("<=", (GOP v, PropertyRange range) => PropertyRangeCompare(v, range, (f, r) => f <= r.max));
            m_QueryEngine.AddOperatorHandler("<", (GOP v, PropertyRange range) => PropertyRangeCompare(v, range, (f, r) => f < r.min));
            m_QueryEngine.AddOperatorHandler(">", (GOP v, PropertyRange range) => PropertyRangeCompare(v, range, (f, r) => f > r.max));
            m_QueryEngine.AddOperatorHandler(">=", (GOP v, PropertyRange range) => PropertyRangeCompare(v, range, (f, r) => f >= r.min));

            m_QueryEngine.AddOperatorHandler(":", (GOP v, float number, StringComparison sc) => PropertyFloatCompare(v, number, (f, r) => StringContains(f, r, sc)));
            m_QueryEngine.AddOperatorHandler("=", (GOP v, float number) => PropertyFloatCompare(v, number, (f, r) => Math.Abs(f - r) < Mathf.Epsilon));
            m_QueryEngine.AddOperatorHandler("!=", (GOP v, float number) => PropertyFloatCompare(v, number, (f, r) => Math.Abs(f - r) >= Mathf.Epsilon));
            m_QueryEngine.AddOperatorHandler("<=", (GOP v, float number) => PropertyFloatCompare(v, number, (f, r) => f <= r));
            m_QueryEngine.AddOperatorHandler("<", (GOP v, float number) => PropertyFloatCompare(v, number, (f, r) => f < r));
            m_QueryEngine.AddOperatorHandler(">", (GOP v, float number) => PropertyFloatCompare(v, number, (f, r) => f > r));
            m_QueryEngine.AddOperatorHandler(">=", (GOP v, float number) => PropertyFloatCompare(v, number, (f, r) => f >= r));

            m_QueryEngine.AddOperatorHandler("=", (GOP v, bool b) => PropertyBoolCompare(v, b, (f, r) => f == r));
            m_QueryEngine.AddOperatorHandler("!=", (GOP v, bool b) => PropertyBoolCompare(v, b, (f, r) => f != r));

            m_QueryEngine.AddOperatorHandler(":", (GOP v, string s, StringComparison sc) => PropertyStringCompare(v, s, (f, r) => StringContains(f, r, sc)));
            m_QueryEngine.AddOperatorHandler("=", (GOP v, string s, StringComparison sc) => PropertyStringCompare(v, s, (f, r) => string.Equals(f, r, sc)));
            m_QueryEngine.AddOperatorHandler("!=", (GOP v, string s, StringComparison sc) => PropertyStringCompare(v, s, (f, r) => !string.Equals(f, r, sc)));
            m_QueryEngine.AddOperatorHandler("<=", (GOP v, string s, StringComparison sc) => PropertyStringCompare(v, s, (f, r) => string.Compare(f, r, sc) <= 0));
            m_QueryEngine.AddOperatorHandler("<", (GOP v, string s, StringComparison sc) => PropertyStringCompare(v, s, (f, r) => string.Compare(f, r, sc) < 0));
            m_QueryEngine.AddOperatorHandler(">", (GOP v, string s, StringComparison sc) => PropertyStringCompare(v, s, (f, r) => string.Compare(f, r, sc) > 0));
            m_QueryEngine.AddOperatorHandler(">=", (GOP v, string s, StringComparison sc) => PropertyStringCompare(v, s, (f, r) => string.Compare(f, r, sc) >= 0));

            m_QueryEngine.AddTypeParser(arg =>
            {
                if (arg.Length > 0 && arg.Last() == ']')
                {
                    var rangeMatches = s_RangeRx.Matches(arg);
                    if (rangeMatches.Count == 1 && rangeMatches[0].Groups.Count == 3)
                    {
                        var rg = rangeMatches[0].Groups;
                        if (float.TryParse(rg[1].Value, out var min) && float.TryParse(rg[2].Value, out var max))
                            return new ParseResult<PropertyRange>(true, new PropertyRange(min, max));
                    }
                }

                return ParseResult<PropertyRange>.none;
            });

            m_QueryEngine.SetSearchDataCallback(OnSearchData, StringComparison.OrdinalIgnoreCase);

            m_QueryEngine.AddFiltersFromAttribute<SceneQueryEngineFilterAttribute, SceneQueryEngineParameterTransformerAttribute>();
        }

        private static bool StringContains<T>(T ev, T fv, StringComparison sc)
        {
            return ev.ToString().IndexOf(fv.ToString(), sc) != -1;
        }

        private static bool PropertyRangeCompare(GOP v, PropertyRange range, Func<float, PropertyRange, bool> comparer)
        {
            if (v.type != GOP.ValueType.Number)
                return false;
            return comparer(v.number, range);
        }

        private static bool PropertyFloatCompare(GOP v, float value, Func<float, float, bool> comparer)
        {
            if (v.type != GOP.ValueType.Number)
                return false;
            return comparer(v.number, value);
        }

        private static bool PropertyBoolCompare(GOP v, bool b, Func<bool, bool, bool> comparer)
        {
            if (v.type != GOP.ValueType.Bool)
                return false;
            return comparer(v.b, b);
        }

        private static bool PropertyStringCompare(GOP v, string s, Func<string, string, bool> comparer)
        {
            if (v.type != GOP.ValueType.Text || String.IsNullOrEmpty(v.text))
                return false;
            return comparer(v.text, s);
        }

        public IEnumerable<GameObject> Search(string searchQuery)
        {
            var query = m_QueryEngine.Parse(searchQuery);
            if (!query.valid)
            {
                #if QUICKSEARCH_DEBUG
                foreach (var err in query.errors)
                    Debug.LogWarning($"Invalid search query. {err.reason} ({err.index},{err.length})");
                #endif
                return Enumerable.Empty<GameObject>();
            }
            return query.Apply(m_GameObjects.Where(g => g));//.ToList();
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

        private GOP FindPropertyValue(UnityEngine.Object obj, string propertyName)
        {
            using (var so = new SerializedObject(obj))
            {
                //Utils.LogProperties(so);
                var property = so.FindProperty(propertyName) ?? so.FindProperty($"m_{propertyName}");
                if (property != null)
                    return ConvertPropertyValue(property);

                property = so.GetIterator();
                var next = property.Next(true);
                while (next)
                {
                    if (property.name.LastIndexOf(propertyName, StringComparison.OrdinalIgnoreCase) != -1)
                        return ConvertPropertyValue(property);
                    next = property.Next(false);
                }
            }

            return GOP.invalid;
        }

        private static string HexConverter(Color c)
        {
            return "#" + Mathf.RoundToInt(c.r * 255f).ToString("X2") + Mathf.RoundToInt(c.g * 255f).ToString("X2") + Mathf.RoundToInt(c.b * 255f).ToString("X2");
        }

        private GOP ConvertPropertyValue(SerializedProperty sp)
        {
            switch (sp.propertyType)
            {
                case SerializedPropertyType.Integer: return new GOP((float)sp.intValue);
                case SerializedPropertyType.Boolean: return new GOP(sp.boolValue);
                case SerializedPropertyType.Float: return new GOP(sp.floatValue);
                case SerializedPropertyType.String: return new GOP(sp.stringValue);
                case SerializedPropertyType.Enum: return new GOP(sp.enumNames[sp.enumValueIndex]);
                case SerializedPropertyType.ObjectReference: return new GOP(sp.objectReferenceValue?.name);
                case SerializedPropertyType.Bounds: return new GOP(sp.boundsValue.size.magnitude);
                case SerializedPropertyType.BoundsInt: return new GOP(sp.boundsIntValue.size.magnitude);
                case SerializedPropertyType.Rect: return new GOP(sp.rectValue.size.magnitude);
                case SerializedPropertyType.Color: return new GOP(HexConverter(sp.colorValue));
                case SerializedPropertyType.Generic: break;
                case SerializedPropertyType.LayerMask: break;
                case SerializedPropertyType.Vector2: break;
                case SerializedPropertyType.Vector3: break;
                case SerializedPropertyType.Vector4: break;
                case SerializedPropertyType.ArraySize: break;
                case SerializedPropertyType.Character: break;
                case SerializedPropertyType.AnimationCurve: break;
                case SerializedPropertyType.Gradient: break;
                case SerializedPropertyType.Quaternion: break;
                case SerializedPropertyType.ExposedReference: break;
                case SerializedPropertyType.FixedBufferSize: break;
                case SerializedPropertyType.Vector2Int: break;
                case SerializedPropertyType.Vector3Int: break;
                case SerializedPropertyType.RectInt: break;
                case SerializedPropertyType.ManagedReference: break;
            }

            return GOP.invalid;
        }

        private GOP OnPropertyFilter(GameObject go, string propertyName)
        {
            var god = GetGOD(go);

            if (god.properties == null)
                god.properties = new Dictionary<string, GOP>();
            else if (god.properties.TryGetValue(propertyName, out var existingProperty))
                return existingProperty;

            var gocs = go.GetComponents<Component>();
            for (int componentIndex = 1; componentIndex < gocs.Length; ++componentIndex)
            {
                var c = gocs[componentIndex];
                if (!c || c.hideFlags.HasFlag(HideFlags.HideInInspector))
                    continue;

                var property = FindPropertyValue(c, propertyName);
                if (property.valid)
                {
                    god.properties[propertyName] = property;
                    return property;
                }
            }
            return GOP.invalid;
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

            value = value.ToLowerInvariant();
            if (op == "=")
                return god.types.Any(t => t.Equals(value, StringComparison.Ordinal));
            return god.types.Any(t => t.IndexOf(value, StringComparison.Ordinal) != -1);
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

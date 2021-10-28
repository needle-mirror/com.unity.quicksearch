using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

#if USE_SEARCH_MODULE
using UnityEditor.SceneManagement;
#else
using UnityEditor.Experimental.SceneManagement;
#endif

namespace UnityEditor.Search
{
    /// <summary>
    /// Utilities used by multiple components of QuickSearch.
    /// </summary>
    public static class SearchUtils
    {
        internal static readonly char[] KeywordsValueDelimiters = new[] { ':', '=', '<', '>', '!', '|' };

        /// <summary>
        /// Separators used to split an entry into indexable tokens.
        /// </summary>
        public static readonly char[] entrySeparators = { '/', ' ', '_', '-', '.' };

        private static readonly Stack<StringBuilder> _SbPool = new Stack<StringBuilder>();

        /// <summary>
        /// Extract all variations on a word.
        /// </summary>
        /// <param name="word"></param>
        /// <returns></returns>
        public static string[] FindShiftLeftVariations(string word)
        {
            if (word.Length <= 1)
                return new string[0];

            var variations = new List<string>(word.Length) { word };
            for (int i = 1, end = word.Length - 1; i < end; ++i)
            {
                word = word.Substring(1);
                variations.Add(word);
            }

            return variations.ToArray();
        }

        /// <summary>
        /// Tokenize a string each Capital letter.
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        static readonly Regex s_CamelCaseSplit = new Regex(@"(?<!^)(?=[A-Z0-9])", RegexOptions.Compiled);
        public static string[] SplitCamelCase(string source)
        {
            return s_CamelCaseSplit.Split(source);
        }

        internal static string UppercaseFirst(string s)
        {
            // Check for empty string.
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }
            // Return char and concat substring.
            return char.ToUpper(s[0]) + s.Substring(1);
        }

        internal static string ToPascalWithSpaces(string s)
        {
            // Check for empty string.
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            var tokens = Regex.Split(s, @"-+|_+|\s+|(?<!^)(?=[A-Z0-9])")
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(UppercaseFirst);
            return string.Join(" ", tokens);
        }

        /// <summary>
        /// Split an entry according to a specified list of separators.
        /// </summary>
        /// <param name="entry">Entry to split.</param>
        /// <param name="entrySeparators">List of separators that indicate split points.</param>
        /// <returns>Returns list of tokens in lowercase</returns>
        public static IEnumerable<string> SplitEntryComponents(string entry, char[] entrySeparators)
        {
            var nameTokens = entry.Split(entrySeparators).Distinct();
            var scc = nameTokens.SelectMany(s => SplitCamelCase(s)).Where(s => s.Length > 0);
            var fcc = scc.Aggregate("", (current, s) => current + s[0]);
            return new[] { fcc }.Concat(scc.Where(s => s.Length > 1))
                .Where(s => s.Length > 1)
                .Select(s => s.ToLowerInvariant())
                .Distinct();
        }

        #if DEBUG_FILE_COMPONENTS
        [MenuItem("Assets/Word Components")]
        static void PrintFilenameComponents()
        {
            var assetPath = AssetDatabase.GetAssetPath(Selection.activeInstanceID);
            if (string.IsNullOrEmpty(assetPath))
                return;
            var filename = Path.GetFileName(assetPath);
            foreach (var c in SplitFileEntryComponents(filename, SearchUtils.entrySeparators))
                Debug.Log(c);
        }

        #endif

        /// <summary>
        /// Split a file entry according to a list of separators and find all the variations on the entry name.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="entrySeparators"></param>
        /// <returns>Returns list of tokens and variations in lowercase</returns>
        public static IEnumerable<string> SplitFileEntryComponents(string path, in char[] entrySeparators)
        {
            path = Utils.RemoveInvalidCharsFromPath(path, '_');
            var name = Path.GetFileNameWithoutExtension(path);
            var nameTokens = name.Split(entrySeparators).Distinct().ToArray();
            var scc = nameTokens.SelectMany(s => SplitCamelCase(s)).Where(s => s.Length > 0).ToArray();
            var fcc = scc.Aggregate("", (current, s) => current + s[0]);
            return new[] { Path.GetExtension(path).Replace(".", "") }
                .Concat(scc.Where(s => s.Length > 1))
                .Concat(FindShiftLeftVariations(fcc))
                .Concat(nameTokens)
                .Concat(path.Split(entrySeparators).Reverse())
                .Where(s => s.Length > 1)
                .Select(s => s.ToLowerInvariant())
                .Distinct();
        }

        /// <summary>
        /// Format the pretty name of a Transform component by appending all the parents hierarchy names.
        /// </summary>
        /// <param name="tform">Transform to extract name from.</param>
        /// <returns>Returns a transform name using "/" as hierarchy separator.</returns>
        public static string GetTransformPath(Transform tform)
        {
            if (tform.parent == null)
                return "/" + tform.name;
            return GetTransformPath(tform.parent) + "/" + tform.name;
        }

        /// <summary>
        /// Get the path of a Unity Object. If it is a GameObject or a Component it is the <see cref="SearchUtils.GetTransformPath(Transform)"/>. Else it is the asset name.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns>Returns the path of an object.</returns>
        public static string GetObjectPath(UnityEngine.Object obj)
        {
            if (!obj)
                return string.Empty;
            if (obj is Component c)
                return GetTransformPath(c.gameObject.transform);
            var assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(assetPath))
            {
                if (Utils.IsBuiltInResource(assetPath))
                    return GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
                return assetPath;
            }
            if (obj is GameObject go)
                return GetTransformPath(go.transform);
            return obj.name;
        }

        static ulong GetStableHash(in UnityEngine.Object obj, in ulong assetHash = 0)
        {
            var fileIdHint = Utils.GetFileIDHint(obj);
            if (fileIdHint == 0)
                fileIdHint = (ulong)obj.GetInstanceID();
            return fileIdHint * 1181783497276652981UL + assetHash;
        }

        /// <summary>
        /// Return a unique document key owning the object
        /// </summary>
        internal static ulong GetDocumentKey(in UnityEngine.Object obj)
        {
            if (!obj)
                return ulong.MaxValue;
            if (obj is GameObject go)
                return GetStableHash(go, (ulong)(GetHierarchyAssetPath(go)?.GetHashCode() ?? 0));
            if (obj is Component c)
                return GetDocumentKey(c.gameObject);
            var assetPath = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(assetPath))
                return ulong.MaxValue;
            return AssetDatabase.AssetPathToGUID(assetPath).GetHashCode64();
        }

        /// <summary>
        /// Get the hierarchy path of a GameObject possibly including the scene name.
        /// </summary>
        /// <param name="gameObject">GameObject to extract a path from.</param>
        /// <param name="includeScene">If true, will append the scene name to the path.</param>
        /// <returns>Returns the path of a GameObject.</returns>
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
                        var prefabStage = PrefabStageUtility.GetPrefabStage(gameObject);
                        if (prefabStage != null)
                            sceneName = "Prefab Stage";
                        else
                            sceneName = "Unsaved Scene";
                    }

                    sb.Append("<b>" + sceneName + "</b>");
                }

                sb.Append(GetTransformPath(gameObject.transform));

                var path = sb.ToString();
                sb.Clear();
                return path;
            }
            finally
            {
                _SbPool.Push(sb);
            }
        }

        /// <summary>
        /// Get the path of the scene (or prefab) containing a GameObject.
        /// </summary>
        /// <param name="gameObject">GameObject to find the scene path.</param>
        /// <param name="prefabOnly">If true, will return a path only if the GameObject is a prefab.</param>
        /// <returns>Returns the path of a scene or prefab</returns>
        public static string GetHierarchyAssetPath(GameObject gameObject, bool prefabOnly = false)
        {
            if (gameObject == null)
                return String.Empty;

            bool isPrefab = PrefabUtility.GetPrefabAssetType(gameObject.gameObject) != PrefabAssetType.NotAPrefab;
            if (isPrefab)
                return PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);

            if (prefabOnly)
                return null;

            return gameObject.scene.path;
        }

        /// <summary>
        /// Select and ping multiple objects in the Project Browser.
        /// </summary>
        /// <param name="items">Search Items to select and ping.</param>
        /// <param name="focusProjectBrowser">If true, will focus the project browser before pinging the objects.</param>
        /// <param name="pingSelection">If true, will ping the selected objects.</param>
        public static void SelectMultipleItems(IEnumerable<SearchItem> items, bool focusProjectBrowser = false, bool pingSelection = true)
        {
            Selection.objects = items.Select(i => i.ToObject()).Where(o => o).ToArray();
            if (Selection.objects.Length == 0)
            {
                var firstItem = items.FirstOrDefault();
                if (firstItem != null)
                    EditorUtility.OpenWithDefaultApp(firstItem.id);
                return;
            }
            EditorApplication.delayCall += () =>
            {
                if (focusProjectBrowser)
                    EditorWindow.FocusWindowIfItsOpen(Utils.GetProjectBrowserWindowType());
                if (pingSelection)
                    EditorApplication.delayCall += () => EditorGUIUtility.PingObject(Selection.objects.LastOrDefault());
            };
        }

        /// <summary>
        /// Helper function to match a string against the SearchContext. This will try to match the search query against each tokens of content (similar to the AddComponent menu workflow)
        /// </summary>
        /// <param name="context">Search context containing the searchQuery that we try to match.</param>
        /// <param name="content">String content that will be tokenized and use to match the search query.</param>
        /// <param name="ignoreCase">Perform matching ignoring casing.</param>
        /// <returns>Has a match occurred.</returns>
        public static bool MatchSearchGroups(SearchContext context, string content, bool ignoreCase = false)
        {
            return MatchSearchGroups(context.searchQuery, context.searchWords, content, out _, out _,
                ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        internal static bool MatchSearchGroups(string searchContext, string[] tokens, string content, out int startIndex, out int endIndex, StringComparison sc = StringComparison.OrdinalIgnoreCase)
        {
            startIndex = endIndex = -1;
            if (String.IsNullOrEmpty(content))
                return false;

            if (string.IsNullOrEmpty(searchContext))
                return false;

            if (searchContext == content)
            {
                startIndex = 0;
                endIndex = content.Length - 1;
                return true;
            }

            return MatchSearchGroups(tokens, content, out startIndex, out endIndex, sc);
        }

        internal static bool MatchSearchGroups(string[] tokens, string content, out int startIndex, out int endIndex, StringComparison sc = StringComparison.OrdinalIgnoreCase)
        {
            startIndex = endIndex = -1;
            if (String.IsNullOrEmpty(content))
                return false;

            // Each search group is space separated
            // Search group must match in order and be complete.
            var searchGroups = tokens;
            var startSearchIndex = 0;
            foreach (var searchGroup in searchGroups)
            {
                if (searchGroup.Length == 0)
                    continue;

                startSearchIndex = content.IndexOf(searchGroup, startSearchIndex, sc);
                if (startSearchIndex == -1)
                {
                    return false;
                }

                startIndex = startIndex == -1 ? startSearchIndex : startIndex;
                startSearchIndex = endIndex = startSearchIndex + searchGroup.Length - 1;
            }

            return startIndex != -1 && endIndex != -1;
        }

        /// <summary>
        /// Utility function to fetch all the game objects in a particular scene.
        /// </summary>
        /// <param name="scene">Scene to get objects from.</param>
        /// <returns>The array of game objects in the scene.</returns>
        public static GameObject[] FetchGameObjects(Scene scene)
        {
            var goRoots = new List<UnityEngine.Object>();
            if (!scene.IsValid() || !scene.isLoaded)
                return new GameObject[0];
            var sceneRootObjects = scene.GetRootGameObjects();
            if (sceneRootObjects != null && sceneRootObjects.Length > 0)
                goRoots.AddRange(sceneRootObjects);

            return SceneModeUtility.GetObjects(goRoots.ToArray(), true);
        }

        /// <summary>
        /// Utility function to fetch all the game objects for the current stage (i.e. scene or prefab)
        /// </summary>
        /// <returns>The array of game objects in the current stage.</returns>
        public static IEnumerable<GameObject> FetchGameObjects()
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
                .Where(o => (o.hideFlags & HideFlags.HideInHierarchy) != HideFlags.HideInHierarchy);
        }

        internal static ISet<string> GetReferences(UnityEngine.Object obj, int level = 1)
        {
            var refs = new HashSet<string>();

            var objPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(objPath))
                refs.UnionWith(AssetDatabase.GetDependencies(objPath));

            if (obj is GameObject go)
            {
                foreach (var c in go.GetComponents<Component>())
                {
                    using (var so = new SerializedObject(c))
                    {
                        var p = so.GetIterator();
                        var next = p.NextVisible(true);
                        while (next)
                        {
                            if (p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue)
                            {
                                var refValue = AssetDatabase.GetAssetPath(p.objectReferenceValue);
                                if (!String.IsNullOrEmpty(refValue))
                                    refs.Add(refValue);
                            }

                            next = p.NextVisible(!p.isArray && !p.isFixedBuffer);
                        }
                    }
                }
            }

            var lvlRefs = refs;
            while (level-- > 0)
            {
                var nestedRefs = new HashSet<string>();

                foreach (var r in lvlRefs)
                    nestedRefs.UnionWith(AssetDatabase.GetDependencies(r, false));

                lvlRefs = nestedRefs;
                lvlRefs.ExceptWith(refs);
                refs.UnionWith(nestedRefs);
            }

            refs.Remove(objPath);

            return refs;
        }

        static readonly Dictionary<string, SearchProvider> s_GroupProviders = new Dictionary<string, SearchProvider>();
        internal static SearchProvider CreateGroupProvider(SearchProvider templateProvider, string groupId, int groupPriority, bool cacheProvider = false)
        {
            if (cacheProvider && s_GroupProviders.TryGetValue(groupId, out var groupProvider))
                return groupProvider;

            groupProvider = new SearchProvider($"_group_provider_{groupId}", groupId)
            {
                type = templateProvider.id,
                priority = groupPriority,
                isExplicitProvider = true,
                actions = templateProvider.actions,
                showDetails = templateProvider.showDetails,
                showDetailsOptions = templateProvider.showDetailsOptions,
                fetchDescription = templateProvider.fetchDescription,
                fetchItems = templateProvider.fetchItems,
                fetchLabel = templateProvider.fetchLabel,
                fetchPreview = templateProvider.fetchPreview,
                fetchThumbnail = templateProvider.fetchThumbnail,
                startDrag = templateProvider.startDrag,
                toObject = templateProvider.toObject,
                trackSelection = templateProvider.trackSelection,
                fetchColumns = templateProvider.fetchColumns,
                toKey = templateProvider.toKey,
                toType = templateProvider.toType,
                fetchPropositions = templateProvider.fetchPropositions,
            };

            if (cacheProvider)
                s_GroupProviders[groupId] = groupProvider;

            return groupProvider;
        }

        public static string GetAssetPath(in SearchItem item)
        {
            if (item.provider.type == Providers.AssetProvider.type)
                return Providers.AssetProvider.GetAssetPath(item);
            if (item.provider.type == "dep")
                return AssetDatabase.GUIDToAssetPath(item.id);
            return null;
        }

        static Dictionary<Type, List<Type>> s_BaseTypes = new Dictionary<Type, List<Type>>();
        internal static IEnumerable<SearchProposition> FetchTypePropositions<T>(string category = "Types", Type blockType = null, int priority = -1444) where T : UnityEngine.Object
        {
            if (category != null)
            {
                yield return new SearchProposition(
                    priority: priority,
                    category: null,
                    label: category,
                    icon: EditorGUIUtility.FindTexture("FilterByType"));

                yield return new SearchProposition(category: category, label: "Prefabs", replacement: "t:prefab",
                    icon: GetTypeIcon(typeof(GameObject)), data: typeof(GameObject), type: blockType, priority: priority, color: QueryColors.type);
            }

            if (string.Equals(category, "Types", StringComparison.Ordinal))
            {
                yield return new SearchProposition(category: "Types", label: "Scripts", replacement: "t:script",
                    icon: GetTypeIcon(typeof(MonoScript)), data: typeof(MonoScript), type: blockType, priority: priority, color: QueryColors.type);
                yield return new SearchProposition(category: "Types", label: "Scenes", replacement: "t:scene",
                    icon: GetTypeIcon(typeof(SceneAsset)), data: typeof(SceneAsset), type: blockType, priority: priority, color: QueryColors.type);
            }

            if (!s_BaseTypes.TryGetValue(typeof(T), out var types))
            {
                var ignoredAssemblies = new[]
                {
                    typeof(EditorApplication).Assembly,
                    typeof(UnityEditorInternal.InternalEditorUtility).Assembly
                };
                types = TypeCache.GetTypesDerivedFrom<T>()
                .Where(t => t.IsVisible)
                .Where(t => !t.IsGenericType)
                .Where(t => !ignoredAssemblies.Contains(t.Assembly))
                .Where(t => !typeof(Editor).IsAssignableFrom(t))
                .Where(t => !typeof(EditorWindow).IsAssignableFrom(t))
                .Where(t => t.Assembly.GetName().Name.IndexOf("Editor", StringComparison.Ordinal) == -1).ToList();
                s_BaseTypes[typeof(T)] = types;
            }
            foreach (var t in types)
            {
                yield return new SearchProposition(
                    priority: t.Name[0] + priority,
                    category: category,
                    label: t.Name,
                    replacement: $"t:{t.Name}",
                    data: t,
                    type: blockType,
                    icon: GetTypeIcon(t),
                    color: QueryColors.type);
            }
        }

        internal static SearchProposition CreateKeywordProposition(in string keyword)
        {
            if (keyword.IndexOf('|') == -1)
                return SearchProposition.invalid;

            var tokens = keyword.Split('|');
            if (tokens.Length != 5)
                return SearchProposition.invalid;

            // <0:fieldname>:|<1:display name>|<2:help text>|<3:property type>|<4: owner type string>
            var valueType = tokens[3];
            var replacement = ParseBlockContent(valueType, tokens[0], out Type blockType);
            var ownerType = FindType<UnityEngine.Object>(tokens[4]);
            if (ownerType == null)
                return SearchProposition.invalid;
            return new SearchProposition(
                priority: (ownerType.Name[0] << 4) + tokens[1][0],
                category: $"Properties/{ownerType.Name}",
                label: $"{tokens[1]} ({blockType?.Name ?? valueType})",
                replacement: replacement,
                help: tokens[2],
                color: replacement.StartsWith("#", StringComparison.Ordinal) ? QueryColors.property : QueryColors.filter,
                icon:
                    #if USE_SEARCH_MODULE
                    AssetPreview.GetMiniTypeThumbnailFromType(blockType) ??
                    #endif
                    GetTypeIcon(ownerType));
        }

        static Dictionary<Type, Texture2D> s_TypeIcons = new Dictionary<Type, Texture2D>();
        internal static Texture2D GetTypeIcon(in Type type)
        {
            if (s_TypeIcons.TryGetValue(type, out var t) && t)
                return t;
            if (!type.IsAbstract && typeof(MonoBehaviour) != type && typeof(MonoBehaviour).IsAssignableFrom(type))
            {
                var go = new GameObject { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    go.SetActive(false);
                    var c = go.AddComponent(type);
                    var p = AssetPreview.GetMiniThumbnail(c);
                    if (p)
                        return s_TypeIcons[type] = p;
                }
                catch
                {
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
            return s_TypeIcons[type] = AssetPreview.GetMiniTypeThumbnail(type) ?? AssetPreview.GetMiniTypeThumbnail(typeof(MonoScript));
        }

        internal static IEnumerable<SearchProposition> EnumeratePropertyPropositions(IEnumerable<UnityEngine.Object> objs)
        {
            return EnumeratePropertyKeywords(objs).Select(k => CreateKeywordProposition(k));
        }

        internal static IEnumerable<string> EnumeratePropertyKeywords(IEnumerable<UnityEngine.Object> objs)
        {
            var templates = GetTemplates(objs);
            foreach (var obj in templates)
            {
                var objType = obj.GetType();
                using (var so = new SerializedObject(obj))
                {
                    var p = so.GetIterator();
                    var next = p.NextVisible(true);
                    while (next)
                    {
                        var supported = IsPropertyTypeSupported(p);
                        if (supported)
                        {
                            var propertyType = GetPropertyManagedTypeString(p);
                            if (propertyType != null)
                            {
                                var keyword = CreateKeyword(p, propertyType);
                                yield return keyword;
                            }
                        }

                        var isVector = p.propertyType == SerializedPropertyType.Vector3 ||
                            p.propertyType == SerializedPropertyType.Vector4 ||
                            p.propertyType == SerializedPropertyType.Quaternion ||
                            p.propertyType == SerializedPropertyType.Vector2;

                        next = p.NextVisible(supported && !p.isArray && !p.isFixedBuffer && !isVector);
                    }
                }
            }
        }

        private static string CreateKeyword(in SerializedProperty p, in string propertyType)
        {
            var path = p.propertyPath;
            if (path.IndexOf(' ') != -1)
                path = p.name;
            return $"#{path.Replace(" ", "")}|{p.displayName}|{p.tooltip}|{propertyType}|{p.serializedObject?.targetObject?.GetType().AssemblyQualifiedName}";
        }

        internal static string GetPropertyManagedTypeString(in SerializedProperty p)
        {
            Type managedType;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector4:
                case SerializedPropertyType.Boolean:
                case SerializedPropertyType.String:
                    return p.propertyType.ToString();

                case SerializedPropertyType.Integer:
                    managedType = p.GetManagedType();
                    if (managedType != null && !managedType.IsPrimitive)
                        return managedType.AssemblyQualifiedName;
                    return "Number";

                case SerializedPropertyType.Character:
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Float:
                    return "Number";

                case SerializedPropertyType.Generic:
                    if (p.isArray)
                        return "Count";
                    return null;

                case SerializedPropertyType.ObjectReference:
                    if (p.objectReferenceValue)
                        return p.objectReferenceValue.GetType().AssemblyQualifiedName;
                    if (p.type.StartsWith("PPtr<", StringComparison.Ordinal) && TryFindType<UnityEngine.Object>(p.type.Substring(5, p.type.Length - 6), out managedType))
                        return managedType.AssemblyQualifiedName;
                    managedType = p.GetManagedType();
                    if (managedType != null && !managedType.IsPrimitive)
                        return managedType.AssemblyQualifiedName;
                    return null;
            }

            if (p.isArray)
                return "Count";

            managedType = p.GetManagedType();
            if (managedType != null && !managedType.IsPrimitive)
                return managedType.AssemblyQualifiedName;

            return p.propertyType.ToString();
        }

        internal static bool IsPropertyTypeSupported(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.AnimationCurve:
                case SerializedPropertyType.Bounds:
                case SerializedPropertyType.Gradient:
                    return false;
            }

            if (p.propertyType == SerializedPropertyType.Generic)
            {
                if (string.Equals(p.type, "map", StringComparison.Ordinal))
                    return false;
                if (string.Equals(p.type, "Matrix4x4f", StringComparison.Ordinal))
                    return false;
            }

            return !p.isArray && !p.isFixedBuffer && p.propertyPath.LastIndexOf('[') == -1;
        }

        internal static IEnumerable<UnityEngine.Object> GetTemplates(IEnumerable<UnityEngine.Object> objects)
        {
            var seenTypes = new HashSet<Type>();
            foreach (var obj in objects)
            {
                if (!obj)
                    continue;
                var ct = obj.GetType();
                if (!seenTypes.Contains(ct))
                {
                    seenTypes.Add(ct);
                    yield return obj;
                }

                if (obj is GameObject go)
                {
                    foreach (var comp in go.GetComponents<Component>())
                    {
                        if (!comp)
                            continue;
                        ct = comp.GetType();
                        if (!seenTypes.Contains(ct))
                        {
                            seenTypes.Add(ct);
                            yield return comp;
                        }
                    }
                }

                var path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path))
                {
                    var importer = AssetImporter.GetAtPath(path);
                    if (importer)
                    {
                        var it = importer.GetType();
                        if (it != typeof(AssetImporter) && !seenTypes.Contains(it))
                        {
                            seenTypes.Add(it);
                            yield return importer;
                        }
                    }
                }
            }
        }

        internal static string ParseSearchText(string searchText, IEnumerable<SearchProvider> providers, out SearchProvider filteredProvider)
        {
            filteredProvider = null;
            var searchQuery = searchText.TrimStart();
            if (string.IsNullOrEmpty(searchQuery))
                return searchQuery;

            foreach (var p in providers)
            {
                if (searchQuery.StartsWith(p.filterId, StringComparison.OrdinalIgnoreCase))
                {
                    filteredProvider = p;
                    searchQuery = searchQuery.Remove(0, p.filterId.Length).TrimStart();
                    break;
                }
            }
            return searchQuery;
        }

        static string ParseBlockContent(string type, in string content, out Type valueType)
        {
            var replacement = content;
            var del = content.LastIndexOf(':');
            if (del != -1)
                replacement = content.Substring(0, del);

            valueType = Type.GetType(type);
            type = valueType?.Name ?? type;

            #if USE_QUERY_BUILDER
            if (QueryListBlockAttribute.TryGetReplacement(replacement.ToLower(), type, ref valueType, out var replacementText))
                return replacementText;
            #endif

            switch (type)
            {
                case "Enum":
                    return $"{replacement}=0";
                case "String":
                    return $"{replacement}:\"\"";
                case "Boolean":
                    return $"{replacement}=true";
                case "Array":
                case "Count":
                    return $"{replacement}>=1";
                case "Integer":
                case "Float":
                case "Number":
                    return $"{replacement}>0";
                case "Color":
                    return $"{replacement}=#00ff00";
                case "Vector2":
                    return $"{replacement}=(,)";
                case "Vector3":
                case "Quaternion":
                    return $"{replacement}=(,,)";
                case "Vector4":
                    return $"{replacement}=(,,,)";

                default:
                    if (valueType != null)
                    {
                        if (typeof(UnityEngine.Object).IsAssignableFrom(valueType))
                            return $"{replacement}=<$object:none,{valueType.FullName}$>";
                        if (valueType.IsEnum)
                        {
                            var enums = valueType.GetEnumValues();
                            if (enums.Length > 0)
                                return $"{replacement}=<$enum:{enums.GetValue(0)},{valueType.Name}$>";
                        }
                    }
                    break;
            }

            return replacement;
        }

        internal static bool TryFindType<T>(in string typeString, out Type type)
        {
            type = FindType<T>(typeString);
            return type != null;
        }

        static Dictionary<string, Type> s_CachedTypes = new Dictionary<string, Type>();
        internal static Type FindType<T>(in string typeString)
        {
            if (s_CachedTypes.TryGetValue(typeString, out var foundType))
                return foundType;

            var selfType = typeof(T);
            if (string.Equals(selfType.Name, typeString, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(selfType.FullName, typeString, StringComparison.Ordinal))
                return s_CachedTypes[typeString] = selfType;

            var type = Type.GetType(typeString);
            if (type != null)
                return s_CachedTypes[typeString] = type;
            foreach (var t in TypeCache.GetTypesDerivedFrom<T>())
            {
                if (!t.IsVisible)
                    continue;
                if (t.GetAttribute<ObsoleteAttribute>() != null)
                    continue;
                if (string.Equals(t.Name, typeString, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.FullName, typeString, StringComparison.Ordinal))
                {
                    return s_CachedTypes[typeString] = t;
                }
            }
            return s_CachedTypes[typeString] = null;
        }

        internal static IEnumerable<Type> FindTypes<T>(string typeString)
        {
            foreach (var t in TypeCache.GetTypesDerivedFrom<T>())
            {
                if (!t.IsVisible)
                    continue;
                if (t.GetAttribute<ObsoleteAttribute>() != null)
                    continue;
                if (string.Equals(t.Name, typeString, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.FullName, typeString, StringComparison.Ordinal))
                {
                    yield return t;
                }
            }
        }
    }
}

//#define DEBUG_INDEXING

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.QuickSearch.Providers
{
    [Serializable]
    class AssetIndexerOptions
    {
        public bool fstats = true;
        public bool types = true;
        public bool properties = false;
        public bool dependencies = false;
        public bool nestedObjects = false;
    }

    [Serializable]
    class AssetIndexerSettings
    {
        public const int version = ADBIndex.version;

        public bool disabled;
        public string[] roots;
        public bool directories = true;
        public string[] includes;
        public string[] excludes;
        public int baseScore = 100;
        public AssetIndexerOptions options;

        public int minIndexCharVariation = 2;
        public int maxIndexCharVariation = 12;
    }

    class AssetIndexer : SearchIndexer
    {
        public string name { get; private set; }
        public AssetIndexerSettings settings { get; private set; }

        private static readonly string[] k_FieldNamesNoKeywords = {"name", "text"};

        #if DEBUG_INDEXING
        private readonly Dictionary<string, HashSet<string>> m_DebugStringTable = new Dictionary<string, HashSet<string>>();
        #endif

        private readonly QueryEngine<SearchResult> m_QueryEngine = new QueryEngine<SearchResult>(validateFilters: false);
        private static Dictionary<string, Query<SearchResult, SearchIndexerQuery<SearchResult>.EvalHandler, object>> s_QueryPool = new Dictionary<string, Query<SearchResult, SearchIndexerQuery<SearchResult>.EvalHandler, object>>();

        public AssetIndexer(string name, AssetIndexerSettings settings)
            : base(name ?? "assets")
        {
            this.name = name;
            this.settings = settings;
            minIndexCharVariation = Math.Max(2, settings?.minIndexCharVariation ?? 2);
            maxIndexCharVariation = Math.Max(minIndexCharVariation, settings?.maxIndexCharVariation ?? 12);
            getEntryComponentsHandler = GetEntryComponents;

            m_QueryEngine.SetSearchDataCallback(e => null);
        }

        public IEnumerable<string> GetEntryComponents(string path, int index)
        {
            return SearchUtils.SplitFileEntryComponents(path, entrySeparators, minIndexCharVariation, maxIndexCharVariation);
        }

        public event Action<int, string, float, bool> reportProgress;

        private Query<SearchResult, SearchIndexerQuery<SearchResult>.EvalHandler, object> BuildQuery(string searchQuery)
        {
            Query<SearchResult, SearchIndexerQuery<SearchResult>.EvalHandler, object> query;
            if (s_QueryPool.TryGetValue(searchQuery, out query) && query.valid)
                return query;

            if (s_QueryPool.Count > 50)
                s_QueryPool.Clear();

            query = m_QueryEngine.Parse<SearchIndexerQuery, SearchIndexerQuery.EvalHandler, object>(searchQuery);
            if (query.valid)
                s_QueryPool[searchQuery] = query;
            return query;
        }

        public IEnumerable<SearchResult> Search(string searchQuery)
        {
            if (settings.disabled)
                return Enumerable.Empty<SearchResult>();

            var query = BuildQuery(searchQuery);
            if (!query.valid)
                return Enumerable.Empty<SearchResult>();

            #if DEBUG_INDEXING
            using (new DebugTimer($"Search \"{searchQuery}\" in {name}"))
            #endif
            {
                return query.Eval(args =>
                {
                    if (args.op == SearchIndexOperator.None)
                        return SearchIndexerQuery.EvalResult.None;

                    #if DEBUG_INDEXING
                    using (var t = new DebugTimer(null))
                    #endif
                    {
                        SearchResultCollection subset = null;
                        if (args.andSet != null)
                            subset = new SearchResultCollection(args.andSet);

                        var results = SearchTerm(args.name, args.value, args.op, args.exclude, int.MaxValue, subset);
                        if (args.orSet != null)
                            results = results.Concat(args.orSet);

                    #if DEBUG_INDEXING
                    SearchIndexerQuery.EvalResult.Print(args, results, subset, t.timeMs);
                    #endif
                    return SearchIndexerQuery.EvalResult.Combined(results);
                    }
                }, null).OrderBy(e => e.score).Distinct();
            }
        }

        public override void Build()
        {
            if (LoadIndexFromDisk(null, true))
                return;

            var it = BuildAsync(-1, null);
            while (it.MoveNext())
                ;
        }

        private bool PatternChecks(string pattern, string ext, string dir, string fileName)
        {
            // Extension check
            if (pattern[0] == '.' && ext.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                return true;

            // Folder check
            if (pattern[pattern.Length - 1] == '/')
            {
                var icDir = pattern.Substring(0, pattern.Length - 1);
                if (dir.IndexOf(icDir, StringComparison.OrdinalIgnoreCase) != -1)
                    return true;
            }

            // File name check
            if (fileName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) != -1)
                return true;

            return false;
        }

        public bool SkipEntry(string path, bool checkRoots = false)
        {
            if (checkRoots && settings.roots != null && settings.roots.Length > 0)
            {
                if (!settings.roots.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            if (!settings.directories && Directory.Exists(path))
                return true;

            var ext = Path.GetExtension(path);

            // Exclude indexes by default
            if (ext.EndsWith("index", StringComparison.OrdinalIgnoreCase))
                return true;

            var dir = Path.GetDirectoryName(path);
            //var fileName = Path.GetFileNameWithoutExtension(path);

            if (settings.includes?.Length > 0 && !settings.includes.Any(pattern => PatternChecks(pattern, ext, dir, path)))
                return true;

            if (settings.excludes?.Length > 0 && settings.excludes.Any(pattern => PatternChecks(pattern, ext, dir, path)))
                return true;

            return false;
        }

        internal System.Collections.IEnumerator BuildAsync(int progressId, object userData = null)
        {
            string[] paths;
            if (settings.roots == null || settings.roots.Length == 0)
                paths = AssetDatabase.GetAllAssetPaths();
            else
            {
                paths = AssetDatabase.FindAssets(String.Empty, settings.roots.Where(r => Directory.Exists(r)).ToArray())
                                     .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            }
            paths = paths.Where(path => !SkipEntry(path)).ToArray();

            var pathIndex = 0;
            var pathCount = (float)paths.Length;

            Start(clear: true);

            EditorApplication.LockReloadAssemblies();
            foreach (var path in paths)
            {
                var progressReport = pathIndex++ / pathCount;
                reportProgress?.Invoke(progressId, path, progressReport, false);
                IndexAsset(path, false);
                yield return null;
            }
            EditorApplication.UnlockReloadAssemblies();

            Finish(() => {});
            while (!IsReady())
                yield return null;

            reportProgress?.Invoke(progressId, $"Indexing Completed (Documents: {documentCount}, Indexes: {indexCount:n0})", 1f, true);
            yield return null;
        }

        private string[] GetComponents(string value, int documentIndex)
        {
            return getEntryComponentsHandler(value, documentIndex).Where(c => c.Length > 0).ToArray();
        }

        private void IndexWordComponents(string path, int documentIndex, string word)
        {
            foreach (var c in GetComponents(word, documentIndex))
                IndexWord(path, c, documentIndex);
        }

        private void IndexPropertyComponents(string path, int documentIndex, string name, string value)
        {
            foreach (var c in GetComponents(value, documentIndex))
                IndexProperty(path, name, c, documentIndex, saveKeyword: false);
        }

        internal void IndexAsset(string path, bool checkIfDocumentExists)
        {
            var documentIndex = AddDocument(path, checkIfDocumentExists);
            if (documentIndex < 0)
                return;

            IndexWordComponents(path, documentIndex, path);

            try
            {
                var fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                IndexWord(path, fileName, documentIndex, Math.Min(16, fileName.Length), true);

                if (path.StartsWith("Packages/", StringComparison.Ordinal))
                    IndexProperty(path, "a", "packages", documentIndex, saveKeyword: true);
                else
                    IndexProperty(path, "a", "assets", documentIndex, saveKeyword: true);

                if (!String.IsNullOrEmpty(name))
                    IndexProperty(path, "a", name, documentIndex, saveKeyword: true);

                if (settings.options.fstats)
                {
                    var fi = new FileInfo(path);
                    if (fi.Exists)
                    {
                        IndexNumber(path, "size", (double)fi.Length, documentIndex);
                        IndexProperty(path, "ext", fi.Extension.Replace(".", "").ToLowerInvariant(), documentIndex, saveKeyword: false);
                        IndexNumber(path, "age", (DateTime.Now - fi.LastWriteTime).TotalDays, documentIndex);
                    }
                }

                if (settings.options.properties || settings.options.types)
                {
                    bool wasLoaded = AssetDatabase.IsMainAssetAtPathLoaded(path);
                    var assetObjects = settings.options.nestedObjects ? AssetDatabase.LoadAllAssetsAtPath(path)
                                            : new Object[] { AssetDatabase.LoadMainAssetAtPath(path) };
                    foreach (var mainAsset in assetObjects)
                    {
                        if (!mainAsset)
                            continue;

                        if (!String.IsNullOrEmpty(mainAsset.name))
                            IndexWord(path, mainAsset.name, documentIndex, true);

                        Type at = mainAsset.GetType();
                        while (at != null && at != typeof(Object))
                        {
                            IndexProperty(path, "t", at.Name, documentIndex, saveKeyword: true);
                            at = at.BaseType;
                        }

                        if (PrefabUtility.GetPrefabAssetType(mainAsset) != PrefabAssetType.NotAPrefab)
                            IndexProperty(path, "t", "prefab", documentIndex, saveKeyword: true);

                        var labels = AssetDatabase.GetLabels(mainAsset);
                        foreach (var label in labels)
                            IndexProperty(path, "l", label, documentIndex, saveKeyword: true);

                        if (settings.options.properties)
                        {
                            IndexObject(path, mainAsset, documentIndex);

                            if (mainAsset is GameObject go)
                            {
                                foreach (var v in go.GetComponents(typeof(Component)))
                                {
                                    if (!v)
                                        continue;
                                    IndexPropertyComponents(path, documentIndex, "has", v.GetType().Name);
                                    IndexObject(path, v, documentIndex);
                                }
                            }
                        }
                    }

                    if (!wasLoaded)
                    {
                        foreach (var assetObject in assetObjects)
                        {
                            if (!assetObject || !AssetDatabase.IsMainAsset(assetObject) ||
                                assetObject.hideFlags.HasFlag(HideFlags.DontUnloadUnusedAsset) ||
                                assetObject is GameObject ||
                                assetObject is Component ||
                                assetObject is AssetBundle)
                                continue;

                            Resources.UnloadAsset(assetObject);
                        }
                    }
                }

                if (settings.options.dependencies)
                {
                    foreach (var depPath in AssetDatabase.GetDependencies(path, true))
                    {
                        if (path == depPath)
                            continue;
                        var depName = Path.GetFileNameWithoutExtension(depPath);
                        IndexProperty(path, "dep", depName, documentIndex, saveKeyword: false);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        [System.Diagnostics.Conditional("DEBUG_INDEXING")]
        private void IndexDebugMatch(string path, string name, string value)
        {
            IndexDebugMatch(path, $"{name}:{value}");
        }

        [System.Diagnostics.Conditional("DEBUG_INDEXING")]
        private void IndexDebugMatch(string path, string word)
        {
            #if DEBUG_INDEXING
            HashSet<string> words;
            if (m_DebugStringTable.TryGetValue(path, out words))
            {
                words.Add(word);
            }
            else
            {
                m_DebugStringTable[path] = new HashSet<string> { word };
            }
            #endif
        }

        private void IndexWord(string path, string word, int documentIndex, int maxVariations, bool exact)
        {
            IndexDebugMatch(path, word);
            AddWord(word.ToLowerInvariant(), minIndexCharVariation, maxVariations, settings.baseScore, documentIndex);
            if (exact)
                AddExactWord(word.ToLowerInvariant(), settings.baseScore-1, documentIndex);
        }

        private void IndexWord(string path, string word, int documentIndex, bool exact = false)
        {
            IndexWord(path, word, documentIndex, maxIndexCharVariation, exact);
        }

        private void IndexProperty(string path, string name, string value, int documentIndex, bool saveKeyword)
        {
            if (String.IsNullOrEmpty(value))
                return;
            IndexDebugMatch(path, name, value);
            AddProperty(name, value.ToLowerInvariant(), settings.baseScore, documentIndex, saveKeyword);
        }

        private void IndexNumber(string path, string name, double number, int documentIndex)
        {
            IndexDebugMatch(path, name, number.ToString());
            AddNumber(name, number, settings.baseScore, documentIndex);
        }

        internal string GetDebugIndexStrings(string path)
        {
            #if DEBUG_INDEXING
            if (!m_DebugStringTable.ContainsKey(path))
                return null;

            return String.Join(" ", m_DebugStringTable[path].ToArray());
            #else
            return null;
            #endif
        }

        private object GetPropertyValue(SerializedProperty p, ref bool saveKeyword)
        {
            object fieldValue = null;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                    fieldValue = (double)p.intValue;
                    break;
                case SerializedPropertyType.Boolean:
                    fieldValue = p.boolValue.ToString();
                    break;
                case SerializedPropertyType.Float:
                    fieldValue = (double)p.floatValue;
                    break;
                case SerializedPropertyType.String:
                    if (p.stringValue != null && p.stringValue.Length < 10)
                        fieldValue = p.stringValue.Replace(" ", "").ToString();
                    break;
                case SerializedPropertyType.Enum:
                    if (p.enumValueIndex >= 0 && p.type == "Enum")
                        fieldValue = p.enumNames[p.enumValueIndex].ToString();
                    break;
                case SerializedPropertyType.ObjectReference:
                    if (p.objectReferenceValue)
                    {
                        saveKeyword = false;
                        fieldValue = p.objectReferenceValue.name.Replace(" ", "");
                    }
                    break;
                #if false
                case SerializedPropertyType.Color:
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector4:
                case SerializedPropertyType.Rect:
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.AnimationCurve:
                case SerializedPropertyType.Bounds:
                case SerializedPropertyType.Gradient:
                case SerializedPropertyType.Quaternion:
                case SerializedPropertyType.ExposedReference:
                case SerializedPropertyType.FixedBufferSize:
                case SerializedPropertyType.Vector2Int:
                case SerializedPropertyType.Vector3Int:
                case SerializedPropertyType.RectInt:
                case SerializedPropertyType.BoundsInt:
                case SerializedPropertyType.ManagedReference:
                case SerializedPropertyType.Generic:
                case SerializedPropertyType.LayerMask:
                #endif
                default:
                    break;
            }

            return fieldValue;
        }

        private void IndexObject(string path, Object obj, int documentIndex)
        {
            using (var so = new SerializedObject(obj))
            {
                var p = so.GetIterator();
                var next = p.Next(true);
                while (next)
                {
                    bool saveKeyword = true;
                    var fieldName = p.displayName.Replace("m_", "").Replace(" ", "").ToLowerInvariant();
                    var scc = SearchUtils.SplitCamelCase(fieldName);
                    var fcc = scc.Length > 1 && fieldName.Length > 10 ? scc.Aggregate("", (current, s) => current + s[0]) : fieldName;
                    object fieldValue = GetPropertyValue(p, ref saveKeyword);

                    // Some property names are not worth indexing and take to much spaces.
                    if (k_FieldNamesNoKeywords.Contains(fieldName))
                        saveKeyword = false;

                    if (fieldValue != null)
                    {
                        var sfv = fieldValue as string;
                        if (sfv != null)
                        {
                            if (sfv != "")
                                IndexProperty(path, fcc, sfv.Replace(" ", "").ToLowerInvariant(), documentIndex, saveKeyword);
                            else
                                IndexWord(path, $"@{fcc}", documentIndex);
                        }
                        else if (fieldValue is double)
                        {
                            var nfv = (double)fieldValue;
                            IndexNumber(path, fcc.ToLowerInvariant(), nfv, documentIndex);
                        }

                        IndexDebugMatch(path, fcc, fieldValue.ToString());
                    }
                    next = p.Next(false);
                }
            }
        }
    }
}

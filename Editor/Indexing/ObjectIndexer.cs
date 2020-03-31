//#define DEBUG_INDEXING

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using Object = UnityEngine.Object;

namespace Unity.QuickSearch.Providers
{
    abstract class ObjectIndexer : SearchIndexer
    {
        private static readonly string[] k_FieldNamesNoKeywords = {"name", "text"};

        public SearchDatabase.Settings settings { get; private set; }

        #if DEBUG_INDEXING
        private readonly Dictionary<string, HashSet<string>> m_DebugStringTable = new Dictionary<string, HashSet<string>>();
        #endif

        private readonly QueryEngine<SearchResult> m_QueryEngine = new QueryEngine<SearchResult>(validateFilters: false);
        private readonly Dictionary<string, Query<SearchResult, object>> m_QueryPool = new Dictionary<string, Query<SearchResult, object>>();

        public event Action<int, string, float, bool> reportProgress;

        public ObjectIndexer(string rootName, SearchDatabase.Settings settings)
            : base(rootName)
        {
            this.name = settings?.name ?? name;
            this.settings = settings;

            m_QueryEngine.SetSearchDataCallback(e => null);
        }

        public override IEnumerable<SearchResult> Search(string searchQuery, int maxScore = int.MaxValue, int patternMatchLimit = 2999)
        {
            if (settings.disabled)
                return Enumerable.Empty<SearchResult>();

            var query = BuildQuery(searchQuery, maxScore, patternMatchLimit);
            if (!query.valid)
                return Enumerable.Empty<SearchResult>();

            #if DEBUG_INDEXING
            using (new DebugTimer($"Search \"{searchQuery}\" in {name}"))
            #endif
            {
                return query.Apply(null).OrderBy(e => e.score).Distinct();
                
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

        public override bool SkipEntry(string path, bool checkRoots = false)
        {
            if (checkRoots && settings.roots != null && settings.roots.Length > 0)
            {
                if (!settings.roots.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            if (!settings.options.directories && Directory.Exists(path))
                return true;

            if (!settings.options.files && File.Exists(path))
                return true;

            var ext = Path.GetExtension(path);

            // Exclude indexes by default
            if (ext.EndsWith("index", StringComparison.OrdinalIgnoreCase))
                return true;

            var dir = Path.GetDirectoryName(path);

            if (settings.includes?.Length > 0 && !settings.includes.Any(pattern => PatternChecks(pattern, ext, dir, path)))
                return true;

            if (settings.excludes?.Length > 0 && settings.excludes.Any(pattern => PatternChecks(pattern, ext, dir, path)))
                return true;

            return false;
        }

        public abstract List<string> GetDependencies();

        public abstract override void IndexDocument(string id, bool checkIfDocumentExists);

        protected void ReportProgress(int progressId, string value, float progressReport, bool finished)
        {
            reportProgress?.Invoke(progressId, value, progressReport, finished);
        }

        protected abstract System.Collections.IEnumerator BuildAsync(int progressId, object userData = null);

        private Query<SearchResult, object> BuildQuery(string searchQuery, int maxScore, int patternMatchLimit)
        {
            Query<SearchResult, object> query;
            if (m_QueryPool.TryGetValue(searchQuery, out query) && query.valid)
                return query;

            if (m_QueryPool.Count > 50)
                m_QueryPool.Clear();

            query = m_QueryEngine.Parse(searchQuery, new SearchIndexerQueryFactory(args =>
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

                    var results = SearchTerm(args.name, args.value, args.op, args.exclude, maxScore, subset, patternMatchLimit);
                    if (args.orSet != null)
                        results = results.Concat(args.orSet);

                    #if DEBUG_INDEXING
                    SearchIndexerQuery.EvalResult.Print(args, results, subset, t.timeMs);
                    #endif
                    return SearchIndexerQuery.EvalResult.Combined(results);
                }
            }));
            if (query.valid)
                m_QueryPool[searchQuery] = query;
            return query;
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

        private string[] GetComponents(string id, int documentIndex)
        {
            return getEntryComponentsHandler(id, documentIndex).Where(c => c.Length > 0).ToArray();
        }

        protected void IndexWordComponents(string id, int documentIndex, string word)
        {
            foreach (var c in GetComponents(word, documentIndex))
                IndexWord(id, c, documentIndex);
        }

        protected void IndexPropertyComponents(string id, int documentIndex, string name, string value)
        {
            foreach (var c in GetComponents(value, documentIndex))
                IndexProperty(id, name, c, documentIndex, saveKeyword: false);
        }

        [System.Diagnostics.Conditional("DEBUG_INDEXING")]
        protected void IndexDebugMatch(string id, string name, string value)
        {
            IndexDebugMatch(id, $"{name}:{value}");
        }

        [System.Diagnostics.Conditional("DEBUG_INDEXING")]
        protected void IndexDebugMatch(string id, string word)
        {
            #if DEBUG_INDEXING
            HashSet<string> words;
            if (m_DebugStringTable.TryGetValue(id, out words))
            {
                words.Add(word);
            }
            else
            {
                m_DebugStringTable[id] = new HashSet<string> { word };
            }
            #endif
        }

        protected void IndexWord(string id, string word, int documentIndex, int maxVariations, bool exact)
        {
            IndexDebugMatch(id, word);
            AddWord(word.ToLowerInvariant(), 2, maxVariations, settings.baseScore, documentIndex);
            if (exact)
                AddExactWord(word.ToLowerInvariant(), settings.baseScore-1, documentIndex);
        }

        protected void IndexWord(string id, string word, int documentIndex, bool exact = false)
        {
            IndexWord(id, word, documentIndex, word.Length, exact);
        }

        protected void IndexProperty(string id, string name, string value, int documentIndex, bool saveKeyword)
        {
            IndexProperty(id, name, value, documentIndex, saveKeyword, false);
        }

        protected void IndexProperty(string id, string name, string value, int documentIndex, bool saveKeyword, bool exact)
        {
            if (String.IsNullOrEmpty(value))
                return;
            IndexDebugMatch(id, name, value);
            if (exact)
                AddProperty(name, value.ToLowerInvariant(), value.Length, value.Length, settings.baseScore, documentIndex, saveKeyword);
            else
                AddProperty(name, value.ToLowerInvariant(), settings.baseScore, documentIndex, saveKeyword);
        }

        protected void IndexNumber(string id, string name, double number, int documentIndex)
        {
            IndexDebugMatch(id, name, number.ToString());
            AddNumber(name, number, settings.baseScore, documentIndex);
        }

        internal string GetDebugIndexStrings(string id)
        {
            #if DEBUG_INDEXING
            if (!m_DebugStringTable.ContainsKey(id))
                return null;

            return String.Join(" ", m_DebugStringTable[id].ToArray());
            #else
            return null;
            #endif
        }

        protected object GetPropertyValue(SerializedProperty property, ref bool saveKeyword)
        {
            object fieldValue = null;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    fieldValue = (double)property.intValue;
                    break;
                case SerializedPropertyType.Boolean:
                    fieldValue = property.boolValue.ToString();
                    break;
                case SerializedPropertyType.Float:
                    fieldValue = (double)property.floatValue;
                    break;
                case SerializedPropertyType.String:
                    if (property.stringValue != null && property.stringValue.Length < 10)
                        fieldValue = property.stringValue.Replace(" ", "").ToString();
                    break;
                case SerializedPropertyType.Enum:
                    if (property.enumValueIndex >= 0 && property.type == "Enum")
                        fieldValue = property.enumNames[property.enumValueIndex].ToString();
                    break;
                case SerializedPropertyType.ObjectReference:
                    if (property.objectReferenceValue)
                    {
                        saveKeyword = false;
                        fieldValue = property.objectReferenceValue.name.Replace(" ", "");
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

        protected void IndexObject(string id, Object obj, int documentIndex)
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
                                IndexProperty(id, fcc, sfv.Replace(" ", "").ToLowerInvariant(), documentIndex, saveKeyword);
                            else
                                IndexWord(id, $"@{fcc}", documentIndex);
                        }
                        else if (fieldValue is double)
                        {
                            var nfv = (double)fieldValue;
                            IndexNumber(id, fcc.ToLowerInvariant(), nfv, documentIndex);
                        }

                        IndexDebugMatch(id, fcc, fieldValue.ToString());
                    }
                    next = p.Next(false);
                }
            }
        }
    }
}

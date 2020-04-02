using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEditor;

namespace Unity.QuickSearch
{
    [DebuggerDisplay("{type} - K:{key}|{number} - C:{crc} - I:{index}")]
    readonly struct SearchIndexEntry : IEquatable<SearchIndexEntry>
    {
        // 1- Initial format
        // 2- Added score to words
        // 3- Save base name in entry paths
        // 4- Added entry types
        // 5- Added indexing tags
        // 6- Revert indexes back to 32 bits instead of 64 bits.
        // 7- Remove min and max char variations.
        // 8- Add metadata field to documents
        internal const int version = 0x4242E000 | 0x008;

        public enum Type : int
        {
            Undefined = 0,
            Word,
            Number,
            Property
        }

        public readonly long key;                   // Value hash
        public readonly int crc;                    // Value correction code (can be length, property key hash, etc.)
        public readonly Type type;  // Type of the index entry
        public readonly int index;                  // Index of documents in the documents array
        public readonly int score;
        public readonly double number;

        public SearchIndexEntry(long _key, int _crc, Type _type, int _index = -1, int _score = int.MaxValue)
        {
            key = _key;
            crc = _crc;
            type = _type;
            index = _index;
            score = _score;
            number = BitConverter.Int64BitsToDouble(key);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return key.GetHashCode() ^ crc.GetHashCode() ^ type.GetHashCode() ^ index.GetHashCode();
            }
        }

        public override bool Equals(object other)
        {
            return other is SearchIndexEntry l && Equals(l);
        }

        public bool Equals(SearchIndexEntry other)
        {
            return key == other.key && crc == other.crc && type == other.type && index == other.index;
        }
    }

    [DebuggerDisplay("{id}[{index}] ({score})")]
    public readonly struct SearchResult : IEquatable<SearchResult>, IComparable<SearchResult>
    {
        public readonly string id;
        public readonly int index;
        public readonly int score;

        public SearchResult(string id, int index, int score)
        {
            this.id = id;
            this.index = index;
            this.score = score;
        }

        public SearchResult(int index)
        {
            this.id = null;
            this.score = 0;
            this.index = index;
        }

        public SearchResult(int index, int score)
        {
            this.id = null;
            this.index = index;
            this.score = score;
        }

        internal SearchResult(in SearchIndexEntry entry)
        {
            this.id = null;
            this.index = entry.index;
            this.score = entry.score;
        }

        public bool Equals(SearchResult other)
        {
            return index == other.index;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return index.GetHashCode();
            }
        }

        public override bool Equals(object other)
        {
            return other is SearchResult l && Equals(l);
        }

        public int CompareTo(SearchResult other)
        {
            return index.CompareTo(other.index);
        }
    }

    [DebuggerDisplay("{baseName} ({basePath})")]
    public readonly struct SearchIndexerRoot
    {
        public readonly string basePath;
        public readonly string baseName;

        public SearchIndexerRoot(string _p, string _n)
        {
            basePath = _p.Replace('\\', '/');
            baseName = _n;
        }
    }

    internal class SearchDocument
    {
        public SearchDocument(string id, string metadata = null)
        {
            this.id = id;
            this.metadata = metadata;
        }

        public string id;
        public string metadata;
    }

    public class SearchIndexer
    {
        public string name { get; set; }
        public SearchIndexerRoot[] roots { get; }
        public char[] entrySeparators { get; set; } = SearchUtils.entrySeparators;

        internal int keywordCount => m_Keywords.Count;
        internal int documentCount => m_Documents.Count;
        internal int indexCount
        {
            get
            {
                lock (this)
                {
                    int total = 0;
                    if (m_Indexes != null && m_Indexes.Length > 0)
                        total += m_Indexes.Length;
                    if (m_BatchIndexes != null && m_BatchIndexes.Count > 0)
                        total += m_BatchIndexes.Count;
                    return total;
                }
            }
        }

        internal IEnumerable<string> GetKeywords() { lock(this) return m_Keywords; }
        internal IEnumerable<string> GetDocuments() { lock(this) return m_Documents.Select(d => d.id); }
        internal SearchDocument GetDocument(int index) { lock(this) return m_Documents[index]; }

        internal Dictionary<int, int> patternMatchCount { get; set; } = new Dictionary<int, int>();

        // Handler used to skip some entries.
        public Func<string, bool> skipEntryHandler { get; set; }

        // Handler used to specify where the index database file should be saved. If the handler returns null, the database won't be saved at all.
        public Func<string, string> getIndexFilePathHandler { get; set; }

        // Handler used to parse and split the search query text into words. The tokens needs to be split similarly to how getEntryComponentsHandler was specified.
        public Func<string, string[]> getQueryTokensHandler { get; set; }

        // Handler used to split into words the entries. The order of the words matter. Words at the beginning of the array have a lower score (lower the better)
        public Func<string, int, IEnumerable<string>> getEntryComponentsHandler { get; set; }

        // Handler used to fetch all the entries under a given root.
        public Func<SearchIndexerRoot, IEnumerable<string>> enumerateRootEntriesHandler { get; set; }

        private Thread m_IndexerThread;
        private volatile bool m_IndexReady = false;
        private volatile bool m_ThreadAborted = false;
        private string m_IndexTempFilePath;
        private Dictionary<RangeSet, IndexRange> m_FixedRanges = new Dictionary<RangeSet, IndexRange>();

        // Final documents and entries when the index is ready.
        private List<SearchDocument> m_Documents;
        private SearchIndexEntry[] m_Indexes;
        protected HashSet<string> m_Keywords;

        // Temporary documents and entries while the index is being built (i.e. Start/Finish).
        private List<SearchIndexEntry> m_BatchIndexes;

        public SearchIndexer(string rootPath)
            : this(rootPath, String.Empty)
        {
        }

        public SearchIndexer(string rootPath, string rootName)
            : this(new[] { new SearchIndexerRoot(rootPath, rootName) })
        {
        }

        public SearchIndexer(IEnumerable<SearchIndexerRoot> roots)
        {
            this.roots = roots.ToArray();

            skipEntryHandler = e => false;
            getIndexFilePathHandler = p => null;
            getEntryComponentsHandler = (e, i) => throw new Exception("You need to specify the get entry components handler");
            enumerateRootEntriesHandler = r => throw new Exception("You need to specify the root entries enumerator");
            getQueryTokensHandler = ParseQuery;

            m_Keywords = new HashSet<string>();
            m_Documents = new List<SearchDocument>();
            m_Indexes = new SearchIndexEntry[0];
            m_IndexTempFilePath = Path.GetTempFileName();
        }

        public virtual void Build()
        {
            Build(true);
        }

        public void Build(bool useThread)
        {
            if (useThread)
            {
                m_ThreadAborted = false;
                m_IndexerThread = new Thread(() =>
                {
                    try
                    {
                        using (new IndexerThreadScope(AbortIndexing))
                            Build(false);
                    }
                    catch (ThreadAbortException)
                    {
                        m_ThreadAborted = true;
                        Thread.ResetAbort();
                    }
                });
                m_IndexerThread.Start();
            }
            else
            {
                BuildWordIndexes();
            }
        }

        private void FetchEntries(string document, int documentIndex, List<SearchIndexEntry> indexes, int baseScore = 0)
        {
            var components = getEntryComponentsHandler(document, documentIndex).ToArray();
            for (int compIndex = 0; compIndex < components.Length; ++compIndex)
            {
                var p = components[compIndex];
                if (p.Length == 0)
                    continue;

                AddWord(p, 2, p.Length, baseScore + compIndex, documentIndex, indexes);
            }
        }

        internal int AddDocument(string document, bool checkIfExists = true)
        {
            return AddDocument(document, null, checkIfExists);
        }

        internal int AddDocument(string document, string metadata, bool checkIfExists = true)
        {
            // Reformat entry to have them all uniformized.
            if (skipEntryHandler(document))
                return -1;

            lock (this)
            {
                if (checkIfExists)
                {
                    var di = m_Documents.FindIndex(d => d.id == document);
                    if (di >= 0)
                    {
                        m_Documents[di].metadata = metadata;
                        return di;
                    }
                }
                m_Documents.Add(new SearchDocument(document, metadata));
                return m_Documents.Count - 1;
            }
        }

        internal void AddWord(string word, int score, int documentIndex)
        {
            lock (this)
                AddWord(word, 2, word.Length, score, documentIndex, m_BatchIndexes);
        }

        internal void AddWord(string word, int score, int documentIndex, List<SearchIndexEntry> indexes)
        {
            AddWord(word, 2, word.Length, score, documentIndex, indexes);
        }

        internal void AddWord(string word, int size, int score, int documentIndex)
        {
            lock (this)
                AddWord(word, size, size, score, documentIndex, m_BatchIndexes);
        }

        internal void AddExactWord(string word, int score, int documentIndex)
        {
            lock (this)
                AddExactWord(word, score, documentIndex, m_BatchIndexes);
        }

        internal void AddExactWord(string word, int score, int documentIndex, List<SearchIndexEntry> indexes)
        {
            indexes.Add(new SearchIndexEntry(word.GetHashCode(), int.MaxValue, SearchIndexEntry.Type.Word, documentIndex, score));
        }

        internal void AddWord(string word, int minVariations, int maxVariations, int score, int documentIndex)
        {
            lock (this)
                AddWord(word, minVariations, maxVariations, score, documentIndex, m_BatchIndexes);
        }

        internal void AddWord(string word, int minVariations, int maxVariations, int score, int documentIndex, List<SearchIndexEntry> indexes)
        {
            if (word == null || word.Length == 0)
                return;

            if (word[0] == '@')
            {
                word = word.Substring(1);
                var vpPos = word.IndexOf(':');
                if (vpPos != -1)
                    minVariations = vpPos + 2;
                else
                    minVariations = word.Length;
            }

            maxVariations = Math.Min(maxVariations, word.Length);

            for (int c = Math.Min(minVariations, maxVariations); c <= maxVariations; ++c)
            {
                var ss = word.Substring(0, c);
                indexes.Add(new SearchIndexEntry(ss.GetHashCode(), ss.Length, SearchIndexEntry.Type.Word, documentIndex, score));
            }

            if (word.Length > maxVariations)
                indexes.Add(new SearchIndexEntry(word.GetHashCode(), word.Length, SearchIndexEntry.Type.Word, documentIndex, score-1));
        }

        internal void AddNumber(string key, double value, int score, int documentIndex)
        {
            lock (this)
                AddNumber(key, value, score, documentIndex, m_BatchIndexes);
        }

        private bool ExcludeWordVariations(string word)
        {
            if (word == "true" || word == "false")
                return true;
            return false;
        }

        internal void AddProperty(string key, string value, int documentIndex, bool saveKeyword)
        {
            lock (this)
                AddProperty(key, value, 2, value.Length, 0, documentIndex, m_BatchIndexes, saveKeyword);
        }

        internal void AddProperty(string key, string value, int score, int documentIndex, bool saveKeyword)
        {
            lock (this)
                AddProperty(key, value, 2, value.Length, score, documentIndex, m_BatchIndexes, saveKeyword);
        }

        internal void AddProperty(string name, string value, int minVariations, int maxVariations, int score, int documentIndex, bool saveKeyword)
        {
            lock (this)
                AddProperty(name, value, minVariations, maxVariations, score, documentIndex, m_BatchIndexes, saveKeyword);
        }

        internal void AddProperty(string name, string value, int minVariations, int maxVariations, int score, int documentIndex, List<SearchIndexEntry> indexes, bool saveKeyword)
        {
            var nameHash = name.GetHashCode();
            var valueHash = value.GetHashCode();
            maxVariations = Math.Min(maxVariations, value.Length);
            if (minVariations > value.Length)
                minVariations = value.Length;
            if (ExcludeWordVariations(value))
                minVariations = maxVariations = value.Length;
            for (int c = Math.Min(minVariations, maxVariations); c <= maxVariations; ++c)
            {
                var ss = value.Substring(0, c);
                indexes.Add(new SearchIndexEntry(ss.GetHashCode(), nameHash, SearchIndexEntry.Type.Property, documentIndex, score + (maxVariations - c)));
            }

            if (value.Length > maxVariations)
                indexes.Add(new SearchIndexEntry(valueHash, nameHash, SearchIndexEntry.Type.Property, documentIndex, score-1));

            // Add an exact match for property="match"
            nameHash = nameHash ^ name.Length.GetHashCode();
            valueHash = value.GetHashCode() ^ value.Length.GetHashCode();
            indexes.Add(new SearchIndexEntry(valueHash, nameHash, SearchIndexEntry.Type.Property, documentIndex, score));

            if (saveKeyword)
                m_Keywords.Add($"{name}:{value}");
        }

        internal void AddNumber(string key, double value, int score, int documentIndex, List<SearchIndexEntry> indexes)
        {
            var keyHash = key.GetHashCode();
            var longNumber = BitConverter.DoubleToInt64Bits(value);
            indexes.Add(new SearchIndexEntry(longNumber, keyHash, SearchIndexEntry.Type.Number, documentIndex, score));

            m_Keywords.Add($"{key}:");
        }

        internal void Start(bool clear = false)
        {
            lock (this)
            {
                m_IndexerThread = null;
                m_ThreadAborted = false;
                m_IndexReady = false;
                m_BatchIndexes = new List<SearchIndexEntry>();
                m_FixedRanges.Clear();
                patternMatchCount.Clear();

                if (clear)
                {
                    m_Keywords.Clear();
                    m_Documents.Clear();
                    m_Indexes = new SearchIndexEntry[0];
                }
            }
        }

        internal void Finish(Action threadCompletedCallback, string[] removedDocuments = null)
        {
            m_ThreadAborted = false;
            m_IndexerThread = new Thread(() =>
            {
                try
                {
                    using (new IndexerThreadScope(AbortIndexing))
                    {
                        Finish(removedDocuments);
                        Dispatcher.Enqueue(threadCompletedCallback);
                    }
                }
                catch (ThreadAbortException)
                {
                    m_ThreadAborted = true;
                    Thread.ResetAbort();
                }
            });
            m_IndexerThread.Start();
        }

        internal void Finish(string[] removedDocuments = null)
        {
            lock (this)
            {
                var shouldRemoveDocuments = removedDocuments != null && removedDocuments.Length > 0;
                if (shouldRemoveDocuments)
                {
                    var removedDocIndexes = new HashSet<int>();
                    foreach (var rd in removedDocuments)
                    {
                        var di = m_Documents.FindIndex(d => d.id == rd);
                        if (di > -1)
                            removedDocIndexes.Add(di);
                    }
                    m_BatchIndexes.AddRange(m_Indexes.Where(e => !removedDocIndexes.Contains(e.index)));
                }
                else
                {
                    m_BatchIndexes.AddRange(m_Indexes);
                }
                UpdateIndexes(m_Documents, m_BatchIndexes, roots[0].basePath);
                m_BatchIndexes.Clear();
            }
        }

        internal void Print()
        {
            #if UNITY_2020_1_OR_NEWER
            foreach (var i in m_Indexes)
            {
                UnityEngine.Debug.LogFormat(UnityEngine.LogType.Log, UnityEngine.LogOption.NoStacktrace, null,
                    $"{i.type} - {i.crc} - {i.key} - {i.index} - {i.score}");
            }
            #endif
        }

        public bool IsReady()
        {
            return m_IndexReady;
        }

        private IEnumerable<SearchResult> SearchWord(string word, SearchIndexOperator op, int maxScore, SearchResultCollection subset, int patternMatchLimit)
        {
            var comparer = new SearchIndexComparer(op);
            int crc = word.Length;
            if (op == SearchIndexOperator.Equal)
                crc = int.MaxValue;
            return SearchIndexes(word.GetHashCode(), crc, SearchIndexEntry.Type.Word, maxScore, comparer, subset, patternMatchLimit);
        }

        private IEnumerable<SearchResult> ExcludeWord(string word, SearchIndexOperator op, SearchResultCollection subset)
        {
            if (subset == null)
                subset = GetAllDocumentIndexesSet();

            var includedDocumentIndexes = new SearchResultCollection(SearchWord(word, op, int.MaxValue, null, int.MaxValue));
            return subset.Where(d => !includedDocumentIndexes.Contains(d));
        }

        private IEnumerable<SearchResult> ExcludeProperty(string name, string value, SearchIndexOperator op, int maxScore, SearchResultCollection subset, int limit)
        {
            if (subset == null)
                subset = GetAllDocumentIndexesSet();

            var includedDocumentIndexes = new SearchResultCollection(SearchProperty(name, value, op, int.MaxValue, null, int.MaxValue));
            return subset.Where(d => !includedDocumentIndexes.Contains(d));
        }

        private IEnumerable<SearchResult> SearchProperty(string name, string value, SearchIndexOperator op, int maxScore, SearchResultCollection subset, int patternMatchLimit)
        {
            var comparer = new SearchIndexComparer(op);
            var valueHash = value.GetHashCode();
            var nameHash = name.GetHashCode();
            if (comparer.op == SearchIndexOperator.Equal)
            {
                nameHash ^= name.Length.GetHashCode();
                valueHash ^= value.Length.GetHashCode();
            }

            return SearchIndexes(valueHash, nameHash, SearchIndexEntry.Type.Property, maxScore, comparer, subset, patternMatchLimit);
        }

        private SearchResultCollection m_AllDocumentIndexes;
        private SearchResultCollection GetAllDocumentIndexesSet()
        {
            if (m_AllDocumentIndexes != null)
                return m_AllDocumentIndexes;
            m_AllDocumentIndexes = new SearchResultCollection();
            for (int i = 0; i < documentCount; ++i)
                m_AllDocumentIndexes.Add(new SearchResult(i, 0));
            return m_AllDocumentIndexes;
        }

        private IEnumerable<SearchResult> ExcludeNumber(string name, double number, SearchIndexOperator op, SearchResultCollection subset)
        {
            if (subset == null)
                subset = GetAllDocumentIndexesSet();

            var includedDocumentIndexes = new SearchResultCollection(SearchNumber(name, number, op, int.MaxValue, null).Select(m => new SearchResult(m.index, m.score)));
            return subset.Where(d => !includedDocumentIndexes.Contains(d));
        }

        private IEnumerable<SearchResult> SearchNumber(string key, double value, SearchIndexOperator op, int maxScore, SearchResultCollection subset)
        {
            var wiec = new SearchIndexComparer(op);
            return SearchIndexes(BitConverter.DoubleToInt64Bits(value), key.GetHashCode(), SearchIndexEntry.Type.Number, maxScore, wiec, subset);
        }

        public virtual IEnumerable<SearchResult> Search(string query, int maxScore = int.MaxValue, int patternMatchLimit = 2999)
        {
            //using (new DebugTimer($"Search Index ({query})"))
            {
                if (!m_IndexReady)
                    return Enumerable.Empty<SearchResult>();

                var tokens = getQueryTokensHandler(query);
                Array.Sort(tokens, SortTokensByPatternMatches);

                var lengths = tokens.Select(p => p.Length).ToArray();
                var patterns = tokens.Select(p => p.GetHashCode()).ToArray();

                if (patterns.Length == 0)
                    return Enumerable.Empty<SearchResult>();

                var wiec = new SearchIndexComparer();
                lock (this)
                {
                    var remains = SearchIndexes(patterns[0], lengths[0], SearchIndexEntry.Type.Word, maxScore, wiec, null, patternMatchLimit).ToList();
                    patternMatchCount[patterns[0]] = remains.Count;

                    if (remains.Count == 0)
                        return Enumerable.Empty<SearchResult>();

                    for (int i = 1; i < patterns.Length; ++i)
                    {
                        var subset = new SearchResultCollection(remains);
                        remains = SearchIndexes(patterns[i], lengths[i], SearchIndexEntry.Type.Word, maxScore, wiec, subset, patternMatchLimit).ToList();
                        if (remains.Count == 0)
                            break;
                    }

                    return remains.Select(fi => new SearchResult(m_Documents[fi.index].id, fi.index, fi.score));
                }
            }
        }

        readonly char[] k_OpCharacters = new char[] { ':', '=', '<', '>', '!' };
        public IEnumerable<SearchResult> SearchTerms(string query, int maxScore = int.MaxValue, int patternMatchLimit = 2999)
        {
            if (!m_IndexReady)
                return Enumerable.Empty<SearchResult>();

            var tokens = getQueryTokensHandler(query);
            if (tokens.Length == 0)
                return Enumerable.Empty<SearchResult>();
            Array.Sort(tokens, SortTokensByPatternMatches);

            //using (new DebugTimer($"Search Terms ({String.Join(", ", tokens)})"))
            {
                SearchResultCollection subset = null;
                var results = new List<SearchResult>();

                lock (this)
                {
                    for (int tokenIndex = 0; tokenIndex < tokens.Length; ++tokenIndex)
                    {
                        var token = tokens[tokenIndex];
                        if (token.Length < 2)
                            continue;

                        var opEndSepPos = token.LastIndexOfAny(k_OpCharacters);
                        if (opEndSepPos > 0)
                        {
                            // Search property
                            var opBeginSepPos = token.IndexOfAny(k_OpCharacters);
                            if (opBeginSepPos > opEndSepPos || opEndSepPos == token.Length - 1)
                                continue;
                            var name = token.Substring(0, opBeginSepPos);
                            var value = token.Substring(opEndSepPos + 1);
                            var opString = token.Substring(opBeginSepPos, opEndSepPos - opBeginSepPos + 1);
                            var op = SearchIndexOperator.Contains;

                            switch (opString)
                            {
                                case "=": op = SearchIndexOperator.Equal; break;
                                case ">": op = SearchIndexOperator.Greater; break;
                                case ">=": op = SearchIndexOperator.GreaterOrEqual; break;
                                case "<": op = SearchIndexOperator.Less; break;
                                case "<=": op = SearchIndexOperator.LessOrEqual; break;
                                case "!=": op = SearchIndexOperator.NotEqual; break;
                                default: // :, etc.
                                    op = SearchIndexOperator.Contains;
                                    break;
                            }

                            double number;
                            if (double.TryParse(value, out number))
                                results = SearchNumber(name, number, op, maxScore, subset).ToList();
                            else
                                results = SearchProperty(name, value, op, maxScore, subset, patternMatchLimit).ToList();
                        }
                        else
                        {
                            // Search word
                            var word = token;
                            var op = SearchIndexOperator.Contains;
                            if (word[0] == '!')
                            {
                                word = word.Substring(1);
                                op = SearchIndexOperator.Equal;
                            }

                            results = SearchWord(word, op, maxScore, subset, patternMatchLimit).ToList();
                        }

                        if (tokenIndex == 0 && results.Count == 0)
                            patternMatchCount[token.GetHashCode()] = results.Count;

                        if (subset == null)
                            subset = new SearchResultCollection(results);
                    }
                }

                return results.Select(r => new SearchResult(m_Documents[r.index].id, r.index, r.score));
            }
        }

        internal IEnumerable<SearchResult> SearchTerm(
            string name, object value, SearchIndexOperator op, bool exclude,
            int maxScore = int.MaxValue, SearchResultCollection subset = null, int limit = int.MaxValue)
        {
            if (op == SearchIndexOperator.NotEqual)
            {
                exclude = true;
                op = SearchIndexOperator.Equal;
            }

            IEnumerable<SearchResult> matches = null;
            if (!String.IsNullOrEmpty(name))
            {
                name = name.ToLowerInvariant();

                // Search property
                double number;
                if (value is double)
                {
                    number = (double)value;
                    matches = SearchNumber(name, number, op, maxScore, subset);
                }
                else if (value is string)
                {
                    var valueString = (string)value;
                    if (double.TryParse(valueString, out number))
                    {
                        if (!exclude && op != SearchIndexOperator.NotEqual)
                            matches = SearchNumber(name, number, op, maxScore, subset);
                        else
                            matches = ExcludeNumber(name, number, op, subset);
                    }
                    else
                    {
                        if (!exclude)
                            matches = SearchProperty(name, valueString.ToLowerInvariant(), op, maxScore, subset, limit);
                        else
                            matches = ExcludeProperty(name, valueString.ToLowerInvariant(), op, maxScore, subset, limit);
                    }
                }
                else
                    throw new ArgumentException($"value must be a number or a string", nameof(value));
            }
            else if (value is string)
            {
                // Search word
                if (!exclude)
                    matches = SearchWord((string)value, op, maxScore, subset, limit);
                else
                    matches = ExcludeWord((string)value, op, subset);
            }
            else
                throw new ArgumentException($"word value must be a string", nameof(value));

            if (matches == null)
                return null;
            return matches.Select(r => new SearchResult(m_Documents[r.index].id, r.index, r.score));
        }

        private int SortTokensByPatternMatches(string item1, string item2)
        {
            patternMatchCount.TryGetValue(item1.GetHashCode(), out var item1PatternMatchCount);
            patternMatchCount.TryGetValue(item2.GetHashCode(), out var item2PatternMatchCount);
            var c = item1PatternMatchCount.CompareTo(item2PatternMatchCount);
            if (c != 0)
                return c;
            return item1.Length.CompareTo(item2.Length);
        }

        public void Write(Stream stream, string tag)
        {
            using (var indexWriter = new BinaryWriter(stream))
            {
                indexWriter.Write(SearchIndexEntry.version);
                indexWriter.Write(tag);

                // Documents
                indexWriter.Write(m_Documents.Count);
                foreach (var p in m_Documents)
                {
                    indexWriter.Write(p.id);

                    bool writeMetadata = !String.IsNullOrEmpty(p.metadata);
                    indexWriter.Write(writeMetadata);
                    if (writeMetadata)
                        indexWriter.Write(p.metadata);
                }

                // Indexes
                indexWriter.Write(m_Indexes.Length);
                foreach (var p in m_Indexes)
                {
                    indexWriter.Write(p.key);
                    indexWriter.Write(p.crc);
                    indexWriter.Write((int)p.type);
                    indexWriter.Write(p.index);
                    indexWriter.Write(p.score);
                }

                // Keywords
                indexWriter.Write(m_Keywords.Count);
                foreach (var t in m_Keywords)
                    indexWriter.Write(t);
            }
        }

        public byte[] SaveBytes()
        {
            using (var memoryStream = new MemoryStream())
            {
                Write(memoryStream, "memory");
                return memoryStream.ToArray();
            }
        }

        private void SaveIndexToDisk(string basePath)
        {
            var indexFilePath = getIndexFilePathHandler(basePath);
            if (String.IsNullOrEmpty(indexFilePath))
                return;

            using (var fileStream = new FileStream(m_IndexTempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                Write(fileStream, basePath);

            try
            {
                try
                {
                    if (File.Exists(indexFilePath))
                        File.Delete(indexFilePath);
                }
                catch (IOException)
                {
                    // ignore file index persistence operation, since it is not critical and will redone later.
                }

                File.Move(m_IndexTempFilePath, indexFilePath);
            }
            catch (IOException)
            {
                // ignore file index persistence operation, since it is not critical and will redone later.
            }
        }

        public bool Read(Stream stream, bool checkVersionOnly)
        {
            using (var indexReader = new BinaryReader(stream))
            {
                int version = indexReader.ReadInt32();
                if (version != SearchIndexEntry.version)
                    return false;

                if (checkVersionOnly)
                    return true;

                indexReader.ReadString(); // Skip

                // Documents
                var elementCount = indexReader.ReadInt32();
                var documents = new SearchDocument[elementCount];
                for (int i = 0; i < elementCount; ++i)
                {
                    documents[i] = new SearchDocument(indexReader.ReadString());
                    bool readMetadata = indexReader.ReadBoolean();
                    if (readMetadata)
                        documents[i].metadata = indexReader.ReadString();
                }

                // Indexes
                elementCount = indexReader.ReadInt32();
                var indexes = new List<SearchIndexEntry>(elementCount);
                for (int i = 0; i < elementCount; ++i)
                {
                    var key = indexReader.ReadInt64();
                    var crc = indexReader.ReadInt32();
                    var type = (SearchIndexEntry.Type)indexReader.ReadInt32();
                    var index = indexReader.ReadInt32();
                    var score = indexReader.ReadInt32();
                    indexes.Add(new SearchIndexEntry(key, crc, type, index, score));
                }

                // Keywords
                elementCount = indexReader.ReadInt32();
                var keywords = new string[elementCount];
                for (int i = 0; i < elementCount; ++i)
                    keywords[i] = indexReader.ReadString();

                // No need to sort the index, it is already sorted in the file stream.
                lock (this)
                {
                    ApplyIndexes(documents, indexes.ToArray());
                    m_Keywords = new HashSet<string>(keywords);
                }

                return true;
            }
        }

        public bool LoadBytes(byte[] bytes, bool checkVersionOnly = false)
        {
            using (var memoryStream = new MemoryStream(bytes))
                return Read(memoryStream, checkVersionOnly);
        }

        internal bool ReadIndexFromDisk(string indexFilePath, bool checkVersionOnly = false)
        {
            lock (this)
            {
                using (var fileStream = new FileStream(indexFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    return Read(fileStream, checkVersionOnly);
            }
        }

        internal bool LoadIndexFromDisk(string basePath, bool useThread = false)
        {
            var indexFilePath = getIndexFilePathHandler(basePath);
            if (indexFilePath == null || !File.Exists(indexFilePath))
                return false;

            if (useThread)
            {
                if (!ReadIndexFromDisk(indexFilePath, true))
                    return false;

                var t = new Thread(() => ReadIndexFromDisk(indexFilePath));
                t.Start();
                return t.ThreadState != System.Threading.ThreadState.Unstarted;
            }

            return ReadIndexFromDisk(indexFilePath);
        }

        private void AbortIndexing()
        {
            if (m_IndexReady)
                return;

            m_ThreadAborted = true;
        }

        private void UpdateIndexes(IEnumerable<SearchDocument> documents, List<SearchIndexEntry> entries, string saveIndexBasePath = null)
        {
            if (entries == null)
                return;

            lock (this)
            {
                m_IndexReady = false;
                var comparer = new SearchIndexComparer();

                try
                {
                    // Sort word indexes to run quick binary searches on them.
                    entries.Sort(comparer);
                    ApplyIndexes(documents, entries.Distinct(comparer).ToArray());
                }
                catch
                {
                    // This can happen while a domain reload is happening.
                    return;
                }

                if (!String.IsNullOrEmpty(saveIndexBasePath))
                    SaveIndexToDisk(saveIndexBasePath);
            }
        }

        private void ApplyIndexes(IEnumerable<SearchDocument> documents, SearchIndexEntry[] entries)
        {
            m_Documents = documents.ToList();
            m_Indexes = entries;
            m_IndexReady = true;
        }

        private void BuildWordIndexes()
        {
            if (roots.Length == 0)
                return;

            lock (this)
                LoadIndexFromDisk(roots[0].basePath);

            int entryStart = 0;
            var documents = new List<SearchDocument>();
            var wordIndexes = new List<SearchIndexEntry>();

            var baseScore = 0;
            foreach (var r in roots)
            {
                if (m_ThreadAborted)
                    return;

                var rootName = r.baseName;
                var basePath = r.basePath;
                var basePathWithSlash = basePath + "/";

                if (!String.IsNullOrEmpty(rootName))
                    rootName = rootName + "/";

                // Fetch entries to be indexed and compiled.
                documents.AddRange(enumerateRootEntriesHandler(r).Select(id => new SearchDocument(rootName + id)));
                BuildPartialIndex(wordIndexes, basePathWithSlash, entryStart, documents, baseScore);

                entryStart = documents.Count;
                baseScore = 100;
            }

            UpdateIndexes(documents, wordIndexes, roots[0].basePath);
        }

        private List<SearchIndexEntry> BuildPartialIndex(string basis, int entryStartIndex, IList<SearchDocument> entries, int baseScore)
        {
            var wordIndexes = new List<SearchIndexEntry>(entries.Count * 3);
            BuildPartialIndex(wordIndexes, basis, entryStartIndex, entries, baseScore);
            return wordIndexes;
        }

        private void BuildPartialIndex(List<SearchIndexEntry> wordIndexes, string basis, int entryStartIndex, IList<SearchDocument> entries, int baseScore)
        {
            int entryCount = entries.Count - entryStartIndex;
            for (int i = entryStartIndex; i != entries.Count; ++i)
            {
                if (m_ThreadAborted)
                    break;

                if (String.IsNullOrEmpty(entries[i].id))
                    continue;

                // Reformat entry to have them all uniformized.
                if (!String.IsNullOrEmpty(basis))
                    entries[i].id = entries[i].id.Replace('\\', '/').Replace(basis, "");

                var path = entries[i].id;
                if (skipEntryHandler(path))
                    continue;

                FetchEntries(path, i, wordIndexes, baseScore);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool NumberCompare(SearchIndexOperator op, double d1, double d2)
        {
            if (op == SearchIndexOperator.Equal)
                return d1 == d2;
            if (op == SearchIndexOperator.Contains)
                return UnityEngine.Mathf.Approximately((float)d1, (float)d2);
            if (op == SearchIndexOperator.Greater)
                return d1 > d2;
            if (op == SearchIndexOperator.GreaterOrEqual)
                return d1 >= d2;
            if (op == SearchIndexOperator.Less)
                return d1 < d2;
            if (op == SearchIndexOperator.LessOrEqual)
                return d1 <= d2;

            return false;
            //throw new NotImplementedException($"Search index compare strategy {op} for number not defined.");
        }

        private bool Rewind(int foundIndex, in SearchIndexEntry term, SearchIndexOperator op)
        {
            if (foundIndex <= 0)
                return false;

            var prevEntry =  m_Indexes[foundIndex - 1];
            if (prevEntry.crc != term.crc || prevEntry.type != term.type)
                return false;

            if (term.type == SearchIndexEntry.Type.Number)
                return NumberCompare(op, prevEntry.number, term.number);

            return prevEntry.key == term.key;
        }

        private bool Advance(int foundIndex, in SearchIndexEntry term, SearchIndexOperator op)
        {
            if (foundIndex < 0 || foundIndex >= m_Indexes.Length ||
                    m_Indexes[foundIndex].crc != term.crc || m_Indexes[foundIndex].type != term.type)
                return false;

            if (term.type == SearchIndexEntry.Type.Number)
                return NumberCompare(op, m_Indexes[foundIndex].number, term.number);

            return m_Indexes[foundIndex].key == term.key;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Lower(ref int foundIndex, in SearchIndexEntry term, SearchIndexOperator op)
        {
            if (op == SearchIndexOperator.Less || op == SearchIndexOperator.LessOrEqual)
            {
                var cont = !Advance(foundIndex, term, op);
                if (cont)
                    foundIndex--;
                return IsIndexValid(foundIndex, term.key, term.type) && cont;
            }

            {
                var cont = Rewind(foundIndex, term, op);
                if (cont)
                    foundIndex--;
                return cont;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Upper(ref int foundIndex, in SearchIndexEntry term, SearchIndexOperator op)
        {
            if (op == SearchIndexOperator.Less || op == SearchIndexOperator.LessOrEqual)
            {
                var cont = Rewind(foundIndex, term, op);
                if (cont)
                    foundIndex--;
                return IsIndexValid(foundIndex, term.crc, term.type) && cont;
            }

            return Advance(++foundIndex, term, op);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsIndexValid(int foundIndex, long crc, SearchIndexEntry.Type type)
        {
            return foundIndex >= 0 && foundIndex < m_Indexes.Length && m_Indexes[foundIndex].crc == crc && m_Indexes[foundIndex].type == type;
        }

        struct IndexRange
        {
            public readonly int start;
            public readonly int end;

            public IndexRange(int s, int e)
            {
                start = s;
                end = e;
            }

            public bool valid => start != -1;

            public static IndexRange Invalid = new IndexRange(-1, -1);
        }

        private IndexRange FindRange(in SearchIndexEntry term, SearchIndexComparer comparer)
        {
            // Find a first match in the sorted indexes.
            int foundIndex = Array.BinarySearch(m_Indexes, term, comparer);
            if (foundIndex < 0 && comparer.op != SearchIndexOperator.Contains && comparer.op != SearchIndexOperator.Equal)
            {
                // Potential range insertion, only used for not exact matches
                foundIndex = (-foundIndex) - 1;
            }

            if (!IsIndexValid(foundIndex, term.crc, term.type))
                return IndexRange.Invalid;

            // Rewind to first element
            while (Lower(ref foundIndex, term, comparer.op))
                ;

            if (!IsIndexValid(foundIndex, term.crc, term.type))
                return IndexRange.Invalid;

            int startRange = foundIndex;

            // Advance to last matching element
            while (Upper(ref foundIndex, term, comparer.op))
                ;

            return new IndexRange(startRange, foundIndex);
        }

        readonly struct RangeSet : IEquatable<RangeSet>
        {
            public readonly SearchIndexEntry.Type type;
            public readonly int crc;

            public RangeSet(SearchIndexEntry.Type type, int crc)
            {
                this.type = type;
                this.crc = crc;
            }

            public override int GetHashCode() => (type, crc).GetHashCode();
            public override bool Equals(object other) => other is RangeSet l && Equals(l);
            public bool Equals(RangeSet other) => type == other.type && crc == other.crc;
        }

        private IndexRange FindTypeRange(int hitIndex, in SearchIndexEntry term)
        {
            if (term.type == SearchIndexEntry.Type.Word)
            {
                if (m_Indexes[0].type != SearchIndexEntry.Type.Word || m_Indexes[hitIndex].type != SearchIndexEntry.Type.Word)
                    return IndexRange.Invalid; // No words

                IndexRange range;
                var rangeSet = new RangeSet(term.type, 0);
                if (m_FixedRanges.TryGetValue(rangeSet, out range))
                    return range;

                int endRange = hitIndex;
                while (m_Indexes[endRange+1].type == SearchIndexEntry.Type.Word)
                    endRange++;

                range = new IndexRange(0, endRange);
                m_FixedRanges[rangeSet] = range;
                return range;
            }
            else if (term.type == SearchIndexEntry.Type.Property || term.type == SearchIndexEntry.Type.Number)
            {
                if (m_Indexes[hitIndex].type != SearchIndexEntry.Type.Property)
                    return IndexRange.Invalid;

                IndexRange range;
                var rangeSet = new RangeSet(term.type, term.crc);
                if (m_FixedRanges.TryGetValue(rangeSet, out range))
                    return range;

                int startRange = hitIndex, prev = hitIndex - 1;
                while (prev >= 0 && m_Indexes[prev].type == SearchIndexEntry.Type.Property && m_Indexes[prev].crc == term.crc)
                    startRange = prev--;

                var indexCount = m_Indexes.Length;
                int endRange = hitIndex, next = hitIndex + 1;
                while (next < indexCount && m_Indexes[next].type == SearchIndexEntry.Type.Property && m_Indexes[next].crc == term.crc)
                    endRange = next++;

                range = new IndexRange(startRange, endRange);
                m_FixedRanges[rangeSet] = range;
                return range;
            }

            return IndexRange.Invalid;
        }

        private IEnumerable<SearchResult> SearchRange(
                int foundIndex, in SearchIndexEntry term,
                int maxScore, SearchIndexComparer comparer,
                SearchResultCollection subset, int limit)
        {
            if (foundIndex < 0 && comparer.op != SearchIndexOperator.Contains && comparer.op != SearchIndexOperator.Equal)
            {
                // Potential range insertion, only used for not exact matches
                foundIndex = (-foundIndex) - 1;
            }

            if (!IsIndexValid(foundIndex, term.crc, term.type))
                return Enumerable.Empty<SearchResult>();

            // Rewind to first element
            while (Lower(ref foundIndex, term, comparer.op))
                ;

            if (!IsIndexValid(foundIndex, term.crc, term.type))
                return Enumerable.Empty<SearchResult>();

            var matches = new List<SearchResult>();
            bool findAll = subset == null;
            do
            {
                var indexEntry = new SearchResult(m_Indexes[foundIndex]);
                #if USE_SORTED_SET
                bool intersects = findAll || (subset.Contains(indexEntry) && subset.TryGetValue(ref indexEntry));
                #else
                bool intersects = findAll || subset.Contains(indexEntry);
                #endif
                if (intersects && indexEntry.score < maxScore)
                {
                    if (term.type == SearchIndexEntry.Type.Number)
                        matches.Add(new SearchResult(indexEntry.index, indexEntry.score + (int)Math.Abs(term.number - m_Indexes[foundIndex].number)));
                    else
                        matches.Add(new SearchResult(indexEntry.index, indexEntry.score));

                    if (matches.Count >= limit)
                        return matches;
                }

                // Advance to last matching element
            } while (Upper(ref foundIndex, term, comparer.op));

            return matches;
        }

        private IEnumerable<SearchResult> SearchIndexes(
                long key, int crc, SearchIndexEntry.Type type, int maxScore,
                SearchIndexComparer comparer, SearchResultCollection subset, int limit = int.MaxValue)
        {
            if (subset != null && subset.Count == 0)
                return Enumerable.Empty<SearchResult>();

            // Find a first match in the sorted indexes.
            var matchKey = new SearchIndexEntry(key, crc, type);
            int foundIndex = Array.BinarySearch(m_Indexes, matchKey, comparer);
            return SearchRange(foundIndex, matchKey, maxScore, comparer, subset, limit);
        }

        protected void UpdateIndexWithNewContent(string[] updated, string[] removed, string[] moved)
        {
            lock (this)
            {
                Start();
                foreach (var id in updated.Concat(moved).Distinct())
                {
                    var documentIndex = AddDocument(id, true);
                    FetchEntries(id, documentIndex, m_BatchIndexes, 0);
                }
                Finish(() => {}, removed);
            }
        }

        private string[] ParseQuery(string query)
        {
            return Regex.Matches(query, @"([\!]*([\""](.+?)[\""]|[^\s_\/]))+").Cast<Match>()
                .Select(m => m.Value.Replace("\"", "").ToLowerInvariant())
                .Where(t => t.Length > 0)
                .OrderBy(t => -t.Length)
                .ToArray();
        }

        struct IndexerThreadScope : IDisposable
        {
            private bool m_Disposed;
            private readonly AssemblyReloadEvents.AssemblyReloadCallback m_AbortHandler;

            public IndexerThreadScope(AssemblyReloadEvents.AssemblyReloadCallback abortHandler)
            {
                m_Disposed = false;
                m_AbortHandler = abortHandler;
                AssemblyReloadEvents.beforeAssemblyReload -= abortHandler;
                AssemblyReloadEvents.beforeAssemblyReload += abortHandler;
            }

            public void Dispose()
            {
                if (m_Disposed)
                    return;
                AssemblyReloadEvents.beforeAssemblyReload -= m_AbortHandler;
                m_Disposed = true;
            }
        }

        public virtual bool SkipEntry(string document, bool checkRoots = false)
        {
            return skipEntryHandler?.Invoke(document) ?? false;
        }

        public virtual void IndexDocument(string document, bool checkIfDocumentExists)
        {
            throw new NotImplementedException($"{nameof(IndexDocument)} must be implemented by a specialized indexer.");
        }
    }
}

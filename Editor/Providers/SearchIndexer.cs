//#define QUICKSEARCH_DEBUG
//#define QUICKSEARCH_PRINT_INDEXING_TIMING
#define FILE_INDEXING_SERIALIZATION

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using JetBrains.Annotations;
using UnityEditor;

#if QUICKSEARCH_DEBUG
using Debug = UnityEngine.Debug;
#endif

namespace Unity.QuickSearch
{
    namespace Providers
    {
        public class SearchIndexer
        {
            [Serializable, DebuggerDisplay("{key} - {length} - {fileIndex}")]
            private struct WordIndexEntry
            {
                public readonly int key;
                public readonly int length;
                public readonly int fileIndex;
                public readonly int score;

                public WordIndexEntry(int _key, int _length, int _fileIndex = -1, int _score = int.MaxValue)
                {
                    key = _key;
                    length = _length;
                    fileIndex = _fileIndex;
                    score = _score;
                }

                public override int GetHashCode()
                {
                    return (key ^ fileIndex).GetHashCode();
                }

                public override bool Equals(object y)
                {
                    WordIndexEntry other = (WordIndexEntry)y;
                    return key == other.key && length == other.length && fileIndex == other.fileIndex && score == other.score;
                }
            }

            [DebuggerDisplay("{path} ({score})")]
            public struct EntryResult
            {
                public string path;
                public int index;
                public int score;
            }

            [DebuggerDisplay("{index} ({score})")]
            private struct PatternMatch
            {
                public PatternMatch(int _i, int _s)
                {
                    index = _i;
                    score = _s;
                }

                public override int GetHashCode()
                {
                    return index.GetHashCode();
                }

                public override bool Equals(object y)
                {
                    PatternMatch other = (PatternMatch)y;
                    return other.index == index;
                }

                public readonly int index;
                public int score;
            }

            [DebuggerDisplay("{baseName} ({basePath})")]
            public struct Root
            {
                public readonly string basePath;
                public readonly string baseName;

                public Root(string _p, string _n)
                {
                    basePath = _p.Replace('\\', '/');
                    baseName = _n;
                }
            }

            public delegate bool SkipEntryHandler(string entry);
            public delegate string[] GetQueryTokensHandler(string query);
            public delegate IEnumerable<string> GetEntryComponentsHandler(string entry, int index);
            public delegate string GetIndexFilePathHandler(string basePath, bool temp);
            public delegate IEnumerable<string> EnumerateRootEntriesHandler(Root root);

            public Root[] roots { get; }
            public int minIndexCharVariation { get; set; } = 2;
            public int maxIndexCharVariation { get; set; } = 8;
            public char[] entrySeparators { get; set; } = SearchUtils.entrySeparators;

            // Handler used to skip some entries. 
            public SkipEntryHandler skipEntryHandler { get; set; } = e => false;
            
            // Handler used to specify where the index database file should be saved. If the handler returns null, the database won't be saved at all.
            public GetIndexFilePathHandler getIndexFilePathHandler { get; set; } = (p, t) => null;
            
            // Handler used to parse and split the search query text into words. The tokens needs to be split similarly to how GetEntryComponentsHandler was specified.
            public GetQueryTokensHandler getQueryTokensHandler { get; set; }

            // Handler used to split into words the entries. The order of the words matter. Words at the beginning of the array have a lower score (lower the better)
            public GetEntryComponentsHandler getEntryComponentsHandler { get; set; } = (e, i) => throw new Exception("You need to specify the get entry components handler");

            // Handler used to fetch all the entries under a given root.
            public EnumerateRootEntriesHandler enumerateRootEntriesHandler { get; set; } = r => throw new Exception("You need to specify the root entries enumerator");

            private Thread m_IndexerThread;
            private volatile bool m_IndexReady = false;
            private volatile bool m_ThreadAborted = false;

            private string[] m_Entries;
            private WordIndexEntry[] m_WordIndexEntries;

            // 1- Initial format
            // 2- Added score to words
            // 3- Save base name in entry paths
            private const int k_IndexFileVersion = 0x4242E000 | 0x003;

            public SearchIndexer(string rootPath)
                : this(rootPath, String.Empty)
            {
            }

            public SearchIndexer(string rootPath, string rootName)
                : this(new[] { new Root(rootPath, rootName) })
            {
            }

            public SearchIndexer(IEnumerable<Root> roots)
            {
                this.roots = roots.ToArray();
                getQueryTokensHandler = ParseQuery;

                EditorApplication.delayCall += CreateIndexerThread;
            }

            public bool IsReady()
            {
                return m_IndexReady;
            }

            public IEnumerable<EntryResult> Search(string query, int maxScore = int.MaxValue)
            {
                #if QUICKSEARCH_DEBUG
                using (new DebugTimer("File Index Search"))
                #endif
                {
                    if (!m_IndexReady)
                        return Enumerable.Empty<EntryResult>();

                    var tokens = getQueryTokensHandler(query);
                    var lengths = tokens.Select(p => p.Length).ToArray();
                    var patterns = tokens.Select(p => p.GetHashCode()).ToArray();

                    if (patterns.Length == 0)
                        return Enumerable.Empty<EntryResult>();

                    var wiec = new WordIndexEntryComparer();
                    lock (this)
                    {
                        var remains = GetPatternFileIndexes(patterns[0], lengths[0], maxScore, wiec).ToList();

                        if (remains.Count == 0)
                            return Enumerable.Empty<EntryResult>();

                        for (int i = 1; i < patterns.Length; ++i)
                        {
                            var newMatches = GetPatternFileIndexes(patterns[i], lengths[i], maxScore, wiec).ToArray();
                            IntersectPatternMatches(remains, newMatches);
                        }

                        return remains.OrderBy(r=>r.score).Select(fi => new EntryResult{path = m_Entries[fi.index], index = fi.index, score = fi.score});
                    }
                }
            }

            private void IntersectPatternMatches(IList<PatternMatch> remains, PatternMatch[] newMatches)
            {
                for (int r = remains.Count - 1; r >= 0; r--)
                {
                    bool intersects = false;
                    foreach (var m in newMatches)
                    {
                        if (remains[r].index == m.index)
                        {
                            intersects = true;
                            remains[r] = new PatternMatch(m.index, Math.Min(remains[r].score, m.score));
                        }
                    }

                    if (!intersects)
                        remains.RemoveAt(r);
                }
            }

            private void LoadIndexFromDisk(string basePath)
            {
                var indexFilePath = getIndexFilePathHandler(basePath, false);
                if (indexFilePath == null || !File.Exists(indexFilePath))
                    return;

                #if QUICKSEARCH_DEBUG
                using (new DebugTimer($"Load Index (<a>{indexFilePath}</a>)"))
                #endif
                {
                    var indexStream = new FileStream(indexFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    try
                    {
                        var indexReader = new BinaryReader(indexStream);
                        int version = indexReader.ReadInt32();
                        if (version == k_IndexFileVersion)
                        {
                            indexReader.ReadString(); // Skip
                            var elementCount = indexReader.ReadInt32();
                            var filePathEntries = new string[elementCount];
                            for (int i = 0; i < elementCount; ++i)
                                filePathEntries[i] = indexReader.ReadString();
                            elementCount = indexReader.ReadInt32();
                            var wordIndexesFromStream = new List<WordIndexEntry>(elementCount);
                            for (int i = 0; i < elementCount; ++i)
                            {
                                var key = indexReader.ReadInt32();
                                var length = indexReader.ReadInt32();
                                var fileIndex = indexReader.ReadInt32();
                                var score = indexReader.ReadInt32();
                                wordIndexesFromStream.Add(new WordIndexEntry(key, length, fileIndex, score));
                            }

                            UpdateIndexes(filePathEntries, wordIndexesFromStream);
                        }
                    }
                    finally
                    {
                        indexStream.Close();
                    }
                }

            }

            private void AbortIndexing(object sender, EventArgs e)
            {
                #if QUICKSEARCH_DEBUG
                Debug.LogWarning("Aborting search indexing...");
                #endif
                if (m_IndexReady)
                    return;

                m_ThreadAborted = true;
            } 

            private void CreateIndexerThread()
            {
                m_IndexerThread = new Thread(() =>
                {
                    try
                    {
                        AppDomain.CurrentDomain.DomainUnload += AbortIndexing;
                        BuildWordIndexes();
                        AppDomain.CurrentDomain.DomainUnload -= AbortIndexing;
                    }
                    catch (ThreadAbortException)
                    {
                        m_IndexReady = false;
                        m_ThreadAborted = true;
                        Thread.ResetAbort();
                    }
                });
                m_IndexerThread.Start();
            }

            private void UpdateIndexes(string[] paths, List<WordIndexEntry> words, string saveIndexBasePath = null)
            {
                // Sort word indexes to run quick binary searches on them.
                words.Sort(SortWordEntryComparer);
                words = words.Distinct().ToList();

                lock (this)
                {
                    m_IndexReady = false;
                    m_Entries = paths;
                    m_WordIndexEntries = words.ToArray();
                    m_IndexReady = true;

                    #if FILE_INDEXING_SERIALIZATION
                    if (saveIndexBasePath != null)
                    {
                        var indexFilePath = getIndexFilePathHandler(saveIndexBasePath, false);
                        if (String.IsNullOrEmpty(indexFilePath))
                            return;

                        #if QUICKSEARCH_DEBUG
                        using (new DebugTimer($"Save Index (<a>{indexFilePath}</a>)"))
                        #endif
                        {
                            var tempIndexFilePath = getIndexFilePathHandler(saveIndexBasePath, true);
                            if (String.IsNullOrEmpty(tempIndexFilePath))
                                return;
                            if (File.Exists(tempIndexFilePath))
                                File.Delete(tempIndexFilePath);
                            var indexStream = new FileStream(tempIndexFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                            BinaryWriter indexWriter = new BinaryWriter(indexStream);
                            indexWriter.Write(k_IndexFileVersion);
                            indexWriter.Write(saveIndexBasePath);

                            indexWriter.Write(m_Entries.Length);
                            foreach (var p in m_Entries)
                                indexWriter.Write(p);
                            indexWriter.Write(m_WordIndexEntries.Length);
                            foreach (var p in m_WordIndexEntries)
                            {
                                indexWriter.Write(p.key);
                                indexWriter.Write(p.length);
                                indexWriter.Write(p.fileIndex);
                                indexWriter.Write(p.score);
                            }

                            indexStream.Close();

                            try
                            {
                                if (File.Exists(indexFilePath))
                                    File.Delete(indexFilePath);
                            }
                            catch (IOException)
                            {
                                // ignore file index persistence operation, since it is not critical and will redone later.
                            }

                            try
                            {
                                File.Move(tempIndexFilePath, indexFilePath);
                            }
                            catch (IOException)
                            {
                                // ignore file index persistence operation, since it is not critical and will redone later.
                            }
                        }
                    }
                    #endif
                }
            }

            private void BuildWordIndexes()
            {
                if (roots.Length == 0)
                    return;

                #if FILE_INDEXING_SERIALIZATION
                lock (this)
                    LoadIndexFromDisk(roots[0].basePath);
                #endif
                
                int entryStart = 0;
                var entries = new List<string>();
                var wordIndexes = new List<WordIndexEntry>();

                #if QUICKSEARCH_DEBUG
                using (new DebugTimer($"Building Index for <a>{roots[0].basePath}</a>"))
                #endif
                {
                    var baseScore = 0;
                    foreach (var r in roots)
                    { 
                        var rootName = r.baseName;
                        var basePath = r.basePath;
                        var basePathWithSlash = basePath + "/";

                        #if QUICKSEARCH_PRINT_INDEXING_TIMING
                        using (new DebugTimer($"Indexing <b>{rootName}</b> entries at <a>{basePath}</a>"))
                        #endif
                        {
                            if (!String.IsNullOrEmpty(rootName))
                                rootName = rootName + "/";

                            // Fetch entries to be indexed and compiled.
                            entries.AddRange(enumerateRootEntriesHandler(r));
                            BuildPartialIndex(wordIndexes, basePathWithSlash, entryStart, entries, baseScore);

                            for (int i = entryStart; i < entries.Count; ++i)
                                entries[i] = rootName + entries[i];

                            entryStart = entries.Count;
                            baseScore = 100;
                        }
                    }

                    #if QUICKSEARCH_PRINT_INDEXING_TIMING
                    using (new DebugTimer($"Updating Index ({entries.Count} entries and {wordIndexes.Count} words)"))
                    #endif
                    {
                        UpdateIndexes(entries.ToArray(), wordIndexes, roots[0].basePath);
                    }
                }
            }

            private List<WordIndexEntry> BuildPartialIndex(string basis, int entryStartIndex, IList<string> entries, int baseScore)
            {
                var wordIndexes = new List<WordIndexEntry>(entries.Count * 3);
                BuildPartialIndex(wordIndexes, basis, entryStartIndex, entries, baseScore);
                return wordIndexes;
            }

            private void BuildPartialIndex(List<WordIndexEntry> wordIndexes, string basis, int entryStartIndex, IList<string> entries, int baseScore)
            {
                for (int i = entryStartIndex; i != entries.Count; ++i)
                {
                    if (m_ThreadAborted)
                        break;

                    // Reformat entry to have them all uniformized.
                    if (!String.IsNullOrEmpty(basis))
                        entries[i] = entries[i].Replace('\\', '/').Replace(basis, "");

                    var path = entries[i];
                    if (skipEntryHandler(path))
                        continue;

                    var filePathComponents = getEntryComponentsHandler(path, i).ToArray();
                    //UnityEngine.Debug.LogFormat(UnityEngine.LogType.Log, UnityEngine.LogOption.NoStacktrace, null, path + " => " + String.Join(", ", filePathComponents));

                    // Build word indexes
                    for (int compIndex = 0; compIndex < filePathComponents.Length; ++compIndex)
                    {
                        var p = filePathComponents[compIndex];
                        for (int c = minIndexCharVariation; c <= p.Length; ++c)
                        {
                            var ss = p.Substring(0, c);
                            wordIndexes.Add(new WordIndexEntry(ss.GetHashCode(), ss.Length, i, baseScore + compIndex));
                        }
                    }
                }
            }

            [Pure] 
            private static int SortWordEntryComparer(WordIndexEntry item1, WordIndexEntry item2)
            {
                var c = item1.length.CompareTo(item2.length);
                if (c != 0)
                    return c;
                return item1.key.CompareTo(item2.key);
            }

            private class WordIndexEntryComparer : IComparer<WordIndexEntry>
            {
                [Pure]
                public int Compare(WordIndexEntry x, WordIndexEntry y)
                {
                    return SortWordEntryComparer(x, y);
                }
            }

            private IEnumerable<PatternMatch> GetPatternFileIndexes(int key, int length, int maxScore, WordIndexEntryComparer wiec)
            {
                // Find a match in the sorted word indexes.
                int foundIndex = Array.BinarySearch(m_WordIndexEntries, new WordIndexEntry(key, length), wiec);
                
                // Rewind to first element
                while (foundIndex > 0 && m_WordIndexEntries[foundIndex - 1].key == key && m_WordIndexEntries[foundIndex - 1].length == length)
                    foundIndex--;

                if (foundIndex < 0)
                    return Enumerable.Empty<PatternMatch>();

                var matches = new List<PatternMatch>();
                do
                {
                    if (m_WordIndexEntries[foundIndex].score < maxScore)
                        matches.Add(new PatternMatch(m_WordIndexEntries[foundIndex].fileIndex, m_WordIndexEntries[foundIndex].score));
                    foundIndex++;
                    // Advance to last matching element
                } while (foundIndex < m_WordIndexEntries.Length && m_WordIndexEntries[foundIndex].key == key && m_WordIndexEntries[foundIndex].length == length);

                return matches;
            }

            protected void UpdateIndexWithNewContent(string[] updated, string[] removed, string[] moved)
            {
                if (!m_IndexReady)
                    return;

                #if QUICKSEARCH_DEBUG
                using( new DebugTimer("Refreshing index with " + String.Join("\r\n\t", updated) + 
                                      $"\r\nRemoved: {String.Join("\r\n\t", removed)}" +
                                      $"\r\nMoved: {String.Join("\r\n\t", moved)}\r\n"))
                #endif
                {
                    lock (this)
                    {
                        List<string> entries = null;
                        List<WordIndexEntry> words = null;

                        // Filter already known entries.
                        updated = updated.Where(u => Array.FindIndex(m_Entries, e => e == u) == -1).ToArray();

                        bool updateIndex = false;
                        if (updated.Length > 0)
                        {
                            entries = new List<string>(m_Entries);
                            words = new List<WordIndexEntry>(m_WordIndexEntries);

                            var wiec = new WordIndexEntryComparer();
                            var partialIndex = BuildPartialIndex(String.Empty, 0, updated, 0);

                            // Update entry file indexes
                            for (int i = 0; i < partialIndex.Count; ++i)
                            {
                                var pk = partialIndex[i];
                                var updatedEntry = updated[pk.fileIndex];
                                var matchedFileIndex = entries.FindIndex(e => e == updatedEntry);
                                if (matchedFileIndex == -1)
                                {
                                    entries.Add(updatedEntry);
                                    matchedFileIndex = entries.Count - 1;
                                }

                                var newWordIndex = new WordIndexEntry(pk.key, pk.length, matchedFileIndex, pk.score);
                                var insertIndex = words.BinarySearch(newWordIndex, wiec);
                                if (insertIndex > -1)
                                    words.Insert(insertIndex, newWordIndex);
                                else
                                    words.Insert(~insertIndex, newWordIndex);
                            }

                            updateIndex = true;
                        }

                        // Remove items
                        if (removed.Length > 0)
                        {
                            entries = entries ?? new List<string>(m_Entries);
                            words = words ?? new List<WordIndexEntry>(m_WordIndexEntries);

                            for (int i = 0; i < removed.Length; ++i)
                            {
                                var entryToBeRemoved = removed[i];
                                var entryIndex = entries.FindIndex(e => e == entryToBeRemoved);
                                if (entryIndex > -1)
                                    updateIndex |= words.RemoveAll(w => w.fileIndex == entryIndex) > 0;
                            }
                        }

                        if (updateIndex)
                            UpdateIndexes(entries.ToArray(), words);
                    }
                }
            }

            private string[] ParseQuery(string query)
            {
                return query.Trim().ToLowerInvariant()
                            .Split(entrySeparators)
                            .Where(t => t.Length > minIndexCharVariation - 1)
                            .Select(t => t.Substring(0, Math.Min(t.Length, maxIndexCharVariation)))
                            .OrderBy(t => -t.Length).ToArray();
            }
        }
    }
}

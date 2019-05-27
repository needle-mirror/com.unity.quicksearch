//#define QUICKSEARCH_DEBUG
//#define QUICKSEARCH_PRINT_INDEXING_TIMING
#define FILE_INDEXING_SERIALIZATION

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using JetBrains.Annotations;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Unity.QuickSearch
{
    namespace Providers
    {
        class FileEntryIndexer
        {
            [Serializable]
            [DebuggerDisplay("{key} - {length} - {fileIndex}")]
            struct WordIndexEntry
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
                    return key == other.key && length == other.length && fileIndex == other.fileIndex;
                }
            }

            [DebuggerDisplay("{path} ({score})")]
            public struct EntryResult
            {
                public string path;
                public int score;
            }

            [DebuggerDisplay("{index} ({score})")]
            struct PatternMatch
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

            public Root[] roots { get; } = new Root[0];

            private Thread m_IndexerThread;
            private volatile bool m_IndexReady = false;
            private volatile bool m_ThreadAborted = false;

            private string[] m_FilePathEntries;
            private WordIndexEntry[] m_WordIndexEntries;

            // 1- Initial format
            // 2- Added score to words
            // 3- Save base name in entry paths
            private const int k_IndexFileVersion = 0x4242E000 | 0x003;
            private const int k_MinIndexCharVariation = 2;
            private const int k_MaxIndexCharVariation = 12;
            private readonly static char[] k_PathSeps = new char[] {'/', ' ', '_', '-', '.'};

            public FileEntryIndexer(string rootPath)
                : this(rootPath, String.Empty)
            {
            }

            public FileEntryIndexer(string rootPath, string rootName)
                : this(new Root[] { new Root(rootPath, rootName) })
            {
            }

            public FileEntryIndexer(IEnumerable<Root> roots)
            {
                this.roots = roots.ToArray();
                CreateFileIndexerThread();

                SearchService.contentRefreshed += UpdateIndexWithNewContent;
            }

            public bool IsReady()
            {
                return m_IndexReady;
            }

            public IEnumerable<EntryResult> EnumerateFileIndexes(string query, int maxScore)
            {
                #if QUICKSEARCH_DEBUG
                using (new DebugTimer("File Index Search"))
                #endif
                {
                    if (!m_IndexReady) 
                        return Enumerable.Empty<EntryResult>();

                    var tokens = query.Trim().ToLowerInvariant().Split(k_PathSeps).Where(t => t.Length > k_MinIndexCharVariation-1)
                                      .Select(t => t.Substring(0, Math.Min(t.Length, k_MaxIndexCharVariation))).OrderBy(t => -t.Length).ToArray();
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

                        return remains.OrderBy(r=>r.score).Select(fi => new EntryResult{path = m_FilePathEntries[fi.index], score = fi.score});
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
                var indexFilePath = GetIndexFilePath(basePath);
                if (!File.Exists(indexFilePath))
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

            private void CreateFileIndexerThread()
            {
                m_IndexerThread = new Thread(() =>
                {
                    try
                    {
                        BuildWordIndexes();
                    }
                    catch (ThreadAbortException)
                    {
                        m_IndexReady = false;
                        m_ThreadAborted = true;
                        Thread.ResetAbort();
                        Debug.LogWarning("Quick Search file entry indexing was aborted (probably because of a domain reload).");
                    }
                    finally
                    {
                    }
                });
                m_IndexerThread.Start();
            }

            private bool ShouldSkipEntry(string entry)
            {
                return entry.Length == 0 || entry[0] == '.' || entry.EndsWith(".meta");
            }

            static string[] SplitCamelCase(string source)
            {
                return Regex.Split(source, @"(?<!^)(?=[A-Z])");
            }

            private static string GetIndexFilePath(string basePath, bool temp = false)
            {
                string indexFileName = "quicksearch.index";
                if (temp)
                    indexFileName = "~" + indexFileName;

                return Path.GetFullPath(Path.Combine(basePath, "..", "Library", indexFileName));
            }

            private void UpdateIndexes(string[] paths, List<WordIndexEntry> words, string saveIndexBasePath = null)
            {
                // Sort word indexes to run quick binary searches on them.
                words.Sort(SortWordEntryComparer);
                words = words.Distinct().ToList();

                lock (this)
                {
                    m_IndexReady = false;
                    m_FilePathEntries = paths;
                    m_WordIndexEntries = words.ToArray();

                    #if FILE_INDEXING_SERIALIZATION
                    if (saveIndexBasePath != null)
                    {
                        var indexFilePath = GetIndexFilePath(saveIndexBasePath);

                        #if QUICKSEARCH_DEBUG
                        using (new DebugTimer($"Save Index (<a>{indexFilePath}</a>)"))
                        #endif
                        {
                            var tempIndexFilePath = GetIndexFilePath(saveIndexBasePath, true);
                            if (File.Exists(tempIndexFilePath))
                                File.Delete(tempIndexFilePath);
                            var indexStream = new FileStream(tempIndexFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                            BinaryWriter indexWriter = new BinaryWriter(indexStream);
                            indexWriter.Write(k_IndexFileVersion);
                            indexWriter.Write(saveIndexBasePath);

                            indexWriter.Write(m_FilePathEntries.Length);
                            foreach (var p in m_FilePathEntries)
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
                                // ignore file index persistance operation, since it is not critical and will redone later.
                            }
                            
                            try { File.Move(tempIndexFilePath, indexFilePath); }
                            catch (IOException)
                            {
                                // ignore file index persistance operation, since it is not critical and will redone later.
                            }
                        }
                    }
                    #endif

                    m_IndexReady = true;
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
                var filePathEntries = new List<string>();
                var wordIndexes = new List<WordIndexEntry>();

                #if QUICKSEARCH_PRINT_INDEXING_TIMING
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
                            filePathEntries.AddRange(Directory.GetFiles(basePath, "*.*", SearchOption.AllDirectories));
                            BuildPartialIndex(wordIndexes, basePathWithSlash, entryStart, filePathEntries, baseScore, ShouldSkipEntry);

                            for (int i = entryStart; i < filePathEntries.Count; ++i)
                                filePathEntries[i] = rootName + filePathEntries[i];

                            entryStart = filePathEntries.Count;
                            baseScore = 100;
                        }
                    }

                    #if QUICKSEARCH_PRINT_INDEXING_TIMING
                    using (new DebugTimer($"Updating Index ({filePathEntries.Count} entries and {wordIndexes.Count} words)"))
                    #endif
                    {
                        UpdateIndexes(filePathEntries.ToArray(), wordIndexes, roots[0].basePath);
                    }
                }
            }

            public static string[] FindShiftLeftVariations(string word)
            {
                var variations = new List<string>(word.Length) {word};
                for (int i = 1, end = word.Length-1; i < end; ++i)
                {
                    word = word.Substring(1);
                    variations.Add(word);
                }

                return variations.ToArray();
            }

            private List<WordIndexEntry> BuildPartialIndex(string basis, 
                int entryStartIndex, IList<string> entries, int baseScore, Func<string, bool> skipEntryHandler)
            {
                var wordIndexes = new List<WordIndexEntry>(entries.Count * 3);
                BuildPartialIndex(wordIndexes, basis, entryStartIndex, entries, baseScore, skipEntryHandler);
                return wordIndexes;
            }

            private void BuildPartialIndex(List<WordIndexEntry> wordIndexes, string basis, 
                int entryStartIndex, IList<string> entries, int baseScore, Func<string, bool> skipEntryHandler)
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

                    var name = Path.GetFileNameWithoutExtension(path);
                    var nameTokens = name.Split(k_PathSeps).Distinct().ToArray();
                    var scc = nameTokens.SelectMany(SplitCamelCase).Where(s=>s.Length>0).ToArray();
                    var fcc = scc.Aggregate("", (current, s) => current + s[0]);
                    var filePathComponents = Enumerable.Empty<string>()
                                                .Concat(scc)
                                                .Concat(new [] {Path.GetExtension(path).Replace(".", "")})
                                                .Concat(FindShiftLeftVariations(fcc))
                                                .Concat(nameTokens.Select(s=>s.ToLowerInvariant()))
                                                .Concat(path.Split(k_PathSeps).Reverse())
                                                .Where(s => s.Length >= k_MinIndexCharVariation)
                                                .Select(s => s.Substring(0, Math.Min(s.Length, k_MaxIndexCharVariation)).ToLowerInvariant())
                                                .Distinct().ToArray();

                    //Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, path + " => " + String.Join(", ", filePathComponents));

                    // Build word indexes
                    for (int compIndex = 0; compIndex < filePathComponents.Length; ++compIndex)
                    {
                        var p = filePathComponents[compIndex];
                        for (int c = k_MinIndexCharVariation; c <= p.Length; ++c)
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
                while (foundIndex >= 0 && m_WordIndexEntries[foundIndex - 1].key == key && m_WordIndexEntries[foundIndex - 1].length == length)
                    foundIndex--;

                if (foundIndex < 0)
                    return new PatternMatch[0];

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

            private void UpdateIndexWithNewContent(string[] updated, string[] removed, string[] moved)
            {
                if (!m_IndexReady)
                    return;

                #if QUICKSEARCH_DEBUG
                using( new DebugTimer("Refreshing index" +
                                      $"\r\nUpdated:\t {String.Join("\r\n\t", updated)}" +
                                      $"\r\nRemoved: {String.Join("\r\n\t", removed)}" +
                                      $"\r\nMoved: {String.Join("\r\n\t", moved)}\r\n"))
                #endif
                {
                    lock (this)
                    {
                        List<string> entries = null;
                        List<WordIndexEntry> words = null;

                        // Filter already known entries.
                        updated = updated.Where(u => Array.FindIndex(m_FilePathEntries, e => e == u) == -1).ToArray();

                        bool updateIndex = false;
                        if (updated.Length > 0)
                        {
                            entries = new List<string>(m_FilePathEntries);
                            words = new List<WordIndexEntry>(m_WordIndexEntries);

                            var wiec = new WordIndexEntryComparer();
                            var partialIndex = BuildPartialIndex(String.Empty, 0, updated, 0, ShouldSkipEntry);

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
                            entries = entries ?? new List<string>(m_FilePathEntries);
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
        }
    }
}

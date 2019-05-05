//#define QUICKSEARCH_DEBUG
#define FILE_INDEXING_SERIALIZATION

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using JetBrains.Annotations;

#if QUICKSEARCH_DEBUG
using UnityEngine;
#endif

namespace Unity.QuickSearch
{
    namespace Providers
    {
        class FileEntryIndexer
        {
            [Serializable]
            struct WordIndexEntry
            {
                public readonly int key;
                public readonly int length;
                public readonly int fileIndex;

                public WordIndexEntry(int _key, int _length, int _fileIndex = -1)
                {
                    key = _key;
                    length = _length;
                    fileIndex = _fileIndex;
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
            
            public string rootPath { get; }

            private Thread m_IndexerThread;
            private volatile bool m_IndexReady = false;
            
            private string[] m_FilePathEntries;
            private WordIndexEntry[] m_WordIndexEntries;

            private const int k_IndexFileVersion = 0x4242E000 | 0x001;
            private const int k_MinIndexCharVariation = 2;
            private const int k_MaxIndexCharVariation = 12;
            private readonly static char[] k_PathSeps = new char[] {'/', ' ', '_', '-', '.'};

            public FileEntryIndexer(string rootPath)
            {
                this.rootPath = rootPath.Replace('\\', '/');
                CreateFileIndexerThread();
            }

            public bool IsReady()
            {
                return m_IndexReady;
            }

            public IEnumerable<string> EnumerateFileIndexes(string query)
            {
                #if QUICKSEARCH_DEBUG
                using (new DebugTimer("File Index Search"))
                #endif
                {
                    if (!m_IndexReady) 
                        return Enumerable.Empty<string>();

                    var tokens = query.Trim().ToLowerInvariant().Split(k_PathSeps).Where(t => t.Length > k_MinIndexCharVariation-1)
                                      .Select(t => t.Substring(0, Math.Min(t.Length, k_MaxIndexCharVariation))).OrderBy(t => -t.Length).ToArray();
                    var lengths = tokens.Select(p => p.Length).ToArray();
                    var patterns = tokens.Select(p => p.GetHashCode()).ToArray();

                    if (patterns.Length == 0)
                        return Enumerable.Empty<string>();

                    var wiec = new WordIndexEntryComparer();
                    lock (this)
                    {
                        var remains = GetPatternFileIndexes(patterns[0], lengths[0], wiec);

                        if (remains.Length == 0)
                            return Enumerable.Empty<string>();

                        for (int i = 1; i < patterns.Length; ++i)
                        {
                            remains = remains.Intersect(GetPatternFileIndexes(patterns[i], lengths[i], wiec)).ToArray();
                        }

                        return remains.Select(fi => m_FilePathEntries[fi]).ToArray();
                    }
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
                    var indexStream = new FileStream(indexFilePath, FileMode.Open);
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
                            var wordIndexesFromStream = new WordIndexEntry[elementCount];
                            for (int i = 0; i < elementCount; ++i)
                            {
                                var key = indexReader.ReadInt32();
                                var length = indexReader.ReadInt32();
                                var fileIndex = indexReader.ReadInt32();
                                wordIndexesFromStream[i] = new WordIndexEntry(key, length, fileIndex);
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
                m_IndexerThread = new Thread(BuildWordIndexes);
                m_IndexerThread.Start();
            }

            private bool ShouldSkipEntry(string entry)
            {
                return entry.EndsWith(".meta");
            }

            string[] SplitCamelCase(string source)
            {
                return Regex.Split(source, @"(?<!^)(?=[A-Z])");
            }

            private static string GetIndexFilePath(string basePath, bool temp = false)
            {
                string indexFileName = "qs.index";
                if (temp)
                    indexFileName = "~" + indexFileName;

                return Path.GetFullPath(Path.Combine(basePath, "..", "Library", indexFileName));
            }

            private void UpdateIndexes(string[] paths, IEnumerable<WordIndexEntry> words, string saveIndexBasePath = null)
            {
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
                            var indexStream = new FileStream(tempIndexFilePath, FileMode.Create);
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
                            }

                            indexStream.Close();

                            if (File.Exists(indexFilePath))
                                File.Delete(indexFilePath);
                            File.Move(tempIndexFilePath, indexFilePath);
                        }
                    }
                    #endif

                    m_IndexReady = true;
                }
            }

            private void BuildWordIndexes()
            {
                var basePath = rootPath;
                var basePathWithSlash = basePath + "/";

                #if FILE_INDEXING_SERIALIZATION
                lock (this)
                    LoadIndexFromDisk(basePath);
                #endif
                
                #if QUICKSEARCH_DEBUG
                int validItemCount = 0;
                int totalComponentCount = 0, minComponentCount = int.MaxValue, maxComponentCount = 0;
                int totalComponentLengthCount = 0, minComponentLengthCount = int.MaxValue, maxComponentLengthCount = 0;
                using (new DebugTimer("Indexing file paths at " + basePath))
                #endif
                {
                    string[] filePathEntries;

                    #if QUICKSEARCH_DEBUG
                    using (new DebugTimer("Index Fetching"))
                    #endif
                    {
                        // Fetch entries to be indexed and compiled.
                        filePathEntries = Directory.GetFiles(basePath, "*.*", SearchOption.AllDirectories);
                    }

                    var wordIndexes = new List<WordIndexEntry>(filePathEntries.Length * 3);
                    for (int i = 0; i != filePathEntries.Length; ++i)
                    {
                        // Reformat entry to have them all uniformized.
                        filePathEntries[i] = filePathEntries[i].Replace('\\', '/').Replace(basePathWithSlash, "");

                        var path = filePathEntries[i];
                        if (ShouldSkipEntry(path))
                            continue;

                        var name = Path.GetFileNameWithoutExtension(path);
                        var nameTokens = name.Split(k_PathSeps).Distinct().ToArray();
                        var filePathComponents = path.Split(k_PathSeps)//.Reverse().Take(3)
                                                    .Concat(nameTokens.Select(s=>s.ToLowerInvariant()))
                                                    .Concat(nameTokens.SelectMany(s=>SplitCamelCase(s)))
                                                    .Where(s => s.Length >= k_MinIndexCharVariation)
                                                    .Select(s => s.Substring(0, Math.Min(s.Length, k_MaxIndexCharVariation)).ToLowerInvariant()).Distinct().ToArray();

                        #if QUICKSEARCH_DEBUG
                        validItemCount++;
                        totalComponentCount += filePathComponents.Length;
                        minComponentCount = Math.Min(minComponentCount, filePathComponents.Length);
                        maxComponentCount = Math.Max(maxComponentCount, filePathComponents.Length);
                        #endif

                        // Build word indexes
                        foreach (var p in filePathComponents)
                        {
                            #if QUICKSEARCH_DEBUG
                            totalComponentLengthCount += p.Length;
                            minComponentLengthCount = Math.Min(minComponentLengthCount, p.Length);
                            maxComponentLengthCount = Math.Max(maxComponentLengthCount, p.Length);
                            #endif

                            for (int c = k_MinIndexCharVariation; c <= p.Length; ++c)
                            {
                                var ss = p.Substring(0, c);
                                wordIndexes.Add(new WordIndexEntry(ss.GetHashCode(), ss.Length, i));
                            }
                        }
                    }

                    #if QUICKSEARCH_DEBUG
                    using (new DebugTimer("Index Sorting"))
                    #endif
                    {
                        // Sort word indexes to run quick binary searches on them.
                        wordIndexes.Sort(SortWordEntryComparer);
                    }

                    #if QUICKSEARCH_DEBUG
                    using (new DebugTimer("Ready Index"))
                    #endif
                    {
                        UpdateIndexes(filePathEntries, wordIndexes.Distinct(), basePath);
                    }

                    #if QUICKSEARCH_DEBUG
                    var countBeforeUniqueness = wordIndexes.Count;
                    int wordCount = m_WordIndexEntries.Length;
                    int wordIndexesMemSize = System.Runtime.InteropServices.Marshal.SizeOf<WordIndexEntry>() * wordCount;
                    Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, $"Word Indexes/File Count: {wordCount}(\u25B3{countBeforeUniqueness-wordCount})/{validItemCount}");
                    Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, $"Word Indexes Mem. Size: {(wordIndexesMemSize / 1024.0 / 1024.0):N1} mb");
                    Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, $"Word Indexes Avg/Min/Max Component Count: {(totalComponentCount/(double)validItemCount):N3}/{minComponentCount}/{maxComponentCount}");
                    Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, $"Word Indexes Avg/Min/Max Length Count: {(totalComponentLengthCount/(double)totalComponentCount):N3}/{minComponentLengthCount}/{maxComponentLengthCount}(capped at {k_MaxIndexCharVariation})");
                    #endif
                }
            }

            [Pure] 
            private static int SortWordEntryComparer(WordIndexEntry item1, WordIndexEntry item2)
            {
                var lengthCompare = item1.length.CompareTo(item2.length);
                if (lengthCompare != 0)
                    return lengthCompare;
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

            private int[] GetPatternFileIndexes(int key, int length, WordIndexEntryComparer wiec)
            {
                // Find a match in the sorted word indexes.
                int foundIndex = Array.BinarySearch(m_WordIndexEntries, new WordIndexEntry(key, length), wiec);
                
                // Rewind to first element
                while (foundIndex >= 0 && m_WordIndexEntries[foundIndex - 1].key == key && m_WordIndexEntries[foundIndex - 1].length == length)
                    foundIndex--;

                if (foundIndex < 0)
                    return new int[0];

                var fileIndexes = new List<int>();
                do
                {
                    fileIndexes.Add(m_WordIndexEntries[foundIndex].fileIndex);
                    foundIndex++;
                    // Advance to last matching element
                } while (foundIndex < m_WordIndexEntries.Length && m_WordIndexEntries[foundIndex].key == key && m_WordIndexEntries[foundIndex].length == length);

                return fileIndexes.ToArray();
            }
        }
    }
}

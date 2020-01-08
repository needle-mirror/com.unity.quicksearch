using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.QuickSearch.Providers;
using UnityEngine;

namespace Unity.QuickSearch
{
    class SearchIndexerTests
    {
        [SetUp]
        public void EnableService()
        {
            SearchService.Enable(SearchContext.Empty);
            SearchService.Filter.ResetFilter(true);
        }

        [TearDown]
        public void DisableService()
        {
            SearchService.Disable(SearchContext.Empty);
        }

        [Test]
        public void IndexSorting()
        {
            List<SearchIndexEntry> indexedWords = new List<SearchIndexEntry>()
            {
                new SearchIndexEntry(33, 2, SearchIndexEntryType.Word, 1, 2),
                new SearchIndexEntry(33, 2, SearchIndexEntryType.Word, 1, 44),
                new SearchIndexEntry(33, 3, SearchIndexEntryType.Word, 1, 445),
                new SearchIndexEntry(33, 1, SearchIndexEntryType.Word, 2, 446),
                new SearchIndexEntry(33, 2, SearchIndexEntryType.Word, 1, 1),
                new SearchIndexEntry(34, 3, SearchIndexEntryType.Word, 2, 447),
                new SearchIndexEntry(33, 1, SearchIndexEntryType.Word, 2, -3)
            };

            Assert.AreEqual(indexedWords.Count, 7);

            Debug.Log($"===> Raw {indexedWords.Count}");
            foreach (var w in indexedWords)
                Debug.Log($"Word {w.crc} - {w.key} - {w.index} - {w.score}");

            var comparer = new SearchIndexComparer();
            indexedWords.Sort(comparer);
            Debug.Log($"===> Sort {indexedWords.Count}");
            foreach (var w in indexedWords)
                Debug.Log($"Word {w.crc} - {w.key} - {w.index} - {w.score}");

            Assert.AreEqual(indexedWords.Count, 7);
            Assert.AreEqual(ToString(indexedWords[0]), "1 - 33 - 2 - -3");
            Assert.AreEqual(ToString(indexedWords[1]), "1 - 33 - 2 - 446");
            Assert.AreEqual(ToString(indexedWords[2]), "2 - 33 - 1 - 1");
            Assert.AreEqual(ToString(indexedWords[3]), "2 - 33 - 1 - 2");
            Assert.AreEqual(ToString(indexedWords[4]), "2 - 33 - 1 - 44");
            Assert.AreEqual(ToString(indexedWords[5]), "3 - 33 - 1 - 445");
            Assert.AreEqual(ToString(indexedWords[6]), "3 - 34 - 2 - 447");

            indexedWords = indexedWords.Distinct(comparer).ToList();
            Debug.Log($"===> Distinct {indexedWords.Count}");
            foreach (var w in indexedWords)
                Debug.Log($"Word {w.crc} - {w.key} - {w.index} - {w.score}");

            Assert.AreEqual(indexedWords.Count, 4);
            Assert.AreEqual(ToString(indexedWords[0]), "1 - 33 - 2 - -3");
            Assert.AreEqual(ToString(indexedWords[1]), "2 - 33 - 1 - 1");
            Assert.AreEqual(ToString(indexedWords[2]), "3 - 33 - 1 - 445");
            Assert.AreEqual(ToString(indexedWords[3]), "3 - 34 - 2 - 447");
        }

        private static string ToString(SearchIndexEntry w)
        {
            return $"{w.crc} - {w.key} - {w.index} - {w.score}";
        }
    }
}

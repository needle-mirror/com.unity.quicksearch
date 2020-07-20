//#define DEBUG_FIND_PROVIDER
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEditor.ShortcutManagement;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace Unity.QuickSearch.Providers
{
    [Flags]
    enum FindOptions
    {
        None = 0,
        Words = 1 << 0,
        Regex = 1 << 1,
        Glob = 1 << 2,
        Fuzzy = 1 << 3,
        Packages = 1 << 28,
        All = Words | Regex | Glob | Fuzzy | Packages,

        CustomStart = 1 << 17,
        CustomFinish = 1 << 23,
        CustomRange = CustomStart | 1 << 18 | 1 << 19 | 1 << 20 | 1 << 21 | 1 << 22 | CustomFinish,
    }

    readonly struct FindResult
    {
        public FindResult(string path, int score)
        {
            this.path = path;
            this.score = score;
        }
        public readonly string path;
        public readonly int score;
    }

    static class FindProvider
    {
        public const string providerId = "find";

        #if DEBUG_FIND_PROVIDER
        private static volatile int s_FileCount = 0;
        #endif
        private static List<string> s_Roots;
        private static readonly List<string> s_ProjectRoots = new List<string>() { "Assets" };
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> s_RootFilePaths = new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>();

        static IEnumerable<SearchItem> FetchItems(SearchContext context, SearchProvider provider)
        {
            var options = FindOptions.Words | FindOptions.Regex | FindOptions.Glob;
            if (context.wantsMore)
                options |= FindOptions.Packages | FindOptions.Fuzzy;
            foreach (var e in Search(context, provider, options))
            {
                yield return provider.CreateItem(context, e.path, e.score,
                    #if true//DEBUG_FIND_PROVIDER
                    $"{e.path} ({(e.score & (int)FindOptions.CustomRange) >> 17}, {(FindOptions)(e.score & (int)FindOptions.All)})",
                    #else
                    null,
                    #endif
                    null, null, null);
            }
        }

        public static IEnumerable<FindResult> Search(SearchContext context, SearchProvider provider, FindOptions options)
        {
            var searchQuery = context.searchQuery;
            if (string.IsNullOrEmpty(searchQuery) || searchQuery.Length < 2)
                yield break;

            #if DEBUG_FIND_PROVIDER
            using (new DebugTimer($"Searching {s_FileCount} files with <i>{searchQuery}</i> ({options})"))
            #endif
            {
                var roots = GetRoots(options);
                var results = new ConcurrentBag<FindResult>();
                var searchTask = Task.Run(() =>
                {
                    Regex globRx = null, rxm = null;
                    var validRx = options.HasFlag(FindOptions.Regex) && ParseRx(searchQuery, out rxm);
                    var validGlob = options.HasFlag(FindOptions.Glob) && ParseGlob(searchQuery, out globRx);
                    var validWords = options.HasFlag(FindOptions.Words);
                    var validFuzzy = options.HasFlag(FindOptions.Fuzzy);
                    Parallel.ForEach(roots, r =>
                    {
                        var isPackage = options.HasFlag(FindOptions.Packages) && r.StartsWith("Packages/", StringComparison.Ordinal);
                        if (!options.HasFlag(FindOptions.Packages) && isPackage)
                            return;

                        if (!s_RootFilePaths.TryGetValue(r, out var files))
                        {
                            files = new ConcurrentDictionary<string, byte>(Directory.EnumerateFiles(r, "*.meta", SearchOption.AllDirectories)
                                .Select(p => p.Substring(0, p.Length - 5).Replace("\\", "/")).ToDictionary(p => p, p => (byte)0));
                            s_RootFilePaths.TryAdd(r, files);
                            #if DEBUG_FIND_PROVIDER
                            s_FileCount += files.Length;
                            #endif
                        }

                        Parallel.ForEach(files, kvp => 
                        {
                            try
                            {
                                var f = kvp.Key;
                                long fuzzyScore = 0;
                                int score = isPackage ? (int)FindOptions.Packages : 0;
                                if (validWords && SearchUtils.MatchSearchGroups(context, f, true))
                                {
                                    results.Add(new FindResult(f, score | (int)FindOptions.Words));
                                }
                                else if (validRx && rxm.IsMatch(f))
                                {
                                    results.Add(new FindResult(f, score | (int)FindOptions.Regex));
                                }
                                else if (validGlob && globRx.IsMatch(f))
                                {
                                    results.Add(new FindResult(f, score | (int)FindOptions.Glob));
                                }
                                else if (validFuzzy && FuzzySearch.FuzzyMatch(searchQuery, f, ref fuzzyScore))
                                {
                                    results.Add(new FindResult(f, ComputeFuzzyScore(score, fuzzyScore)));
                                }
                            }
                            catch
                            {
                                // ignore
                            }
                        });
                    });
                });

                while (results.Count > 0 || !searchTask.Wait(1) || results.Count > 0)
                {
                    if (results.TryTake(out var e))
                        yield return e;
                }
            }
        }

        public static void Update(string[] updated, string[] deleted, string[] moved)
        {
            #if DEBUG_FIND_PROVIDER
            using (new DebugTimer("FD.Update"))
            #endif
            {
                foreach (var u in updated)
                {
                    foreach (var k in s_RootFilePaths.Keys)
                    {
                        if (u.StartsWith(u, StringComparison.Ordinal) && s_RootFilePaths.TryGetValue(k, out var files))
                            files.TryAdd(u, 0);
                    }
                }

                foreach (var u in deleted.Concat(moved))
                {
                    foreach (var k in s_RootFilePaths.Keys)
                    {
                        if (u.StartsWith(u, StringComparison.Ordinal) && s_RootFilePaths.TryGetValue(k, out var files))
                            files.TryRemove(u, out var _);
                    }
                }
            }
        }

        static int ComputeFuzzyScore(int baseScore, long fuzzyScore)
        {
            return baseScore | (int)FindOptions.Fuzzy | (((int)FindOptions.CustomFinish - (int)fuzzyScore) & (int)FindOptions.CustomRange);
        }

        static bool ParseRx(string pattern, out Regex rx)
        {
            try
            {
                rx = new Regex(pattern, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException)
            {
                rx = null;
                return false;
            }

            return true;
        }

        static bool ParseGlob(string pattern, out Regex rx)
        {
            try
            {
                rx = new Regex(Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", "."), RegexOptions.IgnoreCase);
            }
            catch (ArgumentException)
            {
                rx = null;
                return false;
            }

            return true;
        }

        static IEnumerable<string> GetRoots(FindOptions options)
        {
            if (!options.HasFlag(FindOptions.Packages))
                return s_ProjectRoots;

            if (s_Roots != null)
                return s_Roots;

            var listRequest = UnityEditor.PackageManager.Client.List(offlineMode: true);
            while (!listRequest.IsCompleted)
                ;
            return (s_Roots = s_ProjectRoots.Concat(listRequest.Result.Select(r => r.assetPath)).ToList());
        }

        [SearchItemProvider]
        internal static SearchProvider CreateProvider()
        {
            return new SearchProvider(providerId, "Files")
            {
                priority = 25,
                filterId = providerId + ":",
                isExplicitProvider = true,
                isEnabledForContextualSearch = () => Utils.IsFocusedWindowTypeName("ProjectBrowser"),
                fetchItems = (context, items, provider) => FetchItems(context, SearchService.GetProvider("asset") ?? provider)
            };
        }

        [Shortcut("Help/Quick Search/Find Files")]
        internal static void OpenShortcut()
        {
            var qs = QuickSearch.OpenWithContextualProvider(providerId);
            qs.itemIconSize = 0; // Open in compact list view by default.
        }
    }
}
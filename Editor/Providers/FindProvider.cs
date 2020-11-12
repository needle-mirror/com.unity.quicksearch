//#define DEBUG_FIND_PROVIDER
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UnityEditor.Search.Providers
{
    [Flags]
    enum FindOptions
    {
        None = 0,
        Words = 1 << 0,
        Regex = 1 << 1,
        Glob = 1 << 2,
        Fuzzy = 1 << 3,
        Exact = 1 << 15,
        Packages = 1 << 28,
        All = Words | Regex | Glob | Fuzzy | Packages,

        CustomStart = 1 << 17,
        CustomFinish = 1 << 23,
        CustomRange = CustomStart | 1 << 18 | 1 << 19 | 1 << 20 | 1 << 21 | 1 << 22 | CustomFinish,

        AllFiles = 1 << 31
    }

    class FindFilesQueryFactory : SearchQueryEvaluatorFactory<SearchDocument>
    {
        public FindFilesQueryFactory(SearchQueryEvaluator<SearchDocument>.EvalHandler handler)
            : base(handler)
        {}
    }

    class FindFilesQuery : SearchQueryEvaluator<SearchDocument>
    {
        public FindFilesQuery(QueryGraph graph, EvalHandler handler)
            : base(graph, handler)
        {}
    }

    static class FindProvider
    {
        public const string providerId = "find";

        private static List<string> s_Roots;
        private static readonly List<string> s_ProjectRoots = new List<string>() { "Assets" };
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> s_RootFilePaths = new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>();

        private static readonly string[] s_SubsetRoots = new string[] { "<subset>" };
        private static readonly QueryEngine<SearchDocument> s_QueryEngine = new QueryEngine<SearchDocument>(validateFilters: false);

        static IEnumerable<SearchItem> FetchItems(SearchContext context, SearchProvider provider)
        {
            var options = FindOptions.Words | FindOptions.Regex | FindOptions.Glob;
            if (context.wantsMore)
                options |= FindOptions.Packages | FindOptions.Fuzzy;

            foreach (var e in Search(context, provider, options))
            {
                yield return provider.CreateItem(context, e.path, e.score,
                    #if DEBUG_FIND_PROVIDER
                    $"{e.path} ({(e.score & (int)FindOptions.CustomRange) >> 17}, {(FindOptions)(e.score & (int)FindOptions.All)})",
                    #else
                    null,
                    #endif
                    null, null, null);
            }
        }

        public static IEnumerable<SearchDocument> Search(SearchContext context, SearchProvider provider, FindOptions options)
        {
            var searchQuery = context.searchQuery;
            if (string.IsNullOrEmpty(searchQuery) || searchQuery.Length < 2)
                return Enumerable.Empty<SearchDocument>();

            var tokens = searchQuery.ToLowerInvariant().Split(' ').ToArray();
            var args = tokens.Where(t => t.Length > 1 && t[0] == '+').ToArray();
            var words = tokens.Where(t => t.Length > 1).ToArray();
            if (args.Contains("+all"))
            {
                options |= FindOptions.AllFiles;
                searchQuery = searchQuery.Replace("+all", "");
            }

            if (args.Contains("+packages"))
            {
                options |= FindOptions.Packages;
                searchQuery = searchQuery.Replace("+packages", "");
            }

            return Search(searchQuery, words, GetRoots(options), context, provider, options);
        }

        public static IEnumerable<SearchDocument> Search(string searchQuery, string[] words, IEnumerable<string> roots, SearchContext context, SearchProvider provider, FindOptions options)
        {
            #if DEBUG_FIND_PROVIDER
            using (new DebugTimer($"Searching files with <i>{searchQuery}</i> ({options})"))
            #endif
            {
                var query = s_QueryEngine.Parse(searchQuery, new FindFilesQueryFactory(args =>
                {
                    if (args.op == SearchIndexOperator.None)
                        return FindFilesQuery.EvalResult.None;

                    IEnumerable<SearchDocument> subset = args.andSet ??
                        roots.SelectMany(root => GetRootPaths(root, options))
                            .Select(p => new SearchDocument(-1, p.Key));
                    if (args.op == SearchIndexOperator.Equal)
                    {
                        options &= ~FindOptions.Fuzzy;
                        options |= FindOptions.Exact;
                    }

                    IEnumerable<SearchDocument> results = new List<SearchDocument>();
                    if (args.name == null && args.value is string word && word.Length > 0)
                    {
                        results = SearchWord(args.exclude, word, options, subset);
                    }
                    #if DEBUG_FIND_PROVIDER
                    else
                    {
                        Debug.LogWarning($"Not supported {args.op}, name={args.name}, value={args.value}, exclude={args.exclude}, and={args.andSet?.Count()}, or={args.orSet?.Count()}");
                    }
                    #endif

                    if (args.orSet != null)
                        results = results.Concat(args.orSet);

                    return FindFilesQuery.EvalResult.Combined(results);
                }));

                if (!query.valid)
                {
                    context.AddSearchQueryErrors(query.errors.Select(e => new SearchQueryError(e.index, e.length, e.reason, context, provider)));
                    return Enumerable.Empty<SearchDocument>();
                }
                return query.Apply(null).OrderBy(e => e.score).Distinct();
            }
        }

        private static ConcurrentDictionary<string, byte> GetRootPaths(string root, FindOptions options)
        {
            var isPackage = options.HasFlag(FindOptions.Packages) && root.StartsWith("Packages/", StringComparison.Ordinal);
            if (!options.HasFlag(FindOptions.Packages) && isPackage)
                return new ConcurrentDictionary<string, byte>();

            if (!s_RootFilePaths.TryGetValue(root, out var files))
            {
                files = new ConcurrentDictionary<string, byte>(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Where(p => !p.EndsWith(".meta", StringComparison.Ordinal))
                    .Select(p => p.Replace("\\", "/")).ToDictionary(p => p, p => (byte)0));
                s_RootFilePaths.TryAdd(root, files);
            }

            return files;
        }

        private static void SearchWord(bool exclude, string word, FindOptions options, IEnumerable<SearchDocument> documents, ConcurrentBag<SearchDocument> results)
        {
            Regex globRx = null, rxm = null;
            if (options.HasFlag(FindOptions.Regex) && !ParseRx(word, options.HasFlag(FindOptions.Exact), out rxm))
                options &= ~FindOptions.Regex;
            if (options.HasFlag(FindOptions.Glob) && !ParseGlob(word, options.HasFlag(FindOptions.Exact), out globRx))
                options &= ~FindOptions.Glob;
            if (exclude)
                options &= ~FindOptions.Fuzzy;

            Parallel.ForEach(documents, doc =>
            {
                try
                {
                    var match = SearchFile(doc.path, word, options, rxm, globRx, out var score);
                    if (!exclude && match)
                        results.Add(new SearchDocument(doc, score));
                    else if (exclude && !match)
                        results.Add(new SearchDocument(doc, score));
                }
                catch
                {
                    // ignore
                }
            });
        }

        private static bool SearchFile(string f, string word, FindOptions options, Regex rxm, Regex globRx, out int score)
        {
            score = 0;

            if (options.HasFlag(FindOptions.Words))
            {
                if (options.HasFlag(FindOptions.Exact))
                {
                    if (string.Equals(word, f))
                    {
                        score |= (int)FindOptions.Words;
                        return true;
                    }
                }
                else if (f.IndexOf(word, StringComparison.OrdinalIgnoreCase) != -1)
                {
                    score |= (int)FindOptions.Words;
                    return true;
                }
            }

            if (options.HasFlag(FindOptions.Regex) && rxm.IsMatch(f))
            {
                score |= (int)FindOptions.Regex;
                return true;
            }

            if (options.HasFlag(FindOptions.Glob) && globRx.IsMatch(f))
            {
                score |= (int)FindOptions.Glob;
                return true;
            }

            long fuzzyScore = 0;
            if (options.HasFlag(FindOptions.Fuzzy) && FuzzySearch.FuzzyMatch(word, Path.GetFileName(f), ref fuzzyScore))
            {
                score = ComputeFuzzyScore(score, fuzzyScore);
                return true;
            }

            return false;
        }

        public static IEnumerable<SearchDocument> SearchWord(bool exclude, string word, FindOptions options, IEnumerable<SearchDocument> documents)
        {
            var results = new ConcurrentBag<SearchDocument>();
            var searchTask = Task.Run(() => SearchWord(exclude, word, options, documents, results));

            while (results.Count > 0 || !searchTask.Wait(1) || results.Count > 0)
            {
                if (results.TryTake(out var e))
                    yield return e;
            }
        }

        public static void Update(string[] updated, string[] deleted, string[] moved)
        {
            #if DEBUG_FIND_PROVIDER
            using (new DebugTimer($"FD.Update" +
                $"\r\nupdated: {string.Join(",", updated)}" +
                $"\r\ndeleted: {string.Join(",", deleted)}" +
                $"\r\nmoved: {string.Join(",", moved)}"))
            #endif
            {
                foreach (var kvp in s_RootFilePaths)
                {
                    foreach (var u in updated.Concat(moved))
                    {
                        if (u.StartsWith(kvp.Key, StringComparison.Ordinal))
                            kvp.Value.TryAdd(u, 0);
                    }

                    foreach (var u in deleted)
                    {
                        if (u.StartsWith(kvp.Key, StringComparison.Ordinal))
                            kvp.Value.TryRemove(u, out var _);
                    }
                }
            }
        }

        static int ComputeFuzzyScore(int baseScore, long fuzzyScore)
        {
            return baseScore | (int)FindOptions.Fuzzy | (((int)FindOptions.CustomFinish - (int)fuzzyScore) & (int)FindOptions.CustomRange);
        }

        static bool ParseRx(string pattern, bool exact, out Regex rx)
        {
            try
            {
                rx = new Regex(!exact ? pattern : $"^{pattern}$", RegexOptions.IgnoreCase);
            }
            catch (ArgumentException)
            {
                rx = null;
                return false;
            }

            return true;
        }

        static bool ParseGlob(string pattern, bool exact, out Regex rx)
        {
            try
            {
                pattern = Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".");
                rx = new Regex(!exact ? pattern : $"^{pattern}$", RegexOptions.IgnoreCase);
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
            var projectRoots = s_ProjectRoots;
            if (options.HasFlag(FindOptions.AllFiles))
            {
                var baseProjectRoot = new DirectoryInfo(Path.Combine(Application.dataPath, "..")).FullName.Replace("\\", "/");
                projectRoots = Directory.EnumerateDirectories(baseProjectRoot, "*", SearchOption.TopDirectoryOnly)
                    .Select(d => d.Replace(baseProjectRoot, "").Substring(1))
                    .Where(d => d.Length > 0 && char.IsLetterOrDigit(d[0]))
                    .ToList();
            }

            if (!options.HasFlag(FindOptions.Packages))
                return projectRoots;

            if (s_Roots != null)
                return s_Roots;

            var listRequest = PackageManager.Client.List(offlineMode: true);
            while (!listRequest.IsCompleted)
                ;
            return (s_Roots = projectRoots.Concat(listRequest.Result.Select(r => r.assetPath)).ToList());
        }

        [SearchItemProvider]
        internal static SearchProvider CreateProvider()
        {
            s_QueryEngine.SetSearchDataCallback(e => null, s => s.Length < 2 ? null : s, StringComparison.Ordinal);

            return new SearchProvider(providerId, "Files")
            {
                priority = 25,
                filterId = providerId + ":",
                isExplicitProvider = true,
                isEnabledForContextualSearch = () => Utils.IsFocusedWindowTypeName("ProjectBrowser"),
                fetchItems = (context, items, provider) => FetchItems(context, SearchService.GetProvider("asset") ?? provider)
            };
        }

        [ShortcutManagement.Shortcut("Help/Search/Find Files")]
        internal static void OpenShortcut()
        {
            QuickSearch.OpenWithContextualProvider(providerId);
        }
    }
}

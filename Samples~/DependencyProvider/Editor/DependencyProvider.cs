using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Unity.QuickSearch;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditorInternal;
using UnityEngine;

#if UNITY_2020_1_OR_NEWER
static class DependencyProvider
{
    const string providerId = "dep";
    const string dependencyIndexLibraryPath = "Library/dependencies.index";
    private static SearchIndexer index;

    private readonly static Regex guidRx = new Regex(@"guid:\s+([a-z0-9]{32})");

    private readonly static ConcurrentDictionary<string, string> guidToPathMap = new ConcurrentDictionary<string, string>();
    private readonly static ConcurrentDictionary<string, string> pathToGuidMap = new ConcurrentDictionary<string, string>();
    private readonly static ConcurrentDictionary<string,  ConcurrentDictionary<string, byte>> guidToRefsMap = new ConcurrentDictionary<string,  ConcurrentDictionary<string, byte>>();
    private readonly static ConcurrentDictionary<string,  ConcurrentDictionary<string, byte>> guidFromRefsMap = new ConcurrentDictionary<string,  ConcurrentDictionary<string, byte>>();
    private readonly static Dictionary<string, int> guidToDocMap = new Dictionary<string, int>();

    private readonly static string[] builtinGuids = new string[]
    {
         "0000000000000000d000000000000000",
         "0000000000000000e000000000000000",
         "0000000000000000f000000000000000"
    };

    [MenuItem("Window/Quick Search/Rebuild dependency index")]
    private static void Build()
    {
        pathToGuidMap.Clear();
        guidToPathMap.Clear();
        guidToRefsMap.Clear();
        guidFromRefsMap.Clear();
        guidToDocMap.Clear();

        var allGuids = AssetDatabase.FindAssets("a:all");
        foreach (var guid in allGuids.Concat(builtinGuids))
        {
            TrackGuid(guid);
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            pathToGuidMap.TryAdd(assetPath, guid);
            guidToPathMap.TryAdd(guid, assetPath);
        }

        Task.Run(RunThreadIndexing);
    }

    private static void Load(string indexPath)
    {
        var sw = new System.Diagnostics.Stopwatch();
        sw.Start();

        var indexBytes = File.ReadAllBytes(indexPath);
        index = new SearchIndexer()
        {
            resolveDocumentHandler = ResolveAssetPath
        };
        index.LoadBytes(indexBytes, (success) =>
        {
            if (!success)
                Debug.LogError($"Failed to load dependency index at {indexPath}");
            else
                Debug.Log($"Loading dependency index took {sw.Elapsed.TotalMilliseconds,3:0.##} ms ({EditorUtility.FormatBytes(indexBytes.Length)} bytes)");
        });
    }

    private static void RunThreadIndexing()
    {
        var sw = new System.Diagnostics.Stopwatch();
        sw.Start();

        index = new SearchIndexer();
        index.Start();
        int completed = 0;
        var metaFiles = Directory.GetFiles("Assets", "*.meta", SearchOption.AllDirectories);
        var progressId = Progress.Start($"Scanning dependencies ({metaFiles.Length} assets)");

        Parallel.ForEach(metaFiles, mf =>
        {
            Progress.Report(progressId, completed / (float)metaFiles.Length, mf);
            var assetPath = mf.Replace("\\", "/").Substring(0, mf.Length - 5).ToLowerInvariant();
            if (!File.Exists(assetPath))
                return;

            var guid = ToGuid(assetPath);
            Progress.Report(progressId, completed / (float)metaFiles.Length, assetPath);

            TrackGuid(guid);
            pathToGuidMap.TryAdd(assetPath, guid);
            guidToPathMap.TryAdd(guid, assetPath);

            var mfc = File.ReadAllText(mf);
            ScanDependencies(guid, mfc);

            using (var file = new StreamReader(assetPath))
            {
                var header = new char[5];
                if (file.ReadBlock(header, 0, header.Length) == header.Length &&
                    header[0] == '%' && header[1] == 'Y' && header[2] == 'A' && header[3] == 'M' && header[4] == 'L')
                {
                    var ac = file.ReadToEnd();
                    ScanDependencies(guid, ac);
                }
            }

            Progress.Report(progressId, ++completed / (float)metaFiles.Length);
        });
        Progress.Finish(progressId, Progress.Status.Succeeded);

        completed = 0;
        var total = pathToGuidMap.Count + guidToRefsMap.Count + guidFromRefsMap.Count;
        progressId = Progress.Start($"Indexing {total} dependencies");
        foreach (var kvp in pathToGuidMap)
        {
            var guid = kvp.Value;
            var path = kvp.Key;

            var ext = Path.GetExtension(path);
            if (ext.Length > 0 && ext[0] == '.')
                ext = ext.Substring(1);

            Progress.Report(progressId, completed++ / (float)total, path);

            var di = AddGuid(guid);

            index.AddExactWord("all", 0, di);
            AddStaticProperty("id", guid, di, exact: true);
            AddStaticProperty("path", path, di, exact: true);
            AddStaticProperty("t", GetExtension(path).ToLowerInvariant(), di);
            index.AddWord(guid, guid.Length, 0, di);
            IndexWordComponents(di, path);
        }

        foreach (var kvp in guidToRefsMap)
        {
            var guid = kvp.Key;
            var refs = kvp.Value.Keys;
            var di = AddGuid(guid);
            index.AddWord(guid, guid.Length, 0, di);

            Progress.Report(progressId, completed++ / (float)total, guid);

            index.AddNumber("out", refs.Count, 0, di);
            foreach (var r in refs)
                AddStaticProperty("to", r, di);
        }

        foreach (var kvp in guidFromRefsMap)
        {
            var guid = kvp.Key;
            var refs = kvp.Value.Keys;
            var di = AddGuid(guid);

            Progress.Report(progressId, completed++ / (float)total, guid);

            index.AddNumber("in", refs.Count, 0, di);
            foreach (var r in refs)
                AddStaticProperty("from", r, di);

            if (guidToPathMap.TryGetValue(guid, out var path))
                AddStaticProperty("is", "valid", di);
            else
            {
                AddStaticProperty("is", "missing", di);

                foreach (var r in refs)
                {
                    var refDocumentIndex = AddGuid(r);
                    AddStaticProperty("is", "broken", refDocumentIndex);
                    var refDoc = index.GetDocument(refDocumentIndex);
                    if (refDoc.metadata == null)
                        refDoc.metadata = $"Broken links {guid}";
                    else
                        refDoc.metadata += $", {guid}";
                }

                var refString = string.Join(", ", refs.Select(r =>
                {
                    if (guidToPathMap.TryGetValue(r, out var rp))
                        return rp;
                    return r;
                }));
                index.GetDocument(di).metadata = $"Refered by {refString}";
            }
        }

        Progress.SetDescription(progressId, $"Saving dependency index at {dependencyIndexLibraryPath}");

        index.Finish((bytes) =>
        {
            File.WriteAllBytes(dependencyIndexLibraryPath, bytes);
            Progress.Finish(progressId, Progress.Status.Succeeded);

            Debug.Log($"Dependency indexing took {sw.Elapsed.TotalMilliseconds,3:0.##} ms " +
                $"and was saved at {dependencyIndexLibraryPath} ({EditorUtility.FormatBytes(bytes.Length)} bytes)");
        }, removedDocuments: null);
    }

    public static void IndexWordComponents(int documentIndex, string word)
    {
        foreach (var c in SearchUtils.SplitFileEntryComponents(word, SearchUtils.entrySeparators))
            index.AddWord(c.ToLowerInvariant(), 0, documentIndex);
    }

    private static string GetExtension(string path)
    {
        var ext = Path.GetExtension(path);
        if (ext.Length > 0 && ext[0] == '.')
            ext = ext.Substring(1);
        return ext;
    }

    private static void AddStaticProperty(string key, string value, int di, bool exact = false)
    {
        index.AddProperty(key, value, value.Length, value.Length, 0, di, false, exact);
    }

    private static void ScanDependencies(string guid, string content)
    {
        foreach (Match match in guidRx.Matches(content))
        {
            if (match.Groups.Count < 2)
                continue;
            var rg = match.Groups[1].Value;
            if (rg == guid)
                continue;

            TrackGuid(rg);

            guidToRefsMap[guid].TryAdd(rg, 0);
            guidFromRefsMap[rg].TryAdd(guid, 0);
        }
    }

    private static void TrackGuid(string guid)
    {
        if (!guidToRefsMap.ContainsKey(guid))
            guidToRefsMap.TryAdd(guid, new ConcurrentDictionary<string, byte>());

        if (!guidFromRefsMap.ContainsKey(guid))
            guidFromRefsMap.TryAdd(guid, new ConcurrentDictionary<string, byte>());
    }

    private static int AddGuid(string guid)
    {
        if (guidToDocMap.TryGetValue(guid, out var di))
            return di;

        di = index.AddDocument(guid);
        guidToDocMap.Add(guid, di);
        return di;
    }

    private static string ToGuid(string assetPath)
    {
        string metaFile = $"{assetPath}.meta";
        if (!File.Exists(metaFile))
            return null;

        string line;
        using (var file = new StreamReader(metaFile))
        {
            while ((line = file.ReadLine()) != null)
            {
                if (!line.StartsWith("guid:", StringComparison.Ordinal))
                    continue;
                return line.Substring(6);
            }
        }

        return null;
    }

    [SearchItemProvider]
    internal static SearchProvider CreateProvider()
    {
        return new SearchProvider(providerId, "Dependencies")
        {
            active = false,
            filterId = $"dep:",
            isExplicitProvider = true,
            showDetails = true,
            showDetailsOptions = ShowDetailsOptions.Inspector | ShowDetailsOptions.Actions,
            onEnable = OnEnable,
            fetchItems = (context, items, provider) => FetchItems(context, provider),
            fetchLabel = FetchLabel,
            fetchDescription = FetchDescription,
            fetchThumbnail = FetchThumbnail,
            trackSelection = TrackSelection,
            toObject = ToObject
        };
    }

    class DependencyInfo : ScriptableObject
    {
        public string guid;
        public List<string> broken = new List<string>();
        public List<UnityEngine.Object> @using = new List<UnityEngine.Object>();
        public List<UnityEngine.Object> usedBy = new List<UnityEngine.Object>();
        public List<string> untracked = new List<string>();
    }

    private static DependencyInfo s_CurrentDependencyInfo = null;
    private static UnityEngine.Object ToObject(SearchItem item, Type type)
    {
        if (s_CurrentDependencyInfo)
            ScriptableObject.DestroyImmediate(s_CurrentDependencyInfo);
        s_CurrentDependencyInfo = ScriptableObject.CreateInstance<DependencyInfo>();
        s_CurrentDependencyInfo.guid = item.id;
        using (var context = SearchService.CreateContext(new string[] { providerId }, $"from:{item.id}"))
        {
            foreach (var r in SearchService.GetItems(context, SearchFlags.Synchronous))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(r.id);
                if (string.IsNullOrEmpty(assetPath))
                    s_CurrentDependencyInfo.broken.Add(r.id);
                else
                {
                    var ur = AssetDatabase.LoadMainAssetAtPath(assetPath);
                    if (ur != null)
                        s_CurrentDependencyInfo.@using.Add(ur);
                    else
                        s_CurrentDependencyInfo.untracked.Add($"{assetPath} ({r.id})");
                }
            }
        }

        using (var context = SearchService.CreateContext(new string[] { providerId }, $"to:{item.id}"))
        {
            foreach (var r in SearchService.GetItems(context, SearchFlags.Synchronous))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(r.id);
                if (string.IsNullOrEmpty(assetPath))
                    s_CurrentDependencyInfo.broken.Add(r.id);
                else
                {
                    {
                        var ur = AssetDatabase.LoadMainAssetAtPath(assetPath);
                        if (ur != null)
                            s_CurrentDependencyInfo.usedBy.Add(ur);
                        else
                            s_CurrentDependencyInfo.untracked.Add($"{assetPath} ({r.id})");
                    }
                }
            }
        }

        return s_CurrentDependencyInfo;
    }

    private static Texture2D FetchThumbnail(SearchItem item, SearchContext context)
    {
        if (ResolveAssetPath(item, out var path))
            return InternalEditorUtility.GetIconForFile(path);
        return null;
    }

    private static void OnEnable()
    {
        if (index == null)
        {
            if (File.Exists(dependencyIndexLibraryPath))
                Load(dependencyIndexLibraryPath);
            else
                Build();
        }
    }

    [SearchActionsProvider]
    internal static IEnumerable<SearchAction> ActionHandlers()
    {
        return new[]
        {
            SelectAsset(),
            Goto("to", "Show outgoing references", "to"),
            Goto("from", "Show incoming references", "from"),
            Goto("missing", "Show broken links", "is:missing from"),
            LogRefs()
        };
    }

    private static SearchAction SelectAsset()
    {
        return new SearchAction(providerId, "select", null, "Select asset", (SearchItem item) =>
        {
            if (ResolveAssetPath(item, out var path))
                Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(path);
            else
                item.context?.searchView?.SetSearchText($"dep: to:{item.id}");
        })
        {
            closeWindowAfterExecution = false
        };
    }

    private static SearchAction LogRefs()
    {
        return new SearchAction(providerId, "log", null, "Log references and usages", (SearchItem[] items) =>
        {
            foreach (var item in items)
            {
                var sb = new StringBuilder();
                if (ResolveAssetPath(item, out var assetPath))
                    sb.AppendLine($"Dependency info: {LogAssetHref(assetPath)}");
                using (var context = SearchService.CreateContext(new string[] { providerId }, $"from:{item.id}"))
                {
                    sb.AppendLine("outgoing:");
                    foreach (var r in SearchService.GetItems(context, SearchFlags.Synchronous))
                        LogRefItem(sb, r);
                }

                using (var context = SearchService.CreateContext(new string[] { providerId }, $"to:{item.id}"))
                {
                    sb.AppendLine("incoming:");
                    foreach (var r in SearchService.GetItems(context, SearchFlags.Synchronous))
                        LogRefItem(sb, r);
                }

                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, sb.ToString());
            }
        })
        {
            closeWindowAfterExecution = false
        };
    }

    private static string LogAssetHref(string assetPath)
    {
        return $"<a href=\"{assetPath}\" line=\"0\">{assetPath}</a>";
    }

    private static void LogRefItem(StringBuilder sb, SearchItem item)
    {
        if (ResolveAssetPath(item, out var assetPath))
            sb.AppendLine($"\t{LogAssetHref(assetPath)} ({item.id})");
        else
            sb.AppendLine($"\t<color=#EE9898>BROKEN</color> ({item.id})");
    }

    private static SearchAction Goto(string action, string title, string filter)
    {
        return new SearchAction(providerId, action, null, title, (SearchItem item) => item.context?.searchView?.SetSearchText($"dep: {filter}:{item.id}"))
        {
            closeWindowAfterExecution = false
        };
    }

    private static bool ResolveAssetPath(string guid, out string path)
    {
        if (guidToPathMap.TryGetValue(guid, out path))
            return true;

        path = AssetDatabase.GUIDToAssetPath(guid);
        if (!string.IsNullOrEmpty(path))
            return true;

        return false;
    }

    private static string ResolveAssetPath(string id)
    {
        if (ResolveAssetPath(id, out var path))
            return path;
        return null;
    }

    private static bool ResolveAssetPath(SearchItem item, out string path)
    {
        return ResolveAssetPath(item.id, out path);
    }

    private static string FetchLabel(SearchItem item, SearchContext context)
    {
        var metaString = index.GetDocument((int)item.data)?.metadata;
        var hasMetaString = !string.IsNullOrEmpty(metaString);
        if (ResolveAssetPath(item, out var path))
            return !hasMetaString ? path : $"<color=#EE9898>{path}</color>";

        return $"<color=#EE6666>{item.id}</color>";
    }

    private static string GetDescrition(SearchItem item)
    {
        var metaString = index.GetDocument((int)item.data).metadata;
        if (!string.IsNullOrEmpty(metaString))
            return metaString;

        if (ResolveAssetPath(item, out _))
            return item.id;

        return "<invalid>";
    }

    private static string FetchDescription(SearchItem item, SearchContext context)
    {
        var description = GetDescrition(item);
        return $"{FetchLabel(item, context)} ({description})";
    }

    private static void TrackSelection(SearchItem item, SearchContext context)
    {
        EditorGUIUtility.systemCopyBuffer = item.id;
    }

    private static IEnumerable<SearchItem> FetchItems(SearchContext context, SearchProvider provider)
    {
        var sw = new System.Diagnostics.Stopwatch();
        sw.Start();
        while (index == null || !index.IsReady())
            yield return null;
        foreach (var e in index.Search(context.searchQuery.ToLowerInvariant()))
        {
            var item = provider.CreateItem(context, e.id, e.score, null, null, null, e.index);
            item.options &= ~SearchItemOptions.Ellipsis;
            yield return item;
        }
    }

    [Shortcut("Help/Quick Search/Dependencies")]
    internal static void OpenShortcut()
    {
        QuickSearch.OpenWithContextualProvider(providerId);
    }
}
#endif
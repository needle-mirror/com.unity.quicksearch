//#define DEBUG_INDEXING
//#define DEBUG_LOG_CHANGES
//#define DEBUG_RESOLVING

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor.Experimental;
using UnityEngine;

namespace UnityEditor.Search
{
    using Task = SearchTask<TaskData>;

    class TaskData
    {
        public TaskData(byte[] bytes, SearchIndexer combinedIndex)
        {
            this.bytes = bytes;
            this.combinedIndex = combinedIndex;
        }

        public readonly byte[] bytes;
        public readonly SearchIndexer combinedIndex;
    }

    [ExcludeFromPreset]
    class SearchDatabase : ScriptableObject, ITaskReporter
    {
        // 1- First version
        // 2- Rename ADBIndex for SearchDatabase
        // 3- Add db name and type
        // 4- Add better ref: property indexing
        // 5- Fix asset has= property indexing.
        // 6- Use produce artifacts async
        // 7- Update how keywords are encoded
        public const int version = (7 << 8) ^ SearchIndexEntryImporter.version;
        private const string k_QuickSearchLibraryPath = "Library/Search";
        public const string defaultSearchDatabaseIndexPath = "UserSettings/Search.index";

        public enum IndexType
        {
            asset,
            scene,
            prefab
        }

        public enum IndexLocation
        {
            all,
            assets,
            packages
        }

        [Flags]
        enum ChangeStatus
        {
            Deleted = 1 << 1,
            Missing = 1 << 2,
            Changed = 1 << 3,

            All = Deleted | Missing | Changed,
            Any = All
        }

        [Serializable]
        public class Options
        {
            public bool disabled = false;           // Disables the index

            public bool types = true;               // Index type information about objects
            public bool properties = false;         // Index serialized properties of objects
            public bool extended = false;           // Index as many properties as possible (i.e. asset import settings)
            public bool dependencies = false;       // Index object dependencies (i.e. ref:<name>)

            public override int GetHashCode()
            {
                return (types ? (int)IndexingOptions.Types        : 0) |
                    (properties ? (int)IndexingOptions.Properties   : 0) |
                    (extended ? (int)IndexingOptions.Extended     : 0) |
                    (dependencies ? (int)IndexingOptions.Dependencies : 0);
            }
        }

        [Serializable]
        public class Settings
        {
            [NonSerialized] public string root;
            [NonSerialized] public string source;
            [NonSerialized] public string guid;

            public string name;
            public string type = nameof(IndexType.asset);
            public string[] roots;
            public string[] includes;
            public string[] excludes;

            public Options options;
            public int baseScore = 100;
        }

        class IndexArtifact : IEquatable<IndexArtifact>
        {
            public readonly string source;
            public readonly ArtifactKey key;
            public ArtifactID value;
            public string path;
            public long timestamp;
            public OnDemandState state;

            public GUID guid => key.guid;
            public Type indexerType => key.importerType;
            public bool valid => key.isValid && value.isValid;
            public bool timeout => TimeSpan.FromTicks(DateTime.UtcNow.Ticks - timestamp).TotalSeconds > 10d;

            public IndexArtifact(in string source, in GUID guid, in Type indexerType)
            {
                this.source = source;
                key = new ArtifactKey(guid, indexerType);
                value = default;
                path = null;
                timestamp = long.MaxValue;
                state = OnDemandState.Unavailable;
            }

            public override string ToString()
            {
                return $"{guid} / {path} / {value}";
            }

            public override int GetHashCode()
            {
                return guid.GetHashCode() ^ value.value.GetHashCode();
            }

            public override bool Equals(object other)
            {
                return other is IndexArtifact l && Equals(l);
            }

            public bool Equals(IndexArtifact other)
            {
                return guid == other.guid && value.value == other.value.value;
            }
        }

        public new string name
        {
            get
            {
                if (string.IsNullOrEmpty(settings.name))
                    return base.name;
                return settings.name;
            }
            set
            {
                base.name = value;
            }
        }

        [SerializeField] public Settings settings;
        [SerializeField, HideInInspector] public byte[] bytes;

        [NonSerialized] private int m_ProductionLimit = 99;
        [NonSerialized] private int m_InstanceID = 0;
        [NonSerialized] private Task m_CurrentResolveTask;
        [NonSerialized] private Task m_CurrentUpdateTask;
        [NonSerialized] private int m_UpdateTasks = 0;
        [NonSerialized] private string m_IndexSettingsPath;
        [NonSerialized] private ConcurrentBag<AssetIndexChangeSet> m_UpdateQueue = new ConcurrentBag<AssetIndexChangeSet>();

        public ObjectIndexer index { get; internal set; }
        public bool loaded { get; private set; }
        public bool ready => this && loaded && index != null && index.IsReady();
        public bool updating => m_UpdateTasks > 0 || !loaded || m_CurrentResolveTask != null || !ready;
        public string path => m_IndexSettingsPath ?? AssetDatabase.GetAssetPath(this);

        internal static event Action<SearchDatabase> indexLoaded;
        internal static List<SearchDatabase> s_DBs;

        public static SearchDatabase Create(string settingsPath)
        {
            var db = CreateInstance<SearchDatabase>();
            db.hideFlags |= HideFlags.DontUnloadUnusedAsset | HideFlags.DontSaveInEditor;
            return db.Reload(settingsPath);
        }

        public SearchDatabase Reload(string settingsPath)
        {
            m_IndexSettingsPath = settingsPath;
            SearchMonitor.RaiseContentRefreshed(new[] { settingsPath }, new string[0], new string[0]);
            return Reload(LoadSettings(settingsPath));
        }

        public SearchDatabase Reload(Settings settings)
        {
            this.settings = settings;
            index = CreateIndexer(settings);
            name = settings.name;
            LoadAsync();
            return this;
        }

        private static string GetDbGuid(string settingsPath)
        {
            var guid = AssetDatabase.AssetPathToGUID(settingsPath);
            if (string.IsNullOrEmpty(guid))
                return Hash128.Compute(settingsPath).ToString();
            return guid;
        }

        private static bool IsValidType(string path, string[] types)
        {
            if (!File.Exists(path))
                return false;
            var settings = LoadSettings(path);
            if (settings.options.disabled)
                return false;
            return types.Length == 0 || types.Contains(settings.type);
        }

        public static IEnumerable<SearchDatabase> Enumerate(params string[] types)
        {
            return Enumerate(IndexLocation.all, types);
        }

        public static IEnumerable<SearchDatabase> Enumerate(IndexLocation location, params string[] types)
        {
            return EnumerateAll().Where(db =>
            {
                if (types != null && types.Length > 0 && Array.IndexOf(types, db.settings.type) == -1)
                    return false;

                if (location == IndexLocation.all)
                    return true;
                else if (location == IndexLocation.packages)
                    return !string.IsNullOrEmpty(db.path) && db.path.StartsWith("Packages", StringComparison.OrdinalIgnoreCase);

                return string.IsNullOrEmpty(db.path) || db.path.StartsWith("Assets", StringComparison.OrdinalIgnoreCase);
            });
        }

        public static IEnumerable<SearchDatabase> EnumerateAll()
        {
            if (s_DBs == null)
            {
                s_DBs = new List<SearchDatabase>();
                if (File.Exists(defaultSearchDatabaseIndexPath))
                    s_DBs.Add(Create(defaultSearchDatabaseIndexPath));

                string searchDataFindAssetQuery = $"t:{nameof(SearchDatabase)}";
                var dbPaths = AssetDatabase.FindAssets(searchDataFindAssetQuery).Select(AssetDatabase.GUIDToAssetPath)
                    .OrderByDescending(p => Path.GetFileNameWithoutExtension(p));

                s_DBs.AddRange(dbPaths
                    .Select(path => AssetDatabase.LoadAssetAtPath<SearchDatabase>(path))
                    .Where(db => db));

                SearchMonitor.contentRefreshed -= TrackAssetIndexChanges;
                SearchMonitor.contentRefreshed += TrackAssetIndexChanges;
            }

            foreach (var g in s_DBs.Where(db => db).GroupBy(db => db.ready).OrderBy(g => !g.Key))
                foreach (var db in g.OrderBy(db => db.settings.baseScore))
                    yield return db;
        }

        private static IEnumerable<string> FilterIndexes(IEnumerable<string> paths)
        {
            return paths.Where(u => u.EndsWith(".index", StringComparison.OrdinalIgnoreCase));
        }

        private static void TrackAssetIndexChanges(string[] updated, string[] deleted, string[] moved)
        {
            if (s_DBs == null)
                return;

            bool updateViews = false;
            foreach (var p in FilterIndexes(updated))
            {
                var db = Find(p);
                if (db)
                    continue;
                s_DBs.Add(AssetDatabase.LoadAssetAtPath<SearchDatabase>(p));
                updateViews = true;
            }

            foreach (var p in FilterIndexes(deleted))
            {
                var db = Find(p);
                if (db)
                    updateViews |= s_DBs.Remove(db);
            }

            if (updateViews || s_DBs.RemoveAll(db => !db) > 0)
            {
                EditorApplication.delayCall -= SearchService.RefreshWindows;
                EditorApplication.delayCall += SearchService.RefreshWindows;
            }

            Providers.FindProvider.Update(updated, deleted, moved);
        }

        public static Settings LoadSettings(string settingsPath)
        {
            var settingsJSON = File.ReadAllText(settingsPath);
            var indexSettings = JsonUtility.FromJson<Settings>(settingsJSON);

            indexSettings.source = settingsPath;
            indexSettings.guid = GetDbGuid(settingsPath);
            indexSettings.root = Path.GetDirectoryName(settingsPath).Replace("\\", "/");
            if (String.IsNullOrEmpty(indexSettings.name))
                indexSettings.name = Path.GetFileNameWithoutExtension(settingsPath);

            return indexSettings;
        }

        public static ObjectIndexer CreateIndexer(Settings settings, string settingsPath = null)
        {
            // Fix the settings root if needed
            if (!String.IsNullOrEmpty(settingsPath))
            {
                if (String.IsNullOrEmpty(settings.source))
                    settings.source = settingsPath;

                if (String.IsNullOrEmpty(settings.root))
                    settings.root = Path.GetDirectoryName(settingsPath).Replace("\\", "/");

                if (String.IsNullOrEmpty(settings.guid))
                    settings.guid = GetDbGuid(settingsPath);

                if (settings.type == "prefab" || settings.type == "scene")
                    settings.options.extended = true;
            }

            return new AssetIndexer(settings);
        }

        public void Import(string settingsPath)
        {
            settings = LoadSettings(settingsPath);
            index = CreateIndexer(settings);
            name = settings.name;
            DeleteBackupIndex();
        }

        private static SearchDatabase Find(string path)
        {
            return EnumerateAll().Where(db => string.Equals(db.path, path, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        }

        public static SearchDatabase ImportAsset(string settingsPath)
        {
            var currentDb = Find(settingsPath);
            if (currentDb)
                currentDb.DeleteBackupIndex();

            AssetDatabase.ImportAsset(settingsPath);
            if (currentDb)
                currentDb.Reload(settingsPath);
            else
            {
                currentDb = AssetDatabase.LoadAssetAtPath<SearchDatabase>(settingsPath);
                if (!currentDb)
                    currentDb = Create(settingsPath);
                s_DBs.Add(currentDb);
            }

            return currentDb;
        }

        internal void OnEnable()
        {
            if (settings == null)
                return;

            index = CreateIndexer(settings, path);

            if (settings.source == null)
                return;

            m_InstanceID = GetInstanceID();

            Log("OnEnable");
            if (bytes?.Length > 0)
                Dispatcher.Enqueue(Load);
            else
                Dispatcher.Enqueue(LoadAsync);
        }

        internal void OnDisable()
        {
            Log("OnDisable");
            SearchMonitor.contentRefreshed -= OnContentRefreshed;
            m_CurrentResolveTask?.Dispose();
            m_CurrentResolveTask = null;
            m_CurrentUpdateTask?.Dispose();
            m_CurrentUpdateTask = null;
        }

        private void LoadAsync()
        {
            if (!this)
                return;

            var backupIndexPath = GetBackupIndexPath(false);
            if (File.Exists(backupIndexPath))
                IncrementalLoad(backupIndexPath);
            else
                Build();
        }

        [System.Diagnostics.Conditional("DEBUG_INDEXING")]
        private void Log(string callName, params string[] args)
        {
            Log(LogType.Log, callName, args);
        }

        [System.Diagnostics.Conditional("DEBUG_INDEXING")]
        private void Log(LogType logType, string callName, params string[] args)
        {
            if (!this || index == null || index.settings.options.disabled)
                return;
            var status = "";
            if (ready) status += "R";
            if (index != null && index.IsReady()) status += "I";
            if (loaded) status += "L";
            if (updating) status += "~";
            Debug.LogFormat(logType, LogOption.None, this, $"({m_InstanceID}, {status}) <b>{settings.name}</b>.<b>{callName}</b> {string.Join(", ", args)}" +
                $" {Utils.FormatBytes(bytes?.Length ?? 0)}, {index?.documentCount ?? 0} documents, {index?.indexCount ?? 0} elements");
        }

        public void Report(string status, params string[] args)
        {
            Log(status, args);
        }

        private void Load()
        {
            if (!this)
                return;

            var loadTask = new Task("Load", $"Reading {name.ToLowerInvariant()} search index", (task, data) => Setup(), this);
            loadTask.RunThread(() =>
            {
                var step = 0;
                loadTask.Report($"Reading {bytes.Length} bytes...");
                if (!index.LoadBytes(bytes))
                    Debug.LogError($"Failed to load {name} index. Please re-import it.", this);

                loadTask.Report(++step, step + 1);

                Dispatcher.Enqueue(() => loadTask.Resolve(new TaskData(bytes, index)));
            });
        }

        private void IncrementalLoad(string indexPath)
        {
            var loadTask = new Task("Read", $"Loading {name.ToLowerInvariant()} search index", (task, data) => Setup(), this);
            loadTask.RunThread(() =>
            {
                loadTask.Report($"Loading {indexPath}...", -1);
                var fileBytes = File.ReadAllBytes(indexPath);

                if (!index.LoadBytes(fileBytes))
                    throw new Exception($"Failed to load {indexPath}.");

                var deletedAssets = new HashSet<string>();
                foreach (var d in index.GetDocuments())
                {
                    if (d.valid && !File.Exists(d.source))
                        deletedAssets.Add(d.source);
                }

                Dispatcher.Enqueue(() =>
                {
                    if (!this)
                    {
                        loadTask.Resolve(null, completed: true);
                        return;
                    }

                    bytes = fileBytes;

                    loadTask.Report($"Checking for changes...", -1);
                    var diff = SearchMonitor.GetDiff(index.timestamp, deletedAssets, path =>
                    {
                        if (index.SkipEntry(path, true))
                            return false;

                        if (!index.TryGetHash(path, out var hash) || !hash.isValid)
                            return true;

                        return hash != index.GetDocumentHash(path);
                    });
                    if (!diff.empty)
                        IncrementalUpdate(diff);

                    loadTask.Resolve(new TaskData(fileBytes, index));
                });
            });
        }

        private string GetIndexTypeSuffix()
        {
            return $"{settings.options.GetHashCode():X}.index".ToLowerInvariant();
        }

        private bool ResolveArtifactPaths(in IList<IndexArtifact> artifacts, out List<IndexArtifact> unresolvedArtifacts, Task task, ref int completed)
        {
            var inProductionCount = 0;
            var artifactIndexSuffix = "." + GetIndexTypeSuffix();

            task.Report($"Resolving {artifacts.Count} artifacts...");
            unresolvedArtifacts = new List<IndexArtifact>((int)(artifacts.Count / 1.5f));

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < artifacts.Count; ++i)
            {
                var a = artifacts[i];
                if (a == null || a.guid.Empty())
                {
                    ++completed;
                    continue;
                }

                // Simply mark artifact as unresolved and we will resume later.
                if (inProductionCount > m_ProductionLimit || sw.ElapsedMilliseconds > 250)
                {
                    unresolvedArtifacts.AddRange(artifacts.Skip(i));
                    break;
                }

                if (!ProduceArtifact(a))
                {
                    ++completed;
                    continue;
                }

                if (!a.valid)
                {
                    inProductionCount++;
                    if (!a.timeout)
                        unresolvedArtifacts.Add(a);
                    else
                    {
                        // Check if the asset is still available (maybe it was deleted since last request)
                        var resolvedPath = AssetDatabase.GUIDToAssetPath(a.guid);
                        if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath))
                        {
                            a.timestamp = long.MaxValue;
                            if (ProduceArtifact(a))
                                unresolvedArtifacts.Add(a);
                        }
                    }
                }
                else if (GetArtifactPaths(a.value, out var paths))
                {
                    a.path = paths.LastOrDefault(p => p.EndsWith(artifactIndexSuffix, StringComparison.Ordinal));
                    if (a.path == null)
                        ReportWarning(a, artifactIndexSuffix, paths);
                }
            }

            var producedCount = artifacts.Count - unresolvedArtifacts.Count;
            if (producedCount >= m_ProductionLimit)
                m_ProductionLimit = (int)(m_ProductionLimit * 1.5f);
            #if DEBUG_RESOLVING
            Debug.Log($"[{name}] Resolved {producedCount} artifacts in {sw.ElapsedMilliseconds} ms ({inProductionCount} in production, {unresolvedArtifacts.Count} remaining)");
            #endif

            task.Report(completed);
            return unresolvedArtifacts.Count == 0;
        }

        private void ReportWarning(in IndexArtifact a, in string artifactIndexSuffix, params string[] paths)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(a.guid);
            Console.WriteLine($"Cannot find search index artifact for {assetPath} ({a.guid}{artifactIndexSuffix})\n\t- {string.Join("\n\t- ", paths)}");
        }

        private Task ResolveArtifacts(string taskName, string title, Task.ResolveHandler finished)
        {
            var resolveTask = new Task(taskName, title, finished, 1, this);
            List<string> paths = null;
            resolveTask.RunThread(() =>
            {
                resolveTask.Report("Scanning dependencies...");
                paths = index.GetDependencies();
            }, () => ProduceArtifacts(resolveTask, paths));

            return resolveTask;
        }

        private void ProduceArtifacts(Task resolveTask, in IList<string> paths)
        {
            if (resolveTask?.Canceled() ?? false)
                return;

            resolveTask.Report("Producing artifacts...");
            resolveTask.total = paths.Count;
            if (resolveTask?.Canceled() ?? false)
                return;

            ResolveArtifacts(CreateArtifacts(paths), null, resolveTask, true);
        }

        private bool ResolveArtifacts(IndexArtifact[] artifacts, IList<IndexArtifact> partialSet, Task task, bool combineAutoResolve)
        {
            try
            {
                partialSet = partialSet ?? artifacts;

                if (!this || task.Canceled())
                    return false;

                int completed = artifacts.Length - partialSet.Count;
                if (ResolveArtifactPaths(partialSet, out var remainingArtifacts, task, ref completed))
                {
                    if (task.Canceled())
                        return false;
                    return task.RunThread(() => CombineIndexes(settings, artifacts, task, combineAutoResolve));
                }

                // Resume later with remaining artifacts
                Dispatcher.Enqueue(() => ResolveArtifacts(artifacts, remainingArtifacts, task, combineAutoResolve),
                    GetArtifactResolutionCheckDelay(remainingArtifacts.Count));
            }
            catch (Exception err)
            {
                task.Resolve(err);
            }

            return false;
        }

        private double GetArtifactResolutionCheckDelay(int artifactCount)
        {
            if (UnityEditorInternal.InternalEditorUtility.isHumanControllingUs)
                return Math.Max(0.5, Math.Min(artifactCount / 1000.0, 3.0));
            return 1;
        }

        private void CombineIndexes(in Settings settings, in IndexArtifact[] artifacts, Task task, bool autoResolve)
        {
            if (task.Canceled())
                return;

            // Combine all search index artifacts into one large binary stream.
            var combineIndexer = new SearchIndexer();
            var indexName = settings.name.ToLowerInvariant();
            var artifactDbs = EnumerateSearchArtifacts(artifacts, task);

            task.Report("Combining indexes...", -1f);
            task.total = artifacts.Length;

            combineIndexer.Start();
            combineIndexer.CombineIndexes(artifactDbs, settings.baseScore, indexName, progress => task.Report(progress));

            if (task.Canceled())
                return;

            task.Report($"Sorting {combineIndexer.indexCount} indexes...", -1f);
            byte[] bytes = autoResolve ? combineIndexer.SaveBytes() : null;
            Dispatcher.Enqueue(() => task.Resolve(new TaskData(bytes, combineIndexer), completed: autoResolve));
        }

        IEnumerable<SearchIndexer> EnumerateSearchArtifacts(IndexArtifact[] artifacts, Task task)
        {
            long totalMs = 2, totalItr = 1;
            var results = new ConcurrentBag<SearchIndexer>();
            var readTask = System.Threading.Tasks.Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                foreach (var a in artifacts)
                {
                    if (a == null || a.path == null)
                        continue;

                    sw.Restart();
                    var si = new SearchIndexer(Path.GetFileName(a.source));
                    if (!si.ReadIndexFromDisk(a.path))
                        continue;
                    totalMs += sw.ElapsedMilliseconds;
                    totalItr++;

                    results.Add(si);
                }
            });

            while (results.Count > 0 || !readTask.Wait((int)(totalMs / totalItr)) || results.Count > 0)
            {
                while (results.TryTake(out var e))
                {
                    task.Report($"Combining {e.name}...");
                    yield return e;
                }

                if (task.Canceled())
                    yield break;
            }

            #if DEBUG_RESOLVING
            Debug.Log($"Reading artifacts in {totalMs / totalItr} in average");
            #endif
        }

        private static void AddIndexNameArea(int documentIndex, SearchIndexer indexer, string indexName)
        {
            indexer.AddProperty("a", indexName, indexName.Length, indexName.Length, 0, documentIndex, saveKeyword: true, exact: true);
        }

        private void Build()
        {
            m_CurrentResolveTask?.Cancel();
            m_CurrentResolveTask?.Dispose();
            m_CurrentResolveTask = ResolveArtifacts("Build", $"Building {name.ToLowerInvariant()} search index", OnArtifactsResolved);
        }

        private void OnArtifactsResolved(Task task, TaskData data)
        {
            m_CurrentResolveTask = null;
            if (task.canceled || task.error != null)
                return;
            index.ApplyFrom(data.combinedIndex);
            bytes = data.bytes;
            // Do not cache indexes while running tests
            if (!Utils.IsRunningTests())
                SaveIndex(data.bytes, Setup);
            else
                Setup();
            #if DEBUG_RESOLVING
            Debug.Log($"{task.title} took {task.elapsedTime} ms");
            #endif
        }

        private void Setup()
        {
            if (!this)
                return;

            loaded = true;
            Log("Setup");
            indexLoaded?.Invoke(this);
            SearchMonitor.contentRefreshed -= OnContentRefreshed;
            SearchMonitor.contentRefreshed += OnContentRefreshed;
        }

        private string GetBackupIndexPath(bool createDirectory)
        {
            if (createDirectory && !Directory.Exists(k_QuickSearchLibraryPath))
                Directory.CreateDirectory(k_QuickSearchLibraryPath);
            return $"{k_QuickSearchLibraryPath}/{settings.guid}.{SearchIndexEntryImporter.version}.{GetIndexTypeSuffix()}";
        }

        private static void SaveIndex(string backupIndexPath, byte[] saveBytes, Task saveTask = null)
        {
            try
            {
                saveTask?.Report("Saving search index");
                var tempSave = Path.GetTempFileName();
                File.WriteAllBytes(tempSave, saveBytes);

                try
                {
                    if (File.Exists(backupIndexPath))
                        File.Delete(backupIndexPath);
                }
                catch (IOException)
                {
                    // ignore file index persistence operation, since it is not critical and will redone later.
                }

                File.Move(tempSave, backupIndexPath);
            }
            catch (IOException ex)
            {
                Debug.LogException(ex);
            }
        }

        private void SaveIndex(byte[] saveBytes, Action savedCallback = null)
        {
            string savePath = GetBackupIndexPath(createDirectory: true);
            var saveTask = new Task("Save", $"Saving {settings.name.ToLowerInvariant()} search index", (task, data) => savedCallback?.Invoke(), this);
            saveTask.RunThread(() =>
            {
                SaveIndex(savePath, saveBytes, saveTask);
                Dispatcher.Enqueue(() => saveTask.Resolve(new TaskData(saveBytes, index)));
            });
        }

        private void DeleteBackupIndex()
        {
            try
            {
                var backupIndexPath = GetBackupIndexPath(false);
                if (File.Exists(backupIndexPath))
                    File.Delete(backupIndexPath);
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Failed to delete backup index.\r\n{ex}");
            }
        }

        private void OnContentRefreshed(string[] updated, string[] removed, string[] moved)
        {
            if (!this || settings.options.disabled)
                return;
            var changeset = new AssetIndexChangeSet(updated, removed, moved, p => !index.SkipEntry(p, true));
            if (changeset.empty)
                return;
            IncrementalUpdate(changeset);
        }

        private void IncrementalUpdate(AssetIndexChangeSet changeset)
        {
            if (!this)
                return;

            m_UpdateQueue.Add(changeset);
            ProcessIncrementalUpdates();
        }

        private void ProcessIncrementalUpdates()
        {
            if (ready && !updating)
            {
                if (m_UpdateQueue.TryTake(out var diff))
                    ProcessIncrementalUpdate(diff);
            }
            else
            {
                Dispatcher.Enqueue(ProcessIncrementalUpdates, 0.5d);
            }
        }

        private void ProcessIncrementalUpdate(AssetIndexChangeSet changeset)
        {
            var updates = CreateArtifacts(changeset.updated);
            var taskName = $"Updating {settings.name.ToLowerInvariant()} search index";

            Interlocked.Increment(ref m_UpdateTasks);
            m_CurrentUpdateTask = new Task("Update", taskName, (task, data) => MergeDocuments(task, data, changeset), updates.Length, this);
            ResolveArtifacts(updates, null, m_CurrentUpdateTask, false);
        }

        private void MergeDocuments(Task task, TaskData data, AssetIndexChangeSet changeset)
        {
            if (task.canceled || task.error != null)
            {
                ResolveIncrementalUpdate(task);
                return;
            }

            var baseScore = settings.baseScore;
            var indexName = settings.name.ToLowerInvariant();
            var saveIndexCache = !Utils.IsRunningTests();
            var savePath = GetBackupIndexPath(createDirectory: true);

            task.Report("Merging changes to index...");
            task.RunThread(() =>
            {
                index.Merge(changeset.removed, data.combinedIndex, baseScore,
                    (di, indexer, count) => OnDocumentMerged(task, indexer, indexName, di, count));
                if (saveIndexCache)
                    SaveIndex(savePath, index.SaveBytes(), task);
            }, () => ResolveIncrementalUpdate(task));
        }

        private static void OnDocumentMerged(Task task, SearchIndexer indexer, string indexName, int documentIndex, int documentCount)
        {
            if (indexer != null)
                AddIndexNameArea(documentIndex, indexer, indexName);
            task.Report(documentIndex + 1, documentCount);
        }

        private void ResolveIncrementalUpdate(Task task)
        {
            if (task.error != null)
                Debug.LogException(task.error);
            m_CurrentUpdateTask?.Dispose();
            m_CurrentUpdateTask = null;
            Interlocked.Decrement(ref m_UpdateTasks);
            ProcessIncrementalUpdates();
            SearchService.RefreshWindows();
        }

        private IndexArtifact[] CreateArtifacts(in IList<string> assetPaths)
        {
            #if DEBUG_RESOLVING
            using (new DebugTimer("Create artifacts"))
            #endif
            {
                var artifacts = new IndexArtifact[assetPaths.Count];
                var indexImporterType = SearchIndexEntryImporter.GetIndexImporterType(settings.options.GetHashCode());
                for (int i = 0; i < assetPaths.Count; ++i)
                    artifacts[i] = new IndexArtifact(assetPaths[i], AssetDatabase.GUIDFromAssetPath(assetPaths[i]), indexImporterType);

                return artifacts;
            }
        }

        private static bool ProduceArtifact(IndexArtifact artifact)
        {
            artifact.state = AssetDatabaseExperimental.GetOnDemandArtifactProgress(artifact.key).state;

            if (artifact.state == OnDemandState.Failed)
                return false;

            if (artifact.state == OnDemandState.Available)
            {
                artifact.value = AssetDatabaseExperimental.LookupArtifact(artifact.key);
                return true;
            }

            if (artifact.timestamp == long.MaxValue)
            {
                artifact.timestamp = DateTime.UtcNow.Ticks;
                artifact.value = AssetDatabaseExperimental.ProduceArtifactAsync(artifact.key);
            }
            else
            {
                artifact.value = AssetDatabaseExperimental.LookupArtifact(artifact.key);
            }

            return true;
        }

        private static bool GetArtifactPaths(in ArtifactID id, out string[] paths)
        {
            return AssetDatabaseExperimental.GetArtifactPaths(id, out paths);
        }

        internal static SearchDatabase CreateDefaultIndex()
        {
            if (File.Exists(defaultSearchDatabaseIndexPath))
                File.Delete(defaultSearchDatabaseIndexPath);
            var defaultIndexFilename = Path.GetFileNameWithoutExtension(defaultSearchDatabaseIndexPath);
            var defaultIndexFolder = Path.GetDirectoryName(defaultSearchDatabaseIndexPath);
            var defaultDbIndexPath = SearchDatabaseImporter.CreateTemplateIndex("_Default", defaultIndexFolder, defaultIndexFilename);
            return ImportAsset(defaultDbIndexPath);
        }
    }
}

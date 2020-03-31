//#define DEBUG_INDEXING

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.QuickSearch.Providers
{
    class AssetIndexer : ObjectIndexer
    {
        public AssetIndexer(SearchDatabase.Settings settings)
            : base("assets", settings)
        {
            getEntryComponentsHandler = GetEntryComponents;
        }

        public IEnumerable<string> GetEntryComponents(string path, int index)
        {
            return SearchUtils.SplitFileEntryComponents(path, entrySeparators);
        }

        protected override System.Collections.IEnumerator BuildAsync(int progressId, object userData = null)
        {
            var paths = GetDependencies();
            var pathIndex = 0;
            var pathCount = (float)paths.Count;

            Start(clear: true);

            EditorApplication.LockReloadAssemblies();
            foreach (var path in paths)
            {
                var progressReport = pathIndex++ / pathCount;
                ReportProgress(progressId, path, progressReport, false);
                IndexDocument(path, false);
                yield return null;
            }
            EditorApplication.UnlockReloadAssemblies();

            Finish(() => {});
            while (!IsReady())
                yield return null;

            ReportProgress(progressId, $"Indexing Completed (Documents: {documentCount}, Indexes: {indexCount:n0})", 1f, true);
            yield return null;
        }

        public override List<string> GetDependencies()
        {
            string[] roots;
            if (settings.roots == null || settings.roots.Length == 0)
            {
                roots = new string[] { Path.GetDirectoryName(settings.path).Replace("\\", "/") };
            }
            else
            {
                roots = settings.roots.Where(r => Directory.Exists(r)).ToArray();
            }
            return AssetDatabase.FindAssets(String.Empty, roots).Select(AssetDatabase.GUIDToAssetPath).Where(path => !SkipEntry(path)).ToList();
        }

        public override void IndexDocument(string path, bool checkIfDocumentExists)
        {
            var documentIndex = AddDocument(path, checkIfDocumentExists);
            if (documentIndex < 0)
                return;

            IndexWordComponents(path, documentIndex, path);

            try
            {
                var fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                IndexWord(path, fileName, documentIndex, fileName.Length, true);

                if (path.StartsWith("Packages/", StringComparison.Ordinal))
                    IndexProperty(path, "a", "packages", documentIndex, saveKeyword: true);
                else
                    IndexProperty(path, "a", "assets", documentIndex, saveKeyword: true);

                if (!String.IsNullOrEmpty(name))
                    IndexProperty(path, "a", name, documentIndex, saveKeyword: true);

                if (settings.options.fstats)
                {
                    var fi = new FileInfo(path);
                    if (fi.Exists)
                    {
                        IndexNumber(path, "size", (double)fi.Length, documentIndex);
                        IndexProperty(path, "ext", fi.Extension.Replace(".", "").ToLowerInvariant(), documentIndex, saveKeyword: false);
                        IndexNumber(path, "age", (DateTime.Now - fi.LastWriteTime).TotalDays, documentIndex);
                    }
                }

                if (settings.options.properties || settings.options.types)
                {
                    bool wasLoaded = AssetDatabase.IsMainAssetAtPathLoaded(path);
                    var mainAsset = AssetDatabase.LoadMainAssetAtPath(path);
                    if (!mainAsset)
                        return;

                    if (!String.IsNullOrEmpty(mainAsset.name))
                        IndexWord(path, mainAsset.name, documentIndex, true);

                    Type at = mainAsset.GetType();
                    IndexWord(path, at.Name, documentIndex);
                    while (at != null && at != typeof(Object))
                    {
                        IndexProperty(path, "t", at.Name, documentIndex, saveKeyword: true);
                        at = at.BaseType;
                    }

                    if (PrefabUtility.GetPrefabAssetType(mainAsset) != PrefabAssetType.NotAPrefab)
                        IndexProperty(path, "t", "prefab", documentIndex, saveKeyword: true);

                    var labels = AssetDatabase.GetLabels(mainAsset);
                    foreach (var label in labels)
                        IndexProperty(path, "l", label, documentIndex, saveKeyword: true);

                    if (settings.options.properties)
                    {
                        IndexObject(path, mainAsset, documentIndex);

                        if (mainAsset is GameObject go)
                        {
                            foreach (var v in go.GetComponents(typeof(Component)))
                            {
                                if (!v)
                                    continue;
                                IndexPropertyComponents(path, documentIndex, "has", v.GetType().Name);
                                IndexObject(path, v, documentIndex);
                            }
                        }
                    }

                    if (!wasLoaded)
                    {
                        if (mainAsset && !mainAsset.hideFlags.HasFlag(HideFlags.DontUnloadUnusedAsset) &&
                            !(mainAsset is GameObject) &&
                            !(mainAsset is Component) &&
                            !(mainAsset is AssetBundle))
                        {
                            Resources.UnloadAsset(mainAsset);
                        }
                    }
                }

                if (settings.options.dependencies)
                {
                    foreach (var depPath in AssetDatabase.GetDependencies(path, true))
                    {
                        if (path == depPath)
                            continue;
                        var depName = Path.GetFileNameWithoutExtension(depPath);
                        IndexProperty(path, "ref", depName, documentIndex, saveKeyword: false);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}

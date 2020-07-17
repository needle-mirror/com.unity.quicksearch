//#define DEBUG_INDEXING

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.QuickSearch
{
    class AssetIndexer : ObjectIndexer
    {
        public AssetIndexer(SearchDatabase.Settings settings)
            : base(String.IsNullOrEmpty(settings.name) ? "assets" : settings.name, settings)
        {
        }

        public override IEnumerable<string> GetRoots()
        {
            if (settings.roots == null)
                settings.roots = new string[0];
            var roots = settings.roots.Where(r => Directory.Exists(r)).ToArray();
            if (roots.Length == 0)
                roots = new string[] { settings.root };
            return roots.Select(r => r.Replace("\\", "/"));
        }

        public override List<string> GetDependencies()
        {
            List<string> paths = new List<string>();
            foreach (var root in GetRoots())
            {
                paths.AddRange(Directory.GetFiles(root, "*.meta", SearchOption.AllDirectories)
                    .Select(path => path.Replace("\\", "/").Substring(0, path.Length-5))
                    .Where(path => File.Exists(path) && !SkipEntry(path)));
            }

            return paths;
        }

        public override Hash128 GetDocumentHash(string path)
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            return Utils.GetSourceAssetFileHash(guid);
        }

        public override void IndexDocument(string path, bool checkIfDocumentExists)
        {
            var documentIndex = AddDocument(path, checkIfDocumentExists);
            if (documentIndex < 0)
                return;

            AddDocumentHash(path, GetDocumentHash(path));
            IndexWordComponents(documentIndex, path);

            try
            {
                var fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                IndexWord(documentIndex, fileName, fileName.Length, true);
                IndexProperty(documentIndex, "name", fileName, saveKeyword: false);

                IndexWord(documentIndex, path, path.Length, exact: true);
                IndexProperty(documentIndex, "id", path, saveKeyword: false, exact: true);

                if (path.StartsWith("Packages/", StringComparison.Ordinal))
                    IndexProperty(documentIndex, "a", "packages", saveKeyword: true, exact: true);
                else
                    IndexProperty(documentIndex, "a", "assets", saveKeyword: true, exact: true);

                var fi = new FileInfo(path);
                if (fi.Exists)
                {
                    IndexNumber(documentIndex, "size", (double)fi.Length);
                    IndexProperty(documentIndex, "ext", fi.Extension.Replace(".", "").ToLowerInvariant(), saveKeyword: false);
                    IndexNumber(documentIndex, "age", (DateTime.Now - fi.LastWriteTime).TotalDays);

                    foreach (var dir in Path.GetDirectoryName(path).Split(new [] { '/', '\\' }).Skip(1))
                        IndexProperty(documentIndex, "dir", dir.ToLowerInvariant(), saveKeyword: false, exact: true);

                    IndexProperty(documentIndex, "t", "file", saveKeyword: true, exact: true);
                }
                else if (Directory.Exists(path))
                {
                    IndexProperty(documentIndex, "t", "folder", saveKeyword: true, exact: true);
                }

                var at = AssetDatabase.GetMainAssetTypeAtPath(path);
                var hasCustomIndexers = HasCustomIndexers(at);

                if (settings.options.types && at != null)
                {
                    IndexWord(documentIndex, at.Name);
                    while (at != null && at != typeof(Object) && at != typeof(GameObject))
                    {
                        IndexProperty(documentIndex, "t", at.Name, saveKeyword: true);
                        at = at.BaseType;
                    }
                }
                else if (at != null)
                {
                    IndexProperty(documentIndex, "t", at.Name, saveKeyword: true);
                }

                if (settings.options.properties || hasCustomIndexers)
                {
                    bool wasLoaded = AssetDatabase.IsMainAssetAtPathLoaded(path);

                    var mainAsset = path.EndsWith(".prefab") ? PrefabUtility.LoadPrefabContents(path) : AssetDatabase.LoadMainAssetAtPath(path);
                    if (!mainAsset)
                        return;

                    #if !UNITY_2020_2_OR_NEWER
                    var labels = AssetDatabase.GetLabels(mainAsset);
                    #else
                    var guid = AssetDatabase.GUIDFromAssetPath(path);
                    var labels = AssetDatabase.GetLabels(guid);
                    #endif
                    foreach (var label in labels)
                        IndexProperty(documentIndex, "l", label, saveKeyword: true);

                    if (hasCustomIndexers)
                        IndexCustomProperties(path, documentIndex, mainAsset);
                    
                    if (!String.IsNullOrEmpty(mainAsset.name))
                        IndexWord(documentIndex, mainAsset.name, true);

                    if (settings.options.properties)
                        IndexObject(documentIndex, mainAsset);

                    if (settings.options.extended)
                    {
                        var importSettings = AssetImporter.GetAtPath(path);
                        if (importSettings)
                            IndexObject(documentIndex, importSettings, dependencies: settings.options.dependencies, recursive: true);
                    }

                    if (settings.options.properties)
                    {
                        if (mainAsset is GameObject go)
                        {
                            IndexProperty(documentIndex, "t", "prefab", saveKeyword: true);
                            foreach (var v in go.GetComponents(typeof(Component)))
                            {
                                if (!v || v.GetType() == typeof(Transform))
                                    continue;
                                IndexPropertyComponents(documentIndex, "t", v.GetType().Name);

                                if (settings.options.properties)
                                    IndexObject(documentIndex, v, dependencies: settings.options.dependencies);
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
                        AddReference(documentIndex, depPath);
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

//#define DEBUG_INDEXING
#if UNITY_2020_1_OR_NEWER
#define ENABLE_ASYNC_INCREMENTAL_UPDATES
#endif

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.QuickSearch.Providers
{
    class ADBIndex : ScriptableObject
    {
        public const int version = 1;

        [SerializeField] public new string name;
        [SerializeField] public AssetIndexerSettings settings;
        [SerializeField, HideInInspector] public byte[] bytes;

        public AssetIndexer index { get; internal set; }

        internal static Dictionary<string, byte[]> incrementalIndexCache = new Dictionary<string, byte[]>();

        [System.Diagnostics.Conditional("DEBUG_INDEXING")]
        internal void Log(string callName, params string[] args)
        {
            Debug.Log($"({GetInstanceID()}) ADBIndex[<b>{name}</b>].<b>{callName}</b>[{string.Join(",", args)}]({bytes?.Length}, {index?.documentCount})");
        }

        internal void OnEnable()
        {
            Log("OnEnable");

            index = new AssetIndexer(name, settings);
            if (bytes == null)
                bytes = new byte[0];
            else
            {
                if (bytes.Length > 0)
                    Load();
            }
        }

        internal void OnDisable()
        {
            Log("OnDisable");
            AssetPostprocessorIndexer.contentRefreshed -= OnContentRefreshed;
        }

        private void Load()
        {
            Log("Load");
            if (index.LoadBytes(bytes))
                Setup();
        }

        private void Setup()
        {
            Log("Setup");
            AssetPostprocessorIndexer.contentRefreshed += OnContentRefreshed;
        }

        private void OnContentRefreshed(string[] updated, string[] removed, string[] moved)
        {
            if (!this || settings.disabled)
                return;
            var changeset = new AssetIndexChangeSet(updated, removed, moved, p => !index.SkipEntry(p, true));
            if (!changeset.empty)
            {
                Log("OnContentRefreshed", changeset.all.ToArray());

                #if ENABLE_ASYNC_INCREMENTAL_UPDATES
                Progress.RunTask($"Updating {index.name} index...", null, IncrementalUpdate, Progress.Options.None, -1, changeset);
                #else
                var it = IncrementalUpdate(-1, changeset);
                while (it.MoveNext())
                    ;
                #endif
            }
        }

        internal void IncrementalUpdate()
        {
            var changeset = AssetPostprocessorIndexer.GetDiff(p => !index.SkipEntry(p, true));
            if (!changeset.empty)
            {
                Log($"IncrementalUpdate", changeset.all.ToArray());
                IncrementalUpdate(changeset);
            }
        }

        internal void IncrementalUpdate(AssetIndexChangeSet changeset)
        {
            var it = IncrementalUpdate(-1, changeset);
            while (it.MoveNext())
                ;
        }

        private IEnumerator IncrementalUpdate(int progressId, object userData)
        {
            var set = (AssetIndexChangeSet)userData;
            #if ENABLE_ASYNC_INCREMENTAL_UPDATES
            var pathIndex = 0;
            var pathCount = (float)set.updated.Length;
            #endif
            index.Start();
            foreach (var path in set.updated)
            {
                #if ENABLE_ASYNC_INCREMENTAL_UPDATES
                if (progressId != -1)
                {
                    var progressReport = pathIndex++ / pathCount;
                    Progress.Report(progressId, progressReport, path);
                }
                #endif
                index.IndexAsset(path, true);
                yield return null;
            }

            index.Finish(() =>
            {
                bytes = index.SaveBytes();
                EditorUtility.SetDirty(this);

                var sourceAssetPath = AssetDatabase.GetAssetPath(this);
                if (!String.IsNullOrEmpty(sourceAssetPath))
                {
                    // Kick in an incremental import.
                    incrementalIndexCache[sourceAssetPath] = bytes;
                    AssetDatabase.ImportAsset(sourceAssetPath, ImportAssetOptions.Default);
                }
            }, set.removed);
        }

        public static IEnumerable<ADBIndex> Enumerate()
        {
            return AssetDatabase.FindAssets("t:ADBIndex a:all").Select(AssetDatabase.GUIDToAssetPath)
                .Select(path => AssetDatabase.LoadAssetAtPath<ADBIndex>(path))
                .Where(db => db != null && db.index != null && !db.settings.disabled)
                .Select(db => { db.Log("Enumerate"); return db; });
        }
    }
}

//#define DEBUG_INDEXING

using System;
using System.Collections.Generic;
using System.IO;
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

        [NonSerialized] public bool initialized = false;
        public AssetIndexer index { get; internal set; }

        internal static Dictionary<string, byte[]> incrementalIndexCache = new Dictionary<string, byte[]>();

        [System.Diagnostics.Conditional("DEBUG_INDEXING")]
        internal void Log(string callName, params string[] args)
        {
            Debug.Log($"({GetInstanceID()}) ADBIndex[{name}].{callName}[{string.Join(",", args)}]({bytes?.Length}, {index?.documentCount})");
        }

        internal void OnEnable()
        {
            Log("OnEnable");

            initialized = false;
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
            initialized = true;
            AssetPostprocessorIndexer.Enable();
            AssetPostprocessorIndexer.contentRefreshed -= OnContentRefreshed;
            AssetPostprocessorIndexer.contentRefreshed += OnContentRefreshed;
        }

        private void OnContentRefreshed(string[] updated, string[] removed, string[] moved)
        {
            if (!this || settings.disabled)
                return;
            var modifiedSet = updated.Concat(moved).Distinct().Where(p => !index.SkipEntry(p, true)).ToArray();
            if (modifiedSet.Length > 0 || removed.Length > 0)
            {
                Log("OnContentRefreshed", modifiedSet);

                index.Start();
                foreach (var path in modifiedSet)
                    index.IndexAsset(path, true);
                index.Finish(false, removed);
                bytes = index.SaveBytes();
                EditorUtility.SetDirty(this);

                var sourceAssetPath = AssetDatabase.GetAssetPath(this);
                if (!String.IsNullOrEmpty(sourceAssetPath))
                {
                    // Kick in an incremental import.
                    incrementalIndexCache[sourceAssetPath] = bytes;
                    AssetDatabase.ImportAsset(sourceAssetPath, ImportAssetOptions.Default);
                }
            }
                
        }

        public static IEnumerable<ADBIndex> Enumerate()
        {
            return AssetDatabase.FindAssets("t:ADBIndex").Select(AssetDatabase.GUIDToAssetPath)
                .Select(path => AssetDatabase.LoadAssetAtPath<ADBIndex>(path))
                .Where(db => db != null && db.index != null && !db.settings.disabled)
                .Select(db => { db.Log("Enumerate"); return db; });
        }
    }
}

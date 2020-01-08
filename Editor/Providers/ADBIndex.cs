//#define DEBUG_UBER_INDEXING

using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.QuickSearch.Providers
{
    [InitializeOnLoad]
    static class ADBIndex
    {
        static AssetIndexer s_GlobalIndexer;
        static bool s_IndexInitialized = false;

        static ADBIndex()
        {
            s_GlobalIndexer = new AssetIndexer();
            Debug.Assert(!s_GlobalIndexer.IsReady());
        }

        [InitializeOnLoadMethod]
        private static void DelayInitializeOnLoad()
        {
            if (SearchSettings.useUberIndexing)
                EditorApplication.delayCall += Initialize;
        }

        public static void Initialize()
        {
            EditorApplication.delayCall -= Initialize;
            using (new DebugTimer("Loading global asset index"))
            {
                s_GlobalIndexer.Build();
                AssetPostprocessorIndexer.Enable();
                AssetPostprocessorIndexer.contentRefreshed += OnContentRefreshed;

                s_IndexInitialized = true;
            }
        }

        public static AssetIndexer Get()
        {
            if (!s_IndexInitialized)
                Initialize();
            return s_GlobalIndexer;
        }

        private static void OnContentRefreshed(string[] updated, string[] removed, string[] moved)
        {
            s_GlobalIndexer.Start();
            foreach (var path in updated.Concat(moved).Distinct())
            {
                using (new DebugTimer($"Indexing {path}..."))
                    s_GlobalIndexer.IndexAsset(path, true);
            }
            using (new DebugTimer($"Merging changes {String.Join(", ", updated.Concat(removed).Concat(moved).Distinct())}..."))
                s_GlobalIndexer.Finish(true, removed);
        }

        #if DEBUG_UBER_INDEXING
        [MenuItem("Quick Search/Rebuild Über Index")]
        internal static void RebuildIndex()
        {
            if (System.IO.File.Exists(AssetIndexer.k_IndexFilePath))
                System.IO.File.Delete(AssetIndexer.k_IndexFilePath);
            #if UNITY_2020_1_OR_NEWER
            EditorUtility.RequestScriptReload();
            #else
            UnityEditorInternal.InternalEditorUtility.RequestScriptReload();
            #endif
        }
        #endif
    }
}

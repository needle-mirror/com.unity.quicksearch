//#define DEBUG_INDEXING

#if UNITY_2020_2_OR_NEWER
//#define USE_UMPE_INDEXING
#endif

using UnityEditor;
using UnityEditor.Experimental.AssetImporters;
using UnityEngine;

#if USE_UMPE_INDEXING
using System;
using Unity.MPE;
#endif

namespace Unity.QuickSearch.Providers
{
    [ExcludeFromPreset, ScriptedImporter(version: SearchDatabase.version, ext: "index")]
    public class SearchDatabaseImporter : ScriptedImporter
    {
        private SearchDatabase db { get; set; }

        /// This boolean state is used to delay the importation of indexes 
        /// that depends on assets that get imported to late such as prefabs.
        private static bool s_DelayImport = true;

        static SearchDatabaseImporter()
        {
            EditorApplication.delayCall += () => s_DelayImport = false;
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var filePath = ctx.assetPath;
            var jsonText = System.IO.File.ReadAllText(filePath);
            var settings = JsonUtility.FromJson<SearchDatabase.Settings>(jsonText);
            var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);

            #if DEBUG_INDEXING
            using (new DebugTimer($"Importing index {fileName}"))
            #endif
            {
                db = ScriptableObject.CreateInstance<SearchDatabase>();
                db.name = fileName;
                db.hideFlags = HideFlags.NotEditable;
                db.settings = settings;
                if (db.settings.path == null)
                    db.settings.path = filePath;
                if (db.settings.name == null)
                    db.settings.name = fileName;
                db.index = SearchDatabase.CreateIndexer(settings);

                if (!Reimport(filePath))
                {
                    if (ShouldDelayImport())
                    {
                        db.Log("Delayed Import");
                        EditorApplication.delayCall += () => AssetDatabase.ImportAsset(filePath);
                    }
                    else
                    {
                        Build();
                    }
                }

                ctx.AddObjectToAsset(fileName, db);
                ctx.SetMainObject(db);
            }
        }

        private bool ShouldDelayImport()
        {
            if (db.settings.type == "asset")
                return false;
            return s_DelayImport;
        }

        private void Cleanup()
        {
            Resources.UnloadUnusedAssets();
        }

        private bool Reimport(string assetPath)
        {
            if (!SearchDatabase.incrementalIndexCache.TryGetValue(assetPath, out var indexStream))
                return false;
            SearchDatabase.incrementalIndexCache.Remove(assetPath);
            db.bytes = indexStream;
            if (!db.index.LoadBytes(indexStream))
                return false;
            db.Log("Reimport");
            return true;
        }

        private void Build()
        {
            try
            {
                db.index.reportProgress += ReportProgress;
                db.index.Build();
                db.bytes = db.index.SaveBytes();
                db.Log("Build");
            }
            finally
            {
                db.index.reportProgress -= ReportProgress;
                EditorApplication.delayCall -= Cleanup;
                EditorApplication.delayCall += Cleanup;
            }
        }

        private void ReportProgress(int progressId, string description, float progress, bool finished)
        {
            EditorUtility.DisplayProgressBar($"Building {db.name} index...", description, progress);
            if (finished)
                EditorUtility.ClearProgressBar();
        }
    }
}

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
    [ExcludeFromPreset, ScriptedImporter(version: ADBIndex.version ^ SearchIndexEntry.version, ext: "index")]
    public class ADBIndexImporter : ScriptedImporter
    {
        private ADBIndex db { get; set; }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var jsonText = System.IO.File.ReadAllText(ctx.assetPath);
            var settings = JsonUtility.FromJson<AssetIndexerSettings>(jsonText);
            var fileName = System.IO.Path.GetFileNameWithoutExtension(ctx.assetPath);
            
            db = ScriptableObject.CreateInstance<ADBIndex>();
            db.name = fileName;
            db.hideFlags = HideFlags.NotEditable;
            db.settings = settings;
            db.index = new AssetIndexer(db.name, settings);

            if (!Reimport(ctx))
                Build();

            EditorApplication.delayCall -= Cleanup;
            EditorApplication.delayCall += Cleanup;

            ctx.AddObjectToAsset(fileName, db);
            ctx.SetMainObject(db);
        }

        private void Cleanup()
        {
            Resources.UnloadUnusedAssets();
        }

        private bool Reimport(AssetImportContext ctx)
        {
            if (!ADBIndex.incrementalIndexCache.TryGetValue(ctx.assetPath, out var indexStream))
                return false;
            ADBIndex.incrementalIndexCache.Remove(ctx.assetPath);
            db.bytes = indexStream;
            if (!db.index.LoadBytes(indexStream))
                return false;
            db.Log("Reimport");
            return true;
        }

        private void Build()
        {
            db.index.reportProgress += ReportProgress;
            db.index.Build();
            db.bytes = db.index.SaveBytes();
            db.index.reportProgress -= ReportProgress;

            db.Log("Build");
        }

        private void ReportProgress(int progressId, string description, float progress, bool finished)
        {
            EditorUtility.DisplayProgressBar($"Building {db.name} index...", description, progress);
            if (finished)
                EditorUtility.ClearProgressBar();
        }
    }
}

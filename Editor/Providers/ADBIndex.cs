//#define DEBUG_UBER_INDEXING

#if UNITY_2020_1_OR_NEWER
//#define USE_UMPE_INDEXING
#endif

#if UNITY_2020_1_OR_NEWER
#define USE_ASYNC_PROGRESS_BAR
#endif

using System.Linq;
using UnityEditor;
using UnityEngine;

#if USE_UMPE_INDEXING
using System;
using Unity.MPE;
#endif

namespace Unity.QuickSearch.Providers
{
    static class ADBIndex
    {
        private static AssetIndexer s_GlobalIndexer;
        private static bool s_IndexInitialized = false;

        const string k_ProgressTitle = "Building search index...";

        #if USE_UMPE_INDEXING

        const string k_RoleName = "qs_indexer";

        enum EventType
        {
            UmpExit,             // Editor > Indexer: Shutdown indexer
            UmpIndexingStarted,  // Indexer > Editor: Indicated that the indexing has started.
            UmpIndexingProgress, // Indexer > Editor: Shutdown indexer
            UmpIndexingFinished, // Indexer > Editor: Shutdown indexer
        }

        static class Slave
        {
            static void EmitLog(string msg, string stackTrace = "", LogType type = LogType.Log)
            {
                #if DEBUG_UBER_INDEXING
                EventService.Log($"[QS_BOT] {type}: {msg}");
                EventService.Tick();
                #endif
            }

            private static void EmitEvent(EventType type, object[] args = null)
            {
                EventService.Emit(type.ToString(), args);
                EventService.Tick();
            }

            [RoleProvider(k_RoleName, ProcessEvent.UMP_EVENT_AFTER_DOMAIN_RELOAD)]
            internal static void InitializeSlaveProcessDomain()
            {
                EditorApplication.delayCall -= Initialize;
                Application.logMessageReceived += EmitLog;

                EventService.On(nameof(EventType.UmpExit), (type, args) => EditorApplication.Exit(Convert.ToInt32(args[0])));

                EmitLog("Started");
                EmitEvent(EventType.UmpIndexingStarted);

                s_GlobalIndexer.useFinishThread = false;
                s_GlobalIndexer.reportProgress += (id, path, progress, finished) => EmitEvent(EventType.UmpIndexingProgress, new object[] { path, progress });
                s_GlobalIndexer.Build();

                EmitLog("Finished");
                EditorApplication.delayCall += () => EmitEvent(EventType.UmpIndexingFinished);
            }
        }

        static class Master
        {
            #if USE_ASYNC_PROGRESS_BAR
            private static int s_UMPEProgressId = -1;
            #endif

            private static void ReportUMPEProgress(string type, object[] args)
            {
                switch (type)
                {
                    case nameof(EventType.UmpIndexingStarted):
                    {
                        #if USE_ASYNC_PROGRESS_BAR
                        s_UMPEProgressId = Progress.Start(k_ProgressTitle, null, Progress.Options.Sticky);
                        #endif
                    } break;
                    case nameof(EventType.UmpIndexingProgress):
                    {
                        var description = (string)args[0];
                        var progressValue = Convert.ToSingle(args[1]);
                        #if USE_ASYNC_PROGRESS_BAR
                        ReportProgress(s_UMPEProgressId, description, progressValue, false);
                        #else
                        if (progressValue == 1f)
                            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, description);
                        #endif
                    } break;
                    case nameof(EventType.UmpIndexingFinished):
                    {
                        #if USE_ASYNC_PROGRESS_BAR
                        Progress.Finish(s_UMPEProgressId);
                        #endif

                        // Reload index
                        EventService.Emit(nameof(EventType.UmpExit), 0);
                        Initialize();
                    } break;
                    default:
                        throw new Exception($"Report progress event {type} not supported");
                }
            }

            [RoleProvider(ProcessLevel.UMP_MASTER, ProcessEvent.UMP_EVENT_AFTER_DOMAIN_RELOAD)]
            internal static void InitializeMasterProcess()
            {
                EventService.On(nameof(EventType.UmpIndexingStarted), ReportUMPEProgress);
                EventService.On(nameof(EventType.UmpIndexingProgress), ReportUMPEProgress);
                EventService.On(nameof(EventType.UmpIndexingFinished), ReportUMPEProgress);
            }
        }
        #endif

        static ADBIndex()
        {
            s_GlobalIndexer = new AssetIndexer();
            Debug.Assert(!s_GlobalIndexer.IsReady());
        }

        public static AssetIndexer Get()
        {
            return s_GlobalIndexer;
        }

        public static void Initialize()
        {
            if (s_IndexInitialized)
                return;

            if (s_GlobalIndexer.LoadIndexFromDisk(null, true))
            {
                s_IndexInitialized = true;
                AssetPostprocessorIndexer.Enable();
                AssetPostprocessorIndexer.contentRefreshed -= OnContentRefreshed;
                AssetPostprocessorIndexer.contentRefreshed += OnContentRefreshed;

                #if DEBUG_UBER_INDEXING
                Debug.Log("Search index loaded from disk");
                #endif
            }
            #if USE_UMPE_INDEXING
            else if (ProcessService.level == ProcessLevel.UMP_MASTER)
            {
                // Launch indexing bot.
                #if DEBUG_UBER_INDEXING
                Debug.Log("Launching quick search indexing bot...");
                #endif
                ProcessService.LaunchSlave(k_RoleName, 
                    "headless", "true", 
                    "disableManagedDebugger", "true",
                    "ump-cap", "disable-extra-resources");
            }
            #else
            else
            {
                s_GlobalIndexer.reportProgress += ReportProgress;
                #if USE_ASYNC_PROGRESS_BAR
                Progress.RunTask(k_ProgressTitle, null, s_GlobalIndexer.BuildAsync, Progress.Options.Sticky, -1);
                #else
                s_GlobalIndexer.Build();
                #endif
            }
            #endif
        }

        private static void OnContentRefreshed(string[] updated, string[] removed, string[] moved)
        {
            s_GlobalIndexer.Start();
            foreach (var path in updated.Concat(moved).Distinct())
                s_GlobalIndexer.IndexAsset(path, true);
            s_GlobalIndexer.Finish(true, removed);
        }

        private static void ReportProgress(int progressId, string description, float progress, bool finished)
        {
            #if USE_ASYNC_PROGRESS_BAR
            Progress.Report(progressId, progress, description);
            if (finished)
            {
                Progress.SetDescription(progressId, description);
                Progress.Finish(progressId);
            }
            #else
            EditorUtility.DisplayProgressBar(k_ProgressTitle, description, progress);
            if (finished)
            { 
                EditorUtility.ClearProgressBar();
                Debug.Log(description);
            }
            #endif
        }

        #if DEBUG_UBER_INDEXING
        [MenuItem("Quick Search/Rebuild Über Index")]
        #endif
        internal static void RebuildIndex()
        {
            if (System.IO.File.Exists(AssetIndexer.k_IndexFilePath))
                System.IO.File.Delete(AssetIndexer.k_IndexFilePath);
            #if UNITY_2019_3_OR_NEWER
            EditorUtility.RequestScriptReload();
            #else
            UnityEditorInternal.InternalEditorUtility.RequestScriptReload();
            #endif
        }
    }
}

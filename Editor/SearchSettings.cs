using System.Linq;
using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;

namespace Unity.QuickSearch
{
    internal static class SearchSettings
    {
        private const string k_KeyPrefix = "quicksearch";

        public const string settingsPreferencesKey = "Preferences/Quick Search";
        public static bool useDockableWindow { get; private set; }
        public static bool closeWindowByDefault { get; private set; }
        public static bool useFilePathIndexer { get; private set; }
        public static bool trackSelection { get; private set; }
        private static bool s_PriorityFoldout = true;

        static SearchSettings()
        {
            useDockableWindow = EditorPrefs.GetBool($"{k_KeyPrefix}.{nameof(useDockableWindow)}", false);
            closeWindowByDefault = EditorPrefs.GetBool($"{k_KeyPrefix}.{nameof(closeWindowByDefault)}", true);
            useFilePathIndexer = EditorPrefs.GetBool($"{k_KeyPrefix}.{nameof(useFilePathIndexer)}", false);
            trackSelection = EditorPrefs.GetBool($"{k_KeyPrefix}.{nameof(trackSelection)}", true);
        }

        private static void Save()
        {
            EditorPrefs.SetBool($"{k_KeyPrefix}.{nameof(useDockableWindow)}", useDockableWindow);
            EditorPrefs.SetBool($"{k_KeyPrefix}.{nameof(closeWindowByDefault)}", closeWindowByDefault);
            EditorPrefs.SetBool($"{k_KeyPrefix}.{nameof(useFilePathIndexer)}", useFilePathIndexer);
            EditorPrefs.SetBool($"{k_KeyPrefix}.{nameof(trackSelection)}", trackSelection);
        }

        [UsedImplicitly, SettingsProvider]
        private static SettingsProvider CreateSearchSettings()
        {
            return new SettingsProvider(settingsPreferencesKey, SettingsScope.User)
            {
                keywords = new[] { "quick", "omni", "search" },
                guiHandler = searchContext =>
                {
                    EditorGUIUtility.labelWidth = 450;
                    GUILayout.BeginHorizontal();
                    {
                        GUILayout.Space(10);
                        GUILayout.BeginVertical();
                        {
                            GUILayout.Space(10);
                            EditorGUI.BeginChangeCheck();
                            {
                                useDockableWindow = EditorGUILayout.Toggle(Styles.useDockableWindowContent, useDockableWindow);
                                if (useDockableWindow)
                                    closeWindowByDefault = EditorGUILayout.Toggle(Styles.closeWindowByDefaultContent, closeWindowByDefault);
                                trackSelection = EditorGUILayout.Toggle(Styles.trackSelectionContent, trackSelection);
                                useFilePathIndexer = EditorGUILayout.Toggle(Styles.useFilePathIndexerContent, useFilePathIndexer);
                                DrawPriorities();
                            }
                            if (EditorGUI.EndChangeCheck())
                            {
                                Save();
                                SearchService.Refresh();
                            }
                        }
                        GUILayout.EndVertical();
                    }
                    GUILayout.EndHorizontal();
                }
            };
        }

        private static void DrawPriorities()
        {
            #if UNITY_2019_1_OR_NEWER
            s_PriorityFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(s_PriorityFoldout, "Provider Priorities", EditorStyles.largeLabel);
            #else
            s_PriorityFoldout = EditorGUILayout.Foldout(s_PriorityFoldout, "Provider Priorities", EditorStyles.largeLabel);
            #endif
            if (s_PriorityFoldout)
            {
                int upper = 0;
                SearchProvider lowerProviderPriority = null;
                SearchProvider upperProviderPriority = null;
                foreach (var p in SearchService.Providers.OrderBy(p => p.priority))
                {
                    int lower = upper;
                    if (upperProviderPriority != null)
                    {
                        upperProviderPriority.priority = p.priority + 1;
                        EditorPrefs.SetInt($"{k_KeyPrefix}.{upperProviderPriority.name.id}.priority", upperProviderPriority.priority);
                        upperProviderPriority = null;
                        GUI.changed = true;
                    }
                    GUILayout.BeginHorizontal(GUILayout.MaxWidth(300));
                    GUILayout.Space(20);
                    GUILayout.Label(p.name.displayName);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(Styles.increasePriorityContent, Styles.priorityButton))
                        lowerProviderPriority = p;
                    if (GUILayout.Button(Styles.decreasePriorityContent, Styles.priorityButton))
                        upperProviderPriority = p;
                    GUILayout.EndHorizontal();
                    upper = p.priority;

                    if (lowerProviderPriority != null)
                    {
                        lowerProviderPriority.priority = lower - 1;
                        EditorPrefs.SetInt($"{k_KeyPrefix}.{lowerProviderPriority.name.id}.priority", lowerProviderPriority.priority);
                        lowerProviderPriority = null;
                        GUI.changed = true;
                    }
                }
            }
            #if UNITY_2019_1_OR_NEWER
            EditorGUILayout.EndFoldoutHeaderGroup();
            #endif
        }

        static class Styles
        {
            public static GUIStyle priorityButton = new GUIStyle("Button")
            {
                fixedHeight = 20,
                fixedWidth = 20,
                padding = new RectOffset(2, 1, 0, 1),
                margin = new RectOffset(1, 1, 1, 1),
                alignment = TextAnchor.MiddleCenter
            };

            public static GUIContent increasePriorityContent = new GUIContent("+", "Increase the provider's priority");
            public static GUIContent decreasePriorityContent = new GUIContent("-", "Decrease the provider's priority");

            public static GUIContent useDockableWindowContent = new GUIContent("Use a dockable window (instead of a modal popup window)");
            public static GUIContent closeWindowByDefaultContent = new GUIContent("Automatically close the window when an action is executed");
            public static GUIContent useFilePathIndexerContent = new GUIContent(
                "Enable fast indexing of file system entries under your project (experimental)", 
                "This indexing system takes around 1 and 10 seconds to build the first time you launch the quick search window. " +
                "It can take up to 30-40 mb of memory, but it provides very fast search for large projects. " +
                "Note that if you want to use standard asset database filtering, you will need to rely on `t:`, `a:`, etc.");
            public static GUIContent trackSelectionContent = new GUIContent(
                "Track the current selection in the quick search",
                "Tracking the current selection can alter other window state, such as pinging the project browser or the scene hierarchy window.");
        }
    }
}
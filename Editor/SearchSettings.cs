using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;

namespace Unity.QuickSearch
{
    internal static class SearchSettings
    {
        private const string k_KeyPrefix = "quicksearch";

        public static bool useDockableWindow { get; private set; }
        public static bool useFilePathIndexer { get; private set; }

        static SearchSettings()
        {
            useDockableWindow = EditorPrefs.GetBool($"{k_KeyPrefix}.{nameof(useDockableWindow)}", false);
            useFilePathIndexer = EditorPrefs.GetBool($"{k_KeyPrefix}.{nameof(useFilePathIndexer)}", false);
        }

        public static void Save()
        {
            EditorPrefs.SetBool($"{k_KeyPrefix}.{nameof(useDockableWindow)}", useDockableWindow);
            EditorPrefs.SetBool($"{k_KeyPrefix}.{nameof(useFilePathIndexer)}", useFilePathIndexer);
        }

        [UsedImplicitly, SettingsProvider]
        internal static SettingsProvider CreateSearchSettings()
        {
            return new SettingsProvider("Preferences/Quick Search", SettingsScope.User)
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
                                useFilePathIndexer = EditorGUILayout.Toggle(Styles.useFilePathIndexerContent, useFilePathIndexer);
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

        static class Styles
        {
            public static GUIContent useDockableWindowContent = new GUIContent("Use a dockable window (instead of a modal popup window)");
            public static GUIContent useFilePathIndexerContent = new GUIContent(
                "Enable fast indexing of file system entries under your project (experimental)", 
                "This indexing system takes around 1 and 10 seconds to build the first time you launch the quick search window. " +
                "It can take up to 30-40 mb of memory, but it provides very fast search for large projects. " +
                "Note that if you want to use standard asset database filtering, you will need to rely on `t:`, `a:`, etc.");
        }
    }
}
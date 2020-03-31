using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Experimental.SceneManagement;
using UnityEngine;

namespace Unity.QuickSearch
{
    public static class SearchUtils
    {
        public static readonly char[] entrySeparators = { '/', ' ', '_', '-', '.' };

        private static readonly Stack<StringBuilder> _SbPool = new Stack<StringBuilder>();

        public static string[] FindShiftLeftVariations(string word)
        {
            if (word.Length <= 1)
                return new string[0];

            var variations = new List<string>(word.Length) { word };
            for (int i = 1, end = word.Length - 1; i < end; ++i)
            {
                word = word.Substring(1);
                variations.Add(word);
            }

            return variations.ToArray();
        }
        
        /// <summary>
        /// Tokenize a string each Capital letter.
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        public static string[] SplitCamelCase(string source)
        {
            return Regex.Split(source, @"(?<!^)(?=[A-Z0-9])");
        }

        public static IEnumerable<string> SplitEntryComponents(string entry, char[] entrySeparators)
        {
            var nameTokens = entry.Split(entrySeparators).Distinct();
            var scc = nameTokens.SelectMany(s => SplitCamelCase(s)).Where(s => s.Length > 0);
            var fcc = scc.Aggregate("", (current, s) => current + s[0]);
            return new []{ fcc, entry }.Concat(scc.Where(s => s.Length > 1))
                                .Where(s => s.Length > 1)
                                .Select(s => s.ToLowerInvariant())
                                .Distinct();
        }

        public static IEnumerable<string> SplitFileEntryComponents(string path, char[] entrySeparators)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var nameTokens = name.Split(entrySeparators).Distinct().ToArray();
            var scc = nameTokens.SelectMany(s => SplitCamelCase(s)).Where(s => s.Length > 0).ToArray();
            var fcc = scc.Aggregate("", (current, s) => current + s[0]);
            return Enumerable.Empty<string>()
                             .Concat(scc.Where(s => s.Length > 1))
                             .Concat(new[] { Path.GetExtension(path).Replace(".", "") })
                             .Concat(FindShiftLeftVariations(fcc))
                             .Concat(nameTokens)
                             .Concat(path.Split(entrySeparators).Reverse())
                             .Where(s => s.Length > 1)
                             .Select(s => s.ToLowerInvariant())
                             .Distinct();
        }

        public static string GetTransformPath(Transform tform)
        {
            if (tform.parent == null)
                return "/" + tform.name;
            return GetTransformPath(tform.parent) + "/" + tform.name;
        }

        public static string GetObjectPath(UnityEngine.Object obj)
        {
            if (obj is GameObject go)
                return GetTransformPath(go.transform);
            if (obj is Component c)
                return GetTransformPath(c.gameObject.transform);
            return obj.name;
        }

        public static string GetHierarchyPath(GameObject gameObject, bool includeScene = true)
        {
            if (gameObject == null)
                return String.Empty;

            StringBuilder sb;
            if (_SbPool.Count > 0)
            {
                sb = _SbPool.Pop();
                sb.Clear();
            }
            else
            {
                sb = new StringBuilder(200);
            }

            try
            {
                if (includeScene)
                {
                    var sceneName = gameObject.scene.name;
                    if (sceneName == string.Empty)
                    {
                        var prefabStage = PrefabStageUtility.GetPrefabStage(gameObject);
                        if (prefabStage != null)
                            sceneName = "Prefab Stage";
                        else
                            sceneName = "Unsaved Scene";
                    }

                    sb.Append("<b>" + sceneName + "</b>");
                }

                sb.Append(GetTransformPath(gameObject.transform));

                var path = sb.ToString();
                sb.Clear();
                return path;
            }
            finally
            {
                _SbPool.Push(sb);
            }
        }

        public static string GetHierarchyAssetPath(GameObject gameObject, bool prefabOnly = false)
        {
            if (gameObject == null)
                return String.Empty;

            bool isPrefab = PrefabUtility.GetPrefabAssetType(gameObject.gameObject) != PrefabAssetType.NotAPrefab;
            if (isPrefab)
                return PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);

            if (prefabOnly)
                return null;

            return gameObject.scene.path;
        }

        public static void SelectMultipleItems(IEnumerable<SearchItem> items, bool focusProjectBrowser = false)
        {
            Selection.objects = items.Select(i => i.provider.toObject(i, typeof(UnityEngine.Object))).Where(o=>o).ToArray();
            if (Selection.objects.Length == 0)
                return;
            EditorApplication.delayCall += () =>
            {
                if(focusProjectBrowser)
                    EditorWindow.FocusWindowIfItsOpen(Utils.GetProjectBrowserWindowType());
                EditorApplication.delayCall += () => EditorGUIUtility.PingObject(Selection.objects.LastOrDefault());
            };
        }

        /// <summary>
        /// Helper function to match a string against the SearchContext. This will try to match the search query against each tokens of content (similar to the AddComponent menu workflow)
        /// </summary>
        /// <param name="context">Search context containing the searchQuery that we try to match.</param>
        /// <param name="content">String content that will be tokenized and use to match the search query.</param>
        /// <param name="useLowerTokens">Perform matching ignoring casing.</param>
        /// <returns>Has a match occurred.</returns>
        public static bool MatchSearchGroups(SearchContext context, string content, bool ignoreCase = false)
        {
            return MatchSearchGroups(context.searchQuery, context.searchWords, content, out _, out _,
                                     ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        private static bool MatchSearchGroups(string searchContext, string[] tokens, string content, out int startIndex, out int endIndex, StringComparison sc = StringComparison.OrdinalIgnoreCase)
        {
            startIndex = endIndex = -1;
            if (String.IsNullOrEmpty(content))
                return false;

            if (string.IsNullOrEmpty(searchContext))
                return false;

            if (searchContext == content)
            {
                startIndex = 0;
                endIndex = content.Length - 1;
                return true;
            }

            // Each search group is space separated
            // Search group must match in order and be complete.
            var searchGroups = tokens;
            var startSearchIndex = 0;
            foreach (var searchGroup in searchGroups)
            {
                if (searchGroup.Length == 0)
                    continue;

                startSearchIndex = content.IndexOf(searchGroup, startSearchIndex, sc);
                if (startSearchIndex == -1)
                {
                    return false;
                }

                startIndex = startIndex == -1 ? startSearchIndex : startIndex;
                startSearchIndex = endIndex = startSearchIndex + searchGroup.Length - 1;
            }

            return startIndex != -1 && endIndex != -1;
        }

    }
}
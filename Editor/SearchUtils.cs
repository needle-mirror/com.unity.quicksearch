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

        public static IEnumerable<string> SplitEntryComponents(string entry, char[] entrySeparators, int minIndexCharVariation, int maxIndexCharVariation)
        {
            var nameTokens = entry.Split(entrySeparators).Distinct();
            var scc = nameTokens.SelectMany(s => SplitCamelCase(s)).Where(s => s.Length > 0);
            var fcc = scc.Aggregate("", (current, s) => current + s[0]);
            return new []{ fcc, entry }.Concat(scc.Where(s => s.Length > 1))
                                .Where(s => s.Length > 0)
                                .Select(s => s.Substring(0, Math.Min(s.Length, maxIndexCharVariation)).ToLowerInvariant())
                                .Distinct();
        }

        public static IEnumerable<string> SplitFileEntryComponents(string path, char[] entrySeparators, int minIndexCharVariation, int maxIndexCharVariation)
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
                             .Where(s => s.Length > 0)
                             .Select(s => s.Substring(0, Math.Min(s.Length, maxIndexCharVariation)).ToLowerInvariant())
                             .Distinct();
        }

        public static string GetTransformPath(Transform tform)
        {
            if (tform.parent == null)
                return "/" + tform.name;
            return GetTransformPath(tform.parent) + "/" + tform.name;
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
    }
}
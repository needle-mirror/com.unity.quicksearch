using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Unity.QuickSearch
{
    public static class SearchUtils
    {
        public static readonly char[] entrySeparators = { '/', ' ', '_', '-', '.' };

        public static string[] FindShiftLeftVariations(string word)
        {
            var variations = new List<string>(word.Length) { word };
            for (int i = 1, end = word.Length - 1; i < end; ++i)
            {
                word = word.Substring(1);
                variations.Add(word);
            }

            return variations.ToArray();
        }

        public static string[] SplitCamelCase(string source)
        {
            return Regex.Split(source, @"(?<!^)(?=[A-Z])");
        }

        public static IEnumerable<string> SplitEntryComponents(string entry, char[] entrySeparators, int minIndexCharVariation, int maxIndexCharVariation)
        {
            var nameTokens = entry.Split(entrySeparators).Distinct().ToArray();
            var scc = nameTokens.SelectMany(SearchUtils.SplitCamelCase).Where(s => s.Length > 0).ToArray();
            return Enumerable.Empty<string>()
                             .Concat(scc)
                             .Where(s => s.Length >= minIndexCharVariation)
                             .Select(s => s.Substring(0, Math.Min(s.Length, maxIndexCharVariation)).ToLowerInvariant())
                             .Distinct();
        }

        public static IEnumerable<string> SplitFileEntryComponents(string path, char[] entrySeparators, int minIndexCharVariation, int maxIndexCharVariation)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var nameTokens = name.Split(entrySeparators).Distinct().ToArray();
            var scc = nameTokens.SelectMany(SearchUtils.SplitCamelCase).Where(s => s.Length > 0).ToArray();
            var fcc = scc.Aggregate("", (current, s) => current + s[0]);
            return Enumerable.Empty<string>()
                             .Concat(scc)
                             .Concat(new[] { Path.GetExtension(path).Replace(".", "") })
                             .Concat(SearchUtils.FindShiftLeftVariations(fcc))
                             .Concat(nameTokens.Select(s => s.ToLowerInvariant()))
                             .Concat(path.Split(entrySeparators).Reverse())
                             .Where(s => s.Length >= minIndexCharVariation)
                             .Select(s => s.Substring(0, Math.Min(s.Length, maxIndexCharVariation)).ToLowerInvariant())
                             .Distinct();
        }
    }
}
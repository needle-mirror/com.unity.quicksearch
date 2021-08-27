using System;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace UnityEditor.Search
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    class KeywordExampleAttribute : Attribute
    {
        public KeywordExampleAttribute(string query, string description)
        {
            this.query = query;
            this.description = description;
        }
        public string query;
        public string description;
    }

    class KeywordDocAttribute : Attribute
    {
        public KeywordDocAttribute(string label, string usage, string category, string description)
        {
            this.label = label;
            this.usage = usage;
            this.category = category;
            this.description = description;
        }

        public KeywordDocAttribute(string usage, string category = null, string description = null)
            : this(null, usage, category, description)
        {
        }

        public string keyword;
        public string label;
        public string usage;
        public string category;
        public string description;
    }

    static class KeywordCategories
    {
        public const string kProviderIdentifierTokens = "Provider identifier tokens";
        public const string k_SceneProviders = "Scene Providers";
    }

    static class Keywords
    {
        [KeywordDoc("Project (or assets)", "p:", KeywordCategories.kProviderIdentifierTokens, "Asset provider")]
        [KeywordExample("p: asteroid", "Searches for all `assets` containing the word `asteroid` in their name.")]
        public const string k_Project = "p";

        [KeywordDoc("Hierarchy (or current scene)", "h:", KeywordCategories.kProviderIdentifierTokens, "Hierarchy provider")]
        [KeywordExample("h: asteroid", "Searches **current scene** for `game objects` containing the word `asteroid` in their name.")]
        public const string k_Hierarchy = "h";

        [KeywordDoc("Menu", "me:", KeywordCategories.kProviderIdentifierTokens, "Menu provider")]
        [KeywordExample("me: test ru", "Searches all menus for items containing the word `test` and the word `ru` (ex: Test Runner).")]
        public const string k_Menu = "me";

        [KeywordDoc("Settings (preferences or project)", "se:", KeywordCategories.kProviderIdentifierTokens, "Settings provider")]
        [KeywordExample("se: quick", "Searches all **preferences** or **project settings** for sections containing the word `quick` (ex: QuickSearch preferences).")]
        public const string k_Settings = "se";

        [KeywordDoc("path:parent/to/child", KeywordCategories.k_SceneProviders, "Match path in scene")]
        [KeywordExample("path:Wall5/Br", "Searches all game objects whose path matches the partial path `Wall5/Br` (ex: `/Structures/Wall5/Brick`).")]
        [KeywordExample("path=/Structures/Wall5/Brick", "Searches all game objects whose scene path is **exactly** `/Structures/Wall5/Brick`.")]
        public const string k_Path = "path";

        private static KeywordDocAttribute[] s_NoKeywords = new KeywordDocAttribute[0];
        private static KeywordExampleAttribute[] s_NoExamples = new KeywordExampleAttribute[0];
        private static KeywordDocAttribute[] s_Keywords;
        private static Dictionary<string, KeywordExampleAttribute[]> s_ExamplesByKeyword;
        private static Dictionary<string, KeywordDocAttribute[]> s_KeywordsByCategory;
        public static IEnumerable<KeywordDocAttribute> allKeywords
        {
            get
            {
                if (s_Keywords == null)
                {
                    s_Keywords = TypeCache.GetFieldsWithAttribute<KeywordDocAttribute>()
                        .Select(InitKeyword)
                        .ToArray();
                }
                return s_Keywords;
            }
        }

        public static IEnumerable<KeywordDocAttribute> GetKeywords(string category)
        {
            if (s_KeywordsByCategory == null)
            {
                PopulateKeywordsCategories();
            }

            if (s_KeywordsByCategory.TryGetValue(category, out var keywords))
            {
                return keywords;
            }
            return s_NoKeywords;
        }

        public static IEnumerable<KeywordExampleAttribute> GetExamples(string keyword)
        {
            if (s_ExamplesByKeyword == null)
            {
                PopulateExamples();
            }

            if (s_ExamplesByKeyword.TryGetValue(keyword, out var examples))
            {
                return examples;
            }
            return s_NoExamples;
        }

        // TODO: Format markdown table for a category

        // TODO: Sanitize markdown from desc is needed (for autocompletion window)

        [MenuItem("Tools/Print Keywords")]
        public static void PrintKeywords()
        {
            PopulateKeywordsCategories();
            var str = new StringBuilder();
            foreach (var kvp in s_KeywordsByCategory)
            {
                var category = kvp.Key;
                var keywords = kvp.Value;
                str.AppendLine($"## {category}");
                foreach (var keyword in keywords)
                {
                    str.AppendLine($"- {keyword.label}: {keyword.description}. `{keyword.usage}`");
                    foreach (var example in GetExamples(keyword.keyword))
                    {
                        str.AppendLine($"   - `{example.query}` - {example.description}");
                    }
                }
            }

            Debug.Log(str);
        }

        private static KeywordDocAttribute InitKeyword(FieldInfo fi)
        {
            var attr = fi.GetCustomAttribute<KeywordDocAttribute>();
            attr.keyword = fi.GetValue(null) as string;
            return attr;
        }

        private static void PopulateKeywordsCategories()
        {
            var keywordsByCategory = new Dictionary<string, List<KeywordDocAttribute>>();
            foreach (var keyword in allKeywords)
            {
                if (!keywordsByCategory.TryGetValue(keyword.category, out var category))
                {
                    category = new List<KeywordDocAttribute>();
                    keywordsByCategory.Add(keyword.category, category);
                }

                category.Add(keyword);
            }
            s_KeywordsByCategory = keywordsByCategory.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray());
        }

        private static void PopulateExamples()
        {
            var examplesByKeyword = new Dictionary<string,List<KeywordExampleAttribute>>();
            var allExampleFields = TypeCache.GetFieldsWithAttribute<KeywordExampleAttribute>();
            foreach (var exampleField in allExampleFields)
            {
                var fieldValue = exampleField.GetValue(null) as string;
                if (!examplesByKeyword.TryGetValue(fieldValue, out var examples))
                {
                    examples = new List<KeywordExampleAttribute>();
                    examplesByKeyword.Add(fieldValue, examples);
                }
                examples.AddRange(exampleField.GetCustomAttributes<KeywordExampleAttribute>());
            }
            s_ExamplesByKeyword = examplesByKeyword.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray());
        }
    }
}

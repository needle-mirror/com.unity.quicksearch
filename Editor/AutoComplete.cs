using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Text;
using System.Text.RegularExpressions;

namespace UnityEditor.Search
{
    static class BuiltinPropositions
    {
        private static readonly string[] baseTypeFilters = new[]
        {
            "DefaultAsset", "AnimationClip", "AudioClip", "AudioMixer", "ComputeShader", "Font", "GUISKin", "Material", "Mesh",
            "Model", "PhysicMaterial", "Prefab", "Scene", "Script", "ScriptableObject", "Shader", "Sprite", "StyleSheet", "Texture", "VideoClip"
        };

        public static Dictionary<string, string> help = new Dictionary<string, string>
        {
            {"dir:", "Search parent folder name" },
            {"ext:", "Search by extension" },
            {"age:", "Search asset older than N days" },
            {"size:", "Search by asset file size" },
            {"ref:", "Search references" },
            {"a:assets", "Search project assets" },
            {"a:packages", "Search package assets" },
            {"t:file", "Search files" },
            {"t:folder", "Search folders" },
            {"name:", "Search by object name" },
            {"id:", "Search by unique id" },
        };

        static BuiltinPropositions()
        {
            foreach (var t in baseTypeFilters.Concat(TypeCache.GetTypesDerivedFrom<ScriptableObject>().Select(t => t.Name)))
                help[$"t:{t.ToLowerInvariant()}"] = $"Search {t} assets";

            foreach (var t in TypeCache.GetTypesDerivedFrom<Component>().Select(t => t.Name))
                help[$"t:{t.ToLowerInvariant()}"] = $"Search {t} components";
        }
    }

    class SearchProposition : IEquatable<SearchProposition>, IComparable<SearchProposition>
    {
        public readonly string label;
        public readonly string replacement;
        public string help;
        public readonly int priority;
        public readonly TextCursorPlacement moveCursor;

        public SearchProposition(string label, string replacement = null, string help = null, int priority = int.MaxValue, TextCursorPlacement moveCursor = TextCursorPlacement.MoveAutoComplete)
        {
            var kparts = label.Split(new char[] { '|' });
            this.label = kparts[0];
            this.replacement = replacement ?? this.label;
            if (kparts.Length >= 2)
                this.help = kparts[1];
            else
                this.help = help;
            this.priority = priority;
            this.moveCursor = moveCursor;
        }

        public int CompareTo(SearchProposition other)
        {
            var c = priority.CompareTo(other.priority);
            if (c != 0)
                return c;
            c = label.CompareTo(other.label);
            if (c != 0)
                return c;
            return string.Compare(help, other.help);
        }

        public bool Equals(SearchProposition other)
        {
            return label.Equals(other.label) && string.Equals(help, other.help);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                if (help != null)
                    return label.GetHashCode() ^ help.GetHashCode() ^ priority.GetHashCode();
                return label.GetHashCode() ^ priority.GetHashCode();
            }
        }

        public override bool Equals(object other)
        {
            if (other is string s)
                return label.Equals(s);
            return other is SearchProposition l && Equals(l);
        }
    }

    struct SearchPropositionOptions
    {
        public SearchPropositionOptions(string query, int cursor)
        {
            this.query = query;
            this.cursor = cursor;
            m_Word = m_Token = null;
            wordStartPos = wordEndPos = -1;
        }

        public readonly string query;
        public readonly int cursor;
        public int wordStartPos;
        public int wordEndPos;

        private string m_Word;
        private string m_Token;

        public string word
        {
            get
            {
                if (m_Word == null)
                    m_Word = GetWordAtCursorPosition(query, cursor, out wordStartPos, out wordEndPos);
                return m_Word;
            }
        }

        public string token
        {
            get
            {
                if (m_Token == null)
                    m_Token = GetTokenAtCursorPosition(query, cursor);
                return m_Token;
            }
        }

        private static string GetWordAtCursorPosition(string txt, int cursorIndex, out int startPos, out int endPos)
        {
            return GetTokenAtCursorPosition(txt, cursorIndex, out startPos, out endPos, ch => !char.IsLetterOrDigit(ch) && !(ch == '_'));
        }

        private static string GetTokenAtCursorPosition(string txt, int cursorIndex)
        {
            return GetTokenAtCursorPosition(txt, cursorIndex, out var _, out var _, ch => char.IsWhiteSpace(ch));
        }

        internal static void GetTokenBoundariesAtCursorPosition(string txt, int cursorIndex, out int startPos, out int endPos)
        {
            GetTokenAtCursorPosition(txt, cursorIndex, out startPos, out endPos, ch => char.IsWhiteSpace(ch));
        }

        private static string GetTokenAtCursorPosition(string txt, int cursorIndex, out int startPos, out int endPos, Func<char, bool> check)
        {
            if (txt.Length > 0 && (cursorIndex == txt.Length || char.IsWhiteSpace(txt[cursorIndex])))
                cursorIndex--;

            startPos = cursorIndex;
            endPos = cursorIndex;

            // Get the character's position.
            if (cursorIndex >= txt.Length || cursorIndex < 0)
                return "";

            for (; startPos >= 0; startPos--)
            {
                // Allow digits, letters, and underscores as part of the word.
                char ch = txt[startPos];
                if (check(ch)) break;
            }
            startPos++;

            // Find the end of the word.
            for (; endPos < txt.Length; endPos++)
            {
                char ch = txt[endPos];
                if (check(ch)) break;
            }
            endPos--;

            // Return the result.
            if (startPos > endPos)
                return "";
            return txt.Substring(startPos, endPos - startPos + 1);
        }
    }

    static class AutoComplete
    {
        private static string s_LastInput;
        private static int s_CurrentSelection = 0;
        private static List<SearchProposition> s_FilteredList = null;
        private static SearchProposition s_Empty = new SearchProposition(string.Empty);

        private static Rect position;
        private static Rect parent { get; set; }
        private static SearchPropositionOptions options { get; set; }
        private static SortedSet<SearchProposition> propositions { get; set; }

        public static bool enabled { get; set; }

        public static SearchProposition selection
        {
            get
            {
                if (!enabled || s_FilteredList == null)
                    return null;

                if (s_CurrentSelection < 0 || s_CurrentSelection >= s_FilteredList.Count)
                    return null;

                return s_FilteredList[s_CurrentSelection];
            }
        }

        public static int count
        {
            get
            {
                if (!enabled || s_FilteredList == null)
                    return 0;

                return s_FilteredList.Count;
            }
        }

        public static bool Show(SearchContext context, Rect parentRect)
        {
            var te = SearchField.GetTextEditor();

            parent = parentRect;
            options = new SearchPropositionOptions(context.searchText, te.cursorIndex);

            propositions = FetchPropositions(context, options);

            enabled = propositions.Count > 0;
            if (!enabled)
                return false;

            SearchAnalytics.SendEvent(null, SearchAnalytics.GenericEventType.QuickSearchAutoCompleteTab, options.token);
            UpdateCompleteList(te, options);
            return true;
        }

        public static void Draw(SearchContext context, ISearchView view)
        {
            if (!enabled)
                return;

            var evt = Event.current;
            if (evt.type == EventType.MouseDown && !position.Contains(evt.mousePosition))
            {
                evt.Use();
                Clear();
                return;
            }

            // Check if the cache filtered list should be updated
            if (evt.type == EventType.Repaint && !context.searchText.Equals(s_LastInput, StringComparison.Ordinal))
                UpdateCompleteList(SearchField.GetTextEditor());

            if (s_FilteredList == null)
                return;

            var autoFill = DrawItems(evt);
            if (autoFill != null && autoFill != s_Empty)
            {
                if (autoFill.moveCursor == TextCursorPlacement.MoveLineEnd)
                {
                    view.SetSearchText(autoFill.replacement, autoFill.moveCursor);
                }
                else if (!options.token.StartsWith(autoFill.replacement, StringComparison.OrdinalIgnoreCase))
                {
                    var searchText = context.searchText;

                    var replaceFrom = options.cursor - 1;
                    while (replaceFrom >= 0 && !char.IsWhiteSpace(searchText[replaceFrom]))
                        replaceFrom--;
                    if (replaceFrom == -1)
                        replaceFrom = 0;
                    else
                        replaceFrom++;

                    var replaceTo = searchText.IndexOf(' ', options.cursor);
                    if (replaceTo == -1)
                        replaceTo = searchText.Length;
                    var sb = new StringBuilder(searchText);
                    sb.Remove(replaceFrom, replaceTo - replaceFrom);
                    sb.Insert(replaceFrom, autoFill.replacement);

                    var insertion = sb.ToString();
                    SearchAnalytics.SendEvent(null, SearchAnalytics.GenericEventType.QuickSearchAutoCompleteInsertSuggestion, insertion);
                    view.SetSearchText(insertion, autoFill.moveCursor);
                }
                Clear();
            }
            else if (autoFill == s_Empty)
            {
                // No more results
                Clear();
            }
        }

        public static bool HandleKeyEvent(Event evt)
        {
            if (!enabled || evt.type != EventType.KeyDown)
                return false;

            if (evt.keyCode == KeyCode.DownArrow)
            {
                s_CurrentSelection = Utils.Wrap(s_CurrentSelection + 1, s_FilteredList.Count);
                evt.Use();
                return true;
            }
            else if (evt.keyCode == KeyCode.UpArrow)
            {
                s_CurrentSelection = Utils.Wrap(s_CurrentSelection - 1, s_FilteredList.Count);
                evt.Use();
                return true;
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                Clear();
                evt.Use();
                return true;
            }
            else if (IsKeySelection(evt))
            {
                return enabled;
            }

            return false;
        }

        public static bool IsHovered(in Vector2 mousePosition)
        {
            if (enabled && position.Contains(mousePosition))
                return true;
            return false;
        }

        public static void Clear()
        {
            if (!enabled)
                return;

            enabled = false;
            s_CurrentSelection = 0;
            s_LastInput = null;
            s_FilteredList = null;
        }

        private static SortedSet<SearchProposition> FetchPropositions(SearchContext context, SearchPropositionOptions options)
        {
            var propositions = new SortedSet<SearchProposition>();

            FillBuiltInPropositions(context, propositions);
            FillProviderPropositions(context, propositions);

            foreach (var p in propositions)
            {
                if (string.IsNullOrEmpty(p.help) && BuiltinPropositions.help.TryGetValue(p.replacement, out var helpText))
                    p.help = helpText;
            }

            return propositions;
        }

        private static void FillProviderPropositions(SearchContext context, SortedSet<SearchProposition> propositions)
        {
            var providers = context.providers.Where(p => context.filterId == null || context.filterId == p.filterId).ToList();
            var queryEmpty = string.IsNullOrWhiteSpace(context.searchText) && providers.Count(p => !p.isExplicitProvider) > 1;
            foreach (var p in providers)
            {
                if (queryEmpty)
                {
                    propositions.Add(new SearchProposition($"{p.filterId}", $"{p.filterId} ", p.name, p.priority));
                }
                else
                {
                    if (p.fetchPropositions == null)
                        continue;
                    var currentPropositions = p.fetchPropositions(context, options);
                    if (currentPropositions != null)
                        propositions.UnionWith(currentPropositions);
                }
            }
        }

        private static void FillBuiltInPropositions(SearchContext context, SortedSet<SearchProposition> propositions)
        {
            int builtPriority = -50;
            var savedQueries = SearchQuery.GetAllSearchQueryItems(context);
            foreach (var item in savedQueries)
            {
                if (item.data is SearchQuery sq)
                {
                    var helpText = sq.description;
                    if (string.IsNullOrEmpty(helpText))
                        helpText = sq.text;
                    helpText = $"<i>{helpText}</i> (Saved Query)";
                    propositions.Add(new SearchProposition(item.GetLabel(context, true), sq.text, helpText, priority: builtPriority, moveCursor: TextCursorPlacement.MoveLineEnd));
                    builtPriority += 10;
                }
            }

            foreach (var rs in SearchSettings.recentSearches.Take(5))
            {
                propositions.Add(new SearchProposition(rs, rs, "Recent search", priority: builtPriority, moveCursor: TextCursorPlacement.MoveLineEnd));
                builtPriority += 10;
            }
        }

        private static void UpdateCompleteList(in TextEditor te, in SearchPropositionOptions? baseOptions = null)
        {
            options = baseOptions ?? new SearchPropositionOptions(te.text, te.cursorIndex);
            position = CalcRect(te, parent.width * 0.55f, parent.height * 0.8f);

            var maxVisibleCount = Mathf.FloorToInt(position.height / EditorStyles.toolbarDropDown.fixedHeight);
            BuildCompleteList(options.token, maxVisibleCount, 0.4f);

            var maxLabelSize = 100f;
            var gc = new GUIContent();
            foreach (var e in s_FilteredList)
            {
                var sf = 5.0f;
                gc.text = e.label;
                Styles.autoCompleteItemLabel.CalcMinMaxWidth(gc, out var minWidth, out var maxWidth);
                sf += maxWidth;

                if (!string.IsNullOrEmpty(e.help))
                {
                    gc.text = e.help;
                    Styles.autoCompleteTooltip.CalcMinMaxWidth(gc, out minWidth, out maxWidth);
                    sf += maxWidth;
                }

                if (sf > maxLabelSize)
                    maxLabelSize = sf;
            }
            position.width = maxLabelSize;

            s_LastInput = te.text;
        }

        private static void BuildCompleteList(string input, int maxCount, float levenshteinDistance)
        {
            var uniqueSrc = new List<SearchProposition>(propositions);
            int srcCnt = uniqueSrc.Count;
            s_FilteredList = new List<SearchProposition>(Math.Min(maxCount, srcCnt));

            // Start with - slow
            SelectPropositions(ref srcCnt, maxCount, uniqueSrc, p => p.label.StartsWith(input, StringComparison.OrdinalIgnoreCase));

            s_FilteredList.Sort();

            // Contains - very slow
            SelectPropositions(ref srcCnt, maxCount, uniqueSrc, (p) =>
            {
                if (p.label.IndexOf(input, StringComparison.OrdinalIgnoreCase) != -1)
                    return true;
                if (p.help != null && p.help.IndexOf(input, StringComparison.OrdinalIgnoreCase) != -1)
                    return true;
                return false;
            });

            // Levenshtein Distance - very very slow.
            if (levenshteinDistance > 0f && input.Length > 3 && s_FilteredList.Count < maxCount)
            {
                var levenshteinInput = input.Replace("<", "").Replace("=", "").Replace(">", "");
                levenshteinDistance = Mathf.Clamp01(levenshteinDistance);
                SelectPropositions(ref srcCnt, maxCount, uniqueSrc, p =>
                {
                    int distance = Utils.LevenshteinDistance(p.label, levenshteinInput, caseSensitive: false);
                    return (int)(levenshteinDistance * p.label.Length) > distance;
                });
            }

            s_CurrentSelection = Math.Max(-1, Math.Min(s_CurrentSelection, s_FilteredList.Count - 1));
        }

        private static void SelectPropositions(ref int srcCnt, int maxCount, List<SearchProposition> source, Func<SearchProposition, bool> compare)
        {
            for (int i = 0; i < srcCnt && s_FilteredList.Count < maxCount; i++)
            {
                var p = source[i];
                if (!compare(p))
                    continue;

                s_FilteredList.Add(p);
                source.RemoveAt(i);
                srcCnt--;
                i--;
            }
        }

        private static SearchProposition DrawItems(Event evt)
        {
            int cnt = s_FilteredList.Count;
            if (cnt == 0)
                return s_Empty;

            position = new Rect(position.x, position.y, position.width, cnt * Styles.autoCompleteItemLabel.fixedHeight + 20f);
            GUI.Box(position, GUIContent.none, Styles.autoCompleteBackground);
            using (new GUI.ClipScope(position))
            {
                Rect lineRect = new Rect(1, 10, position.width - 2, Styles.autoCompleteItemLabel.fixedHeight);
                for (int i = 0; i < cnt; i++)
                {
                    if (DrawItem(evt, lineRect, i == s_CurrentSelection, s_FilteredList[i]))
                        return s_FilteredList[i];
                    lineRect.y += lineRect.height;
                }
            }

            return null;
        }

        private static bool IsKeySelection(Event evt)
        {
            var kc = evt.keyCode;
            return kc == KeyCode.Return || kc == KeyCode.KeypadEnter || kc == KeyCode.Tab;
        }

        private static string HightlightLabel(string label)
        {
            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(options.token) || label.IndexOf('<') != -1)
                return label;
            var escapedToken = Regex.Escape(options.token);
            return Regex.Replace(label, escapedToken, $"<b>{options.token}</b>", RegexOptions.IgnoreCase);
        }

        private static bool DrawItem(Event evt, Rect rect, bool selected, SearchProposition item)
        {
            var itemSelected = selected && evt.type == EventType.KeyDown && IsKeySelection(evt);
            if (itemSelected || GUI.Button(rect, HightlightLabel(item.label), selected ? Styles.autoCompleteSelectedItemLabel : Styles.autoCompleteItemLabel))
            {
                evt.Use();
                GUI.changed = true;
                return true;
            }

            if (!string.IsNullOrEmpty(item.help))
                GUI.Label(rect, item.help, Styles.autoCompleteTooltip);

            return false;
        }

        private static Rect CalcRect(TextEditor te, float maxWidth, float maxHeight)
        {
            return CalcRect(te, new Vector2(maxWidth, maxHeight), true);
        }

        private static Rect CalcRect(TextEditor te, Vector2 popupSize, bool setMinMax = false)
        {
            var itemHeight = Styles.autoCompleteItemLabel.fixedHeight;
            if (setMinMax)
                popupSize = new Vector2(popupSize.x, Mathf.Max(115f, Mathf.Min(propositions.Count * itemHeight, popupSize.y)));
            var popupOffset = new Vector2(te.position.xMin, te.position.yMax - 4f);
            return new Rect(te.graphicalCursorPos + popupOffset, popupSize);
        }
    }
}

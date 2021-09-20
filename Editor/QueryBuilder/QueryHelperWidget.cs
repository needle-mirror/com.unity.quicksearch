#if USE_QUERY_BUILDER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Profiling;
using UnityEngine;

namespace UnityEditor.Search
{
    [Serializable]
    class QueryHelperSearchGroup
    {
        public enum QueryType
        {
            Template,
            Recent
        }

        public struct QueryData : IComparable<QueryData>, IEquatable<QueryData>
        {
            public QueryBuilder builder;
            public ISearchQuery query;
            public string searchText;
            public GUIContent icon;
            public GUIContent description;
            public GUIContent tooltip;
            public Vector2 descSize;
            public QueryType type;

            public int CompareTo(QueryData other)
            {
                return -query.lastUsedTime.CompareTo(other.query.lastUsedTime);
            }

            public override int GetHashCode()
            {
                return query.displayName.GetHashCode() ^ query.lastUsedTime.GetHashCode() * 53;
            }

            public override bool Equals(object other)
            {
                return other is QueryData l && Equals(l);
            }

            public bool Equals(QueryData other)
            {
                return query.lastUsedTime == other.query.lastUsedTime && string.Equals(query.displayName, other.query.displayName, StringComparison.Ordinal);
            }
        }

        public QueryHelperSearchGroup(bool blockMode, string title)
        {
            this.blockMode = blockMode;
            displayName = title;
            this.title = new GUIContent(displayName);
            queries = new List<QueryData>();
            isExpanded = true;
        }

        public bool Add(ISearchQuery query, QueryType type, Texture2D icon)
        {
            if (string.IsNullOrEmpty(query.searchText))
                return false;

            QueryBuilder builder = null;
            if (blockMode)
            {
                builder = new QueryBuilder(query.searchText)
                {
                    drawBackground = false,
                    @readonly = true
                };
                foreach (var b in builder.blocks)
                    b.disableHovering = true;
            }

            if (builder == null || (builder.errors.Count == 0 && builder.blocks.Count > 0))
            {
                var desc = "";
                if (!string.IsNullOrEmpty(query.details))
                    desc = query.details;
                else if (!string.IsNullOrEmpty(query.displayName))
                    desc = query.displayName;

                queries.Add(new QueryData() { query = query, builder = builder,
                    icon = new GUIContent("", icon),
                    description = new GUIContent(desc, string.IsNullOrEmpty(query.filePath) ? null : QueryHelperWidget.Constants.templateIcon),
                    searchText = query.searchText,
                    type = type,
                    tooltip = new GUIContent("", query.searchText)
                });
                return true;
            }

            return false;
        }

        public void Add(string queryStr, QueryType type, Texture2D icon)
        {
            Add(QueryHelperWidget.CreateQuery(queryStr), type, icon);
        }

        public void UpdateTitle()
        {
            title.text = ($"{displayName} ({queries.Count})");
        }

        public bool HasQuery(ISearchQuery query, out int index)
        {
            index = queries.FindIndex(d => d.query == query);
            return index != -1;
        }

        public bool HasBuilder(QueryBuilder builder, out int index)
        {
            index = queries.FindIndex(d => d.builder == builder);
            return index != -1;
        }

        public bool blockMode;
        public Texture2D queryTypeIcon;
        public GUIContent title;
        public string displayName;
        public List<QueryData> queries;
        public QueryData[] filteredQueries;
        public bool isExpanded;
        public Vector2 scrollPos;

        public float expectedHeight {
            get {
                if (filteredQueries.Length == 0)
                    return 0;
                if (!isExpanded)
                    return QueryHelperWidget.Constants.kGroupHeaderHeight;
                return QueryHelperWidget.Constants.kGroupHeaderHeight + filteredQueries.Length * QueryHelperWidget.Constants.kBuilderHeight;
            }
        }
        public float compputedHeight;
        public float queryAreaHeight => compputedHeight - QueryHelperWidget.Constants.kGroupHeaderHeight;
    }

    [Serializable]
    class QueryHelperWidget
    {
        SearchProvider[] m_ActiveSearchProviders;
        QueryBuilder m_Areas;
        QueryHelperSearchGroup m_Searches;
        Rect m_WidgetRect;
        float m_LastLayoutWindowHeight;
        ISearchView m_SearchView;
        bool m_BlockMode;

        string m_CurrentAreaFilterId;
        double m_LastUpClick;
        const string k_All = "all";

        bool needGroupLayouting => m_LastLayoutWindowHeight != m_WidgetRect.height;

        internal static class Constants
        {
            public const float kBuilderHeight = 25f;
            public const float kGroupHeaderHeight = 22;
            public const float kMaxWindowHeight = 450;
            public const float kWindowWidth = 700;

            public const float kAreaSectionHeight = 45;
            public const float kAreaBuilderMaxHeight = 100;

            public const float kLeftPadding = 5;
            public const float kBuilderIconSize = 24;
            public const float kScrollbarOffset = 15;

            public static readonly Texture2D templateIcon = Utils.LoadIcon("UnityEditor/Search/SearchQueryAsset Icon");
            public static readonly Texture2D recentSearchesIcon = EditorGUIUtility.FindTexture("UndoHistory");
        }

        static class Styles
        {
            public static readonly GUIStyle categoryLabel = new GUIStyle("IN Title")
            {
                richText = true,
                wordWrap = false,
                alignment = TextAnchor.MiddleLeft
            };

            public static readonly GUIStyle foldout = new GUIStyle("IN Foldout");
            public static readonly GUIStyle description = new GUIStyle("label")
            {
                name = "description",
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(2, (int)Constants.kScrollbarOffset, 1,1),
                fixedHeight = Constants.kBuilderHeight
            };

            public static readonly GUIStyle builderRow = Utils.FromUSS("quick-search-builder-row");

            public static readonly GUIStyle textQuery = new GUIStyle("label")
            {
                wordWrap = true
            };
        }

        internal event Action<ISearchQuery> queryExecuted;

        public bool drawBorder;

        public QueryBlock currentQueryAreaBlock
        {
            get
            {
                return m_Areas.blocks.FirstOrDefault(b => GetAreaFilterId(b) == m_CurrentAreaFilterId);
            }
        }

        internal string GetAreaFilterId(QueryBlock block)
        {
            var area = block as QueryAreaBlock;
            if (area == null)
                return k_All;
            return string.IsNullOrEmpty(area.filterId) ? area.value : area.filterId;
        }

        public QueryHelperWidget(bool blockMode, ISearchView view = null)
        {
            m_BlockMode = blockMode;
            drawBorder = true;

            m_Areas = new QueryBuilder("")
            {
                drawBackground = false,
            };
            m_Areas.AddBlock(new QueryAreaBlock(m_Areas, k_All, ""));

            var allProviders = view != null && view.context != null ? view.context.GetProviders() : SearchService.GetActiveProviders();
            var generalProviders = allProviders.Where(p => !p.isExplicitProvider);
            var explicitProviders = allProviders.Where(p => p.isExplicitProvider);
            var providers = generalProviders.Concat(explicitProviders);
            if (blockMode)
                m_ActiveSearchProviders = providers.Where(p => p.fetchPropositions != null && p.id != "expression").ToArray();
            else
                m_ActiveSearchProviders = providers.ToArray();

            foreach (var p in m_ActiveSearchProviders)
            {
                m_Areas.AddBlock(new QueryAreaBlock(m_Areas, p));
            }
            m_Areas.@readonly = true;

            if (string.IsNullOrEmpty(m_CurrentAreaFilterId))
            {
                m_CurrentAreaFilterId = SearchSettings.helperWidgetCurrentArea;
            }
            m_Searches = new QueryHelperSearchGroup(m_BlockMode, L10n.Tr("Searches"));

            ChangeCurrentAreaFilter(m_CurrentAreaFilterId);

            PopulateSearches();
            RefreshSearches();
            BindSearchView(view);
        }

        public void BindSearchView(ISearchView view)
        {
            m_SearchView = view;
        }

        public Vector2 GetExpectedSize()
        {
            var height = Mathf.Min(m_Searches.expectedHeight + Constants.kAreaSectionHeight, Constants.kMaxWindowHeight);
            return new Vector2(Constants.kWindowWidth, height);
        }

        public void Draw(Event e, Rect widgetRect)
        {
            m_WidgetRect = widgetRect;
            m_WidgetRect.x += Search.Styles.tipsSection.margin.left + Search.Styles.tipsSection.padding.left;
            m_WidgetRect.xMax -= (Search.Styles.tipsSection.margin.right + Search.Styles.tipsSection.padding.right + Search.Styles.tipsSection.margin.left + Search.Styles.tipsSection.padding.left);
            m_WidgetRect.y += Search.Styles.tipsSection.margin.top + Search.Styles.tipsSection.padding.top;
            m_WidgetRect.yMax -= (Search.Styles.tipsSection.margin.bottom + Search.Styles.tipsSection.padding.bottom + Search.Styles.tipsSection.margin.top + Search.Styles.tipsSection.padding.top);

            GUILayout.BeginVertical(Search.Styles.tipsSection, GUILayout.Width(m_WidgetRect.width), GUILayout.ExpandHeight(true));
            GUILayout.Label("Narrow your search");

            var areaRect = new Rect(m_WidgetRect.x, GUILayoutUtility.GetLastRect().yMax, m_WidgetRect.width, Constants.kAreaBuilderMaxHeight);
            areaRect = DrawBuilder(e, m_Areas, areaRect);
            DrawSearches(e, m_Searches);
            EditorGUILayout.EndVertical();

            if (drawBorder)
                GUI.Label(m_WidgetRect, GUIContent.none, "grey_border");
        }

        internal static ISearchQuery CreateQuery(string queryStr)
        {
            var q = new SearchQuery()
            {
                searchText = queryStr
            };
            return q;
        }

        private void DrawSearches(Event e, QueryHelperSearchGroup group)
        {
            if (group.filteredQueries.Length == 0)
                return;

            GUILayout.Label(group.title);
            group.scrollPos = EditorGUILayout.BeginScrollView(group.scrollPos, GUIStyle.none, GUI.skin.verticalScrollbar);
            var maxDescriptionWidth = .4f * m_WidgetRect.width;
            for (var i = 0; i < group.filteredQueries.Length; ++i)
            {
                var queryData = group.filteredQueries[i];
                if (m_BlockMode)
                {
                    if (queryData.descSize.x == 0)
                        queryData.descSize = Styles.description.CalcSize(queryData.description);
                    var y = i == 0 ? 0 : GUILayoutUtility.GetLastRect().yMax;
                    var rowHeight = m_BlockMode ? queryData.builder.rect.height : Constants.kBuilderHeight;
                    var rowRect = new Rect(0, y, m_WidgetRect.width, rowHeight);
                    GUI.Label(rowRect, queryData.tooltip, Styles.builderRow);

                    var iconRect = new Rect(Constants.kLeftPadding, y, Constants.kBuilderIconSize, Constants.kBuilderIconSize);
                    GUI.Label(iconRect, queryData.icon, Search.Styles.panelHeaderIcon);

                    var descWidth = Mathf.Min(maxDescriptionWidth, queryData.descSize.x + Constants.kScrollbarOffset);
                    var descRect = new Rect(m_WidgetRect.width - descWidth - Constants.kLeftPadding, y, descWidth, Constants.kBuilderHeight);
                    GUI.Label(descRect, queryData.description, Styles.description);

                    var builderWidth = m_WidgetRect.width - descRect.width - iconRect.width;
                    var builderRect = new Rect(iconRect.xMin + Constants.kBuilderIconSize, y, builderWidth, Constants.kBuilderHeight);

                    DrawBuilder(e, queryData.builder, builderRect);
                }
                else
                {
                    var y = i == 0 ? 0 : GUILayoutUtility.GetLastRect().yMax;
                    var rowRect = new Rect(0, y, m_WidgetRect.width, m_BlockMode ? queryData.builder.rect.height : Constants.kBuilderHeight);
                    GUI.Label(rowRect, queryData.searchText != queryData.tooltip.tooltip ? queryData.tooltip : GUIContent.Temp(""), Styles.builderRow);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(queryData.icon, Search.Styles.panelHeaderIcon);
                    GUILayout.Label(queryData.searchText, Styles.textQuery);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(queryData.description, GUILayout.MaxHeight(Constants.kBuilderHeight));
                    GUILayout.EndHorizontal();

                    if (e.type == EventType.MouseUp && e.button == 0 && rowRect.Contains(e.mousePosition))
                    {
                        ExecuteQuery(queryData.query);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void PopulateSearches()
        {
            var builtinSearches = SearchTemplateAttribute.GetAllQueries();
            foreach (var q in builtinSearches)
            {
                m_Searches.Add(q, QueryHelperSearchGroup.QueryType.Template, SearchQuery.GetIcon(q));
            }

            foreach (var q in SearchQueryAsset.savedQueries.Cast<ISearchQuery>().Concat(SearchQuery.userQueries).Where(q => q.isSearchTemplate))
            {
                m_Searches.Add(q, QueryHelperSearchGroup.QueryType.Template, SearchQuery.GetIcon(q));
            }

            foreach (var a in EnumerateUniqueRecentSearches().Take(5))
                m_Searches.Add(a, QueryHelperSearchGroup.QueryType.Recent, Constants.recentSearchesIcon);
        }

        IEnumerable<string> EnumerateUniqueRecentSearches()
        {
            var recentSearches = SearchSettings.recentSearches.ToList();
            for (var i = 0; i < recentSearches.Count(); ++i)
            {
                var a = recentSearches[i];
                yield return a;

                for (var j = i + 1; j < recentSearches.Count();)
                {
                    var b = recentSearches[j];
                    var dist = Utils.LevenshteinDistance(a, b, false);
                    if (dist < 9)
                    {
                        recentSearches.RemoveAt(j);
                    }
                    else
                    {
                        j++;
                    }
                }
            }
        }

        private void RefreshSearches()
        {
            using (new EditorPerformanceTracker("helper_RefreshSearches"))
            {
                var isAll = k_All == m_CurrentAreaFilterId;
                var currentProvider = m_ActiveSearchProviders.FirstOrDefault(p => p.filterId == m_CurrentAreaFilterId);

                m_Searches.filteredQueries = m_Searches.queries.Where(q => {
                    if (isAll)
                        return true;
                    if (q.type == QueryHelperSearchGroup.QueryType.Recent)
                        return q.searchText.StartsWith(m_CurrentAreaFilterId);
                    return IsFilteredQuery(q.query, currentProvider);
                }).ToArray();
                m_Searches.UpdateTitle();

                m_LastLayoutWindowHeight = 0;
            }
        }

        private bool IsFilteredQuery(ISearchQuery query, SearchProvider provider)
        {
            if (provider == null)
                return false;

            if (query.searchText.StartsWith(provider.filterId))
                return true;

            var queryProviders = query.GetProviderIds().ToArray();
            return queryProviders.Length == 1 && queryProviders[0] == provider.id;
        }

        private Rect DrawBuilder(Event e, QueryBuilder builder, Rect r)
        {
            r = builder.Draw(e, r);
            if (e.type == EventType.MouseUp && e.button == 0 && r.Contains(e.mousePosition))
            {
                var now = EditorApplication.timeSinceStartup;
                var isDoubleClick = now - m_LastUpClick < 0.3;
                foreach (var b in builder.blocks)
                {
                    if (b.drawRect.Contains(e.mousePosition))
                    {
                        BlockClicked(builder, b, isDoubleClick);
                        e.Use();
                        break;
                    }
                }

                BuilderClicked(builder, isDoubleClick);
                e.Use();

                m_LastUpClick = now;
            }
            return r;
        }

        private void BuilderClicked(QueryBuilder builder, bool isDoubleClick)
        {
            ISearchQuery query = null;
            if (m_Searches.HasBuilder(builder, out var queryIndex))
            {
                query = m_Searches.queries[queryIndex].query;
            }

            if (query != null)
            {
                ExecuteQuery(query);
            }
        }

        private void ChangeCurrentAreaFilter(string newFilterArea)
        {
            m_CurrentAreaFilterId = newFilterArea;
            var currentAreaIndex = m_Areas.blocks.IndexOf(currentQueryAreaBlock);
            if (currentAreaIndex == -1)
            {
                m_CurrentAreaFilterId = GetAreaFilterId(m_Areas.blocks[0]);
                currentAreaIndex = 0;
            }
            m_Areas.SetSelection(currentAreaIndex);
            SearchSettings.helperWidgetCurrentArea = m_CurrentAreaFilterId;
        }

        private void BlockClicked(QueryBuilder builder, QueryBlock block, bool isDoubleClick)
        {
            if (builder == m_Areas)
            {
                if (isDoubleClick)
                {
                    var query = CreateQuery(block.ToString());
                    ExecuteQuery(query);
                }
                else
                {
                    ChangeCurrentAreaFilter(GetAreaFilterId(block));
                    RefreshSearches();
                }
            }
        }

        private void ExecuteQuery(ISearchQuery query)
        {
            if (m_SearchView != null)
                ((QuickSearch)m_SearchView).ExecuteSearchQuery(query);
            queryExecuted?.Invoke(query);
        }
    }
}
#endif

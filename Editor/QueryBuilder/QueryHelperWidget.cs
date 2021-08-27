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
        public struct QueryData
        {
            public QueryBuilder builder;
            public ISearchQuery query;
            public string searchText;
            public GUIContent icon;
            public GUIContent description;
            public Vector2 descSize;
        }

        public QueryHelperSearchGroup(string title, Texture2D icon)
        {
            displayName = title;
            this.title = new GUIContent(displayName);
            queries = new List<QueryData>();
            isExpanded = true;
            queryTypeIcon = icon;
        }

        public bool Add(ISearchQuery query)
        {
            if (string.IsNullOrEmpty(query.searchText))
                return false;

            // TODO: remove context
            var builder = new QueryBuilder(SearchService.CreateContext(query.searchText))
            {
                drawBackground = false,
                @readonly = true
            };
            if (builder.errors.Count == 0 && builder.blocks.Count > 0)
            {
                var desc = query.searchText;
                if (query != null)
                {
                    if (!string.IsNullOrEmpty(query.details))
                        desc = query.details;
                    else if (!string.IsNullOrEmpty(query.displayName))
                        desc = query.displayName;
                    else
                        desc = query.searchText;
                }
                queries.Add(new QueryData() { query = query, builder = builder,
                    icon = new GUIContent("", queryTypeIcon),
                    description = new GUIContent(desc),
                    searchText = query.searchText
                });
                return true;
            }

            return false;
        }

        public void Add(string queryStr)
        {
            Add(QueryHelperWidget.CreateQuery(queryStr));
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
        QueryHelperSearchGroup m_SearchTemplates;
        QueryHelperSearchGroup m_RecentSearches;
        Rect m_WidgetRect;
        float m_LastLayoutWindowHeight;
        ISearchView m_SearchView;

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
                alignment = TextAnchor.MiddleRight
            };

            public static readonly GUIStyle builderRow = Utils.FromUSS("quick-search-builder-row");
        }

        internal event Action<QueryBuilder, QueryBlock, bool> blockSelected;

        public bool drawBorder;

        public QueryHelperWidget(ISearchView view = null)
        {
            drawBorder = true;
            m_Areas = new QueryBuilder(view?.context ?? SearchService.CreateContext("")) { drawBackground = false };
            m_Areas.AddBlock(new QueryAreaBlock(m_Areas, k_All, ""));
            m_ActiveSearchProviders = SearchService.GetActiveProviders().Where(p => p.fetchPropositions != null && p.id != "expression").ToArray();
            foreach (var p in m_ActiveSearchProviders)
            {
                m_Areas.AddBlock(new QueryAreaBlock(m_Areas, p));
            }
            m_Areas.@readonly = true;

            m_CurrentAreaFilterId = m_CurrentAreaFilterId ?? k_All;
            m_SearchTemplates = new QueryHelperSearchGroup(L10n.Tr("Search Templates"), Utils.LoadIcon("UnityEditor/Search/SearchQueryAsset Icon"));
            m_RecentSearches = new QueryHelperSearchGroup(L10n.Tr("Recent Searches"), EditorGUIUtility.FindTexture("UndoHistory"));

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
            var height = Mathf.Min(m_RecentSearches.expectedHeight + m_SearchTemplates.expectedHeight + Constants.kAreaSectionHeight, Constants.kMaxWindowHeight);
            return new Vector2(Constants.kWindowWidth, height);
        }

        public void Draw(Event e, Rect widgetRect)
        {
            m_WidgetRect = widgetRect;

            GUILayout.BeginVertical();
            GUILayout.Label("Narrow your search");

            var areaRect = new Rect(widgetRect.x + Constants.kLeftPadding, GUILayoutUtility.GetLastRect().yMax, m_WidgetRect.width, Constants.kAreaBuilderMaxHeight);
            areaRect = DrawBuilder(e, m_Areas, areaRect);
            if (needGroupLayouting && e.type == EventType.Repaint)
                ComputeSearchGroupLayout(areaRect.height + 17);
            DrawSearches(e, m_SearchTemplates);
            DrawSearches(e, m_RecentSearches);
            EditorGUILayout.EndVertical();

            if (drawBorder)
                GUI.Label(widgetRect, GUIContent.none, "grey_border");
        }

        internal static ISearchQuery CreateQuery(string queryStr)
        {
            var q = new SearchQuery()
            {
                searchText = queryStr,
                displayName = queryStr
            };
            return q;
        }

        private void ComputeSearchGroupLayout(float offset)
        {
            using (new EditorPerformanceTracker("Helper_ComputeSearchGroupLayout"))
            {
                m_LastLayoutWindowHeight = m_WidgetRect.height;
                var groups = new[] { m_SearchTemplates, m_RecentSearches };

                var availableHeight = m_WidgetRect.height - offset;
                var expectedHeight = 0f;
                var notEmptyGroup = 0;
                foreach (var g in groups)
                {
                    g.compputedHeight = g.expectedHeight;
                    expectedHeight += g.expectedHeight;
                    if (g.filteredQueries.Length > 0)
                        notEmptyGroup++;
                }
                if (expectedHeight < availableHeight)
                {
                    // Allocate extra space t0 the last group:
                    groups.Last().compputedHeight += (availableHeight - expectedHeight);
                    return;
                }

                var heightPerGroup = availableHeight / notEmptyGroup;
                // Check for smaller group and gather extra space
                var fittedGroup = 0;
                foreach (var g in groups)
                {
                    if (g.expectedHeight <= heightPerGroup)
                    {
                        g.compputedHeight = g.expectedHeight;
                        availableHeight -= g.expectedHeight;
                        fittedGroup++;
                    }
                    else
                    {
                        g.compputedHeight = 0;
                    }
                }

                // Allocate extra space
                heightPerGroup = availableHeight / (groups.Length - fittedGroup);
                foreach (var g in groups)
                {
                    if (g.compputedHeight == 0)
                    {
                        g.compputedHeight = heightPerGroup;
                    }
                }
            }
        }

        private void DrawSearches(Event e, QueryHelperSearchGroup group)
        {
            if (group.filteredQueries.Length == 0)
                return;

            if (GUILayout.Button(group.title, Styles.categoryLabel, GUILayout.ExpandWidth(true)))
            {
                group.isExpanded = !group.isExpanded;
            }
            if (e.type == EventType.Repaint)
            {
                var btnRect = GUILayoutUtility.GetLastRect();
                btnRect.x += 3;
                btnRect.width = 15;
                Styles.foldout.Draw(btnRect, false, false, group.isExpanded, false);
            }

            if (group.isExpanded)
            {
                var height = group.queryAreaHeight;
                group.scrollPos = EditorGUILayout.BeginVerticalScrollView(group.scrollPos, false, GUIStyle.none, GUI.skin.scrollView, GUILayout.Height(height));
                for (var i = 0; i < group.filteredQueries.Length; ++i)
                {
                    var y = i == 0 ? 0 : GUILayoutUtility.GetLastRect().yMax;
                    var queryData = group.filteredQueries[i];
                    if (queryData.descSize.x == 0)
                    {
                        queryData.descSize = Styles.description.CalcSize(queryData.description);
                    }
                    var builder = queryData.builder;
                    var rowRect = new Rect(0, y, m_WidgetRect.width, builder.rect.height);
                    GUI.Label(rowRect, "", Styles.builderRow);

                    var iconRect = new Rect(Constants.kLeftPadding, y, Constants.kBuilderIconSize, Constants.kBuilderIconSize);
                    GUI.Label(iconRect, queryData.icon, Search.Styles.panelHeaderIcon);

                    var descRect = new Rect(m_WidgetRect.width - queryData.descSize.x - Constants.kLeftPadding, y, queryData.descSize.x, Constants.kBuilderHeight);
                    GUI.Label(descRect, queryData.description);

                    var builderWidth = m_WidgetRect.width - descRect.width - iconRect.width;
                    var builderRect = new Rect(iconRect.xMin + Constants.kBuilderIconSize, y, builderWidth, Constants.kBuilderHeight);

                    DrawBuilder(e, builder, builderRect);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void PopulateSearches()
        {
            using (new EditorPerformanceTracker("helper_PopulateSearches"))
            {
                var builtinSearches = SearchTemplateAttribute.GetAllQueries();
                foreach (var s in builtinSearches)
                {
                    m_SearchTemplates.Add(s);
                }

                foreach (var pq in SearchQueryAsset.savedQueries.Cast<ISearchQuery>().Concat(SearchQuery.userQueries).Where(q => q.isSearchTemplate))
                {
                    m_SearchTemplates.Add(pq);
                }
                m_SearchTemplates.queries.Sort((a, b) =>
                {
                    if (a.query.lastUsedTime == 0 && b.query.lastUsedTime == 0)
                        return a.query.displayName.CompareTo(b.query.displayName);
                    return -(int)(a.query.lastUsedTime - b.query.lastUsedTime);
                });

                var recentSearches = SearchSettings.recentSearches.ToList();
                for (var i = 0; i < recentSearches.Count(); ++i)
                {
                    var a = recentSearches[i];
                    m_RecentSearches.Add(a);
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
        }

        private void RefreshSearches()
        {
            using (new EditorPerformanceTracker("helper_RefreshSearches"))
            {
                var isAll = k_All == m_CurrentAreaFilterId;
                var currentProvider = m_ActiveSearchProviders.FirstOrDefault(p => p.filterId == m_CurrentAreaFilterId);

                m_SearchTemplates.filteredQueries = m_SearchTemplates.queries.Where(q => isAll || IsFilteredQuery(q.query, currentProvider)).ToArray();
                m_SearchTemplates.UpdateTitle();

                m_RecentSearches.filteredQueries = m_RecentSearches.queries.Where(q => isAll || q.searchText.StartsWith(m_CurrentAreaFilterId)).ToArray();
                m_RecentSearches.UpdateTitle();

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

            // Assume explicit provider Query uses a filterId (above) or have a single provider enabled.
            if (provider.isExplicitProvider)
                return queryProviders.Length == 1 && queryProviders[0] == provider.id;

            if (m_ActiveSearchProviders.Any(p => query.searchText.StartsWith(p.filterId)))
            {
                // Explicit query that matches another provider id.
                return false;
            }

            return queryProviders.Contains(provider.id);
        }

        private Rect DrawBuilder(Event e, QueryBuilder builder, Rect r)
        {
            r = builder.Draw(e, r);
            if (e.type == EventType.MouseUp && e.button == 0 && r.Contains(e.mousePosition))
            {
                var now = EditorApplication.timeSinceStartup;
                var mouseInBuilder = e.mousePosition - r.position;
                var isDoubleClick = now - m_LastUpClick < 0.3;
                foreach (var b in builder.blocks)
                {
                    if (b.rect.Contains(mouseInBuilder))
                    {
                        BlockClicked(builder, b, isDoubleClick);
                        break;
                    }
                }

                BuilderClicked(builder, isDoubleClick);

                m_LastUpClick = now;
            }
            return r;
        }

        private void BuilderClicked(QueryBuilder builder, bool isDoubleClick)
        {
            ISearchQuery query = null;
            var queryIndex = -1;
            if (m_RecentSearches.HasBuilder(builder, out queryIndex))
            {
                query = m_RecentSearches.queries[queryIndex].query;
            }
            else if (m_SearchTemplates.HasBuilder(builder, out queryIndex))
            {
                query = m_SearchTemplates.queries[queryIndex].query;
            }

            if (query != null)
            {
                ExecuteQuery(query, builder, null, isDoubleClick);
            }
        }

        private void BlockClicked(QueryBuilder builder, QueryBlock block, bool isDoubleClick)
        {
            if (builder == m_Areas)
            {
                if (isDoubleClick)
                {
                    var query = CreateQuery(block.ToString());
                    ExecuteQuery(query, builder, block, isDoubleClick);
                }
                else
                {
                    var area = (QueryAreaBlock)block;
                    m_CurrentAreaFilterId = string.IsNullOrEmpty(area.filterId) ? area.value : area.filterId;
                    RefreshSearches();
                }
            }
        }

        private void ExecuteQuery(ISearchQuery query, QueryBuilder builder, QueryBlock block, bool isDoubleClick)
        {
            if (m_SearchView != null)
                ((QuickSearch)m_SearchView).ExecuteSearchQuery(query);
            blockSelected?.Invoke(builder, block, isDoubleClick);
        }
    }
}
#endif

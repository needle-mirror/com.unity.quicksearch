#if USE_QUERY_BUILDER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace UnityEditor.Search
{
    class QueryBuilder : IQuerySource
    {
        const float blockSpacing = 4f;
        const float builderPadding = 2f;
        const float minHeight = 20f;

        private Rect m_LayoutRect;
        private float m_BuilderWidth;
        private float m_BuilderHeight;
        private QueryAddNewBlock m_AddBlock;
        private QueryTextFieldBlock m_TextBlock;
        private SearchField m_SearchField;

        private readonly SearchContext m_Context;
        private readonly ISearchView m_SearchView;
        private readonly QueryEngine<QueryBlock> m_QueryEngine;

        private bool m_ReadOnly;
        public bool @readonly
        {
            get
            {
                return m_ReadOnly;
            }
            set
            {
                m_ReadOnly = value;
                if (blocks != null)
                {
                    foreach (var b in blocks)
                    {
                        b.@readonly = value;
                    }
                }
            }
        }

        public Rect rect => m_LayoutRect;
        public bool drawBackground;
        public List<QueryBlock> blocks { get; private set; }
        public List<QueryError> errors { get; private set; }

        public SearchContext context => m_Context ?? searchView.context;
        public ISearchView searchView => m_SearchView ?? m_Context.searchView;

        public bool valid => errors.Count == 0;

        protected QueryBuilder()
        {
            errors = new List<QueryError>();
            blocks = new List<QueryBlock>();
            m_QueryEngine = new QueryEngine<QueryBlock>(validateFilters: false);
            drawBackground = true;
        }

        protected QueryBuilder(ISearchView searchView)
            : this()
        {
            m_SearchView = searchView;
        }

        public QueryBuilder(SearchContext searchContext, SearchField searchField = null)
            : this(searchContext.searchView)
        {
            m_Context = searchContext;
            m_SearchField = searchField;
            Build();
        }

        public IEnumerable<QueryBlock> EnumerateBlocks()
        {
            foreach (var b in blocks)
                yield return b;

            if (m_AddBlock != null)
                yield return m_AddBlock;
            if (m_TextBlock != null)
                yield return m_TextBlock;
        }

        public void Repaint()
        {
            searchView?.Repaint();
        }

        public Rect Draw(Event evt, Rect rect)
        {
            if (evt.type == EventType.Layout || rect != m_LayoutRect)
                LayoutBlocks(rect.width - 20f);

            m_LayoutRect = rect;
            m_LayoutRect.height = m_BuilderHeight;
            if (m_SearchField == null)
            {
                GUILayoutUtility.GetRect(m_LayoutRect.width, m_LayoutRect.height);
            }
            else
            {
                GUILayoutUtility.GetRect(m_LayoutRect.width, m_LayoutRect.height + Styles.searchField.margin.bottom, Styles.queryBuilderToolbar);
            }

            if (drawBackground)
                DrawBackground(evt);
            DrawBlocks(evt);

            if (!@readonly && evt.type == EventType.ContextClick && m_LayoutRect.Contains(evt.mousePosition))
                OpenGlobalContextualMenu(evt);

            return m_LayoutRect;
        }

        private void DrawBackground(in Event evt)
        {
            if (evt.type == EventType.Repaint)
                Styles.searchField.Draw(m_LayoutRect, false, false, false, false);
        }

        private void LayoutBlocks(float availableWidth)
        {
            var blockPosition = new Vector2(builderPadding, 0f);
            int rowCount = 1;
            m_BuilderWidth = 0f;
            m_BuilderHeight = minHeight;

            foreach (var block in EnumerateBlocks())
            {
                if (!block.visible)
                    continue;

                block.rect = GUIUtility.AlignRectToDevice(block.Layout(blockPosition, availableWidth));
                if (block.rect.width == 0 || block.rect.height == 0)
                    continue;

                if (block.rect.xMax >= availableWidth)
                {
                    // New row
                    var indent = builderPadding;
                    block.rect = GUIUtility.AlignRectToDevice(
                        new Rect(
                            indent, block.rect.y + block.rect.height + blockSpacing,
                            block.rect.width, block.rect.height));

                    ++rowCount;
                }

                m_BuilderWidth = Mathf.Max(m_BuilderWidth, block.rect.xMax);
                m_BuilderHeight = Mathf.Max(m_BuilderHeight, block.rect.yMax);
                blockPosition = new Vector2(block.rect.xMax + blockSpacing, block.rect.y);
            }

            m_BuilderHeight += builderPadding * 2;
        }

        private void DrawBlocks(in Event evt)
        {
            if (evt.type == EventType.Layout)
                return;

            m_LayoutRect.y += builderPadding;
            foreach (var block in EnumerateBlocks())
            {
                if (!block.visible)
                    continue;
                block.Draw(evt, m_LayoutRect);
            }
        }

        private void OpenGlobalContextualMenu(Event evt)
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Add condition"), false, () => Debug.LogWarning("TODO"));
            menu.AddItem(new GUIContent("Update"), false, () => Build());

#if USE_GRAPH_VIEWER
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Open Graph Viewer"), false, () => Utils.OpenGraphViewer(context.searchQuery));
#endif

            menu.ShowAsContext();
            evt.Use();
        }

        public string BuildQuery()
        {
            var query = new StringBuilder();
            BuildQuery(query, EnumerateBlocks().Where(b => !b.disabled));
            return Utils.Simplify(query.ToString());
        }

        private void BuildQuery(StringBuilder query, IEnumerable<QueryBlock> blocks)
        {
            foreach (var c in blocks)
            {
                var s = c.ToString();
                if (string.IsNullOrEmpty(s))
                    continue;
                if (c.excluded)
                    query.Append('-');
                query.Append(s);
                query.Append(' ');
            }
        }

        public bool Build()
        {
            errors.Clear();

            var newBlocks = new List<QueryBlock>();
            if (!string.IsNullOrEmpty(context.searchText))
            {
                if (!string.IsNullOrEmpty(context.filterId))
                    newBlocks.Add(new QueryAreaBlock(this, context.providers.First()));

                var query = m_QueryEngine.Parse(context.searchQuery);
                if (context.options.HasAny(SearchFlags.ShowErrorsWithResults) && !query.valid)
                    errors.AddRange(query.errors);

                var rootNode = query.queryGraph.root;
                if (rootNode != null)
                    ParseNode(rootNode, newBlocks);
            }

            if (m_SearchField != null)
            {
                m_AddBlock = new QueryAddNewBlock(this);
                m_TextBlock = new QueryTextFieldBlock(this, m_SearchField);
            }

            blocks.Clear();
            blocks.AddRange(newBlocks);
            return errors.Count == 0;
        }

        private IList<QueryBlock> Build(string searchText)
        {
            var newBlocks = new List<QueryBlock>();
            var searchQuery = searchText;

            var query = m_QueryEngine.Parse(searchQuery);
            var rootNode = query.queryGraph.root;
            if (rootNode == null)
                return null;

            ParseNode(rootNode, newBlocks);

            return newBlocks;
        }

        private void ParseNode(in IQueryNode node, List<QueryBlock> blocks, bool exclude = false)
        {
            if (!node.leaf)
                ParseNode(node.children[0], blocks, node.type == QueryNodeType.Not);

            var newBlock = CreateBlock(node, context);
            if (newBlock != null)
            {
                if (exclude)
                    newBlock.excluded = exclude;
                blocks.Add(newBlock);
            }

            if (!node.leaf && node.children.Count > 1)
            {
                foreach (var c in node.children.Skip(1))
                    ParseNode(c, blocks);
            }
        }

        private QueryBlock CreateBlock(in IQueryNode node, in SearchContext context)
        {
            if (node.type == QueryNodeType.Search && node is SearchNode sn)
                return new QueryWordBlock(this, sn);

            if (node.type == QueryNodeType.Filter && node is FilterNode fn)
            {
                if (string.Equals(fn.filterId, "t", StringComparison.Ordinal))
                    return new QueryTypeBlock(this, fn.filterValue, fn.operatorId);
                return new QueryFilterBlock(this, fn);
            }

            if (context.options.HasAny(SearchFlags.Debug))
                Debug.LogWarning($"TODO: Failed to parse block {node.identifier} ({node.type})");
            return null;
        }

        public void Apply()
        {
            var queryString = BuildQuery();
            if (context.options.HasAny(SearchFlags.Debug))
                Debug.Log($"Apply query: {context.searchText} > {queryString}");
            SetSearchText(queryString);
        }

        public void AddBlock(string text)
        {
            var newBlocks = Build(text);
            if (newBlocks == null || newBlocks.Count == 0)
                return;

            blocks.AddRange(newBlocks);
            Apply();
        }

        public void AddBlock(QueryBlock block)
        {
            blocks.Add(block);
            Apply();
        }

        public void RemoveBlock(in QueryBlock block)
        {
            blocks.Remove(block);
            Apply();
        }

        private void SetSearchText(string text)
        {
            text = Utils.Simplify(text);
            if (searchView != null)
                searchView.SetSearchText(text, TextCursorPlacement.MoveLineEnd);
            else if (context != null)
                context.searchText = text;
        }
    }
}
#endif

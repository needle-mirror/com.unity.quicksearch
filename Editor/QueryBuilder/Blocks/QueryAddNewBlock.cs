#if USE_QUERY_BUILDER
using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor.Search
{
    class QueryAddNewBlock : QueryBlock, IBlockSource
    {
        public override bool wantsEvents => true;
        public override string ToString() => null;
        public override IBlockEditor OpenEditor(in Rect rect) => null;

        public QueryAddNewBlock(IQuerySource source)
            : base(source)
        {
            hideMenu = true;
        }

        public override Rect Layout(in Vector2 at, in float availableSpace)
        {
            return GetRect(at, Styles.QueryBuilder.addNewDropDown.fixedWidth, 20f);
        }

        protected override void Draw(in Rect blockRect, in Vector2 mousePosition)
        {
            if (EditorGUI.DropdownButton(blockRect, Styles.QueryBuilder.createContent, FocusType.Passive, Styles.QueryBuilder.addNewDropDown))
                AddBlock(blockRect);
        }

        private void AddBlock(in Rect buttonRect)
        {
            QuerySelector.Open(buttonRect, this);
        }

        public override void Apply(in SearchProposition searchProposition)
        {
            if (searchProposition.data is SearchProvider provider)
                source.AddBlock(new QueryAreaBlock(source, provider));
            else if (searchProposition.data is QueryBlock block)
                source.AddBlock(block);
            else if (searchProposition.data is System.Type t)
                source.AddBlock(new QueryTypeBlock(source, t));
            else if (searchProposition.type != null)
                source.AddBlock(new QueryFilterBlock(source, searchProposition.replacement, searchProposition.type));
            else
                source.AddBlock(searchProposition.replacement);
        }

        public override IEnumerable<SearchProposition> FetchPropositions()
        {
            if (source.context.empty)
                return QueryAreaBlock.FetchPropositions(context);

            var options = new SearchPropositionOptions(string.Empty,
                SearchPropositionFlags.IgnoreRecents | SearchPropositionFlags.QueryBuilder);
            return SearchProposition.Fetch(context, options);
        }
    }
}
#endif

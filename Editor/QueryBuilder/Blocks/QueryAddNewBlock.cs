#if USE_QUERY_BUILDER
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityEditor.Search
{
    class QueryAddNewBlock : QueryBlock, IBlockSource
    {
        public override bool wantsEvents => true;
        public override string ToString() => null;
        public override IBlockEditor OpenEditor(in Rect rect) => AddBlock(rect);

        public QueryAddNewBlock(IQuerySource source)
            : base(source)
        {
            hideMenu = true;
        }

        public override Rect Layout(in Vector2 at, in float availableSpace)
        {
            return GetRect(at, 20f, 20f);
        }

        protected override void Draw(in Rect blockRect, in Vector2 mousePosition)
        {
            if (EditorGUI.DropdownButton(blockRect, Styles.QueryBuilder.createContent, FocusType.Passive, Styles.dropdownItem))
                AddBlock(blockRect);
        }

        private IBlockEditor AddBlock(in Rect buttonRect)
        {
            var title = source.context.empty ? QueryAreaBlock.title : "Add Search Filter";
            return QuerySelector.Open(buttonRect, this, title);
        }

        public override void Apply(in SearchProposition searchProposition)
        {
            source.AddProposition(searchProposition);
        }

        public override IEnumerable<SearchProposition> FetchPropositions()
        {
            var options = new SearchPropositionOptions(string.Empty,
                SearchPropositionFlags.IgnoreRecents |
                SearchPropositionFlags.QueryBuilder |
                (source.context.empty ? SearchPropositionFlags.ForceAllProviders : SearchPropositionFlags.None));
            if (source.context.empty)
            {
                return QueryAreaBlock.FetchPropositions(context)
                    .Concat(new[] { SearchProposition.CreateSeparator() })
                    .Concat(SearchProposition.Fetch(context, options).OrderBy(p => p));
            }
            else
            {
                return SearchProposition.Fetch(context, options)
                    .Concat(QueryAndOrBlock.BuiltInQueryBuilderPropositions()).OrderBy(p => p);
            }
        }
    }
}
#endif

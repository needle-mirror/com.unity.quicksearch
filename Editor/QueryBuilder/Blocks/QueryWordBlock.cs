#if USE_QUERY_BUILDER
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityEditor.Search
{
    class QueryWordBlock : QueryBlock
    {
        public QueryWordBlock(IQuerySource source, SearchNode node)
            : base(source)
        {
            name = string.Empty;
            value = node.searchValue;
        }

        public override Rect Layout(in Vector2 at, in float availableSpace)
        {
            var wordContent = Styles.QueryBuilder.label.CreateContent(value);
            var widgetWidth = wordContent.expandedWidth;
            return GetRect(at, widgetWidth, blockHeight);
        }

        protected override void Draw(in Rect widgetRect, in Vector2 mousePosition)
        {
            var wordContent = Styles.QueryBuilder.label.CreateContent(value);
            var widgetWidth = wordContent.expandedWidth;

            DrawBackground(widgetRect);
            var wordRect = new Rect(widgetRect.x + wordContent.style.margin.left, widgetRect.y - 1f, widgetWidth, widgetRect.height);
            wordContent.Draw(wordRect, mousePosition);
            DrawBorders(widgetRect, mousePosition);
        }

        public override IEnumerable<SearchProposition> FetchPropositions()
        {
            return Enumerable.Empty<SearchProposition>();
        }

        protected override Color GetBackgroundColor()
        {
            return QueryColors.word;
        }

        public override string ToString()
        {
            return value;
        }
    }
}
#endif

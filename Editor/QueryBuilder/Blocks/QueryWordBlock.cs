#if USE_QUERY_BUILDER
using UnityEngine;

namespace UnityEditor.Search
{
    class QueryWordBlock : QueryBlock
    {
        public QueryWordBlock(IQuerySource source, SearchNode node)
            : this(source, node.searchValue)
        {
            if (node.rawSearchValueStringView.HasQuotes())
                explicitQuotes = true;
        }

        public QueryWordBlock(IQuerySource source, string searchValue)
            : base(source)
        {
            name = string.Empty;
            value = searchValue;
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

        public override IBlockEditor OpenEditor(in Rect rect)
        {
            var screenRect = new Rect(rect.position + context.searchView.position.position, rect.size);
            return QueryTextBlockEditor.Open(screenRect, this);
        }

        protected override Color GetBackgroundColor()
        {
            return QueryColors.word;
        }

        public override string ToString()
        {
            return EscapeLiteralString(value);
        }
    }
}
#endif

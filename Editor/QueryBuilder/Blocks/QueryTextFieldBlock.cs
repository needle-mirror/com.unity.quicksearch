#if USE_QUERY_BUILDER
using UnityEngine;

namespace UnityEditor.Search
{
    class QueryTextFieldBlock : QueryBlock
    {
        private SearchField m_SearchField;
        public override bool wantsEvents => true;

        public QueryTextFieldBlock(IQuerySource source, SearchField searchField)
            : base(source)
        {
            hideMenu = true;
            value = string.Empty;
            m_SearchField = searchField;
        }

        public override string ToString() => value;
        public override IBlockEditor OpenEditor(in Rect rect) => null;

        public override Rect Layout(in Vector2 at, in float availableSpace)
        {
            const float blockSpacing = 4f;

            if (m_SearchField == null)
                m_SearchField = new SearchField();

            var spaceLeft = availableSpace - at.x - blockSpacing;
            var size = Styles.queryBuilderSearchField.CalcSize(Utils.GUIContentTemp(value));
            return GetRect(at, Mathf.Max(spaceLeft, size.x), size.y);
        }

        internal SearchField GetSearchField()
        {
            return m_SearchField;
        }

        protected override void Draw(in Rect blockRect, in Vector2 mousePosition)
        {
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && blockRect.Contains(evt.mousePosition))
                source.BlockActivated(this);

            var newSearchText = m_SearchField.Draw(blockRect, value, Styles.queryBuilderSearchField);
            if (!string.Equals(newSearchText, value))
            {
                value = newSearchText;
                source.Apply();
            }
        }
    }
}
#endif

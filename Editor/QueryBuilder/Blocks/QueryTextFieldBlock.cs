#if USE_QUERY_BUILDER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace UnityEditor.Search
{
    class QueryTextFieldBlock : QueryBlock
    {
        const float blockSpacing = 4f;

        private string m_SearchText;
        private SearchField m_SearchField;

        public override bool wantsEvents => true;

        public QueryTextFieldBlock(IQuerySource source, SearchField searchField)
            : base(source)
        {
            m_SearchText = string.Empty;
            m_SearchField = searchField;
            hideMenu = true;
        }

        public override IBlockEditor OpenEditor(in Rect rect)
        {
            return null;
        }

        public override Rect Layout(in Vector2 at, in float availableSpace)
        {
            var spaceLeft = availableSpace - at.x - blockSpacing;
            var size = Styles.queryBuilderSearchField.CalcSize(Utils.GUIContentTemp(m_SearchText));
            return GetRect(at, Mathf.Max(spaceLeft, size.x), size.y);
        }

        protected override void Draw(in Rect blockRect, in Vector2 mousePosition)
        {
            if (m_SearchField == null)
                m_SearchField = new SearchField();

            //GUI.DrawTexture(blockRect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, false, 0f, Color.red, 0, 0);

            var newSearchText = m_SearchField.Draw(blockRect, m_SearchText, Styles.queryBuilderSearchField);
            if (!string.Equals(newSearchText, m_SearchText))
            {
                m_SearchText = newSearchText;
                source.Apply();
            }
        }

        public override string ToString()
        {
            return m_SearchText;
        }
    }
}
#endif

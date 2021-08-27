#if USE_QUERY_BUILDER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityEditor.Search
{
    class QueryTypeBlock : QueryFilterBlock
    {
        const float iconSize = 16f;

        private Type type;
        private string typeName;
        private Texture2D typeTexture;

        public QueryTypeBlock(IQuerySource source, string type, in string op = "=")
            : base(source, "t", op, type)
        {
            SetType(TypeCache.GetTypesDerivedFrom<UnityEngine.Object>().FirstOrDefault(t => string.Equals(t.Name, type, StringComparison.OrdinalIgnoreCase)));
        }

        public QueryTypeBlock(IQuerySource source, in Type type)
            : base(source, "t", "=", type.Name)
        {
            SetType(type);
        }

        private void SetType(in Type type)
        {
            this.type = type;
            if (this.type != null)
            {
                op = "=";
                value = type.Name;
                typeName = ObjectNames.NicifyVariableName(type.Name);
                typeTexture = AssetPreview.GetMiniTypeThumbnail(type);
            }
        }

        public override IBlockEditor OpenEditor(in Rect rect)
        {
            return QuerySelector.Open(rect, this);
        }

        public override void Apply(in SearchProposition searchProposition)
        {
            if (searchProposition.data is Type t)
            {
                SetType(t);
                source.Apply();
            }
        }

        public override IEnumerable<SearchProposition> FetchPropositions()
        {
            yield return new SearchProposition(
                category: null,
                label: "Components",
                icon: EditorGUIUtility.LoadIcon("GameObject Icon"));

            var componentType = typeof(Component);
            var assetTypes = SearchUtils.FetchTypePropositions<UnityEngine.Object>().Where(p => !componentType.IsAssignableFrom((Type)p.data));
            var propositions = SearchUtils.FetchTypePropositions<Component>("Components").Concat(assetTypes);
            foreach (var p in propositions)
                yield return p;
        }

        protected override Color GetBackgroundColor()
        {
            return typeTexture == null ? QueryColors.type : QueryColors.typeIcon;
        }

        public override Rect Layout(in Vector2 at, in float availableSpace)
        {
            if (typeTexture == null)
                return base.Layout(at, availableSpace);

            var labelStyle = Styles.QueryBuilder.label;
            var valueContent = labelStyle.CreateContent(typeName ?? value);
            var blockWidth = iconSize + valueContent.width + labelStyle.margin.horizontal * 1.5f + blockExtraPadding + QueryContent.DownArrow.width;
            return GetRect(at, blockWidth, blockHeight);
        }

        protected override void Draw(in Rect blockRect, in Vector2 mousePosition)
        {
            if (typeTexture == null)
            {
                base.Draw(blockRect, mousePosition);
                return;
            }

            var labelStyle = Styles.QueryBuilder.label;
            var valueContent = labelStyle.CreateContent(typeName ?? value);

            DrawBackground(blockRect);

            var trect = new Rect(blockRect.x + 6f, blockRect.y + 2f, iconSize, iconSize);
            GUI.DrawTexture(trect, typeTexture, ScaleMode.ScaleToFit, true);
            trect.width -= 6f;
            DrawValue(trect, blockRect, mousePosition, valueContent);

            DrawBorders(blockRect, mousePosition);
        }
    }
}
#endif

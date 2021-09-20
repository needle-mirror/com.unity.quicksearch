#if USE_QUERY_BUILDER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditorInternal;
using UnityEngine;

namespace UnityEditor.Search
{
    abstract class QueryListBlock : QueryBlock
    {
        const float iconSize = 16f;
        static readonly Color s_TextureBackgroundColor = new Color(0.2f, 0.2f, 0.25f, 0.95f);

        public readonly string id;
        public readonly string category;
        protected string label;
        protected Texture2D icon;
        protected bool alwaysDrawLabel;

        public abstract IEnumerable<SearchProposition> GetPropositions(SearchPropositionFlags flags = SearchPropositionFlags.None);

        protected QueryListBlock(IQuerySource source, string value, QueryListBlockAttribute attr)
            : base(source)
        {
            this.id = attr.id;
            this.op = attr.op;
            this.name = attr.name;
            this.value = value;
            this.category = attr.category;
        }

        protected string GetCategory(SearchPropositionFlags flags)
        {
            return flags.HasAny(SearchPropositionFlags.NoCategory) ? null : category;
        }

        public override IBlockEditor OpenEditor(in Rect rect)
        {
            return QuerySelector.Open(rect, this);
        }

        public override IEnumerable<SearchProposition> FetchPropositions()
        {
            return GetPropositions(SearchPropositionFlags.NoCategory);
        }

        public override void Apply(in SearchProposition searchProposition)
        {
            value = searchProposition.data?.ToString() ?? searchProposition.replacement;
            source.Apply();
        }

        protected override Color GetBackgroundColor()
        {
            return icon == null ? QueryColors.type : QueryColors.typeIcon;
        }

        protected SearchProposition CreateProposition(SearchPropositionFlags flags, string label, string data, string help = "", int score = 0)
        {
            return new SearchProposition(category: GetCategory(flags), label: label, help: help,
                    data: data, priority: score, icon: icon, type: GetType());
        }

        protected SearchProposition CreateProposition(SearchPropositionFlags flags, string label, string data, string help, Texture2D icon, int score = 0)
        {
            return new SearchProposition(category: GetCategory(flags), label: label, help: help,
                    data: data, priority: score, icon: icon, type: GetType());
        }

        public override Rect Layout(in Vector2 at, in float availableSpace)
        {
            if (!icon || alwaysDrawLabel)
                return base.Layout(at, availableSpace);

            var labelStyle = Styles.QueryBuilder.label;
            var valueContent = labelStyle.CreateContent(label ?? value);
            var blockWidth = iconSize + valueContent.width + labelStyle.margin.horizontal + blockExtraPadding + QueryContent.DownArrow.width;
            return GetRect(at, blockWidth, blockHeight);
        }

        protected override void Draw(in Rect blockRect, in Vector2 mousePosition)
        {
            if (!icon || alwaysDrawLabel)
            {
                base.Draw(blockRect, mousePosition);
                return;
            }

            var labelStyle = Styles.QueryBuilder.label;
            var valueContent = labelStyle.CreateContent(label ?? value);

            DrawBackground(blockRect);

            var backgroundTextureRect = new Rect(blockRect.x + 1f, blockRect.y + 1f, 24f, blockRect.height - 2f);
            var iconBackgroundRadius = new Vector4(borderRadius, 0, 0, editor != null ? 0 : borderRadius);
            var backgroundTextureRect2 = backgroundTextureRect;
            if (selected)
                backgroundTextureRect2.xMin -= 1f;
            GUI.DrawTexture(backgroundTextureRect2, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, false, 0f, s_TextureBackgroundColor, Vector4.zero, iconBackgroundRadius);

            var valueRect = backgroundTextureRect;
            var textureRect = backgroundTextureRect;
            textureRect.x += 5f; textureRect.y += 1f; textureRect.width = iconSize; textureRect.height = iconSize;
            GUI.DrawTexture(textureRect, icon, ScaleMode.ScaleToFit, true);

            valueRect.x -= 4f;
            DrawValue(valueRect, blockRect, mousePosition, valueContent);

            DrawBorders(blockRect, mousePosition);
        }

        public override string ToString()
        {
            return $"{id}{op}{value}";
        }

        protected override void AddContextualMenuItems(GenericMenu menu)
        {
            menu.AddItem(EditorGUIUtility.TrTextContent($"Operator/Equal (=)"), string.Equals(op, "=", StringComparison.Ordinal), () => SetOperator("="));
            menu.AddItem(EditorGUIUtility.TrTextContent($"Operator/Contains (:)"), string.Equals(op, ":", StringComparison.Ordinal), () => SetOperator(":"));
        }
    }

    [QueryListBlock("Types", "type", "t", ":")]
    class QueryTypeBlock : QueryListBlock
    {
        private Type type;

        public QueryTypeBlock(IQuerySource source, string value, QueryListBlockAttribute attr)
            : base(source, value, attr)
        {
            SetType(GetValueType(value));
        }

        private void SetType(in Type type)
        {
            this.type = type;
            if (this.type != null)
            {
                value = type.Name;
                label = ObjectNames.NicifyVariableName(type.Name);
                icon = AssetPreview.GetMiniTypeThumbnail(type);
            }
        }

        private static Type GetValueType(string value)
        {
            return TypeCache.GetTypesDerivedFrom<UnityEngine.Object>().FirstOrDefault(t => string.Equals(t.Name, value, StringComparison.OrdinalIgnoreCase) || string.Equals(t.ToString(), value, StringComparison.OrdinalIgnoreCase));
        }

        public override void Apply(in SearchProposition searchProposition)
        {
            if (searchProposition.data is Type t)
            {
                SetType(t);
                source.Apply();
            }
        }

        public override IEnumerable<SearchProposition> GetPropositions(SearchPropositionFlags flags)
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
    }

    [QueryListBlock("Components", "component", "t", ":")]
    class QueryComponentBlock : QueryTypeBlock
    {
        public QueryComponentBlock(IQuerySource source, string value, QueryListBlockAttribute attr)
             : base(source, value, attr)
        {
        }

        public override IEnumerable<SearchProposition> GetPropositions(SearchPropositionFlags flags)
        {
            return SearchUtils.FetchTypePropositions<Component>("Components", GetType());
        }
    }

    [QueryListBlock("Labels", "label", "l", ":")]
    class QueryLabelBlock : QueryListBlock
    {
       public QueryLabelBlock(IQuerySource source, string value, QueryListBlockAttribute attr)
            : base(source, value, attr)
        {
            icon = Utils.LoadIcon("AssetLabelIcon");
        }

        public override IEnumerable<SearchProposition> GetPropositions(SearchPropositionFlags flags)
        {
            foreach (var l in AssetDatabase.GetAllLabels())
            {
                yield return CreateProposition(flags, ObjectNames.NicifyVariableName(l.Key), l.Key);
            }
        }
    }

    [QueryListBlock("Tags", "tag", "tag")]
    class QueryTagBlock : QueryListBlock
    {
        public QueryTagBlock(IQuerySource source, string value, QueryListBlockAttribute attr)
            : base(source, value, attr)
        {
            icon = Utils.LoadIcon("AssetLabelIcon");
            alwaysDrawLabel = true;
        }

        public override IEnumerable<SearchProposition> GetPropositions(SearchPropositionFlags flags)
        {

            foreach (var t in InternalEditorUtility.tags)
            {
                yield return CreateProposition(flags, ObjectNames.NicifyVariableName(t), t);
            }
        }
    }

    [QueryListBlock("Layers", "layer", "layer")]
    class QueryLayerBlock : QueryListBlock
    {
        public QueryLayerBlock(IQuerySource source, string value, QueryListBlockAttribute attr)
            : base(source, value, attr)
        {
            icon = Utils.LoadIcon("GUILayer Icon");
            alwaysDrawLabel = true;
        }

        public override IEnumerable<SearchProposition> GetPropositions(SearchPropositionFlags flags)
        {
            for (var i = 0; i < 32; i++)
            {
                var layerName = InternalEditorUtility.GetLayerName(i);
                if (!string.IsNullOrEmpty(layerName))
                {
                    yield return CreateProposition(flags, ObjectNames.NicifyVariableName(layerName), layerName);
                }
            }
        }

        public override string ToString()
        {
            for (var i = 0; i < 32; i++)
            {
                var layerName = InternalEditorUtility.GetLayerName(i);
                if (layerName == value)
                    return $"{id}{op}{i}";
            }

            return base.ToString();
        }
    }

    [QueryListBlock("Prefabs", "prefab", "prefab", ":")]
    class QueryPrefabFilterBlock : QueryListBlock
    {
        public QueryPrefabFilterBlock(IQuerySource source, string value, QueryListBlockAttribute attr)
            : base(source, value, attr)
        {
            icon = Utils.LoadIcon("Prefab Icon");
        }

        public override IEnumerable<SearchProposition> GetPropositions(SearchPropositionFlags flags)
        {
            yield return CreateProposition(flags, "Root", "root", "Search prefab roots");
            yield return CreateProposition(flags, "Top", "top", "Search top-level prefab root instances");
            yield return CreateProposition(flags, "Instance", "instance", "Search objects that are part of a prefab instance");
            yield return CreateProposition(flags, "Non asset", "nonasset", "Search prefab objects that are not part of an asset");
            yield return CreateProposition(flags, "Asset", "asset", "Search prefab objects that are part of an asset");
            yield return CreateProposition(flags, "Model", "model", "Search prefab objects that are part of a model");
            yield return CreateProposition(flags, "Regular", "regular", "Search regular prefab objects");
            yield return CreateProposition(flags, "Variant", "variant", "Search variant prefab objects");
            yield return CreateProposition(flags, "Modified", "modified", "Search modified prefab assets");
            yield return CreateProposition(flags, "Altered", "altered", "Search modified prefab instances");
        }
    }

    [QueryListBlock("Filters", "is", "is", ":")]
    class QueryIsFilterBlock : QueryListBlock
    {
        public QueryIsFilterBlock(IQuerySource source, string value, QueryListBlockAttribute attr)
            : base(source, value, attr)
        {
            icon = Utils.LoadIcon("Filter Icon");
            alwaysDrawLabel = true;
        }

        public override IEnumerable<SearchProposition> GetPropositions(SearchPropositionFlags flags)
        {
            yield return CreateProposition(flags, "Child", "child", "Search object with a parent");
            yield return CreateProposition(flags, "Leaf", "leaf", "Search object without children");
            yield return CreateProposition(flags,  "Root", "root", "Search root objects");
            yield return CreateProposition(flags, "Visible", "visible", "Search view visible objects");
            yield return CreateProposition(flags, "Hidden", "hidden", "Search hierarchically hidden objects");
            yield return CreateProposition(flags, "Static", "static", "Search static objects");
            yield return CreateProposition(flags, "Prefab", "prefab", "Search prefab objects");
        }
    }
}
#endif

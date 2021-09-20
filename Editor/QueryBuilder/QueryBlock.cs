#if USE_QUERY_BUILDER
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor.Search
{
    static class QueryColors
    {
        public static readonly Color area;
        public static readonly Color filter;
        public static readonly Color type;
        public static readonly Color typeIcon;
        public static readonly Color word;

        static QueryColors()
        {
            ColorUtility.TryParseHtmlString("#74CBEE", out area);
            ColorUtility.TryParseHtmlString("#70B0BA", out filter);
            ColorUtility.TryParseHtmlString("#EBD05F", out type);
            ColorUtility.TryParseHtmlString("#CBB03F", out typeIcon);
            ColorUtility.TryParseHtmlString("#367BA1", out word);
        }
    }

    abstract class QueryBlock : IBlockSource
    {
        protected const float arrowOffset = 5f;
        protected const float blockHeight = SearchField.minSinglelineTextHeight;
        protected const float blockExtraPadding = 4f;
        protected const float borderRadius = 8f;
        protected static readonly Color hoveredBorderColor = new Color(0.6f, 0.6f, 0.6f);
        protected static readonly Color normalBorderColor = new Color(0.1f, 0.1f, 0.1f);
        protected static readonly Color selectedBorderColor = new Color(58/255f, 121/255f, 187/255f);
        protected Rect arrowRect { get; set; }

        public IQuerySource source { get; private set; }
        public SearchContext context => source.context; // TODO: Can this be removed from here?
        public IBlockEditor editor { get; private set; }

        public string name { get; protected set; }
        public string value { get; set; }
        public string op { get; protected set; }
        public bool explicitQuotes { get; protected set; }
        public virtual bool formatNames => false;
        public virtual bool visible => true;
        public virtual bool wantsEvents => false;
        public virtual bool canExclude => true;
        public virtual bool canDisable => true;
        public bool hideMenu { get; set; }
        public bool disabled { get; set; }
        public bool @readonly { get; set; }
        public bool disableHovering { get; set; }
        public bool excluded { get; set; }
        public bool selected { get; set; }

        public Rect drawRect { get; set; }
        public Rect layoutRect { get; set; }
        public float width => layoutRect.width;
        public float height => layoutRect.height;
        public Vector2 size => layoutRect.size;

        public QueryBlock(IQuerySource source)
        {
            this.source = source;
        }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(name))
                return value;
            if (string.IsNullOrEmpty(value))
                return name;
            return $"{name}={value}";
        }

        void Delete()
        {
            source.RemoveBlock(this);
        }

        void OpenMenu(Event evt)
        {
            var menu = new GenericMenu();

            if (!@readonly)
            {
                if (canDisable)
                    menu.AddItem(EditorGUIUtility.TrTextContent("Enable"), !disabled, ToggleDisabled);

                if (canExclude)
                    menu.AddItem(EditorGUIUtility.TrTextContent("Exclude"), excluded, ToggleExcluded);
            }

            if (menu.GetItemCount() > 0)
                menu.AddSeparator("");
            var bc = menu.GetItemCount();
            AddContextualMenuItems(menu);
            if (!@readonly)
            {
                if (menu.GetItemCount() != bc)
                    menu.AddSeparator("");
                menu.AddItem(EditorGUIUtility.TrTextContent("Delete"), false, Delete);
            }

            if (menu.GetItemCount() > 0)
            {
                menu.ShowAsContext();
                evt.Use();
            }
        }

        private void ToggleDisabled()
        {
            disabled = !disabled;
            source.Apply();
        }

        protected virtual void AddContextualMenuItems(GenericMenu menu) {}

        void OpenEditor(Event evt, in Rect rect)
        {
            if (editor == null)
            {
                editor = OpenEditor(rect);
                if (editor != null)
                    evt.Use();
            }
            else if (editor.window)
            {
                editor.window.Close();
                editor = null;
            }
        }

        public virtual IBlockEditor OpenEditor(in Rect rect)
        {
            return QuerySelector.Open(rect, this);
        }

        public void CloseEditor()
        {
            editor = null;
            context?.searchView?.Repaint();
        }

        protected Rect GetRect(in Vector2 at, in float width, in float height)
        {
            return new Rect(at, new Vector2(width, height));
        }

        protected virtual bool HandleEvents(Event evt, in Rect blockRect)
        {
            return false;
        }

        private void DefaultHandleEvents(Event evt, in Rect blockRect)
        {
            var hovered = blockRect.Contains(evt.mousePosition);
            if (evt.type == EventType.ContextClick && hovered)
            {
                OpenMenu(evt);
            }
            else if (evt.type == EventType.MouseDown && hovered)
            {
                if (evt.button == 0)
                {
                    if ((evt.control || evt.command) && canExclude)
                    {
                        ToggleExcluded();
                        evt.Use();
                    }
                    else if (evt.alt && canDisable)
                    {
                        ToggleDisabled();
                        evt.Use();
                    }
                    else if (!disabled)
                    {
                        if (arrowRect != Rect.zero)
                        {
                            if (arrowRect.Contains(evt.mousePosition))
                                OpenEditor(evt, blockRect);
                        }
                        else if (!wantsEvents)
                        {
                            OpenEditor(evt, blockRect);
                        }

                        source.BlockActivated(this);
                        evt.Use();
                    }
                }
                else if (evt.button == 2)
                {
                    Utils.CallDelayed(Delete);
                    evt.Use();
                }
                else
                    OpenMenu(evt);
            }
        }

        private void ToggleExcluded()
        {
            excluded = !excluded;
            source.Apply();
        }

        public Rect Draw(Event evt, in Rect builderRect)
        {
            drawRect = GUIUtility.AlignRectToDevice(new Rect(layoutRect.position + builderRect.position, layoutRect.size));
            if (evt.type == EventType.Repaint || (wantsEvents && !@readonly))
            {
                var oldColor = GUI.color;
                if (disabled)
                    GUI.color = GUI.color * new Color(1f, 1f, 1f, 0.5f);

                Draw(drawRect, evt.mousePosition);
                GUI.color = oldColor;

                if (evt.type == EventType.Repaint)
                {
                    if (excluded)
                    {
                        var disabledLine = new Rect(drawRect.x + 4f, drawRect.center.y, drawRect.width - 8f, 2f);
                        GUI.DrawTexture(disabledLine, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, false, 0f, Color.black, 0, 1);
                    }
                }
            }

            if (!@readonly && !hideMenu)
            {
                if (!wantsEvents || !HandleEvents(evt, drawRect))
                    DefaultHandleEvents(evt, drawRect);
            }

            return drawRect;
        }

        public virtual Rect Layout(in Vector2 at, in float availableSpace)
        {
            var labelStyle = Styles.QueryBuilder.label;
            var nameContent = labelStyle.CreateContent(name);
            var valueContent = labelStyle.CreateContent(value);
            var blockWidth = nameContent.width + valueContent.width + labelStyle.margin.horizontal * 2f;
            if (!@readonly)
                blockWidth += blockExtraPadding + QueryContent.DownArrow.width;
            return GetRect(at, blockWidth, blockHeight);
        }

        protected virtual void Draw(in Rect blockRect, in Vector2 mousePosition)
        {
            var labelStyle = Styles.QueryBuilder.label;
            var nameContent = labelStyle.CreateContent(name);
            var valueContent = labelStyle.CreateContent(value);

            DrawBackground(blockRect);

            var nameRect = DrawName(blockRect, mousePosition, nameContent);
            var sepRect = DrawSeparator(nameRect);
            DrawValue(sepRect, blockRect, mousePosition, valueContent);

            DrawBorders(blockRect, mousePosition);
        }

        protected void DrawValue(in Rect at, in Rect blockRect, in Vector2 mousePosition, in QueryContent valueContent)
        {
            var x = at.xMax + valueContent.style.margin.left;
            var valueRect = new Rect(x, blockRect.y - 1f, blockRect.width - (x - blockRect.xMin) - valueContent.style.margin.right, blockRect.height);
            valueContent.Draw(valueRect, mousePosition);

            if (!@readonly)
                DrawArrow(blockRect, mousePosition, editor != null ? QueryContent.UpArrow : QueryContent.DownArrow);
        }

        protected void DrawArrow(in Rect blockRect, in Vector2 mousePosition, QueryContent arrowContent)
        {
            arrowRect = new Rect(blockRect.xMax - arrowContent.width - arrowOffset, blockRect.y - 1f, QueryContent.DownArrow.width, blockRect.height);
            EditorGUIUtility.AddCursorRect(arrowRect, MouseCursor.Link);
            arrowContent.Draw(arrowRect, mousePosition);
        }

        protected virtual Rect DrawSeparator(in Rect at)
        {
            var sepRect = new Rect(at.xMax, at.yMin + 1f, 1f, Mathf.Ceil(at.height - 1f));
            GUI.DrawTexture(sepRect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, false, 0f, Styles.QueryBuilder.splitterColor, 0f, 0f);
            return sepRect;
        }

        protected Rect DrawName(in Rect blockRect, in Vector2 mousePosition, QueryContent nameContent)
        {
            var nameRect = blockRect;
            nameRect.y -= 1;
            nameRect.width = nameContent.width + nameContent.style.margin.horizontal;
            nameRect.xMin += nameContent.style.margin.left;
            return nameContent.Draw(nameRect, mousePosition);
        }

        protected void DrawBorders(in Rect blockRect, in Vector2 mousePosition)
        {
            var isHovered = blockRect.Contains(mousePosition);
            if (selected || (isHovered  && !disableHovering))
            {
                var borderColor = selected ? selectedBorderColor : hoveredBorderColor;
                var borderWidth4 = selected ? new Vector4(1, 1, 1, 1) : new Vector4(1, 1, 1, 1);
                var borderRadius4 = editor != null ? new Vector4(borderRadius, borderRadius, 0, 0) : new Vector4(borderRadius, borderRadius, borderRadius, borderRadius);
                GUI.DrawTexture(blockRect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, false, 0f, borderColor, borderWidth4, borderRadius4);
            }
        }

        protected void DrawBackground(in Rect blockRect)
        {
            var borderRadius4 = editor != null ? new Vector4(borderRadius, borderRadius, 0, 0) : new Vector4(borderRadius, borderRadius, borderRadius, borderRadius);
            var bgColor = GetBackgroundColor();
            var color = selected ? EditorGUIUtility.LightenColor(bgColor) : bgColor;
            GUI.DrawTexture(blockRect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, false, 0f, color, Vector4.zero, borderRadius4);
        }

        protected virtual Color GetBackgroundColor() => Color.red;

        protected string EscapeLiteralString(in string sv)
        {
            if (string.IsNullOrEmpty(sv))
                return "\"\"";
            if (explicitQuotes || value.IndexOfAny(new[] { ' ', '/', '*' }) != -1)
                return '"' + sv + '"';
            return sv;
        }

        public void SetOperator(in string op)
        {
            this.op = op;
            source.Apply();
        }

        public virtual void Apply(in SearchProposition searchProposition) => throw new NotSupportedException($"Cannot apply {searchProposition} for {this} control");
        public virtual IEnumerable<SearchProposition> FetchPropositions() => throw new NotSupportedException($"Cannot fetch propositions for {this} control");
    }
}
#endif

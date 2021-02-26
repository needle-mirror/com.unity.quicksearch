namespace UnityEditor.Search
{
    static partial class Parsers
    {
        [SearchExpressionParser("expand", BuiltinParserPriority.Expand)]
        internal static SearchExpression ExpandParser(SearchExpressionParserArgs args)
        {
            var outerText = args.text;
            var text = ParserUtils.SimplifyExpression(outerText);
            if (!text.StartsWith("...", System.StringComparison.Ordinal))
                return null;

            var expression = ParserManager.Parse(args.With(text.Substring(3)));
            return new SearchExpression(expression, expression.types | SearchExpressionType.Expandable, outerText, expression.innerText);
        }
    }
}

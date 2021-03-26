using System.Text.RegularExpressions;

namespace UnityEditor.Search
{
    static partial class Parsers
    {
        static readonly Regex s_SelectorPattern = new Regex(@"^[@$][^><=!:\s]+");
        static readonly SearchExpressionEvaluator s_SelectorEvaluator = EvaluatorManager.GetConstantEvaluatorByName("selector");

        [SearchExpressionParser("selector", BuiltinParserPriority.String)]
        internal static SearchExpression SelectorParser(StringView outerText)
        {
            var text = ParserUtils.SimplifyExpression(outerText);
            if (!s_SelectorPattern.IsMatch(text.ToString()))
                return null;
            return new SearchExpression(SearchExpressionType.Selector, outerText, text.Substring(1), s_SelectorEvaluator);
        }
    }
}

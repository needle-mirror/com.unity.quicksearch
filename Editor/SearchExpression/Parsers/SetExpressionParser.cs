using System.Linq;

namespace UnityEditor.Search
{
    static partial class Parsers
    {
        static readonly SearchExpressionEvaluator SetEvaluator = EvaluatorManager.GetConstantEvaluatorByName("set");

        [SearchExpressionParser("fixedset", BuiltinParserPriority.Set)]
        internal static SearchExpression FixedSetParser(SearchExpressionParserArgs args)
        {
            var outerText = args.text;
            var text = ParserUtils.SimplifyExpression(outerText);
            if (text.Length < 2 || text[0] != '[' || text[text.Length - 1] != ']')
                return null;
            var expressions = ParserUtils.GetExpressionsStartAndLength(text, out _);
            if (expressions.Length != 1 || expressions[0].startIndex != text.startIndex || expressions[0].Length != text.Length)
                return null;

            var parameters = ParserUtils.ExtractArguments(text)
                .Select(paramText => ParserManager.Parse(args.With(paramText).With(SearchExpressionParserFlags.ImplicitLiterals)))
                .ToArray();

            var innerText = ParserUtils.SimplifyExpression(text.Substring(1, text.Length - 2));
            return new SearchExpression(SearchExpressionType.Set, outerText, innerText, SetEvaluator, parameters);
        }

        [SearchExpressionParser("set", BuiltinParserPriority.Set)]
        internal static SearchExpression ExpressionSetParser(SearchExpressionParserArgs args)
        {
            var outerText = args.text;
            if (outerText.Length < 2)
                return null;
            var innerText = ParserUtils.SimplifyExpression(outerText, false);
            if (outerText.Length == innerText.Length)
                return null;
            var text = outerText.Substring(innerText.startIndex - outerText.startIndex - 1, innerText.Length + 2);
            if (text[0] != '{' || text[text.Length - 1] != '}')
                return null;

            var expressions = ParserUtils.GetExpressionsStartAndLength(innerText, out var rootHasParameters);
            if (!rootHasParameters)
                return null;

            var parameters = ParserUtils.ExtractArguments(text)
                .Select(paramText => ParserManager.Parse(args.With(paramText).Without(SearchExpressionParserFlags.ImplicitLiterals)))
                .ToArray();

            return new SearchExpression(SearchExpressionType.Set, outerText, innerText.Trim(), SetEvaluator, parameters);
        }
    }
}

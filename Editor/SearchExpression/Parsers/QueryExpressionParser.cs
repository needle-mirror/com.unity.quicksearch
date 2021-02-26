using System.Collections.Generic;

namespace UnityEditor.Search
{
    static partial class Parsers
    {
        static readonly SearchExpressionEvaluator QueryEvaluator = EvaluatorManager.GetConstantEvaluatorByName("query");

        [SearchExpressionParser("query", BuiltinParserPriority.Query)]
        internal static SearchExpression QueryParser(SearchExpressionParserArgs args)
        {
            var text = ParserUtils.SimplifyExpression(args.text);
            var nestedExpressions = new List<SearchExpression>();
            var expressionsStartAndLength = ParserUtils.GetExpressionsStartAndLength(text, out _);
            var lastExpressionEndIndex = 0;
            foreach (var expression in expressionsStartAndLength)
            {
                string exprName = string.Empty;
                if (expression[0] == '{')
                {
                    var namedText = text.Substring(lastExpressionEndIndex, expression.startIndex - text.startIndex + 1 - lastExpressionEndIndex).ToString();
                    var match = ParserUtils.namedExpressionStartRegex.Match(namedText);
                    if (match != null && match.Success && match.Groups["name"].Value != string.Empty)
                        exprName = match.Groups["name"].Value;
                }

                var paramText = args.With(text.Substring(expression.startIndex - text.startIndex - exprName.Length, expression.Length + exprName.Length));
                var nestedExpression = ParserManager.Parse(paramText);
                nestedExpressions.Add(nestedExpression);
                lastExpressionEndIndex = expression.startIndex + expression.Length;
            }

            return new SearchExpression(SearchExpressionType.QueryString, args.text, text, QueryEvaluator, nestedExpressions.ToArray());
        }
    }
}

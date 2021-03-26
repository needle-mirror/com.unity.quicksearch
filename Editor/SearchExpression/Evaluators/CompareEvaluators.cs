using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System;
using System.ComponentModel;

namespace UnityEditor.Search
{
    static partial class Evaluators
    {
        private static int DefaultComparer(object a, object b)
        {
            if (Utils.TryGetNumber(a, out var da) && Utils.TryGetNumber(b, out var db))
                return Comparer.DefaultInvariant.Compare(da, db);
            return Comparer.DefaultInvariant.Compare(a, b);
        }

        public static IEnumerable<SearchItem> Compare(SearchExpressionContext c, Func<object, object, bool> comparer)
        {
            if (c.args.Length < 2 || c.args.Length > 3)
                c.ThrowError($"Invalid arguments");

            if (c.args.Length == 3 && !IsSelectorLiteral(c.args[1]))
                c.ThrowError($"Invalid selector");

            var setExpr = c.args[0];
            if (!setExpr.types.IsIterable())
                c.ThrowError("Primary set is not iterable", setExpr.outerText);

            string valueSelector = null;
            var compareExprIndex = 1;
            if (c.args.Length == 3)
            {
                valueSelector = c.args[1].innerText.ToString();
                compareExprIndex++;
            }
            var compareExpr = c.args[compareExprIndex];
            var compareValueItr = compareExpr.Execute(c).FirstOrDefault(e => e != null);
            if (compareValueItr == null)
                c.ThrowError("Invalid comparer value", compareExpr.outerText);
            var compareValue = compareValueItr.value;

            if (valueSelector == null)
            {
                return setExpr.Execute(c).Where(item => item != null && comparer(item.GetValue(valueSelector), compareValue));
            }

            return TaskEvaluatorManager.EvaluateMainThread(setExpr.Execute(c), item =>
            {
                var value = SelectorManager.SelectValue(item, c.search, valueSelector);
                if (value != null && comparer(value, compareValue))
                    return item;
                return null;
            }, 25);
        }

        [Description("Keep search results that have a greater value."), Category("Comparers")]
        [SearchExpressionEvaluator(SearchExpressionType.Iterable, SearchExpressionType.Literal | SearchExpressionType.QueryString)]
        [SearchExpressionEvaluator(SearchExpressionType.Iterable, SearchExpressionType.Selector, SearchExpressionType.Literal | SearchExpressionType.QueryString)]
        static IEnumerable<SearchItem> gt(SearchExpressionContext c) => Compare(c, (a, b) => DefaultComparer(a, b) > 0);

        [Description("Keep search results that have a greater or equal value."), Category("Comparers")]
        [SearchExpressionEvaluator(SearchExpressionType.Iterable, SearchExpressionType.Literal | SearchExpressionType.QueryString)]
        [SearchExpressionEvaluator(SearchExpressionType.Iterable, SearchExpressionType.Selector, SearchExpressionType.Literal | SearchExpressionType.QueryString)]
        static IEnumerable<SearchItem> gte(SearchExpressionContext c) => Compare(c, (a, b) => DefaultComparer(a, b) >= 0);

        [Description("Keep search results that have a lower value."), Category("Comparers")]
        [SearchExpressionEvaluator(SearchExpressionType.Iterable, SearchExpressionType.Literal | SearchExpressionType.QueryString)]
        [SearchExpressionEvaluator(SearchExpressionType.Iterable, SearchExpressionType.Selector, SearchExpressionType.Literal | SearchExpressionType.QueryString)]
        static IEnumerable<SearchItem> lw(SearchExpressionContext c) => Compare(c, (a, b) => DefaultComparer(a, b) < 0);

        [Description("Keep search results that have a lower or equal value."), Category("Comparers")]
        [SearchExpressionEvaluator(SearchExpressionType.Iterable, SearchExpressionType.Literal | SearchExpressionType.QueryString)]
        [SearchExpressionEvaluator(SearchExpressionType.Iterable, SearchExpressionType.Selector, SearchExpressionType.Literal | SearchExpressionType.QueryString)]
        static IEnumerable<SearchItem> lwe(SearchExpressionContext c) => Compare(c, (a, b) => DefaultComparer(a, b) <= 0);

        [Description("Keep search results that have an equal value."), Category("Comparers")]
        [SearchExpressionEvaluator(SearchExpressionType.Iterable, SearchExpressionType.Literal | SearchExpressionType.QueryString)]
        [SearchExpressionEvaluator(SearchExpressionType.Iterable, SearchExpressionType.Selector, SearchExpressionType.Literal | SearchExpressionType.QueryString)]
        static IEnumerable<SearchItem> eq(SearchExpressionContext c) => Compare(c, (a, b) => DefaultComparer(a, b) == 0);

        [Description("Keep search results that have a different value."), Category("Comparers")]
        [SearchExpressionEvaluator(SearchExpressionType.Iterable, SearchExpressionType.Literal | SearchExpressionType.QueryString)]
        [SearchExpressionEvaluator(SearchExpressionType.Iterable, SearchExpressionType.Selector, SearchExpressionType.Literal | SearchExpressionType.QueryString)]
        static IEnumerable<SearchItem> neq(SearchExpressionContext c) => Compare(c, (a, b) => DefaultComparer(a, b) != 0);

        [Description("Exclude search results for which the expression is not valid."), Category("Comparers")]
        [SearchExpressionEvaluator(SearchExpressionType.Iterable, SearchExpressionType.Text | SearchExpressionType.QueryString | SearchExpressionType.Selector)]
        public static IEnumerable<SearchItem> Where(SearchExpressionContext c)
        {
            var setExpr = c.args[0];
            var whereConditionExpr = c.args[1];
            var queryStr = whereConditionExpr.innerText;
            if (whereConditionExpr.types.HasFlag(SearchExpressionType.Selector))
            {
                queryStr = whereConditionExpr.outerText;
            }
            var setResults = setExpr.Execute(c);
            return EvaluatorManager.itemQueryEngine.WhereMainThread(c, setResults, queryStr.ToString());
        }
    }
}

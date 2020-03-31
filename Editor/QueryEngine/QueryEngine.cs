using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using NodesToStringPosition = System.Collections.Generic.Dictionary<Unity.QuickSearch.IQueryNode, System.Tuple<int,int>>;

namespace Unity.QuickSearch
{
    internal interface IQueryHandler<out TData, in TPayload>
    {
        IEnumerable<TData> Eval(TPayload payload);
    }
    internal interface IQueryHandlerFactory<TData, out TQueryHandler, TPayload>
        where TQueryHandler : IQueryHandler<TData, TPayload>
    {
        TQueryHandler Create(QueryGraph graph);
    }

    internal interface IParseResult
    {
        bool success { get; }
    }

    internal interface ITypeParser
    {
        Type type { get; }
        IParseResult Parse(string value);
    }

    internal readonly struct TypeParser<T> : ITypeParser
    {
        private readonly Func<string, ParseResult<T>> m_Parser;

        public Type type => typeof(T);

        public TypeParser(Func<string, ParseResult<T>> parser)
        {
            m_Parser = parser;
        }

        public IParseResult Parse(string value)
        {
            return m_Parser(value);
        }
    }

    /// <summary>
    /// A ParseResult holds the result of a parsing operation.
    /// </summary>
    /// <typeparam name="T">Type of the result of the parsing operation.</typeparam>
    public readonly struct ParseResult<T> : IParseResult
    {
        /// <summary>
        /// Flag indicating if the parsing succeeded or not.
        /// </summary>
        public bool success { get; }

        /// <summary>
        /// Actual result of the parsing.
        /// </summary>
        public readonly T parsedValue;

        /// <summary>
        /// Create a ParseResult.
        /// </summary>
        /// <param name="success">Flag indicating if the parsing succeeded or not.</param>
        /// <param name="value">Actual result of the parsing.</param>
        public ParseResult(bool success, T value)
        {
            this.success = success;
            this.parsedValue = value;
        }

        public static readonly ParseResult<T> none = new ParseResult<T>(false, default(T));
    }

    /// <summary>
    /// A QueryError holds the definition of a query parsing error.
    /// </summary>
    public class QueryError
    {
        /// <summary> Index where the error happened. </summary>
        public int index { get; }

        /// <summary> Length of the block that was being parsed. </summary>
        public int length { get; }

        /// <summary> Reason why the parsing failed. </summary>
        public string reason { get; }

        /// <summary>
        /// Construct a new QueryError with a default length of 1.
        /// </summary>
        /// <param name="index">Index where the error happened.</param>
        /// <param name="reason">Reason why the parsing failed.</param>
        public QueryError(int index, string reason)
        {
            this.index = index;
            this.reason = reason;
            length = 1;
        }

        /// <summary>
        /// Construct a new QueryError.
        /// </summary>
        /// <param name="index">Index where the error happened.</param>
        /// <param name="length">Length of the block that was being parsed.</param>
        /// <param name="reason">Reason why the parsing failed.</param>
        public QueryError(int index, int length, string reason)
        {
            this.index = index;
            this.reason = reason;
            this.length = length;
        }
    }

    /// <summary>
    /// A Query defines an operation that can be used to filter a data set.
    /// </summary>
    /// <typeparam name="TData">The filtered data type.</typeparam>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    public class Query<TData, TPayload>
        where TPayload : class
    {
        /// <summary> Indicates if the query is valid or not. </summary>
        public bool valid => errors.Count == 0 && graph != null;

        /// <summary> List of QueryErrors. </summary>
        public List<QueryError> errors { get; } = new List<QueryError>();

        internal IQueryHandler<TData, TPayload> graphHandler { get; set; }

        internal QueryGraph graph { get; }

        internal Query(QueryGraph graph, List<QueryError> errors)
        {
            this.graph = graph;
            this.errors.AddRange(errors);
        }
        internal Query(QueryGraph graph, List<QueryError> errors, IQueryHandler<TData, TPayload> graphHandler)
            : this(graph, errors)
        {
            if (valid)
            {
                this.graphHandler = graphHandler;
            }
        }

        /// <summary>
        /// Apply the filtering on a payload.
        /// </summary>
        /// <param name="payload">The data to filter</param>
        /// <returns>A filtered IEnumerable.</returns>
        public virtual IEnumerable<TData> Apply(TPayload payload = null)
        {
            if (!valid)
                return null;
            return graphHandler.Eval(payload);
        }

        /// <summary>
        /// Optimize the query by optimizing the underlying filtering graph.
        /// </summary>
        /// <param name="propagateNotToLeaves">Propagate "Not" operations to leaves, so only leaves can have "Not" operations as parents.</param>
        /// <param name="swapNotToRightHandSide">Swaps "Not" operations to the right hand side of combining operations (i.e. "And", "Or"). Useful if a "Not" operation is slow.</param>
        public void Optimize(bool propagateNotToLeaves, bool swapNotToRightHandSide)
        {
            graph?.Optimize(propagateNotToLeaves, swapNotToRightHandSide);
        }
    }

    /// <summary>
    /// A Query defines an operation that can be used to filter a data set.
    /// </summary>
    /// <typeparam name="T">The filtered data type.</typeparam>
    public class Query<T> : Query<T, IEnumerator<T>>
    {
        internal Query(QueryGraph graph, List<QueryError> errors, QueryEngine<T> engine)
            : base(graph, errors)
        {
            if (valid)
            {
                var dataWalkerGraphHandler = new DataWalkerQueryHandler<T>();
                dataWalkerGraphHandler.Initialize(engine, graph);
                graphHandler = dataWalkerGraphHandler;
            }
        }

        /// <summary>
        /// Apply the filtering on an IEnumerable data set.
        /// </summary>
        /// <param name="data">The data to filter</param>
        /// <returns>A filtered IEnumerable.</returns>
        public IEnumerable<T> Apply(IEnumerable<T> data)
        {
            return Apply(data.GetEnumerator());
        }

        /// <summary>
        /// Apply the filtering on an IEnumerator.
        /// </summary>
        /// <param name="data">The data to filter</param>
        /// <returns>A filtered IEnumerable.</returns>
        public override IEnumerable<T> Apply(IEnumerator<T> data = null)
        {
            if (!valid)
                return null;
            return graphHandler.Eval(data);
        }
    }

    internal static class EnumerableExtensions
    {
        public static IEnumerable<T> ToIEnumerable<T>(this IEnumerator<T> enumerator)
        {
            while (enumerator.MoveNext())
            {
                yield return enumerator.Current;
            }
        }
    }

    internal interface IQueryEngineImplementation
    {
        void AddFilterOperationGenerator<T>();
    }

    /// <summary>
    /// Struct containing the available query validation options.
    /// </summary>
    public struct QueryValidationOptions
    {
        /// <summary>
        /// Boolean indicating if filters should be validated.
        /// </summary>
        public bool validateFilters;

        /// <summary>
        /// Boolean indicating if unknown filters should be skipped.
        /// If validateFilters is true and skipUnknownFilters is false, unknown filters will generate errors
        /// if no default handler is provided.
        /// </summary>
        public bool skipUnknownFilters;
    }

    internal sealed class QueryEngineImpl<TData> : IQueryEngineImplementation
    {
        private Dictionary<string, IFilter> m_Filters = new Dictionary<string, IFilter>();
        private Func<TData, string, string, string, bool> m_DefaultFilterHandler;
        private Func<TData, string, string, string, string, bool> m_DefaultParamFilterHandler;
        private Dictionary<string, FilterOperator> m_FilterOperators = new Dictionary<string, FilterOperator>();
        private List<ITypeParser> m_TypeParsers = new List<ITypeParser>();
        Dictionary<Type, ITypeParser> m_DefaultTypeParsers = new Dictionary<Type, ITypeParser>();
        Dictionary<Type, IFilterOperationGenerator> m_FilterOperationGenerators = new Dictionary<Type, IFilterOperationGenerator>();

        // To match a regex at a specific index, use \\G and Match(input, startIndex)
        private static readonly Regex k_PhraseRx = new Regex("\\G!?\\\".*?\\\"");
        private Regex m_FilterRx = new Regex("\\G([\\w]+)([:><=!]+)(\\\".*?\\\"|[\\S]+)");
        private static readonly Regex k_WordRx = new Regex("\\G!?\\S+");

        private static readonly HashSet<char> k_WhiteSpaceChars = new HashSet<char>(" \f\n\r\t\v");
        private static readonly Dictionary<string, Func<IQueryNode>> k_CombiningTokenGenerators = new Dictionary<string, Func<IQueryNode>>
        {
            {"and", () => new AndNode()},
            {"or", () => new OrNode()},
            {"not", () => new NotNode()},
            {"-", () => new NotNode()}
        };

        enum DefaultOperator
        {
            Contains,
            Equal,
            NotEqual,
            Greater,
            GreaterOrEqual,
            Less,
            LessOrEqual
        }

        readonly struct FilterCreationParams
        {
            public readonly string token;
            public readonly string[] supportedOperators;
            public readonly bool overridesGlobalComparisonOptions;
            public readonly StringComparison comparisonOptions;
            public readonly bool useParameterTransformer;
            public readonly string parameterTransformerFunction;
            public readonly Type parameterTransformerAttributeType;

            public FilterCreationParams(
                string token, string[] supportedOperators, bool overridesGlobalComparison,
                StringComparison comparisonOptions, bool useParameterTransformer,
                string parameterTransformerFunction, Type parameterTransformerAttributeType)
            {
                this.token = token;
                this.supportedOperators = supportedOperators;
                this.overridesGlobalComparisonOptions = overridesGlobalComparison;
                this.comparisonOptions = comparisonOptions;
                this.useParameterTransformer = useParameterTransformer;
                this.parameterTransformerFunction = parameterTransformerFunction;
                this.parameterTransformerAttributeType = parameterTransformerAttributeType;
            }
        }

        private delegate int TokenConsumer(string text, int tokenIndexStart, int tokenEndIndex, List<IQueryNode> nodes, List<QueryError> errors, NodesToStringPosition nodesToStringPosition, out bool matched);

        private List<TokenConsumer> m_TokenConsumers;

        public Func<TData, IEnumerable<string>> searchDataCallback { get; private set; }

        public QueryValidationOptions validationOptions { get; set; }

        public StringComparison globalStringComparison { get; set; } = StringComparison.OrdinalIgnoreCase;

        public StringComparison searchDataStringComparison { get; private set; } = StringComparison.OrdinalIgnoreCase;
        public bool searchDataOverridesGlobalStringComparison { get; private set; }

        public QueryEngineImpl()
            : this(new QueryValidationOptions())
        { }

        public QueryEngineImpl(QueryValidationOptions validationOptions)
        {
            this.validationOptions = validationOptions;

            // Default operators
            AddOperator(":", false)
                .AddHandler((object ev, object fv, StringComparison sc) => CompareObjects(ev, fv, sc, DefaultOperator.Contains, ":"))
                .AddHandler((string ev, string fv, StringComparison sc) => ev?.IndexOf(fv, sc) >= 0)
                .AddHandler((int ev, int fv, StringComparison sc) => ev.ToString().IndexOf(fv.ToString(), sc) != -1)
                .AddHandler((float ev, float fv, StringComparison sc) => ev.ToString().IndexOf(fv.ToString(), sc) != -1);
            AddOperator("=", false)
                .AddHandler((object ev, object fv, StringComparison sc) => CompareObjects(ev, fv, sc, DefaultOperator.Equal, "="))
                .AddHandler((int ev, int fv) => ev == fv)
                .AddHandler((float ev, float fv) => Math.Abs(ev - fv) < Mathf.Epsilon)
                .AddHandler((bool ev, bool fv) => ev == fv)
                .AddHandler((string ev, string fv, StringComparison sc) => string.Equals(ev, fv, sc));
            AddOperator("!=", false)
                .AddHandler((object ev, object fv, StringComparison sc) => CompareObjects(ev, fv, sc, DefaultOperator.NotEqual, "!="))
                .AddHandler((int ev, int fv) => ev != fv)
                .AddHandler((float ev, float fv) => Math.Abs(ev - fv) >= Mathf.Epsilon)
                .AddHandler((bool ev, bool fv) => ev != fv)
                .AddHandler((string ev, string fv, StringComparison sc) => !string.Equals(ev, fv, sc));
            AddOperator("<", false)
                .AddHandler((object ev, object fv, StringComparison sc) => CompareObjects(ev, fv, sc, DefaultOperator.Less, "<"))
                .AddHandler((int ev, int fv) => ev < fv)
                .AddHandler((float ev, float fv) => ev < fv)
                .AddHandler((string ev, string fv, StringComparison sc) => string.Compare(ev, fv, sc) < 0);
            AddOperator(">", false)
                .AddHandler((object ev, object fv, StringComparison sc) => CompareObjects(ev, fv, sc, DefaultOperator.Greater, ">"))
                .AddHandler((int ev, int fv) => ev > fv)
                .AddHandler((float ev, float fv) => ev > fv)
                .AddHandler((string ev, string fv, StringComparison sc) => string.Compare(ev, fv, sc) > 0);
            AddOperator("<=", false)
                .AddHandler((object ev, object fv, StringComparison sc) => CompareObjects(ev, fv, sc, DefaultOperator.LessOrEqual, "<="))
                .AddHandler((int ev, int fv) => ev <= fv)
                .AddHandler((float ev, float fv) => ev <= fv)
                .AddHandler((string ev, string fv, StringComparison sc) => string.Compare(ev, fv, sc) <= 0);
            AddOperator(">=", false)
                .AddHandler((object ev, object fv, StringComparison sc) => CompareObjects(ev, fv, sc, DefaultOperator.GreaterOrEqual, ">="))
                .AddHandler((int ev, int fv) => ev >= fv)
                .AddHandler((float ev, float fv) => ev >= fv)
                .AddHandler((string ev, string fv, StringComparison sc) => string.Compare(ev, fv, sc) >= 0);

            BuildFilterRegex();
            BuildDefaultTypeParsers();
        }

        private bool CompareObjects(object ev, object fv, StringComparison sc, DefaultOperator op, string opToken)
        {
            if (ev == null || fv == null)
                return false;

            var evt = ev.GetType();
            var fvt = fv.GetType();

            if (m_FilterOperators.TryGetValue(opToken, out var operators))
            {
                var opHandler = operators.GetHandler(evt, fvt);
                if (opHandler != null)
                    return opHandler.Invoke(ev, fv, sc);
            }

            if (evt != fvt)
                return false;

            if (ev is string evs && fv is string fvs)
            {
                switch (op)
                {
                    case DefaultOperator.Contains: return evs.IndexOf(fvs, sc) != -1;
                    case DefaultOperator.Equal: return evs.Equals(fvs, sc);
                    case DefaultOperator.NotEqual: return !evs.Equals(fvs, sc);
                    case DefaultOperator.Greater: return string.Compare(evs, fvs, sc) > 0;
                    case DefaultOperator.GreaterOrEqual: return string.Compare(evs, fvs, sc) >= 0;
                    case DefaultOperator.Less: return string.Compare(evs, fvs, sc) < 0;
                    case DefaultOperator.LessOrEqual: return string.Compare(evs, fvs, sc) <= 0;
                }

                return false;
            }

            switch (op)
            {
                case DefaultOperator.Contains: return ev?.ToString().IndexOf(fv?.ToString(), sc) >= 0;
                case DefaultOperator.Equal: return ev?.Equals(fv) ?? false;
                case DefaultOperator.NotEqual: return !ev?.Equals(fv) ?? false;
                case DefaultOperator.Greater: return Comparer<object>.Default.Compare(ev, fv) > 0;
                case DefaultOperator.GreaterOrEqual: return Comparer<object>.Default.Compare(ev, fv) >= 0;
                case DefaultOperator.Less: return Comparer<object>.Default.Compare(ev, fv) < 0;
                case DefaultOperator.LessOrEqual: return Comparer<object>.Default.Compare(ev, fv) <= 0;
            }

            return false;
        }

        public void AddFilter(string token, IFilter filter)
        {
            if (m_Filters.ContainsKey(token))
            {
                Debug.LogWarning($"A filter for \"{token}\" already exists. Please remove it first before adding a new one.");
                return;
            }
            m_Filters.Add(token, filter);
        }

        public void RemoveFilter(string token)
        {
            if (!m_Filters.ContainsKey(token))
            {
                Debug.LogWarning($"No filter found for \"{token}\".");
                return;
            }
            m_Filters.Remove(token);
        }

        public FilterOperator AddOperator(string op)
        {
            return AddOperator(op, true);
        }

        private FilterOperator AddOperator(string op, bool rebuildFilterRegex)
        {
            if (m_FilterOperators.ContainsKey(op))
                return m_FilterOperators[op];
            var filterOperator = new FilterOperator(op, this);
            m_FilterOperators.Add(op, filterOperator);
            if (rebuildFilterRegex)
                BuildFilterRegex();
            return filterOperator;
        }

        public FilterOperator GetOperator(string op)
        {
            return m_FilterOperators.ContainsKey(op) ? m_FilterOperators[op] : null;
        }

        public void AddOperatorHandler<TLhs, TRhs>(string op, Func<TLhs, TRhs, bool> handler)
        {
            AddOperatorHandler<TLhs, TRhs>(op, (ev, fv, sc) => handler(ev, fv));
        }

        public void AddOperatorHandler<TLhs, TRhs>(string op, Func<TLhs, TRhs, StringComparison, bool> handler)
        {
            if (!m_FilterOperators.ContainsKey(op))
                return;
            m_FilterOperators[op].AddHandler(handler);

            // Enums are user defined but still simple enough to generate a parse function for them.
            if (typeof(TRhs).IsEnum && !m_DefaultTypeParsers.ContainsKey(typeof(TRhs)))
            {
                AddDefaultEnumTypeParser<TRhs>();
            }
        }

        public void SetDefaultFilter(Func<TData, string, string, string, bool> handler)
        {
            m_DefaultFilterHandler = handler;
        }

        public void SetDefaultParamFilter(Func<TData, string, string, string, string, bool> handler)
        {
            m_DefaultParamFilterHandler = handler;
        }

        public void SetSearchDataCallback(Func<TData, IEnumerable<string>> getSearchDataCallback)
        {
            searchDataCallback = getSearchDataCallback;
            // Remove the override flag in case it was already set.
            searchDataOverridesGlobalStringComparison = false;
        }

        public void SetSearchDataCallback(Func<TData, IEnumerable<string>> getSearchDataCallback, StringComparison stringComparison)
        {
            SetSearchDataCallback(getSearchDataCallback);
            searchDataStringComparison = stringComparison;
            searchDataOverridesGlobalStringComparison = true;
        }

        public void AddTypeParser<TFilterConstant>(Func<string, ParseResult<TFilterConstant>> parser)
        {
            m_TypeParsers.Add(new TypeParser<TFilterConstant>(parser));
            AddFilterOperationGenerator<TFilterConstant>();
        }

        private IQueryNode BuildGraphRecursively(string text, int startIndex, int endIndex, List<QueryError> errors, NodesToStringPosition nodesToStringPosition)
        {
            var expressionNodes = new List<IQueryNode>();
            var index = startIndex;
            while (index < endIndex)
            {
                var matched = false;
                foreach (var tokenConsumer in m_TokenConsumers)
                {
                    var consumed = tokenConsumer(text, index, endIndex, expressionNodes, errors, nodesToStringPosition, out var consumerMatched);
                    if (!consumerMatched)
                        continue;
                    if (consumed == -1)
                    {
                        return null;
                    }
                    index += consumed;
                    matched = true;
                    break;
                }

                if (!matched)
                {
                    errors.Add(new QueryError(index, $"Error parsing string. No token could be deduced at {index}"));
                    return null;
                }
            }

            InsertAndIfNecessary(expressionNodes, nodesToStringPosition);
            var rootNode = CombineNodesToTree(expressionNodes, errors, nodesToStringPosition);
            ValidateGraph(rootNode, errors, nodesToStringPosition);
            return rootNode;
        }

        internal QueryGraph BuildGraph(string text, List<QueryError> errors)
        {
            var nodesToStringPosition = new NodesToStringPosition();
            var rootNode = BuildGraphRecursively(text, 0, text.Length, errors, nodesToStringPosition);

            // Final simplification
            RemoveNoOpNodes(ref rootNode, errors, nodesToStringPosition);

            return new QueryGraph(rootNode);
        }

        private static int ConsumeEmpty(string text, int startIndex, int endIndex, List<IQueryNode> nodes, List<QueryError> errors, NodesToStringPosition nodesToStringPosition, out bool matched)
        {
            var currentIndex = startIndex;
            var lengthMatched = 0;
            matched = false;
            while (currentIndex < endIndex && IsWhiteSpaceChar(text[currentIndex]))
            {
                ++currentIndex;
                ++lengthMatched;
                matched = true;
            }
            return lengthMatched;
        }

        private static bool IsWhiteSpaceChar(char c)
        {
            return k_WhiteSpaceChars.Contains(c);
        }

        private static int ConsumeCombiningToken(string text, int startIndex, int endIndex, List<IQueryNode> nodes, List<QueryError> errors, NodesToStringPosition nodesToStringPosition, out bool matched)
        {
            var totalUsableLength = endIndex - startIndex;

            foreach (var combiningTokenKVP in k_CombiningTokenGenerators)
            {
                var combiningToken = combiningTokenKVP.Key;
                var tokenLength = combiningToken.Length;
                if (tokenLength > totalUsableLength)
                    continue;

                var stringView = text.GetStringView(startIndex, startIndex + tokenLength);
                if (stringView == combiningToken)
                {
                    matched = true;
                    var newNode = combiningTokenKVP.Value();
                    nodesToStringPosition.Add(newNode, new Tuple<int, int>(startIndex, stringView.Length));
                    nodes.Add(newNode);
                    return stringView.Length;
                }
            }

            matched = false;
            return -1;
        }

        private int ConsumeFilter(string text, int startIndex, int endIndex, List<IQueryNode> nodes, List<QueryError> errors, NodesToStringPosition nodesToStringPosition, out bool matched)
        {
            var match = m_FilterRx.Match(text, startIndex, endIndex - startIndex);
            if (!match.Success)
            {
                matched = false;
                return -1;
            }

            matched = true;
            var node = CreateFilterToken(match.Value, match, startIndex, errors);
            if (node != null)
            {
                nodesToStringPosition.Add(node, new Tuple<int, int>(startIndex, match.Length));
                nodes.Add(node);
            }

            return match.Length;
        }

        private int ConsumeWords(string text, int startIndex, int endIndex, List<IQueryNode> nodes, List<QueryError> errors, NodesToStringPosition nodesToStringPosition, out bool matched)
        {
            var match = k_PhraseRx.Match(text, startIndex, endIndex - startIndex);
            if (!match.Success)
                match = k_WordRx.Match(text, startIndex, endIndex - startIndex);
            if (!match.Success)
            {
                matched = false;
                return -1;
            }

            matched = true;
            if (validationOptions.validateFilters && searchDataCallback == null)
            {
                errors.Add(new QueryError(startIndex, match.Length, "Cannot use a search word without setting the search data callback."));
                return -1;
            }

            var node = CreateWordExpressionNode(match.Value);
            if (node != null)
            {
                nodesToStringPosition.Add(node, new Tuple<int, int>(startIndex, match.Length));
                nodes.Add(node);
            }

            return match.Length;
        }

        private int ConsumeGroup(string text, int groupStartIndex, int endIndex, List<IQueryNode> nodes, List<QueryError> errors, NodesToStringPosition nodesToStringPosition, out bool matched)
        {
            if (groupStartIndex >= text.Length || text[groupStartIndex] != '(')
            {
                matched = false;
                return -1;
            }

            matched = true;
            if (groupStartIndex < 0 || groupStartIndex >= text.Length)
            {
                errors.Add(new QueryError(0, $"A group should have been found but index was {groupStartIndex}"));
                return -1;
            }

            var charConsumed = 0;

            var parenthesisCounter = 1;
            var groupEndIndex = groupStartIndex + 1;
            for (; groupEndIndex < text.Length && parenthesisCounter > 0; ++groupEndIndex)
            {
                if (text[groupEndIndex] == '(')
                    ++parenthesisCounter;
                else if (text[groupEndIndex] == ')')
                    --parenthesisCounter;
            }

            // Because of the final ++groupEndIndex, decrement the index
            --groupEndIndex;

            if (parenthesisCounter != 0)
            {
                errors.Add(new QueryError(groupStartIndex, $"Unbalanced parenthesis"));
                return -1;
            }

            charConsumed = groupEndIndex - groupStartIndex + 1;

            var groupNode = BuildGraphRecursively(text, groupStartIndex + 1, groupEndIndex, errors, nodesToStringPosition);
            if (groupNode != null)
                nodes.Add(groupNode);

            return charConsumed;
        }

        private static void InsertAndIfNecessary(List<IQueryNode> nodes, NodesToStringPosition nodesToStringPosition)
        {
            if (nodes.Count <= 1)
                return;

            for (var i = 0; i < nodes.Count - 1; ++i)
            {
                if (nodes[i] is CombinedNode cn && cn.leaf)
                    continue;
                if (nodes[i + 1] is CombinedNode nextCn && nextCn.leaf && nextCn.type != QueryNodeType.Not)
                    continue;

                var andNode = new AndNode();
                var previousNodePosition = nodesToStringPosition[nodes[i]];
                var nextNodePosition = nodesToStringPosition[nodes[i + 1]];
                var startPosition = previousNodePosition.Item1 + previousNodePosition.Item2;
                var length = nextNodePosition.Item1 - startPosition;
                nodesToStringPosition.Add(andNode, new Tuple<int, int>(startPosition, length));
                nodes.Insert(i + 1, andNode);
                // Skip this new node
                ++i;
            }
        }

        private static bool IsPhraseToken(string token)
        {
            var startIndex = token[0] == '!' ? 1 : 0;
            var endIndex = token.Length - 1;
            return token[startIndex] == '"' && token[endIndex] == '"';
        }

        private IQueryNode CreateFilterToken(string token, Match match, int index, List<QueryError> errors)
        {
            if (match.Groups.Count != 5)
            {
                errors.Add(new QueryError(index, token.Length, $"Could not parse filter block \"{token}\"."));
                return null;
            }

            var filterType = match.Groups[1].Value;
            var filterParam = match.Groups[2].Value;
            var filterOperator = match.Groups[3].Value;
            var filterValue = match.Groups[4].Value;

            var filterTypeIndex = index + match.Groups[1].Index;
            var filterOperatorIndex = index + match.Groups[3].Index;
            var filterValueIndex = index + match.Groups[4].Index;

            if (!string.IsNullOrEmpty(filterParam))
            {
                // Trim () around the group
                filterParam = filterParam.Trim('(', ')');
            }

            if (!m_Filters.TryGetValue(filterType, out var filter))
            {
                // When skipping unknown filter, just return a noop. The graph will get simplified later.
                if (validationOptions.skipUnknownFilters)
                {
                    return new NoOpNode(token);
                }

                if (m_DefaultFilterHandler == null && validationOptions.validateFilters)
                {
                    errors.Add(new QueryError(filterTypeIndex, filterType.Length, $"Unknown filter type \"{filterType}\"."));
                    return null;
                }
                if (string.IsNullOrEmpty(filterParam))
                    filter = new DefaultFilter<TData>(filterType, m_DefaultFilterHandler ?? ((o, s, fo, value) => false) );
                else
                    filter = new DefaultParamFilter<TData>(filterType, m_DefaultParamFilterHandler ?? ((o, s, param, fo, value) => false));
            }

            if (!m_FilterOperators.ContainsKey(filterOperator))
            {
                errors.Add(new QueryError(filterOperatorIndex, filterOperator.Length, $"Unknown filter operator \"{filterOperator}\"."));
                return null;
            }
            var op = m_FilterOperators[filterOperator];

            if (filter.supportedFilters.Any() && !filter.supportedFilters.Any(filterOp => filterOp.Equals(op.token)))
            {
                errors.Add(new QueryError(filterOperatorIndex, filterOperator.Length, $"The filter \"{op.token}\" is not supported for this filter."));
                return null;
            }

            if (IsPhraseToken(filterValue))
                filterValue = filterValue.Trim('"');

            var parseResult = ParseFilterValue(filterValue, filter, op, out var filterValueType);

            if (!parseResult.success)
            {
                errors.Add(new QueryError(filterValueIndex, filterValue.Length, $"The value {filterValue} could not be converted to any of the supported handler types."));
                return null;
            }

            IFilterOperationGenerator generator = null;
            if (!m_FilterOperationGenerators.ContainsKey(filterValueType))
            {
                errors.Add(new QueryError(filterValueIndex, filterValue.Length, $"The type {filterValueType} does not have any corresponding operation generator."));
                return null;
            }
            generator = m_FilterOperationGenerators[filterValueType];

            var generatorData = new FilterOperationGeneratorData
            {
                filterValue = filterValue,
                filterValueParseResult = parseResult,
                globalStringComparison = globalStringComparison,
                op = op,
                paramValue = filterParam,
                generator = generator
            };

            var operation = filter.GenerateOperation(generatorData, filterOperatorIndex, errors);
            return new FilterNode(operation, token);
        }

        private IParseResult ParseFilterValue(string filterValue, IFilter filter, FilterOperator op, out Type filterValueType)
        {
            var foundValueType = false;
            IParseResult parseResult = null;
            filterValueType = filter.type;

            // Filter resolver only support values of the same type as the filter in their resolver
            if (filter.resolver)
            {
                return ParseSpecificType(filterValue, filter.type);
            }

            // Check custom parsers first
            foreach (var typeParser in m_TypeParsers)
            {
                parseResult = typeParser.Parse(filterValue);
                if (parseResult.success)
                {
                    filterValueType = typeParser.type;
                    foundValueType = true;
                    break;
                }
            }

            // If no custom type parsers managed to parse the string, try our default ones with the types of handlers available for the operator
            if (!foundValueType)
            {
                // OPTME: Not the prettiest bit of code, but we got rid of LINQ at least.
                var handlerTypes = new List<FilterOperatorTypes>();
                foreach (var handlersKey in op.handlers.Keys)
                {
                    if (handlersKey != filter.type)
                        continue;

                    foreach (var rhsType in op.handlers[handlersKey].Keys)
                    {
                        handlerTypes.Add(new FilterOperatorTypes(handlersKey, rhsType));
                    }
                }
                if (filter.type != typeof(object))
                {
                    foreach (var handlersKey in op.handlers.Keys)
                    {
                        if (handlersKey != typeof(object))
                            continue;

                        foreach (var rhsType in op.handlers[handlersKey].Keys)
                        {
                            handlerTypes.Add(new FilterOperatorTypes(handlersKey, rhsType));
                        }
                    }
                }
                var sortedHandlerTypes = new List<FilterOperatorTypes>();
                foreach (var handlerType in handlerTypes)
                {
                    if (handlerType.rightHandSideType != typeof(object))
                    {
                        sortedHandlerTypes.Add(handlerType);
                    }
                }
                foreach (var handlerType in handlerTypes)
                {
                    if (handlerType.rightHandSideType == typeof(object))
                    {
                        sortedHandlerTypes.Add(handlerType);
                    }
                }

                foreach (var opHandlerTypes in sortedHandlerTypes)
                {
                    var rhsType = opHandlerTypes.rightHandSideType;
                    parseResult = GenerateParseResultForType(filterValue, rhsType);
                    if (parseResult.success)
                    {
                        filterValueType = rhsType;
                        foundValueType = true;
                        break;
                    }
                }
            }

            // If we still didn't manage to parse the value, try with the type of the filter instead
            if (!foundValueType)
            {
                // Try one last time with the type of the filter instead
                parseResult = GenerateParseResultForType(filterValue, filter.type);
                if (parseResult.success)
                    filterValueType = filter.type;
            }

            return parseResult;
        }

        private IParseResult ParseSpecificType(string filterValue, Type type)
        {
            foreach (var typeParser in m_TypeParsers)
            {
                if (type != typeParser.type)
                    continue;

                return typeParser.Parse(filterValue);
            }

            return GenerateParseResultForType(filterValue, type);
        }

        private static IQueryNode CreateWordExpressionNode(string token)
        {
            var isExact = token.StartsWith("!");
            if (isExact)
                token = token.Remove(0, 1);
            if (IsPhraseToken(token))
                token = token.Trim('"');

            return new SearchNode(token, isExact);
        }

        private static IQueryNode CombineNodesToTree(List<IQueryNode> expressionNodes, List<QueryError> errors, NodesToStringPosition nodesToStringPosition)
        {
            var count = expressionNodes.Count;
            if (count == 0)
                return null;

            CombineNotNodes(expressionNodes, errors, nodesToStringPosition);
            CombineAndOrNodes(QueryNodeType.And, expressionNodes, errors, nodesToStringPosition);
            CombineAndOrNodes(QueryNodeType.Or, expressionNodes, errors, nodesToStringPosition);

            return expressionNodes[0];
        }

        private static void CombineNotNodes(List<IQueryNode> expressionNodes, List<QueryError> errors, NodesToStringPosition nodesToStringPosition)
        {
            var count = expressionNodes.Count;
            if (count == 0)
                return;

            for (var i = count - 1; i >= 0; --i)
            {
                var currentNode = expressionNodes[i];
                if (currentNode.type != QueryNodeType.Not)
                    continue;
                var nextNode = i < count - 1 ? expressionNodes[i + 1] : null;
                if (!(currentNode is CombinedNode combinedNode))
                    continue;
                if (!combinedNode.leaf)
                    continue;

                var (startIndex, length) = nodesToStringPosition[currentNode];
                if (nextNode == null)
                {
                    errors.Add(new QueryError(startIndex + length, $"Missing operand to combine with node {currentNode.type}."));
                }
                else
                {
                    combinedNode.AddNode(nextNode);
                    expressionNodes.RemoveAt(i + 1);
                }
            }
        }

        private static void CombineAndOrNodes(QueryNodeType nodeType, List<IQueryNode> expressionNodes, List<QueryError> errors, NodesToStringPosition nodesToStringPosition)
        {
            var count = expressionNodes.Count;
            if (count == 0)
                return;

            for (var i = count - 1; i >= 0; --i)
            {
                var currentNode = expressionNodes[i];
                if (currentNode.type != nodeType)
                    continue;
                var nextNode = i < count - 1 ? expressionNodes[i + 1] : null;
                var previousNode = i > 0 ? expressionNodes[i - 1] : null;
                if (!(currentNode is CombinedNode combinedNode))
                    continue;
                if (!combinedNode.leaf)
                    continue;

                var (startIndex, length) = nodesToStringPosition[currentNode];
                if (previousNode == null)
                {
                    errors.Add(new QueryError(startIndex + length, $"Missing left-hand operand to combine with node {currentNode.type}."));
                }
                else
                {
                    combinedNode.AddNode(previousNode);
                    expressionNodes.RemoveAt(i - 1);
                    // Update current index
                    --i;
                }

                if (nextNode == null)
                {
                    errors.Add(new QueryError(startIndex + length, $"Missing right-hand operand to combine with node {currentNode.type}."));
                }
                else
                {
                    combinedNode.AddNode(nextNode);
                    expressionNodes.RemoveAt(i + 1);
                }
            }
        }

        private static void RemoveNoOpNodes(ref IQueryNode node, List<QueryError> errors, NodesToStringPosition nodesToStringPosition)
        {
            if (node == null)
                return;

            if (!(node is CombinedNode combinedNode))
            {
                // When not processing NoOp, nothing to do.
                if (node.type != QueryNodeType.NoOp)
                    return;

                // This is the root. Set it to null
                if (node.parent == null)
                {
                    node = null;
                    return;
                }

                // Otherwise, remove ourselves from our parent
                node.parent.children.Remove(node);
            }
            else
            {
                var children = combinedNode.children.ToArray();
                for (var i = 0; i < children.Length; ++i)
                {
                    RemoveNoOpNodes(ref children[i], errors, nodesToStringPosition);
                }

                // If we have become a leaf, remove ourselves from our parent
                if (combinedNode.leaf)
                {
                    if (combinedNode.parent != null)
                        combinedNode.parent.children.Remove(node);
                    else
                        node = null;

                    return;
                }

                // If we are a Not and not a leaf, nothing to do.
                if (combinedNode.type == QueryNodeType.Not)
                    return;

                // If we are an And or an Or and children count is still 2, nothing to do
                if (combinedNode.children.Count == 2)
                    return;

                // If we have no parent, we are the root so the remaining child must take our place
                if (combinedNode.parent == null)
                {
                    node = combinedNode.children[0];
                    return;
                }

                // Otherwise, replace ourselves with the remaining child in our parent child list
                var index = combinedNode.parent.children.IndexOf(combinedNode);
                if (index == -1)
                {
                    var (nodeStringIndex, _) = nodesToStringPosition[node];
                    errors.Add(new QueryError(nodeStringIndex, $"Node {combinedNode.type} not found in its parent's children list."));
                    return;
                }

                combinedNode.parent.children[index] = combinedNode.children[0];
            }
        }

        private static void ValidateGraph(IQueryNode root, List<QueryError> errors, NodesToStringPosition nodesToStringPosition)
        {
            if (root == null)
            {
                errors.Add(new QueryError(0, "Encountered a null node."));
                return;
            }
            var (position, length) = nodesToStringPosition[root];
            if (root is CombinedNode cn)
            {
                if (root.leaf)
                {
                    errors.Add(new QueryError(position, length, $"Node {root.type} is a leaf."));
                    return;
                }

                if (root.type == QueryNodeType.Not && root.children.Count != 1)
                {
                    errors.Add(new QueryError(position, length, $"Node {root.type} should have a child."));
                }
                else if (root.type != QueryNodeType.Not && root.children.Count != 2)
                {
                    errors.Add(new QueryError(position, length, $"Node {root.type} should have 2 children."));
                }

                foreach (var child in root.children)
                {
                    ValidateGraph(child, errors, nodesToStringPosition);
                }
            }
        }

        private void BuildFilterRegex()
        {
            var sortedOperators = m_FilterOperators.Keys.Select(Regex.Escape).ToList();
            sortedOperators.Sort((s, s1) => s1.Length.CompareTo(s.Length));
            var filterRx = $"\\G([\\w]+)(\\([^\\(\\)]+\\))?({string.Join("|", sortedOperators)}+)(\\\".*?\\\"|[\\S]+)";
            m_FilterRx = new Regex(filterRx, RegexOptions.Compiled);

            // The order of regex in this list is important. Keep it like that unless you know what you are doing!
            m_TokenConsumers = new List<TokenConsumer>
            {
                ConsumeEmpty,
                ConsumeCombiningToken,
                ConsumeGroup,
                ConsumeFilter,
                ConsumeWords
            };
        }

        private IParseResult GenerateParseResultForType(string value, Type type)
        {
            // Check if we have a default type parser before doing the expensive reflection call
            if (m_DefaultTypeParsers.ContainsKey(type))
            {
                var parser = m_DefaultTypeParsers[type];
                return parser.Parse(value);
            }

            var thisClassType = typeof(QueryEngineImpl<TData>);
            var method = thisClassType.GetMethod("ParseData", BindingFlags.NonPublic | BindingFlags.Instance);
            var typedMethod = method.MakeGenericMethod(type);
            return typedMethod.Invoke(this, new object[] { value }) as IParseResult;
        }

        private ParseResult<T> ParseData<T>(string value)
        {
            if (Utils.TryConvertValue(value, out T parsedValue))
            {
                // Last resort to get a operation generator if we don't have one yet
                AddFilterOperationGenerator<T>();

                return new ParseResult<T>(true, parsedValue);
            }

            return ParseResult<T>.none;
        }

        private void BuildDefaultTypeParsers()
        {
            AddDefaultTypeParser(s => int.TryParse(s, out var value) ? new ParseResult<int>(true, value) : ParseResult<int>.none);
            AddDefaultTypeParser(s => float.TryParse(s, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out var value) ? new ParseResult<float>(true, value) : ParseResult<float>.none);
            AddDefaultTypeParser(s => bool.TryParse(s, out var value) ? new ParseResult<bool>(true, value) : ParseResult<bool>.none);
            AddDefaultTypeParser(s => double.TryParse(s, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out var value) ? new ParseResult<double>(true, value) : ParseResult<double>.none);
        }

        private void AddDefaultTypeParser<T>(Func<string, ParseResult<T>> parser)
        {
            if (m_DefaultTypeParsers.ContainsKey(typeof(T)))
            {
                Debug.LogWarning($"A default parser for type {typeof(T)} already exists.");
                return;
            }
            m_DefaultTypeParsers.Add(typeof(T), new TypeParser<T>(parser));
            AddFilterOperationGenerator<T>();
        }

        private void AddDefaultEnumTypeParser<T>()
        {
            AddDefaultTypeParser(s =>
            {
                try
                {
                    var value = Enum.Parse(typeof(T), s, true);
                    return new ParseResult<T>(true, (T)value);
                }
                catch (Exception)
                {
                    return ParseResult<T>.none;
                }
            });
        }

        public void AddFilterOperationGenerator<T>()
        {
            if (m_FilterOperationGenerators.ContainsKey(typeof(T)))
                return;
            m_FilterOperationGenerators.Add(typeof(T), new FilterOperationGenerator<T>());
        }

        public void AddFiltersFromAttribute<TFilterAttribute, TTransformerAttribute>()
            where TFilterAttribute : QueryEngineFilterAttribute
            where TTransformerAttribute : QueryEngineParameterTransformerAttribute
        {
            var filters = Utils.GetAllMethodsWithAttribute<TFilterAttribute>()
                .Select(CreateFilterFromFilterAttribute<TFilterAttribute, TTransformerAttribute>)
                .Where(filter => filter != null);
            foreach (var filter in filters)
            {
                AddFilter(filter.token, filter);
            }
        }

        private IFilter CreateFilterFromFilterAttribute<TFilterAttribute, TTransformerAttribute>(MethodInfo mi)
            where TFilterAttribute : QueryEngineFilterAttribute
            where TTransformerAttribute : QueryEngineParameterTransformerAttribute
        {
            var attr = mi.GetCustomAttributes(typeof(TFilterAttribute), false).Cast<TFilterAttribute>().First();
            var filterToken = attr.token;
            var stringComparison = attr.overridesStringComparison ? attr.comparisonOptions : globalStringComparison;
            var supportedOperators = attr.supportedOperators;
            var creationParams = new FilterCreationParams(
                filterToken,
                supportedOperators,
                attr.overridesStringComparison,
                stringComparison,
                attr.useParamTransformer,
                attr.paramTransformerFunction,
                typeof(TTransformerAttribute)
                );

            try
            {
                var inputParams = mi.GetParameters();
                if (inputParams.Length == 0)
                {
                    Debug.LogWarning($"Filter method {mi.Name} should have at least one input parameter.");
                    return null;
                }

                var objectParam = inputParams[0];
                if (objectParam.ParameterType != typeof(TData))
                {
                    Debug.LogWarning($"Parameter {objectParam.Name}'s type of filter method {mi.Name} must be {typeof(TData)}.");
                    return null;
                }
                var returnType = mi.ReturnType;

                // Basic filter
                if (inputParams.Length == 1)
                {
                    return CreateFilterForMethodInfo(creationParams, mi, false, false, returnType);
                }

                // Filter function
                if (inputParams.Length == 2)
                {
                    var filterParamType = inputParams[1].ParameterType;
                    return CreateFilterForMethodInfo(creationParams, mi, true, false, filterParamType, returnType);
                }

                // Filter resolver
                if (inputParams.Length == 3)
                {
                    var operatorType = inputParams[1].ParameterType;
                    var filterValueType = inputParams[2].ParameterType;
                    if (operatorType != typeof(string))
                    {
                        Debug.LogWarning($"Parameter {inputParams[1].Name}'s type of filter method {mi.Name} must be {typeof(string)}.");
                        return null;
                    }

                    if (returnType != typeof(bool))
                    {
                        Debug.LogWarning($"Return type of filter method {mi.Name} must be {typeof(bool)}.");
                        return null;
                    }

                    return CreateFilterForMethodInfo(creationParams, mi, false, true, filterValueType);
                }

                // Filter function resolver
                if (inputParams.Length == 4)
                {
                    var filterParamType = inputParams[1].ParameterType;
                    var operatorType = inputParams[2].ParameterType;
                    var filterValueType = inputParams[3].ParameterType;
                    if (operatorType != typeof(string))
                    {
                        Debug.LogWarning($"Parameter {inputParams[1].Name}'s type of filter method {mi.Name} must be {typeof(string)}.");
                        return null;
                    }

                    if (returnType != typeof(bool))
                    {
                        Debug.LogWarning($"Return type of filter method {mi.Name} must be {typeof(bool)}.");
                        return null;
                    }

                    return CreateFilterForMethodInfo(creationParams, mi, true, true, filterParamType, filterValueType);
                }

                Debug.LogWarning($"Error while creating filter {filterToken}. Parameter count mismatch.");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error while creating filter {filterToken}. {ex.Message}");
                return null;
            }
        }

        private IFilter CreateFilterForMethodInfo(FilterCreationParams creationParams, MethodInfo mi, bool filterFunction, bool resolver, params Type[] methodTypes)
        {
            var methodName = $"CreateFilter{(filterFunction ? "Function" : "")}{(resolver ? "Resolver" : "")}ForMethodInfoTyped";
            var thisClassType = typeof(QueryEngineImpl<TData>);
            var method = thisClassType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            var typedMethod = method.MakeGenericMethod(methodTypes);
            return typedMethod.Invoke(this, new object[] { creationParams, mi }) as IFilter;
        }

        private IFilter CreateFilterForMethodInfoTyped<TFilter>(FilterCreationParams creationParams, MethodInfo mi)
        {
            var methodFunc = Delegate.CreateDelegate(typeof(Func<TData, TFilter>), mi) as Func<TData, TFilter>;
            if (creationParams.overridesGlobalComparisonOptions)
                return new Filter<TData, TFilter>(creationParams.token, creationParams.supportedOperators, methodFunc, creationParams.comparisonOptions);
            return new Filter<TData, TFilter>(creationParams.token, creationParams.supportedOperators, methodFunc);
        }

        private IFilter CreateFilterResolverForMethodInfoTyped<TFilter>(FilterCreationParams creationParams, MethodInfo mi)
        {
            var methodFunc = Delegate.CreateDelegate(typeof(Func<TData, string, TFilter, bool>), mi) as Func<TData, string, TFilter, bool>;
            return new Filter<TData, TFilter>(creationParams.token, creationParams.supportedOperators, methodFunc);
        }

        private IFilter CreateFilterFunctionForMethodInfoTyped<TParam, TFilter>(FilterCreationParams creationParams, MethodInfo mi)
        {
            var methodFunc = Delegate.CreateDelegate(typeof(Func<TData, TParam, TFilter>), mi) as Func<TData, TParam, TFilter>;

            if (creationParams.useParameterTransformer)
            {
                var parameterTransformerFunc = GetParameterTransformerFunction<TParam>(mi, creationParams.parameterTransformerFunction, creationParams.parameterTransformerAttributeType);
                if (creationParams.overridesGlobalComparisonOptions)
                    return new Filter<TData, TParam, TFilter>(creationParams.token, creationParams.supportedOperators, methodFunc, parameterTransformerFunc, creationParams.comparisonOptions);
                return new Filter<TData, TParam, TFilter>(creationParams.token, creationParams.supportedOperators, methodFunc, parameterTransformerFunc);
            }

            if (creationParams.overridesGlobalComparisonOptions)
                return new Filter<TData, TParam, TFilter>(creationParams.token, creationParams.supportedOperators, methodFunc, creationParams.comparisonOptions);
            return new Filter<TData, TParam, TFilter>(creationParams.token, creationParams.supportedOperators, methodFunc);
        }

        private IFilter CreateFilterFunctionResolverForMethodInfoTyped<TParam, TFilter>(FilterCreationParams creationParams, MethodInfo mi)
        {
            var methodFunc = Delegate.CreateDelegate(typeof(Func<TData, TParam, string, TFilter, bool>), mi) as Func<TData, TParam, string, TFilter, bool>;

            if (creationParams.useParameterTransformer)
            {
                var parameterTransformerFunc = GetParameterTransformerFunction<TParam>(mi, creationParams.parameterTransformerFunction, creationParams.parameterTransformerAttributeType);
                return new Filter<TData, TParam, TFilter>(creationParams.token, creationParams.supportedOperators, methodFunc, parameterTransformerFunc);
            }
            return new Filter<TData, TParam, TFilter>(creationParams.token, creationParams.supportedOperators, methodFunc);
        }

        private static Func<string, TParam> GetParameterTransformerFunction<TParam>(MethodInfo mi, string functionName, Type transformerAttributeType)
        {
            var transformerMethod = Utils.GetAllMethodsWithAttribute(transformerAttributeType)
                .Where(transformerMethodInfo =>
                {
                    var sameType = transformerMethodInfo.ReturnType == typeof(TParam);
                    var sameName = transformerMethodInfo.Name == functionName;
                    var paramInfos = transformerMethodInfo.GetParameters();
                    var sameParameter = paramInfos.Length == 1 && paramInfos[0].ParameterType == typeof(string);
                    return sameType && sameName && sameParameter;
                }).FirstOrDefault();
            if (transformerMethod == null)
            {
                Debug.LogWarning($"Filter function {mi.Name} uses a parameter transformer, but the function {functionName} was not found.");
                return null;
            }

            return Delegate.CreateDelegate(typeof(Func<string, TParam>), transformerMethod) as Func<string, TParam>;
        }
    }

    /// <summary>
    /// A QueryEngine defines how to build a query from an input string.
    /// It can be customized to support custom filters and operators.
    /// </summary>
    /// <typeparam name="TData">The filtered data type.</typeparam>
    public class QueryEngine<TData>
    {
        private QueryEngineImpl<TData> m_Impl;

        /// <summary>
        /// Get of set if the engine must validate filters when parsing the query. Defaults to true.
        /// </summary>
        public bool validateFilters
        {
            get => m_Impl.validationOptions.validateFilters;
            set
            {
                var options = m_Impl.validationOptions;
                options.validateFilters = value;
                m_Impl.validationOptions = options;
            }
        }

        public bool skipUnknownFilters
        {
            get => m_Impl.validationOptions.skipUnknownFilters;
            set
            {
                var options = m_Impl.validationOptions;
                options.skipUnknownFilters = value;
                m_Impl.validationOptions = options;
            }
        }

        /// <summary>
        /// Global string comparison options for word matching and filter handling (if not overridden).
        /// </summary>
        public StringComparison globalStringComparison => m_Impl.globalStringComparison;

        /// <summary>
        /// String comparison options for word/phrase matching.
        /// </summary>
        public StringComparison searchDataStringComparison => m_Impl.searchDataStringComparison;

        /// <summary>
        /// Indicates if word/phrase matching uses searchDataStringComparison or not.
        /// </summary>
        public bool searchDataOverridesStringComparison => m_Impl.searchDataOverridesGlobalStringComparison;

        /// <summary>
        /// The callback used to get the data to match to the search words.
        /// </summary>
        public Func<TData, IEnumerable<string>> searchDataCallback => m_Impl.searchDataCallback;

        /// <summary>
        /// Construct a new QueryEngine.
        /// </summary>
        public QueryEngine()
        {
            m_Impl = new QueryEngineImpl<TData>(new QueryValidationOptions{ validateFilters = true });
        }

        /// <summary>
        /// Construct a new QueryEngine.
        /// </summary>
        /// <param name="validateFilters">Indicates if the engine must validate filters when parsing the query.</param>
        public QueryEngine(bool validateFilters)
        {
            m_Impl = new QueryEngineImpl<TData>(new QueryValidationOptions { validateFilters = validateFilters });
        }

        /// <summary>
        /// Construct a new QueryEngine with the specified validation options.
        /// </summary>
        /// <param name="validationOptions">The validation options to use in this engine.</param>
        public QueryEngine(QueryValidationOptions validationOptions)
        {
            m_Impl = new QueryEngineImpl<TData>(validationOptions);
        }

        /// <summary>
        /// Add a new custom filter.
        /// </summary>
        /// <typeparam name="TFilter">The type of the data that is compared by the filter.</typeparam>
        /// <param name="token">The identifier of the filter. Typically what precedes the operator in a filter (i.e. "id" in "id>=2").</param>
        /// <param name="getDataFunc">Callback used to get the object that is used in the filter. Takes an object of type TData and returns an object of type TFilter.</param>
        /// <param name="supportedOperatorType">List of supported operator tokens. Null for all operators.</param>
        public void AddFilter<TFilter>(string token, Func<TData, TFilter> getDataFunc, string[] supportedOperatorType = null)
        {
            var filter = new Filter<TData, TFilter>(token, supportedOperatorType, getDataFunc);
            m_Impl.AddFilter(token, filter);
        }

        /// <summary>
        /// Add a new custom filter.
        /// </summary>
        /// <typeparam name="TFilter">The type of the data that is compared by the filter.</typeparam>
        /// <param name="token">The identifier of the filter. Typically what precedes the operator in a filter (i.e. "id" in "id>=2").</param>
        /// <param name="getDataFunc">Callback used to get the object that is used in the filter. Takes an object of type TData and returns an object of type TFilter.</param>
        /// <param name="stringComparison">String comparison options.</param>
        /// <param name="supportedOperatorType">List of supported operator tokens. Null for all operators.</param>
        public void AddFilter<TFilter>(string token, Func<TData, TFilter> getDataFunc, StringComparison stringComparison, string[] supportedOperatorType = null)
        {
            var filter = new Filter<TData, TFilter>(token, supportedOperatorType, getDataFunc, stringComparison);
            m_Impl.AddFilter(token, filter);
        }

        /// <summary>
        /// Add a new custom filter function.
        /// </summary>
        /// <typeparam name="TParam">The type of the constant parameter passed to the function.</typeparam>
        /// <typeparam name="TFilter">The type of the data that is compared by the filter.</typeparam>
        /// <param name="token">The identifier of the filter. Typically what precedes the operator in a filter (i.e. "id" in "id>=2").</param>
        /// <param name="getDataFunc">Callback used to get the object that is used in the filter. Takes an object of type TData and TParam, and returns an object of type TFilter.</param>
        /// <param name="supportedOperatorType">List of supported operator tokens. Null for all operators.</param>
        public void AddFilter<TParam, TFilter>(string token, Func<TData, TParam, TFilter> getDataFunc, string[] supportedOperatorType = null)
        {
            var filter = new Filter<TData, TParam, TFilter>(token, supportedOperatorType, getDataFunc);
            m_Impl.AddFilter(token, filter);
        }

        /// <summary>
        /// Add a new custom filter function.
        /// </summary>
        /// <typeparam name="TParam">The type of the constant parameter passed to the function.</typeparam>
        /// <typeparam name="TFilter">The type of the data that is compared by the filter.</typeparam>
        /// <param name="token">The identifier of the filter. Typically what precedes the operator in a filter (i.e. "id" in "id>=2").</param>
        /// <param name="getDataFunc">Callback used to get the object that is used in the filter. Takes an object of type TData and TParam, and returns an object of type TFilter.</param>
        /// <param name="stringComparison">String comparison options.</param>
        /// <param name="supportedOperatorType">List of supported operator tokens. Null for all operators.</param>
        public void AddFilter<TParam, TFilter>(string token, Func<TData, TParam, TFilter> getDataFunc, StringComparison stringComparison, string[] supportedOperatorType = null)
        {
            var filter = new Filter<TData, TParam, TFilter>(token, supportedOperatorType, getDataFunc, stringComparison);
            m_Impl.AddFilter(token, filter);
        }

        /// <summary>
        /// Add a new custom filter function.
        /// </summary>
        /// <typeparam name="TParam">The type of the constant parameter passed to the function.</typeparam>
        /// <typeparam name="TFilter">The type of the data that is compared by the filter.</typeparam>
        /// <param name="token">The identifier of the filter. Typically what precedes the operator in a filter (i.e. "id" in "id>=2").</param>
        /// <param name="getDataFunc">Callback used to get the object that is used in the filter. Takes an object of type TData and TParam, and returns an object of type TFilter.</param>
        /// <param name="parameterTransformer">Callback used to convert a string to the type TParam. Used when parsing the query to convert what is passed to the function into the correct format.</param>
        /// <param name="supportedOperatorType">List of supported operator tokens. Null for all operators.</param>
        public void AddFilter<TParam, TFilter>(string token, Func<TData, TParam, TFilter> getDataFunc, Func<string, TParam> parameterTransformer, string[] supportedOperatorType = null)
        {
            var filter = new Filter<TData, TParam, TFilter>(token, supportedOperatorType, getDataFunc, parameterTransformer);
            m_Impl.AddFilter(token, filter);
        }

        /// <summary>
        /// Add a new custom filter function.
        /// </summary>
        /// <typeparam name="TParam">The type of the constant parameter passed to the function.</typeparam>
        /// <typeparam name="TFilter">The type of the data that is compared by the filter.</typeparam>
        /// <param name="token">The identifier of the filter. Typically what precedes the operator in a filter (i.e. "id" in "id>=2").</param>
        /// <param name="getDataFunc">Callback used to get the object that is used in the filter. Takes an object of type TData and TParam, and returns an object of type TFilter.</param>
        /// <param name="parameterTransformer">Callback used to convert a string to the type TParam. Used when parsing the query to convert what is passed to the function into the correct format.</param>
        /// <param name="stringComparison">String comparison options.</param>
        /// <param name="supportedOperatorType">List of supported operator tokens. Null for all operators.</param>
        public void AddFilter<TParam, TFilter>(string token, Func<TData, TParam, TFilter> getDataFunc, Func<string, TParam> parameterTransformer, StringComparison stringComparison, string[] supportedOperatorType = null)
        {
            var filter = new Filter<TData, TParam, TFilter>(token, supportedOperatorType, getDataFunc, parameterTransformer, stringComparison);
            m_Impl.AddFilter(token, filter);
        }

        /// <summary>
        /// Add a new custom filter with a custom resolver. Useful when you wish to handle all operators yourself.
        /// </summary>
        /// <typeparam name="TFilter">The type of the data that is compared by the filter.</typeparam>
        /// <param name="token">The identifier of the filter. Typically what precedes the operator in a filter (i.e. "id" in "id>=2").</param>
        /// <param name="filterResolver">Callback used to handle any operators for this filter. Takes an object of type TData, the operator token and the filter value, and returns a boolean indicating if the filter passed or not.</param>
        /// <param name="supportedOperatorType">List of supported operator tokens. Null for all operators.</param>
        public void AddFilter<TFilter>(string token, Func<TData, string, TFilter, bool> filterResolver, string[] supportedOperatorType = null)
        {
            var filter = new Filter<TData, TFilter>(token, supportedOperatorType, filterResolver);
            m_Impl.AddFilter(token, filter);
        }

        /// <summary>
        /// Add a new custom filter function with a custom resolver. Useful when you wish to handle all operators yourself.
        /// </summary>
        /// <typeparam name="TParam">The type of the constant parameter passed to the function.</typeparam>
        /// <typeparam name="TFilter">The type of the data that is compared by the filter.</typeparam>
        /// <param name="token">The identifier of the filter. Typically what precedes the operator in a filter (i.e. "id" in "id>=2").</param>
        /// <param name="filterResolver">Callback used to handle any operators for this filter. Takes an object of type TData, an object of type TParam, the operator token and the filter value, and returns a boolean indicating if the filter passed or not.</param>
        /// <param name="supportedOperatorType">List of supported operator tokens. Null for all operators.</param>
        public void AddFilter<TParam, TFilter>(string token, Func<TData, TParam, string, TFilter, bool> filterResolver, string[] supportedOperatorType = null)
        {
            var filter = new Filter<TData, TParam, TFilter>(token, supportedOperatorType, filterResolver);
            m_Impl.AddFilter(token, filter);
        }

        /// <summary>
        /// Add a new custom filter function with a custom resolver. Useful when you wish to handle all operators yourself.
        /// </summary>
        /// <typeparam name="TParam">The type of the constant parameter passed to the function.</typeparam>
        /// <typeparam name="TFilter">The type of the data that is compared by the filter.</typeparam>
        /// <param name="token">The identifier of the filter. Typically what precedes the operator in a filter (i.e. "id" in "id>=2").</param>
        /// <param name="filterResolver">Callback used to handle any operators for this filter. Takes an object of type TData, an object of type TParam, the operator token and the filter value, and returns a boolean indicating if the filter passed or not.</param>
        /// <param name="parameterTransformer">Callback used to convert a string to the type TParam. Used when parsing the query to convert what is passed to the function into the correct format.</param>
        /// <param name="supportedOperatorType">List of supported operator tokens. Null for all operators.</param>
        public void AddFilter<TParam, TFilter>(string token, Func<TData, TParam, string, TFilter, bool> filterResolver, Func<string, TParam> parameterTransformer, string[] supportedOperatorType = null)
        {
            var filter = new Filter<TData, TParam, TFilter>(token, supportedOperatorType, filterResolver, parameterTransformer);
            m_Impl.AddFilter(token, filter);
        }

        /// <summary>
        /// Add all custom filters that are identified with the method attribute TAttribute.
        /// </summary>
        /// <typeparam name="TFilterAttribute">The type of the attribute of filters to fetch.</typeparam>
        /// <typeparam name="TTransformerAttribute">The attribute type for the parameter transformers associated with the filter attribute.</typeparam>
        public void AddFiltersFromAttribute<TFilterAttribute, TTransformerAttribute>()
            where TFilterAttribute : QueryEngineFilterAttribute
            where TTransformerAttribute : QueryEngineParameterTransformerAttribute
        {
            m_Impl.AddFiltersFromAttribute<TFilterAttribute, TTransformerAttribute>();
        }

        /// <summary>
        /// Remove a custom filter.
        /// </summary>
        /// <param name="token">The identifier of the filter. Typically what precedes the operator in a filter (i.e. "id" in "id>=2").</param>
        /// <remarks>You will get a warning if you try to remove a non existing filter.</remarks>
        public void RemoveFilter(string token)
        {
            m_Impl.RemoveFilter(token);
        }

        /// <summary>
        /// Add a custom filter operator.
        /// </summary>
        /// <param name="op">The operator identifier.</param>
        public void AddOperator(string op)
        {
            m_Impl.AddOperator(op);
        }

        internal FilterOperator GetOperator(string op)
        {
            return m_Impl.GetOperator(op);
        }

        /// <summary>
        /// Add a custom filter operator handler.
        /// </summary>
        /// <typeparam name="TFilterVariable">The operator's left hand side type. This is the type returned by a filter handler.</typeparam>
        /// <typeparam name="TFilterConstant">The operator's right hand side type.</typeparam>
        /// <param name="op">The filter operator.</param>
        /// <param name="handler">Callback to handle the operation. Takes a TFilterVariable (value returned by the filter handler, will vary for each element) and a TFilterConstant (right hand side value of the operator, which is constant), and returns a boolean indicating if the filter passes or not.</param>
        public void AddOperatorHandler<TFilterVariable, TFilterConstant>(string op, Func<TFilterVariable, TFilterConstant, bool> handler)
        {
            m_Impl.AddOperatorHandler(op, handler);
        }

        /// <summary>
        /// Add a custom filter operator handler.
        /// </summary>
        /// <typeparam name="TFilterVariable">The operator's left hand side type. This is the type returned by a filter handler.</typeparam>
        /// <typeparam name="TFilterConstant">The operator's right hand side type.</typeparam>
        /// <param name="op">The filter operator.</param>
        /// <param name="handler">Callback to handle the operation. Takes a TFilterVariable (value returned by the filter handler, will vary for each element), a TFilterConstant (right hand side value of the operator, which is constant), a StringComparison option and returns a boolean indicating if the filter passes or not.</param>
        public void AddOperatorHandler<TFilterVariable, TFilterConstant>(string op, Func<TFilterVariable, TFilterConstant, StringComparison, bool> handler)
        {
            m_Impl.AddOperatorHandler(op, handler);
        }

        /// <summary>
        /// Add a type parser that parse a string and returns a custom type. Used
        /// by custom operator handlers.
        /// </summary>
        /// <typeparam name="TFilterConstant">The type of the parsed operand that is on the right hand side of the operator.</typeparam>
        /// <param name="parser">Callback used to determine if a string can be converted into TFilterConstant. Takes a string and returns a ParseResult object. This contains the success flag, and the actual converted value if it succeeded.</param>
        public void AddTypeParser<TFilterConstant>(Func<string, ParseResult<TFilterConstant>> parser)
        {
            m_Impl.AddTypeParser(parser);
        }

        /// <summary>
        /// Set the default filter handler for filters that were not registered.
        /// </summary>
        /// <param name="handler">Callback used to handle the filter. Takes an object of type TData, the filter identifier, the operator and the filter value, and returns a boolean indicating if the filter passed or not.</param>
        public void SetDefaultFilter(Func<TData, string, string, string, bool> handler)
        {
            m_Impl.SetDefaultFilter(handler);
        }

        /// <summary>
        /// Set the default filter handler for function filters that were not registered.
        /// </summary>
        /// <param name="handler">Callback used to handle the function filter. Takes an object of type TData, the filter identifier, the parameter, the operator and the filter value, and returns a boolean indicating if the filter passed or not.</param>
        public void SetDefaultParamFilter(Func<TData, string, string, string, string, bool> handler)
        {
            m_Impl.SetDefaultParamFilter(handler);
        }

        /// <summary>
        /// Set the callback to be used to fetch the data that will be matched against the search words.
        /// </summary>
        /// <param name="getSearchDataCallback">Callback used to get the data to be matched against the search words. Takes an object of type TData and return an IEnumerable of strings.</param>
        public void SetSearchDataCallback(Func<TData, IEnumerable<string>> getSearchDataCallback)
        {
            m_Impl.SetSearchDataCallback(getSearchDataCallback);
        }

        /// <summary>
        /// Set the callback to be used to fetch the data that will be matched against the search words.
        /// </summary>
        /// <param name="getSearchDataCallback">Callback used to get the data to be matched against the search words. Takes an object of type TData and return an IEnumerable of strings.</param>
        /// <param name="stringComparison">String comparison options.</param>
        public void SetSearchDataCallback(Func<TData, IEnumerable<string>> getSearchDataCallback, StringComparison stringComparison)
        {
            m_Impl.SetSearchDataCallback(getSearchDataCallback, stringComparison);
        }

        /// <summary>
        /// Set global string comparison options. Used for word matching and filter handling (unless overridden by filter).
        /// </summary>
        /// <param name="stringComparison">String comparison options.</param>
        public void SetGlobalStringComparisonOptions(StringComparison stringComparison)
        {
            m_Impl.globalStringComparison = stringComparison;
        }

        /// <summary>
        /// Parse a query string into a Query operation. This Query operation can then be used to filter any data set of type TData.
        /// </summary>
        /// <param name="text">The query input string.</param>
        /// <returns>Query operation of type TData.</returns>
        public Query<TData> Parse(string text)
        {
            var errors = new List<QueryError>();
            var graph = m_Impl.BuildGraph(text, errors);
            return new Query<TData>(graph, errors, this);
        }

        internal Query<TData, TPayload> Parse<TQueryHandler, TPayload>(string text, IQueryHandlerFactory<TData, TQueryHandler, TPayload> queryHandlerFactory)
        where TQueryHandler : IQueryHandler<TData, TPayload>
        where TPayload : class
        {
            var errors = new List<QueryError>();
            var graph = m_Impl.BuildGraph(text, errors);
            return new Query<TData, TPayload>(graph, errors, queryHandlerFactory.Create(graph));
        }
    }

    /// <summary>
    /// A QueryEngine defines how to build a query from an input string.
    /// It can be customized to support custom filters and operators.
    /// Default query engine of type object.
    /// </summary>
    public class QueryEngine : QueryEngine<object>
    {
        /// <summary>
        /// Construct a new QueryEngine.
        /// </summary>
        public QueryEngine()
        { }

        /// <summary>
        /// Construct a new QueryEngine.
        /// </summary>
        /// <param name="validateFilters">Indicates if the engine must validate filters when parsing the query.</param>
        public QueryEngine(bool validateFilters)
            : base(validateFilters)
        { }

        /// <summary>
        /// Construct a new QueryEngine with the specified validation options.
        /// </summary>
        /// <param name="validationOptions">The validation options to use in this engine.</param>
        public QueryEngine(QueryValidationOptions validationOptions)
            : base(validationOptions)
        { }
    }
}

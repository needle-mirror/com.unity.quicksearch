using System;

namespace UnityEditor.Search
{
    public partial class QueryEngine<TData>
    {
#if USE_SEARCH_MODULE
        [Obsolete("Parse has been deprecated. Use ParseQuery instead (UnityUpgradable) -> ParseQuery(*)")]
#else
        [Obsolete("Parse has been deprecated. Use ParseQuery instead.")]
#endif
        public Query<TData> Parse(string text, bool useFastYieldingQueryHandler = false)
        {
            var handlerFactory = new DefaultQueryHandlerFactory<TData>(this, useFastYieldingQueryHandler);
            var pq = BuildQuery(text, handlerFactory, (evalGraph, queryGraph, errors, tokens, toggles, handler) => new ParsedQuery<TData>(text, evalGraph, queryGraph, errors, tokens, toggles, handler));
            return new Query<TData>(pq.text, pq.evaluationGraph, pq.queryGraph, pq.errors, pq.tokens, pq.toggles, pq.graphHandler);
        }

#if USE_SEARCH_MODULE
        [Obsolete("Parse has been deprecated. Use ParseQuery instead (UnityUpgradable) -> ParseQuery<TQueryHandler, TPayload>(*)")]
#else
        [Obsolete("Parse has been deprecated. Use ParseQuery instead.")]
#endif
        public Query<TData, TPayload> Parse<TQueryHandler, TPayload>(string text, IQueryHandlerFactory<TData, TQueryHandler, TPayload> queryHandlerFactory)
            where TQueryHandler : IQueryHandler<TData, TPayload>
            where TPayload : class
        {
            var pq = BuildQuery(text, queryHandlerFactory, (evalGraph, queryGraph, errors, tokens, toggles, handler) => new ParsedQuery<TData, TPayload>(text, evalGraph, queryGraph, errors, tokens, toggles, handler));
            return new Query<TData, TPayload>(pq.text, pq.evaluationGraph, pq.queryGraph, pq.errors, pq.tokens, pq.toggles, pq.graphHandler);
        }
    }
}

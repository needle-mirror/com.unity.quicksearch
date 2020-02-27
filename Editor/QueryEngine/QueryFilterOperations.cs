using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;

namespace Unity.QuickSearch
{
    internal interface IFilterOperation
    {
        string filterName { get; }
        string filterValue { get; }
        string filterParams { get; }
        IFilter filter { get; }
        FilterOperator filterOperator { get; }
        string ToString();
    }

    internal struct FilterOperationGeneratorData
    {
        public FilterOperator op;
        public string filterValue;
        public string paramValue;
        public IParseResult filterValueParseResult;
        public StringComparison globalStringComparison;
        public IFilterOperationGenerator generator;
    }

    internal interface IFilterOperationGenerator
    {
        IFilterOperation GenerateOperation<TData, TFilterLhs>(FilterOperationGeneratorData data, Filter<TData, TFilterLhs> filter, int operatorIndex, List<QueryError> errors);
        IFilterOperation GenerateOperation<TData, TParam, TFilterLhs>(FilterOperationGeneratorData data, Filter<TData, TParam, TFilterLhs> filter, int operatorIndex, List<QueryError> errors);
    }

    internal class FilterOperationGenerator<TFilterRhs> : IFilterOperationGenerator
    {
        public IFilterOperation GenerateOperation<TData, TFilterLhs>(FilterOperationGeneratorData data, Filter<TData, TFilterLhs> filter, int operatorIndex, List<QueryError> errors)
        {
            var filterValue = ((ParseResult<TFilterRhs>)data.filterValueParseResult).parsedValue;
            var stringComparisonOptions = filter.overrideStringComparison ? filter.stringComparison : data.globalStringComparison;
            Func<TData, bool> operation = o => false;

            var handlerFound = false;
            var typedHandler = data.op.GetHandler<TFilterLhs, TFilterRhs>();
            if (typedHandler != null)
            {
                operation = o => typedHandler(filter.GetData(o), filterValue, stringComparisonOptions);
                handlerFound = true;
            }

            if (!handlerFound)
            {
                var genericHandler = data.op.GetHandler<object, object>();
                if (genericHandler != null)
                {
                    operation = o => genericHandler(filter.GetData(o), filterValue, stringComparisonOptions);
                    handlerFound = true;
                }
            }

            if (!handlerFound)
            {
                var error = $"No handler of type ({typeof(TFilterLhs)}, {typeof(TFilterRhs)}) or (object, object) found for operator {data.op.token}";
                errors.Add(new QueryError(operatorIndex, data.op.token.Length, error));
            }

            return new FilterOperation<TData, TFilterLhs>(filter, data.op, data.filterValue, operation);
        }

        public IFilterOperation GenerateOperation<TData, TParam, TFilterLhs>(FilterOperationGeneratorData data, Filter<TData, TParam, TFilterLhs> filter, int operatorIndex, List<QueryError> errors)
        {
            Func<TData, TParam, bool> operation = (o, p) => false;
            var filterValue = ((ParseResult<TFilterRhs>)data.filterValueParseResult).parsedValue;
            var stringComparisonOptions = filter.overrideStringComparison ? filter.stringComparison : data.globalStringComparison;

            var handlerFound = false;
            var typedHandler = data.op.GetHandler<TFilterLhs, TFilterRhs>();
            if (typedHandler != null)
            {
                operation = (o, p) => typedHandler(filter.GetData(o, p), filterValue, stringComparisonOptions);
                handlerFound = true;
            }

            if (!handlerFound)
            {
                var genericHandler = data.op.GetHandler<object, object>();
                if (genericHandler != null)
                {
                    operation = (o, p) => genericHandler(filter.GetData(o, p), filterValue, stringComparisonOptions);
                    handlerFound = true;
                }
            }

            if (!handlerFound)
            {
                var error = $"No handler of type ({typeof(TFilterLhs)}, {typeof(TFilterRhs)}) or (object, object) found for operator {data.op.token}";
                errors.Add(new QueryError(operatorIndex, data.op.token.Length, error));
            }

            return new FilterOperation<TData, TParam, TFilterLhs>(filter, data.op, data.filterValue, data.paramValue, operation);
        }
    }

    internal abstract class BaseFilterOperation<TData> : IFilterOperation
    {
        public string filterName => filter.token;
        public string filterValue { get; }
        public virtual string filterParams => null;
        public IFilter filter { get; }
        public FilterOperator filterOperator { get; }

        protected BaseFilterOperation(IFilter filter, FilterOperator filterOperator, string filterValue)
        {
            this.filter = filter;
            this.filterOperator = filterOperator;
            this.filterValue = filterValue;
        }

        public abstract bool Match(TData obj);

        public new virtual string ToString()
        {
            return $"{filterName}{filterOperator.token}{filterValue}";
        }
    }

    internal class FilterOperation<TData, TFilterLhs> : BaseFilterOperation<TData>
    {
        public Func<TData, bool> operation { get; }

        public FilterOperation(Filter<TData, TFilterLhs> filter, FilterOperator filterOperator, string filterValue, Func<TData, bool> operation)
            : base(filter, filterOperator, filterValue)
        {
            this.operation = operation;
        }

        public override bool Match(TData obj)
        {
            try
            {
                return operation(obj);
            }
            catch (Exception ex)
            {
                throw new Exception($"{ex.Message}: Failed to execute {filterOperator} handler with {obj} and {filterValue}\r\n{ex.StackTrace}");
            }
        }
    }

    internal class FilterOperation<TData, TParam, TFilterLhs> : BaseFilterOperation<TData>
    {
        private string m_ParamValue;

        public Func<TData, TParam, bool> operation { get; }
        public TParam param { get; }

        public override string filterParams => m_ParamValue;

        public FilterOperation(Filter<TData, TParam, TFilterLhs> filter, FilterOperator filterOperator, string filterValue, Func<TData, TParam, bool> operation)
            : base(filter, filterOperator, filterValue)
        {
            this.operation = operation;
            m_ParamValue = null;
            param = default;
        }

        public FilterOperation(Filter<TData, TParam, TFilterLhs> filter, FilterOperator filterOperator, string filterValue, string paramValue, Func<TData, TParam, bool> operation)
            : base(filter, filterOperator, filterValue)
        {
            this.operation = operation;
            m_ParamValue = paramValue;
            param = string.IsNullOrEmpty(paramValue) ? default : filter.TransformParameter(paramValue);
        }

        public override bool Match(TData obj)
        {
            try
            {
                return operation(obj, param);
            }
            catch (Exception ex)
            {
                throw new Exception($"{ex.Message}: Failed to execute {filterOperator} handler with {obj} and {filterValue}\r\n{ex.StackTrace}");
            }
        }

        public override string ToString()
        {
            var paramString = string.IsNullOrEmpty(m_ParamValue) ? "" : $"({m_ParamValue})";
            return $"{filterName}{paramString}{filterOperator.token}{filterValue}";
        }
    }
}
